#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Weapons;
using ShootingRange.Ballistics;

namespace ShootingRange.Editor
{
    public static class WeaponAssetGenerator
    {
        [MenuItem("Shooting Range/Create Default Weapons & Surfaces")]
        public static void CreateDefaultAssets()
        {
            string weaponDir = "Assets/ShootingRange/Weapons/Data";
            if (!Directory.Exists(weaponDir)) Directory.CreateDirectory(weaponDir);

            string surfaceDir = "Assets/ShootingRange/Ballistics/Data";
            if (!Directory.Exists(surfaceDir)) Directory.CreateDirectory(surfaceDir);

            // 1. Semi-Auto Pistol Definition
            string pistolPath = Path.Combine(weaponDir, "Weapon_Pistol_9mm.asset");
            var pistol = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(pistolPath);
            if (pistol == null)
            {
                pistol = ScriptableObject.CreateInstance<WeaponDefinition>();
                var so = new SerializedObject(pistol);
                so.FindProperty("_weaponName").stringValue = "Tactical 9mm Pistol";
                so.FindProperty("_weaponType").enumValueIndex = (int)WeaponType.Pistol;
                so.FindProperty("_weightKg").floatValue = 1.05f;
                so.FindProperty("_fireRateRpm").floatValue = 420f;
                so.FindProperty("_defaultFireMode").enumValueIndex = (int)FireMode.SemiAuto;
                so.FindProperty("_magazineCapacity").intValue = 15;
                so.FindProperty("_chamberCapacity").intValue = 1;
                so.FindProperty("_bulletVelocityMps").floatValue = 380f;
                so.FindProperty("_bulletMassKg").floatValue = 0.008f; // 8 grams
                so.FindProperty("_caliberMm").floatValue = 9.0f;
                so.FindProperty("_baseDamage").floatValue = 35.0f;
                so.FindProperty("_maxRange").floatValue = 120.0f;
                so.FindProperty("_recoilPitchDegrees").floatValue = 0.65f;
                so.FindProperty("_recoilYawDegrees").floatValue = 0.12f;
                so.FindProperty("_recoilKickBackMeters").floatValue = 0.020f;
                so.FindProperty("_recoilRecoverySpeed").floatValue = 20.0f;
                so.FindProperty("_baseSpreadDegrees").floatValue = 0.04f;
                so.FindProperty("_maxSpreadDegrees").floatValue = 0.28f;
                so.FindProperty("_spreadPerShotDegrees").floatValue = 0.035f;
                so.FindProperty("_jamProbability").floatValue = 0.004f;
                so.ApplyModifiedProperties();
                AssetDatabase.CreateAsset(pistol, pistolPath);
            }

            // 2. Assault Rifle Definition
            string riflePath = Path.Combine(weaponDir, "Weapon_AssaultRifle_556.asset");
            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(riflePath);
            if (rifle == null)
            {
                rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
                var so = new SerializedObject(rifle);
                so.FindProperty("_weaponName").stringValue = "Tactical AR-15 (5.56mm)";
                so.FindProperty("_weaponType").enumValueIndex = (int)WeaponType.Rifle;
                so.FindProperty("_weightKg").floatValue = 3.4f;
                so.FindProperty("_fireRateRpm").floatValue = 750f;
                so.FindProperty("_defaultFireMode").enumValueIndex = (int)FireMode.FullAuto;
                var modesProp = so.FindProperty("_supportedFireModes");
                modesProp.arraySize = 3;
                modesProp.GetArrayElementAtIndex(0).enumValueIndex = (int)FireMode.SemiAuto;
                modesProp.GetArrayElementAtIndex(1).enumValueIndex = (int)FireMode.Burst;
                modesProp.GetArrayElementAtIndex(2).enumValueIndex = (int)FireMode.FullAuto;
                so.FindProperty("_burstCount").intValue = 3;
                so.FindProperty("_magazineCapacity").intValue = 30;
                so.FindProperty("_chamberCapacity").intValue = 1;
                so.FindProperty("_bulletVelocityMps").floatValue = 910f;
                so.FindProperty("_bulletMassKg").floatValue = 0.004f; // 4 grams
                so.FindProperty("_caliberMm").floatValue = 5.56f;
                so.FindProperty("_baseDamage").floatValue = 48.0f;
                so.FindProperty("_maxRange").floatValue = 500.0f;
                so.FindProperty("_recoilPitchDegrees").floatValue = 0.35f;
                so.FindProperty("_recoilYawDegrees").floatValue = 0.08f;
                so.FindProperty("_recoilKickBackMeters").floatValue = 0.015f;
                so.FindProperty("_recoilRecoverySpeed").floatValue = 22.0f;
                so.FindProperty("_baseSpreadDegrees").floatValue = 0.02f;
                so.FindProperty("_maxSpreadDegrees").floatValue = 0.20f;
                so.FindProperty("_spreadPerShotDegrees").floatValue = 0.02f;
                so.FindProperty("_jamProbability").floatValue = 0.002f;
                so.ApplyModifiedProperties();
                AssetDatabase.CreateAsset(rifle, riflePath);
            }

            // 3. Surface Definitions
            CreateSurfaceAsset(surfaceDir, "Surface_Concrete", SurfaceType.Concrete, 0.85f, 50f, 40f, 20f, 0.5f);
            CreateSurfaceAsset(surfaceDir, "Surface_Wood", SurfaceType.Wood, 0.35f, 15f, 80f, 35f, 0.35f);
            CreateSurfaceAsset(surfaceDir, "Surface_Metal", SurfaceType.Metal, 0.95f, 85f, 25f, 15f, 0.65f);
            CreateSurfaceAsset(surfaceDir, "Surface_Glass", SurfaceType.Glass, 0.20f, 5f, 100f, 45f, 0.25f);

            AssetDatabase.SaveAssets();
            Debug.Log("[WeaponAssetGenerator] Default weapons and surfaces generated successfully.");
        }

        private static void CreateSurfaceAsset(string dir, string name, SurfaceType type, float hardness, float energyLossPerMm, float maxPenetrationMm, float ricochetAngleDeg, float restitution)
        {
            string path = Path.Combine(dir, $"{name}.asset");
            if (AssetDatabase.LoadAssetAtPath<SurfaceMaterialDefinition>(path) != null) return;

            var surf = ScriptableObject.CreateInstance<SurfaceMaterialDefinition>();
            var so = new SerializedObject(surf);
            so.FindProperty("_surfaceType").enumValueIndex = (int)type;
            so.FindProperty("_surfaceName").stringValue = name;
            so.FindProperty("_hardness").floatValue = hardness;
            so.FindProperty("_energyLossPerMm").floatValue = energyLossPerMm;
            so.FindProperty("_maxPenetrationDepthMm").floatValue = maxPenetrationMm;
            so.FindProperty("_ricochetAngleThresholdDegrees").floatValue = ricochetAngleDeg;
            so.FindProperty("_ricochetRestitution").floatValue = restitution;
            so.ApplyModifiedProperties();

            AssetDatabase.CreateAsset(surf, path);
        }
    }
}
#endif

