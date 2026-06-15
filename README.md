# Bugyard Unity SDK

Bugyard Unity SDK lets playtesters file a rich bug report from inside a Unity
build. Press the configured hotkey, fill in the overlay, and the SDK uploads a
screenshot, recent logs, scene/player metadata, build/runtime details, and any
optional diagnostic context to your Bugyard backend.

> **Status: alpha (0.1.x).** The SDK is suitable for internal playtests and QA
> builds. Public APIs and UI may change between minor versions, and the built-in
> overlay is intentionally minimal. For live production builds, pin the package,
> review the payload, and gate initialization to the builds where you want report
> capture enabled.

## What you get

- Built-in report overlay opened by **F8** or `Bugyard.Open()`.
- Headless reporting through `Bugyard.Capture(...)` for custom UI, debug menus,
  and automated gameplay reports.
- Screenshot capture after the overlay hides itself, so the image shows the game.
- Recent Unity console logs with a bounded ring buffer.
- Automatic metadata: scene name, player position, build version, Unity version,
  SDK version, device specs, locale, timezone, and estimated FPS.
- Optional reporter identity, free-form `context`, `events.json`, `save_state`,
  and an automatic `diagnostic_snapshot.zip` (runtime metrics + custom files) for
  deeper diagnostics.
- Client-side payload limits, retry handling, idempotent `clientReportId`s, and an
  offline queue for transient failures.
- Editor tools for creating config assets, validating common mistakes, syncing
  package version, and sending a real connectivity test report.

## Requirements

- Unity **2021.3+**.
- Any **Active Input Handling** setting: legacy Input Manager, new Input System,
  or **Both**. If you do not use a Unity input backend, open the overlay yourself
  with `Bugyard.Open()`.
- A Bugyard backend endpoint and project API key. The default hosted endpoint is
  `https://api.bugyard.com`; self-hosters should use their own backend base URL.

## Install

In Unity, open **Window -> Package Manager -> + -> Add package from git URL...**
and paste:

```text
https://github.com/Bugyard/unity-sdk.git
```

Or add the package to `Packages/manifest.json`:

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

After Unity resolves the package, you should see **Tools -> Bugyard** in the
Unity menu and the **Basic Usage** sample in Package Manager.

## Quick start

1. Create a config asset with **Tools -> Bugyard -> Create Config Asset**.
2. Select the asset and set:
   - `apiKey`: your project key, for example `by_pk_test_xxx`.
   - `endpoint`: your backend base URL, for example `https://api.bugyard.com`.
     Do not include `/v1`; the SDK appends it.
3. Initialize once from a GameObject in your first scene:

```csharp
using UnityEngine;
using BugyardSDK;

public class Bootstrap : MonoBehaviour
{
    [SerializeField] private BugyardConfig config;

    void Awake() => Bugyard.Init(config);
}
```

4. Assign the config asset to the `config` field in the Inspector.
5. Verify the end-to-end connection with **Tools -> Bugyard -> Send Test Report**.
   Success returns a dashboard link; failure reports the concrete auth, endpoint,
   validation, or network reason.
6. Enter play mode or run a build, press **F8**, fill in the overlay, and click
   **Send**.

### Without a config asset

For a quick spike, initialize with values directly:

```csharp
Bugyard.Init("by_pk_test_xxx", "https://api.bugyard.com");
```

A config asset is recommended for real projects because it exposes capture
toggles, payload limits, hotkey behavior, and offline queue settings.

### Programmatic triggers

Open the built-in overlay from your own UI:

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

Pass a callback to handle success or failure:

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

Use `Bugyard.IsInitialized`, `Bugyard.IsOverlayOpen`, and
`Bugyard.IsInputBlocked` when integrating with your own UI and input handling.

## Production readiness checklist

- Pin the package to a commit or release tag before using it in a reproducible
  build pipeline.
- Commit only test keys (`by_pk_test_*`) or empty config assets. Inject live keys
  at runtime or keep live-key config assets out of source control.
- Confirm the endpoint is the backend base URL with no trailing `/v1`.
- Run **Tools -> Bugyard -> Send Test Report** for each environment before
  giving a build to testers.
- Decide which builds call `Bugyard.Init(...)`. Many teams enable it only for
  development, staging, closed beta, or QA builds.
- Review [what gets sent](docs/what-gets-sent.md), disable screenshots/logs if
  needed, and tune payload caps for your privacy and disk-budget requirements.
- If your game polls raw `Input.GetKey(...)` or the new Input System directly,
  gate that code on `!Bugyard.IsInputBlocked` while the overlay is open.
- Keep `pauseWhileOpen` off unless pausing with `Time.timeScale = 0` is compatible
  with your game.

## API reference

Everything lives in the `BugyardSDK` namespace. The public surface is the static
`Bugyard` class plus plain data types.

### `Bugyard`

| Member | Signature | Description |
|--------|-----------|-------------|
| `Init` | `void Init(BugyardConfig config)` | Initialize once at startup with a config asset. Logs an error on a null config and warns on duplicate init. |
| `Init` | `void Init(string apiKey, string endpoint = null)` | Convenience initializer for prototypes. `endpoint` defaults to `https://api.bugyard.com`. |
| `Open` | `void Open()` | Open the built-in report overlay. No-op if already open. Logs an error if not initialized. |
| `Capture` | `void Capture(ReportInput report, Action<SendResult> onResult = null)` | Capture and send a report headless, bypassing the overlay. |
| `SetContext` / `RemoveContext` / `ClearContext` | `void SetContext(string key, object value)`, … | Manage persistent context merged into every report's `metadata.context`. Per-report `context` overrides matching keys. |
| `Track` | `void Track(string name, object payload = null)` | Record a gameplay breadcrumb; the most recent (up to `maxBreadcrumbs`) are attached to the next report as `events.json`. |
| `RegisterSaveStateProvider` / `UnregisterSaveStateProvider` | `void RegisterSaveStateProvider(SaveStateProvider provider)`, `void UnregisterSaveStateProvider()` | Register/clear a callback that produces the save/game-state blob on demand; invoked during capture when save-state inclusion is enabled. |
| `RegisterDiagnosticFileProvider` / `UnregisterDiagnosticFileProvider` | `void RegisterDiagnosticFileProvider(string name, DiagnosticFileProvider provider)`, `void UnregisterDiagnosticFileProvider(string name)` | Register/clear a named producer of a custom file embedded as `custom/<name>` in the diagnostic snapshot. |
| `Shutdown` | `void Shutdown()` | Tear down the SDK, unhook the log handler, and destroy the runtime. Mainly useful for tests and reinitialization. |
| `IsInitialized` | `bool` (get) | True after `Init` and before `Shutdown`. |
| `IsOverlayOpen` | `bool` (get) | True while the report overlay is open. |
| `IsInputBlocked` | `bool` (get) | True while the overlay is swallowing gameplay input according to `blockGameplayInput`. |

### `ReportInput`

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `title` | `string` | `(no title)` | Short report summary. |
| `description` | `string` | `null` | Longer details or repro steps. |
| `expectedResult` | `string` | `null` | What the tester expected to happen. |
| `severity` | `Severity` | `Medium` | `Low`, `Medium`, `High`, or `Critical`. |
| `category` | `string` | `config.defaultCategory` | Report category, for example `bug`, `crash`, or `feedback`. |
| `reporter` | `ReporterInfo` | `null` | Optional tester identity. |
| `playerPosition` | `Vector3?` | main camera position | Override for the reported player position. |
| `context` | `Dictionary<string, object>` | `null` | Free-form app state serialized into `metadata.context`. |
| `events` | `byte[]` | `null` | Optional JSON attachment uploaded as `events.json`. |
| `saveState` | `byte[]` | `null` | Optional save/game-state attachment. |
| `saveStateIsJson` | `bool` | `false` | Upload `saveState` as JSON instead of raw bytes. |
| `includeSaveState` | `bool?` | `null` | Invoke the registered save-state provider for this report. `null` defers to `config.includeSaveStateByDefault`. |
| `includeDiagnosticSnapshot` | `bool?` | `null` | Build and attach a diagnostic snapshot for this report. `null` defers to `config.includeDiagnosticSnapshotByDefault`. |
| `diagnosticSnapshot` | `byte[]` | `null` | Optional prebuilt zip uploaded as `diagnostic_snapshot.zip`. Overrides the SDK's snapshot builder. |

`ReporterInfo` carries optional `id`, `name`, and `email` strings.

### `SendResult`

| Field | Type | Description |
|-------|------|-------------|
| `success` | `bool` | Whether the report was accepted. |
| `httpStatus` | `long` | HTTP status of the final attempt, or `0` on a transport/network error. |
| `reportId` | `string` | Backend report id on success. |
| `status` | `string` | `created` or `already_exists` on success. |
| `dashboardUrl` | `string` | Link to the report in the dashboard on success. |
| `errorCode` | `string` | Backend error code on failure, for example `UNAUTHORIZED`. |
| `message` | `string` | Human-friendly message safe to show in UI. |
| `details` | `string` | Raw backend `details` field for logging/diagnostics. |
| `queuedForRetry` | `bool` | True when a transient failure was persisted to the offline queue. |

## Configuration reference

Fields on the `BugyardConfig` asset:

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `apiKey` | `string` | `""` | Project API key. Do not commit live keys. |
| `endpoint` | `string` | `https://api.bugyard.com` | Backend base URL, no trailing `/v1`. |
| `environment` | `string` | `development` | Environment label sent with every report. |
| `hotkey` | `KeyCode` | `F8` | Key that opens the overlay. |
| `captureScreenshot` | `bool` | `true` | Capture a screenshot when a report is created. |
| `captureLogs` | `bool` | `true` | Attach recent Unity console logs. |
| `maxLogLines` | `int` | `500` | Recent log lines kept in memory. |
| `maxBreadcrumbs` | `int` | `300` | Recent `Bugyard.Track` breadcrumbs kept in memory; oldest dropped when full. Serialized into `events.json`. |
| `pauseWhileOpen` | `bool` | `false` | Set `Time.timeScale = 0` while the overlay is open. |
| `blockGameplayInput` | `bool` | `true` | Block gameplay input while the overlay is open and expose `Bugyard.IsInputBlocked`. |
| `defaultCategory` | `string` | `bug` | Category for reports that do not specify one. |
| `includeSaveStateByDefault` | `bool` | `false` | Attach the registered save-state provider's output by default. |
| `maxScreenshotBytes` | `int` | `5 MB` | Downscale or drop screenshots above this size. |
| `maxLogBytes` | `int` | `2 MB` | Trim older logs above this size. |
| `maxMetadataBytes` | `int` | `256 KB` | Cap on serialized metadata. |
| `maxContextBytes` | `int` | `16 KB` | Drop oversized `context` before upload. |
| `maxEventsBytes` | `int` | `512 KB` | Drop oversized `events` attachments before upload. |
| `maxSaveStateBytes` | `int` | `10 MB` | Drop oversized `saveState` attachments before upload. |
| `maxDiagnosticSnapshotBytes` | `int` | `25 MB` | Drop oversized `diagnostic_snapshot.zip` before upload. Keep at or below the backend's limit. |
| `includeDiagnosticSnapshotByDefault` | `bool` | `false` | Build and attach a diagnostic snapshot by default (recommend on for dev builds). |
| `enableOfflineQueue` | `bool` | `true` | Persist transient failures to disk and retry later. |
| `maxQueuedReports` | `int` | `50` | Max failed reports kept on disk; oldest is dropped when full. |

## What gets sent

A multipart `POST {endpoint}/v1/reports` with:

- `metadata` JSON: `clientReportId`, environment, build/engine/SDK version,
  scene name, player position, report body, optional reporter, optional context,
  device specs, and runtime info.
- `screenshot` PNG, unless screenshot capture is disabled or the image is dropped
  by size limits.
- `logs` text, unless log capture is disabled.
- Optional `events`, `save_state`, and `diagnostic_snapshot.zip` attachments when
  supplied or enabled through `ReportInput` / config.

Auth is `Authorization: Bearer <apiKey>`. `clientReportId` is stable across
retries, so the backend can deduplicate repeated uploads.

### Offline queue

When a report cannot be uploaded because the player is offline or the server
returns a 5xx, the SDK retries in-process and then saves the report under the
player persistent data path. Queued reports are retried on the next launch and
after the next successful send.

The queue stores metadata, screenshot, and logs. Large diagnostic blobs
(`events`, `save_state`, `diagnostic_snapshot`) are not persisted to disk for replay.
Permanent failures such as bad API keys, validation errors, attachment-too-large
responses, and rate limiting are not queued.

## Documentation

- [Quick start](docs/quick-start.md)
- [API reference](docs/api-reference.md)
- [Configuration](docs/configuration.md)
- [What gets sent](docs/what-gets-sent.md)
- [Changelog](CHANGELOG.md)

## Roadmap

- [ ] Themed UI overlay.
- [ ] More built-in adapters for player identity and game-state collection.
- [ ] Expanded examples for CI/release gating and self-hosted environments.

## Continuous integration

Two GitHub Actions workflows guard the package:

- **Install verification** (`.github/workflows/install-verification.yml`) — the real
  safety net. On every push/PR it installs this package *by path* into a fresh, empty
  Unity project (`.github/install-harness/`) and runs the EditMode + PlayMode suites
  against it, across a matrix of Unity versions (the `2021.3` floor and a current LTS)
  and Input System present/absent. A fast `.meta`-coverage pre-check
  (`.github/scripts/check-meta-coverage.sh`) fails the run in seconds if any `.cs`/
  `.asmdef` is missing its sibling `.meta`, before the slow Unity legs start. Unlike
  the unit tests (which run where everything is already resolved), this reproduces a
  real consumer install and catches missing meta files, unresolved assembly references,
  and undeclared dependencies. See [`plans/install-verification/`](plans/install-verification/).
- **Docs** (`.github/workflows/docs.yml`) — builds and publishes the MkDocs site.

CI runs headless, so it can't exercise the overlay UI, a real hotkey press, or a
live backend round-trip. Those are covered before each release by the
[manual smoke test](docs/manual-smoke-test.md), whose results are recorded in the
release PR using [`.github/release-smoke-test-template.md`](.github/release-smoke-test-template.md).

### Required secrets (Unity license)

The Unity legs use [`game-ci`](https://game.ci), which needs a Unity license
activated from repository secrets. Add these under **Settings → Secrets and variables
→ Actions**:

| Secret | Purpose |
|--------|---------|
| `UNITY_LICENSE` | Contents of the activated `.ulf` license file (Personal license). |
| `UNITY_EMAIL` | Unity account email (for license activation). |
| `UNITY_PASSWORD` | Unity account password. |

For a Personal license, generate the `.ulf` once with the
[game-ci activation flow](https://game.ci/docs/github/activation) and paste its full
contents into `UNITY_LICENSE`. The Unity versions in the matrix must match an
available [game-ci editor image](https://game.ci/docs/docker/versions) — bump them in
the workflow's `unityVersion` list as new LTS images ship.

## Releasing

The SDK version lives in `package.json#version` and `BugyardVersion.Value`.
To release, bump `package.json`, then run **Tools -> Bugyard -> Sync Version from
package.json** to update `BugyardVersion.cs`. The editor logs an error on load if
the two ever drift.

## License

MIT - see [`LICENSE`](LICENSE).
