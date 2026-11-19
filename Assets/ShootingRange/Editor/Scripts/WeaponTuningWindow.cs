#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using ShootingRange.Weapons;
using ShootingRange.Core;

namespace ShootingRange.Editor
{
    public class WeaponTuningWindow : EditorWindow
    {
        private WeaponDefinition _selectedWeapon;
        private Vector2 _scrollPos;

        [MenuItem("Shooting Range/Weapon Tuning Window")]
        public static void ShowWindow()
        {
            var win = GetWindow<WeaponTuningWindow>("Weapon Tuning");
            win.minSize = new Vector2(400, 550);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Weapon Designer & Recoil Tuning Tool", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Select a WeaponDefinition ScriptableObject to edit ballistics, recoil, firing modes and preview kinetic statistics live.", MessageType.Info);

            EditorGUILayout.Space(5);
            _selectedWeapon = (WeaponDefinition)EditorGUILayout.ObjectField("Selected Weapon", _selectedWeapon, typeof(WeaponDefinition), false);

            if (_selectedWeapon == null)
            {
                EditorGUILayout.HelpBox("Please select or assign a WeaponDefinition asset to begin tuning.", MessageType.Warning);
                if (GUILayout.Button("Find Weapons in Project"))
                {
                    string[] guids = AssetDatabase.FindAssets("t:WeaponDefinition");
                    if (guids.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                        _selectedWeapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                    }
                }
                return;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            SerializedObject so = new SerializedObject(_selectedWeapon);
            so.Update();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Weapon Statistics & Kinetic Energy", EditorStyles.boldLabel);
            DrawWeaponTelemetry();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("General & Ammunition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("_weaponName"));
            EditorGUILayout.PropertyField(so.FindProperty("_weaponType"));
            EditorGUILayout.PropertyField(so.FindProperty("_weightKg"));
            EditorGUILayout.PropertyField(so.FindProperty("_magazineCapacity"));
            EditorGUILayout.PropertyField(so.FindProperty("_chamberCapacity"));
            EditorGUILayout.PropertyField(so.FindProperty("_jamProbability"));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Firing Rates & Modes", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("_fireRateRpm"));
            EditorGUILayout.PropertyField(so.FindProperty("_defaultFireMode"));
            EditorGUILayout.PropertyField(so.FindProperty("_supportedFireModes"), true);
            EditorGUILayout.PropertyField(so.FindProperty("_burstCount"));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Ballistics & Damage", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("_bulletVelocityMps"));
            EditorGUILayout.PropertyField(so.FindProperty("_bulletMassKg"));
            EditorGUILayout.PropertyField(so.FindProperty("_caliberMm"));
            EditorGUILayout.PropertyField(so.FindProperty("_baseDamage"));
            EditorGUILayout.PropertyField(so.FindProperty("_maxRange"));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Recoil & Spread Tuning", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("_recoilPitchDegrees"));
            EditorGUILayout.PropertyField(so.FindProperty("_recoilYawDegrees"));
            EditorGUILayout.PropertyField(so.FindProperty("_recoilKickBackMeters"));
            EditorGUILayout.PropertyField(so.FindProperty("_recoilRecoverySpeed"));
            EditorGUILayout.PropertyField(so.FindProperty("_baseSpreadDegrees"));
            EditorGUILayout.PropertyField(so.FindProperty("_maxSpreadDegrees"));
            EditorGUILayout.PropertyField(so.FindProperty("_spreadPerShotDegrees"));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Audio Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(so.FindProperty("_fireSound"));
            EditorGUILayout.PropertyField(so.FindProperty("_dryFireSound"));
            EditorGUILayout.PropertyField(so.FindProperty("_jamSound"));
            EditorGUILayout.PropertyField(so.FindProperty("_slideRackSound"));
            EditorGUILayout.PropertyField(so.FindProperty("_magazineInsertSound"));
            EditorGUILayout.PropertyField(so.FindProperty("_magazineEjectSound"));

            EditorGUILayout.Space(15);
            if (GUILayout.Button("Save Weapon Changes", GUILayout.Height(35)))
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(_selectedWeapon);
                AssetDatabase.SaveAssets();
                Debug.Log($"[WeaponTuning] Successfully saved changes to {_selectedWeapon.name}.");
            }

            so.ApplyModifiedProperties();
            EditorGUILayout.EndScrollView();
        }

        private void DrawWeaponTelemetry()
        {
            float speed = _selectedWeapon.BulletVelocityMps;
            float mass = _selectedWeapon.BulletMassKg;
            float energyJoules = 0.5f * mass * (speed * speed);
            float rps = _selectedWeapon.FireRateRpm / 60.0f;
            float dps = _selectedWeapon.BaseDamage * rps;
            float momentumNs = mass * speed;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"• Muzzle Kinetic Energy: {energyJoules:F1} Joules", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"• Projectile Momentum: {momentumNs:F2} N·s", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"• Sustained DPS: {dps:F1} dmg/sec ({rps:F1} rounds/sec)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"• Cyclic Interval: {_selectedWeapon.FireIntervalSeconds * 1000f:F1} ms per cycle", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
    }
}
#endif

