# BugCapture Unity SDK

The BugCapture Unity SDK lets playtesters file a rich bug report from inside a
Unity build with a single hotkey. Each report bundles a screenshot, recent
console logs, scene name, player position, build version and device specs, and
sends them to your BugCapture backend (`POST /v1/reports`).

## Requirements

- Unity **2021.3** or newer.
- The hotkey works under any **Active Input Handling** setting (Player Settings) —
  the legacy Input Manager, the new Input System package, or *Both*. With no input
  backend wired up you can still open the overlay yourself via `BugCapture.Open()`.

## Concepts

| Piece | Role |
|-------|------|
| `BugCaptureConfig` | ScriptableObject holding API key, endpoint, hotkey and capture toggles. |
| `BugCapture` | Static entry point: `Init`, `Open`, `Capture`, `Shutdown`. |
| `BugCaptureRuntime` | Hidden MonoBehaviour created by `Init`; drives hotkey, log buffer, overlay, screenshot. |
| `BugCaptureClient` | Multipart upload with idempotent retries. |
| `MetadataCollector` | Builds the metadata payload from runtime state. |

## Payload

Metadata is serialized to JSON and sent as the multipart `metadata` field,
alongside optional `screenshot` (PNG) and `logs` (text) files. The shape matches
the backend contract in `bugcapture-backend-docs/03-api-contracts.md`. The
`clientReportId` is a GUID generated per capture and reused across retries so the
backend deduplicates idempotently.

## Screenshots

Screenshots are captured with `ScreenCapture.CaptureScreenshotAsTexture()` at the
end of the frame, after the overlay has been hidden, so the report image shows the
game frame without the BugCapture UI. The texture is PNG-encoded and destroyed
immediately, so no texture is leaked. If capture or encoding fails, the report is
still sent — just without the screenshot.

Known limitations:

- **Main display only.** Capture reads the back buffer of the primary display
  (`Display.main`). On multi-display setups, secondary displays are not included.
- **Render pipelines.** Built-in, URP and HDRP all render to the back buffer, so
  capture works across pipelines. Effects that exist only outside that buffer
  (e.g. some native plugin or XR/HMD compositor overlays) won't appear.
- **Resolution.** The image matches the current screen/back-buffer resolution.
  Oversized PNGs are subject to the `maxScreenshotBytes` cap (see config).
- **Editor vs. build.** In the Editor the captured frame is the Game view; in a
  player it is the full window.

## Limitations (0.1.x)

- No `events`, `save_state`, `memory_dump`, or free-form `context` yet — only the
  core metadata, screenshot and logs.
- Overlay is minimal IMGUI; no theming.
