# Releasing

The SDK version lives in three places that must agree: `package.json#version`,
`BugyardVersion.Value` (the value compiled into builds and reported as
`sdkVersion`), and the release section in [`CHANGELOG.md`](changelog.md).

To release:

1. Bump `package.json#version`.
2. Run **Tools → Bugyard → Sync Version from package.json** to update
   `BugyardVersion.cs`.
3. Move the `[Unreleased]` notes in [`CHANGELOG.md`](changelog.md) into a dated
   `## [X.Y.Z]` section and add the matching `[X.Y.Z]:` link reference at the
   bottom.
4. Run the pre-publish check (below) and fix anything it flags.
5. Run the [manual smoke test](manual-smoke-test.md) and record the results in the
   release PR / notes.
6. Tag the release (`vX.Y.Z`).

!!! note
    The editor logs an error on load if the version values ever drift, and the
    `VersionSyncTests` enforce the same invariant (including the changelog entry)
    in CI — so a mismatch is caught before you ship.

## Pre-publish checklist

These are the packaging gates that must pass before tagging. They are automated
as a single script so you don't have to check them by hand:

```bash
python3 .github/scripts/pre-publish-check.py            # offline
python3 .github/scripts/pre-publish-check.py --check-urls  # also resolves URLs
```

It exits non-zero unless every gate passes:

- [x] Every `.cs` / `.asmdef` has a sibling `.meta` (the install-stability guard).
- [x] Every external package an `asmdef` references is declared in
      `package.json#dependencies`. Optional packages (Input System) are wired
      through `versionDefines`, so they are allowed to be absent.
- [x] `package.json#version`, `BugyardVersion.Value`, and the `CHANGELOG.md`
      entry are in lock-step.
- [x] The **Basic Usage** sample is present (and compiles — see CI below).
- [x] `package.json` has the required fields and follows UPM naming, and the
      `documentationUrl` / `changelogUrl` / `licensesUrl` are well-formed (and,
      with `--check-urls`, resolve).

## CI gates

The same gates run in CI from
[`.github/workflows/install-verification.yml`](https://github.com/Bugyard/unity-sdk/blob/main/.github/workflows/install-verification.yml):

- **Pre-publish hygiene** — runs the script above on every push and PR (no Unity
  editor needed; resolves the published URLs).
- **Package Validation Suite** — runs Unity's
  `com.unity.package-validation-suite` against the package in a real editor,
  checking `package.json` correctness, meta coverage, naming conventions, sample
  layout, and version/changelog consistency.
- **Install verification** — installs the package into a fresh project and runs
  the full test suite (including the **Basic Usage** sample compile) across the
  supported Unity versions, with and without the Input System package.

These gates run headless, so they can't see the overlay UI, a real key press, or a
live backend round-trip. The [manual smoke test](manual-smoke-test.md) is the human
complement that covers those before each tag.

## Docs site

This documentation is built with [MkDocs Material](https://squidfunk.github.io/mkdocs-material/)
from the `docs/` folder and published to GitHub Pages by the
`.github/workflows/docs.yml` workflow on every push to `main`.

To preview locally:

```bash
pip install -r docs/requirements.txt
mkdocs serve
```

Then open <http://127.0.0.1:8000>.
