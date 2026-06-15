# Bugyard Unity SDK

Bugyard Unity SDK lets playtesters file a rich bug report from inside a Unity
build. A tester presses the configured hotkey, fills in the overlay, and the SDK
uploads a screenshot, recent logs, scene/player metadata, build/runtime details,
and optional diagnostic context to your Bugyard backend.

> Status: alpha (0.1.x). The SDK is suitable for internal playtests and QA
> builds. Public APIs and UI may change between minor versions, and the built-in
> overlay is intentionally minimal.

## Requirements

- Unity **2021.3** or newer.
- Any **Active Input Handling** setting: legacy Input Manager, new Input System,
  or **Both**. If you do not use a Unity input backend, open the overlay yourself
  with `Bugyard.Open()`.
- A Bugyard backend endpoint and project API key. The default hosted endpoint is
  `https://api.bugyard.com`; self-hosters should use their own backend base URL.

## Install and first report

1. Add the package through Unity Package Manager with:

   ```text
   https://github.com/Bugyard/unity-sdk.git
   ```

2. Create a config asset with **Tools -> Bugyard -> Create Config Asset**.
3. Select the asset and set:
   - `apiKey`: your project key, for example `by_pk_test_xxx`.
   - `endpoint`: your backend base URL, for example `https://api.bugyard.com`.
     Do not include `/v1`; the SDK appends it.
4. Add a bootstrap component to a GameObject in your first scene:

   ```csharp
   using UnityEngine;
   using BugyardSDK;

   public class Bootstrap : MonoBehaviour
   {
       [SerializeField] private BugyardConfig config;

       void Awake() => Bugyard.Init(config);
   }
   ```

5. Assign the config asset in the Inspector.
6. Run **Tools -> Bugyard -> Send Test Report** to verify auth and endpoint
   connectivity.
7. Enter play mode or run a build, press **F8**, fill in the overlay, and click
   **Send**.

For reproducible CI or release builds, pin the package to a commit or release tag
instead of tracking the default branch.

## What you get

- Built-in report overlay opened by **F8** or `Bugyard.Open()`.
- Headless reporting through `Bugyard.Capture(...)` for custom UI, debug menus,
  and automated gameplay reports.
- Screenshot capture after the overlay hides itself, so the image shows the game.
- Recent Unity console logs with a bounded ring buffer.
- Automatic metadata: scene name, player position, build version, Unity version,
  SDK version, device specs, locale, timezone, and estimated FPS.
- Optional reporter identity, free-form `context`, `events.json`, `save_state`,
  and `memory_dump.gz` attachments for deeper diagnostics.
- Client-side payload limits, retry handling, idempotent `clientReportId`s, and an
  offline queue for transient failures.

## Programmatic use

Open the overlay from your own UI:

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

## Configuration highlights

`BugyardConfig` controls:

- connection: `apiKey`, `endpoint`, `environment`,
- overlay/input: `hotkey`, `pauseWhileOpen`, `blockGameplayInput`,
- capture: `captureScreenshot`, `captureLogs`, `maxLogLines`,
- defaults: `defaultCategory`,
- payload caps: `maxScreenshotBytes`, `maxLogBytes`, `maxMetadataBytes`,
  `maxContextBytes`, `maxEventsBytes`, `maxSaveStateBytes`,
  `maxMemoryDumpBytes`, and
- offline queue: `enableOfflineQueue`, `maxQueuedReports`.

Keep live keys out of source control. Commit only test keys (`by_pk_test_*`) or
empty config assets, then inject live keys at runtime or keep live-key assets out
of the repo.

## Payload

The SDK sends multipart `POST {endpoint}/v1/reports`:

- `metadata` JSON with report body, environment, build/runtime data,
  scene/player metadata, device info, optional reporter, optional context, and
  `clientReportId`,
- optional `screenshot` PNG,
- optional `logs` text,
- optional `events` JSON,
- optional `save_state`, and
- optional `memory_dump.gz`.

The offline queue persists metadata, screenshot, and logs for transient failures.
Large diagnostic blobs (`events`, `save_state`, `memory_dump`) are not persisted
for queued replay.

## Production readiness

- Pin the package for reproducible builds.
- Run **Tools -> Bugyard -> Send Test Report** for each environment.
- Review screenshots, logs, context, and attachments before enabling the SDK
  beyond a small internal test.
- Gate `Bugyard.Init(...)` to the builds where you want report capture enabled.
- If your game polls raw input directly, gate that code on
  `!Bugyard.IsInputBlocked` while the overlay is open.

## Limitations (0.1.x)

- Built-in overlay is minimal IMGUI and not themeable yet.
- Advanced diagnostics are code-driven through `Bugyard.Capture(...)`; the
  overlay does not collect them.
- Public APIs and UI may change between minor versions.
