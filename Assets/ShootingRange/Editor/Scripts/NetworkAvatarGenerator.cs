#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Unity.Netcode;
using ShootingRange.Multiplayer;

namespace ShootingRange.Editor
{
    [InitializeOnLoad]
    public static class NetworkAvatarGenerator
    {
        static NetworkAvatarGenerator()
        {
            EditorApplication.delayCall += EnsureAvatarPrefab;
        }

        [MenuItem("Shooting Range/Generate Player Avatar Prefab")]
        public static void EnsureAvatarPrefab()
        {
            string resDir = "Assets/ShootingRange/Multiplayer/Resources";
            if (!Directory.Exists(resDir))
            {
                Directory.CreateDirectory(resDir);
            }

            string prefabPath = Path.Combine(resDir, "NetworkPlayerAvatar.prefab");

            // Load persistent material assets from project palette
            string matDir = "Assets/ShootingRange/Materials";
            var polymerMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Mat_TacticalPolymer.mat");
            var steelMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Mat_TacticalSteel.mat");
            var tanMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Mat_FDE_Tan.mat");
            var visorMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Mat_ScreenCyan.mat");
            var redDotMat = AssetDatabase.LoadAssetAtPath<Material>($"{matDir}/Mat_RedDotReticle.mat");

            var root = new GameObject("NetworkPlayerAvatar");
            root.AddComponent<NetworkObject>();
            var avatar = root.AddComponent<NetworkPlayerAvatar>();
            avatar.EnsureVisuals(polymerMat, steelMat, tanMat, visorMat, redDotMat);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NetworkAvatarGenerator] Successfully generated NetworkPlayerAvatar prefab with persistent materials at: {prefabPath}");
        }
    }
}
#endif
