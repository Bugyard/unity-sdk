using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BugyardSDK
{
    /// <summary>
    /// Produces a custom diagnostic blob on demand. Register one under a name with
    /// <see cref="Bugyard.RegisterDiagnosticFileProvider"/>; the SDK invokes it while building a
    /// report's <c>diagnostic_snapshot.zip</c> (when snapshot inclusion is enabled) and stores the
    /// returned bytes as <c>custom/&lt;name&gt;</c> inside the zip. Runs on the main thread during
    /// capture, so keep it fast. Return null (or empty) to contribute nothing.
    /// </summary>
    public delegate byte[] DiagnosticFileProvider();

    /// <summary>
    /// Builds the <c>diagnostic_snapshot.zip</c> attachment: a small bundle of the diagnostics that
    /// don't have a first-class report slot of their own — a <c>manifest.json</c> (sdk/build/scene/
    /// timestamp + what the snapshot contains), a <c>runtime_metrics.json</c> (ProfilerRecorder
    /// memory/render counters sampled at capture), and any number of <c>custom/&lt;name&gt;</c> files
    /// produced by <see cref="DiagnosticFileProvider"/>s. ZIP (not gzip) so several files ride in one
    /// attachment; uploaded with MIME <c>application/zip</c>.
    ///
    /// The builder is pure and Unity-free (it only touches <see cref="ContextJson"/> and
    /// <see cref="ZipArchive"/>), so the zip layout can be unit-tested without the runtime. Entry
    /// timestamps are fixed so the same inputs always produce a byte-identical archive.
    /// </summary>
    public static class DiagnosticSnapshot
    {
        public const string ManifestEntry = "manifest.json";
        public const string RuntimeMetricsEntry = "runtime_metrics.json";
        public const string CustomPrefix = "custom/";

        // Fixed entry timestamp so archives are reproducible. The DOS time format zip uses can't
        // represent anything before 1980, so any constant >= 1980 works; the build time lives in
        // the manifest's capturedAtUtc, not the entry metadata.
        static readonly DateTimeOffset EntryTime = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Build the snapshot zip from the manifest/metrics field bags and the custom blobs. The
        /// dictionaries are serialized with <see cref="ContextJson"/> (same value handling as the
        /// report <c>context</c>); a null <paramref name="runtimeMetrics"/> omits the metrics entry.
        /// Custom entries whose bytes are null/empty are skipped, names are sanitized to a safe
        /// single path segment, and collisions are disambiguated with a numeric suffix.
        /// </summary>
        public static byte[] Build(
            IReadOnlyDictionary<string, object> manifest,
            IReadOnlyDictionary<string, object> runtimeMetrics,
            IEnumerable<KeyValuePair<string, byte[]>> customFiles)
        {
            return Build(
                manifest != null ? ContextJson.Serialize(manifest) : null,
                runtimeMetrics != null ? ContextJson.Serialize(runtimeMetrics) : null,
                customFiles);
        }

        /// <summary>
        /// Build the snapshot zip from already-serialized JSON for the manifest and runtime metrics.
        /// Null/empty JSON omits that entry. See the dictionary overload for custom-file handling.
        /// </summary>
        public static byte[] Build(
            string manifestJson,
            string runtimeMetricsJson,
            IEnumerable<KeyValuePair<string, byte[]>> customFiles)
        {
            using (var ms = new MemoryStream())
            {
                // leaveOpen so ToArray() works after the archive's central directory is flushed on Dispose.
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                {
                    if (!string.IsNullOrEmpty(manifestJson))
                        WriteText(zip, ManifestEntry, manifestJson);
                    if (!string.IsNullOrEmpty(runtimeMetricsJson))
                        WriteText(zip, RuntimeMetricsEntry, runtimeMetricsJson);

                    if (customFiles != null)
                    {
                        var used = new HashSet<string>(StringComparer.Ordinal);
                        foreach (KeyValuePair<string, byte[]> kv in customFiles)
                        {
                            if (kv.Value == null || kv.Value.Length == 0) continue;
                            string safe = SanitizeCustomName(kv.Key);
                            if (safe.Length == 0) continue;
                            WriteBytes(zip, CustomPrefix + Unique(used, safe), kv.Value);
                        }
                    }
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Reduce an arbitrary provider name to a single safe zip path segment: directory separators
        /// and traversal collapse away, control characters are stripped, and an empty/dot-only result
        /// becomes "file" so a registered provider always lands somewhere readable rather than being
        /// silently dropped or escaping the <c>custom/</c> folder.
        /// </summary>
        public static string SanitizeCustomName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                // Flatten any path structure to one segment and drop characters that aren't safe in
                // a zip entry name across platforms.
                if (c == '/' || c == '\\') { continue; }
                if (c < 0x20 || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|')
                    continue;
                sb.Append(c);
            }

            string cleaned = sb.ToString().Trim().Trim('.');
            return cleaned.Length == 0 ? "file" : cleaned;
        }

        // Append _2, _3, ... to a name already present so two providers with the same (post-sanitize)
        // name both make it into the zip instead of one clobbering the other.
        static string Unique(HashSet<string> used, string name)
        {
            if (used.Add(name)) return name;
            for (int i = 2; ; i++)
            {
                string candidate = name + "_" + i;
                if (used.Add(candidate)) return candidate;
            }
        }

        static void WriteText(ZipArchive zip, string entryName, string text) =>
            WriteBytes(zip, entryName, Encoding.UTF8.GetBytes(text));

        static void WriteBytes(ZipArchive zip, string entryName, byte[] data)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using (Stream s = entry.Open())
                s.Write(data, 0, data.Length);
        }
    }
}
