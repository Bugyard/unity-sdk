using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BugyardSDK
{
    /// <summary>
    /// Internal driver: listens for the hotkey, buffers logs, renders the overlay form,
    /// captures a screenshot, and hands everything to <see cref="BugyardClient"/>.
    /// Created and owned by <see cref="Bugyard"/>; not meant to be added manually.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class BugyardRuntime : MonoBehaviour
    {
        BugyardConfig _config;
        BugyardClient _client;

        readonly Queue<string> _logs = new Queue<string>();
        readonly object _logLock = new object();

        // SDK-wide persistent game context (Bugyard.SetContext) merged into every report's
        // metadata.context, and the recent-breadcrumbs buffer (Bugyard.Track) captured as the
        // events.json attachment. Both are locked so they can be updated from any thread.
        readonly Dictionary<string, object> _context = new Dictionary<string, object>();
        readonly object _contextLock = new object();
        BreadcrumbBuffer _breadcrumbs;

        // Optional caller-registered producer of the save_state attachment
        // (Bugyard.RegisterSaveStateProvider). Invoked during capture when save-state inclusion is
        // enabled for the report. Null when unset; gates the overlay's "Include save state" checkbox.
        SaveStateProvider _saveStateProvider;

        // Named caller-registered producers of custom diagnostic files
        // (Bugyard.RegisterDiagnosticFileProvider). Each is invoked during capture when diagnostic-
        // snapshot inclusion is enabled and its bytes stored as custom/<name> in diagnostic_snapshot.zip.
        // Locked so registration from any thread is safe against the main-thread capture snapshot.
        readonly Dictionary<string, DiagnosticFileProvider> _diagnosticFileProviders =
            new Dictionary<string, DiagnosticFileProvider>();
        readonly object _diagLock = new object();

        // Runtime-metric recorders (memory + render counters) sampled into runtime_metrics.json when a
        // diagnostic snapshot is built. Created in Configure, disposed in Teardown. Counters that don't
        // exist on a given platform/build are skipped, so a release build simply contributes fewer.
        readonly List<KeyValuePair<string, ProfilerRecorder>> _recorders =
            new List<KeyValuePair<string, ProfilerRecorder>>();

        // Overlay category choices. Lowercase so the selected value is the backend wire
        // value directly (see ReportBody.category); labels are capitalized for display only.
        static readonly string[] Categories = { "bug", "crash", "feedback" };

        // Backend field caps (exceeding them is rejected with REQUEST_NOT_VALID). Enforced
        // in the form via the TextField/TextArea maxLength overloads so over-length input
        // can't be entered, rather than letting the backend reject the report.
        const int MaxTitleLength = 200;
        const int MaxDescriptionLength = 5000;

        // _overlayOpen is the form's visual visibility; it flips off briefly while the
        // screenshot is taken. _sessionActive spans the whole open..close lifecycle
        // (including that transient hide) and gates the pause / input-block behaviour so
        // those don't flicker mid-send.
        bool _overlayOpen;
        bool _sessionActive;
        bool _sending;

        // Saved Time.timeScale while pauseWhileOpen holds the game at 0, restored verbatim
        // on close so we never clobber a scale the game set for its own reasons.
        float _savedTimeScale;
        bool _timeScaleSaved;

        // Outcome of the most recent overlay Send. While success, the overlay shows a
        // confirmation view; while failure, the form stays open with an inline error banner.
        // Null means no send has completed since the form was last opened/reset.
        SendResult _result;

        string _title = "";
        string _description = "";
        string _expectedResult = "";
        Severity _severity = Severity.Medium;
        int _categoryIndex;

        // Overlay "Include save state" checkbox state; only shown/used when a provider is
        // registered. Seeded from config.includeSaveStateByDefault and reset with the form.
        bool _includeSaveState;

        // Overlay "Include diagnostic snapshot" checkbox state. Seeded from
        // config.includeDiagnosticSnapshotByDefault and reset with the form. Always shown (runtime
        // metrics are available without any provider); custom files appear only if providers exist.
        bool _includeDiagnosticSnapshot;

        // Lazily built in OnGUI (GUI.skin is only valid during a GUI pass).
        GUIStyle _counterStyle;
        GUIStyle _counterWarnStyle;
        GUIStyle _hintStyle;
        GUIStyle _errorStyle;
        GUIStyle _successStyle;
        GUIStyle _titleStyle;

        public void Configure(BugyardConfig config)
        {
            _config = config;
            _client = new BugyardClient(config);
            _breadcrumbs = new BreadcrumbBuffer(config.maxBreadcrumbs);
            _categoryIndex = DefaultCategoryIndex();
            _includeSaveState = config.includeSaveStateByDefault;
            _includeDiagnosticSnapshot = config.includeDiagnosticSnapshotByDefault;

            StartRecorders();

            if (config.captureLogs)
            {
                Application.logMessageReceivedThreaded += OnLog;
            }

            // Retry any reports persisted from a previous session (e.g. submitted while offline).
            // FlushQueue is a no-op when the queue is disabled or empty.
            StartCoroutine(_client.FlushQueue());
        }

        // Index of config.defaultCategory in Categories, or 0 ("bug") when it isn't one
        // of the overlay choices.
        int DefaultCategoryIndex()
        {
            string def = _config != null ? _config.defaultCategory : null;
            for (int i = 0; i < Categories.Length; i++)
            {
                if (string.Equals(Categories[i], def, System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return 0;
        }

        bool _torndown;

        /// <summary>
        /// Synchronously unhook from engine callbacks and close the overlay.
        /// Idempotent. Called by <see cref="Bugyard.Shutdown"/> before the GameObject is
        /// destroyed so state is clean immediately, rather than at end of frame via OnDestroy.
        /// </summary>
        public void Teardown()
        {
            if (_torndown) return;
            _torndown = true;

            if (_config != null && _config.captureLogs)
            {
                Application.logMessageReceivedThreaded -= OnLog;
            }
            EndSession(); // restore time scale if we were paused
            _overlayOpen = false;
            _result = null;

            // Drop accumulated context/breadcrumbs and the registered providers so a later Init
            // starts from a clean slate, and release the profiler recorders.
            ClearContext();
            _breadcrumbs?.Clear();
            _saveStateProvider = null;
            lock (_diagLock) _diagnosticFileProviders.Clear();
            StopRecorders();
        }

        void OnDestroy()
        {
            Teardown();
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            string entry = $"[{type}] {condition}";

            // Attach the stack trace for error-severity entries so reports carry
            // enough context to locate the failure. Other types stay single-line to
            // keep the buffer readable. Each message is still one ring-buffer entry,
            // so maxLogLines continues to bound the number of captured messages.
            if (IsError(type) && !string.IsNullOrEmpty(stackTrace))
            {
                entry += "\n" + stackTrace.TrimEnd();
            }

            lock (_logLock)
            {
                _logs.Enqueue(entry);
                int max = Mathf.Max(1, _config.maxLogLines);
                while (_logs.Count > max) _logs.Dequeue();
            }
        }

        static bool IsError(LogType type) =>
            type == LogType.Error || type == LogType.Exception || type == LogType.Assert;

        void Update()
        {
            if (_config != null && !_overlayOpen && HotkeyPressed())
            {
                OpenOverlay();
            }

#if ENABLE_LEGACY_INPUT_MANAGER
            // This runtime has a very early execution order (-1000), so resetting here runs
            // before gameplay scripts poll input: axes report 0 and buttons report not-pressed
            // for the frame, so movement/action bound through the Input Manager won't react to
            // keys typed into the form. Raw Input.GetKey(...) polling can't be intercepted from
            // here; gate that on Bugyard.IsInputBlocked in your own code instead. The new
            // Input System has no equivalent global reset, so projects on it should likewise
            // gate their own actions on Bugyard.IsInputBlocked.
            if (InputBlocked)
            {
                Input.ResetInputAxes();
            }
#endif
        }

        // True on the frame the configured hotkey transitions to pressed, under whichever input
        // backend(s) the project enables. With "Both" Active Input Handling both branches compile;
        // the legacy check short-circuits first and OpenOverlay is idempotent, so the overlay still
        // opens exactly once. Returns false (no hotkey) when neither backend is active.
        bool HotkeyPressed()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(_config.hotkey)) return true;
#endif
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                Key key = ToInputSystemKey(_config.hotkey);
                if (key != Key.None && keyboard[key].wasPressedThisFrame) return true;
            }
#endif
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        // Translate the legacy KeyCode the hotkey is stored as (config.hotkey) into the new Input
        // System's Key enum. Covers letters, both digit rows, function keys and the common
        // editing/navigation/modifier keys — enough for any reasonable hotkey. Unmapped codes
        // return Key.None, which HotkeyPressed treats as "no new-input binding" rather than throwing.
        static Key ToInputSystemKey(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.A: return Key.A;
                case KeyCode.B: return Key.B;
                case KeyCode.C: return Key.C;
                case KeyCode.D: return Key.D;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.I: return Key.I;
                case KeyCode.J: return Key.J;
                case KeyCode.K: return Key.K;
                case KeyCode.L: return Key.L;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.O: return Key.O;
                case KeyCode.P: return Key.P;
                case KeyCode.Q: return Key.Q;
                case KeyCode.R: return Key.R;
                case KeyCode.S: return Key.S;
                case KeyCode.T: return Key.T;
                case KeyCode.U: return Key.U;
                case KeyCode.V: return Key.V;
                case KeyCode.W: return Key.W;
                case KeyCode.X: return Key.X;
                case KeyCode.Y: return Key.Y;
                case KeyCode.Z: return Key.Z;

                case KeyCode.Alpha0: return Key.Digit0;
                case KeyCode.Alpha1: return Key.Digit1;
                case KeyCode.Alpha2: return Key.Digit2;
                case KeyCode.Alpha3: return Key.Digit3;
                case KeyCode.Alpha4: return Key.Digit4;
                case KeyCode.Alpha5: return Key.Digit5;
                case KeyCode.Alpha6: return Key.Digit6;
                case KeyCode.Alpha7: return Key.Digit7;
                case KeyCode.Alpha8: return Key.Digit8;
                case KeyCode.Alpha9: return Key.Digit9;

                case KeyCode.Keypad0: return Key.Numpad0;
                case KeyCode.Keypad1: return Key.Numpad1;
                case KeyCode.Keypad2: return Key.Numpad2;
                case KeyCode.Keypad3: return Key.Numpad3;
                case KeyCode.Keypad4: return Key.Numpad4;
                case KeyCode.Keypad5: return Key.Numpad5;
                case KeyCode.Keypad6: return Key.Numpad6;
                case KeyCode.Keypad7: return Key.Numpad7;
                case KeyCode.Keypad8: return Key.Numpad8;
                case KeyCode.Keypad9: return Key.Numpad9;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;

                case KeyCode.F1: return Key.F1;
                case KeyCode.F2: return Key.F2;
                case KeyCode.F3: return Key.F3;
                case KeyCode.F4: return Key.F4;
                case KeyCode.F5: return Key.F5;
                case KeyCode.F6: return Key.F6;
                case KeyCode.F7: return Key.F7;
                case KeyCode.F8: return Key.F8;
                case KeyCode.F9: return Key.F9;
                case KeyCode.F10: return Key.F10;
                case KeyCode.F11: return Key.F11;
                case KeyCode.F12: return Key.F12;

                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.Delete: return Key.Delete;
                case KeyCode.Insert: return Key.Insert;
                case KeyCode.Home: return Key.Home;
                case KeyCode.End: return Key.End;
                case KeyCode.PageUp: return Key.PageUp;
                case KeyCode.PageDown: return Key.PageDown;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.RightAlt: return Key.RightAlt;
                case KeyCode.BackQuote: return Key.Backquote;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                case KeyCode.LeftBracket: return Key.LeftBracket;
                case KeyCode.RightBracket: return Key.RightBracket;
                case KeyCode.Semicolon: return Key.Semicolon;
                case KeyCode.Quote: return Key.Quote;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Slash: return Key.Slash;
                case KeyCode.Backslash: return Key.Backslash;

                default: return Key.None;
            }
        }
#endif

        /// <summary>True from the moment the overlay opens until it is fully closed,
        /// including the brief moment it hides itself to take the screenshot.</summary>
        public bool SessionActive => _sessionActive;

        /// <summary>True while an open overlay is configured to swallow gameplay input.</summary>
        public bool InputBlocked => _sessionActive && _config != null && _config.blockGameplayInput;

        public void OpenOverlay()
        {
            if (_overlayOpen) return;
            _overlayOpen = true;
            BeginSession();
        }

        // Start the overlay session: pause the game if configured, capturing the current
        // time scale so it can be restored exactly. Idempotent; the screenshot hide and any
        // double OpenOverlay won't re-save or re-pause.
        void BeginSession()
        {
            if (_sessionActive) return;
            _sessionActive = true;

            if (_config != null && _config.pauseWhileOpen)
            {
                _savedTimeScale = Time.timeScale;
                _timeScaleSaved = true;
                Time.timeScale = 0f;
            }
        }

        // End the overlay session: restore the original time scale. Idempotent so the
        // various close paths (cancel, success-close, teardown) can all call it safely.
        void EndSession()
        {
            if (!_sessionActive) return;
            _sessionActive = false;

            if (_timeScaleSaved)
            {
                Time.timeScale = _savedTimeScale;
                _timeScaleSaved = false;
            }
        }

        // Close the overlay and end its session in one step. Used by every user-facing
        // close path so pause/input-block state is always torn down with the form.
        void CloseOverlay()
        {
            _overlayOpen = false;
            EndSession();
        }

        // --- Persistent context & breadcrumbs (Bugyard.SetContext / Bugyard.Track) ---

        public void SetContext(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_contextLock) _context[key] = value;
        }

        public void RemoveContext(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_contextLock) _context.Remove(key);
        }

        public void ClearContext()
        {
            lock (_contextLock) _context.Clear();
        }

        public void Track(string name, object payload)
        {
            _breadcrumbs?.Add(name, payload);
        }

        // Store (or clear, with null) the save-state producer invoked during capture.
        public void RegisterSaveStateProvider(SaveStateProvider provider)
        {
            _saveStateProvider = provider;
        }

        // True when a save-state provider is registered; gates the overlay's "Include save state" checkbox.
        bool HasSaveStateProvider => _saveStateProvider != null;

        // Register (or remove, with a null provider) a named custom-diagnostic-file producer.
        public void RegisterDiagnosticFileProvider(string name, DiagnosticFileProvider provider)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (_diagLock)
            {
                if (provider == null) _diagnosticFileProviders.Remove(name);
                else _diagnosticFileProviders[name] = provider;
            }
        }

        // A stable copy of the registered providers so capture can invoke them without holding the
        // lock (a provider could itself register/unregister, or take a while).
        List<KeyValuePair<string, DiagnosticFileProvider>> DiagnosticProvidersSnapshot()
        {
            lock (_diagLock)
                return new List<KeyValuePair<string, DiagnosticFileProvider>>(_diagnosticFileProviders);
        }

        // --- Diagnostic snapshot (runtime metrics + custom provider files) ---

        // Start the ProfilerRecorders sampled into runtime_metrics.json. Counter values are 0/empty
        // until a frame elapses, which it always has by capture time. Memory counters are emitted on
        // all builds; the render counters are only populated in development builds / the editor.
        void StartRecorders()
        {
            TryAddRecorder("totalUsedMemoryBytes", ProfilerCategory.Memory, "Total Used Memory");
            TryAddRecorder("totalReservedMemoryBytes", ProfilerCategory.Memory, "Total Reserved Memory");
            TryAddRecorder("gcUsedMemoryBytes", ProfilerCategory.Memory, "GC Used Memory");
            TryAddRecorder("gcReservedMemoryBytes", ProfilerCategory.Memory, "GC Reserved Memory");
            TryAddRecorder("systemUsedMemoryBytes", ProfilerCategory.Memory, "System Used Memory");
            TryAddRecorder("drawCalls", ProfilerCategory.Render, "Draw Calls Count");
            TryAddRecorder("setPassCalls", ProfilerCategory.Render, "SetPass Calls Count");
            TryAddRecorder("triangles", ProfilerCategory.Render, "Triangles Count");
            TryAddRecorder("vertices", ProfilerCategory.Render, "Vertices Count");
        }

        void TryAddRecorder(string key, ProfilerCategory category, string statName)
        {
            try
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, statName);
                if (recorder.Valid) _recorders.Add(new KeyValuePair<string, ProfilerRecorder>(key, recorder));
                else recorder.Dispose();
            }
            catch (Exception)
            {
                // A counter/category that doesn't exist on this platform or Unity version — skip it
                // rather than letting a diagnostics nicety throw during Init.
            }
        }

        void StopRecorders()
        {
            foreach (KeyValuePair<string, ProfilerRecorder> kv in _recorders)
            {
                if (kv.Value.Valid) kv.Value.Dispose();
            }
            _recorders.Clear();
        }

        IReadOnlyDictionary<string, object> CollectRuntimeMetrics()
        {
            var metrics = new Dictionary<string, object>();
            float dt = Time.unscaledDeltaTime;
            metrics["fps"] = dt > 0f ? Mathf.RoundToInt(1f / dt) : 0;
            foreach (KeyValuePair<string, ProfilerRecorder> kv in _recorders)
            {
                if (kv.Value.Valid) metrics[kv.Key] = kv.Value.LastValue;
            }
            return metrics;
        }

        // Build the diagnostic_snapshot.zip for a report when inclusion is enabled, else null. The
        // caller handles an explicit input.diagnosticSnapshot passthrough (which wins outright); here
        // we assemble the manifest + runtime metrics + custom provider files. A provider that throws
        // is logged and skipped so one bad provider can't fail the whole capture.
        byte[] BuildDiagnosticSnapshot(ReportInput input)
        {
            bool include = input.includeDiagnosticSnapshot ?? _config.includeDiagnosticSnapshotByDefault;
            if (!include) return null;

            var customFiles = new List<KeyValuePair<string, byte[]>>();
            foreach (KeyValuePair<string, DiagnosticFileProvider> kv in DiagnosticProvidersSnapshot())
            {
                try
                {
                    byte[] bytes = kv.Value?.Invoke();
                    if (bytes != null && bytes.Length > 0)
                        customFiles.Add(new KeyValuePair<string, byte[]>(kv.Key, bytes));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Bugyard] Diagnostic file provider '{kv.Key}' threw; skipping it.\n{e}");
                }
            }

            try
            {
                return DiagnosticSnapshot.Build(BuildManifest(customFiles), CollectRuntimeMetrics(), customFiles);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Bugyard] Failed to build diagnostic snapshot; sending report without it.\n{e}");
                return null;
            }
        }

        // The manifest.json contents: identity (sdk/engine/build/env/platform), the active scene, the
        // capture time, and a listing of what the zip carries so a reader knows it without unzipping.
        IReadOnlyDictionary<string, object> BuildManifest(List<KeyValuePair<string, byte[]>> customFiles)
        {
            var contents = new List<object> { DiagnosticSnapshot.RuntimeMetricsEntry };
            foreach (KeyValuePair<string, byte[]> kv in customFiles)
                contents.Add(DiagnosticSnapshot.CustomPrefix + DiagnosticSnapshot.SanitizeCustomName(kv.Key));

            return new Dictionary<string, object>
            {
                ["sdkVersion"] = BugyardVersion.Value,
                ["engine"] = "unity",
                ["engineVersion"] = Application.unityVersion,
                ["buildVersion"] = Application.version,
                ["environment"] = _config.environment,
                ["platform"] = Application.platform.ToString(),
                ["scene"] = SceneManager.GetActiveScene().name,
                ["capturedAtUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ["contents"] = contents,
            };
        }

        // A copy of the persistent context (null when empty) so the capture coroutine reads a
        // stable snapshot without holding the lock while building/serializing metadata.
        IReadOnlyDictionary<string, object> ContextSnapshot()
        {
            lock (_contextLock)
            {
                if (_context.Count == 0) return null;
                return new Dictionary<string, object>(_context);
            }
        }

        public void CaptureAndSend(ReportInput input, Action<SendResult> onResult = null)
        {
            StartCoroutine(CaptureRoutine(input, onResult));
        }

        IEnumerator CaptureRoutine(ReportInput input, Action<SendResult> onResult)
        {
            byte[] screenshot = null;
            if (_config.captureScreenshot)
            {
                // The overlay is drawn from OnGUI while _overlayOpen is true, and callers
                // clear it before reaching here. Wait one full frame so a Repaint without
                // the overlay happens, then WaitForEndOfFrame so we read the back buffer
                // after everything (including any remaining GUI) has rendered.
                yield return null;
                yield return new WaitForEndOfFrame();
                screenshot = CaptureScreenshotPng();
            }

            // Resolve the save_state attachment: an explicit input.saveState wins, otherwise a
            // registered provider is invoked when inclusion is enabled for this report. A provider
            // that throws degrades to a report without save state rather than failing the capture.
            SaveState saveState = SaveStateResolver.Resolve(
                input, _config.includeSaveStateByDefault, _saveStateProvider,
                e => Debug.LogWarning($"[Bugyard] Save-state provider threw; sending report without save state.\n{e}"));

            ReportMetadata metadata = MetadataCollector.Build(_config, input, ContextSnapshot());
            var artifacts = new ReportArtifacts
            {
                screenshot = screenshot,
                logs = _config.captureLogs ? LogsSnapshot() : null,
                // events.json defaults to the recorded breadcrumbs (Bugyard.Track); a caller that
                // supplies its own events on the ReportInput overrides them. Save state is produced
                // by the registered provider (or passed through verbatim). The diagnostic snapshot is
                // built by the SDK (runtime metrics + registered provider files) when inclusion is
                // enabled, unless the caller supplies explicit bytes, which are uploaded verbatim.
                events = input.events ?? _breadcrumbs?.ToJsonBytes(),
                saveState = saveState.bytes,
                saveStateIsJson = saveState.isJson,
                diagnosticSnapshot = input.diagnosticSnapshot ?? BuildDiagnosticSnapshot(input),
            };

            SendResult sent = null;
            yield return _client.Send(metadata, artifacts, r => { sent = r; onResult?.Invoke(r); });

            // A successful send means we're back online; opportunistically drain any backlog
            // persisted from earlier offline sessions.
            if (sent != null && sent.success)
                StartCoroutine(_client.FlushQueue());
        }

        /// <summary>
        /// Capture the current frame of the main display as PNG bytes. Returns null if the
        /// frame could not be captured or encoded. The intermediate texture is always
        /// destroyed, even if encoding throws, so no GPU/CPU texture is leaked.
        /// </summary>
        byte[] CaptureScreenshotPng()
        {
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex == null)
                {
                    Debug.LogWarning("[Bugyard] Screenshot capture returned no texture; sending report without a screenshot.");
                    return null;
                }

                byte[] png = tex.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    Debug.LogWarning("[Bugyard] Screenshot PNG encoding produced no data; sending report without a screenshot.");
                    return null;
                }
                return png;
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
        }

        string LogsSnapshot()
        {
            var sb = new StringBuilder();
            lock (_logLock)
            {
                foreach (string line in _logs) sb.AppendLine(line);
            }
            return sb.ToString();
        }

        // --- Minimal IMGUI overlay (no UI package dependency) ---

        void OnGUI()
        {
            if (!_overlayOpen) return;
            EnsureStyles();

            // A delivered report and a report saved for offline retry both show the confirmation
            // view (the latter so the user isn't left re-submitting into a dead connection, which
            // would queue a second report with a fresh clientReportId). Only a non-queued failure
            // keeps the form open with an inline error banner.
            bool confirmationView = _result != null && (_result.success || _result.queuedForRetry);
            bool errorBanner = _result != null && !_result.success && !_result.queuedForRetry;

            const float w = 440f;
            float h = confirmationView ? (_result.success ? 210f : 240f) : (errorBanner ? 510f : 470f);
            // The "Include save state" checkbox only appears on the form (not the confirmation view)
            // and only when a provider is registered; reserve a row for it so nothing is clipped.
            if (!confirmationView && HasSaveStateProvider) h += 28f;
            // The "Include diagnostic snapshot" checkbox and its caution line always appear on the form.
            if (!confirmationView) h += 28f + 18f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 12, rect.width - 24, rect.height - 24));
            if (confirmationView)
                DrawConfirmation();
            else
                DrawForm(errorBanner);
            GUILayout.EndArea();
        }

        // Confirmation shown after a Send that was delivered, or saved for offline retry. The form
        // fields were already reset, so closing this view leaves a clean form for the next report.
        void DrawConfirmation()
        {
            GUILayout.Label(_result.success ? "Report sent" : "Report saved", _titleStyle);
            GUILayout.Space(8);
            GUILayout.Label(_result.message, _successStyle);

            if (!string.IsNullOrEmpty(_result.reportId))
            {
                GUILayout.Space(6);
                GUILayout.Label("Report ID: " + _result.reportId);
            }

            if (!string.IsNullOrEmpty(_result.dashboardUrl))
            {
                GUILayout.Space(6);
                if (GUILayout.Button("Open in dashboard"))
                    Application.OpenURL(_result.dashboardUrl);
            }

            GUILayout.Space(12);
            if (GUILayout.Button("Close"))
            {
                CloseOverlay();
                _result = null;
            }
        }

        void DrawForm(bool errorBanner)
        {
            GUILayout.Label("Report a bug", _titleStyle);
            GUILayout.Space(6);

            // A failed Send leaves the form populated so the user can fix and retry; the
            // reason is shown here rather than only in the console.
            if (errorBanner)
            {
                GUILayout.Label("Couldn't send: " + _result.message, _errorStyle);
                GUILayout.Space(6);
            }

            // maxLength on the field prevents over-length input (and pasting past the cap),
            // and the inline counter shows the limit before it's reached.
            LabelWithCounter("Title", _title.Length, MaxTitleLength);
            _title = GUILayout.TextField(_title, MaxTitleLength);
            if (string.IsNullOrWhiteSpace(_title))
                GUILayout.Label("Title is required.", _hintStyle);
            GUILayout.Space(4);

            LabelWithCounter("What happened?", _description.Length, MaxDescriptionLength);
            _description = GUILayout.TextArea(_description, MaxDescriptionLength, GUILayout.Height(70));
            GUILayout.Space(4);

            GUILayout.Label("What did you expect to happen?");
            _expectedResult = GUILayout.TextArea(_expectedResult, GUILayout.Height(50));
            GUILayout.Space(6);

            GUILayout.Label("Category");
            _categoryIndex = GUILayout.Toolbar(_categoryIndex, new[] { "Bug", "Crash", "Feedback" });
            GUILayout.Space(6);

            GUILayout.Label("Severity");
            _severity = (Severity)GUILayout.Toolbar(
                (int)_severity, new[] { "Low", "Medium", "High", "Critical" });
            GUILayout.Space(12);

            // Only offer the save-state toggle when the game registered a provider to produce one.
            if (HasSaveStateProvider)
            {
                _includeSaveState = GUILayout.Toggle(_includeSaveState, " Include save state");
                GUILayout.Space(12);
            }

            // The diagnostic snapshot (runtime metrics + any registered custom files) is always
            // offerable; the caution mirrors that custom providers may add game/diagnostic data.
            _includeDiagnosticSnapshot = GUILayout.Toggle(_includeDiagnosticSnapshot, " Include diagnostic snapshot");
            GUILayout.Label("May include game state or custom diagnostic data.", _hintStyle);
            GUILayout.Space(12);

            GUILayout.BeginHorizontal();
            GUI.enabled = !_sending && !string.IsNullOrWhiteSpace(_title);
            if (GUILayout.Button(_sending ? "Sending..." : "Send"))
            {
                Submit();
            }
            GUI.enabled = !_sending;
            if (GUILayout.Button("Cancel"))
            {
                CloseOverlay();
                ResetForm();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // A field label with a right-aligned "used/max" character counter. The counter turns
        // to a warning colour once the cap is reached so the limit is obvious inline.
        void LabelWithCounter(string label, int length, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{length}/{max}", length >= max ? _counterWarnStyle : _counterStyle);
            GUILayout.EndHorizontal();
        }

        void EnsureStyles()
        {
            if (_counterStyle != null) return;

            _counterStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight };
            _counterWarnStyle = new GUIStyle(_counterStyle);
            _counterWarnStyle.normal.textColor = Color.yellow;
            _hintStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic };
            _hintStyle.normal.textColor = new Color(1f, 0.7f, 0.3f);

            _errorStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Bold };
            _errorStyle.normal.textColor = new Color(1f, 0.45f, 0.4f);
            _successStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            _successStyle.normal.textColor = new Color(0.5f, 0.9f, 0.55f);
            _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }

        void Submit()
        {
            var input = new ReportInput
            {
                title = _title,
                description = _description,
                expectedResult = _expectedResult,
                severity = _severity,
                category = Categories[_categoryIndex],
                // The checkbox is the user's explicit choice for this report, so it overrides the
                // config default. When no provider is registered the resolver ignores it anyway.
                includeSaveState = _includeSaveState,
                includeDiagnosticSnapshot = _includeDiagnosticSnapshot,
            };
            _sending = true;
            _result = null; // clear any prior failure banner while this send is in flight
            StartCoroutine(SubmitRoutine(input));
        }

        IEnumerator SubmitRoutine(ReportInput input)
        {
            _overlayOpen = false; // hide the overlay before the screenshot is taken
            SendResult result = null;
            yield return CaptureRoutine(input, r => result = r);
            _sending = false;
            _result = result;
            _overlayOpen = true; // re-show the overlay to report the outcome

            // When the report was delivered or saved for offline retry, the form is cleared (the
            // confirmation is shown until the user closes it); on a non-queued failure the entered
            // text is kept so the user can fix and retry.
            if (result != null && (result.success || result.queuedForRetry))
                ResetForm(keepResult: true);
        }

        void ResetForm(bool keepResult = false)
        {
            _title = "";
            _description = "";
            _expectedResult = "";
            _severity = Severity.Medium;
            _categoryIndex = DefaultCategoryIndex();
            _includeSaveState = _config != null && _config.includeSaveStateByDefault;
            _includeDiagnosticSnapshot = _config != null && _config.includeDiagnosticSnapshotByDefault;
            if (!keepResult) _result = null;
        }
    }
}
