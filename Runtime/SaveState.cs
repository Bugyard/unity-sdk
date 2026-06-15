using System;

namespace BugyardSDK
{
    /// <summary>
    /// Callback that produces the current game save / state blob on demand. Register one with
    /// <see cref="Bugyard.RegisterSaveStateProvider"/>; the SDK invokes it on the main thread while
    /// capturing a report (when save-state inclusion is enabled) and uploads the result as the
    /// <c>save_state</c> attachment. Return <see cref="SaveState.None"/> when there is nothing to send.
    /// </summary>
    public delegate SaveState SaveStateProvider();

    /// <summary>
    /// A save / game-state blob returned by a <see cref="SaveStateProvider"/>: the raw bytes and
    /// whether they are JSON (which selects the <c>save_state.json</c> / <c>application/json</c>
    /// upload over the raw <c>save_state.bin</c> / <c>application/octet-stream</c> one).
    /// </summary>
    public struct SaveState
    {
        public byte[] bytes;
        public bool isJson;

        public SaveState(byte[] bytes, bool isJson = false)
        {
            this.bytes = bytes;
            this.isJson = isJson;
        }

        /// <summary>A JSON save blob, uploaded as <c>save_state.json</c> (<c>application/json</c>).</summary>
        public static SaveState Json(byte[] bytes) => new SaveState(bytes, true);

        /// <summary>A raw/binary save blob, uploaded as <c>save_state.bin</c> (<c>application/octet-stream</c>).</summary>
        public static SaveState Binary(byte[] bytes) => new SaveState(bytes, false);

        /// <summary>Nothing to attach. Return this from a provider when no save exists yet.</summary>
        public static readonly SaveState None = default;

        /// <summary>True when there are bytes to upload.</summary>
        public bool HasData => bytes != null && bytes.Length > 0;
    }

    /// <summary>
    /// Decides which save-state bytes (if any) accompany a report, given the report input, the
    /// configured default, and the registered provider. Pure and side-effect free (the provider
    /// is the only thing invoked), so the precedence rules can be unit-tested without the runtime.
    /// </summary>
    public static class SaveStateResolver
    {
        /// <summary>
        /// Resolve the save state for a report. Precedence:
        /// <list type="number">
        /// <item>An explicit <see cref="ReportInput.saveState"/> always wins (caller passthrough),
        /// even when no provider is registered or inclusion is disabled.</item>
        /// <item>Otherwise, when inclusion is enabled for this report and a provider is registered,
        /// the provider is invoked and its result used.</item>
        /// <item>Otherwise nothing is attached (<see cref="SaveState.None"/>).</item>
        /// </list>
        /// Inclusion is <see cref="ReportInput.includeSaveState"/> when set, else
        /// <paramref name="includeByDefault"/>. A provider that throws is reported via
        /// <paramref name="onError"/> and treated as <see cref="SaveState.None"/> so a broken
        /// provider degrades to a report without save state rather than failing the whole capture.
        /// </summary>
        public static SaveState Resolve(
            ReportInput input,
            bool includeByDefault,
            SaveStateProvider provider,
            Action<Exception> onError = null)
        {
            if (input == null) return SaveState.None;

            // Explicit passthrough always wins, regardless of inclusion or provider.
            if (input.saveState != null)
                return new SaveState(input.saveState, input.saveStateIsJson);

            bool include = input.includeSaveState ?? includeByDefault;
            if (!include || provider == null)
                return SaveState.None;

            try
            {
                return provider();
            }
            catch (Exception e)
            {
                onError?.Invoke(e);
                return SaveState.None;
            }
        }
    }
}
