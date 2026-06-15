# Unity SDK Tasks

Per-ticket breakdown of the Unity SDK track, split from
[`../13-unity-sdk-tasks.md`](../13-unity-sdk-tasks.md). Each file is a self-contained ticket
with **status**, **scope**, **depends on**, and **acceptance criteria**.

The SDK is a thin client over the backend ingestion contract: it collects context in a Unity
build and `POST`s a multipart report to `/v1/reports`
(see `../11-implementation-order.md` §9 and `bugcapture-backend-docs/03-api-contracts.md`).

**Goal (§16):** Pressing F8 in a Unity build sends a report with screenshot, logs, scene name,
player position, and build version — visible in the dashboard.

Status legend: ✅ done · 🟡 partial · ⬜ todo

## Backend contract the SDK must honor

| Concern | Value |
|---|---|
| Endpoint | `POST {endpoint}/v1/reports`, `multipart/form-data` |
| Auth | `Authorization: Bearer bc_pk_{test\|live}_…` |
| Fields | `metadata` (JSON string, required), `screenshot`/`logs`/`events` (optional) |
| Idempotency | stable `clientReportId` → duplicate returns existing |
| Size limits | metadata 256KB, screenshot 5MB, logs 2MB, events 512KB |
| MIME | `image/png`, `image/jpeg`, `text/plain`, `application/json` |
| Errors | `{ error, message, details? }`; `REQUEST_NOT_VALID`, `FILE_TOO_LARGE`, `UNAUTHORIZED`, `REPORT_LIMIT_EXCEEDED`, 429 |

## Milestones & tickets

### M0 — Package & versioning hygiene
- [U01](U01-audit-package-layout.md) — Audit UPM package layout · 🟡
- [U02](U02-single-source-version.md) — Single-source the SDK version · 🟡

### M1 — Capture pipeline (core)
- [U03](U03-init-lifecycle.md) — Init / lifecycle / singleton · ✅
- [U04](U04-config-asset.md) — Config asset · ✅
- [U05](U05-screenshot-capture.md) — Screenshot capture · ✅
- [U06](U06-log-ring-buffer.md) — Unity log capture (ring buffer) · 🟡
- [U07](U07-metadata-collector.md) — Metadata collector · 🟡

### M2 — Overlay UX
- [U08](U08-overlay-category-expected.md) — Overlay: category + expected-result fields · 🟡
- [U09](U09-overlay-validation.md) — Overlay: input validation + length caps · ✅
- [U10](U10-overlay-result-feedback.md) — Overlay: send result feedback · ⬜
- [U11](U11-overlay-input-isolation.md) — Overlay: input isolation while open · ⬜

### M3 — Input compatibility
- [U12](U12-new-input-system.md) — New Input System support for the hotkey · ⬜
- [U13](U13-programmatic-trigger.md) — Programmatic trigger API · ✅

### M4 — Upload reliability
- [U14](U14-client-size-limits.md) — Client-side size limits · ⬜
- [U15](U15-surface-send-result.md) — Surface the send result · 🟡
- [U16](U16-map-error-codes.md) — Map backend error codes · ⬜
- [U17](U17-offline-queue.md) — Offline / failure queue · ⬜
- [U18](U18-retry-after.md) — Retry/backoff + Retry-After · 🟡

### M5 — Editor tooling & DX
- [U19](U19-config-menu.md) — Config asset menu · ✅
- [U20](U20-config-validation.md) — Config validation warnings · ⬜
- [U21](U21-send-test-report.md) — "Send test report" action · ✅

### M6 — Tests & QA
- [U22](U22-editmode-tests.md) — EditMode tests · ⬜
- [U23](U23-playmode-tests.md) — PlayMode tests · ⬜
- [U24](U24-client-mock-tests.md) — Client tests against a mock server · ⬜

### M7 — Docs, samples, release
- [U25](U25-readme-api-reference.md) — README + API reference · 🟡
- [U26](U26-basic-usage-sample.md) — Basic Usage sample · 🟡
- [U27](U27-changelog-release.md) — CHANGELOG + tagged release · ⬜

## Critical path (shippable v0.1)

```
U01 → U02 → U06 → U07 → U08 → U15 → U16 → U14 → U12 → U25 → U27
```

## Status summary

- ✅ **done:** U03, U04, U05, U13, U19, U21
- 🟡 **partial:** U01, U02, U06, U07, U08, U15, U18, U25, U26
- ⬜ **todo:** U09, U10, U11, U12, U14, U16, U17, U20, U22, U23, U24, U27
