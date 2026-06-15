using System;
using System.IO;
using System.Runtime.CompilerServices;
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
