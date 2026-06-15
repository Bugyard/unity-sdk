# Configuration

`BugyardConfig` is a ScriptableObject. Create one with **Tools -> Bugyard ->
Create Config Asset** or **Assets -> Create -> Bugyard -> Config**, then pass it
to `Bugyard.Init(config)`.

## Required connection fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `apiKey` | `string` | `""` | Project API key, for example `by_pk_test_xxx`. Empty keys initialize the SDK but uploads are rejected with 401. |
| `endpoint` | `string` | `https://api.bugyard.com` | Backend base URL, no trailing `/v1`. The SDK posts to `{endpoint}/v1/reports`. |
| `environment` | `string` | `development` | Environment label sent with every report, for example `development`, `staging`, or `production`. |

!!! warning "Keep live keys out of version control"
    The `apiKey` is serialized into the asset. Commit a test key or an empty key,
    then inject live keys at runtime or keep live-key config assets out of source
    control.

## Overlay and input

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `hotkey` | `KeyCode` | `F8` | Key that opens the overlay. Works with legacy Input Manager, new Input System, or Both. |
| `pauseWhileOpen` | `bool` | `false` | Set `Time.timeScale = 0` while the overlay is open and restore the previous scale on close. |
| `blockGameplayInput` | `bool` | `true` | Block gameplay input while the overlay is open and expose `Bugyard.IsInputBlocked`. |

`blockGameplayInput` neutralizes legacy Input Manager axes/buttons while the
overlay is open. It cannot intercept every raw `Input.GetKey(...)` call or new
Input System poll in your own scripts, so gate those paths yourself:

```csharp
if (Bugyard.IsInputBlocked)
    return;
```

Leave `pauseWhileOpen` off unless pausing with `Time.timeScale = 0` is compatible
with your game.

## Capture defaults

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `captureScreenshot` | `bool` | `true` | Capture a screenshot when a report is created. |
| `captureLogs` | `bool` | `true` | Attach recent Unity console logs. |
| `maxLogLines` | `int` | `500` | Recent log lines kept in the in-memory ring buffer. |
| `maxBreadcrumbs` | `int` | `300` | Recent `Bugyard.Track` breadcrumbs kept in the in-memory ring buffer. When full, the oldest is dropped. Serialized into the `events.json` attachment unless the report supplies its own `events`. |
| `defaultCategory` | `string` | `bug` | Category applied to reports that do not specify one. |
| `includeSaveStateByDefault` | `bool` | `false` | When a save-state provider is registered, attach its output by default. Per-report `includeSaveState` overrides this, and the overlay's "Include save state" checkbox is seeded from it. |
| `includeDiagnosticSnapshotByDefault` | `bool` | `false` | Build and attach a diagnostic snapshot by default (recommend on for dev builds). Per-report `includeDiagnosticSnapshot` overrides this, and the overlay's "Include diagnostic snapshot" checkbox is seeded from it. |

Screenshots are captured at the end of the frame after the overlay hides itself.
Logs are captured from Unity's log callback after `Bugyard.Init(...)` runs.

## Client-side size caps

The client enforces these caps before upload so it does not send a payload that
the backend would reject as too large.

| Field | Type | Default | Behavior |
|-------|------|---------|----------|
| `maxScreenshotBytes` | `int` | `5 MB` | Oversized screenshots are progressively downscaled, then dropped if still too large. |
| `maxLogBytes` | `int` | `2 MB` | Older log lines are trimmed to keep the newest logs. |
| `maxMetadataBytes` | `int` | `256 KB` | Report free-text fields are truncated until metadata fits where possible. |
| `maxContextBytes` | `int` | `16 KB` | Oversized `context` is dropped, not truncated. |
| `maxEventsBytes` | `int` | `512 KB` | Oversized `events` attachments are dropped. |
| `maxSaveStateBytes` | `int` | `10 MB` | Oversized `saveState` attachments are dropped. |
| `maxDiagnosticSnapshotBytes` | `int` | `25 MB` | Oversized diagnostic snapshots (`diagnostic_snapshot.zip`) are dropped. Keep this at or below the backend's limit. |

Non-positive caps are treated as no limit for screenshot, metadata, context, and
binary attachments. For logs, a non-positive cap sends no logs.

## Offline queue

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `enableOfflineQueue` | `bool` | `true` | Persist reports that fail because the player is offline or the server returns a 5xx, then retry them later. |
| `maxQueuedReports` | `int` | `50` | Maximum failed reports kept on disk. When full, the oldest report is dropped. |

Queued reports store metadata, screenshot, and logs under the player's persistent
data path. Large diagnostic blobs (`events`, `save_state`, `diagnostic_snapshot`) are
not persisted for replay to avoid filling player disks.

Permanent failures such as bad API keys, validation errors, attachment-too-large
responses, and rate limiting are not queued.

## Editor validation

The custom Inspector and build preprocessor warn about common mistakes:

- empty API key,
- live key stored in a source-controlled asset,
- key with an unexpected prefix,
- empty or malformed endpoint,
- placeholder endpoint, and
- endpoint that already ends in `/v1`.

Use **Tools -> Bugyard -> Send Test Report** to verify the selected or discovered
config asset against the backend before sharing a build with testers.
