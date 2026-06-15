# Phase 3 — Package hygiene gates

Catches packaging mistakes that compile fine but break distribution or trust.

## 3.1 Unity Package Validation Suite

- [x] Runs `com.unity.package-validation-suite` in CI via the `package-validation`
      job in `.github/workflows/install-verification.yml`, across the same Unity
      versions as the install matrix (2021.3.45f1 + 2022.3.50f1).
- [x] The suite is invoked through a batch-mode editor runner,
      `.github/install-harness/validation/BugyardValidationRunner.cs`, which is
      copied into the harness project's `Assets/Editor` only for this job (so the
      faster install legs, which don't install the suite, don't compile it). It
      calls `ValidationSuite.ValidatePackage` reflectively (the API has shifted
      across versions) using the `LocalDevelopment` profile, echoes the report
      into the build log, and exits non-zero on failure.
- [x] game-ci's `unity-builder` runs it via `buildMethod` — the method owns the
      run and calls `EditorApplication.Exit`, so no player is actually built.
- It checks `package.json` correctness, meta-file coverage, naming conventions,
  sample layout, and version/changelog consistency.

> **Verification note:** like Phase 1.1 / Phase 2, the Unity leg cannot be run in
> this environment (no editor / no license). The runner and job are written and
> wired; they are exercised in CI on push/PR.

## 3.2 Version & changelog sync

- [x] `package.json#version`, `BugyardVersion.cs`, and `CHANGELOG.md` are kept in
      lockstep. `Tests/Editor/VersionSyncTests.cs` already covered
      `BugyardVersion.Value == package.json#version`; it now also has
      `Changelog_HasEntryForCurrentVersion`, which asserts a `## [x.y.z]` release
      section **and** a `[x.y.z]:` link reference exist in `CHANGELOG.md`.
- [x] These tests run in CI: the package is listed under `testables` in the
      harness manifest, so the install-verification job's EditMode run executes
      them against the installed package.
- [x] The same version+changelog invariant is also enforced (without an editor)
      by the pre-publish script — see 3.3.

## 3.3 Pre-publish checklist (script + doc)

Automated as a single script: `.github/scripts/pre-publish-check.py`. It runs
every gate and exits non-zero unless they all pass. Run it locally before tagging;
it also runs in CI as the `pre-publish` job (no Unity editor required).

- [x] Every `.cs`/`.asmdef` has a `.meta` (delegates to the Phase 1.1
      `check-meta-coverage.sh` guard).
- [x] Dependencies declared in `package.json` (Phase 1.2): every external package
      an asmdef references must be a declared dependency; optional packages wired
      through `versionDefines` (Input System) are allowed to be absent.
- [x] Version bumped and `CHANGELOG.md` updated (version sync across all three
      sources + changelog section and link reference).
- [x] Basic Usage sample present (compile is verified by the install job).
- [x] `documentationUrl` / `changelogUrl` / `licensesUrl` are well-formed and the
      on-repo targets exist; with `--check-urls` they are also resolved live (the
      CI `pre-publish` job passes `--check-urls`).

The checklist is documented for humans in [`docs/releasing.md`](../../docs/releasing.md).

## Definition of done

- [x] Validation suite wired to run in CI (3.1).
- [x] Pre-publish checklist documented **and** automated as a single script that
      passes today (`python3 .github/scripts/pre-publish-check.py` → exit 0).
- [x] Both run as CI jobs in `install-verification.yml` (`pre-publish` and
      `package-validation`).
