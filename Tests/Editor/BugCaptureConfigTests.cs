using NUnit.Framework;
using UnityEngine;

namespace BugCaptureSDK.Tests
{
    /// <summary>
    /// Guards the default values of a freshly created <see cref="BugCaptureConfig"/>.
    /// These defaults are the SDK's out-of-the-box behaviour; changing one is a behaviour
    /// change that should be deliberate, so these assertions pin them.
    /// </summary>
    public class BugCaptureConfigTests
    {
        BugCaptureConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<BugCaptureConfig>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_config);
        }

        [Test]
        public void Defaults_MatchDocumentedValues()
        {
            Assert.AreEqual("", _config.apiKey);
            Assert.AreEqual("https://api.bugcapture.dev", _config.endpoint);
            Assert.AreEqual("development", _config.environment);
            Assert.AreEqual(KeyCode.F8, _config.hotkey);
            Assert.AreEqual("bug", _config.defaultCategory);
        }

        [Test]
        public void Defaults_CaptureTogglesOn()
        {
            Assert.IsTrue(_config.captureScreenshot);
            Assert.IsTrue(_config.captureLogs);
            Assert.AreEqual(500, _config.maxLogLines);
        }

        [Test]
        public void Defaults_OverlayBehaviour()
        {
            Assert.IsFalse(_config.pauseWhileOpen);
            Assert.IsTrue(_config.blockGameplayInput);
        }

        [Test]
        public void Defaults_SizeCaps()
        {
            Assert.AreEqual(5 * 1024 * 1024, _config.maxScreenshotBytes);
            Assert.AreEqual(2 * 1024 * 1024, _config.maxLogBytes);
            Assert.AreEqual(256 * 1024, _config.maxMetadataBytes);
        }

        [Test]
        public void Defaults_OfflineQueue()
        {
            Assert.IsTrue(_config.enableOfflineQueue);
            Assert.AreEqual(50, _config.maxQueuedReports);
        }
    }
}
