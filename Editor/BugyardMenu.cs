#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BugyardSDK.Editor
{
    static class BugyardMenu
    {
        [MenuItem("Tools/Bugyard/Create Config Asset")]
        static void CreateConfig()
        {
            var existing = FindExistingConfig();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[Bugyard] A config asset already exists at {AssetDatabase.GetAssetPath(existing)}. Selected it instead of creating a duplicate.", existing);
                return;
            }

            var cfg = ScriptableObject.CreateInstance<BugyardConfig>();
            const string path = "Assets/BugyardConfig.asset";
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
            Debug.Log("[Bugyard] Created config asset. Set your apiKey and endpoint in the Inspector.", cfg);
        }

        internal static BugyardConfig FindExistingConfig()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:BugyardConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cfg = AssetDatabase.LoadAssetAtPath<BugyardConfig>(path);
                if (cfg != null)
                    return cfg;
            }
            return null;
        }
    }
}
#endif
