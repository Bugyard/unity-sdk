# U19 — Config asset menu

**Status:** ✅ done · **Milestone:** M5 — Editor tooling & DX · **Depends on:** U04

## Scope
- `Tools > BugCapture > Create Config Asset` creates and pings the asset (`Editor/BugCaptureMenu.cs`).
- Add "select existing config if present" to avoid duplicates.

## Acceptance criteria
- Menu creates a config or selects the existing one; never silently makes duplicates.
