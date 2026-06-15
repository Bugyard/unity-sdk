# Phase 2 — Automated install verification in CI

This is the real safety net: it reproduces "someone installs the package" on
every change, which is the exact scenario unit tests cannot cover.

**Status: implemented.** See [`.github/workflows/install-verification.yml`](../../.github/workflows/install-verification.yml)
and the harness at [`.github/install-harness/`](../../.github/install-harness/).
The only remaining box is the "green CI run" in the definition of done, which
requires the Unity license secrets to be added to the repo (see Notes) — the
config is ready for it.

## 2.1 Compile-on-install check

- [x] Add a `game-ci/unity-test-runner` (or `game-ci/unity-builder`) workflow.
      → `install-verification.yml` uses `game-ci/unity-test-runner@v4`.
- [x] On every PR/push: create a **fresh empty Unity project**, install this
      package **by path/git URL** (not as embedded source), and assert it
      **compiles with zero errors**. → The committed harness at
      `.github/install-harness/` installs the package via `"com.bugyard.sdk":
      "file:../../.."` (Package Manager, not embedded loose source). It lives
      under `.github/` precisely so Unity's dot-folder rule keeps the package
      from re-importing its own harness.
- [x] Fail the job on any compile error or assembly-resolution error. → A
      compile/assembly-resolution error aborts the test run, failing the job.

## 2.2 Run the test suites in install context

- [x] Run **EditMode** and **PlayMode** suites against the installed package. →
      `testMode: all`; the package's tests are discovered via the manifest's
      `"testables": ["com.bugyard.sdk"]`.
- [x] Import the **Basic Usage** sample (`Samples~/BasicUsage`) and confirm it
      compiles. → The "Import Basic Usage sample" step copies the sample into the
      harness `Assets/` before the run, so a broken sample fails the build.

## 2.3 Matrix

- [x] **Unity versions:** at minimum the declared floor **2021.3** and a current
      LTS — catches version drift. → matrix `2021.3.45f1` + `2022.3.50f1`
      (bump patch versions to match available game-ci images).
- [x] **Input System present / absent:** locks in the Phase 1.2 fix so the
      optional/required behavior cannot silently break. → matrix `inputSystem:
      [without, with]`; the `with` leg adds `com.unity.inputsystem` to the
      manifest, which is the entire toggle since the asmdef gates
      `ENABLE_INPUT_SYSTEM` on package presence via `versionDefines`. The
      `without` leg keeps `ProjectSettings.activeInputHandler: 0` (legacy only),
      which also avoids Unity's "Input System selected but not installed" error.
      The `with` leg flips it to `2` (**Both**) so the new-input backend is
      actually active and the `ENABLE_LEGACY_INPUT_MANAGER + ENABLE_INPUT_SYSTEM`
      "both defined" path in `BugyardRuntime.HotkeyPressed` is compiled and run —
      not just the package-present-but-backend-off case.

## 2.4 Meta-coverage guard

- [x] Run the Phase 1.1 meta-coverage check as a fast pre-step so a missing
      `.meta` fails the build before the (slow) Unity legs start. → The
      `meta-coverage` job runs `check-meta-coverage.sh` and the Unity matrix job
      `needs: meta-coverage`.

## Notes

- `game-ci` needs Unity license activation via repo secrets (`UNITY_LICENSE` /
  `UNITY_EMAIL` / `UNITY_PASSWORD`). **Documented** in the README
  "Continuous integration" section; the workflow wires them as env on the
  `unity-test-runner` step. They still need to be *added to the repo* for the
  Unity legs to activate.
- Keep the empty-project harness in the repo (or generate it in the job) so the
  install path is reproducible. → Harness committed at `.github/install-harness/`
  (with `.github/install-harness/README.md` explaining it).

## Definition of done

- [~] A green CI run proves: fresh install compiles, samples compile, all tests
      pass, across the version + Input System matrix. → **Blocked only on adding
      the Unity license secrets to the repo.** Workflow, harness, matrix and
      meta-coverage gate are all in place.
