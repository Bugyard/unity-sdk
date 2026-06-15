using UnityEngine;
using BugyardSDK;

/// <summary>
/// Drop this on a GameObject in your first scene and assign a BugyardConfig asset.
/// Press the configured hotkey (default F8) in play mode to open the report overlay.
/// </summary>
public class BugyardBootstrap : MonoBehaviour
{
    [SerializeField] private BugyardConfig config;

    void Awake()
    {
        Bugyard.Init(config);
    }

    void Update()
    {
        // The SDK neutralizes legacy Input Manager axes/buttons while the overlay is open,
        // but it can't intercept raw Input.GetKey(...) (or new Input System) polling from
        // your scripts. Gate that input on Bugyard.IsInputBlocked so keys typed into the
        // report form don't also drive the game.
        if (Bugyard.IsInputBlocked) return;

        // ... your own per-frame input handling here ...
    }

    // Example: open the built-in overlay from your own UI (e.g. a "Report a bug"
    // button), instead of relying on the hotkey. Wire this to Button.onClick.
    public void OpenReportOverlay()
    {
        Bugyard.Open();
    }

    // Example: file a report straight from gameplay code, bypassing the overlay.
    // Works with no overlay present; the optional callback reports the outcome.
    public void ReportStuckPlayer()
    {
        Bugyard.Capture(
            new ReportInput
            {
                title = "Player stuck",
                description = "Auto-filed from gameplay code.",
                severity = Severity.High,
            },
            result =>
            {
                if (result.success)
                    Debug.Log($"[Bugyard] Filed report {result.reportId}.");
                else
                    Debug.LogWarning($"[Bugyard] Report failed: {result.message}");
            });
    }
}
