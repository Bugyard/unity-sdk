using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static BugyardSDK.Tests.MockReportServer;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// PlayMode coverage for <see cref="BugyardClient"/> driven against an in-process loopback
    /// mock of <c>POST /v1/reports</c> (<see cref="MockReportServer"/>) — no request ever leaves
    /// the machine or reaches a real backend. Covers every upload path: success parse, retry on
    /// 5xx and 429, <c>Retry-After</c> honoring, client-side size-limit enforcement, error-code
    /// mapping, and offline-queue replay.
    ///
    /// These are PlayMode tests because the send path yields on real <see cref="UnityWebRequest"/>
    /// calls and <see cref="WaitForSeconds"/> backoffs, neither of which advances in EditMode.
    /// </summary>
    public class BugyardClientTests
    {
        const string ApiKey = "by_pk_test_abc123";

        MockReportServer _server;
        BugyardConfig _config;

        static string QueueRoot => Path.Combine(Application.persistentDataPath, "Bugyard", "queue");

        [SetUp]
        public void SetUp()
        {
            // The failure paths log Debug.LogError ("giving up"); we assert on the typed SendResult,
            // not the log, so don't let those fail the test.
            LogAssert.ignoreFailingMessages = true;

            ClearQueue();

            _config = ScriptableObject.CreateInstance<BugyardConfig>();
            _config.apiKey = ApiKey;
            _config.enableOfflineQueue = false; // opt in per-test so disk state is explicit
        }

        [TearDown]
        public void TearDown()
        {
            _server?.Dispose();
            _server = null;
            if (_config != null) Object.DestroyImmediate(_config);
            ClearQueue();
            LogAssert.ignoreFailingMessages = false;
        }

        // --- success parse -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator Send_Success_ParsesBodyAndPostsToReportsEndpoint()
        {
            _server = new MockReportServer().Enqueue(Response.Json(201,
                "{\"reportId\":\"r_abc123\",\"status\":\"created\"," +
                "\"dashboardUrl\":\"https://app.bugyard.com/r/r_abc123\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-success"), Png(), "player log line", r => result = r);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.success);
            Assert.AreEqual(201, result.httpStatus);
            Assert.AreEqual("r_abc123", result.reportId);
            Assert.AreEqual("created", result.status);
            Assert.AreEqual("https://app.bugyard.com/r/r_abc123", result.dashboardUrl);
            Assert.IsFalse(result.queuedForRetry);

            // Exactly one request, to the right path, carrying the bearer token and all three parts.
            Assert.AreEqual(1, _server.RequestCount);
            RecordedRequest req = _server.LastRequest;
            Assert.AreEqual("POST", req.Method);
            Assert.AreEqual("/v1/reports", req.Path);
            Assert.AreEqual("Bearer " + ApiKey, req.Authorization);
            StringAssert.Contains("id-success", req.MetadataText);
            Assert.IsTrue(req.HasPart("screenshot"));
            Assert.IsTrue(req.HasPart("logs"));
        }

        [UnityTest]
        public IEnumerator Send_AlreadyExists_ReportsDeduplicatedOutcome()
        {
            _server = new MockReportServer().Enqueue(Response.Json(200,
                "{\"reportId\":\"r_dupe\",\"status\":\"already_exists\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-dupe"), null, null, r => result = r);

            Assert.IsTrue(result.success);
            Assert.AreEqual("already_exists", result.status);
            StringAssert.Contains("already received", result.message);
        }

        // --- retry on 5xx / 429 ------------------------------------------------------------

        [UnityTest]
        public IEnumerator Send_RetriesOn5xx_ThenSucceeds_WithStableClientReportId()
        {
            _server = new MockReportServer().Enqueue(
                Response.Json(500, "{\"error\":\"INTERNAL\"}"),
                Response.Json(503, "{\"error\":\"UNAVAILABLE\"}"),
                Response.Json(201, "{\"reportId\":\"r_ok\",\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-retry-5xx"), null, null, r => result = r);

            Assert.IsTrue(result.success);
            Assert.AreEqual("r_ok", result.reportId);
            Assert.AreEqual(3, _server.RequestCount, "should retry twice before the success on attempt 3");

            // The clientReportId must be identical across every retry so the backend can dedupe.
            foreach (RecordedRequest req in _server.Requests)
                StringAssert.Contains("id-retry-5xx", req.MetadataText);
        }

        [UnityTest]
        public IEnumerator Send_RetriesOn429_ThenSucceeds()
        {
            _server = new MockReportServer().Enqueue(
                Response.Json(429, ""),
                Response.Json(201, "{\"reportId\":\"r_ok\",\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-retry-429"), null, null, r => result = r);

            Assert.IsTrue(result.success);
            Assert.AreEqual(2, _server.RequestCount);
        }

        [UnityTest]
        public IEnumerator Send_ExhaustsRetriesOn5xx_FailsWithFriendlyMessage()
        {
            _server = new MockReportServer().AlwaysRespondWith(Response.Json(500, "{\"error\":\"INTERNAL\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-5xx-exhaust"), null, null, r => result = r);

            Assert.IsFalse(result.success);
            Assert.AreEqual(500, result.httpStatus);
            Assert.AreEqual(3, _server.RequestCount, "should attempt the configured maximum of 3 times");
            StringAssert.Contains("server had a problem", result.message);
            Assert.IsFalse(result.queuedForRetry, "offline queue is disabled in this test");
        }

        // --- Retry-After honored -----------------------------------------------------------

        [UnityTest]
        public IEnumerator Send_HonorsRetryAfterHeaderOn429()
        {
            // Retry-After (3s) is deliberately larger than the 1s default first-attempt backoff,
            // so the elapsed time proves the server-specified interval was used, not the default.
            _server = new MockReportServer().Enqueue(
                Response.RateLimited("3"),
                Response.Json(201, "{\"reportId\":\"r_ok\",\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            var sw = Stopwatch.StartNew();
            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-retry-after"), null, null, r => result = r);
            sw.Stop();

            Assert.IsTrue(result.success);
            Assert.AreEqual(2, _server.RequestCount);
            Assert.GreaterOrEqual(sw.Elapsed.TotalSeconds, 2.5,
                "should wait the ~3s Retry-After interval, not the 1s default backoff");
        }

        // --- size-limit enforcement --------------------------------------------------------

        [UnityTest]
        public IEnumerator Send_TrimsOversizedLogsToTheConfiguredCap()
        {
            _config.maxLogBytes = 512;
            _server = new MockReportServer().Enqueue(Response.Json(201, "{\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            var sb = new StringBuilder();
            for (int i = 0; i < 200; i++) sb.Append("log line ").Append(i).Append(" lorem ipsum\n");
            string bigLogs = sb.ToString();
            Assert.Greater(Encoding.UTF8.GetByteCount(bigLogs), 512, "precondition: logs exceed the cap");

            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-logcap"), null, bigLogs, null);

            string uploaded = _server.LastRequest.LogsText;
            Assert.IsNotNull(uploaded);
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(uploaded), 512,
                "uploaded logs must be clamped to maxLogBytes before they reach the wire");
            StringAssert.Contains("trimmed", uploaded, "the trim notice should be present");
        }

        [UnityTest]
        public IEnumerator Send_DropsUndecodableOversizedScreenshot()
        {
            _config.maxScreenshotBytes = 64;
            _server = new MockReportServer().Enqueue(Response.Json(201, "{\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            // Over the cap and not a decodable image, so it can't be downscaled — it must be dropped
            // rather than shipped and rejected by the backend with PAYLOAD_TOO_LARGE.
            byte[] junk = new byte[256];

            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-shotcap"), junk, null, null);

            Assert.AreEqual(1, _server.RequestCount);
            Assert.IsFalse(_server.LastRequest.HasPart("screenshot"),
                "an oversized, undecodable screenshot should be omitted from the upload");
        }

        // --- error-code mapping ------------------------------------------------------------

        [UnityTest]
        public IEnumerator Send_Unauthorized_MapsToFriendlyMessageAndStopsImmediately()
        {
            _server = new MockReportServer().AlwaysRespondWith(Response.Json(401,
                "{\"error\":\"UNAUTHORIZED\",\"message\":\"Invalid API key\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-401"), null, null, r => result = r);

            Assert.IsFalse(result.success);
            Assert.AreEqual(401, result.httpStatus);
            Assert.AreEqual(BackendErrors.Unauthorized, result.errorCode);
            StringAssert.Contains("API key", result.message);
            Assert.AreEqual(1, _server.RequestCount, "a 4xx is permanent and must not be retried");
        }

        [UnityTest]
        public IEnumerator Send_RequestNotValid_MapsToFriendlyMessage()
        {
            _server = new MockReportServer().AlwaysRespondWith(Response.Json(400,
                "{\"error\":\"REQUEST_NOT_VALID\",\"message\":\"title is required\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-400"), null, null, r => result = r);

            Assert.IsFalse(result.success);
            Assert.AreEqual(BackendErrors.RequestNotValid, result.errorCode);
            StringAssert.Contains("rejected as invalid", result.message);
            Assert.AreEqual(1, _server.RequestCount);
        }

        [UnityTest]
        public IEnumerator Send_PayloadTooLarge_MapsToFriendlyMessage()
        {
            _server = new MockReportServer().AlwaysRespondWith(Response.Json(413,
                "{\"error\":\"PAYLOAD_TOO_LARGE\",\"message\":\"attachment exceeds limit\"}"));
            _config.endpoint = _server.Endpoint;

            SendResult result = null;
            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-413"), null, null, r => result = r);

            Assert.IsFalse(result.success);
            Assert.AreEqual(BackendErrors.PayloadTooLarge, result.errorCode);
            StringAssert.Contains("too large", result.message);
            Assert.AreEqual(1, _server.RequestCount);
        }

        // --- extra attachments (events / save_state / diagnostic_snapshot) -----------------

        [UnityTest]
        public IEnumerator Send_UploadsEventsSaveStateAndDiagnosticSnapshotWithContractFieldNames()
        {
            _server = new MockReportServer().Enqueue(Response.Json(201, "{\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            byte[] events = Encoding.UTF8.GetBytes("[{\"t\":1,\"e\":\"jump\"}]");
            byte[] saveState = { 1, 2, 3, 4 };
            byte[] diagnosticSnapshot = { 9, 8, 7 };

            var artifacts = new ReportArtifacts
            {
                events = events,
                saveState = saveState,
                diagnosticSnapshot = diagnosticSnapshot,
            };

            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-attachments"), artifacts, null);

            RecordedRequest req = _server.LastRequest;
            Assert.IsTrue(req.HasPart("events"), "events.json attachment should be uploaded");
            Assert.IsTrue(req.HasPart("save_state"), "save_state attachment should be uploaded");
            // The diagnostic snapshot rides the memory_dump field (the backend slot keeps that name).
            Assert.IsTrue(req.HasPart("memory_dump"), "diagnostic snapshot attachment should be uploaded");
            Assert.AreEqual(events, req.Events);
            Assert.AreEqual(saveState, req.SaveState);
            Assert.AreEqual(diagnosticSnapshot, req.DiagnosticSnapshot);
        }

        [UnityTest]
        public IEnumerator Send_DropsOversizedAttachmentsBeforeUpload()
        {
            _config.maxEventsBytes = 4;
            _config.maxSaveStateBytes = 4;
            _config.maxDiagnosticSnapshotBytes = 4;
            _server = new MockReportServer().Enqueue(Response.Json(201, "{\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            var artifacts = new ReportArtifacts
            {
                events = new byte[64],
                saveState = new byte[64],
                diagnosticSnapshot = new byte[64],
            };

            yield return new BugyardClient(_config)
                .Send(NewMetadata("id-attach-cap"), artifacts, null);

            RecordedRequest req = _server.LastRequest;
            Assert.IsFalse(req.HasPart("events"), "oversized events should be dropped");
            Assert.IsFalse(req.HasPart("save_state"), "oversized save_state should be dropped");
            Assert.IsFalse(req.HasPart("memory_dump"), "oversized diagnostic snapshot should be dropped");
        }

        // --- offline-queue replay ----------------------------------------------------------

        [UnityTest]
        public IEnumerator TransientFailure_IsQueued_ThenReplayedByFlushQueue()
        {
            _config.enableOfflineQueue = true;

            // The first send fails transiently (3x 500), so it is persisted; the later flush finds
            // the server healthy (fallback 201) and delivers the same report.
            _server = new MockReportServer()
                .Enqueue(
                    Response.Json(500, ""),
                    Response.Json(500, ""),
                    Response.Json(500, ""))
                .AlwaysRespondWith(Response.Json(201, "{\"reportId\":\"r_late\",\"status\":\"created\"}"));
            _config.endpoint = _server.Endpoint;

            var client = new BugyardClient(_config);

            SendResult sendResult = null;
            yield return client.Send(NewMetadata("id-queued"), null, "queued logs", r => sendResult = r);

            Assert.IsFalse(sendResult.success);
            Assert.IsTrue(sendResult.queuedForRetry, "a 5xx failure should be queued for a later launch");
            Assert.AreEqual(1, OfflineReportQueue.Count(), "exactly one report should be on disk");
            int afterSend = _server.RequestCount;
            Assert.AreEqual(3, afterSend);

            // A later launch flushes the queue against the now-healthy server.
            yield return client.FlushQueue();

            Assert.AreEqual(0, OfflineReportQueue.Count(), "the delivered report should be removed from disk");
            Assert.AreEqual(afterSend + 1, _server.RequestCount, "flush should make exactly one more request");
            StringAssert.Contains("id-queued", _server.LastRequest.MetadataText,
                "the replay must reuse the original clientReportId so the backend dedupes");
        }

        [UnityTest]
        public IEnumerator FlushQueue_KeepsReportOnStillTransientFailure()
        {
            _config.enableOfflineQueue = true;

            // Queue a report by failing the first send, and keep the server unhealthy.
            _server = new MockReportServer().AlwaysRespondWith(Response.Json(500, ""));
            _config.endpoint = _server.Endpoint;

            var client = new BugyardClient(_config);

            SendResult sendResult = null;
            yield return client.Send(NewMetadata("id-still-down"), null, null, r => sendResult = r);
            Assert.IsTrue(sendResult.queuedForRetry);
            Assert.AreEqual(1, OfflineReportQueue.Count());

            // Still offline: the flush must leave the report on disk for the next launch.
            yield return client.FlushQueue();
            Assert.AreEqual(1, OfflineReportQueue.Count(),
                "a still-transient failure must not drop the queued report");
        }

        // --- helpers -----------------------------------------------------------------------

        // A minimal, fully-populated ReportMetadata so MetadataJson.Serialize succeeds without
        // depending on engine-derived fields. The clientReportId is caller-controlled so tests can
        // assert it survives retries and offline replay.
        static ReportMetadata NewMetadata(string clientReportId)
        {
            return new ReportMetadata
            {
                clientReportId = clientReportId,
                environment = "test",
                buildVersion = "1.0.0",
                engine = "unity",
                engineVersion = "2021.3.0f1",
                sdkVersion = "0.1.0",
                sceneName = "TestScene",
                playerPosition = new Vec3 { x = 0f, y = 0f, z = 0f },
                report = new ReportBody { title = "Test report", severity = "medium", category = "bug" },
                device = new DeviceInfo { os = "test-os", cpu = "test-cpu", gpu = "test-gpu", ramMb = 1, deviceModel = "test" },
                runtime = new RuntimeInfo { fps = 60, locale = "en-US", timezone = "UTC" },
            };
        }

        // A tiny but valid PNG so the screenshot part is uploaded as-is (well under any cap).
        static byte[] Png()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixel(0, 0, Color.red);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        static void ClearQueue()
        {
            try { if (Directory.Exists(QueueRoot)) Directory.Delete(QueueRoot, true); }
            catch { /* best effort */ }
        }
    }
}
