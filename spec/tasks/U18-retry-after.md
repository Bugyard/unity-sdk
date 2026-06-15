# U18 — Retry/backoff + Retry-After

**Status:** 🟡 partial · **Milestone:** M4 — Upload reliability · **Depends on:** U15

## Scope
- Exponential backoff over 3 attempts on transient failures (0/429/5xx) already exists (`BugyardClient.Send`).
- Honor the `Retry-After` header on 429 instead of fixed backoff.

## Acceptance criteria
- On 429 the client waits the server-specified interval.
- Non-retryable errors fail fast.
