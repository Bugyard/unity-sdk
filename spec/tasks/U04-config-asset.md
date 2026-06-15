# U04 — Config asset

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** —

## Scope
- `BugyardConfig` ScriptableObject with apiKey, endpoint, environment, hotkey, capture toggles, `maxLogLines` (`Runtime/BugyardConfig.cs`).
- Add tunables this list relies on: client-side size caps (screenshot/logs/metadata) and default `category`.

## Acceptance criteria
- Config drives all capture behavior.
- New fields have tooltips and sane defaults.
