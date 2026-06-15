using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace BugyardSDK
{
    /// <summary>
    /// Uploads a report to <c>POST {endpoint}/v1/reports</c> as multipart/form-data,
    /// with bounded retries on transient failures. The <c>clientReportId</c> is stable
    /// across retries so the backend deduplicates idempotently.
    ///
    /// When an upload still fails after its in-process retries and the failure is transient
    /// (offline or a 5xx), the report is persisted to the <see cref="OfflineReportQueue"/> and
    /// retried on a later launch via <see cref="FlushQueue"/> — the same stable
    /// <c>clientReportId</c> keeps that cross-session retry idempotent.
    /// </summary>
    public class BugyardClient
    {
        const int MaxAttempts = 3;

        // Upper bound on how long we'll honor a 429 Retry-After before a backoff wait. A retry
        // happens inline while the player's overlay is open, so a hostile or buggy header value
        // can't be allowed to stall the session for minutes.
        const float MaxRetryAfterSeconds = 120f;

        readonly BugyardConfig _config;

        // Guards against two flush passes running at once (startup flush + post-success flush).
        bool _flushing;

        public BugyardClient(BugyardConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Upload a report carrying only a screenshot and logs. Convenience overload that wraps the
        /// inputs in a <see cref="ReportArtifacts"/> and delegates to
        /// <see cref="Send(ReportMetadata, ReportArtifacts, Action{SendResult})"/>.
        /// </summary>
        public IEnumerator Send(
            ReportMetadata metadata, byte[] screenshot, string logs, Action<SendResult> onComplete = null)
        {
            return Send(metadata, new ReportArtifacts { screenshot = screenshot, logs = logs }, onComplete);
        }

        /// <summary>
        /// Upload the report with all of its artifacts, retrying transient failures.
        /// <paramref name="onComplete"/> is invoked exactly once with the typed outcome (success +
        /// reportId/dashboardUrl, or failure + a friendly reason) once the upload finishes or is
        /// given up on. A transient failure is persisted for a later launch and reported with
        /// <see cref="SendResult.queuedForRetry"/>.
        /// </summary>
        public IEnumerator Send(
            ReportMetadata metadata, ReportArtifacts artifacts, Action<SendResult> onComplete = null)
        {
            artifacts = artifacts ?? new ReportArtifacts();

            // Enforce the config size caps before upload so we don't ship a payload the backend
            // rejects with PAYLOAD_TOO_LARGE: oversized screenshots are downscaled (or dropped),
            // logs are trimmed to their most recent lines, metadata free-text is truncated, and the
            // binary attachments (events / save state / memory dump) are dropped when over their cap.
            artifacts.screenshot = PayloadLimits.ClampScreenshot(artifacts.screenshot, _config.maxScreenshotBytes);
            artifacts.logs = PayloadLimits.ClampLogs(artifacts.logs, _config.maxLogBytes);
            artifacts.events = PayloadLimits.ClampAttachment(artifacts.events, _config.maxEventsBytes, "Gameplay events");
            artifacts.saveState = PayloadLimits.ClampAttachment(artifacts.saveState, _config.maxSaveStateBytes, "Save state");
            artifacts.memoryDump = PayloadLimits.ClampAttachment(artifacts.memoryDump, _config.maxMemoryDumpBytes, "Memory dump");
            string json = PayloadLimits.ClampMetadata(metadata, _config.maxMetadataBytes);

            SendResult result = null;
            yield return SendWire(json, artifacts, r => result = r);

            // A transient failure (offline / server error) is saved so a later online launch can
            // deliver it. Permanent failures (auth, validation, too-large) and rate limiting are
            // not queued — replaying them wouldn't help.
            if (result != null && !result.success && _config.enableOfflineQueue && ShouldPersist(result))
            {
                // Only the lightweight artifacts (screenshot + logs) and the metadata — which carries
                // the free-form context inline — are persisted for cross-session retry. The heavy
                // diagnostic blobs (events / save state / memory dump, up to 100 MB) are intentionally
                // not written to disk, so an offline replay is delivered without them rather than
                // bloating the queue. Context survives because it travels inside the metadata JSON.
                if (OfflineReportQueue.Enqueue(_config, json, artifacts.screenshot, artifacts.logs, metadata.clientReportId))
                {
                    result.queuedForRetry = true;
                    result.message =
                        "Couldn't reach the server right now — your report was saved and will be sent " +
                        "automatically next time you're online.";
                }
            }

            onComplete?.Invoke(result);
        }

        /// <summary>
        /// Replay every report persisted from an earlier session, oldest first. Delivered reports
        /// (including ones the backend reports as already received) are removed from disk; a report
        /// that fails transiently is left in place and the pass stops, so a still-offline launch
        /// doesn't churn the whole queue. Safe to call repeatedly — overlapping calls are ignored.
        /// Drive it from a MonoBehaviour via <c>StartCoroutine(client.FlushQueue())</c>.
        /// </summary>
        public IEnumerator FlushQueue()
        {
            if (!_config.enableOfflineQueue || _flushing) yield break;

            _flushing = true;
            try
            {
                List<OfflineReportQueue.Entry> entries = OfflineReportQueue.LoadAll();
                if (entries.Count == 0) yield break;

                Debug.Log($"[Bugyard] Retrying {entries.Count} queued report(s) from a previous session.");

                foreach (OfflineReportQueue.Entry entry in entries)
                {
                    // Queued replays carry only the persisted screenshot + logs; the heavy blobs
                    // were never written to disk (see Send), so they're absent on replay by design.
                    var artifacts = new ReportArtifacts { screenshot = entry.Screenshot, logs = entry.Logs };
                    SendResult result = null;
                    yield return SendWire(entry.MetadataJson, artifacts, r => result = r);

                    if (result != null && result.success)
                    {
                        OfflineReportQueue.Remove(entry.Path);
                        Debug.Log($"[Bugyard] Delivered queued report {entry.ClientReportId} ({result.status}).");
                    }
                    else if (result != null && ShouldPersist(result))
                    {
                        // Still transient (e.g. offline). Leave this and the rest on disk and stop;
                        // the next launch will pick up where we left off.
                        Debug.Log("[Bugyard] Still unable to reach the server; leaving reports queued for next launch.");
                        break;
                    }
                    else
                    {
                        // Permanent failure — drop it so a poison report can't wedge the queue forever.
                        OfflineReportQueue.Remove(entry.Path);
                        Debug.LogWarning(
                            $"[Bugyard] Dropping queued report {entry.ClientReportId}; it was rejected and won't be retried.");
                    }
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        // Persist only failures a later launch could plausibly clear: a transport failure (offline,
        // httpStatus 0) or a server error (5xx). 4xx (auth/validation/too-large) is permanent, and
        // 429 rate limiting is handled by the in-process backoff — neither is worth replaying.
        static bool ShouldPersist(SendResult result) => result.httpStatus == 0 || result.httpStatus >= 500;

        // Perform the multipart upload of already-clamped wire artifacts, retrying transient
        // failures with backoff. Used for both fresh sends and queued replays. Backoff is
        // exponential by default; on a 429 carrying a Retry-After header we wait the
        // server-specified interval instead.
        IEnumerator SendWire(string json, ReportArtifacts art, Action<SendResult> onComplete)
        {
            string url = _config.endpoint.TrimEnd('/') + "/v1/reports";

            SendResult result = null;

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                // Default backoff: 1s, 2s, ... A 429 Retry-After overrides this below.
                float backoffSeconds = Mathf.Pow(2f, attempt - 1);
                var form = new List<IMultipartFormSection>
                {
                    new MultipartFormDataSection("metadata", json),
                };
                if (art.screenshot != null && art.screenshot.Length > 0)
                {
                    form.Add(new MultipartFormFileSection("screenshot", art.screenshot, "screenshot.png", "image/png"));
                }
                if (!string.IsNullOrEmpty(art.logs))
                {
                    form.Add(new MultipartFormFileSection("logs", Encoding.UTF8.GetBytes(art.logs), "player.log", "text/plain"));
                }
                if (art.events != null && art.events.Length > 0)
                {
                    form.Add(new MultipartFormFileSection("events", art.events, "events.json", "application/json"));
                }
                if (art.saveState != null && art.saveState.Length > 0)
                {
                    form.Add(art.saveStateIsJson
                        ? new MultipartFormFileSection("save_state", art.saveState, "save_state.json", "application/json")
                        : new MultipartFormFileSection("save_state", art.saveState, "save_state.bin", "application/octet-stream"));
                }
                if (art.memoryDump != null && art.memoryDump.Length > 0)
                {
                    form.Add(new MultipartFormFileSection("memory_dump", art.memoryDump, "memory_dump.gz", "application/gzip"));
                }

                using (var req = UnityWebRequest.Post(url, form))
                {
                    req.SetRequestHeader("Authorization", "Bearer " + _config.apiKey);
                    yield return req.SendWebRequest();

                    long code = req.responseCode;
                    string body = req.downloadHandler != null ? req.downloadHandler.text : "";

                    if (code >= 200 && code < 300)
                    {
                        result = SendResult.Successful(code, body);
                        Debug.Log($"[Bugyard] Report sent ({code}). {body}");
                        break;
                    }

                    bool retryable = code == 0 || code == 429 || code >= 500;
                    Debug.LogWarning(
                        $"[Bugyard] Send failed (attempt {attempt}/{MaxAttempts}, code {code}): {req.error} {body}");

                    if (!retryable || attempt == MaxAttempts)
                    {
                        result = SendResult.Failed(code, body, req.error);
                        Debug.LogError("[Bugyard] Giving up on report after " + attempt + " attempt(s).");
                        break;
                    }

                    // Honor a server-specified back-pressure interval on 429 rather than guessing.
                    if (code == 429)
                    {
                        float retryAfter = ParseRetryAfter(req.GetResponseHeader("Retry-After"));
                        if (retryAfter > 0f)
                        {
                            backoffSeconds = retryAfter;
                            Debug.Log($"[Bugyard] Server asked to retry after {backoffSeconds:0.##}s (Retry-After).");
                        }
                    }
                }

                yield return new WaitForSeconds(backoffSeconds);
            }

            onComplete?.Invoke(result);
        }

        // Parse a Retry-After header (RFC 7231 §7.1.3) into a wait in seconds. Accepts both forms:
        // delta-seconds ("120") and an HTTP-date ("Wed, 21 Oct 2015 07:28:00 GMT"), for which we
        // wait until that instant. Returns 0 when the header is absent, malformed, or already in
        // the past — the caller then falls back to exponential backoff. The result is clamped to
        // <see cref="MaxRetryAfterSeconds"/> so a hostile value can't stall the session.
        static float ParseRetryAfter(string headerValue)
        {
            if (string.IsNullOrWhiteSpace(headerValue)) return 0f;
            headerValue = headerValue.Trim();

            // delta-seconds: a non-negative integer.
            if (int.TryParse(headerValue, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds))
                return Mathf.Min(seconds, MaxRetryAfterSeconds);

            // HTTP-date: wait until the given instant.
            if (DateTimeOffset.TryParse(
                    headerValue, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset when))
            {
                double delta = (when - DateTimeOffset.UtcNow).TotalSeconds;
                return delta > 0d ? Mathf.Min((float)delta, MaxRetryAfterSeconds) : 0f;
            }

            return 0f;
        }
    }
}
