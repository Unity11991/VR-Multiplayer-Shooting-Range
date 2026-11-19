#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ShootingRange.Core;
using ShootingRange.Input;
using ShootingRange.Interaction;
using ShootingRange.Weapons;
using ShootingRange.Ballistics;
using ShootingRange.Targets;
using ShootingRange.Multiplayer;
using ShootingRange.Replay;
using ShootingRange.UI;

namespace ShootingRange.Editor
{
    public static class ShootingRangeSceneBuilder
    {
        [MenuItem("Shooting Range/Configure URP and Upgrade Materials")]
        public static void ConfigureURPAndUpgradeMaterials()
        {
            EnsurePipelineConfigured();
            CreateMaterialPalette();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneBuilder] URP configured and all ShootingRange materials upgraded successfully!");
        }

        [MenuItem("Shooting Range/Generate Tactical Range Scene")]
        public static void GenerateTacticalRangeScene()
        {
            Debug.Log("[SceneBuilder] Building production tactical shooting range scene with beautiful weapons and environment...");

            // Ensure Universal Render Pipeline is active and configured
            EnsurePipelineConfigured();

            // Create new scene
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Create materials palette
            var palette = CreateMaterialPalette();

            // 1. Environment Root & Directional Lighting
            var envRoot = new GameObject("Environment");

            var sunObj = new GameObject("Directional Light");
            sunObj.transform.SetParent(envRoot.transform);
            var sun = sunObj.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.96f, 0.95f, 0.92f);
            sun.intensity = 0.95f;
            sunObj.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            // 2. Concrete Range Structure & Ambient Lighting
            CreateStructure(envRoot.transform, palette);

            // 3. Shooting Stalls & Tables with Overhead Spotlights
            CreateShootingLanes(envRoot.transform, palette);

            // 4. Target Rails & Mechanisms (300 Targets Pooled, GPU Instanced)
            var targetManagerObj = new GameObject("TargetManager");
            var targetManager = targetManagerObj.AddComponent<TargetManager>();
            targetManager.SetTargetMaterials(
                palette.TargetCardboard,
                palette.TargetRedZone,
                palette.TargetBlueZone,
                palette.ArmourPlate,
                palette.WeakPointCore,
                palette.Steel
            );
            CreateTargets(targetManagerObj.transform, targetManager, palette);

            // 5. Ballistics Manager
            var ballisticsObj = new GameObject("BallisticsManager");
            var ballistics = ballisticsObj.AddComponent<BallisticsManager>();

            // 6. Network Manager & Multiplayer Session Manager
            var nmObj = new GameObject("NetworkManager");
            var nm = nmObj.AddComponent<Unity.Netcode.NetworkManager>();
            var transport = nmObj.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            nm.NetworkConfig = new Unity.Netcode.NetworkConfig
            {
                NetworkTransport = transport,
                TickRate = 60
            };

            NetworkAvatarGenerator.EnsureAvatarPrefab();
            var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShootingRange/Multiplayer/Resources/NetworkPlayerAvatar.prefab");
            if (avatarPrefab != null)
            {
                nm.NetworkConfig.PlayerPrefab = avatarPrefab;
                nm.NetworkConfig.Prefabs.Add(new Unity.Netcode.NetworkPrefab { Prefab = avatarPrefab });
            }

            var mpObj = new GameObject("NetworkShootingSession");
            mpObj.AddComponent<Unity.Netcode.NetworkObject>();
            var mpSession = mpObj.AddComponent<NetworkShootingSession>();

            // Dedicated LAN Multiplayer Room Panel (Toggle with 'M' key or on-screen button)
            var lobbyUIObj = new GameObject("NetworkLobbyUI");
            lobbyUIObj.AddComponent<NetworkLobbyUI>();

            // 7. Replay Manager
            var replayObj = new GameObject("ReplayManager");
            var replay = replayObj.AddComponent<ReplayManager>();

            // 8. Player Rig & Composition Root
            var playerRig = CreatePlayerRig(palette);

            // 9. Beautiful Weapons (Modular Pistol and Rifle)
            CreateWeapons(envRoot.transform, palette);

            // 10. World-Space Scoreboard & Settings Kiosk
            CreateScoreboardAndUI(envRoot.transform, targetManager, palette);

            // Wire Composition Root
            var compRoot = playerRig.GetComponent<ShootingRangeCompositionRoot>();
            var compRootSo = new SerializedObject(compRoot);
            compRootSo.FindProperty("_ballisticsSource").objectReferenceValue = ballistics;
            compRootSo.FindProperty("_targetSource").objectReferenceValue = targetManager;
            compRootSo.FindProperty("_networkSource").objectReferenceValue = mpSession;
            compRootSo.FindProperty("_replaySource").objectReferenceValue = replay;
            compRootSo.ApplyModifiedProperties();

            // Save Scene
            string sceneDir = "Assets/ShootingRange/Scenes";
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
            string scenePath = Path.Combine(sceneDir, "MainShootingRange.unity");

            EditorSceneManager.SaveScene(scene, scenePath);
            Debug.Log($"[SceneBuilder] Scene successfully saved at: {scenePath}");

            // Register in EditorBuildSettings
            var scenes = new EditorBuildSettingsScene[]
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = scenes;
            AssetDatabase.SaveAssets();
        }

        public class MaterialPalette
        {
            public Material Steel;
            public Material DarkPolymer;
            public Material TanPolymer;
            public Material Brass;
            public Material TritiumGreen;
            public Material RedDot;
            public Material OpticGlass;
            public Material ConcreteFloor;
            public Material WallAcoustic;
            public Material WoodBench;
            public Material RubberMat;
            public Material HazardYellow;
            public Material TargetCardboard;
            public Material TargetRedZone;
            public Material TargetBlueZone;
            public Material ArmourPlate;
            public Material WeakPointCore;
            public Material ScreenCyan;
        }

        private static MaterialPalette CreateMaterialPalette()
        {
            string dir = "Assets/ShootingRange/Materials";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var p = new MaterialPalette
            {
                Steel = GetOrCreateMat(dir, "Mat_TacticalSteel", new Color(0.18f, 0.20f, 0.22f), 0.85f, 0.70f),
                DarkPolymer = GetOrCreateMat(dir, "Mat_TacticalPolymer", new Color(0.10f, 0.10f, 0.11f), 0.05f, 0.25f),
                TanPolymer = GetOrCreateMat(dir, "Mat_FDE_Tan", new Color(0.52f, 0.45f, 0.35f), 0.05f, 0.30f),
                Brass = GetOrCreateMat(dir, "Mat_PolishedBrass", new Color(0.95f, 0.80f, 0.35f), 0.95f, 0.85f),
                TritiumGreen = GetOrCreateMat(dir, "Mat_TritiumGreen", new Color(0.2f, 1f, 0.3f), 0f, 0.5f, new Color(0.3f, 2.8f, 0.5f)),
                RedDot = GetOrCreateMat(dir, "Mat_RedDotReticle", new Color(1f, 0.1f, 0.1f), 0f, 0.5f, new Color(3.5f, 0.2f, 0.2f)),
                OpticGlass = GetOrCreateMat(dir, "Mat_OpticGlass", new Color(0.2f, 0.5f, 0.6f, 0.5f), 0.1f, 0.95f),
                ConcreteFloor = GetOrCreateMat(dir, "Mat_FloorConcrete", new Color(0.25f, 0.26f, 0.28f), 0.10f, 0.40f),
                WallAcoustic = GetOrCreateMat(dir, "Mat_WallAcoustic", new Color(0.15f, 0.15f, 0.16f), 0.05f, 0.15f),
                WoodBench = GetOrCreateMat(dir, "Mat_BenchWood", new Color(0.30f, 0.18f, 0.10f), 0.05f, 0.45f),
                RubberMat = GetOrCreateMat(dir, "Mat_BenchRubber", new Color(0.08f, 0.08f, 0.09f), 0.02f, 0.12f),
                HazardYellow = GetOrCreateMat(dir, "Mat_HazardYellow", new Color(0.95f, 0.80f, 0.05f), 0.10f, 0.30f),
                TargetCardboard = GetOrCreateMat(dir, "Mat_TargetCardboard", new Color(0.82f, 0.74f, 0.58f), 0f, 0.1f),
                TargetRedZone = GetOrCreateMat(dir, "Mat_TargetRedBullseye", new Color(0.88f, 0.15f, 0.12f), 0.05f, 0.3f),
                TargetBlueZone = GetOrCreateMat(dir, "Mat_TargetBlueZone", new Color(0.15f, 0.35f, 0.70f), 0.05f, 0.3f),
                ArmourPlate = GetOrCreateMat(dir, "Mat_ArmourPlate", new Color(0.32f, 0.35f, 0.38f), 0.90f, 0.65f),
                WeakPointCore = GetOrCreateMat(dir, "Mat_WeakPointCore", new Color(0.1f, 0.9f, 1f), 0f, 0.5f, new Color(0.3f, 2.5f, 3.5f)),
                ScreenCyan = GetOrCreateMat(dir, "Mat_ScreenCyan", new Color(0.05f, 0.25f, 0.35f), 0.1f, 0.8f, new Color(0.1f, 1.2f, 1.6f))
            };

            return p;
        }

        private static void EnsurePipelineConfigured()
        {
            if (GraphicsSettings.defaultRenderPipeline != null && QualitySettings.renderPipeline != null)
            {
                return;
            }

            string folderPath = "Assets/ShootingRange/Settings";
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            string urpAssetPath = $"{folderPath}/UniversalRenderPipelineAsset.asset";
            var urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpAssetPath);

            if (urpAsset == null)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/UniversalRendererData.asset");

                if (rendererData == null)
                {
                    string rendererPath = $"{folderPath}/UniversalRendererData.asset";
                    rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
                    if (rendererData == null)
                    {
                        rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                        AssetDatabase.CreateAsset(rendererData, rendererPath);
                    }
                }

                urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urpAsset, urpAssetPath);
            }

            GraphicsSettings.defaultRenderPipeline = urpAsset;
            QualitySettings.renderPipeline = urpAsset;
            EditorUtility.SetDirty(urpAsset);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneBuilder] Universal Render Pipeline configured and assigned.");
        }

        private static Material GetOrCreateMat(string dir, string name, Color albedo, float metallic, float smoothness, Color? emission = null)
        {
            string path = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            var targetShader = Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Sprites/Default");

            if (mat == null)
            {
                mat = new Material(targetShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != targetShader && targetShader != null)
            {
                mat.shader = targetShader;
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", albedo);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", albedo);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            mat.enableInstancing = true;

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void CreateStructure(Transform parent, MaterialPalette pal)
        {
            var structure = new GameObject("Structure");
            structure.transform.SetParent(parent);

            // Concrete Floor (34m long x 16m wide)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "ConcreteFloor";
            floor.transform.SetParent(structure.transform);
            floor.transform.position = new Vector3(0f, -0.1f, 16f);
            floor.transform.localScale = new Vector3(16f, 0.2f, 34f);
            floor.GetComponent<Renderer>().sharedMaterial = pal.ConcreteFloor;
            AddSurface(floor, SurfaceType.Concrete, 200f);

            // Safety Demarcation Line along Firing Line (Yellow strip at z = 0.7m)
            var safetyLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
            safetyLine.name = "SafetyDemarcationLine";
            safetyLine.transform.SetParent(structure.transform);
            safetyLine.transform.position = new Vector3(0f, 0.005f, 0.7f);
            safetyLine.transform.localScale = new Vector3(14f, 0.01f, 0.15f);
            safetyLine.GetComponent<Renderer>().sharedMaterial = pal.HazardYellow;

            // Backstop Wall (Heavy reinforced concrete bullet trap at 32m)
            var backstop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backstop.name = "BulletBackstop_Concrete";
            backstop.transform.SetParent(structure.transform);
            backstop.transform.position = new Vector3(0f, 3.0f, 32.5f);
            backstop.transform.localScale = new Vector3(16f, 6.0f, 1.2f);
            backstop.GetComponent<Renderer>().sharedMaterial = pal.WallAcoustic;
            AddSurface(backstop, SurfaceType.Concrete, 400f);

            // Left & Right Concrete Acoustic Walls
            var leftWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftWall.name = "LeftWall_Acoustic";
            leftWall.transform.SetParent(structure.transform);
            leftWall.transform.position = new Vector3(-8f, 3.0f, 16f);
            leftWall.transform.localScale = new Vector3(0.6f, 6.0f, 34f);
            leftWall.GetComponent<Renderer>().sharedMaterial = pal.WallAcoustic;
            AddSurface(leftWall, SurfaceType.Concrete, 250f);

            var rightWall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightWall.name = "RightWall_Acoustic";
            rightWall.transform.SetParent(structure.transform);
            rightWall.transform.position = new Vector3(8f, 3.0f, 16f);
            rightWall.transform.localScale = new Vector3(0.6f, 6.0f, 34f);
            rightWall.GetComponent<Renderer>().sharedMaterial = pal.WallAcoustic;
            AddSurface(rightWall, SurfaceType.Concrete, 250f);

            // Ceiling / Roof Structure with linear overhead fluorescent style lights
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Ceiling_Acoustic";
            roof.transform.SetParent(structure.transform);
            roof.transform.position = new Vector3(0f, 6.1f, 16f);
            roof.transform.localScale = new Vector3(16f, 0.2f, 34f);
            roof.GetComponent<Renderer>().sharedMaterial = pal.WallAcoustic;
            AddSurface(roof, SurfaceType.Metal, 15f);

            // Downrange Track Light Strip
            for (int z = 6; z <= 28; z += 6)
            {
                var lightObj = new GameObject($"DownrangeLight_{z}m");
                lightObj.transform.SetParent(structure.transform);
                lightObj.transform.position = new Vector3(0f, 5.5f, z);
                var l = lightObj.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 14f;
                l.intensity = 0.8f;
                l.color = new Color(0.95f, 0.95f, 1.0f);
            }
        }

        private static void CreateShootingLanes(Transform parent, MaterialPalette pal)
        {
            var lanesRoot = new GameObject("ShootingLanes");
            lanesRoot.transform.SetParent(parent);

            float[] laneXPositions = new float[] { -3.5f, 0f, 3.5f };
            string[] laneNames = new string[] { "LANE 1 (25M)", "LANE 2 (50M)", "LANE 3 (100M)" };

            for (int i = 0; i < laneXPositions.Length; i++)
            {
                float x = laneXPositions[i];
                var laneObj = new GameObject($"Lane_{i + 1}");
                laneObj.transform.SetParent(lanesRoot.transform);

                // Wooden Shooting Bench Table
                var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                table.name = $"ShootingTable_Lane_{i + 1}";
                table.transform.SetParent(laneObj.transform);
                table.transform.position = new Vector3(x, 0.85f, 1.25f);
                table.transform.localScale = new Vector3(1.9f, 0.08f, 0.85f);
                table.GetComponent<Renderer>().sharedMaterial = pal.WoodBench;
                AddSurface(table, SurfaceType.Wood, 40f);

                // Bench Rubber Gun Mat
                var rubberMat = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rubberMat.name = "RubberGunMat";
                rubberMat.transform.SetParent(table.transform);
                rubberMat.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                rubberMat.transform.localScale = new Vector3(0.85f, 0.15f, 0.80f);
                rubberMat.GetComponent<Renderer>().sharedMaterial = pal.RubberMat;
                Object.DestroyImmediate(rubberMat.GetComponent<Collider>());

                // Steel Table Legs
                CreateBenchLeg(table.transform, new Vector3(-0.45f, -5f, -0.4f), pal.Steel);
                CreateBenchLeg(table.transform, new Vector3(0.45f, -5f, -0.4f), pal.Steel);
                CreateBenchLeg(table.transform, new Vector3(-0.45f, -5f, 0.4f), pal.Steel);
                CreateBenchLeg(table.transform, new Vector3(0.45f, -5f, 0.4f), pal.Steel);

                // Warm Overhead Spotlight directly illuminating the shooting bench
                var spotObj = new GameObject($"LaneSpotlight_{i + 1}");
                spotObj.transform.SetParent(laneObj.transform);
                spotObj.transform.position = new Vector3(x, 4.2f, 1.1f);
                spotObj.transform.rotation = Quaternion.Euler(75f, 0f, 0f);
                var spot = spotObj.AddComponent<Light>();
                spot.type = LightType.Spot;
                spot.spotAngle = 65f;
                spot.range = 8f;
                spot.intensity = 2.4f;
                spot.color = new Color(1.0f, 0.95f, 0.88f);

                // Stall Partitions (Polycarbonate / Framed Ballistic Glass)
                if (i < laneXPositions.Length - 1)
                {
                    var dividerFrame = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    dividerFrame.name = $"BallisticDividerFrame_{i + 1}";
                    dividerFrame.transform.SetParent(laneObj.transform);
                    dividerFrame.transform.position = new Vector3(x + 1.75f, 1.6f, 1.4f);
                    dividerFrame.transform.localScale = new Vector3(0.08f, 2.4f, 2.6f);
                    dividerFrame.GetComponent<Renderer>().sharedMaterial = pal.Steel;
                    AddSurface(dividerFrame, SurfaceType.Metal, 30f);

                    var dividerGlass = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    dividerGlass.name = "GlassPane";
                    dividerGlass.transform.SetParent(dividerFrame.transform);
                    dividerGlass.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                    dividerGlass.transform.localScale = new Vector3(0.4f, 0.85f, 0.85f);
                    dividerGlass.GetComponent<Renderer>().sharedMaterial = pal.OpticGlass;
                    AddSurface(dividerGlass, SurfaceType.Glass, 25f);
                }
            }
        }

        private static void CreateBenchLeg(Transform tableParent, Vector3 localPos, Material mat)
        {
            var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            leg.name = "Leg";
            leg.transform.SetParent(tableParent);
            leg.transform.localPosition = localPos;
            leg.transform.localScale = new Vector3(0.04f, 5f, 0.04f);
            leg.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(leg.GetComponent<Collider>());
        }

        private static void CreateTargets(Transform parent, TargetManager manager, MaterialPalette pal)
        {
            // Lane 1: 2 Pop-Up Targets
            CreatePopUpTarget(parent, manager, 1, new Vector3(-3.8f, 1.4f, 11f), pal);
            CreatePopUpTarget(parent, manager, 2, new Vector3(-3.8f, 1.4f, 19f), pal);

            // Lane 2: 2 Strafing Targets
            CreateStrafingTarget(parent, manager, 3, new Vector3(0f, 1.5f, 13.5f), pal);
            CreateStrafingTarget(parent, manager, 4, new Vector3(0f, 1.5f, 21.5f), pal);

            // Lane 3: 2 Zig-Zag Targets
            CreateZigZagTarget(parent, manager, 5, new Vector3(3.6f, 1.4f, 15.5f), pal);
            CreateZigZagTarget(parent, manager, 6, new Vector3(-1.8f, 1.4f, 24f), pal);

            // Lane 4: 2 Heavy Armoured Targets with Glowing Weak Points
            CreateArmouredTarget(parent, manager, 7, new Vector3(3.6f, 1.6f, 26.5f), pal);
            CreateArmouredTarget(parent, manager, 8, new Vector3(0.8f, 1.6f, 29f), pal);

            // Pre-populate remaining 292 pooled targets across downrange field
            manager.Prepopulate300TargetPool(292);

            // Keep only 8 targets active by default, pooling the rest
            manager.SetActiveTargetCount(8);
        }

        private static void CreatePopUpTarget(Transform parent, TargetManager manager, int id, Vector3 pos, MaterialPalette pal)
        {
            var root = new GameObject($"PopUpTarget_{id}");
            root.transform.SetParent(parent);
            root.transform.position = pos;

            var pop = root.AddComponent<PopUpTarget>();

            // Target Body (Paper IPSC Torso) - Has BoxCollider
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform);
            torso.transform.localPosition = Vector3.zero;
            torso.transform.localScale = new Vector3(0.48f, 0.85f, 0.04f);
            torso.GetComponent<Renderer>().sharedMaterial = pal.TargetCardboard;

            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);
            AddSurface(torso, SurfaceType.Wood, 20f);

            // Chest Bullseye ring (10-ring vital) - Head / Critical HitZone
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "VitalsRing";
            ring.transform.SetParent(torso.transform);
            ring.transform.localPosition = new Vector3(0f, 0.12f, -0.55f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(0.45f, 0.05f, 0.45f);
            ring.GetComponent<Renderer>().sharedMaterial = pal.TargetRedZone;
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            var ringCol = ring.AddComponent<SphereCollider>();
            ringCol.radius = 0.28f;
            var vitalZone = ring.AddComponent<TargetHitZone>();
            vitalZone.Configure(HitZone.Head, 2.5f, 250);

            // Target Stand Base
            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "StandPost";
            stand.transform.SetParent(root.transform);
            stand.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            stand.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            stand.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(stand.GetComponent<Collider>());

            pop.Initialize(id, pos, Quaternion.identity);
            var popSo = new SerializedObject(pop);
            popSo.FindProperty("_targetId").intValue = id;
            popSo.FindProperty("_spawnPosition").vector3Value = pos;
            popSo.FindProperty("_spawnRotation").quaternionValue = Quaternion.identity;
            popSo.FindProperty("_visualMeshTransform").objectReferenceValue = torso.transform;
            popSo.ApplyModifiedProperties();
            manager.RegisterTarget(pop);
        }

        private static void CreateStrafingTarget(Transform parent, TargetManager manager, int id, Vector3 pos, MaterialPalette pal)
        {
            var root = new GameObject($"StrafingTarget_{id}");
            root.transform.SetParent(parent);
            root.transform.position = pos;

            var strafe = root.AddComponent<StrafingTarget>();

            // Silhouette Torso - Has CapsuleCollider
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Silhouette";
            torso.transform.SetParent(root.transform);
            torso.transform.localPosition = Vector3.zero;
            torso.transform.localScale = new Vector3(0.46f, 0.65f, 0.46f);
            torso.GetComponent<Renderer>().sharedMaterial = pal.TargetBlueZone;

            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);
            AddSurface(torso, SurfaceType.Metal, 10f);

            // Center Ring - Head / Critical HitZone
            var ring = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ring.name = "CenterCore";
            ring.transform.SetParent(torso.transform);
            ring.transform.localPosition = new Vector3(0f, 0.15f, -0.45f);
            ring.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
            ring.GetComponent<Renderer>().sharedMaterial = pal.TargetRedZone;
            var coreZone = ring.AddComponent<TargetHitZone>();
            coreZone.Configure(HitZone.Head, 2.5f, 250);

            strafe.Initialize(id, pos, Quaternion.identity);
            var strafeSo = new SerializedObject(strafe);
            strafeSo.FindProperty("_targetId").intValue = id;
            strafeSo.FindProperty("_spawnPosition").vector3Value = pos;
            strafeSo.FindProperty("_spawnRotation").quaternionValue = Quaternion.identity;
            strafeSo.FindProperty("_visualMeshTransform").objectReferenceValue = torso.transform;
            strafeSo.ApplyModifiedProperties();
            manager.RegisterTarget(strafe);
        }

        private static void CreateZigZagTarget(Transform parent, TargetManager manager, int id, Vector3 pos, MaterialPalette pal)
        {
            var root = new GameObject($"ZigZagTarget_{id}");
            root.transform.SetParent(parent);
            root.transform.position = pos;

            var zigzag = root.AddComponent<ZigZagTarget>();

            // Torso - Has BoxCollider
            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform);
            torso.transform.localPosition = Vector3.zero;
            torso.transform.localScale = new Vector3(0.50f, 0.95f, 0.05f);
            torso.GetComponent<Renderer>().sharedMaterial = pal.TargetCardboard;

            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);
            AddSurface(torso, SurfaceType.Wood, 25f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(torso.transform);
            head.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            head.transform.localScale = new Vector3(0.5f, 0.35f, 0.95f);
            head.GetComponent<Renderer>().sharedMaterial = pal.TargetRedZone;
            var headZone = head.AddComponent<TargetHitZone>();
            headZone.Configure(HitZone.Head, 2.5f, 250);

            zigzag.Initialize(id, pos, Quaternion.identity);
            var zigSo = new SerializedObject(zigzag);
            zigSo.FindProperty("_targetId").intValue = id;
            zigSo.FindProperty("_spawnPosition").vector3Value = pos;
            zigSo.FindProperty("_spawnRotation").quaternionValue = Quaternion.identity;
            zigSo.FindProperty("_visualMeshTransform").objectReferenceValue = torso.transform;
            zigSo.ApplyModifiedProperties();
            manager.RegisterTarget(zigzag);
        }

        private static void CreateArmouredTarget(Transform parent, TargetManager manager, int id, Vector3 pos, MaterialPalette pal)
        {
            var root = new GameObject($"ArmouredTarget_{id}");
            root.transform.SetParent(parent);
            root.transform.position = pos;

            var armour = root.AddComponent<ArmouredTarget>();

            // Heavy Ballistic Armor Shield - Has BoxCollider
            var shield = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shield.name = "BallisticShieldPlate";
            shield.transform.SetParent(root.transform);
            shield.transform.localPosition = Vector3.zero;
            shield.transform.localScale = new Vector3(0.70f, 1.45f, 0.12f);
            shield.GetComponent<Renderer>().sharedMaterial = pal.ArmourPlate;

            var shieldZone = shield.AddComponent<TargetHitZone>();
            shieldZone.Configure(HitZone.Body, 0.10f, 50);
            AddSurface(shield, SurfaceType.Metal, 80f);

            // Glowing Pulsing Holographic Weak Point in Center - Has SphereCollider
            var weakPoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            weakPoint.name = "ArmourWeakPoint";
            weakPoint.transform.SetParent(shield.transform);
            weakPoint.transform.localPosition = new Vector3(0f, 0.15f, -0.65f);
            weakPoint.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);
            weakPoint.GetComponent<Renderer>().sharedMaterial = pal.WeakPointCore;

            var hitZone = weakPoint.AddComponent<TargetHitZone>();
            var hitZoneSo = new SerializedObject(hitZone);
            hitZoneSo.FindProperty("_zone").enumValueIndex = (int)HitZone.WeakPoint;
            hitZoneSo.FindProperty("_damageMultiplier").floatValue = 3.5f;
            hitZoneSo.ApplyModifiedProperties();
            AddSurface(weakPoint, SurfaceType.Metal, 10f);

            armour.Initialize(id, pos, Quaternion.identity);
            var armSo = new SerializedObject(armour);
            armSo.FindProperty("_targetId").intValue = id;
            armSo.FindProperty("_spawnPosition").vector3Value = pos;
            armSo.FindProperty("_spawnRotation").quaternionValue = Quaternion.identity;
            armSo.FindProperty("_visualMeshTransform").objectReferenceValue = shield.transform;
            armSo.ApplyModifiedProperties();
            manager.RegisterTarget(armour);
        }

        private static GameObject CreatePlayerRig(MaterialPalette pal)
        {
            var rig = new GameObject("PlayerRig");
            rig.transform.position = Vector3.zero;

            var compRoot = rig.AddComponent<ShootingRangeCompositionRoot>();
            var inputMgr = rig.AddComponent<InputModeManager>();

            // Head / Camera
            var headObj = new GameObject("Main Camera");
            headObj.tag = "MainCamera";
            headObj.transform.SetParent(rig.transform);
            headObj.transform.position = new Vector3(0f, 1.75f, 0f);
            var cam = headObj.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            headObj.AddComponent<AudioListener>();

            // Vignette UI overlay
            var vignetteCanvas = new GameObject("VignetteCanvas");
            vignetteCanvas.transform.SetParent(headObj.transform);
            var canvas = vignetteCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            vignetteCanvas.AddComponent<ComfortVignette>();

            // Left Hand
            var leftHandObj = new GameObject("LeftHand");
            leftHandObj.transform.SetParent(rig.transform);
            leftHandObj.transform.position = new Vector3(-0.20f, 1.25f, 0.45f);
            var leftInteractor = leftHandObj.AddComponent<VRHandInteractor>();
            var leftSo = new SerializedObject(leftInteractor);
            leftSo.FindProperty("_hand").enumValueIndex = (int)Handedness.Left;
            leftSo.ApplyModifiedProperties();
            leftHandObj.AddComponent<VRRayPointer>();

            var leftRb = leftHandObj.AddComponent<Rigidbody>();
            leftRb.isKinematic = true;
            leftRb.useGravity = false;

            var leftPokeCol = leftHandObj.AddComponent<SphereCollider>();
            leftPokeCol.isTrigger = true;
            leftPokeCol.radius = 0.08f;

            var leftVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leftVisual.name = "LeftHandVisual";
            leftVisual.transform.SetParent(leftHandObj.transform);
            leftVisual.transform.localPosition = Vector3.zero;
            leftVisual.transform.localScale = new Vector3(0.07f, 0.07f, 0.10f);
            leftVisual.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(leftVisual.GetComponent<Collider>());

            // Right Hand
            var rightHandObj = new GameObject("RightHand");
            rightHandObj.transform.SetParent(rig.transform);
            rightHandObj.transform.position = new Vector3(0.20f, 1.25f, 0.45f);
            var rightInteractor = rightHandObj.AddComponent<VRHandInteractor>();
            var rightSo = new SerializedObject(rightInteractor);
            rightSo.FindProperty("_hand").enumValueIndex = (int)Handedness.Right;
            rightSo.ApplyModifiedProperties();
            rightHandObj.AddComponent<VRRayPointer>();

            var rightRb = rightHandObj.AddComponent<Rigidbody>();
            rightRb.isKinematic = true;
            rightRb.useGravity = false;

            var rightPokeCol = rightHandObj.AddComponent<SphereCollider>();
            rightPokeCol.isTrigger = true;
            rightPokeCol.radius = 0.08f;

            var rightVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rightVisual.name = "RightHandVisual";
            rightVisual.transform.SetParent(rightHandObj.transform);
            rightVisual.transform.localPosition = Vector3.zero;
            rightVisual.transform.localScale = new Vector3(0.07f, 0.07f, 0.10f);
            rightVisual.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(rightVisual.GetComponent<Collider>());

            // Wire rig to Composition Root
            var compSo = new SerializedObject(compRoot);
            compSo.FindProperty("_playerRigTransform").objectReferenceValue = rig.transform;
            compSo.FindProperty("_headTransform").objectReferenceValue = headObj.transform;
            compSo.FindProperty("_leftHandTransform").objectReferenceValue = leftHandObj.transform;
            compSo.FindProperty("_rightHandTransform").objectReferenceValue = rightHandObj.transform;
            compSo.FindProperty("_trackingAndInputSource").objectReferenceValue = inputMgr;
            compSo.ApplyModifiedProperties();

            // Turning controller
            rig.AddComponent<SnapSmoothTurnController>();

            // Initialize input mode manager
            inputMgr.EnsureInitialized();

            return rig;
        }

        private static void CreateWeapons(Transform parent, MaterialPalette pal)
        {
            var weaponsRoot = new GameObject("Weapons");
            weaponsRoot.transform.SetParent(parent, false);

            // ==========================================
            // 1. BEAUTIFUL TACTICAL SEMI-AUTO PISTOL 9MM
            // ==========================================
            var pistolRoot = new GameObject("SemiAutoPistol_9mm");
            pistolRoot.transform.SetParent(weaponsRoot.transform, false);
            pistolRoot.transform.localPosition = Vector3.zero;
            pistolRoot.transform.localRotation = Quaternion.identity;

            var pistolCol = pistolRoot.AddComponent<BoxCollider>();
            pistolCol.size = new Vector3(0.06f, 0.16f, 0.22f);
            pistolCol.center = new Vector3(0f, 0.01f, 0f);

            var pistolRb = pistolRoot.AddComponent<Rigidbody>();
            pistolRb.mass = 1.1f;
            pistolRb.isKinematic = true;
            var pistolGrabbable = pistolRoot.AddComponent<PhysicsGrabbable>();
            var pistolController = pistolRoot.AddComponent<PistolController>();

            // Grip / Hand Hold Point
            var pGripPointObj = new GameObject("PrimaryGripPoint");
            pGripPointObj.transform.SetParent(pistolRoot.transform, false);
            pGripPointObj.transform.localPosition = new Vector3(0f, -0.04f, -0.03f);
            pGripPointObj.transform.localRotation = Quaternion.identity;
            var pistolGrabPoint = pGripPointObj.AddComponent<GrabPoint>();

            var pGrabbableSo = new SerializedObject(pistolGrabbable);
            pGrabbableSo.FindProperty("_primaryGrabPoint").objectReferenceValue = pistolGrabPoint;
            pGrabbableSo.ApplyModifiedProperties();

            // Visual Pistol Slide Assembly (Steel)
            var pSlide = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pSlide.name = "PistolSlide";
            pSlide.transform.SetParent(pistolRoot.transform, false);
            pSlide.transform.localPosition = new Vector3(0f, 0.05f, 0.02f);
            pSlide.transform.localRotation = Quaternion.identity;
            pSlide.transform.localScale = new Vector3(0.034f, 0.036f, 0.19f);
            pSlide.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(pSlide.GetComponent<Collider>());

            // Visual Pistol Barrel extending slightly forward
            var pBarrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pBarrel.name = "PistolBarrel";
            pBarrel.transform.SetParent(pSlide.transform, false);
            pBarrel.transform.localPosition = new Vector3(0f, -0.05f, 0.52f);
            pBarrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pBarrel.transform.localScale = new Vector3(0.35f, 0.15f, 0.35f);
            pBarrel.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(pBarrel.GetComponent<Collider>());

            // Night Combat Sights: Green Tritium front post & white rear sights
            var pFrontSight = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pFrontSight.name = "FrontSight_Tritium";
            pFrontSight.transform.SetParent(pSlide.transform, false);
            pFrontSight.transform.localPosition = new Vector3(0f, 0.58f, 0.44f);
            pFrontSight.transform.localRotation = Quaternion.identity;
            pFrontSight.transform.localScale = new Vector3(0.18f, 0.22f, 0.08f);
            pFrontSight.GetComponent<Renderer>().sharedMaterial = pal.TritiumGreen;
            Object.DestroyImmediate(pFrontSight.GetComponent<Collider>());

            var pRearSight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pRearSight.name = "RearSight";
            pRearSight.transform.SetParent(pSlide.transform, false);
            pRearSight.transform.localPosition = new Vector3(0f, 0.56f, -0.45f);
            pRearSight.transform.localRotation = Quaternion.identity;
            pRearSight.transform.localScale = new Vector3(0.6f, 0.18f, 0.06f);
            pRearSight.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(pRearSight.GetComponent<Collider>());

            // Visual Pistol Polymer Frame & Grip
            var pGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pGrip.name = "PolymerGrip";
            pGrip.transform.SetParent(pistolRoot.transform, false);
            pGrip.transform.localPosition = new Vector3(0f, -0.04f, -0.03f);
            pGrip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            pGrip.transform.localScale = new Vector3(0.032f, 0.12f, 0.055f);
            pGrip.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(pGrip.GetComponent<Collider>());

            // Trigger Guard
            var pTriggerGuard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pTriggerGuard.name = "TriggerGuard";
            pTriggerGuard.transform.SetParent(pistolRoot.transform, false);
            pTriggerGuard.transform.localPosition = new Vector3(0f, -0.015f, 0.03f);
            pTriggerGuard.transform.localRotation = Quaternion.identity;
            pTriggerGuard.transform.localScale = new Vector3(0.02f, 0.04f, 0.06f);
            pTriggerGuard.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(pTriggerGuard.GetComponent<Collider>());

            // Tactical Rail Light Module
            var pTacLight = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pTacLight.name = "TacticalLightModule";
            pTacLight.transform.SetParent(pistolRoot.transform, false);
            pTacLight.transform.localPosition = new Vector3(0f, 0.015f, 0.07f);
            pTacLight.transform.localRotation = Quaternion.identity;
            pTacLight.transform.localScale = new Vector3(0.028f, 0.025f, 0.06f);
            pTacLight.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(pTacLight.GetComponent<Collider>());

            // Pistol Muzzle Forward Tip
            var pistolMuzzle = new GameObject("Muzzle");
            pistolMuzzle.transform.SetParent(pistolRoot.transform, false);
            pistolMuzzle.transform.localPosition = new Vector3(0f, 0.05f, 0.13f);
            pistolMuzzle.transform.localRotation = Quaternion.identity;

            // Assign Weapon Definition and Muzzle
            string pistolDefPath = "Assets/ShootingRange/Weapons/Data/Weapon_Pistol_9mm.asset";
            var pistolDef = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(pistolDefPath);
            var pistolSo = new SerializedObject(pistolController);
            if (pistolDef != null)
            {
                pistolSo.FindProperty("_definition").objectReferenceValue = pistolDef;
            }
            pistolSo.FindProperty("_muzzleTransform").objectReferenceValue = pistolMuzzle.transform;
            pistolSo.ApplyModifiedProperties();

            // Pistol Magazine Well
            var pMagWellObj = new GameObject("MagazineWell");
            pMagWellObj.transform.SetParent(pistolRoot.transform, false);
            pMagWellObj.transform.localPosition = new Vector3(0f, -0.04f, -0.03f);
            pMagWellObj.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
            var pMagWellCol = pMagWellObj.AddComponent<BoxCollider>();
            pMagWellCol.isTrigger = true;
            pMagWellCol.size = new Vector3(0.035f, 0.10f, 0.055f);
            var pistolMagWell = pMagWellObj.AddComponent<MagazineWell>();

            // Pre-inserted loaded 15-round magazine seated inside magwell
            var loadedPistolMagObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            loadedPistolMagObj.name = "LoadedPistolMag";
            loadedPistolMagObj.transform.SetParent(pMagWellObj.transform, false);
            loadedPistolMagObj.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            loadedPistolMagObj.transform.localRotation = Quaternion.identity;
            loadedPistolMagObj.transform.localScale = new Vector3(0.028f, 0.11f, 0.045f);
            loadedPistolMagObj.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;

            var loadedPistolMagCol = loadedPistolMagObj.GetComponent<BoxCollider>();
            if (loadedPistolMagCol != null) loadedPistolMagCol.enabled = false;

            var loadedPistolMagRb = loadedPistolMagObj.AddComponent<Rigidbody>();
            loadedPistolMagRb.mass = 0.25f;
            loadedPistolMagRb.isKinematic = true;
            loadedPistolMagRb.detectCollisions = false;
            loadedPistolMagRb.interpolation = RigidbodyInterpolation.None;

            var loadedPistolMag = loadedPistolMagObj.AddComponent<Magazine>();
            loadedPistolMag.Initialize(15, WeaponType.Pistol);
            loadedPistolMag.SetSeated(true, pMagWellObj.transform, new Vector3(0f, -0.02f, 0f));
            loadedPistolMagObj.AddComponent<GrabPoint>();

            pistolSo.FindProperty("_magazineWellTransform").objectReferenceValue = pMagWellObj.transform;
            pistolSo.FindProperty("_insertedMagazine").objectReferenceValue = loadedPistolMag;
            pistolSo.FindProperty("_magazineMaterial").objectReferenceValue = pal.DarkPolymer;
            pistolSo.FindProperty("_brassMaterial").objectReferenceValue = pal.Brass;
            pistolSo.ApplyModifiedProperties();

            // Set final resting position & rotation on bench
            pistolRoot.transform.position = new Vector3(-3.5f, 0.94f, 1.25f);
            pistolRoot.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            // Infinite Magazine Dispenser on Lane 1 Bench Mat (Bullets Never End!)
            var pistolDispenserObj = new GameObject("PistolMagazineDispenser");
            pistolDispenserObj.transform.SetParent(weaponsRoot.transform, false);
            pistolDispenserObj.transform.position = new Vector3(-3.72f, 0.94f, 1.25f);
            var pistolDispenser = pistolDispenserObj.AddComponent<MagazineDispenser>();
            pistolDispenser.Initialize(WeaponType.Pistol, 15, pal.DarkPolymer, pal.Brass);


            // ==========================================
            // 2. BEAUTIFUL TACTICAL ASSAULT RIFLE 5.56MM
            // ==========================================
            var rifleRoot = new GameObject("AssaultRifle_556mm");
            rifleRoot.transform.SetParent(weaponsRoot.transform, false);
            rifleRoot.transform.localPosition = Vector3.zero;
            rifleRoot.transform.localRotation = Quaternion.identity;

            var rifleCol = rifleRoot.AddComponent<BoxCollider>();
            rifleCol.size = new Vector3(0.08f, 0.24f, 0.85f);
            rifleCol.center = new Vector3(0f, 0.02f, 0.10f);

            var rifleRb = rifleRoot.AddComponent<Rigidbody>();
            rifleRb.mass = 3.4f;
            rifleRb.isKinematic = true;
            var rifleController = rifleRoot.AddComponent<RifleController>();

            // Upper & Lower Receiver
            var rReceiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rReceiver.name = "RifleReceiver";
            rReceiver.transform.SetParent(rifleRoot.transform, false);
            rReceiver.transform.localPosition = new Vector3(0f, 0.04f, 0f);
            rReceiver.transform.localRotation = Quaternion.identity;
            rReceiver.transform.localScale = new Vector3(0.045f, 0.09f, 0.24f);
            rReceiver.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rReceiver.GetComponent<Collider>());

            // Top Picatinny Optic Rail
            var rTopRail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rTopRail.name = "TopPicatinnyRail";
            rTopRail.transform.SetParent(rifleRoot.transform, false);
            rTopRail.transform.localPosition = new Vector3(0f, 0.095f, 0.08f);
            rTopRail.transform.localRotation = Quaternion.identity;
            rTopRail.transform.localScale = new Vector3(0.025f, 0.02f, 0.40f);
            rTopRail.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rTopRail.GetComponent<Collider>());

            // Red Dot / Holographic Sight Housing
            var rOpticHousing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rOpticHousing.name = "RedDotSightHousing";
            rOpticHousing.transform.SetParent(rifleRoot.transform, false);
            rOpticHousing.transform.localPosition = new Vector3(0f, 0.135f, 0.02f);
            rOpticHousing.transform.localRotation = Quaternion.identity;
            rOpticHousing.transform.localScale = new Vector3(0.036f, 0.048f, 0.075f);
            rOpticHousing.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rOpticHousing.GetComponent<Collider>());

            // Optic Glass Pane
            var rOpticGlass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rOpticGlass.name = "OpticLens";
            rOpticGlass.transform.SetParent(rOpticHousing.transform, false);
            rOpticGlass.transform.localPosition = Vector3.zero;
            rOpticGlass.transform.localRotation = Quaternion.identity;
            rOpticGlass.transform.localScale = new Vector3(0.85f, 0.85f, 0.15f);
            rOpticGlass.GetComponent<Renderer>().sharedMaterial = pal.OpticGlass;
            Object.DestroyImmediate(rOpticGlass.GetComponent<Collider>());

            // Illuminated Glowing Floating RED DOT Reticle in Sight Center
            var rRedDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rRedDot.name = "RedDotReticle";
            rRedDot.transform.SetParent(rOpticHousing.transform, false);
            rRedDot.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            rRedDot.transform.localRotation = Quaternion.identity;
            rRedDot.transform.localScale = new Vector3(0.12f, 0.12f, 0.05f);
            rRedDot.GetComponent<Renderer>().sharedMaterial = pal.RedDot;
            Object.DestroyImmediate(rRedDot.GetComponent<Collider>());

            // M-LOK Handguard / Rail Shroud (FDE Tan / Desert Camo contrast)
            var rHandguard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rHandguard.name = "MlokHandguard";
            rHandguard.transform.SetParent(rifleRoot.transform, false);
            rHandguard.transform.localPosition = new Vector3(0f, 0.04f, 0.24f);
            rHandguard.transform.localRotation = Quaternion.identity;
            rHandguard.transform.localScale = new Vector3(0.042f, 0.065f, 0.28f);
            rHandguard.GetComponent<Renderer>().sharedMaterial = pal.TanPolymer;
            Object.DestroyImmediate(rHandguard.GetComponent<Collider>());

            // Steel Barrel protruding forward
            var rBarrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rBarrel.name = "RifleBarrel";
            rBarrel.transform.SetParent(rifleRoot.transform, false);
            rBarrel.transform.localPosition = new Vector3(0f, 0.04f, 0.44f);
            rBarrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rBarrel.transform.localScale = new Vector3(0.018f, 0.16f, 0.018f);
            rBarrel.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rBarrel.GetComponent<Collider>());

            // Birdcage Muzzle Brake / Flash Hider
            var rMuzzleBrake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rMuzzleBrake.name = "MuzzleBrake";
            rMuzzleBrake.transform.SetParent(rifleRoot.transform, false);
            rMuzzleBrake.transform.localPosition = new Vector3(0f, 0.04f, 0.58f);
            rMuzzleBrake.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rMuzzleBrake.transform.localScale = new Vector3(0.024f, 0.035f, 0.024f);
            rMuzzleBrake.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rMuzzleBrake.GetComponent<Collider>());

            // Ergonomic Pistol Grip
            var rGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rGrip.name = "RiflePistolGrip";
            rGrip.transform.SetParent(rifleRoot.transform, false);
            rGrip.transform.localPosition = new Vector3(0f, -0.06f, -0.06f);
            rGrip.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
            rGrip.transform.localScale = new Vector3(0.032f, 0.13f, 0.052f);
            rGrip.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(rGrip.GetComponent<Collider>());

            // Buffer Tube & Collapsible Crane Stock (lowered profile so optics have 100% unobstructed view)
            var rBufferTube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rBufferTube.name = "BufferTube";
            rBufferTube.transform.SetParent(rifleRoot.transform, false);
            rBufferTube.transform.localPosition = new Vector3(0f, 0.035f, -0.20f);
            rBufferTube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rBufferTube.transform.localScale = new Vector3(0.024f, 0.10f, 0.024f);
            rBufferTube.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(rBufferTube.GetComponent<Collider>());

            var rCraneStock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rCraneStock.name = "CraneStock";
            rCraneStock.transform.SetParent(rifleRoot.transform, false);
            rCraneStock.transform.localPosition = new Vector3(0f, -0.01f, -0.28f);
            rCraneStock.transform.localRotation = Quaternion.identity;
            rCraneStock.transform.localScale = new Vector3(0.036f, 0.07f, 0.16f);
            rCraneStock.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(rCraneStock.GetComponent<Collider>());

            // Tactical Angled Foregrip (Secondary Hand Grip)
            var rForegrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rForegrip.name = "AngledForegrip";
            rForegrip.transform.SetParent(rifleRoot.transform, false);
            rForegrip.transform.localPosition = new Vector3(0f, -0.025f, 0.25f);
            rForegrip.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            rForegrip.transform.localScale = new Vector3(0.030f, 0.08f, 0.05f);
            rForegrip.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(rForegrip.GetComponent<Collider>());

            // Rifle Muzzle Transform
            var rifleMuzzle = new GameObject("Muzzle");
            rifleMuzzle.transform.SetParent(rifleRoot.transform, false);
            rifleMuzzle.transform.localPosition = new Vector3(0f, 0.04f, 0.62f);
            rifleMuzzle.transform.localRotation = Quaternion.identity;

            string rifleDefPath = "Assets/ShootingRange/Weapons/Data/Weapon_AssaultRifle_556.asset";
            var rifleDef = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(rifleDefPath);
            var rifleSo = new SerializedObject(rifleController);
            if (rifleDef != null)
            {
                rifleSo.FindProperty("_definition").objectReferenceValue = rifleDef;
            }
            rifleSo.FindProperty("_muzzleTransform").objectReferenceValue = rifleMuzzle.transform;
            rifleSo.FindProperty("_opticalSightTransform").objectReferenceValue = rRedDot.transform;
            rifleSo.ApplyModifiedProperties();

            // Primary Grip Point (Rear Grip)
            var primaryGripObj = new GameObject("PrimaryGripPoint");
            primaryGripObj.transform.SetParent(rifleRoot.transform, false);
            primaryGripObj.transform.localPosition = new Vector3(0f, -0.05f, -0.06f);
            primaryGripObj.transform.localRotation = Quaternion.identity;
            var primGrab = primaryGripObj.AddComponent<GrabPoint>();

            // Secondary Grip Point (Foregrip)
            var secondaryGripObj = new GameObject("SecondaryForegripPoint");
            secondaryGripObj.transform.SetParent(rifleRoot.transform, false);
            secondaryGripObj.transform.localPosition = new Vector3(0f, -0.025f, 0.25f);
            secondaryGripObj.transform.localRotation = Quaternion.identity;
            var secGrab = secondaryGripObj.AddComponent<GrabPoint>();
            var secGrabSo = new SerializedObject(secGrab);
            secGrabSo.FindProperty("_isSecondaryGrip").boolValue = true;
            secGrabSo.ApplyModifiedProperties();

            var rifleGrip = rifleRoot.GetComponent<TwoHandedRifleGrip>();
            var rifleGripSo = new SerializedObject(rifleGrip);
            rifleGripSo.FindProperty("_primaryGrabPoint").objectReferenceValue = primGrab;
            rifleGripSo.FindProperty("_secondaryGrabPoint").objectReferenceValue = secGrab;
            rifleGripSo.ApplyModifiedProperties();

            // Rifle Magazine Well
            var rMagWellObj = new GameObject("MagazineWell");
            rMagWellObj.transform.SetParent(rifleRoot.transform, false);
            rMagWellObj.transform.localPosition = new Vector3(0f, -0.01f, 0.04f);
            rMagWellObj.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            var rMagWellCol = rMagWellObj.AddComponent<BoxCollider>();
            rMagWellCol.isTrigger = true;
            rMagWellCol.size = new Vector3(0.04f, 0.10f, 0.09f);
            var rifleMagWell = rMagWellObj.AddComponent<MagazineWell>();

            // Pre-inserted loaded 30-round STANAG magazine seated inside magwell
            var loadedRifleMagObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            loadedRifleMagObj.name = "LoadedRifleMag";
            loadedRifleMagObj.transform.SetParent(rMagWellObj.transform, false);
            loadedRifleMagObj.transform.localPosition = new Vector3(0f, -0.06f, 0f);
            loadedRifleMagObj.transform.localRotation = Quaternion.identity;
            loadedRifleMagObj.transform.localScale = new Vector3(0.034f, 0.18f, 0.08f);
            loadedRifleMagObj.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;

            var loadedRifleMagCol = loadedRifleMagObj.GetComponent<BoxCollider>();
            if (loadedRifleMagCol != null) loadedRifleMagCol.enabled = false;

            var loadedRifleMagRb = loadedRifleMagObj.AddComponent<Rigidbody>();
            loadedRifleMagRb.mass = 0.45f;
            loadedRifleMagRb.isKinematic = true;
            loadedRifleMagRb.detectCollisions = false;
            loadedRifleMagRb.interpolation = RigidbodyInterpolation.None;

            var loadedRifleMag = loadedRifleMagObj.AddComponent<Magazine>();
            loadedRifleMag.Initialize(30, WeaponType.Rifle);
            loadedRifleMag.SetSeated(true, rMagWellObj.transform, new Vector3(0f, -0.06f, 0f));
            loadedRifleMagObj.AddComponent<GrabPoint>();

            rifleSo.FindProperty("_magazineWellTransform").objectReferenceValue = rMagWellObj.transform;
            rifleSo.FindProperty("_insertedMagazine").objectReferenceValue = loadedRifleMag;
            rifleSo.FindProperty("_opticalSightTransform").objectReferenceValue = rRedDot.transform;
            rifleSo.FindProperty("_magazineMaterial").objectReferenceValue = pal.DarkPolymer;
            rifleSo.FindProperty("_brassMaterial").objectReferenceValue = pal.Brass;
            rifleSo.ApplyModifiedProperties();

            // Set final resting position & rotation on bench
            rifleRoot.transform.position = new Vector3(0.05f, 0.94f, 1.25f);
            rifleRoot.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            // Infinite Magazine Dispenser on Lane 2 Bench Mat (Bullets Never End!)
            var rifleDispenserObj = new GameObject("RifleMagazineDispenser");
            rifleDispenserObj.transform.SetParent(weaponsRoot.transform, false);
            rifleDispenserObj.transform.position = new Vector3(0.35f, 0.94f, 1.25f);
            var rifleDispenser = rifleDispenserObj.AddComponent<MagazineDispenser>();
            rifleDispenser.Initialize(WeaponType.Rifle, 30, pal.DarkPolymer, pal.Brass);
        }

        private static void CreateScoreboardAndUI(Transform parent, TargetManager manager, MaterialPalette pal)
        {
            var kioskRoot = new GameObject("WorldSpaceScoreboardKiosk");
            kioskRoot.transform.SetParent(parent, false);
            kioskRoot.transform.position = new Vector3(-5.2f, 0f, 2.2f);
            kioskRoot.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

            // 1. Pedestal / Base Pillar
            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            pedestal.transform.SetParent(kioskRoot.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            pedestal.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
            pedestal.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            AddSurface(pedestal, SurfaceType.Metal, 30f);

            // 2. Main Display Housing
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = "DisplayHousing";
            housing.transform.SetParent(kioskRoot.transform, false);
            housing.transform.localPosition = new Vector3(0f, 1.45f, 0f);
            housing.transform.localRotation = Quaternion.Euler(-5f, 0f, 0f);
            housing.transform.localScale = new Vector3(1.75f, 1.15f, 0.12f);
            housing.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            AddSurface(housing, SurfaceType.Metal, 25f);

            // 3. Dark Screen Face Inset
            var screenFace = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screenFace.name = "ScreenFace";
            screenFace.transform.SetParent(housing.transform, false);
            screenFace.transform.localPosition = new Vector3(0f, 0.04f, -0.56f);
            screenFace.transform.localScale = new Vector3(0.94f, 0.88f, 0.08f);
            screenFace.GetComponent<Renderer>().sharedMaterial = pal.DarkPolymer;
            Object.DestroyImmediate(screenFace.GetComponent<Collider>());

            // 4. Console Shelf for Physical Poke Buttons
            var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shelf.name = "ConsoleShelf";
            shelf.transform.SetParent(kioskRoot.transform, false);
            shelf.transform.localPosition = new Vector3(0f, 0.95f, -0.28f);
            shelf.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            shelf.transform.localScale = new Vector3(1.65f, 0.08f, 0.38f);
            shelf.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            AddSurface(shelf, SurfaceType.Metal, 20f);

            // 5. World Space Canvas on Display Screen
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null)
            {
                try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            }

            var canvasObj = new GameObject("KioskWorldCanvas");
            canvasObj.transform.SetParent(housing.transform, false);
            canvasObj.transform.localPosition = new Vector3(0f, 0.04f, -0.62f);
            canvasObj.transform.localRotation = Quaternion.identity;
            canvasObj.transform.localScale = new Vector3(0.0014f, 0.0014f, 0.0014f);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1150f, 700f);
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Canvas Background Panel
            CreatePanel(canvasObj.transform, "CanvasBg", Vector2.zero, new Vector2(1150f, 700f), new Color(0.04f, 0.06f, 0.09f, 0.96f));

            // Header
            CreateText(canvasObj.transform, "Title", "TACTICAL RANGE COMMAND & SETTINGS", 28, Color.cyan, TextAnchor.MiddleCenter, new Vector2(0f, 310f), new Vector2(1100f, 40f), font, true);
            CreateText(canvasObj.transform, "Subtitle", "World-Space Ray & Poke | 30s Ghost Replay (~1.47 KB/s) | Comfort Turns & Vignette", 14, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0f, 275f), new Vector2(1100f, 25f), font, false);

            // =====================================
            // COLUMN 1: SCOREBOARD (Left, x = -375)
            // =====================================
            CreatePanel(canvasObj.transform, "ScorePanel", new Vector2(-375f, 15f), new Vector2(340f, 480f), new Color(0.08f, 0.12f, 0.18f, 0.90f));
            CreateText(canvasObj.transform, "ScoreHeader", "=== SCOREBOARD ===", 20, Color.yellow, TextAnchor.MiddleCenter, new Vector2(-375f, 225f), new Vector2(320f, 30f), font, true);

            var currentScoreText = CreateText(canvasObj.transform, "CurrentScoreText", "CURRENT SCORE\n<color=#00FFFF>000000</color>", 22, Color.white, TextAnchor.MiddleCenter, new Vector2(-375f, 140f), new Vector2(320f, 70f), font, true);
            var highScoreText = CreateText(canvasObj.transform, "HighScoreText", "ALL-TIME HIGH SCORE\n<color=#FFD700>000000</color>", 20, Color.white, TextAnchor.MiddleCenter, new Vector2(-375f, 40f), new Vector2(320f, 70f), font, true);
            var statsText = CreateText(canvasObj.transform, "StatsText", "HITS: 0  |  ACCURACY: 100.0%  |  TARGETS: 8", 16, new Color(0.7f, 1f, 0.7f), TextAnchor.MiddleCenter, new Vector2(-375f, -48f), new Vector2(320f, 32f), font, false);

            var targetCountText = CreateText(canvasObj.transform, "TargetCountText", "ACTIVE TARGETS: <color=#00FFFF>8</color> / 300", 15, Color.white, TextAnchor.MiddleCenter, new Vector2(-375f, -86f), new Vector2(320f, 24f), font, false);

            var decTargetBtn = CreateButton(canvasObj.transform, "DecTargetBtn", "◀ -4", new Vector2(-500f, -118f), new Vector2(60f, 32f), new Color(0.18f, 0.32f, 0.48f, 0.95f), font, out _);
            var targetSlider = CreateSlider(canvasObj.transform, "TargetCountSlider", 4f, 300f, 8f, new Vector2(-375f, -118f), new Vector2(170f, 18f));
            var incTargetBtn = CreateButton(canvasObj.transform, "IncTargetBtn", "+4 ▶", new Vector2(-250f, -118f), new Vector2(60f, 32f), new Color(0.18f, 0.32f, 0.48f, 0.95f), font, out _);

            var resetBtn = CreateButton(canvasObj.transform, "ResetScoreBtn", "RESET SCORE", new Vector2(-375f, -172f), new Vector2(260f, 40f), new Color(0.65f, 0.2f, 0.1f, 0.95f), font, out _);

            // =====================================
            // COLUMN 2: COMFORT SETTINGS (Center, x = 0)
            // =====================================
            CreatePanel(canvasObj.transform, "ComfortPanel", new Vector2(0f, 15f), new Vector2(370f, 480f), new Color(0.08f, 0.12f, 0.18f, 0.90f));
            CreateText(canvasObj.transform, "ComfortHeader", "=== COMFORT SETTINGS ===", 20, Color.yellow, TextAnchor.MiddleCenter, new Vector2(0f, 225f), new Vector2(350f, 30f), font, true);

            var turnModeBtn = CreateButton(canvasObj.transform, "TurnModeBtn", "TURN MODE: SNAP (45°)", new Vector2(0f, 175f), new Vector2(330f, 42f), new Color(0.15f, 0.35f, 0.55f, 0.95f), font, out Text turnModeText);

            var snapAngleText = CreateText(canvasObj.transform, "SnapAngleText", "Snap Angle: 45°", 15, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 135f), new Vector2(330f, 22f), font, false);
            var snapAngleSlider = CreateSlider(canvasObj.transform, "SnapAngleSlider", 15f, 90f, 45f, new Vector2(0f, 112f), new Vector2(310f, 18f));

            var smoothSpeedText = CreateText(canvasObj.transform, "SmoothSpeedText", "Smooth Speed: 90°/s", 15, Color.white, TextAnchor.MiddleCenter, new Vector2(0f, 82f), new Vector2(330f, 22f), font, false);
            var smoothSpeedSlider = CreateSlider(canvasObj.transform, "SmoothSpeedSlider", 30f, 180f, 90f, new Vector2(0f, 60f), new Vector2(310f, 18f));

            var vignetteBtn = CreateButton(canvasObj.transform, "VignetteBtn", "COMFORT VIGNETTE: ENABLED", new Vector2(0f, 10f), new Vector2(330f, 42f), new Color(0.35f, 0.18f, 0.52f, 0.95f), font, out Text vignetteText);
            var handednessBtn = CreateButton(canvasObj.transform, "HandednessBtn", "HAND: RIGHT", new Vector2(0f, -50f), new Vector2(330f, 42f), new Color(0.12f, 0.45f, 0.40f, 0.95f), font, out Text handednessText);
            var stanceBtn = CreateButton(canvasObj.transform, "StanceBtn", "STANCE: STANDING", new Vector2(0f, -110f), new Vector2(330f, 42f), new Color(0.25f, 0.35f, 0.45f, 0.95f), font, out Text stanceText);

            // =====================================
            // COLUMN 3: 30s REPLAY (Right, x = +375)
            // =====================================
            CreatePanel(canvasObj.transform, "ReplayPanel", new Vector2(375f, 15f), new Vector2(340f, 480f), new Color(0.08f, 0.12f, 0.18f, 0.90f));
            CreateText(canvasObj.transform, "ReplayHeader", "=== 30s REPLAY SYSTEM ===", 20, Color.yellow, TextAnchor.MiddleCenter, new Vector2(375f, 225f), new Vector2(320f, 30f), font, true);

            var playReplayBtn = CreateButton(canvasObj.transform, "PlayReplayBtn", "▶ PLAY 30s REPLAY", new Vector2(375f, 175f), new Vector2(300f, 48f), new Color(0.06f, 0.55f, 0.45f, 0.95f), font, out Text playReplayText);
            var stopReplayBtn = CreateButton(canvasObj.transform, "StopReplayBtn", "⏹ STOP REPLAY", new Vector2(375f, 115f), new Vector2(300f, 42f), new Color(0.55f, 0.15f, 0.15f, 0.95f), font, out _);

            CreateText(canvasObj.transform, "ScrubberLabel", "Playback Scrubber (0.0s - 30.0s):", 14, Color.white, TextAnchor.MiddleLeft, new Vector2(375f, 72f), new Vector2(300f, 22f), font, false);
            var replayScrubber = CreateSlider(canvasObj.transform, "ReplayScrubber", 0f, 1f, 0f, new Vector2(375f, 48f), new Vector2(300f, 20f));

            var telemetryText = CreateText(canvasObj.transform, "TelemetryText",
                "30s Rolling Buffer: RECORDING\nCompact Bitrate: ~1.47 KB/s | 49B/frame\nSpatial Quantization: 1mm (P1+P2+Guns)\nGhost Avatars & Bullets Active\nPress 'P' to Toggle Replay Anytime",
                13, new Color(0f, 1f, 0.85f), TextAnchor.MiddleLeft, new Vector2(375f, -65f), new Vector2(300f, 150f), font, false);

            // 6. Four Physical 3D Poke Buttons on Front Console Shelf
            var pokeReplay = Create3DPokeButton(shelf.transform, "Poke_Replay", "POKE: REPLAY", new Vector3(-0.52f, 0.05f, 0f), new Color(0f, 0.85f, 1f), pal);
            var pokeTurn = Create3DPokeButton(shelf.transform, "Poke_Turn", "POKE: TURN", new Vector3(-0.17f, 0.05f, 0f), new Color(0.2f, 0.85f, 0.35f), pal);
            var pokeVignette = Create3DPokeButton(shelf.transform, "Poke_Vignette", "POKE: VIGNETTE", new Vector3(0.17f, 0.05f, 0f), new Color(0.75f, 0.3f, 0.95f), pal);
            var pokeReset = Create3DPokeButton(shelf.transform, "Poke_Reset", "POKE: RESET", new Vector3(0.52f, 0.05f, 0f), new Color(1f, 0.45f, 0.1f), pal);

            // 7. Add and Wire WorldSpaceSettingsUI
            var ui = kioskRoot.AddComponent<WorldSpaceSettingsUI>();
            var uiSo = new SerializedObject(ui);

            uiSo.FindProperty("_currentScoreText").objectReferenceValue = currentScoreText;
            uiSo.FindProperty("_highScoreText").objectReferenceValue = highScoreText;
            uiSo.FindProperty("_statsText").objectReferenceValue = statsText;
            uiSo.FindProperty("_resetScoreButton").objectReferenceValue = resetBtn;

            uiSo.FindProperty("_targetManager").objectReferenceValue = manager;
            uiSo.FindProperty("_targetCountText").objectReferenceValue = targetCountText;
            uiSo.FindProperty("_targetCountSlider").objectReferenceValue = targetSlider;
            uiSo.FindProperty("_decreaseTargetButton").objectReferenceValue = decTargetBtn;
            uiSo.FindProperty("_increaseTargetButton").objectReferenceValue = incTargetBtn;

            uiSo.FindProperty("_turnModeButton").objectReferenceValue = turnModeBtn;
            uiSo.FindProperty("_turnModeText").objectReferenceValue = turnModeText;
            uiSo.FindProperty("_snapAngleSlider").objectReferenceValue = snapAngleSlider;
            uiSo.FindProperty("_snapAngleText").objectReferenceValue = snapAngleText;
            uiSo.FindProperty("_smoothSpeedSlider").objectReferenceValue = smoothSpeedSlider;
            uiSo.FindProperty("_smoothSpeedText").objectReferenceValue = smoothSpeedText;
            uiSo.FindProperty("_vignetteButton").objectReferenceValue = vignetteBtn;
            uiSo.FindProperty("_vignetteText").objectReferenceValue = vignetteText;
            uiSo.FindProperty("_handednessButton").objectReferenceValue = handednessBtn;
            uiSo.FindProperty("_handednessText").objectReferenceValue = handednessText;
            uiSo.FindProperty("_stanceButton").objectReferenceValue = stanceBtn;
            uiSo.FindProperty("_stanceText").objectReferenceValue = stanceText;

            uiSo.FindProperty("_playReplayButton").objectReferenceValue = playReplayBtn;
            uiSo.FindProperty("_playReplayButtonText").objectReferenceValue = playReplayText;
            uiSo.FindProperty("_stopReplayButton").objectReferenceValue = stopReplayBtn;
            uiSo.FindProperty("_replayScrubber").objectReferenceValue = replayScrubber;
            uiSo.FindProperty("_replayTelemetryText").objectReferenceValue = telemetryText;

            uiSo.FindProperty("_pokePlayReplay").objectReferenceValue = pokeReplay;
            uiSo.FindProperty("_pokeTurnMode").objectReferenceValue = pokeTurn;
            uiSo.FindProperty("_pokeVignette").objectReferenceValue = pokeVignette;
            uiSo.FindProperty("_pokeResetScore").objectReferenceValue = pokeReset;

            uiSo.ApplyModifiedProperties();
        }

        private static VRPokeInteractable Create3DPokeButton(Transform parent, string name, string label, Vector3 localPos, Color capColor, MaterialPalette pal)
        {
            var buttonRoot = new GameObject(name);
            buttonRoot.transform.SetParent(parent, false);
            buttonRoot.transform.localPosition = localPos;
            buttonRoot.transform.localRotation = Quaternion.identity;

            // Base socket collar
            var socket = GameObject.CreatePrimitive(PrimitiveType.Cube);
            socket.name = "Socket";
            socket.transform.SetParent(buttonRoot.transform, false);
            socket.transform.localPosition = Vector3.zero;
            socket.transform.localScale = new Vector3(0.24f, 0.04f, 0.16f);
            socket.GetComponent<Renderer>().sharedMaterial = pal.Steel;
            Object.DestroyImmediate(socket.GetComponent<Collider>());

            // Moving Cap
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "MovingCap";
            cap.transform.SetParent(buttonRoot.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            cap.transform.localScale = new Vector3(0.20f, 0.05f, 0.13f);

            var capMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard"));
            capMat.color = capColor;
            capMat.SetFloat("_Smoothness", 0.85f);
            cap.GetComponent<Renderer>().sharedMaterial = capMat;
            Object.DestroyImmediate(cap.GetComponent<Collider>());

            // 3D Floating TextMesh Label
            var labelObj = new GameObject("Label3D");
            labelObj.transform.SetParent(buttonRoot.transform, false);
            labelObj.transform.localPosition = new Vector3(0f, 0.075f, 0.02f);
            labelObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = labelObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.015f;
            tm.fontSize = 32;
            tm.fontStyle = FontStyle.Bold;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;

            // Trigger BoxCollider and Rigidbody for poke & ray interaction
            var rb = buttonRoot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var col = buttonRoot.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.04f, 0f);
            col.size = new Vector3(0.24f, 0.12f, 0.20f);

            var poke = buttonRoot.AddComponent<VRPokeInteractable>();
            var pokeSo = new SerializedObject(poke);
            pokeSo.FindProperty("_displayName").stringValue = label;
            pokeSo.FindProperty("_pressDepthMeters").floatValue = 0.022f;
            pokeSo.FindProperty("_buttonCapTransform").objectReferenceValue = cap.transform;
            pokeSo.ApplyModifiedProperties();

            return poke;
        }

        private static GameObject CreatePanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static Text CreateText(Transform parent, string name, string text, int fontSize, Color color, TextAnchor alignment, Vector2 pos, Vector2 size, Font font, bool bold = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;
            if (font != null) txt.font = font;
            if (bold) txt.fontStyle = FontStyle.Bold;
            txt.supportRichText = true;
            txt.raycastTarget = false;
            return txt;
        }

        private static Button CreateButton(Transform parent, string name, string labelText, Vector2 pos, Vector2 size, Color bgColor, Font font, out Text buttonText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.75f;
            btn.colors = colors;

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, -8f);
            col.size = new Vector3(size.x, size.y, 25f);

            buttonText = CreateText(go.transform, "Label", labelText, 17, Color.white, TextAnchor.MiddleCenter, Vector2.zero, size, font, true);
            return btn;
        }

        private static Slider CreateSlider(Transform parent, string name, float min, float max, float current, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            // Background track
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(go.transform, false);
            var bgRect = bgGo.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bgGo.AddComponent<Image>();
            bgImg.color = new Color(0.18f, 0.22f, 0.28f, 0.9f);

            // Fill area
            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(go.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0f, 0.25f);
            faRect.anchorMax = new Vector2(1f, 0.75f);
            faRect.offsetMin = Vector2.zero;
            faRect.offsetMax = Vector2.zero;

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0f, 0.75f, 1f, 0.95f);

            // Handle area
            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(go.transform, false);
            var haRect = handleArea.AddComponent<RectTransform>();
            haRect.anchorMin = Vector2.zero;
            haRect.anchorMax = Vector2.one;
            haRect.offsetMin = Vector2.zero;
            haRect.offsetMax = Vector2.zero;

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.AddComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(20f, 0f);
            var handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = current;

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, -8f);
            col.size = new Vector3(size.x, size.y, 25f);

            return slider;
        }

        private static void AddSurface(GameObject obj, SurfaceType type, float thicknessMm)
        {
            if (obj == null) return;
            if (obj.GetComponent<Collider>() == null)
            {
                obj.AddComponent<BoxCollider>();
            }
            var surfaceId = obj.GetComponent<SurfaceIdentifier>();
            if (surfaceId == null) surfaceId = obj.AddComponent<SurfaceIdentifier>();
            if (surfaceId == null) return;

            var so = new SerializedObject(surfaceId);
            so.FindProperty("_surfaceType").enumValueIndex = (int)type;
            so.FindProperty("_estimatedThicknessMm").floatValue = thicknessMm;
            so.ApplyModifiedProperties();
        }
    }
}
#endif
