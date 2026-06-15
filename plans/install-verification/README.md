# Install-Verification Plan

**Goal:** guarantee that when a stranger adds this package to their Unity project,
it compiles, resolves, and runs — not just that our unit tests pass.

Unit tests run *inside* a project where everything is already resolved, so they
cannot catch install-time failures (missing meta files, unresolved assembly
references, undeclared dependencies). This plan covers that gap.

## Status

Phases 1–4 are **implemented** in the repo (meta files committed, asmdef/​
`package.json` fixed, CI workflow + harness, hygiene scripts, and the manual
smoke-test docs). The only outstanding items are the Unity-license-gated CI legs
— a green run requires `UNITY_LICENSE`/`UNITY_EMAIL`/`UNITY_PASSWORD` secrets to
be added to the repo. Each phase doc records its own per-task status.

## Documents

| File | What it covers |
|------|----------------|
| [findings.md](findings.md) | Confirmed install-blocking problems in the current repo |
| [phase-1-blockers.md](phase-1-blockers.md) | 🔴 Must-do fixes that make a clean install work today |
| [phase-2-ci.md](phase-2-ci.md) | Automated install verification in CI (the real safety net) |
| [phase-3-hygiene.md](phase-3-hygiene.md) | Package hygiene gates (validation suite, pre-publish checks) |
| [phase-4-manual.md](phase-4-manual.md) | Manual smoke-test matrix as a release gate |

## Priority

- **Phase 1** is the only part that blocks shipping — the SDK does not work on a
  clean install until it is done.
- **Phases 2–4** prevent regressions and turn "works today" into "stays working."

## Open decisions

1. **Input System: required or optional?** The runtime supports legacy input via
   `#if ENABLE_INPUT_SYSTEM`, which implies *optional*. But the asmdef references
   `Unity.InputSystem` unconditionally. The choice drives the Phase 1 fix — see
   [phase-1-blockers.md](phase-1-blockers.md).
2. **Meta generation source of truth.** Generate by opening the package in a real
   Unity editor (authoritative GUIDs) vs. scripted generation.
