#!/usr/bin/env python3
"""Pre-publish hygiene gate for the Bugyard Unity package.

One script that runs every check from Phase 3.3 so a release can't go out with a
packaging mistake that compiles fine but breaks distribution or trust:

  1. Meta coverage      - every .cs / .asmdef has a sibling .meta (delegates to
                          check-meta-coverage.sh, the Phase 1.1 guard).
  2. Dependencies       - every external package an asmdef references is declared
                          in package.json#dependencies (Phase 1.2). Optional
                          packages wired only through versionDefines are allowed
                          to be absent (that is what "optional" means).
  3. Version + changelog - package.json#version == BugyardVersion.Value, and the
                          version has a "## [x.y.z]" section and a "[x.y.z]:" link
                          reference in CHANGELOG.md (Phase 3.2).
  4. Sample             - the Basic Usage sample declared in package.json exists
                          and contains a .cs file (compile is verified in CI).
  5. package.json       - required fields present and well-formed; documentation/
                          changelog/license URLs are well-formed and the on-repo
                          targets (CHANGELOG.md, LICENSE) exist. With --check-urls
                          the URLs are also resolved over the network.

Exit code is 0 only if every gate passes. Run it locally before tagging a
release, and in CI (it needs no Unity editor).

Usage:
    python3 .github/scripts/pre-publish-check.py [--check-urls]

    --check-urls   Additionally perform live HTTP(S) HEAD/GET requests against
                   documentationUrl / changelogUrl / licensesUrl. Off by default
                   so the check stays deterministic and works offline.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPTS_DIR = Path(__file__).resolve().parent

# Unity built-in module / engine assemblies are always available in any project
# and are never declared in package.json#dependencies. Anything an asmdef
# references that is neither one of these, nor a sibling asmdef defined in this
# repo, must be a declared dependency.
UNITY_BUILTIN_PREFIXES = (
    "UnityEngine",
    "UnityEditor",
    "Unity.",            # e.g. Unity.TestRunner, Unity.InputSystem (when referenced)
)

# Reset / bold are only used when stdout is a TTY.
class C:
    OK = "\033[32m"
    WARN = "\033[33m"
    FAIL = "\033[31m"
    BOLD = "\033[1m"
    END = "\033[0m"


_USE_COLOR = sys.stdout.isatty()


def _c(text: str, color: str) -> str:
    return f"{color}{text}{C.END}" if _USE_COLOR else text


class Reporter:
    """Collects pass/fail results so the whole suite runs before exiting."""

    def __init__(self) -> None:
        self.failures: list[str] = []
        self.warnings: list[str] = []

    def ok(self, msg: str) -> None:
        print(f"  {_c('PASS', C.OK)}  {msg}")

    def fail(self, msg: str) -> None:
        print(f"  {_c('FAIL', C.FAIL)}  {msg}")
        self.failures.append(msg)

    def warn(self, msg: str) -> None:
        print(f"  {_c('WARN', C.WARN)}  {msg}")
        self.warnings.append(msg)

    def section(self, title: str) -> None:
        print(f"\n{_c(title, C.BOLD)}")


def load_package_json(rep: Reporter) -> dict | None:
    path = REPO_ROOT / "package.json"
    if not path.exists():
        rep.fail("package.json not found at repo root.")
        return None
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as e:
        rep.fail(f"package.json is not valid JSON: {e}")
        return None


# ---------------------------------------------------------------------------
# 1. Meta coverage (Phase 1.1)
# ---------------------------------------------------------------------------
def check_meta_coverage(rep: Reporter) -> None:
    rep.section("1. Meta-file coverage")
    script = SCRIPTS_DIR / "check-meta-coverage.sh"
    if not script.exists():
        rep.fail(f"{script} is missing.")
        return
    result = subprocess.run(
        ["bash", str(script)], capture_output=True, text=True
    )
    if result.returncode == 0:
        rep.ok("Every .cs / .asmdef has a sibling .meta.")
    else:
        out = (result.stdout + result.stderr).strip()
        rep.fail("Missing .meta file(s):\n" + "\n".join("        " + line for line in out.splitlines()))


# ---------------------------------------------------------------------------
# 2. Dependencies declared (Phase 1.2)
# ---------------------------------------------------------------------------
def _iter_asmdefs() -> list[Path]:
    # Skip hidden directories (.git, .github, ...): Unity ignores them, so any
    # asmdef there (e.g. the CI harness) is not part of the shipped package.
    return [
        p for p in REPO_ROOT.rglob("*.asmdef")
        if not any(part.startswith(".") for part in p.relative_to(REPO_ROOT).parts)
    ]


def check_dependencies(rep: Reporter, pkg: dict) -> None:
    # Boundary: this gate inspects asmdef `references` only. An auto-referenced
    # *external* package reached through a bare `using` (no asmdef entry, but the
    # package must still be installed) would not appear here. The one such case in
    # this repo — Input System — is gated behind versionDefines/#if and is meant
    # to be optional, so it is intentionally allowed to be absent. If a future
    # change relies on an auto-referenced external package that is NOT optional,
    # add it to package.json#dependencies and reference it explicitly; this check
    # will not flag it for you.
    rep.section("2. Dependencies declared in package.json")

    declared = set(pkg.get("dependencies", {}).keys())
    # Optional packages are wired through versionDefines, not references; they are
    # allowed to be absent on a consumer's machine, so they must NOT be required
    # dependencies. We collect them to explain why a reference is permitted.
    optional_pkgs: set[str] = set()
    local_asmdef_names: set[str] = set()
    asmdefs: list[tuple[Path, dict]] = []

    for path in _iter_asmdefs():
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            rep.fail(f"{path.relative_to(REPO_ROOT)} is not valid JSON: {e}")
            continue
        asmdefs.append((path, data))
        if "name" in data:
            local_asmdef_names.add(data["name"])
        for vd in data.get("versionDefines", []):
            if "name" in vd:
                optional_pkgs.add(vd["name"])

    problems = 0
    for path, data in asmdefs:
        rel = path.relative_to(REPO_ROOT)
        for ref in data.get("references", []):
            # References are either a sibling asmdef name, a GUID:... reference,
            # a Unity built-in assembly, or an external package assembly.
            if ref.startswith("GUID:"):
                continue
            if ref in local_asmdef_names:
                continue
            if ref.startswith(UNITY_BUILTIN_PREFIXES):
                continue
            # An external package assembly: it must be a declared dependency.
            if ref not in declared:
                problems += 1
                rep.fail(
                    f"{rel} references '{ref}' but it is not in "
                    "package.json#dependencies."
                )

    if problems == 0:
        detail = "no undeclared external references."
        if optional_pkgs:
            detail += (
                f" Optional packages via versionDefines (allowed to be absent): "
                f"{', '.join(sorted(optional_pkgs))}."
            )
        rep.ok(f"asmdef references are consistent with package.json — {detail}")


# ---------------------------------------------------------------------------
# 3. Version + changelog sync (Phase 3.2)
# ---------------------------------------------------------------------------
def read_bugyard_version(rep: Reporter) -> str | None:
    path = REPO_ROOT / "Runtime" / "BugyardVersion.cs"
    if not path.exists():
        rep.fail("Runtime/BugyardVersion.cs not found.")
        return None
    m = re.search(r'public\s+const\s+string\s+Value\s*=\s*"([^"]*)"', path.read_text(encoding="utf-8"))
    if not m:
        rep.fail("Could not find `public const string Value = \"...\"` in BugyardVersion.cs.")
        return None
    return m.group(1)


def check_version_and_changelog(rep: Reporter, pkg: dict) -> None:
    rep.section("3. Version & changelog sync")

    pkg_version = pkg.get("version")
    if not pkg_version:
        rep.fail("package.json#version is missing or empty.")
        return
    rep.ok(f"package.json#version = {pkg_version}")

    code_version = read_bugyard_version(rep)
    if code_version is None:
        return
    if code_version == pkg_version:
        rep.ok(f"BugyardVersion.Value matches ({code_version}).")
    else:
        rep.fail(
            f"Version drift: BugyardVersion.Value = '{code_version}' but "
            f"package.json#version = '{pkg_version}'. "
            "Run Tools/Bugyard/Sync Version from package.json."
        )

    changelog_path = REPO_ROOT / "CHANGELOG.md"
    if not changelog_path.exists():
        rep.fail("CHANGELOG.md not found.")
        return
    changelog = changelog_path.read_text(encoding="utf-8")
    escaped = re.escape(pkg_version)
    if re.search(rf"(?m)^##\s*\[{escaped}\]", changelog):
        rep.ok(f"CHANGELOG.md has a '## [{pkg_version}]' release section.")
    else:
        rep.fail(
            f"CHANGELOG.md has no '## [{pkg_version}]' section. "
            "Move the [Unreleased] notes into a dated release section."
        )
    if re.search(rf"(?m)^\[{escaped}\]:\s*\S+", changelog):
        rep.ok(f"CHANGELOG.md has a '[{pkg_version}]:' link reference.")
    else:
        rep.fail(
            f"CHANGELOG.md has no '[{pkg_version}]:' link reference at the bottom."
        )


# ---------------------------------------------------------------------------
# 4. Sample present (compile is verified in CI)
# ---------------------------------------------------------------------------
def check_samples(rep: Reporter, pkg: dict) -> None:
    rep.section("4. Samples")
    samples = pkg.get("samples", [])
    if not samples:
        rep.warn("package.json declares no samples.")
        return
    for sample in samples:
        path = sample.get("path", "")
        name = sample.get("displayName", path)
        sample_dir = REPO_ROOT / path
        if not sample_dir.is_dir():
            rep.fail(f"Sample '{name}' path '{path}' does not exist.")
            continue
        cs_files = list(sample_dir.rglob("*.cs"))
        if not cs_files:
            rep.warn(f"Sample '{name}' has no .cs files.")
        else:
            rep.ok(
                f"Sample '{name}' present ({len(cs_files)} .cs). "
                "Compile is verified by the install-verification workflow."
            )


# ---------------------------------------------------------------------------
# 5. package.json correctness + URLs
# ---------------------------------------------------------------------------
REQUIRED_FIELDS = ("name", "version", "displayName", "description", "unity")
URL_FIELDS = ("documentationUrl", "changelogUrl", "licensesUrl")
# changelogUrl / licensesUrl should point at files that exist in the repo.
URL_TO_LOCAL_FILE = {
    "changelogUrl": "CHANGELOG.md",
    "licensesUrl": "LICENSE",
}


def check_package_json(rep: Reporter, pkg: dict, check_urls: bool) -> None:
    rep.section("5. package.json correctness")

    for field in REQUIRED_FIELDS:
        if not pkg.get(field):
            rep.fail(f"package.json is missing required field '{field}'.")
        else:
            rep.ok(f"'{field}' present.")

    name = pkg.get("name", "")
    # UPM naming convention: reverse-DNS, lowercase, no spaces.
    if name and not re.fullmatch(r"[a-z0-9]+(\.[a-z0-9][a-z0-9-]*)+", name):
        rep.fail(f"package name '{name}' does not follow UPM reverse-DNS naming.")

    for field in URL_FIELDS:
        url = pkg.get(field)
        if not url:
            rep.warn(f"package.json has no '{field}'.")
            continue
        if not re.match(r"^https?://", url):
            rep.fail(f"'{field}' is not an http(s) URL: {url}")
            continue
        # The changelog/license URLs should resolve to files we actually ship.
        local = URL_TO_LOCAL_FILE.get(field)
        if local and not (REPO_ROOT / local).exists():
            rep.fail(f"'{field}' implies a {local} file, but it is missing from the repo.")
        else:
            rep.ok(f"'{field}' is a well-formed URL.")

    if check_urls:
        _check_urls_live(rep, pkg)


def _check_urls_live(rep: Reporter, pkg: dict) -> None:
    rep.section("5b. URL resolution (network)")
    for field in URL_FIELDS:
        url = pkg.get(field)
        if not url or not re.match(r"^https?://", url):
            continue
        try:
            req = urllib.request.Request(url, method="GET", headers={"User-Agent": "bugyard-pre-publish"})
            with urllib.request.urlopen(req, timeout=15) as resp:
                code = resp.getcode()
            if 200 <= code < 400:
                rep.ok(f"{field} -> HTTP {code}: {url}")
            else:
                rep.fail(f"{field} -> HTTP {code}: {url}")
        except urllib.error.HTTPError as e:
            rep.fail(f"{field} -> HTTP {e.code}: {url}")
        except (urllib.error.URLError, TimeoutError, OSError) as e:
            rep.warn(f"{field} could not be reached ({e}); skipping. URL: {url}")


def main() -> int:
    check_urls = "--check-urls" in sys.argv[1:]

    print(_c("Bugyard package pre-publish check", C.BOLD))
    print(f"Repo: {REPO_ROOT}")

    rep = Reporter()
    pkg = load_package_json(rep)

    check_meta_coverage(rep)
    if pkg is not None:
        check_dependencies(rep, pkg)
        check_version_and_changelog(rep, pkg)
        check_samples(rep, pkg)
        check_package_json(rep, pkg, check_urls)

    print()
    if rep.warnings:
        print(_c(f"{len(rep.warnings)} warning(s).", C.WARN))
    if rep.failures:
        print(_c(f"FAIL: {len(rep.failures)} gate(s) failed. Fix before publishing.", C.FAIL))
        return 1
    print(_c("OK: all pre-publish gates passed.", C.OK))
    return 0


if __name__ == "__main__":
    sys.exit(main())
