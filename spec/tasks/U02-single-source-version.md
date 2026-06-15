# U02 — Single-source the SDK version

**Status:** 🟡 partial · **Milestone:** M0 — Package & versioning hygiene · **Depends on:** U01

## Scope
- `BugCaptureVersion.Value` (`Runtime/BugCaptureVersion.cs`) and `package.json#version` must match.
- Document/automate the sync (editor check or build step) so `sdkVersion` in metadata is never stale.

## Acceptance criteria
- Bumping the version in one place is caught if the other drifts.
- `sdkVersion` in a sent report equals `package.json` version.
