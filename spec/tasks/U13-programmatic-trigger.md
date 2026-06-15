# U13 — Programmatic trigger API

**Status:** ✅ done · **Milestone:** M3 — Input compatibility · **Depends on:** U03

## Scope
- `Bugyard.Open()` (overlay) and `Bugyard.Capture(ReportInput)` (headless) already exposed for custom UI / automation (`Runtime/Bugyard.cs`).
- Add usage docs/examples; ensure `Capture` works with no overlay present.

## Acceptance criteria
- A custom button can open the overlay or send a report without the built-in hotkey.
