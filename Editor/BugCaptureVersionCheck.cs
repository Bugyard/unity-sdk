#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace BugCaptureSDK.Editor
{
    /// <summary>
    /// Single-sources the SDK version: <see cref="BugCaptureVersion.Value"/> (compiled into
    /// builds and sent as <c>sdkVersion</c>) must equal <c>package.json#version</c>.
    /// Runs on editor load and logs an error if the two drift, and offers a one-click sync.
    /// </summary>
    [InitializeOnLoad]
    static class BugCaptureVersionCheck
    {
        const string PackageName = "com.bugcapture.sdk";

        static BugCaptureVersionCheck()
        {
            // Defer so AssetDatabase is ready during domain reload / first import.
            EditorApplication.delayCall += Validate;
        }

        [MenuItem("Tools/BugCapture/Check Version Sync")]
        static void Validate()
        {
            if (!TryGetPackageVersion(out string packageVersion))
                return; // Could not locate package.json; nothing to compare against.

            if (packageVersion != BugCaptureVersion.Value)
            {
                Debug.LogError(
                    $"[BugCapture] SDK version drift: BugCaptureVersion.Value is \"{BugCaptureVersion.Value}\" " +
                    $"but package.json#version is \"{packageVersion}\". These must match so the reported " +
                    "sdkVersion is accurate. Run Tools/BugCapture/Sync Version from package.json to fix.");
            }
        }

        [MenuItem("Tools/BugCapture/Sync Version from package.json")]
        static void Sync()
        {
            if (!TryGetPackageVersion(out string packageVersion))
            {
                Debug.LogError("[BugCapture] Could not locate package.json to sync the SDK version.");
                return;
            }

            if (packageVersion == BugCaptureVersion.Value)
            {
                Debug.Log($"[BugCapture] SDK version already in sync ({packageVersion}).");
                return;
            }

            string scriptPath = FindAssetPath("BugCaptureVersion");
            if (scriptPath == null)
            {
                Debug.LogError("[BugCapture] Could not locate BugCaptureVersion.cs to update.");
                return;
            }

            string absolutePath = Path.GetFullPath(scriptPath);
            string contents = File.ReadAllText(absolutePath);
            string updated = System.Text.RegularExpressions.Regex.Replace(
                contents,
                "(public const string Value = \")[^\"]*(\";)",
                "${1}" + packageVersion + "${2}");

            if (updated == contents)
            {
                Debug.LogError("[BugCapture] Could not find the Value constant in BugCaptureVersion.cs.");
                return;
            }

            File.WriteAllText(absolutePath, updated);
            AssetDatabase.ImportAsset(scriptPath);
            Debug.Log($"[BugCapture] Synced BugCaptureVersion.Value to {packageVersion} from package.json.");
        }

        static bool TryGetPackageVersion(out string version)
        {
            version = null;

            // Preferred: resolved package metadata (works for installed UPM packages).
            PackageInfo pkg = PackageInfo.FindForAssembly(typeof(BugCaptureVersion).Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.version))
            {
                version = pkg.version;
                return true;
            }

            // Fallback: read package.json from disk (embedded in Assets/ during development).
            string manifestPath = FindPackageManifestPath();
            if (manifestPath == null)
                return false;

            try
            {
                var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
                if (manifest != null && !string.IsNullOrEmpty(manifest.version))
                {
                    version = manifest.version;
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BugCapture] Failed to read package.json: {e.Message}");
            }

            return false;
        }

        static string FindPackageManifestPath()
        {
            // Walk up from this editor script to the package root containing package.json.
            string scriptPath = FindAssetPath("BugCaptureVersionCheck");
            if (scriptPath == null)
                return null;

            DirectoryInfo dir = Directory.GetParent(Path.GetFullPath(scriptPath));
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "package.json");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        static string FindAssetPath(string scriptName)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{scriptName} t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == scriptName)
                    return path;
            }

            return null;
        }

        [Serializable]
        class Manifest
        {
            public string version;
        }
    }
}
#endif
