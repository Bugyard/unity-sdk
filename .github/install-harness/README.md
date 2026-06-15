# Install-verification harness

A throwaway, empty Unity project used **only by CI** to prove that this package
installs cleanly into a fresh project — the scenario unit tests can't cover. See
[`plans/install-verification/phase-2-ci.md`](../../plans/install-verification/phase-2-ci.md).

## Why it lives under `.github/`

The package root *is* the UPM package, and `Packages/manifest.json` here installs
it by relative path:

```json
"com.bugyard.sdk": "file:../../.."
```

That points at the repo root, so Unity imports the package exactly as a consumer
would — through the Package Manager, **not** as embedded loose source in
`Assets/`. Because `.github/` is a dot-folder, Unity ignores it while importing
the package, so the harness can sit inside the repo without the package trying to
re-import its own harness.

## What CI does with it

The [`install-verification`](../workflows/install-verification.yml) workflow, per
matrix leg:

1. Optionally adds `com.unity.inputsystem` to `Packages/manifest.json` (the
   Input-System present/absent axis).
2. Copies `Samples~/BasicUsage/*.cs` into `Assets/` so the sample is compiled in
   install context.
3. Runs `game-ci/unity-test-runner` (EditMode + PlayMode). The package's tests
   are discovered because the package is listed under `testables`.

A compile or assembly-resolution error fails the test run, which is exactly the
install failure we want to catch.

## Local reproduction

```bash
# from the repo root, with a Unity 2021.3+ editor installed:
cp -r .github/install-harness /tmp/bugyard-harness
# open /tmp/bugyard-harness in the Unity Hub, or run the editor in batchmode:
#   <Unity> -projectPath .github/install-harness -runTests -testPlatform EditMode
```

`activeInputHandler: 0` (legacy) is set in `ProjectSettings.asset` so the
"without Input System" leg never trips Unity's "Input System package selected but
not installed" error; the optional new-input code still compiles whenever the
package is present, because `Bugyard.Runtime.asmdef` defines `ENABLE_INPUT_SYSTEM`
via `versionDefines`.
