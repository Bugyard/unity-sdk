# U07 — Metadata collector

**Status:** ✅ done · **Milestone:** M1 — Capture pipeline (core) · **Depends on:** U03

## Scope
- Builds `ReportMetadata` (clientReportId, env, build/engine/sdk versions, scene, player position, report, reporter, device, runtime) — `Runtime/MetadataCollector.cs`.
- `category` is wired from `input.category` → `config.defaultCategory`; severity is `Severity.ToString().ToLowerInvariant()` (low|medium|high|critical), matching backend lowercase enum.
- Optional `reporter` (id/name/email) added to match the backend schema field-for-field.
- Serialization moved off `JsonUtility` to `Runtime/MetadataJson.cs`, which omits empty optionals (JsonUtility emits them as `""`).

## Acceptance criteria
- Metadata matches the backend schema field-for-field.
- Category/severity reflect user choice; missing optionals omitted cleanly.
