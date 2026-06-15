# 13 Unity SDK Tasks (Execution-Ready)

This is the build-ready task list for the **Unity SDK** — the deferred track in
`11-implementation-order.md` §16 and `08-implementation-roadmap.md` Phase 7, exploded into
small self-contained tickets.

The SDK is a thin client over the backend ingestion contract: it collects context in a Unity
build and `POST`s a multipart report to `/v1/reports` (see `11-implementation-order.md` §9 and
`bugyard-backend-docs/03-api-contracts.md` / `04-ingestion-flow.md`).

Each task is a ticket: **status**, **scope**, **depends on**, **acceptance criteria (AC)**.
Status reflects the current repo state:

- ✅ **done** — implemented and working
- 🟡 **partial** — exists but has gaps called out in scope
- ⬜ **todo** — not started

> **Goal (from §16):** Pressing F8 in a Unity build sends a report with screenshot, logs,
> scene name, player position, and build version — visible in the dashboard.
> That slice already works; the milestones below harden it into a shippable UPM package.

## Backend contract the SDK must honor

| Concern | Value | Source |
|---|---|---|
| Endpoint | `POST {endpoint}/v1/reports`, `multipart/form-data` | order §9 |
| Auth | `Authorization: Bearer by_pk_{test\|live}_…` | order §4–5 |
| Fields | `metadata` (JSON string, required), `screenshot`/`logs`/`events` (optional) | order §9 |
| Idempotency | stable `clientReportId` → duplicate returns existing | order §6 |
| Size limits | metadata 256KB, screenshot 5MB, logs 2MB, events 512KB | order §14 |
| MIME | `image/png`, `image/jpeg`, `text/plain`, `application/json` | tasks T21 |
| Errors | `{ error, message, details? }`; codes `REQUEST_NOT_VALID`, `PAYLOAD_TOO_LARGE`, `UNAUTHORIZED`, `REPORT_LIMIT_EXCEEDED`, 429 | tasks T29 |

---

# Milestone 0 — Package & versioning hygiene

**Definition of done:** the package installs via UPM from git, asmdefs compile clean, version is single-sourced.

## U01 — Audit UPM package layout
**Status:** 🟡 partial · **Depends on:** —
- Confirm `package.json`, `Runtime/` + `Editor/` asmdefs, `Documentation~/`, `Samples~/BasicUsage/` follow UPM conventions.
- Replace `REPLACE_ME` placeholders in `package.json` (`documentationUrl`, `changelogUrl`, `licensesUrl`) with the real repo URL.
- Verify `unity: "2021.3"` minimum is accurate for the APIs used.
- **AC:** package installs into a clean project via `Add package from git URL`; no asmdef/compile errors.

## U02 — Single-source the SDK version
**Status:** 🟡 partial · **Depends on:** U01
- `BugyardVersion.Value` (`Runtime/BugyardVersion.cs`) and `package.json#version` must match.
- Document/automate the sync (editor check or build step) so `sdkVersion` in metadata is never stale.
- **AC:** bumping the version in one place is caught if the other drifts; `sdkVersion` in a sent report equals `package.json` version.

---

# Milestone 1 — Capture pipeline (core)

**Definition of done:** Init → hotkey → screenshot + logs + metadata collected correctly.

## U03 — Init / lifecycle / singleton
**Status:** ✅ done · **Depends on:** —
- `Bugyard.Init(config)` / `Init(apiKey, endpoint)` create a hidden `DontDestroyOnLoad` runtime; duplicate Init guarded; null/empty key warned (`Runtime/Bugyard.cs`).
- Add `Bugyard.Shutdown()` to unhook log handler and destroy the runtime (for tests / re-init).
- **AC:** Init once works; second Init is ignored with a warning; Shutdown leaves a clean state so a later Init succeeds.

## U04 — Config asset
**Status:** ✅ done · **Depends on:** —
- `BugyardConfig` ScriptableObject with apiKey, endpoint, environment, hotkey, capture toggles, `maxLogLines` (`Runtime/BugyardConfig.cs`).
- Add tunables this list relies on: client-side size caps (screenshot/logs/metadata) and default `category`.
- **AC:** config drives all capture behavior; new fields have tooltips and sane defaults.

## U05 — Screenshot capture
**Status:** ✅ done · **Depends on:** U03
- Overlay is hidden before `CaptureScreenshotAsTexture` so it isn't in the shot; PNG encoded; texture destroyed (`BugyardRuntime.CaptureRoutine`).
- Verify behavior with multiple displays / non-default render pipelines; document limitations.
- **AC:** captured PNG contains the game frame without the overlay; no leaked textures.

## U06 — Unity log capture (ring buffer)
**Status:** 🟡 partial · **Depends on:** U03
- Thread-safe ring buffer on `Application.logMessageReceivedThreaded`, bounded by `maxLogLines` (`BugyardRuntime.OnLog`).
- Gap: include stack traces for `Error`/`Exception` entries (currently only `[type] condition`).
- **AC:** logs snapshot includes recent messages with stack traces for errors; buffer never exceeds `maxLogLines`; handler unhooked on destroy.

## U07 — Metadata collector
**Status:** 🟡 partial · **Depends on:** U03
- Builds `ReportMetadata` (clientReportId, env, build/engine/sdk versions, scene, player position, device, runtime) — `Runtime/MetadataCollector.cs`.
- Gaps: `category` is hardcoded `"bug"` (wire it from input/overlay); confirm severity enum maps to backend lowercase values.
- **AC:** metadata matches the backend schema field-for-field; category/severity reflect user choice; missing optionals omitted cleanly.

---

# Milestone 2 — Overlay UX

**Definition of done:** the in-game form captures everything the backend accepts, with validation and feedback.

## U08 — Overlay: category + expected-result fields
**Status:** 🟡 partial · **Depends on:** U07
- IMGUI overlay currently has title / description / severity (`BugyardRuntime.OnGUI`).
- Add a `category` selector and an `expectedResult` field (both exist in `ReportInput`/backend schema).
- **AC:** overlay collects title, description, expected result, severity, category and passes them through to metadata.

## U09 — Overlay: input validation + length caps
**Status:** ⬜ todo · **Depends on:** U08
- Enforce backend caps client-side: title ≤200, description ≤5000; require non-empty title (Send already gated on title).
- Show inline messaging when a limit is hit instead of letting the backend reject with `REQUEST_NOT_VALID`.
- **AC:** over-length input is prevented/trimmed in the form; Send is blocked until required fields are valid.

## U10 — Overlay: send result feedback
**Status:** ⬜ todo · **Depends on:** U15
- After Send, show success (with reportId / dashboard link if returned) or a friendly error; today result only goes to `Debug.Log`.
- **AC:** the user sees clear success/failure in-overlay; on success the form closes/resets, on failure it stays with the entered text.

## U11 — Overlay: input isolation while open
**Status:** ⬜ todo · **Depends on:** U08
- Optionally block/consume gameplay input (and optionally pause `Time.timeScale`) while the overlay is open, restoring on close.
- **AC:** typing in the form does not leak to game controls; original time scale restored on close/cancel.

---

# Milestone 3 — Input compatibility

**Definition of done:** the hotkey works regardless of the project's input backend.

## U12 — New Input System support for the hotkey
**Status:** ⬜ todo · **Depends on:** U03
- `Update()` is guarded by `ENABLE_LEGACY_INPUT_MANAGER` only (`BugyardRuntime.Update`); projects on the new Input System get no hotkey.
- Add an Input System path (`ENABLE_INPUT_SYSTEM`) and document the "Both" setting.
- **AC:** F8 opens the overlay under legacy, new, and both input backends.

## U13 — Programmatic trigger API
**Status:** ✅ done · **Depends on:** U03
- `Bugyard.Open()` (overlay) and `Bugyard.Capture(ReportInput)` (headless) already exposed for custom UI / automation (`Runtime/Bugyard.cs`).
- Add usage docs/examples; ensure `Capture` works with no overlay present.
- **AC:** a custom button can open the overlay or send a report without the built-in hotkey.

---

# Milestone 4 — Upload reliability

**Definition of done:** uploads respect limits, surface results, map errors, and survive offline.

## U14 — Client-side size limits
**Status:** ✅ done · **Depends on:** U04
- Before upload, enforce config caps (screenshot 5MB, logs 2MB, metadata 256KB) — trim logs / drop or downscale screenshot rather than ship a payload the backend rejects with `PAYLOAD_TOO_LARGE`.
- **AC:** an oversized screenshot/log is reduced or dropped with a warning; metadata never exceeds 256KB.

## U15 — Surface the send result
**Status:** 🟡 partial · **Depends on:** U03
- `BugyardClient.Send` logs success/failure but returns nothing (`Runtime/BugyardClient.cs`).
- Parse the response (`reportId`, `status`, `dashboardUrl`) and expose it via callback/event so the overlay (U10) and callers can react.
- **AC:** callers receive a typed result (success + reportId/dashboardUrl, or failure + reason).

## U16 — Map backend error codes
**Status:** ✅ done · **Depends on:** U15
- Translate `{ error, message, details? }` codes (`REQUEST_NOT_VALID`, `PAYLOAD_TOO_LARGE`, `UNAUTHORIZED`, `REPORT_LIMIT_EXCEEDED`, 429) into friendly SDK-side messages.
- **AC:** each documented error code produces a distinct, actionable message; unknown errors fall back gracefully.

## U17 — Offline / failure queue
**Status:** ✅ done · **Depends on:** U15
- Persist failed reports (metadata + attachments) to disk and retry on next launch; the stable `clientReportId` keeps retries idempotent.
- Bound the queue size; drop oldest when full.
- **AC:** a report submitted while offline is delivered on a later online launch with no duplicate created.

## U18 — Retry/backoff + Retry-After
**Status:** 🟡 partial · **Depends on:** U15
- Exponential backoff over 3 attempts on transient failures (0/429/5xx) already exists (`BugyardClient.Send`).
- Honor the `Retry-After` header on 429 instead of fixed backoff.
- **AC:** on 429 the client waits the server-specified interval; non-retryable errors fail fast.

---

# Milestone 5 — Editor tooling & DX

**Definition of done:** a developer can configure, validate, and test the SDK without leaving the Editor.

## U19 — Config asset menu
**Status:** ✅ done · **Depends on:** U04
- `Tools > Bugyard > Create Config Asset` creates and pings the asset (`Editor/BugyardMenu.cs`).
- Add "select existing config if present" to avoid duplicates.
- **AC:** menu creates a config or selects the existing one; never silently makes duplicates.

## U20 — Config validation warnings
**Status:** ⬜ todo · **Depends on:** U04
- Editor-time warnings: empty apiKey, `by_pk_live_` key committed to source control, placeholder endpoint.
- **AC:** misconfiguration is flagged in the Inspector/console before a build ships.

## U21 — "Send test report" action
**Status:** ⬜ todo · **Depends on:** U15
- Editor button that sends a synthetic report using the current config to verify connectivity/auth end-to-end.
- **AC:** clicking it reports success (with dashboard link) or a precise failure reason.

---

# Milestone 6 — Tests & QA

**Definition of done:** core logic is covered by automated tests that run in CI.

## U22 — EditMode tests
**Status:** ✅ done · **Depends on:** U07
- Test `MetadataCollector` output, severity→string mapping, config defaults, version sync.
- Add a test asmdef + `com.unity.test-framework` reference.
- **AC:** `dotnet`/Unity test runner executes EditMode tests; metadata/version assertions pass.

## U23 — PlayMode tests
**Status:** ⬜ todo · **Depends on:** U06
- Test the log ring buffer bound, capture-routine ordering (overlay hidden before screenshot), Init/Shutdown lifecycle.
- **AC:** PlayMode tests pass; buffer never exceeds `maxLogLines`.

## U24 — Client tests against a mock server
**Status:** ✅ done · **Depends on:** U16, U17
- Mock `/v1/reports` to assert: success parse, retry on 5xx/429, `Retry-After` honored, size-limit enforcement, error-code mapping, offline-queue replay.
- **AC:** all upload paths covered; no network calls hit a real backend.

---

# Milestone 7 — Docs, samples, release

**Definition of done:** the package is documented and tagged for distribution.

## U25 — README + API reference
**Status:** 🟡 partial · **Depends on:** U13
- Quickstart (install, create config, Init, F8), API surface (`Init`/`Open`/`Capture`/`Shutdown`), config reference, backend setup link.
- **AC:** a new user can install and send their first report by following the README only.

## U26 — Basic Usage sample
**Status:** 🟡 partial · **Depends on:** U03
- Verify `Samples~/BasicUsage/BugyardBootstrap.cs` compiles against the current API and matches the README.
- **AC:** importing the sample and pressing F8 sends a report with no code changes.

## U27 — CHANGELOG + tagged release
**Status:** ⬜ todo · **Depends on:** U01, U02
- Fill `CHANGELOG.md` for the release; verify semver; tag; confirm UPM git install of the tag works.
- **AC:** a fresh project can install the tagged version via git URL and run the slice.

---

# Critical path (shippable v0.1)

```
U01 → U02 → U06 → U07 → U08 → U15 → U16 → U14 → U12 → U25 → U27
```

This takes the already-working capture slice (U03/U04/U05/U13/U19 done) to a hardened,
documented, multi-input-backend package. Offline queue (U17), input isolation (U11),
and the full test suite (U22–U24) are valuable but not blockers for the first tagged release.

# Status summary

- ✅ **done:** U03, U04, U05, U13, U14, U17, U19, U21, U22, U24
- 🟡 **partial:** U01, U02, U06, U07, U08, U15, U18, U25, U26
- ⬜ **todo:** U09, U10, U11, U12, U16, U20, U23, U27
