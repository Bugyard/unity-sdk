# Changelog

All notable changes to this package are documented here. This project adheres to
[Semantic Versioning](https://semver.org/) and the format of
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added

- Save-state provider: `Bugyard.RegisterSaveStateProvider(SaveStateProvider)` /
  `UnregisterSaveStateProvider()`. Register a callback that returns the current save /
  game-state blob (`SaveState.Json` / `SaveState.Binary` / `SaveState.None`) and the SDK
  pulls it during capture and uploads it as the `save_state` attachment — so overlay/F8
  reports can carry a save without the caller wiring bytes onto every report. Inclusion is
  opt-in per report via `ReportInput.includeSaveState` (`bool?`, `null` defers to config)
  or globally via `BugyardConfig.includeSaveStateByDefault`; when a provider is registered
  the overlay shows an "Include save state" checkbox seeded from that default. An explicit
  `ReportInput.saveState` still takes precedence, and a provider that throws degrades to a
  report without save state.
- Persistent game-context store: `Bugyard.SetContext(key, value)` /
  `RemoveContext(key)` / `ClearContext()`. The SDK-wide context is merged into every
  report's `metadata.context` (including overlay/F8 reports); per-report context passed
  to `Capture` overrides matching keys. Bounded by `maxContextBytes`.
- Gameplay breadcrumbs: `Bugyard.Track(name, payload?)` records recent events into a
  bounded ring buffer (`maxBreadcrumbs`, default 300) that is captured as the
  `events.json` attachment on the next report — so overlay reports now carry breadcrumbs
  automatically. A caller-supplied `ReportInput.events` still overrides them.
- Automatic diagnostic snapshot: `Bugyard.RegisterDiagnosticFileProvider(name, provider)` /
  `UnregisterDiagnosticFileProvider(name)`. When a report is captured with the diagnostic
  snapshot included, the SDK builds a `diagnostic_snapshot.zip` (`application/zip`) carrying
  a `manifest.json`, a `runtime_metrics.json` sampled from `Unity.Profiling.ProfilerRecorder`
  (memory + render counters), and a `custom/<name>` file for each registered provider — for
  game-specific state Unity can't surface on its own. Inclusion is opt-in per report via
  `ReportInput.includeDiagnosticSnapshot` (`bool?`) or globally via
  `BugyardConfig.includeDiagnosticSnapshotByDefault` (recommend on for dev builds); the
  overlay shows an "Include diagnostic snapshot" checkbox seeded from that default. An
  explicit `ReportInput.diagnosticSnapshot` (prebuilt zip bytes) overrides the builder. New
  `DiagnosticSnapshot` zip builder and `DiagnosticFileProvider` delegate.
- Optional extra capture channels on the multipart upload, matching the backend
  ingestion contract: gameplay `events` (`events.json`), `save_state` (raw bytes or
  JSON), and a `diagnostic_snapshot.zip`. Supplied programmatically via new `ReportInput`
  fields (`events`, `saveState` / `saveStateIsJson`, `diagnosticSnapshot`) and bundled
  through the new `ReportArtifacts` type. The snapshot rides the backend `memory_dump`
  slot (no DB migration); the backend `memory_dump` MIME allowlist now also accepts
  `application/zip`.
- Free-form `context` object on `ReportInput` (`Dictionary<string, object>`,
  arbitrarily nested) serialized verbatim into the metadata `context` field via the
  new `ContextJson` writer.
- Configurable client-side caps for the new payloads (`maxContextBytes` 16 KB,
  `maxEventsBytes` 512 KB, `maxSaveStateBytes` 10 MB, `maxDiagnosticSnapshotBytes` 25 MB).
  Oversized context and binary attachments are dropped before upload (they can't be
  truncated) so the rest of the report still sends instead of being rejected with
  `PAYLOAD_TOO_LARGE`. Keep `maxDiagnosticSnapshotBytes` at or below the backend's limit.
- CI install verification (`.github/workflows/install-verification.yml`): on every
  push/PR the package is installed by path into a fresh empty Unity project and the
  EditMode + PlayMode suites run against it, across a matrix of Unity versions (the
  declared `2021.3` floor and a current LTS) and Input System present/absent, fronted
  by a fast `.meta`-coverage pre-check. This reproduces "someone installs the package"
  on every change and catches clean-install failures unit tests can't (missing meta
  files, unresolved assembly references, undeclared dependencies). Requires Unity
  license secrets — see the README "Continuous integration" section and
  `plans/install-verification/`.
- Manual smoke-test release gate (`docs/manual-smoke-test.md`): a short human pass
  run before each tag that covers what headless CI can't — the overlay UX, a real
  hotkey press under both input backends, and a live auth + endpoint round-trip via
  **Tools → Bugyard → Send Test Report** — across the 2021.3-floor / current-LTS ×
  Input-System-present/absent matrix on a standalone target. Results are recorded
  in the release PR using `.github/release-smoke-test-template.md`, and the
  `Releasing` flow now requires it before tagging.

### Notes

- The IMGUI overlay files the report body, screenshot, logs, breadcrumbs, and — via its
  checkboxes — the save state (when a provider is registered) and the diagnostic snapshot;
  a caller-supplied `events` attachment is sent through `Bugyard.Capture(ReportInput, ...)`.
- The offline queue persists metadata (including `context`), screenshot and logs, but
  intentionally does **not** persist the heavy `events` / `save_state` / `diagnostic_snapshot`
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
