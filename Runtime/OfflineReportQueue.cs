using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace BugCaptureSDK
{
    /// <summary>
    /// On-disk store for reports that failed to upload (e.g. while offline). The already-clamped
    /// wire artifacts — metadata JSON, screenshot PNG and logs — are persisted under
    /// <c>{persistentDataPath}/BugCapture/queue</c>, one file per report, so a later launch can
    /// replay them. The original <c>clientReportId</c> is preserved, so a replay the backend
    /// already received is deduplicated idempotently rather than creating a duplicate.
    ///
    /// The queue is bounded by <see cref="BugCaptureConfig.maxQueuedReports"/>; when full, the
    /// oldest report is dropped to make room. Every method is tolerant of I/O failures and never
    /// throws into the upload path — a queue problem must never break sending or the game.
    /// </summary>
    public static class OfflineReportQueue
    {
        // Bump if the persisted envelope shape changes; files with a different/absent version are
        // dropped on load rather than risking a malformed replay.
        const int SchemaVersion = 1;

        // Persisted envelope. Binary attachments are base64-encoded so the whole record is a single
        // JSON file (JsonUtility-friendly, atomic to write) and the embedded metadata JSON needs no
        // re-escaping. base64 has no characters JsonUtility must escape.
        [Serializable]
        class Envelope
        {
            public int version;
            public string clientReportId;
            public long enqueuedAtUtcTicks;
            public string metadataBase64;
            public string logsBase64;
            public string screenshotBase64;
        }

        /// <summary>A decoded queued report ready to replay, plus the disk path backing it.</summary>
        public class Entry
        {
            public string Path;
            public string ClientReportId;
            public string MetadataJson;
            public string Logs;
            public byte[] Screenshot;
            public long EnqueuedAtUtcTicks;
        }

        static string Root => Path.Combine(Application.persistentDataPath, "BugCapture", "queue");

        /// <summary>
        /// Persist a failed report so it can be retried on a later launch. Enforces the configured
        /// queue bound by dropping the oldest entries first, and replaces any existing file for the
        /// same <paramref name="clientReportId"/> so re-enqueueing the same report can't duplicate it.
        /// Returns true if the report was written; false (with a warning) on any I/O error.
        /// </summary>
        public static bool Enqueue(
            BugCaptureConfig config, string metadataJson, byte[] screenshot, string logs, string clientReportId)
        {
            if (string.IsNullOrEmpty(metadataJson) || string.IsNullOrEmpty(clientReportId)) return false;

            try
            {
                Directory.CreateDirectory(Root);

                // Drop any prior file for this id (keeps re-enqueue idempotent), then enforce the
                // bound, leaving room for the one we're about to add.
                foreach (string existing in FilesForId(clientReportId)) SafeDelete(existing);

                int max = Mathf.Max(1, config != null ? config.maxQueuedReports : 50);
                List<string> files = QueueFiles();
                int dropCount = files.Count - (max - 1);
                for (int i = 0; i < dropCount; i++) SafeDelete(files[i]);

                var env = new Envelope
                {
                    version = SchemaVersion,
                    clientReportId = clientReportId,
                    enqueuedAtUtcTicks = DateTime.UtcNow.Ticks,
                    metadataBase64 = ToBase64(metadataJson),
                    logsBase64 = string.IsNullOrEmpty(logs) ? null : ToBase64(logs),
                    screenshotBase64 = (screenshot != null && screenshot.Length > 0)
                        ? Convert.ToBase64String(screenshot)
                        : null,
                };

                // Filename is the enqueue time padded to a fixed width so an ordinal sort of the
                // directory is chronological, then the id for uniqueness/lookup.
                string name = env.enqueuedAtUtcTicks.ToString("D19") + "-" + clientReportId + ".json";
                string finalPath = Path.Combine(Root, name);
                string tmpPath = finalPath + ".tmp";

                // Write to a temp file then move into place so a crash mid-write can't leave a
                // half-written record that would fail to parse on the next launch.
                File.WriteAllText(tmpPath, JsonUtility.ToJson(env), Encoding.UTF8);
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tmpPath, finalPath);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BugCapture] Could not persist report for offline retry: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Load every queued report, oldest first. Records that can't be read or decoded are dropped
        /// (with a warning) rather than blocking the rest of the queue. Returns an empty list when
        /// nothing is queued or the directory doesn't exist.
        /// </summary>
        public static List<Entry> LoadAll()
        {
            var result = new List<Entry>();
            try
            {
                if (!Directory.Exists(Root)) return result;

                foreach (string path in QueueFiles())
                {
                    try
                    {
                        var env = JsonUtility.FromJson<Envelope>(File.ReadAllText(path, Encoding.UTF8));
                        if (env == null || env.version != SchemaVersion || string.IsNullOrEmpty(env.metadataBase64))
                        {
                            Debug.LogWarning("[BugCapture] Dropping an unrecognized queued report: " + Path.GetFileName(path));
                            SafeDelete(path);
                            continue;
                        }

                        result.Add(new Entry
                        {
                            Path = path,
                            ClientReportId = env.clientReportId,
                            MetadataJson = FromBase64Utf8(env.metadataBase64),
                            Logs = string.IsNullOrEmpty(env.logsBase64) ? null : FromBase64Utf8(env.logsBase64),
                            Screenshot = string.IsNullOrEmpty(env.screenshotBase64)
                                ? null
                                : Convert.FromBase64String(env.screenshotBase64),
                            EnqueuedAtUtcTicks = env.enqueuedAtUtcTicks,
                        });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[BugCapture] Dropping an unreadable queued report: " + e.Message);
                        SafeDelete(path);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BugCapture] Could not read the offline report queue: " + e.Message);
            }
            return result;
        }

        /// <summary>Delete a queued report once it has been delivered (or given up on).</summary>
        public static void Remove(string path) => SafeDelete(path);

        /// <summary>Number of reports currently persisted on disk (best-effort; 0 on any error).</summary>
        public static int Count()
        {
            try { return Directory.Exists(Root) ? Directory.GetFiles(Root, "*.json").Length : 0; }
            catch { return 0; }
        }

        // Queue files, oldest first. Names are tick-prefixed at a fixed width, so an ordinal sort of
        // the full paths (which share the Root prefix) orders them by enqueue time.
        static List<string> QueueFiles()
        {
            var list = new List<string>(Directory.GetFiles(Root, "*.json"));
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        static IEnumerable<string> FilesForId(string clientReportId)
        {
            if (!Directory.Exists(Root)) return Array.Empty<string>();
            return Directory.GetFiles(Root, "*-" + clientReportId + ".json");
        }

        static void SafeDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning("[BugCapture] Could not delete a queued report file: " + e.Message); }
        }

        static string ToBase64(string utf8) => Convert.ToBase64String(Encoding.UTF8.GetBytes(utf8));

        static string FromBase64Utf8(string base64) => Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}
