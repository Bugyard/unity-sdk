# U08 — Overlay: category + expected-result fields

**Status:** 🟡 partial · **Milestone:** M2 — Overlay UX · **Depends on:** U07

## Scope
- IMGUI overlay currently has title / description / severity (`BugCaptureRuntime.OnGUI`).
- Add a `category` selector and an `expectedResult` field (both exist in `ReportInput`/backend schema).

## Acceptance criteria
- Overlay collects title, description, expected result, severity, category and passes them through to metadata.
