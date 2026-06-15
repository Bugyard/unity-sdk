using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// EditMode coverage for the <see cref="DiagnosticSnapshot"/> zip builder: entry layout
    /// (manifest / runtime_metrics / custom/*), JSON serialization of the manifest bag, custom-name
    /// sanitization and de-duplication, skipping of empty blobs, and byte-for-byte determinism. The
    /// ProfilerRecorder sampling and provider invocation are runtime concerns and not exercised here.
    /// </summary>
    public class DiagnosticSnapshotTests
    {
        static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        // Read a built archive into entryName -> bytes so assertions can look entries up by name.
        static Dictionary<string, byte[]> ReadZip(byte[] zipBytes)
        {
            var entries = new Dictionary<string, byte[]>();
            using (var ms = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    using (var es = e.Open())
                    using (var buf = new MemoryStream())
                    {
                        es.CopyTo(buf);
                        entries[e.FullName] = buf.ToArray();
                    }
                }
            }
            return entries;
        }

        static string Text(Dictionary<string, byte[]> entries, string name) =>
            Encoding.UTF8.GetString(entries[name]);

        // --- entry layout ---

        [Test]
        public void Build_WritesManifestAndRuntimeMetricsEntries()
        {
            byte[] zip = DiagnosticSnapshot.Build("{\"a\":1}", "{\"fps\":60}", null);

            Dictionary<string, byte[]> entries = ReadZip(zip);
            Assert.IsTrue(entries.ContainsKey(DiagnosticSnapshot.ManifestEntry));
            Assert.IsTrue(entries.ContainsKey(DiagnosticSnapshot.RuntimeMetricsEntry));
            Assert.AreEqual("{\"a\":1}", Text(entries, DiagnosticSnapshot.ManifestEntry));
            Assert.AreEqual("{\"fps\":60}", Text(entries, DiagnosticSnapshot.RuntimeMetricsEntry));
        }

        [Test]
        public void Build_NullRuntimeMetrics_OmitsMetricsEntry()
        {
            byte[] zip = DiagnosticSnapshot.Build("{\"a\":1}", (string)null, null);

            Dictionary<string, byte[]> entries = ReadZip(zip);
            Assert.IsTrue(entries.ContainsKey(DiagnosticSnapshot.ManifestEntry));
            Assert.IsFalse(entries.ContainsKey(DiagnosticSnapshot.RuntimeMetricsEntry));
        }

        [Test]
        public void Build_CustomFiles_GoUnderCustomPrefix()
        {
            var custom = new[]
            {
                new KeyValuePair<string, byte[]>("ai_state.json", Bytes("{\"state\":\"idle\"}")),
                new KeyValuePair<string, byte[]>("heap.bin", new byte[] { 1, 2, 3 }),
            };

            byte[] zip = DiagnosticSnapshot.Build("{}", null, custom);

            Dictionary<string, byte[]> entries = ReadZip(zip);
            Assert.IsTrue(entries.ContainsKey("custom/ai_state.json"));
            Assert.IsTrue(entries.ContainsKey("custom/heap.bin"));
            Assert.AreEqual(new byte[] { 1, 2, 3 }, entries["custom/heap.bin"]);
        }

        // --- custom-file edge cases ---

        [Test]
        public void Build_SkipsNullAndEmptyCustomBlobs()
        {
            var custom = new[]
            {
                new KeyValuePair<string, byte[]>("empty.bin", new byte[0]),
                new KeyValuePair<string, byte[]>("null.bin", null),
                new KeyValuePair<string, byte[]>("real.bin", new byte[] { 7 }),
            };

            Dictionary<string, byte[]> entries = ReadZip(DiagnosticSnapshot.Build("{}", null, custom));

            Assert.IsFalse(entries.ContainsKey("custom/empty.bin"));
            Assert.IsFalse(entries.ContainsKey("custom/null.bin"));
            Assert.IsTrue(entries.ContainsKey("custom/real.bin"));
        }

        [Test]
        public void Build_SanitizesPathsSoCustomEntriesCannotEscape()
        {
            var custom = new[]
            {
                new KeyValuePair<string, byte[]>("../../etc/passwd", new byte[] { 1 }),
                new KeyValuePair<string, byte[]>("nested/dir/file.txt", new byte[] { 2 }),
            };

            Dictionary<string, byte[]> entries = ReadZip(DiagnosticSnapshot.Build("{}", null, custom));

            foreach (string name in entries.Keys)
            {
                Assert.IsTrue(name.StartsWith(DiagnosticSnapshot.CustomPrefix), $"unexpected entry {name}");
                string segment = name.Substring(DiagnosticSnapshot.CustomPrefix.Length);
                Assert.IsFalse(segment.Contains("/"), $"custom name should be a single segment: {segment}");
                Assert.IsFalse(segment.Contains(".."), $"traversal should be stripped: {segment}");
            }
        }

        [Test]
        public void Build_DuplicateNames_AreDisambiguated()
        {
            var custom = new[]
            {
                new KeyValuePair<string, byte[]>("dump", new byte[] { 1 }),
                new KeyValuePair<string, byte[]>("dump", new byte[] { 2 }),
                new KeyValuePair<string, byte[]>("dump", new byte[] { 3 }),
            };

            Dictionary<string, byte[]> entries = ReadZip(DiagnosticSnapshot.Build("{}", null, custom));

            Assert.AreEqual(3, entries.Count, "all three same-named blobs should survive");
            Assert.IsTrue(entries.ContainsKey("custom/dump"));
            Assert.IsTrue(entries.ContainsKey("custom/dump_2"));
            Assert.IsTrue(entries.ContainsKey("custom/dump_3"));
        }

        [Test]
        public void SanitizeCustomName_Cases()
        {
            Assert.AreEqual("passwd", DiagnosticSnapshot.SanitizeCustomName("../../etc/passwd"));
            Assert.AreEqual("adirfile.txt", DiagnosticSnapshot.SanitizeCustomName("a/dir/file.txt"));
            Assert.AreEqual("file", DiagnosticSnapshot.SanitizeCustomName(""));
            Assert.AreEqual("file", DiagnosticSnapshot.SanitizeCustomName("..."));
            Assert.AreEqual("name.json", DiagnosticSnapshot.SanitizeCustomName("name.json"));
        }

        // --- dictionary overload serializes via ContextJson ---

        [Test]
        public void Build_DictionaryOverload_SerializesManifestAndMetrics()
        {
            var manifest = new Dictionary<string, object>
            {
                ["sdkVersion"] = "0.1.0",
                ["scene"] = "Level1",
            };
            var metrics = new Dictionary<string, object> { ["fps"] = 59 };

            byte[] zip = DiagnosticSnapshot.Build(manifest, metrics, null);

            Dictionary<string, byte[]> entries = ReadZip(zip);
            string manifestJson = Text(entries, DiagnosticSnapshot.ManifestEntry);
            StringAssert.Contains("\"sdkVersion\":\"0.1.0\"", manifestJson);
            StringAssert.Contains("\"scene\":\"Level1\"", manifestJson);
            StringAssert.Contains("\"fps\":59", Text(entries, DiagnosticSnapshot.RuntimeMetricsEntry));
        }

        // --- determinism ---

        [Test]
        public void Build_IsDeterministic_ForSameInputs()
        {
            var custom = new[] { new KeyValuePair<string, byte[]>("a.bin", new byte[] { 1, 2, 3 }) };

            byte[] first = DiagnosticSnapshot.Build("{\"a\":1}", "{\"fps\":60}", custom);
            byte[] second = DiagnosticSnapshot.Build("{\"a\":1}", "{\"fps\":60}", custom);

            Assert.AreEqual(first, second, "same inputs should yield a byte-identical archive");
        }
    }
}
