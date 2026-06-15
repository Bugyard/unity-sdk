# U03 — Init / lifecycle / singleton

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** —

## Scope
- `Bugyard.Init(config)` / `Init(apiKey, endpoint)` create a hidden `DontDestroyOnLoad` runtime; duplicate Init guarded; null/empty key warned (`Runtime/Bugyard.cs`).
- Add `Bugyard.Shutdown()` to unhook the log handler and destroy the runtime (for tests / re-init).

## Acceptance criteria
- Init once works; second Init is ignored with a warning.
- Shutdown leaves a clean state so a later Init succeeds.
