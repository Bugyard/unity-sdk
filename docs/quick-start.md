# Quick start

This page takes you from a clean Unity project to one verified Bugyard report.

## 1. Install the package

In Unity, open **Window -> Package Manager -> + -> Add package from git URL...**
and paste:

```text
https://github.com/Bugyard/unity-sdk.git
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.bugyard.sdk": "https://github.com/Bugyard/unity-sdk.git"
  }
}
```

That short URL tracks the default branch and is useful while evaluating the SDK.
For repeatable CI or release builds, pin a commit hash or release tag instead:

```json
{
  "dependencies": {
    "com.bugyard.sdk": "https://github.com/Bugyard/unity-sdk.git#<commit-or-tag>"
  }
}
```

After Unity resolves the package, confirm that **Tools -> Bugyard** appears in
the Unity menu. Package Manager should also show a **Basic Usage** sample you can
import if you want a working bootstrap script.

## 2. Create configuration

1. Create a config asset with **Tools -> Bugyard -> Create Config Asset**.
2. Select the asset in the Project window.
3. Set:
    - `apiKey`: your project key, for example `by_pk_test_xxx`.
    - `endpoint`: your backend base URL, for example `https://api.bugyard.com`.
      Do not include `/v1`; the SDK appends it.

!!! warning "Do not commit live keys"
    The config asset is serialized into your Unity project. Commit a test key or
    an empty key, then inject live keys at runtime or keep live-key assets out of
    source control.

## 3. Initialize once

Create a bootstrap component and place it on a GameObject in your first scene:

```csharp
using UnityEngine;
using BugyardSDK;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private BugyardConfig config;

    void Awake() => Bugyard.Init(config);
}
```

Assign the config asset to the `config` field in the Inspector.

If your game polls raw `Input.GetKey(...)` or the new Input System directly, gate
that polling while the overlay is open:

```csharp
void Update()
{
    if (Bugyard.IsInputBlocked)
        return;

    // Your own gameplay input here.
}
```

## 4. Verify the backend connection

Run **Tools -> Bugyard -> Send Test Report** before sharing a build with testers.
It sends a synthetic report with the selected or discovered config asset.

On success, the dialog includes a dashboard link. On failure, it reports the
specific reason, such as an invalid endpoint, unauthorized API key, backend
validation error, or network failure.

## 5. File a real report

Enter play mode or run a build. Press **F8** to open the overlay, fill in the
form, and click **Send**.

The SDK captures the screenshot at the end of the frame after hiding the overlay,
so the screenshot shows the game instead of the form.

## Optional: no config asset

For a quick spike, initialize with values directly:

```csharp
Bugyard.Init("by_pk_test_xxx", "https://api.bugyard.com");
```

A config asset is recommended for real projects because it exposes capture
toggles, payload limits, hotkey behavior, and offline queue settings.

## Optional: trigger from your own UI

Open the built-in overlay:

```csharp
myReportButton.onClick.AddListener(Bugyard.Open);
```

Send a report without showing the overlay:

```csharp
Bugyard.Capture(new ReportInput
{
    title = "I got stuck behind the bridge",
    description = "Could not move after jumping near the bridge.",
    severity = Severity.High,
    category = "bug",
    reporter = new ReporterInfo { name = "QA Bot" },
});
```

Pass a callback to handle the result:

```csharp
var report = new ReportInput
{
    title = "I got stuck behind the bridge",
    severity = Severity.High,
};

Bugyard.Capture(report, result =>
{
    if (result.success)
        Debug.Log($"Filed {result.reportId}: {result.dashboardUrl}");
    else
        Debug.LogWarning($"Report failed: {result.message}");
});
```

See [API reference](api-reference.md) for every available field, including
`context`, `events`, `saveState`, and `memoryDump`.

## First-install troubleshooting

| Symptom | Check |
|---------|-------|
| **Tools -> Bugyard** is missing | Confirm Package Manager resolved `com.bugyard.sdk` and the Unity console has no compile errors. |
| **Send Test Report** says the endpoint is invalid | Use the backend base URL only, for example `https://api.bugyard.com`; do not include `/v1`. |
| Test report returns **401 Unauthorized** | Confirm `apiKey` is present and starts with `by_pk_test_` or `by_pk_live_`. |
| Pressing **F8** does nothing | Confirm `Bugyard.Init(config)` runs before pressing the hotkey and the selected config has the expected hotkey. |
| Typing in the overlay also controls the game | Gate raw input polling on `!Bugyard.IsInputBlocked`. |
| No screenshot or logs arrive | Check `captureScreenshot`, `captureLogs`, and the size caps in [Configuration](configuration.md). |

## Next steps

- Review [What gets sent](what-gets-sent.md) before enabling the SDK for wider
  testing.
- Tune payload and queue settings in [Configuration](configuration.md).
- Import the **Basic Usage** sample from Package Manager if you want a small
  reference scene script.
