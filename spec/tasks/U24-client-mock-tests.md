# U24 — Client tests against a mock server

**Status:** ✅ done · **Milestone:** M6 — Tests & QA · **Depends on:** U16, U17

## Scope
- Mock `/v1/reports` to assert: success parse, retry on 5xx/429, `Retry-After` honored, size-limit enforcement, error-code mapping, offline-queue replay.

## Acceptance criteria
- All upload paths covered; no network calls hit a real backend.
