# U14 — Client-side size limits

**Status:** ✅ done · **Milestone:** M4 — Upload reliability · **Depends on:** U04

## Scope
- Before upload, enforce config caps (screenshot 5MB, logs 2MB, metadata 256KB) — trim logs / drop or downscale screenshot rather than ship a payload the backend rejects with `PAYLOAD_TOO_LARGE`.

## Acceptance criteria
- An oversized screenshot/log is reduced or dropped with a warning.
- Metadata never exceeds 256KB.
