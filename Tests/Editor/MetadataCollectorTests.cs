using NUnit.Framework;
using UnityEngine;

namespace BugCaptureSDK.Tests
{
    /// <summary>
    /// EditMode coverage for <see cref="MetadataCollector.Build"/>: the fields it derives
    /// from config + input, and the title/category/playerPosition fallbacks. Engine-derived
    /// fields (device, runtime, scene) are non-deterministic in the test runner and only
    /// checked for presence, not exact value.
    /// </summary>
    public class MetadataCollectorTests
    {
        static BugCaptureConfig NewConfig()
        {
            return ScriptableObject.CreateInstance<BugCaptureConfig>();
        }

        [Test]
        public void Build_CopiesConfigAndConstantFields()
        {
            BugCaptureConfig config = NewConfig();
            config.environment = "staging";

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x" });

            Assert.AreEqual("staging", m.environment);
            Assert.AreEqual("unity", m.engine);
            Assert.AreEqual(BugCaptureVersion.Value, m.sdkVersion);
            Assert.AreEqual(Application.version, m.buildVersion);
            Assert.AreEqual(Application.unityVersion, m.engineVersion);
        }

        [Test]
        public void Build_GeneratesUniqueGuidClientReportId()
        {
            BugCaptureConfig config = NewConfig();

            ReportMetadata a = MetadataCollector.Build(config, new ReportInput { title = "a" });
            ReportMetadata b = MetadataCollector.Build(config, new ReportInput { title = "b" });

            Assert.That(System.Guid.TryParse(a.clientReportId, out _), Is.True,
                "clientReportId should be a valid GUID");
            Assert.AreNotEqual(a.clientReportId, b.clientReportId,
                "each Build call should mint a fresh clientReportId");
        }

        [Test]
        public void Build_DefaultsBlankTitle()
        {
            BugCaptureConfig config = NewConfig();

            Assert.AreEqual("(no title)", MetadataCollector.Build(config, new ReportInput { title = null }).report.title);
            Assert.AreEqual("(no title)", MetadataCollector.Build(config, new ReportInput { title = "   " }).report.title);
        }

        [Test]
        public void Build_PreservesProvidedTitle()
        {
            BugCaptureConfig config = NewConfig();

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "Crash on jump" });

            Assert.AreEqual("Crash on jump", m.report.title);
        }

        [Test]
        public void Build_UsesConfigDefaultCategoryWhenInputBlank()
        {
            BugCaptureConfig config = NewConfig();
            config.defaultCategory = "feedback";

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x", category = null });

            Assert.AreEqual("feedback", m.report.category);
        }

        [Test]
        public void Build_UsesInputCategoryWhenProvided()
        {
            BugCaptureConfig config = NewConfig();
            config.defaultCategory = "bug";

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x", category = "crash" });

            Assert.AreEqual("crash", m.report.category);
        }

        [Test]
        public void Build_CopiesBodyFields()
        {
            BugCaptureConfig config = NewConfig();
            var input = new ReportInput
            {
                title = "t",
                description = "d",
                expectedResult = "e",
            };

            ReportMetadata m = MetadataCollector.Build(config, input);

            Assert.AreEqual("d", m.report.description);
            Assert.AreEqual("e", m.report.expectedResult);
        }

        [Test]
        public void Build_UsesProvidedPlayerPosition()
        {
            BugCaptureConfig config = NewConfig();
            var input = new ReportInput { title = "x", playerPosition = new Vector3(1.5f, -2f, 3f) };

            ReportMetadata m = MetadataCollector.Build(config, input);

            Assert.AreEqual(1.5f, m.playerPosition.x);
            Assert.AreEqual(-2f, m.playerPosition.y);
            Assert.AreEqual(3f, m.playerPosition.z);
        }

        [Test]
        public void Build_PassesThroughReporter()
        {
            BugCaptureConfig config = NewConfig();
            var reporter = new ReporterInfo { id = "u1", name = "Tester", email = "t@example.com" };

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x", reporter = reporter });

            Assert.AreSame(reporter, m.reporter);
        }

        [Test]
        public void Build_OmitsReporterWhenNotSet()
        {
            BugCaptureConfig config = NewConfig();

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x" });

            Assert.IsNull(m.reporter);
        }
    }
}
