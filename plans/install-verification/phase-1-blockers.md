# Phase 1 — Fix the blockers (must-do)

These are the only items that block shipping. Until they are done, the SDK does
not work on a clean install. See [findings.md](findings.md) for the evidence.

## 1.1 Generate & commit `.meta` files

- [x] ~~Open the package once in a real Unity editor~~ — **no editor available in
      this environment.** Instead, `.github/scripts/generate-meta-files.py` mints
      a valid, stable GUID per asset (`md5` of the package-relative path). GUIDs
      only need to be valid, unique and committed — Unity preserves whatever GUID
      it finds in a committed `.meta`, so this is equivalent for install
      stability. The generator is idempotent (never overwrites an existing
      `.meta`), so if the package is later opened in Unity those GUIDs are kept.
- [x] Generated `.meta` for every asset: each `.cs`, each `.asmdef`, each asset
      folder (Runtime/Editor/Tests + subfolders, docs), `package.json`, root text
      assets (README/CHANGELOG/LICENSE/mkdocs.yml), and the `Samples~/BasicUsage`
      assets. **Committed** (commit `ac71e5e`).
- [x] Verified: **zero orphans**, every `.cs`/`.asmdef` has a sibling `.meta`.
      `git ls-files | grep -c '\.meta$'` → 58.

**Guard against regression** (also wired into CI in Phase 2):

- [x] Added `.github/scripts/check-meta-coverage.sh` — fails (non-zero) if any
      `.cs`/`.asmdef` lacks a sibling `.meta`. (Rewritten from the snippet below,
      which always exits 0: its `missing=1` runs in the pipe's subshell and is
      lost. The script uses process substitution to avoid that.)

```bash
# original snippet — BUGGY: pipe puts the while-loop in a subshell, so
# `missing=1` never escapes and `exit $missing` is always 0.
missing=0
find . -name '*.cs' -o -name '*.asmdef' | grep -v '/.git/' | while read -r f; do
  [ -f "$f.meta" ] || { echo "MISSING META: $f"; missing=1; }
done
exit $missing
```

## 1.2 Resolve the Input System dependency

Pick one based on the **required vs. optional** decision (see README open
decisions):

### Option A — Input System is *required* (simplest)
- [ ] Add to `package.json`:
  ```json
  "dependencies": { "com.unity.inputsystem": "1.7.0" }
  ```
- [ ] Keep the asmdef reference. The dependency guarantees it resolves.
- Trade-off: forces the Input System package onto every consumer.

### Option B — Input System is *optional* (matches the `#if` guards) ✅ CHOSEN
Chosen because README "Requirements" promises the SDK works with **any** Active
Input Handling setting (legacy, new, both, or none), and every Input System call
in `BugyardRuntime.cs` is already behind `#if ENABLE_INPUT_SYSTEM`. Option A would
break that promise by forcing the package onto every consumer.

- [x] Removed the unconditional `Unity.InputSystem` entry from the asmdef
      `references` (now `[]`).
- [x] Re-added it via `versionDefines` so `ENABLE_INPUT_SYSTEM` is defined only
      when the package is present:
  ```json
  "versionDefines": [
    { "name": "com.unity.inputsystem", "expression": "1.0.0", "define": "ENABLE_INPUT_SYSTEM" }
  ]
  ```
  Why this links correctly: `Unity.InputSystem` is an **auto-referenced**
  assembly, so when the package is installed (`overrideReferences:false`) the
  assembly is referenced automatically — no explicit `references` entry needed —
  and when it is absent there is no unresolved-reference error. The static entry
  was both redundant and the cause of the DOA compile failure.
- [x] Legacy path: code is guarded by the Unity-builtin
      `ENABLE_LEGACY_INPUT_MANAGER` and falls back to `Bugyard.Open()`; with the
      Input System package absent, `ENABLE_INPUT_SYSTEM` is undefined and that
      code is excluded — assembly compiles with no InputSystem reference. *(Full
      both-states compile is the Phase 2 CI matrix; no editor here to run it.)*
- Trade-off: more config; must be tested in both states (covered in Phase 2).

## Definition of done

- [ ] Fresh empty project + this package installed by git URL → compiles with
      zero errors, both with and without the Input System package. **Requires a
      Unity editor → deferred to the Phase 2 CI matrix.** The code/config is ready
      for it.
- [x] Meta-coverage guard passes (`check-meta-coverage.sh` → OK). Metas are
      generated **and committed** (commit `ac71e5e`).
