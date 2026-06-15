# Phase 4 — Manual smoke-test matrix (release gate)

A short human pass before each release, covering what automation cannot easily
assert (overlay UX, hotkey, real backend round-trip).

**Status: implemented.** The procedure, rationale, and matrix are documented for
humans at [`docs/manual-smoke-test.md`](../../docs/manual-smoke-test.md) (published
to the docs site and linked from the [`Releasing`](../../docs/releasing.md) flow as
step 5, before tagging). The fillable results record that gets pasted into each
release PR/notes is a single source at
[`.github/release-smoke-test-template.md`](../../.github/release-smoke-test-template.md),
included into the doc via a MkDocs snippet so the two can't drift. Running the pass
itself is, by definition, a human action at release time.

## Procedure

- [x] In a **clean Unity project**, open Package Manager → **Add package from git
      URL** → install this package. → Step 1 of the documented procedure.
- [x] Import the **Basic Usage** sample. → Step 3.
- [x] Enter **Play mode**. → Step 4.
- [x] Press the configured **hotkey** → the report overlay opens. → Step 5; the doc
      names the default (**F8**, `BugyardConfig.hotkey`).
- [x] Fill in and submit a report. → Step 6.
- [x] Run **Tools → Bugyard → Send Test Report** → confirm auth + endpoint
      round-trip succeeds. → Step 7.

> Documented as a 7-step list because the original step 1 ("clean project +
> install") is split into install (1) and config setup (2) — a fresh project has no
> API key/endpoint, so the round-trip step would otherwise have nothing to talk to.

## Matrix

- [x] Unity floor **2021.3** and a current LTS. → Matrix axis, mirroring the CI
      install matrix.
- [x] At least one standalone platform target. → Matrix axis; the template has a
      per-cell **Platform** column to record which target was used.
- [x] Input System present and absent (mirrors Phase 2.3). → Matrix axis; the
      template has one row per (Unity version × Input-System state) = four cells.

## Definition of done

- [x] All steps pass on every matrix cell; results recorded in the release notes /
      PR. → Enforced by process: the release flow (Releasing step 5) requires the
      filled-in [`release-smoke-test-template.md`](../../.github/release-smoke-test-template.md)
      in the PR, whose sign-off line gates tagging. The *act* of running it is human
      and happens per release, not at plan-authoring time.
