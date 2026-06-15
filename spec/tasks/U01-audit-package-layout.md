# U01 — Audit UPM package layout

**Status:** 🟡 partial · **Milestone:** M0 — Package & versioning hygiene · **Depends on:** —

## Scope
- Confirm `package.json`, `Runtime/` + `Editor/` asmdefs, `Documentation~/`, `Samples~/BasicUsage/` follow UPM conventions.
- Replace `REPLACE_ME` placeholders in `package.json` (`documentationUrl`, `changelogUrl`, `licensesUrl`) with the real repo URL.
- Verify `unity: "2021.3"` minimum is accurate for the APIs used.

## Acceptance criteria
- Package installs into a clean project via `Add package from git URL`.
- No asmdef/compile errors.
