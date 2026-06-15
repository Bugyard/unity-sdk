#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BugCaptureSDK.Editor
{
    static class BugCaptureMenu
    {
        [MenuItem("Tools/BugCapture/Create Config Asset")]
        static void CreateConfig()
        {
            var existing = FindExistingConfig();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[BugCapture] A config asset already exists at {AssetDatabase.GetAssetPath(existing)}. Selected it instead of creating a duplicate.", existing);
                return;
            }

            var cfg = ScriptableObject.CreateInstance<BugCaptureConfig>();
            const string path = "Assets/BugCaptureConfig.asset";
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
            Debug.Log("[BugCapture] Created config asset. Set your apiKey and endpoint in the Inspector.", cfg);
        }

        internal static BugCaptureConfig FindExistingConfig()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BugCaptureConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cfg = AssetDatabase.LoadAssetAtPath<BugCaptureConfig>(path);
                if (cfg != null)
                    return cfg;
            }
            return null;
        }
    }
}
#endif
