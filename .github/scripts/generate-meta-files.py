#!/usr/bin/env python3
"""Generate Unity .meta files for every importable asset in this UPM package.

Unity requires a committed .meta (carrying a stable GUID) next to every asset it
imports, otherwise it mints a fresh per-machine GUID on install and asmdef/asset
references drift. We normally let the editor mint those GUIDs, but they only need
to be valid, unique and *stable* -- so we derive each GUID deterministically from
the asset's package-relative path (md5). Re-running is idempotent: an existing
.meta is never overwritten, so any GUIDs a real editor later adopts are kept.

Scope: code/asset folders Unity imports in place (Runtime, Editor, Tests, docs),
the package manifest, root text assets, and the on-demand-imported sample under
Samples~/BasicUsage. Hidden folders (dot-prefixed) and other tilde folders
(Documentation~) are deliberately skipped -- Unity ignores them.

Usage:  python3 .github/scripts/generate-meta-files.py [--check]
        --check : exit non-zero (and list) instead of creating missing metas.
"""
from __future__ import annotations

import hashlib
import os
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# Roots Unity imports as assets. Samples~ is a tilde folder Unity ignores in
# place, but its BasicUsage sample is copied into a consumer's project on demand,
# so committing metas there keeps the imported sample's GUIDs stable too.
ASSET_DIRS = ["Runtime", "Editor", "Tests", "docs", "Samples~/BasicUsage"]

# Individual files at the package root that Unity imports.
ROOT_FILES = ["package.json", "README.md", "CHANGELOG.md", "LICENSE", "mkdocs.yml"]


def guid_for(rel_path: str) -> str:
    """Deterministic 32-char hex GUID from the package-relative path."""
    return hashlib.md5(rel_path.replace(os.sep, "/").encode("utf-8")).hexdigest()


def importer_block(rel_path: str, is_dir: bool) -> str:
    name = os.path.basename(rel_path)
    ext = os.path.splitext(name)[1].lower()

    if is_dir:
        return "folderAsset: yes\nDefaultImporter:\n" + _tail()
    if ext == ".cs":
        return (
            "MonoImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  defaultReferences: []\n"
            "  executionOrder: 0\n"
            "  icon: {instanceID: 0}\n"
            "  userData: \n"
            "  assetBundleName: \n"
            "  assetBundleVariant: \n"
        )
    if ext == ".asmdef":
        return "AssemblyDefinitionImporter:\n" + _tail()
    if name == "package.json":
        return "PackageManifestImporter:\n" + _tail()
    if ext in (".md", ".txt", ".json"):
        return "TextScriptImporter:\n" + _tail()
    # Unknown extensions (LICENSE, *.yml) fall back to DefaultImporter, matching
    # how the editor treats assets it has no dedicated importer for.
    return "DefaultImporter:\n" + _tail()


def _tail() -> str:
    return (
        "  externalObjects: {}\n"
        "  userData: \n"
        "  assetBundleName: \n"
        "  assetBundleVariant: \n"
    )


def meta_text(rel_path: str, is_dir: bool) -> str:
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid_for(rel_path)}\n"
        f"{importer_block(rel_path, is_dir)}"
    )


def targets() -> list[tuple[str, bool]]:
    """Return (relative_path, is_dir) for every asset that should have a .meta."""
    out: list[tuple[str, bool]] = []

    for d in ASSET_DIRS:
        abs_d = os.path.join(REPO_ROOT, d)
        if not os.path.isdir(abs_d):
            continue
        out.append((d, True))  # the asset folder itself
        for dirpath, dirnames, filenames in os.walk(abs_d):
            dirnames[:] = sorted(n for n in dirnames if not n.startswith("."))
            for sub in dirnames:
                out.append((os.path.relpath(os.path.join(dirpath, sub), REPO_ROOT), True))
            for f in sorted(filenames):
                if f.startswith(".") or f.endswith(".meta"):
                    continue
                out.append((os.path.relpath(os.path.join(dirpath, f), REPO_ROOT), False))

    for f in ROOT_FILES:
        if os.path.isfile(os.path.join(REPO_ROOT, f)):
            out.append((f, False))

    return out


def main() -> int:
    check_only = "--check" in sys.argv[1:]
    missing: list[str] = []
    created = 0

    for rel_path, is_dir in targets():
        meta_path = os.path.join(REPO_ROOT, rel_path + ".meta")
        if os.path.exists(meta_path):
            continue
        if check_only:
            missing.append(rel_path)
            continue
        with open(meta_path, "w", newline="\n") as fh:
            fh.write(meta_text(rel_path, is_dir))
        created += 1

    if check_only:
        if missing:
            print("Missing .meta for:")
            for m in missing:
                print(f"  {m}")
            return 1
        print("All importable assets have a .meta.")
        return 0

    print(f"Created {created} .meta file(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
