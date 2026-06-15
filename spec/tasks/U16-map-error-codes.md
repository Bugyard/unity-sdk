# U16 — Map backend error codes

**Status:** ✅ done · **Milestone:** M4 — Upload reliability · **Depends on:** U15

## Scope
- Translate `{ error, message, details? }` codes (`REQUEST_NOT_VALID`, `PAYLOAD_TOO_LARGE`, `UNAUTHORIZED`, `REPORT_LIMIT_EXCEEDED`, 429) into friendly SDK-side messages.

## Acceptance criteria
- Each documented error code produces a distinct, actionable message.
- Unknown errors fall back gracefully.
