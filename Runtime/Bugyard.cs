using System;
using UnityEngine;

namespace BugyardSDK
{
    /// <summary>
    /// Public entry point for the Bugyard Unity SDK.
    /// Call <see cref="Init(BugyardConfig)"/> once at startup, then either let the
    /// configured hotkey open the overlay, or trigger reports yourself via
    /// <see cref="Open"/> / <see cref="Capture"/>.
    /// </summary>
    public static class Bugyard
    {
        static BugyardRuntime _runtime;

        public static bool IsInitialized => _runtime != null;

        /// <summary>True while the report overlay is open (including the brief frame it hides
        /// itself to grab the screenshot).</summary>
        public static bool IsOverlayOpen => _runtime != null && _runtime.SessionActive;

        /// <summary>
        /// True while an open overlay is swallowing gameplay input (config
        /// <c>blockGameplayInput</c>). Legacy Input Manager axes/buttons are neutralized
        /// automatically; gate any raw <c>Input.GetKey(...)</c> / new Input System polling in
        /// your own code on this flag so typing into the form doesn't drive the game.
        /// </summary>
        public static bool IsInputBlocked => _runtime != null && _runtime.InputBlocked;

        /// <summary>Initialize the SDK with a configuration asset.</summary>
        public static void Init(BugyardConfig config)
        {
            if (config == null)
            {
                Debug.LogError("[Bugyard] Init called with a null config.");
                return;
            }
            if (string.IsNullOrEmpty(config.apiKey))
            {
                Debug.LogWarning("[Bugyard] Init called with an empty apiKey; reports will be rejected (401).");
            }
            if (_runtime != null)
            {
                Debug.LogWarning("[Bugyard] Already initialized; ignoring duplicate Init.");
                return;
            }

            var go = new GameObject("Bugyard");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _runtime = go.AddComponent<BugyardRuntime>();
            _runtime.Configure(config);

            Debug.Log($"[Bugyard] Initialized (env={config.environment}, endpoint={config.endpoint}).");
        }

        /// <summary>Convenience initializer for quick prototyping.</summary>
        public static void Init(string apiKey, string endpoint = null)
        {
            var cfg = ScriptableObject.CreateInstance<BugyardConfig>();
            cfg.apiKey = apiKey;
            if (!string.IsNullOrEmpty(endpoint)) cfg.endpoint = endpoint;
            Init(cfg);
        }

        /// <summary>
        /// Tear down the SDK: unhook the log handler and destroy the runtime.
        /// Safe to call when not initialized. After Shutdown a later <see cref="Init(BugyardConfig)"/>
        /// starts fresh. Primarily useful for tests and re-initialization.
        /// </summary>
        public static void Shutdown()
        {
            if (_runtime == null) return;

            var go = _runtime.gameObject;
            _runtime.Teardown();
            _runtime = null;

            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        /// <summary>
        /// Open the in-game report overlay. Wire this to your own button or debug menu to
        /// trigger the form without the configured hotkey. No-op if the overlay is already open.
        /// </summary>
        public static void Open()
        {
            if (!EnsureReady()) return;
            _runtime.OpenOverlay();
        }

        /// <summary>
        /// Capture and send a report directly, bypassing the overlay UI. Works headless with
        /// no overlay present (e.g. auto-filing from gameplay code or a one-click reporter).
        /// The optional <paramref name="onResult"/> callback receives the typed
        /// <see cref="SendResult"/> (success + reportId/dashboardUrl, or failure + reason)
        /// when the upload finishes.
        /// </summary>
        public static void Capture(ReportInput report, Action<SendResult> onResult = null)
        {
            if (!EnsureReady()) return;
            _runtime.CaptureAndSend(report, onResult);
        }

        /// <summary>
        /// Set a persistent game-context value (inventory, quest flags, checkpoint id, match id,
        /// wave number, boss phase — whatever helps reproduce a bug). The current context is merged
        /// into every report's <c>metadata.context</c>; per-report context passed to
        /// <see cref="Capture"/> overrides matching keys. Values may nest (dictionaries/lists/
        /// primitives) and are bounded by <see cref="BugyardConfig.maxContextBytes"/>. Safe to call
        /// from any thread.
        /// </summary>
        public static void SetContext(string key, object value)
        {
            if (!EnsureReadyForState()) return;
            _runtime.SetContext(key, value);
        }

        /// <summary>Remove a persistent context key set via <see cref="SetContext"/>. No-op if absent.</summary>
        public static void RemoveContext(string key)
        {
            if (!EnsureReadyForState()) return;
            _runtime.RemoveContext(key);
        }

        /// <summary>Clear all persistent context set via <see cref="SetContext"/>.</summary>
        public static void ClearContext()
        {
            if (!EnsureReadyForState()) return;
            _runtime.ClearContext();
        }

        /// <summary>
        /// Record a gameplay breadcrumb (e.g. "StartedBossFight", "LoadedCheckpoint"). The most
        /// recent breadcrumbs (up to <see cref="BugyardConfig.maxBreadcrumbs"/>) are attached to the
        /// next report as <c>events.json</c>, giving a dev the sequence of actions that led to the
        /// bug. The optional <paramref name="payload"/> is serialized verbatim (any dictionary/list/
        /// primitive). Safe to call from any thread.
        /// </summary>
        public static void Track(string name, object payload = null)
        {
            if (!EnsureReadyForState()) return;
            _runtime.Track(name, payload);
        }

        static bool EnsureReady()
        {
            if (_runtime == null)
            {
                Debug.LogError("[Bugyard] Not initialized. Call Bugyard.Init(...) first.");
                return false;
            }
            return true;
        }

        static bool _warnedStateBeforeInit;

        // Quieter guard for the high-frequency state calls (SetContext/Track): warns once instead
        // of per-call so an early call (before Init) is surfaced without flooding the console.
        static bool EnsureReadyForState()
        {
            if (_runtime != null) return true;
            if (!_warnedStateBeforeInit)
            {
                _warnedStateBeforeInit = true;
                Debug.LogWarning("[Bugyard] SetContext/Track called before Init; value ignored. Call Bugyard.Init(...) first.");
            }
            return false;
        }
    }
}
