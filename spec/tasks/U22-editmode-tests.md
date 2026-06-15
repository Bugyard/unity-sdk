# U22 — EditMode tests

**Status:** ✅ done · **Milestone:** M6 — Tests & QA · **Depends on:** U07

## Scope
- Test `MetadataCollector` output, severity→string mapping, config defaults, version sync.
- Add a test asmdef + `com.unity.test-framework` reference.

## Acceptance criteria
- Unity test runner executes EditMode tests; metadata/version assertions pass.

## Implementation
- `Tests/Editor/BugCapture.Tests.Editor.asmdef` — Editor-only test assembly (`UNITY_INCLUDE_TESTS`
  constraint, nunit + TestRunner references).
- `MetadataCollectorTests.cs` — config/constant fields, GUID uniqueness, title/category/playerPosition
  fallbacks, reporter pass-through/omission.
- `SeverityMappingTests.cs` — `Severity` → lowercase backend string for every enum member.
- `BugCaptureConfigTests.cs` — pins documented `BugCaptureConfig` defaults.
- `VersionSyncTests.cs` — asserts `BugCaptureVersion.Value` == `package.json#version`.
