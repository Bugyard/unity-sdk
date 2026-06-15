#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BugyardSDK.Editor
{
    /// <summary>
    /// Editor-time validation for <see cref="BugyardConfig"/>. The same checks back the
    /// custom Inspector (so misconfiguration is visible while editing) and a build pre-processor
    /// (so it's logged to the console before a build ships).
    /// </summary>
    static class BugyardConfigValidation
    {
        public const string LiveKeyPrefix = "by_pk_live_";
        public const string KeyPrefix = "by_pk_";

        public readonly struct Issue
        {
            public readonly MessageType Severity;
            public readonly string Message;

            public Issue(MessageType severity, string message)
            {
                Severity = severity;
                Message = message;
            }
        }

        /// <summary>Returns every misconfiguration found on <paramref name="config"/>, in display order.</summary>
        public static List<Issue> Validate(BugyardConfig config)
        {
            var issues = new List<Issue>();
            if (config == null)
                return issues;

            string apiKey = config.apiKey == null ? "" : config.apiKey.Trim();

            if (string.IsNullOrEmpty(apiKey))
            {
                issues.Add(new Issue(MessageType.Warning,
                    "apiKey is empty. Reports will be rejected with 401 Unauthorized. " +
                    "Paste your by_pk_test_… (or by_pk_live_…) key in the Inspector."));
            }
            else if (apiKey.StartsWith(LiveKeyPrefix))
            {
                issues.Add(new Issue(MessageType.Warning,
                    "A live API key (by_pk_live_…) is stored in this asset, which is committed to source control. " +
                    "Use a by_pk_test_… key for development, or inject the live key at runtime via " +
                    "Bugyard.Init(apiKey, …) so it never lands in version control."));
            }
            else if (!apiKey.StartsWith(KeyPrefix))
            {
                issues.Add(new Issue(MessageType.Warning,
                    "apiKey doesn't look like a Bugyard key (expected a by_pk_test_… or by_pk_live_… prefix). " +
                    "Double-check you copied the whole key."));
            }

            if (IsPlaceholderEndpoint(config.endpoint, out string endpointReason))
            {
                issues.Add(new Issue(MessageType.Warning,
                    $"endpoint \"{config.endpoint}\" looks like a placeholder ({endpointReason}). " +
                    "Set it to your Bugyard backend base URL (e.g. https://api.bugyard.com, no trailing /v1)."));
            }

            return issues;
        }

        static bool IsPlaceholderEndpoint(string endpoint, out string reason)
        {
            string value = endpoint == null ? "" : endpoint.Trim();

            if (string.IsNullOrEmpty(value))
            {
                reason = "it's empty";
                return true;
            }

            if (!value.StartsWith("http://") && !value.StartsWith("https://"))
            {
                reason = "it's not an http(s) URL";
                return true;
            }

            string lower = value.ToLowerInvariant();
            string[] markers = { "example.", "your-", "yourdomain", "changeme", "todo", "<", ">" };
            foreach (string marker in markers)
            {
                if (lower.Contains(marker))
                {
                    reason = $"it contains \"{marker}\"";
                    return true;
                }
            }

            if (value.TrimEnd('/').EndsWith("/v1"))
            {
                reason = "it ends with /v1, which the SDK appends itself";
                return true;
            }

            reason = null;
            return false;
        }
    }

    /// <summary>Inspector that surfaces <see cref="BugyardConfigValidation"/> issues above the fields.</summary>
    [CustomEditor(typeof(BugyardConfig))]
    class BugyardConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var config = (BugyardConfig)target;
            foreach (var issue in BugyardConfigValidation.Validate(config))
                EditorGUILayout.HelpBox(issue.Message, issue.Severity);

            DrawDefaultInspector();
        }
    }

    /// <summary>Logs config misconfiguration to the console before a build is produced.</summary>
    class BugyardConfigBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:BugyardConfig"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var config = AssetDatabase.LoadAssetAtPath<BugyardConfig>(path);
                if (config == null)
                    continue;

                foreach (var issue in BugyardConfigValidation.Validate(config))
                    Debug.LogWarning($"[Bugyard] {path}: {issue.Message}", config);
            }
        }
    }
}
#endif
