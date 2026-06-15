using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// EditMode coverage for <see cref="BreadcrumbBuffer"/>: the bounded FIFO behaviour, the
    /// JSON-array shape captured into <c>events.json</c>, payload inclusion, and that an empty
    /// buffer serializes to null so the attachment is omitted.
    /// </summary>
    public class BreadcrumbBufferTests
    {
        static string Json(BreadcrumbBuffer b)
        {
            byte[] bytes = b.ToJsonBytes();
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        [Test]
        public void Empty_SerializesToNull()
        {
            Assert.IsNull(new BreadcrumbBuffer(10).ToJsonBytes());
        }

        [Test]
        public void Add_NullOrEmptyName_Ignored()
        {
            var b = new BreadcrumbBuffer(10);
            b.Add(null, null);
            b.Add("", null);
            Assert.AreEqual(0, b.Count);
            Assert.IsNull(b.ToJsonBytes());
        }

        [Test]
        public void Add_RecordsNameAndTimestampAsJsonArray()
        {
            var b = new BreadcrumbBuffer(10);
            b.Add("StartedBossFight", null);

            string json = Json(b);

            StringAssert.StartsWith("[", json);
            StringAssert.Contains("\"name\":\"StartedBossFight\"", json);
            StringAssert.Contains("\"ts\":\"", json); // ISO timestamp present
            Assert.IsFalse(json.Contains("\"payload\""), "payload omitted when not supplied");
        }

        [Test]
        public void Add_IncludesPayloadWhenSupplied()
        {
            var b = new BreadcrumbBuffer(10);
            b.Add("LoadedCheckpoint", new Dictionary<string, object> { { "checkpointId", "cp_04" } });

            StringAssert.Contains("\"payload\":{\"checkpointId\":\"cp_04\"}", Json(b));
        }

        [Test]
        public void Buffer_KeepsNewestUpToCapAndDropsOldest()
        {
            var b = new BreadcrumbBuffer(2);
            b.Add("first", null);
            b.Add("second", null);
            b.Add("third", null);

            Assert.AreEqual(2, b.Count);
            string json = Json(b);
            Assert.IsFalse(json.Contains("first"), "oldest breadcrumb should be dropped past the cap");
            StringAssert.Contains("\"name\":\"second\"", json);
            StringAssert.Contains("\"name\":\"third\"", json);
        }

        [Test]
        public void Buffer_PreservesOrderOldestFirst()
        {
            var b = new BreadcrumbBuffer(10);
            b.Add("a", null);
            b.Add("b", null);

            string json = Json(b);
            Assert.Less(json.IndexOf("\"a\"", System.StringComparison.Ordinal),
                        json.IndexOf("\"b\"", System.StringComparison.Ordinal),
                        "breadcrumbs should serialize oldest-first");
        }

        [Test]
        public void Clear_EmptiesBuffer()
        {
            var b = new BreadcrumbBuffer(10);
            b.Add("x", null);
            b.Clear();

            Assert.AreEqual(0, b.Count);
            Assert.IsNull(b.ToJsonBytes());
        }

        [Test]
        public void Cap_BelowOne_TreatedAsOne()
        {
            var b = new BreadcrumbBuffer(0);
            b.Add("only", null);
            b.Add("kept", null);

            Assert.AreEqual(1, b.Count);
            StringAssert.Contains("\"name\":\"kept\"", Json(b));
        }
    }
}
