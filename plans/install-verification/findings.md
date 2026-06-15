# Findings — current install risks

Two problems will prevent the SDK from working on a clean install **today**.
Both were confirmed by inspecting the repo on branch `main`.

## 🔴 1. No `.meta` files exist at all

- Zero `.meta` files on disk; zero tracked in git.
- The repo's own `.gitignore` documents the requirement:
  > `.meta` files are NOT ignored — Unity requires them committed for git packages.

**Why it breaks installs:** distributed as a git UPM package, Unity regenerates
GUIDs per machine when meta files are absent. That makes asmdef GUID references
and sample/asset links unstable across consumers and produces import warnings.

**Affected:** every `.cs`, every `.asmdef`, every folder, `package.json`, and the
`Samples~/BasicUsage` assets.

## 🔴 2. `Unity.InputSystem` referenced but not depended on

- `Runtime/Bugyard.Runtime.asmdef` lists `Unity.InputSystem` in `references`.
- `package.json` has **no `dependencies` block**.
- Runtime code is guarded with `#if ENABLE_INPUT_SYSTEM`
  (`Runtime/BugyardRuntime.cs:6,202,213`), but the **asmdef reference itself is
  unconditional**.

**Why it breaks installs:** in a project without the Input System package, the
unconditional asmdef reference fails with `reference could not be resolved`, so
the entire `Bugyard.Runtime` assembly fails to compile — the SDK is dead on
arrival.

## Why our tests miss both

Unit tests (`Tests/Editor`, `Tests/Runtime`) execute inside a project where the
assemblies and packages are already resolved. Install-correctness is a different
property and needs the verification described in [phase-2-ci.md](phase-2-ci.md).
