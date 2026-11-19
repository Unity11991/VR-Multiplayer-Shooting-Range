#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShootingRange.Editor
{
    public static class BuildPipelineScript
    {
        private static readonly string[] DefaultScenes = new string[]
        {
            "Assets/ShootingRange/Scenes/MainShootingRange.unity"
        };

        [MenuItem("Shooting Range/Build/Build Windows PCVR")]
        public static void BuildWindowsPCVR()
        {
            Debug.Log("[BuildPipeline] Starting Windows PCVR x64 build...");

            string buildDir = Path.GetFullPath("Builds/WindowsPCVR");
            if (!Directory.Exists(buildDir))
            {
                Directory.CreateDirectory(buildDir);
            }

            string exePath = Path.Combine(buildDir, "ShootingRange.exe");

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenePaths(),
                locationPathName = exePath,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            BuildReport report = UnityEditor.BuildPipeline.BuildPlayer(buildPlayerOptions);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildPipeline] Windows PCVR build succeeded: {summary.totalSize} bytes in {summary.totalTime.TotalSeconds:F1} seconds at {exePath}");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            else
            {
                Debug.LogError($"[BuildPipeline] Windows PCVR build failed with {summary.totalErrors} errors!");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        [MenuItem("Shooting Range/Build/Configure Android Quest")]
        public static void ConfigureAndroidQuest()
        {
            Debug.Log("[BuildPipeline] Configuring project for Meta Quest (Android ARM64)...");

            EditorUserBuildSettings.selectedBuildTargetGroup = BuildTargetGroup.Android;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            Debug.Log("[BuildPipeline] Android Quest ARM64 IL2CPP configuration applied successfully.");
        }

        private static string[] GetEnabledScenePaths()
        {
            var editorScenes = EditorBuildSettings.scenes;
            if (editorScenes != null && editorScenes.Length > 0)
            {
                var activeScenes = new System.Collections.Generic.List<string>();
                foreach (var s in editorScenes)
                {
                    if (s.enabled && !string.IsNullOrEmpty(s.path))
                    {
                        activeScenes.Add(s.path);
                    }
                }
                if (activeScenes.Count > 0) return activeScenes.ToArray();
            }

            return DefaultScenes;
        }
    }
}
#endif
