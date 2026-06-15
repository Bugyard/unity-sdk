# U04 — Config asset

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** —

## Scope
- `BugCaptureConfig` ScriptableObject with apiKey, endpoint, environment, hotkey, capture toggles, `maxLogLines` (`Runtime/BugCaptureConfig.cs`).
- Add tunables this list relies on: client-side size caps (screenshot/logs/metadata) and default `category`.

## Acceptance criteria
- Config drives all capture behavior.
- New fields have tooltips and sane defaults.
