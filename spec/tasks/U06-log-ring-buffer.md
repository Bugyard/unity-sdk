# U06 — Unity log capture (ring buffer)

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** U03

## Scope
- Thread-safe ring buffer on `Application.logMessageReceivedThreaded`, bounded by `maxLogLines` (`BugCaptureRuntime.OnLog`).
- Gap: include stack traces for `Error`/`Exception` entries (currently only `[type] condition`).

## Acceptance criteria
- Logs snapshot includes recent messages with stack traces for errors.
- Buffer never exceeds `maxLogLines`; handler unhooked on destroy.
