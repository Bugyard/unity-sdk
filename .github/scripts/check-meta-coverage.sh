#!/usr/bin/env bash
# Fail if any .cs or .asmdef asset is missing its sibling .meta file.
#
# Unity needs a committed .meta (with a stable GUID) next to every imported
# asset; a missing one means per-machine GUIDs and broken asmdef/asset
# references on a clean install. This is a fast pre-check meant to run before
# the (slow) Unity CI legs -- see plans/install-verification/phase-2-ci.md.
#
# Usage: .github/scripts/check-meta-coverage.sh
set -euo pipefail

cd "$(dirname "$0")/../.."

missing=0
while IFS= read -r -d '' f; do
  if [ ! -f "$f.meta" ]; then
    echo "MISSING META: $f"
    missing=1
  fi
# Prune dot-directories (.git, .github, .idea, ...): Unity ignores hidden folders,
# so nothing under them is imported as an asset and none of it needs a .meta. The
# CI install harness and scripts live under .github for exactly that reason.
done < <(find . \
  \( -type d -name '.?*' -prune \) -o \
  \( -name '*.cs' -o -name '*.asmdef' \) -print0)

if [ "$missing" -ne 0 ]; then
  echo "FAIL: one or more source/asmdef files have no .meta. Run: python3 .github/scripts/generate-meta-files.py" >&2
  exit 1
fi

echo "OK: every .cs and .asmdef has a sibling .meta."
