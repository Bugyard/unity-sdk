# U12 — New Input System support for the hotkey

**Status:** ✅ done · **Milestone:** M3 — Input compatibility · **Depends on:** U03

## Scope
- `Update()` is guarded by `ENABLE_LEGACY_INPUT_MANAGER` only (`BugCaptureRuntime.Update`); projects on the new Input System get no hotkey.
- Add an Input System path (`ENABLE_INPUT_SYSTEM`) and document the "Both" setting.

## Acceptance criteria
- F8 opens the overlay under legacy, new, and both input backends.
