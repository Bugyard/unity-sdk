#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace BugCaptureSDK.Editor
{
    /// <summary>
    /// "Send Test Report" editor action (U21): uploads a synthetic report with the current
    /// config to verify connectivity and auth end-to-end, then reports success (with a dashboard
    /// link) or a precise failure reason.
    ///
    /// This deliberately does not reuse <see cref="BugCaptureClient.Send"/>: that path is a
    /// coroutine that waits with <c>WaitForSeconds</c> and persists transient failures to the
    /// offline queue — neither of which belongs in an edit-mode connectivity check. Instead it
    /// builds the exact same wire payload (same metadata schema, same <c>/v1/reports</c> endpoint,
    /// same <c>Authorization</c> header) and does a single attempt driven by
    /// <see cref="EditorApplication.update"/>, mapping the outcome through the shared
    /// <see cref="SendResult"/> / <see cref="BackendErrors"/> so the reason matches what a player
    /// would see at runtime.
    /// </summary>
    static class BugCaptureTestReport
    {
        // Guards against a second invocation while a request is already in flight.
        static bool _inFlight;

        [MenuItem("Tools/BugCapture/Send Test Report")]
        static void SendTestReport()
        {
            if (_inFlight)
            {
                Debug.LogWarning("[BugCapture] A test report is already being sent; please wait for it to finish.");
                return;
            }

            BugCaptureConfig config = ResolveConfig();
            if (config == null)
            {
                EditorUtility.DisplayDialog(
                    "BugCapture — Send Test Report",
                    "No BugCapture config asset was found.\n\n" +
                    "Create one via Tools ‣ BugCapture ‣ Create Config Asset, then set your API key and endpoint.",
                    "OK");
                return;
            }

            // Catch the failures we can name precisely without a network round trip: an unusable
            // endpoint (a malformed URL would just throw inside UnityWebRequest). The empty-key case
            // is intentionally allowed through so the test exercises auth and surfaces the real 401.
            if (!IsUsableEndpoint(config.endpoint, out string endpointReason))
            {
                EditorUtility.DisplayDialog(
                    "BugCapture — Send Test Report",
                    $"Can't send a test report: the endpoint \"{config.endpoint}\" is {endpointReason}.\n\n" +
                    "Set it to your BugCapture backend base URL (e.g. https://api.bugcapture.dev, no trailing /v1).",
                    "OK");
                return;
            }

            if (string.IsNullOrEmpty(config.apiKey) && !EditorUtility.DisplayDialog(
                    "BugCapture — Send Test Report",
                    "The API key is empty, so the server will reject this report with 401 Unauthorized.\n\n" +
                    "Send it anyway to confirm the endpoint is reachable?",
                    "Send anyway", "Cancel"))
            {
                return;
            }

            BeginSend(config);
        }

        // Prefer a config the user is actively inspecting, so testing the asset you have selected
        // tests that asset; otherwise fall back to the single discovered config.
        static BugCaptureConfig ResolveConfig()
        {
            if (Selection.activeObject is BugCaptureConfig selected)
                return selected;
            return BugCaptureMenu.FindExistingConfig();
        }

        static void BeginSend(BugCaptureConfig config)
        {
            string url = config.endpoint.TrimEnd('/') + "/v1/reports";

            ReportMetadata metadata = MetadataCollector.Build(config, BuildTestInput(config));
            string json = MetadataJson.Serialize(metadata);

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormDataSection("metadata", json),
            };

            // Post returns a request we own; the runner disposes it when the operation completes.
            UnityWebRequest req = UnityWebRequest.Post(url, form);
            req.SetRequestHeader("Authorization", "Bearer " + config.apiKey);

            _inFlight = true;
            Debug.Log($"[BugCapture] Sending a test report to {url} (clientReportId={metadata.clientReportId}).");

            UnityWebRequestAsyncOperation op = req.SendWebRequest();
            EditorApplication.update += new Poller(req, op).Tick;
        }

        static ReportInput BuildTestInput(BugCaptureConfig config)
        {
            return new ReportInput
            {
                title = "BugCapture connectivity test",
                description =
                    "Synthetic report sent from the Unity Editor via Tools ‣ BugCapture ‣ Send Test Report " +
                    "to verify connectivity and authentication. Safe to ignore or delete.",
                severity = Severity.Low,
                category = config.defaultCategory,
            };
        }

        // Pumps a single in-flight request from the editor update loop (edit mode has no coroutine
        // runner). Unregisters itself, clears the progress bar, disposes the request, and reports
        // the outcome once the operation is done.
        sealed class Poller
        {
            readonly UnityWebRequest _req;
            readonly UnityWebRequestAsyncOperation _op;

            public Poller(UnityWebRequest req, UnityWebRequestAsyncOperation op)
            {
                _req = req;
                _op = op;
            }

            public void Tick()
            {
                if (!_op.isDone)
                {
                    EditorUtility.DisplayProgressBar(
                        "BugCapture", "Sending a test report…", Mathf.Clamp01(_op.progress));
                    return;
                }

                EditorApplication.update -= Tick;
                EditorUtility.ClearProgressBar();
                _inFlight = false;

                try
                {
                    Report(_req);
                }
                finally
                {
                    _req.Dispose();
                }
            }
        }

        static void Report(UnityWebRequest req)
        {
            long code = req.responseCode;
            string body = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (code >= 200 && code < 300)
            {
                SendResult ok = SendResult.Successful(code, body);
                bool alreadyExists = string.Equals(ok.status, "already_exists", System.StringComparison.OrdinalIgnoreCase);
                string headline = alreadyExists
                    ? "Connected — this test report was already received earlier."
                    : "Connected — test report delivered successfully.";

                var details = new System.Text.StringBuilder(headline).Append("\n\n");
                if (!string.IsNullOrEmpty(ok.reportId)) details.Append($"Report ID: {ok.reportId}\n");
                details.Append($"HTTP {code}");

                Debug.Log($"[BugCapture] Test report succeeded (HTTP {code}). reportId={ok.reportId} status={ok.status} url={ok.dashboardUrl}");

                bool hasLink = !string.IsNullOrEmpty(ok.dashboardUrl);
                if (hasLink)
                {
                    if (EditorUtility.DisplayDialog(
                            "BugCapture — Test Report Sent", details.ToString(), "Open Dashboard", "Close"))
                    {
                        Application.OpenURL(ok.dashboardUrl);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("BugCapture — Test Report Sent", details.ToString(), "Close");
                }
                return;
            }

            SendResult failed = SendResult.Failed(code, body, req.error);
            string reason = failed.message;
            string codeLine = code > 0
                ? $"HTTP {code}" + (string.IsNullOrEmpty(failed.errorCode) ? "" : $" ({failed.errorCode})")
                : "no response (network/transport error)";

            Debug.LogError($"[BugCapture] Test report failed [{codeLine}]: {reason}");
            EditorUtility.DisplayDialog(
                "BugCapture — Test Report Failed",
                $"{reason}\n\n{codeLine}",
                "OK");
        }

        // Minimal usability gate: a malformed endpoint would throw inside UnityWebRequest, so reject
        // it up front with a precise reason. Everything else (auth, server-side validation) is left
        // to the round trip so the test stays genuinely end-to-end.
        static bool IsUsableEndpoint(string endpoint, out string reason)
        {
            string value = endpoint == null ? "" : endpoint.Trim();
            if (string.IsNullOrEmpty(value))
            {
                reason = "empty";
                return false;
            }
            if (!value.StartsWith("http://") && !value.StartsWith("https://"))
            {
                reason = "not an http(s) URL";
                return false;
            }
            reason = null;
            return true;
        }
    }
}
#endif
