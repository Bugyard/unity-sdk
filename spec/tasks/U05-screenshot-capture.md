# U05 — Screenshot capture

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** U03

## Scope
- Overlay is hidden before `CaptureScreenshotAsTexture` so it isn't in the shot; PNG encoded; texture destroyed (`BugyardRuntime.CaptureRoutine`).
- Verify behavior with multiple displays / non-default render pipelines; document limitations.

## Acceptance criteria
- Captured PNG contains the game frame without the overlay.
- No leaked textures.
