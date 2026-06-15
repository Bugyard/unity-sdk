# U17 — Offline / failure queue

**Status:** ✅ done · **Milestone:** M4 — Upload reliability · **Depends on:** U15

## Scope
- Persist failed reports (metadata + attachments) to disk and retry on next launch; the stable `clientReportId` keeps retries idempotent.
- Bound the queue size; drop oldest when full.

## Acceptance criteria
- A report submitted while offline is delivered on a later online launch with no duplicate created.
