# U15 — Surface the send result

**Status:** ✅ done · **Milestone:** M4 — Upload reliability · **Depends on:** U03

## Scope
- `BugCaptureClient.Send` logs success/failure but returns nothing (`Runtime/BugCaptureClient.cs`).
- Parse the response (`reportId`, `status`, `dashboardUrl`) and expose it via callback/event so the overlay (U10) and callers can react.

## Acceptance criteria
- Callers receive a typed result (success + reportId/dashboardUrl, or failure + reason).
