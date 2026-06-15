# Changelog

All notable changes to this package are documented here. This project adheres to
[Semantic Versioning](https://semver.org/) and the format of
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Persistent game-context store: `Bugyard.SetContext(key, value)` /
  `RemoveContext(key)` / `ClearContext()`. The SDK-wide context is merged into every
  report's `metadata.context` (including overlay/F8 reports); per-report context passed
  to `Capture` overrides matching keys. Bounded by `maxContextBytes`.
- Gameplay breadcrumbs: `Bugyard.Track(name, payload?)` records recent events into a
  bounded ring buffer (`maxBreadcrumbs`, default 300) that is captured as the
  `events.json` attachment on the next report — so overlay reports now carry breadcrumbs
  automatically. A caller-supplied `ReportInput.events` still overrides them.
- Optional extra capture channels on the multipart upload, matching the backend
  ingestion contract: gameplay `events` (`events.json`), `save_state` (raw bytes or
  JSON), and a gzip `memory_dump` (`memory_dump.gz`). Supplied programmatically via
  new `ReportInput` fields (`events`, `saveState` / `saveStateIsJson`, `memoryDump`)
  and bundled through the new `ReportArtifacts` type.
- Free-form `context` object on `ReportInput` (`Dictionary<string, object>`,
  arbitrarily nested) serialized verbatim into the metadata `context` field via the
  new `ContextJson` writer.
- Configurable client-side caps for the new payloads (`maxContextBytes` 16 KB,
  `maxEventsBytes` 512 KB, `maxSaveStateBytes` 10 MB, `maxMemoryDumpBytes` 100 MB).
  Oversized context and binary attachments are dropped before upload (they can't be
  truncated) so the rest of the report still sends instead of being rejected with
  `PAYLOAD_TOO_LARGE`.

### Notes

- The IMGUI overlay still files only the report body, screenshot and logs; the new
  attachments and `context` are sent through `Bugyard.Capture(ReportInput, ...)`.
- The offline queue persists metadata (including `context`), screenshot and logs, but
  intentionally does **not** persist the heavy `events` / `save_state` / `memory_dump`
  blobs; an offline replay is delivered without them.

## [0.1.0] - 2026-06-15

### Added

- Initial MVP SDK package (`com.bugyard.sdk`).
- `Bugyard.Init` / `Open` / `Capture` entry points.
- `BugyardConfig` ScriptableObject (API key, endpoint, environment, hotkey,
  capture toggles).
- F8 hotkey overlay (minimal IMGUI) for filing a report in-game, working under any
  Active Input Handling backend (legacy Input Manager, the new Input System, or Both).
- Screenshot capture (PNG), recent-log ring buffer, and metadata collection
  (scene, player position, build/engine version, device specs, runtime info,
  optional reporter identity), serialized to match the backend contract
  field-for-field with empty optionals omitted.
- Multipart upload to `POST /v1/reports` with idempotent `clientReportId` and
  bounded retries on transient failures (0/429/5xx), with exponential backoff that
  honors a `Retry-After` header on 429 (delta-seconds or HTTP-date). Non-retryable
  errors fail fast.
- Client-side size enforcement before upload (configurable caps): oversized
  screenshots are progressively downscaled or dropped, logs are trimmed to their
  most recent lines, and metadata free-text is truncated so the payload never
  exceeds the caps the backend would otherwise reject with `PAYLOAD_TOO_LARGE`.
- Typed send result (`SendResult`) surfaced from `BugyardClient.Send`, exposed
  via the optional `Bugyard.Capture(report, onResult)` callback (reportId,
  status, dashboardUrl on success; a friendly reason on failure).
- Friendly translation of backend error responses (`{ error, message, details? }`)
  into distinct, actionable messages for each documented code (`UNAUTHORIZED`,
  `REQUEST_NOT_VALID`, `PAYLOAD_TOO_LARGE`, `REPORT_LIMIT_EXCEEDED`, and 429 rate
  limiting), with graceful fallbacks for unknown errors; the raw `details` value is
  preserved on `SendResult.details` for diagnostics.
- Overlay send feedback: a success confirmation (with report ID and an "Open in
  dashboard" link when returned) that resets the form, or an inline error banner
  that keeps the entered text so the report can be fixed and retried.
- Overlay input isolation: optional `pauseWhileOpen` (holds `Time.timeScale` at 0 while
  the overlay is open, restoring the original scale on close/cancel) and
  `blockGameplayInput` (neutralizes legacy Input Manager axes/buttons so form text doesn't
  reach game controls). `Bugyard.IsOverlayOpen` / `Bugyard.IsInputBlocked` expose the
  state for gating raw input polling in your own code.
- Offline/failure queue: reports that fail to upload transiently (offline or a 5xx, after the
  in-process retries) are persisted to disk and retried automatically on the next launch — and
  opportunistically after the next successful send. The stable `clientReportId` makes the
  cross-session retry idempotent, so a report the backend already received is deduplicated
  rather than duplicated. The queue is bounded by `maxQueuedReports` (oldest dropped when full)
  and can be disabled with `enableOfflineQueue`. The overlay confirms a saved report
  (`SendResult.queuedForRetry`) instead of leaving the form open to be re-submitted.
- Basic Usage sample and package documentation.
- Editor version-sync check that errors when `BugyardVersion.Value` and
  `package.json#version` drift, with a Tools menu action to sync them.
- "Send Test Report" editor action (**Tools → Bugyard → Send Test Report**) that
  uploads a synthetic report with the current config to verify connectivity and auth
  end-to-end, reporting success (with a dashboard link) or a precise failure reason.

[Unreleased]: https://github.com/Bugyard/unity-sdk/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Bugyard/unity-sdk/releases/tag/v0.1.0
