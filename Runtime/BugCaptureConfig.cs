using UnityEngine;

namespace BugCaptureSDK
{
    /// <summary>
    /// Configuration asset for the BugCapture SDK. Create one via
    /// <c>Tools &gt; BugCapture &gt; Create Config Asset</c> (or
    /// <c>Assets &gt; Create &gt; BugCapture &gt; Config</c>) and pass it to
    /// <see cref="BugCapture.Init(BugCaptureConfig)"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "BugCaptureConfig", menuName = "BugCapture/Config")]
    public class BugCaptureConfig : ScriptableObject
    {
        [Tooltip("Project API key, e.g. bc_pk_test_xxx. Do NOT commit production keys to source control.")]
        public string apiKey = "";

        [Tooltip("Base URL of the BugCapture backend (no trailing /v1).")]
        public string endpoint = "https://api.bugcapture.dev";

        [Tooltip("Environment label sent with every report (e.g. development, staging, production).")]
        public string environment = "development";

        [Tooltip("Hotkey that opens the in-game report overlay. Works with the legacy Input Manager, the new Input System, or Both (Active Input Handling in Player Settings).")]
        public KeyCode hotkey = KeyCode.F8;

        [Tooltip("Capture a screenshot when a report is created.")]
        public bool captureScreenshot = true;

        [Tooltip("Attach recent Unity console logs as a text file.")]
        public bool captureLogs = true;

        [Tooltip("Max number of recent log lines kept in the ring buffer.")]
        public int maxLogLines = 500;

        [Header("Overlay behaviour")]
        [Tooltip("Pause the game (Time.timeScale = 0) while the report overlay is open, restoring the original scale on close. Off by default so existing pause logic isn't disturbed.")]
        public bool pauseWhileOpen = false;

        [Tooltip("Block gameplay input while the overlay is open so text typed into the form doesn't reach game controls. Neutralizes legacy input axes/buttons each frame and exposes BugCapture.IsInputBlocked for cooperative gating in your own input code.")]
        public bool blockGameplayInput = true;

        [Header("Defaults")]
        [Tooltip("Category applied to reports that don't specify one (e.g. bug, crash, feedback).")]
        public string defaultCategory = "bug";

        [Header("Client-side size caps")]
        [Tooltip("Max screenshot size in bytes. Oversized screenshots are downscaled or dropped before upload. Default 5 MB.")]
        public int maxScreenshotBytes = 5 * 1024 * 1024;

        [Tooltip("Max attached-log size in bytes. Older lines are trimmed to fit before upload. Default 2 MB.")]
        public int maxLogBytes = 2 * 1024 * 1024;

        [Tooltip("Max serialized metadata size in bytes. Default 256 KB.")]
        public int maxMetadataBytes = 256 * 1024;

        [Header("Offline queue")]
        [Tooltip("Persist reports that fail to upload (e.g. while offline) to disk and retry them on the next launch. The stable clientReportId keeps cross-session retries idempotent, so no duplicate report is created.")]
        public bool enableOfflineQueue = true;

        [Tooltip("Max number of failed reports kept on disk. When full, the oldest is dropped. Each queued report can be up to ~7 MB (a 5 MB screenshot plus logs/metadata), so size this for your disk budget.")]
        public int maxQueuedReports = 50;
    }
}
