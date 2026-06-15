using System;
using System.Text;
using NUnit.Framework;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// EditMode coverage for the save-state provider plumbing: <see cref="SaveStateResolver.Resolve"/>
    /// precedence (explicit passthrough vs. provider vs. nothing), the per-report/config inclusion
    /// toggle, provider-throws degradation, and the <see cref="SaveState"/> helpers. The overlay
    /// checkbox and capture coroutine wiring are PlayMode concerns and not exercised here.
    /// </summary>
    public class SaveStateProviderTests
    {
        static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        // --- SaveState value type ---

        [Test]
        public void SaveState_JsonAndBinaryHelpers_SetIsJsonFlag()
        {
            SaveState json = SaveState.Json(Bytes("{}"));
            SaveState bin = SaveState.Binary(Bytes("raw"));

            Assert.IsTrue(json.isJson);
            Assert.IsFalse(bin.isJson);
            Assert.IsTrue(json.HasData);
            Assert.IsTrue(bin.HasData);
        }

        [Test]
        public void SaveState_None_HasNoData()
        {
            Assert.IsFalse(SaveState.None.HasData);
            Assert.IsNull(SaveState.None.bytes);
        }

        [Test]
        public void SaveState_EmptyBytes_NotHasData()
        {
            Assert.IsFalse(new SaveState(Array.Empty<byte>()).HasData);
        }

        // --- Resolve: explicit passthrough wins ---

        [Test]
        public void Resolve_ExplicitSaveState_TakesPrecedenceOverProvider()
        {
            bool providerCalled = false;
            SaveStateProvider provider = () => { providerCalled = true; return SaveState.Json(Bytes("from-provider")); };

            var input = new ReportInput { saveState = Bytes("explicit"), saveStateIsJson = true, includeSaveState = true };
            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: true, provider);

            Assert.IsFalse(providerCalled, "Provider must not run when an explicit save state is supplied.");
            Assert.AreEqual(Bytes("explicit"), result.bytes);
            Assert.IsTrue(result.isJson);
        }

        [Test]
        public void Resolve_ExplicitSaveState_UsedEvenWhenInclusionDisabledAndNoProvider()
        {
            var input = new ReportInput { saveState = Bytes("explicit"), includeSaveState = false };
            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: false, provider: null);

            Assert.AreEqual(Bytes("explicit"), result.bytes);
            Assert.IsFalse(result.isJson);
        }

        // --- Resolve: provider invocation gated by inclusion ---

        [Test]
        public void Resolve_ProviderInvoked_WhenIncludeTrue()
        {
            SaveStateProvider provider = () => SaveState.Json(Bytes("save"));
            var input = new ReportInput { includeSaveState = true };

            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: false, provider);

            Assert.AreEqual(Bytes("save"), result.bytes);
            Assert.IsTrue(result.isJson);
        }

        [Test]
        public void Resolve_ProviderSkipped_WhenIncludeFalse()
        {
            bool providerCalled = false;
            SaveStateProvider provider = () => { providerCalled = true; return SaveState.Binary(Bytes("x")); };
            var input = new ReportInput { includeSaveState = false };

            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: true, provider);

            Assert.IsFalse(providerCalled);
            Assert.IsFalse(result.HasData);
        }

        [Test]
        public void Resolve_NoProvider_ReturnsNone_EvenWhenIncludeTrue()
        {
            var input = new ReportInput { includeSaveState = true };

            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: true, provider: null);

            Assert.IsFalse(result.HasData);
        }

        // --- Resolve: include defaulting (null defers to config default) ---

        [Test]
        public void Resolve_IncludeNull_UsesConfigDefaultTrue()
        {
            SaveStateProvider provider = () => SaveState.Binary(Bytes("save"));
            var input = new ReportInput { includeSaveState = null };

            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: true, provider);

            Assert.AreEqual(Bytes("save"), result.bytes);
        }

        [Test]
        public void Resolve_IncludeNull_UsesConfigDefaultFalse()
        {
            bool providerCalled = false;
            SaveStateProvider provider = () => { providerCalled = true; return SaveState.Binary(Bytes("save")); };
            var input = new ReportInput { includeSaveState = null };

            SaveState result = SaveStateResolver.Resolve(input, includeByDefault: false, provider);

            Assert.IsFalse(providerCalled);
            Assert.IsFalse(result.HasData);
        }

        [Test]
        public void Resolve_PerReportInclude_OverridesConfigDefault()
        {
            bool providerCalled = false;
            SaveStateProvider provider = () => { providerCalled = true; return SaveState.Binary(Bytes("save")); };

            // Per-report explicit false beats a default-on config.
            SaveStateResolver.Resolve(new ReportInput { includeSaveState = false }, includeByDefault: true, provider);
            Assert.IsFalse(providerCalled, "Per-report includeSaveState=false should override config default true.");
        }

        // --- Resolve: provider failure degrades gracefully ---

        [Test]
        public void Resolve_ProviderThrows_ReturnsNoneAndReportsError()
        {
            Exception captured = null;
            SaveStateProvider provider = () => throw new InvalidOperationException("boom");
            var input = new ReportInput { includeSaveState = true };

            SaveState result = SaveStateResolver.Resolve(
                input, includeByDefault: true, provider, e => captured = e);

            Assert.IsFalse(result.HasData);
            Assert.IsInstanceOf<InvalidOperationException>(captured);
        }

        [Test]
        public void Resolve_ProviderThrows_WithoutOnError_DoesNotPropagate()
        {
            SaveStateProvider provider = () => throw new InvalidOperationException("boom");
            var input = new ReportInput { includeSaveState = true };

            Assert.DoesNotThrow(() =>
                SaveStateResolver.Resolve(input, includeByDefault: true, provider));
        }

        [Test]
        public void Resolve_NullInput_ReturnsNone()
        {
            SaveStateProvider provider = () => SaveState.Binary(Bytes("x"));
            Assert.IsFalse(SaveStateResolver.Resolve(null, includeByDefault: true, provider).HasData);
        }

        // --- Config default ---

        [Test]
        public void Config_IncludeSaveStateByDefault_IsOffByDefault()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<BugyardConfig>();
            Assert.IsFalse(config.includeSaveStateByDefault);
            UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
