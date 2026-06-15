using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// EditMode coverage for <see cref="ContextJson.Serialize"/> and the context size cap applied
    /// by <see cref="MetadataCollector.Build"/>: the JSON shape for the supported value types, and
    /// that an oversized context bag is dropped rather than shipped.
    /// </summary>
    public class ContextJsonTests
    {
        [Test]
        public void Serialize_Null_ReturnsNull()
        {
            Assert.IsNull(ContextJson.Serialize(null));
        }

        [Test]
        public void Serialize_EmptyObject_IsEmptyBraces()
        {
            Assert.AreEqual("{}", ContextJson.Serialize(new Dictionary<string, object>()));
        }

        [Test]
        public void Serialize_Primitives_UseJsonLiteralsAndInvariantNumbers()
        {
            var ctx = new Dictionary<string, object>
            {
                { "health", 80 },
                { "ratio", 0.5 },
                { "alive", true },
                { "checkpoint", "desert_arena_entry" },
                { "missing", null },
            };

            string json = ContextJson.Serialize(ctx);

            StringAssert.Contains("\"health\":80", json);
            StringAssert.Contains("\"ratio\":0.5", json);
            StringAssert.Contains("\"alive\":true", json);
            StringAssert.Contains("\"checkpoint\":\"desert_arena_entry\"", json);
            StringAssert.Contains("\"missing\":null", json);
        }

        [Test]
        public void Serialize_NestsDictionariesAndLists()
        {
            var ctx = new Dictionary<string, object>
            {
                { "inventory", new List<object> { "sword", "shield" } },
                { "questFlags", new Dictionary<string, object> { { "bridgeUnlocked", false } } },
            };

            string json = ContextJson.Serialize(ctx);

            StringAssert.Contains("\"inventory\":[\"sword\",\"shield\"]", json);
            StringAssert.Contains("\"questFlags\":{\"bridgeUnlocked\":false}", json);
        }

        [Test]
        public void Serialize_EscapesStringsAndCoercesNonFiniteToNull()
        {
            var ctx = new Dictionary<string, object>
            {
                { "note", "line1\n\"quoted\"" },
                { "bad", float.NaN },
            };

            string json = ContextJson.Serialize(ctx);

            StringAssert.Contains("\"note\":\"line1\\n\\\"quoted\\\"\"", json);
            StringAssert.Contains("\"bad\":null", json);
        }

        [Test]
        public void Build_SerializesContextIntoMetadata()
        {
            BugyardConfig config = ScriptableObject.CreateInstance<BugyardConfig>();
            var input = new ReportInput
            {
                title = "x",
                context = new Dictionary<string, object> { { "checkpoint", "entry" } },
            };

            ReportMetadata m = MetadataCollector.Build(config, input);

            StringAssert.Contains("\"checkpoint\":\"entry\"", m.contextJson);
            StringAssert.Contains("\"context\":{\"checkpoint\":\"entry\"}", MetadataJson.Serialize(m));

            Object.DestroyImmediate(config);
        }

        [Test]
        public void Build_DropsOversizedContext()
        {
            BugyardConfig config = ScriptableObject.CreateInstance<BugyardConfig>();
            config.maxContextBytes = 32;

            var big = new string('a', 256);
            var input = new ReportInput
            {
                title = "x",
                context = new Dictionary<string, object> { { "blob", big } },
            };

            // ClampContext logs a warning when it drops the bag; don't let that fail the test.
            LogAssert.ignoreFailingMessages = true;
            ReportMetadata m = MetadataCollector.Build(config, input);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsNull(m.contextJson, "context over the cap should be dropped, not shipped");
            Assert.IsFalse(MetadataJson.Serialize(m).Contains("\"context\""),
                "dropped context should not appear in the serialized metadata");

            Object.DestroyImmediate(config);
        }
    }
}
