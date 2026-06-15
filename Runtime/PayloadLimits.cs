using System.Text;
using UnityEngine;

namespace BugyardSDK
{
    /// <summary>
    /// Enforces the client-side size caps from <see cref="BugyardConfig"/> right before
    /// upload, so an oversized attachment is reduced or dropped here rather than shipped and
    /// rejected by the backend with <c>PAYLOAD_TOO_LARGE</c>:
    /// <list type="bullet">
    ///   <item>screenshots are progressively downscaled, then dropped if still too large;</item>
    ///   <item>logs are trimmed to their most recent lines (oldest dropped first);</item>
    ///   <item>metadata free-text is truncated until the serialized JSON fits.</item>
    /// </list>
    /// All methods are tolerant of a non-positive cap (treated as "no limit", except logs/metadata
    /// where it still produces a valid, empty-ish payload) and never throw into the upload path.
    /// Texture work runs on the main thread (the upload coroutine), matching where capture happens.
    /// </summary>
    public static class PayloadLimits
    {
        // How many times we halve the screenshot's dimensions trying to fit the cap before
        // giving up and dropping it. Each pass quarters the pixel count, so four passes take a
        // 4K-ish frame down by 256x in area — well past any realistic 5 MB PNG.
        const int MaxScreenshotDownscaleSteps = 4;

        /// <summary>
        /// Return <paramref name="png"/> unchanged if it fits <paramref name="maxBytes"/>;
        /// otherwise decode, progressively downscale, and re-encode until it fits. Returns
        /// <c>null</c> (drop the screenshot) if it cannot be brought under the cap or cannot be
        /// decoded. A warning is logged whenever the screenshot is altered or dropped.
        /// </summary>
        public static byte[] ClampScreenshot(byte[] png, int maxBytes)
        {
            if (png == null || png.Length == 0) return png;
            if (maxBytes <= 0 || png.Length <= maxBytes) return png;

            Texture2D source = null;
            try
            {
                source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!source.LoadImage(png))
                {
                    Debug.LogWarning(
                        $"[Bugyard] Screenshot is {png.Length / 1024} KB (cap {maxBytes / 1024} KB) " +
                        "and could not be decoded to downscale; sending the report without it.");
                    return null;
                }

                int width = source.width;
                int height = source.height;
                byte[] current = png;

                for (int step = 0; step < MaxScreenshotDownscaleSteps && current.Length > maxBytes; step++)
                {
                    width = Mathf.Max(1, width / 2);
                    height = Mathf.Max(1, height / 2);

                    byte[] scaled = ResampleToPng(source, width, height);
                    if (scaled == null || scaled.Length == 0) break;
                    current = scaled;

                    if (width == 1 && height == 1) break; // can't shrink further
                }

                if (current.Length > maxBytes)
                {
                    Debug.LogWarning(
                        $"[Bugyard] Screenshot still {current.Length / 1024} KB after downscaling " +
                        $"(cap {maxBytes / 1024} KB); sending the report without it.");
                    return null;
                }

                Debug.LogWarning(
                    $"[Bugyard] Screenshot downscaled to {width}x{height} ({current.Length / 1024} KB) " +
                    $"to fit the {maxBytes / 1024} KB cap.");
                return current;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    "[Bugyard] Failed to downscale an oversized screenshot; sending the report without it. " + e.Message);
                return null;
            }
            finally
            {
                if (source != null) Object.Destroy(source);
            }
        }

        // CPU bilinear resample of a readable source texture to width x height, encoded as PNG.
        // Sampling the full-resolution source each pass keeps quality better than chaining halvings.
        static byte[] ResampleToPng(Texture2D source, int width, int height)
        {
            var dst = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color[width * height];
                for (int y = 0; y < height; y++)
                {
                    float v = height == 1 ? 0f : (float)y / (height - 1);
                    for (int x = 0; x < width; x++)
                    {
                        float u = width == 1 ? 0f : (float)x / (width - 1);
                        pixels[y * width + x] = source.GetPixelBilinear(u, v);
                    }
                }
                dst.SetPixels(pixels);
                dst.Apply(false);
                return dst.EncodeToPNG();
            }
            finally
            {
                Object.Destroy(dst);
            }
        }

        /// <summary>
        /// Trim <paramref name="logs"/> to at most <paramref name="maxBytes"/> of UTF-8, keeping
        /// the most recent lines (oldest dropped first) and prepending a one-line trimmed notice.
        /// Returns the input unchanged when it already fits. A warning is logged when trimming.
        /// </summary>
        public static string ClampLogs(string logs, int maxBytes)
        {
            if (string.IsNullOrEmpty(logs)) return logs;

            byte[] all = Encoding.UTF8.GetBytes(logs);
            if (maxBytes <= 0)
            {
                Debug.LogWarning("[Bugyard] Log size cap is non-positive; sending the report without logs.");
                return "";
            }
            if (all.Length <= maxBytes) return logs;

            const string notice = "[Bugyard] ...older log lines were trimmed to fit the upload size cap.\n";
            int noticeBytes = Encoding.UTF8.GetByteCount(notice);
            int budget = Mathf.Max(0, maxBytes - noticeBytes);

            // Keep the tail that fits the budget. Snap the cut forward to the next line start so the
            // first kept line is whole; if the over-budget span has no newline (one giant line), snap
            // to a UTF-8 character boundary so we never split a multi-byte code point.
            int start = all.Length - budget;
            if (start < 0) start = 0;

            int lineStart = start;
            while (lineStart < all.Length && all[lineStart] != (byte)'\n') lineStart++;
            if (lineStart < all.Length)
                start = lineStart + 1; // skip the newline itself
            else
                while (start < all.Length && (all[start] & 0xC0) == 0x80) start++; // skip continuation bytes

            string kept = Encoding.UTF8.GetString(all, start, all.Length - start);
            Debug.LogWarning(
                $"[Bugyard] Logs were {all.Length / 1024} KB (cap {maxBytes / 1024} KB); " +
                "older lines were trimmed to fit.");
            return notice + kept;
        }

        /// <summary>
        /// Serialize <paramref name="metadata"/> and, if the JSON exceeds
        /// <paramref name="maxBytes"/>, truncate its free-text fields (description, expected result,
        /// title) until it fits. Returns the final JSON, which is guaranteed within the cap unless
        /// the non-text fields alone already exceed it (logged as a warning). Mutates the free-text
        /// fields of the passed metadata when trimming.
        /// </summary>
        public static string ClampMetadata(ReportMetadata metadata, int maxBytes)
        {
            string json = MetadataJson.Serialize(metadata);
            if (maxBytes <= 0 || Encoding.UTF8.GetByteCount(json) <= maxBytes) return json;

            ReportBody body = metadata.report;
            bool trimmed = false;

            // Halve the longest free-text field each pass and re-serialize. Halving converges in a
            // logarithmic number of steps regardless of which field is the bloat source, and the
            // guard bounds it even in pathological cases. JSON escaping makes the size non-linear,
            // so we measure after each pass rather than computing a budget up front.
            for (int guard = 0; guard < 64 && Encoding.UTF8.GetByteCount(json) > maxBytes; guard++)
            {
                if (!TrimLongestField(body)) break; // nothing left to trim
                trimmed = true;
                json = MetadataJson.Serialize(metadata);
            }

            if (Encoding.UTF8.GetByteCount(json) > maxBytes)
                Debug.LogWarning(
                    $"[Bugyard] Metadata is {Encoding.UTF8.GetByteCount(json) / 1024} KB and exceeds the " +
                    $"{maxBytes / 1024} KB cap even after truncating free-text fields; the backend may reject it.");
            else if (trimmed)
                Debug.LogWarning(
                    $"[Bugyard] Metadata exceeded the {maxBytes / 1024} KB cap; free-text fields were truncated to fit.");

            return json;
        }

        // Truncate the longest of the report's free-text fields by half. Returns false when all
        // three are empty (nothing left to trim).
        static bool TrimLongestField(ReportBody body)
        {
            int dl = body.description != null ? body.description.Length : 0;
            int el = body.expectedResult != null ? body.expectedResult.Length : 0;
            int tl = body.title != null ? body.title.Length : 0;

            int max = Mathf.Max(dl, Mathf.Max(el, tl));
            if (max == 0) return false;

            if (dl == max) body.description = Halve(body.description);
            else if (el == max) body.expectedResult = Halve(body.expectedResult);
            else body.title = Halve(body.title);
            return true;
        }

        static string Halve(string s) =>
            string.IsNullOrEmpty(s) ? s : s.Substring(0, s.Length / 2);
    }
}
