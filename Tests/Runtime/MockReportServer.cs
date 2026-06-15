using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// A loopback HTTP mock of <c>POST {endpoint}/v1/reports</c> for exercising
    /// <see cref="BugyardClient"/> end-to-end without touching a real backend. It binds an
    /// <see cref="HttpListener"/> to a free port on <c>127.0.0.1</c>, serves a scripted sequence
    /// of responses on a background thread, and records every request it received (status line,
    /// headers, and the decoded multipart parts) for assertions.
    ///
    /// Usage: enqueue the responses a test needs in order, optionally set a fallback with
    /// <see cref="AlwaysRespondWith"/>, point the config at <see cref="Endpoint"/>, then inspect
    /// <see cref="Requests"/> afterwards. Always <see cref="Dispose"/> it (a <c>using</c> or
    /// <c>[TearDown]</c>) so the listener and its thread are released.
    /// </summary>
    public sealed class MockReportServer : IDisposable
    {
        /// <summary>A single scripted HTTP response.</summary>
        public sealed class Response
        {
            public int Status = 200;
            public string Body = "";
            public Dictionary<string, string> Headers;

            public static Response Json(int status, string body) =>
                new Response { Status = status, Body = body };

            /// <summary>A 429 carrying a <c>Retry-After</c> header (delta-seconds or HTTP-date).</summary>
            public static Response RateLimited(string retryAfter, string body = "") =>
                new Response
                {
                    Status = 429,
                    Body = body,
                    Headers = new Dictionary<string, string> { { "Retry-After", retryAfter } },
                };
        }

        /// <summary>A request the mock received, with its multipart body decoded.</summary>
        public sealed class RecordedRequest
        {
            public string Method;
            public string Path;
            public string Authorization;
            public string ContentType;
            public byte[] Body;

            /// <summary>Multipart form field name → raw bytes (metadata, screenshot, logs, events, save_state, memory_dump).
            /// The diagnostic snapshot rides the memory_dump field (the backend slot keeps that name).</summary>
            public IReadOnlyDictionary<string, byte[]> Parts;

            public bool HasPart(string name) => Parts.ContainsKey(name);

            public string PartText(string name) =>
                Parts.TryGetValue(name, out byte[] bytes) ? Encoding.UTF8.GetString(bytes) : null;

            public string MetadataText => PartText("metadata");
            public string LogsText => PartText("logs");
            public byte[] Screenshot => Parts.TryGetValue("screenshot", out byte[] b) ? b : null;
            public byte[] Events => Parts.TryGetValue("events", out byte[] b) ? b : null;
            public byte[] SaveState => Parts.TryGetValue("save_state", out byte[] b) ? b : null;
            public byte[] DiagnosticSnapshot => Parts.TryGetValue("memory_dump", out byte[] b) ? b : null;
        }

        readonly HttpListener _listener = new HttpListener();
        readonly Queue<Response> _scripted = new Queue<Response>();
        readonly List<RecordedRequest> _received = new List<RecordedRequest>();
        readonly object _gate = new object();
        Response _fallback;
        readonly Thread _thread;
        volatile bool _running;

        /// <summary>Base URL to assign to <see cref="BugyardConfig.endpoint"/> (no trailing slash).</summary>
        public string Endpoint { get; }

        public MockReportServer()
        {
            int port = FreeTcpPort();
            Endpoint = "http://127.0.0.1:" + port;
            _listener.Prefixes.Add(Endpoint + "/");
            _listener.Start();

            _running = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "MockReportServer" };
            _thread.Start();
        }

        /// <summary>Queue responses served in order, one per request, before <see cref="_fallback"/>.</summary>
        public MockReportServer Enqueue(params Response[] responses)
        {
            lock (_gate)
                foreach (Response r in responses)
                    _scripted.Enqueue(r);
            return this;
        }

        /// <summary>Response served once the scripted queue is exhausted (defaults to 200 with an empty body).</summary>
        public MockReportServer AlwaysRespondWith(Response response)
        {
            lock (_gate) _fallback = response;
            return this;
        }

        public int RequestCount
        {
            get { lock (_gate) return _received.Count; }
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get { lock (_gate) return _received.ToArray(); }
        }

        public RecordedRequest LastRequest
        {
            get { lock (_gate) return _received.Count == 0 ? null : _received[_received.Count - 1]; }
        }

        void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; } // listener stopped — exit the thread
                try { Handle(ctx); }
                catch { /* a single bad request must not kill the mock */ }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;

            byte[] body;
            using (var ms = new MemoryStream())
            {
                req.InputStream.CopyTo(ms);
                body = ms.ToArray();
            }

            var recorded = new RecordedRequest
            {
                Method = req.HttpMethod,
                Path = req.Url.AbsolutePath,
                Authorization = req.Headers["Authorization"],
                ContentType = req.ContentType,
                Body = body,
                Parts = MultipartParser.Parse(req.ContentType, body),
            };

            Response resp;
            lock (_gate)
            {
                _received.Add(recorded);
                resp = _scripted.Count > 0
                    ? _scripted.Dequeue()
                    : (_fallback ?? Response.Json(200, ""));
            }

            byte[] payload = Encoding.UTF8.GetBytes(resp.Body ?? "");
            ctx.Response.StatusCode = resp.Status;
            ctx.Response.ContentType = "application/json";
            if (resp.Headers != null)
                foreach (KeyValuePair<string, string> h in resp.Headers)
                    ctx.Response.AddHeader(h.Key, h.Value);
            ctx.Response.ContentLength64 = payload.Length;
            ctx.Response.OutputStream.Write(payload, 0, payload.Length);
            ctx.Response.OutputStream.Close();
        }

        // Ask the OS for a free loopback port by binding to 0, then releasing it. A brief race
        // before HttpListener re-binds is acceptable for a test-only loopback server.
        static int FreeTcpPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose()
        {
            _running = false;
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.Close(); } catch { /* ignore */ }
            try { _thread?.Join(1000); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Minimal <c>multipart/form-data</c> reader: splits a request body on its boundary and maps
    /// each part's <c>name</c> to its raw content bytes. Only what the tests need — enough to read
    /// the <c>metadata</c> JSON and check which attachments (<c>screenshot</c>, <c>logs</c>) were
    /// actually uploaded. Not a general-purpose parser.
    /// </summary>
    static class MultipartParser
    {
        static readonly byte[] CrlfCrlf = { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

        public static Dictionary<string, byte[]> Parse(string contentType, byte[] body)
        {
            var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(contentType) || body == null || body.Length == 0) return parts;

            const string marker = "boundary=";
            int bi = contentType.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (bi < 0) return parts;
            string boundary = contentType.Substring(bi + marker.Length).Trim().Trim('"');

            byte[] delim = Encoding.ASCII.GetBytes("--" + boundary);
            List<int> bounds = FindAll(body, delim);

            // Each part lives between two consecutive boundary delimiters. The final "--boundary--"
            // also starts with the delimiter, so it terminates the last real part.
            for (int i = 0; i + 1 < bounds.Count; i++)
            {
                int segStart = bounds[i] + delim.Length;
                int segEnd = bounds[i + 1];
                if (segStart >= segEnd) continue;

                // Skip the CRLF that follows the delimiter, then split headers from content.
                int p = segStart;
                if (p + 1 < segEnd && body[p] == (byte)'\r' && body[p + 1] == (byte)'\n') p += 2;

                int headerEnd = IndexOf(body, CrlfCrlf, p, segEnd);
                if (headerEnd < 0) continue;

                string headers = Encoding.ASCII.GetString(body, p, headerEnd - p);
                string name = NameFromDisposition(headers);
                if (name == null) continue;

                int contentStart = headerEnd + CrlfCrlf.Length;
                int contentEnd = segEnd;
                // Strip the trailing CRLF that precedes the next delimiter.
                if (contentEnd - 2 >= contentStart &&
                    body[contentEnd - 2] == (byte)'\r' && body[contentEnd - 1] == (byte)'\n')
                    contentEnd -= 2;

                int len = Math.Max(0, contentEnd - contentStart);
                var content = new byte[len];
                Array.Copy(body, contentStart, content, 0, len);
                parts[name] = content;
            }
            return parts;
        }

        static string NameFromDisposition(string headers)
        {
            const string key = "name=\"";
            int i = headers.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += key.Length;
            int j = headers.IndexOf('"', i);
            return j < 0 ? null : headers.Substring(i, j - i);
        }

        static List<int> FindAll(byte[] haystack, byte[] needle)
        {
            var result = new List<int>();
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
                if (Match(haystack, i, needle)) result.Add(i);
            return result;
        }

        static int IndexOf(byte[] haystack, byte[] needle, int from, int to)
        {
            for (int i = from; i <= to - needle.Length; i++)
                if (Match(haystack, i, needle)) return i;
            return -1;
        }

        static bool Match(byte[] h, int at, byte[] n)
        {
            for (int k = 0; k < n.Length; k++)
                if (h[at + k] != n[k]) return false;
            return true;
        }
    }
}
