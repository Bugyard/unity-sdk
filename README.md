# BugCapture Unity SDK

Capture screenshots, logs, scene name, player position, build version and device
info from your Unity playtests and send them to your
[BugCapture](https://github.com/bugcapture/bugcapture) dashboard — with one hotkey.

> **Status: alpha (0.1.x).** API may change between minor versions. Intended for
> private alpha testing, not production.

## Install

In Unity: **Window → Package Manager → + → Add package from git URL…**

```
https://github.com/bugcapture/bugcapture-unity.git
```

To pin a version, append a tag:

```
https://github.com/bugcapture/bugcapture-unity.git#v0.1.0
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.bugcapture.sdk": "https://github.com/bugcapture/bugcapture-unity.git#v0.1.0"
  }
}
```

## Quick start

1. Create a config asset: **Tools → BugCapture → Create Config Asset**.
2. Select it and set:
   - `apiKey` — your project key (e.g. `bc_pk_test_xxx`). Create it in the
     [BugCapture dashboard](https://github.com/bugcapture/bugcapture#readme)
     (Project → Settings → API keys).
   - `endpoint` — your backend base URL (no trailing `/v1`). The default
     `https://api.bugcapture.dev` points at the hosted backend; self-hosters set
     their own. See the [backend setup guide](https://github.com/bugcapture/bugcapture#readme).
3. Initialize once at startup and let the hotkey do the rest:

```csharp
using UnityEngine;
using BugCaptureSDK;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private BugCaptureConfig config;

    void Awake() => BugCapture.Init(config);
}
```

Verify your config talks to the backend before shipping: **Tools → BugCapture →
Send Test Report** uploads a synthetic report with the current settings and reports
success (with a dashboard link) or the precise failure reason.

Press **F8** in play mode to open the report overlay. Fill it in, hit **Send**.

### Without a config asset (prototyping)

```csharp
BugCapture.Init("bc_pk_test_xxx", "https://api.bugcapture.dev");
```

### Programmatic triggers (custom UI / automation)

You don't have to rely on the hotkey. Once `Init` has run you can drive the SDK
from your own button, debug menu, or gameplay code.

**Open the overlay** — e.g. wired to a "Report a bug" button:

```csharp
// Show the same form the hotkey opens; the user fills it in and hits Send.
myReportButton.onClick.AddListener(BugCapture.Open);
```

**Send a report headless** — no overlay involved, useful for auto-filing or a
one-click reporter. Works whether or not the overlay is open:

```csharp
BugCapture.Capture(new ReportInput
{
    title = "I got stuck behind the bridge",
    description = "Could not move after jumping near the bridge.",
    severity = Severity.High,
    category = "bug",                       // optional; defaults to config.defaultCategory
    reporter = new ReporterInfo { name = "QA Bot" },  // optional
});
```

Pass a callback to learn the outcome (success carries `reportId`/`dashboardUrl`,
failure a friendly `message`):

```csharp
BugCapture.Capture(report, result =>
{
    if (result.success)
        Debug.Log($"Filed {result.reportId}: {result.dashboardUrl}");
    else
        Debug.LogWarning($"Report failed: {result.message}");
});
```

Both work under any input backend (or none) — see `BugCapture.IsInitialized`
and `BugCapture.IsOverlayOpen` if you need to gate your UI.

## API reference

Everything lives in the `BugCaptureSDK` namespace. The whole public surface is the
static `BugCapture` class plus a few plain data types.

### `BugCapture`

| Member | Signature | Description |
|--------|-----------|-------------|
| `Init` | `void Init(BugCaptureConfig config)` | Initialize once at startup with a config asset. No-op (warns) if already initialized; logs an error on a null config. |
| `Init` | `void Init(string apiKey, string endpoint = null)` | Convenience initializer for prototyping — builds a config in memory. `endpoint` defaults to `https://api.bugcapture.dev`. |
| `Open` | `void Open()` | Open the report overlay (the same form the hotkey opens). No-op if already open. Logs an error if not initialized. |
| `Capture` | `void Capture(ReportInput report, Action<SendResult> onResult = null)` | Capture and send a report headless, bypassing the overlay. The optional callback receives the typed `SendResult` when the upload finishes. |
| `Shutdown` | `void Shutdown()` | Tear down the SDK: unhook the log handler and destroy the runtime. Safe to call when not initialized; a later `Init` starts fresh. Mainly for tests and re-initialization. |
| `IsInitialized` | `bool` (get) | True once `Init` has run and before `Shutdown`. |
| `IsOverlayOpen` | `bool` (get) | True while the report overlay is open (including the brief frame it hides itself to grab the screenshot). |
| `IsInputBlocked` | `bool` (get) | True while an open overlay is swallowing gameplay input (config `blockGameplayInput`). Gate your own raw `Input.GetKey(...)` / Input System polling on this so typing into the form doesn't drive the game. |

### `ReportInput`

Caller-supplied content for `Capture`. Only `title` is really needed; everything
else is optional.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `title` | `string` | — | Short summary of the report. |
| `description` | `string` | — | Longer details / repro steps. |
| `expectedResult` | `string` | — | What the tester expected to happen. |
| `severity` | `Severity` | `Medium` | `Low` / `Medium` / `High` / `Critical`. |
| `category` | `string` | `config.defaultCategory` | e.g. `bug`, `crash`, `feedback`. |
| `reporter` | `ReporterInfo` | `null` | Optional tester identity; omitted from the payload when unset. |
| `playerPosition` | `Vector3?` | main camera position | Override for the reported player position. |

`ReporterInfo` carries optional `id`, `name` and `email` strings.

### `SendResult`

Outcome passed to the `Capture` callback (and surfaced in the overlay).

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Whether the report was accepted. |
| `httpStatus` | `long` | HTTP status of the final attempt, or `0` on a transport/network error. |
| `reportId` | `string` | Backend report id (on success). |
| `status` | `string` | `"created"` or `"already_exists"` (on success). |
| `dashboardUrl` | `string` | Link to the report in the dashboard (on success). |
| `errorCode` | `string` | Backend error code, e.g. `UNAUTHORIZED` (on failure; may be empty). |
| `message` | `string` | Human-friendly message, safe to show in UI. |
| `details` | `string` | Raw `details` field from the error body, for logging/diagnostics. |
| `queuedForRetry` | `bool` | True when a transient failure was persisted to the offline queue and will be retried automatically later. |

## Configuration reference

Fields on the `BugCaptureConfig` asset:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `apiKey` | `string` | `""` | Project API key (e.g. `bc_pk_test_xxx`). Don't commit production keys. |
| `endpoint` | `string` | `https://api.bugcapture.dev` | Backend base URL, no trailing `/v1`. |
| `environment` | `string` | `development` | Environment label sent with every report. |
| `hotkey` | `KeyCode` | `F8` | Key that opens the overlay (any Active Input Handling setting). |
| `captureScreenshot` | `bool` | `true` | Capture a screenshot when a report is created. |
| `captureLogs` | `bool` | `true` | Attach recent Unity console logs. |
| `maxLogLines` | `int` | `500` | Recent log lines kept in the ring buffer. |
| `pauseWhileOpen` | `bool` | `false` | Set `Time.timeScale = 0` while the overlay is open, restoring it on close. |
| `blockGameplayInput` | `bool` | `true` | Stop form text reaching game controls; exposes `BugCapture.IsInputBlocked`. |
| `defaultCategory` | `string` | `bug` | Category for reports that don't specify one. |
| `maxScreenshotBytes` | `int` | `5 MB` | Screenshots above this are downscaled or dropped before upload. |
| `maxLogBytes` | `int` | `2 MB` | Older log lines are trimmed to fit before upload. |
| `maxMetadataBytes` | `int` | `256 KB` | Cap on serialized metadata size. |
| `enableOfflineQueue` | `bool` | `true` | Persist failed reports to disk and retry on next launch. |
| `maxQueuedReports` | `int` | `50` | Max failed reports kept on disk; oldest is dropped when full. |

## What gets sent

A multipart `POST {endpoint}/v1/reports` with:

- `metadata` (JSON) — `clientReportId`, environment, build/engine/sdk version,
  scene name, player position, the report body (title/description/severity),
  device specs and runtime info.
- `screenshot` (PNG) — unless disabled in config.
- `logs` (text) — recent Unity console output, unless disabled in config.

Auth is `Authorization: Bearer <apiKey>`. The `clientReportId` is stable across
retries, so the backend deduplicates idempotently.

### Offline / failure queue

If a report can't be uploaded — you're offline, or the server returns a 5xx — after
the in-process retries it's saved to disk (under the player's persistent data path)
and retried automatically on the next launch, and again right after the next
successful send. Because the saved report keeps its original `clientReportId`, a
report the backend already received is recognised and **not** duplicated.

The queue is bounded by `maxQueuedReports` (default 50); when it's full the oldest
report is dropped. Set `enableOfflineQueue = false` in the config to turn it off.
Permanent failures (bad API key, invalid report, attachment too large) and rate
limiting are not queued — replaying them wouldn't help.

## Requirements

- Unity **2021.3+**.
- The hotkey works under any **Active Input Handling** setting (Player Settings →
  Active Input Handling): the legacy Input Manager, the new Input System package,
  or **Both**. With no input backend wired up you can still call `BugCapture.Open()`
  yourself.

## Roadmap

- [ ] Free-form `context` (inventory, quest flags, save snapshot)
- [ ] `events`, `save_state`, `memory_dump` attachments
- [ ] Themed UI overlay (uGUI/UI Toolkit)

See [`CHANGELOG.md`](CHANGELOG.md) for version history.

## Releasing

The SDK version lives in two places that must agree: `package.json#version` and
`BugCaptureVersion.Value` (the value compiled into builds and reported as
`sdkVersion`). To release, bump `package.json`, then run **Tools → BugCapture →
Sync Version from package.json** to update `BugCaptureVersion.cs`. The editor
logs an error on load if the two ever drift.

## License

MIT — see [`LICENSE`](LICENSE).
