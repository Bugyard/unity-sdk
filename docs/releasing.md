# Releasing

The SDK version lives in two places that must agree: `package.json#version` and
`BugyardVersion.Value` (the value compiled into builds and reported as
`sdkVersion`).

To release:

1. Bump `package.json#version`.
2. Run **Tools → Bugyard → Sync Version from package.json** to update
   `BugyardVersion.cs`.
3. Update [`CHANGELOG.md`](changelog.md) and tag the release (`vX.Y.Z`).

!!! note
    The editor logs an error on load if the two version values ever drift, so a
    mismatch is caught before you ship.

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
