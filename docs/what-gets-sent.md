# What gets sent

A multipart `POST {endpoint}/v1/reports` with:

- **`metadata`** (JSON) — `clientReportId`, environment, build/engine/sdk version,
  scene name, player position, the report body (title/description/severity),
  device specs and runtime info.
- **`screenshot`** (PNG) — unless disabled in config.
- **`logs`** (text) — recent Unity console output, unless disabled in config.

Auth is `Authorization: Bearer <apiKey>`. The `clientReportId` is stable across
retries, so the backend deduplicates idempotently.

## Screenshots

Screenshots are captured with `ScreenCapture.CaptureScreenshotAsTexture()` at the
end of the frame, after the overlay has been hidden, so the report image shows the
game frame without the Bugyard UI. The texture is PNG-encoded and destroyed
immediately, so no texture is leaked. If capture or encoding fails, the report is
still sent — just without the screenshot.

Known limitations:

- **Main display only.** Capture reads the back buffer of the primary display
  (`Display.main`). On multi-display setups, secondary displays are not included.
- **Render pipelines.** Built-in, URP and HDRP all render to the back buffer, so
  capture works across pipelines. Effects that exist only outside that buffer
  (e.g. some native plugin or XR/HMD compositor overlays) won't appear.
- **Resolution.** The image matches the current screen/back-buffer resolution.
  Oversized PNGs are subject to the `maxScreenshotBytes` cap (see
  [Configuration](configuration.md)).
- **Editor vs. build.** In the Editor the captured frame is the Game view; in a
  player it is the full window.

## Offline / failure queue

If a report can't be uploaded — you're offline, or the server returns a 5xx — after
the in-process retries it's saved to disk (under the player's persistent data path)
and retried automatically on the next launch, and again right after the next
successful send. Because the saved report keeps its original `clientReportId`, a
report the backend already received is recognised and **not** duplicated.

The queue is bounded by `maxQueuedReports` (default 50); when it's full the oldest
report is dropped. Set `enableOfflineQueue = false` in the config to turn it off.

!!! note "What is *not* queued"
    Permanent failures (bad API key, invalid report, attachment too large) and
    rate limiting are not queued — replaying them wouldn't help. They fail fast
    with an actionable message on `SendResult`.

## Limitations (0.1.x)

- No `events`, `save_state`, `memory_dump`, or free-form `context` yet — only the
  core metadata, screenshot and logs.
- Overlay is minimal IMGUI; no theming.

See the [Roadmap](https://github.com/bugyard/bugyard-unity#roadmap) for what's next.
