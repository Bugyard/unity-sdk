# Configuration

Fields on the `BugyardConfig` asset. Create one with **Tools → Bugyard → Create
Config Asset**, then assign it to your bootstrap script.

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `apiKey` | `string` | `""` | Project API key (e.g. `by_pk_test_xxx`). Don't commit production keys. |
| `endpoint` | `string` | `https://api.bugyard.com` | Backend base URL, no trailing `/v1`. |
| `environment` | `string` | `development` | Environment label sent with every report. |
| `hotkey` | `KeyCode` | `F8` | Key that opens the overlay (any Active Input Handling setting). |
| `captureScreenshot` | `bool` | `true` | Capture a screenshot when a report is created. |
| `captureLogs` | `bool` | `true` | Attach recent Unity console logs. |
| `maxLogLines` | `int` | `500` | Recent log lines kept in the ring buffer. |
| `pauseWhileOpen` | `bool` | `false` | Set `Time.timeScale = 0` while the overlay is open, restoring it on close. |
| `blockGameplayInput` | `bool` | `true` | Stop form text reaching game controls; exposes `Bugyard.IsInputBlocked`. |
| `defaultCategory` | `string` | `bug` | Category for reports that don't specify one. |
| `maxScreenshotBytes` | `int` | `5 MB` | Screenshots above this are downscaled or dropped before upload. |
| `maxLogBytes` | `int` | `2 MB` | Older log lines are trimmed to fit before upload. |
| `maxMetadataBytes` | `int` | `256 KB` | Cap on serialized metadata size. |
| `enableOfflineQueue` | `bool` | `true` | Persist failed reports to disk and retry on next launch. |
| `maxQueuedReports` | `int` | `50` | Max failed reports kept on disk; oldest is dropped when full. |

!!! warning "Keep production keys out of version control"
    The `apiKey` is serialized into the asset. Use a `by_pk_test_*` key in
    committed assets, and inject the live key at runtime (or via a config asset
    excluded from source control) for production builds.

## Input handling

The `hotkey` works under any **Active Input Handling** setting (Player Settings →
Active Input Handling): the legacy Input Manager, the new Input System package, or
**Both**.

- `blockGameplayInput` neutralizes legacy Input Manager axes/buttons while the
  overlay is open so typing into the form doesn't drive the game. Gate your own
  raw `Input.GetKey(...)` / Input System polling on `Bugyard.IsInputBlocked`.
- `pauseWhileOpen` holds `Time.timeScale` at `0` while the overlay is open and
  restores the original scale on close/cancel.

## Size limits

The client enforces the size caps above *before* upload, so the payload never
exceeds what the backend would reject with `PAYLOAD_TOO_LARGE`:

- oversized screenshots are progressively downscaled or dropped,
- logs are trimmed to their most recent lines, and
- metadata free-text is truncated.

See [What gets sent](what-gets-sent.md) for the full payload and the
offline/failure queue.
