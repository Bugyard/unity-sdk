using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace BugyardSDK.Tests
{
    /// <summary>
    /// <see cref="BugyardVersion.Value"/> is compiled into builds and sent as
    /// <c>sdkVersion</c>; it must stay in lock-step with <c>package.json#version</c>
    /// (the same invariant BugyardVersionCheck enforces in the editor). This catches
    /// drift in CI before it ships an inaccurate version to the dashboard.
    /// </summary>
    public class VersionSyncTests
    {
        [Serializable]
        class Manifest
        {
            public string version;
        }

        [Test]
        public void BugyardVersion_MatchesPackageJson()
        {
            string packageRoot = FindPackageRoot();
            Assert.IsNotNull(packageRoot, "Could not locate package.json above the test source file.");

            string json = File.ReadAllText(Path.Combine(packageRoot, "package.json"));
            Manifest manifest = JsonUtility.FromJson<Manifest>(json);

            Assert.IsNotNull(manifest, "package.json failed to parse.");
            Assert.IsFalse(string.IsNullOrEmpty(manifest.version), "package.json#version is empty.");
            Assert.AreEqual(manifest.version, BugyardVersion.Value,
                "BugyardVersion.Value and package.json#version have drifted. " +
                "Run Tools/Bugyard/Sync Version from package.json.");
        }

        // The third leg of the version invariant: the version being shipped must be
        // documented. A "## [x.y.z]" heading and a matching "[x.y.z]:" link reference
        // in CHANGELOG.md prove the release was written up (Keep a Changelog format)
        // and that the comparison/tag link was wired. This catches the common mistake
        // of bumping package.json without adding a changelog entry — see Phase 3.2.
        [Test]
        public void Changelog_HasEntryForCurrentVersion()
        {
            string packageRoot = FindPackageRoot();
            Assert.IsNotNull(packageRoot, "Could not locate package.json above the test source file.");

            string changelogPath = Path.Combine(packageRoot, "CHANGELOG.md");
            Assert.IsTrue(File.Exists(changelogPath), "CHANGELOG.md is missing from the package root.");

            string changelog = File.ReadAllText(changelogPath);
            string version = BugyardVersion.Value;
            // Escape the version for use inside a regex (the dots are literal).
            string escaped = Regex.Escape(version);

            // A released section heading, e.g. "## [0.1.0] - 2026-06-15".
            Assert.IsTrue(
                Regex.IsMatch(changelog, $@"(?m)^##\s*\[{escaped}\]"),
                $"CHANGELOG.md has no \"## [{version}]\" section heading. " +
                "Bump the [Unreleased] notes into a dated release section before publishing.");

            // A link-reference definition, e.g. "[0.1.0]: https://.../tag/v0.1.0".
            Assert.IsTrue(
                Regex.IsMatch(changelog, $@"(?m)^\[{escaped}\]:\s*\S+"),
                $"CHANGELOG.md has no \"[{version}]:\" link reference. " +
                "Add the release/compare link at the bottom of the changelog.");
        }

        // Walk up from this source file (resolved at compile time, so it works regardless of
        // where the package is mounted in the consuming project) to the folder holding package.json.
        static string FindPackageRoot([CallerFilePath] string sourceFilePath = "")
        {
            DirectoryInfo dir = Directory.GetParent(sourceFilePath);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
