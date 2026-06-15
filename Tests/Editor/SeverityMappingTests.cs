using NUnit.Framework;
using UnityEngine;

namespace BugCaptureSDK.Tests
{
    /// <summary>
    /// The backend expects lowercase severity values (low | medium | high | critical).
    /// <see cref="MetadataCollector.Build"/> derives them from the <see cref="Severity"/>
    /// enum; this verifies the mapping for every enum member.
    /// </summary>
    public class SeverityMappingTests
    {
        [TestCase(Severity.Low, "low")]
        [TestCase(Severity.Medium, "medium")]
        [TestCase(Severity.High, "high")]
        [TestCase(Severity.Critical, "critical")]
        public void Build_MapsSeverityToLowercaseString(Severity severity, string expected)
        {
            BugCaptureConfig config = ScriptableObject.CreateInstance<BugCaptureConfig>();

            ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x", severity = severity });

            Assert.AreEqual(expected, m.report.severity);
        }

        [Test]
        public void EveryEnumMemberMapsToAKnownBackendValue()
        {
            BugCaptureConfig config = ScriptableObject.CreateInstance<BugCaptureConfig>();
            var allowed = new[] { "low", "medium", "high", "critical" };

            foreach (Severity severity in System.Enum.GetValues(typeof(Severity)))
            {
                ReportMetadata m = MetadataCollector.Build(config, new ReportInput { title = "x", severity = severity });
                Assert.Contains(m.report.severity, allowed,
                    $"{severity} produced an unexpected backend severity value");
            }
        }
    }
}
