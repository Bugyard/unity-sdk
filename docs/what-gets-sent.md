# What gets sent

The SDK uploads a multipart `POST {endpoint}/v1/reports`. `endpoint` comes from
`BugyardConfig.endpoint`; the SDK appends `/v1/reports`.

Auth uses:

```text
Authorization: Bearer <apiKey>
```

Every capture gets a stable `clientReportId`. Retries reuse that id so the
backend can deduplicate repeated uploads.

## Multipart fields

| Field | Type | When sent | Contents |
|-------|------|-----------|----------|
| `metadata` | JSON form field | Always | Report body, environment, build/runtime data, scene/player metadata, device info, optional reporter, optional context, and `clientReportId`. |
| `screenshot` | `image/png` file | When `captureScreenshot` is enabled and capture succeeds | Current game frame captured after the overlay hides itself. |
| `logs` | `text/plain` file | When `captureLogs` is enabled and logs are present | Recent Unity console output collected after `Bugyard.Init(...)`. |
| `events` | `application/json` file | When `ReportInput.events` is supplied, or breadcrumbs have been recorded with `Bugyard.Track(...)` | Recent gameplay/event log bytes. Defaults to the most recent breadcrumbs (up to `maxBreadcrumbs`) as a JSON array; an explicit `ReportInput.events` overrides them. |
| `save_state` | `application/octet-stream` file (named `save_state.bin`) or `application/json` file (named `save_state.json`) | When `ReportInput.saveState` is supplied, or a registered save-state provider runs (see `includeSaveState`) | Save-game or game-state bytes. Set `saveStateIsJson` (or use `SaveState.Json`) for JSON (`save_state.json`); otherwise raw bytes (`save_state.bin`). |
| `memory_dump` | `application/zip` file (named `diagnostic_snapshot.zip`) | When the diagnostic snapshot is included (`includeDiagnosticSnapshot`), or `ReportInput.diagnosticSnapshot` is supplied | Diagnostic snapshot zip: `manifest.json`, `runtime_metrics.json`, and `custom/<name>` files from registered providers. Rides the `memory_dump` slot (backend enum unchanged). |

## Metadata

`metadata` includes:

- `clientReportId`,
- `environment`,
- `buildVersion`,
- `engine` and `engineVersion`,
- `sdkVersion`,
- `sceneName`,
- `playerPosition`,
- report body (`title`, `description`, `expectedResult`, `severity`, `category`),
- optional `reporter` (`id`, `name`, `email`),
- `device` (`os`, `cpu`, `gpu`, `ramMb`, `deviceModel`),
- `runtime` (`fps`, `locale`, `timezone`), and
- optional free-form `context`.

`context` is supplied programmatically, either per report or as persistent state
that the SDK merges into every report (per-report keys win on conflict):

```csharp
// Persistent: set once as game state changes, attached to every later report.
Bugyard.SetContext("checkpoint", "desert_arena_entry");

// Per report: merged over the persistent context for this capture only.
Bugyard.Capture(new ReportInput
{
    title = "Stuck behind the bridge",
    severity = Severity.High,
    context = new Dictionary<string, object>
    {
        { "inventory", new[] { "sword", "shield" } },
        { "questFlags", new Dictionary<string, object> { { "bridgeUnlocked", false } } },
        { "health", 80 },
    },
});
```

`context` may contain nested dictionaries, lists, strings, numbers, booleans, and
nulls. It is stored as part of the metadata payload. If the serialized context is
larger than `maxContextBytes`, the SDK drops it before upload rather than
truncating it into invalid JSON.

## Optional attachments

The overlay sends the report body, screenshot, and logs. Deeper diagnostics are
added from code through `ReportInput`:

```csharp
Bugyard.Capture(new ReportInput
{
    title = "Quest state is blocked",
    events = recentEventsJsonBytes,
    saveState = currentSaveBytes,
    saveStateIsJson = false,
    includeDiagnosticSnapshot = true, // SDK builds diagnostic_snapshot.zip
});
```

Each optional attachment is bounded by its own config cap. Oversized binary
attachments are dropped before upload so the rest of the report can still send.

## Screenshots

Screenshots are captured with `ScreenCapture.CaptureScreenshotAsTexture()` at the
end of the frame, after the overlay has been hidden. The texture is PNG-encoded
and destroyed immediately. If capture or encoding fails, the report is still sent
without a screenshot.

Known limitations:

- **Main display only.** Capture reads the back buffer of the primary display
  (`Display.main`). Secondary displays are not included.
- **Render pipelines.** Built-in, URP, and HDRP render to the back buffer, so
  capture works across those pipelines. Native plugin or XR/HMD compositor
  overlays that exist outside that buffer may not appear.
- **Resolution.** The image matches the current screen/back-buffer resolution.
  Oversized PNGs are subject to `maxScreenshotBytes`.
- **Editor vs. build.** In the Editor the captured frame is the Game view; in a
  player it is the full window.

## Logs

The SDK subscribes to Unity logging after `Bugyard.Init(...)` runs and keeps a
bounded in-memory ring buffer. It does not collect historical logs from before
initialization.

When `captureLogs` is enabled, the SDK uploads the newest logs that fit
`maxLogBytes`. If logs exceed the cap, older lines are trimmed first.

## Offline queue

If a report cannot be uploaded because the player is offline or the server
returns a 5xx, the SDK retries in-process. If it still fails, and
`enableOfflineQueue` is true, it saves the report under the player's persistent
data path and retries:

- on the next launch, and
- immediately after the next successful send.

The queue stores metadata, screenshot, and logs. The metadata includes `context`,
so context survives a queued replay. Large diagnostic blobs (`events`,
`save_state`, `diagnostic_snapshot`) are not persisted to disk for replay.

The queue is bounded by `maxQueuedReports`; when full, the oldest report is
dropped.

## What is not queued

The SDK does not queue failures that replaying will not fix:

- bad or missing API key,
- validation errors,
- attachment-too-large responses,
- rate limiting, and
- other non-transient 4xx responses.

Those fail fast with a `SendResult.message` suitable for UI or logs.

## Privacy checklist

Before enabling the SDK beyond a small internal test:

- Review whether screenshots can include player personal data, chat, account
  names, or unreleased content.
- Review whether Unity logs can include secrets, access tokens, or user data.
- Disable `captureScreenshot` or `captureLogs` where needed.
- Keep `context`, `events`, `saveState`, and the diagnostic snapshot
  (`runtime_metrics` + your custom providers) limited to data your team is allowed
  to collect.
- Tune size caps and `maxQueuedReports` to match your disk-budget expectations.

## Limitations (0.1.x)

- Built-in overlay is minimal IMGUI and not themeable yet.
- Advanced diagnostics: `context` and `events` are code-driven — per report through
  `Bugyard.Capture(...)`, or continuously via `Bugyard.SetContext(...)` and
  `Bugyard.Track(...)` — while the save state and diagnostic snapshot can also be
  toggled from the overlay's checkboxes.
- The package is alpha. APIs and UI may change between minor versions.
