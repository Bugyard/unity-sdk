using System;
using System.Text;

namespace BugyardSDK
{
    /// <summary>
    /// Translates the backend error envelope (<c>{ error, message, details? }</c>) and the
    /// HTTP status of a failed upload into a single, friendly, actionable message safe to show
    /// in the overlay. See the ingestion contract in bugyard-backend-docs/03-api-contracts.md.
    ///
    /// Documented error codes always win over the raw server <c>message</c> so each produces a
    /// distinct, actionable string; unknown codes fall back to the server message, then to a
    /// status-class generic, then to the transport error.
    /// </summary>
    public static class BackendErrors
    {
        // Documented { error } codes (see bugyard-backend-docs/03-api-contracts.md "Error codes").
        public const string Unauthorized = "UNAUTHORIZED";
        public const string RequestNotValid = "REQUEST_NOT_VALID";
        public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
        public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";
        public const string ReportLimitExceeded = "REPORT_LIMIT_EXCEEDED";

        /// <summary>
        /// Build the user-facing message for a failed send.
        /// </summary>
        /// <param name="httpStatus">HTTP status of the final attempt, or 0 on a transport error.</param>
        /// <param name="errorCode">The <c>error</c> code from the body (may be null/empty).</param>
        /// <param name="serverMessage">The <c>message</c> from the body (may be null/empty).</param>
        /// <param name="transportError">UnityWebRequest.error, if any.</param>
        public static string FriendlyMessage(
            long httpStatus, string errorCode, string serverMessage, string transportError)
        {
            string code = Normalize(errorCode);

            // 1. Documented codes → distinct, actionable SDK message (translation wins over the
            //    raw server text). Rate limiting is keyed off the 429 status because the body may
            //    carry no { error } code.
            switch (code)
            {
                case Unauthorized:
                    return "Upload rejected: the API key is missing or invalid. " +
                           "Check the Bugyard API key in your config.";
                case RequestNotValid:
                    return "The report was rejected as invalid. " +
                           "Check the report fields (title, severity, category) and try again.";
                case PayloadTooLarge:
                    return "An attachment is too large to upload. " +
                           "Try a smaller screenshot or shorter logs.";
                case ReportLimitExceeded:
                    return "This project has reached its report limit. " +
                           "Ask the project owner to raise the quota, then try again.";
            }

            if (httpStatus == 429 || code == RateLimitExceeded)
                return "Too many reports were sent recently. Please wait a moment and try again.";

            // 2. Clear transport failure: there is no useful body to fall back to.
            if (httpStatus == 0)
                return "Couldn't reach the server. Check your connection and try again.";

            // 3. Unknown code, but the server explained itself → surface that verbatim.
            if (!string.IsNullOrEmpty(serverMessage))
                return serverMessage;

            // 4. Status-class generics.
            if (httpStatus >= 500)
                return "The server had a problem. Please try again later.";

            // 5. Last resorts.
            if (!string.IsNullOrEmpty(transportError))
                return transportError;
            return httpStatus > 0 ? $"Send failed (HTTP {httpStatus})." : "Send failed.";
        }

        static string Normalize(string code)
        {
            return string.IsNullOrEmpty(code) ? "" : code.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Extract the raw JSON value of a top-level field as a string, without deserializing it.
        /// Used for the optional <c>details</c> field, whose shape is unspecified (string, object,
        /// or array) — declaring it on the <c>JsonUtility</c> envelope would risk throwing and
        /// losing <c>error</c>/<c>message</c> when the server sends a non-string. Returns null when
        /// the field is absent or the value can't be cleanly delimited. Not a general JSON parser:
        /// it scans for <c>"field"</c> at the top level and captures one balanced value.
        /// </summary>
        public static string ExtractRawField(string body, string field)
        {
            if (string.IsNullOrEmpty(body) || string.IsNullOrEmpty(field)) return null;

            string needle = "\"" + field + "\"";
            int i = 0;
            while (true)
            {
                i = body.IndexOf(needle, i, StringComparison.Ordinal);
                if (i < 0) return null;
                // Reject a match inside a string value by checking the char isn't escaped.
                if (i == 0 || body[i - 1] != '\\') break;
                i += needle.Length;
            }

            i += needle.Length;
            i = SkipWhitespace(body, i);
            if (i >= body.Length || body[i] != ':') return null;
            i = SkipWhitespace(body, i + 1);
            if (i >= body.Length) return null;

            char c = body[i];
            if (c == '{') return CaptureBalanced(body, i, '{', '}');
            if (c == '[') return CaptureBalanced(body, i, '[', ']');
            if (c == '"') return CaptureString(body, i);
            return CaptureScalar(body, i); // number / true / false / null
        }

        static int SkipWhitespace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        static string CaptureBalanced(string s, int start, char open, char close)
        {
            int depth = 0;
            bool inStr = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == open) depth++;
                else if (c == close && --depth == 0) return s.Substring(start, i - start + 1);
            }
            return null; // unbalanced
        }

        static string CaptureString(string s, int start)
        {
            var sb = new StringBuilder();
            for (int i = start + 1; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\')
                {
                    if (i + 1 >= s.Length) return null;
                    char n = s[++i];
                    switch (n)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 >= s.Length) return null;
                            sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                            i += 4;
                            break;
                        default: return null;
                    }
                    continue;
                }
                if (c == '"') return sb.ToString();
                sb.Append(c);
            }
            return null; // unterminated
        }

        static string CaptureScalar(string s, int start)
        {
            int i = start;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && !char.IsWhiteSpace(s[i])) i++;
            string v = s.Substring(start, i - start);
            return v.Length == 0 || v == "null" ? null : v;
        }
    }
}
