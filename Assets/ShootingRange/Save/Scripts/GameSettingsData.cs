using System;
using System.IO;
using UnityEngine;

namespace ShootingRange.Save
{
    [Serializable]
    public class GameSettingsData
    {
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion = CurrentSchemaVersion;

        // Player & Comfort Settings
        public bool IsLeftHanded = false;
        public bool IsSeatedMode = false;
        public float PlayerHeightOffset = 0.0f;
        public bool UseSnapTurn = true;
        public float SnapTurnAngle = 45.0f;
        public float SmoothTurnSpeed = 90.0f;
        public bool ComfortVignetteEnabled = true;
        public float VignetteIntensity = 0.65f;

        // Audio Settings
        public float MasterVolume = 1.0f;
        public float SfxVolume = 1.0f;

        // Gameplay Scores (Added in Schema V2)
        public int HighScore = 0;
        public int TotalShotsFired = 0;
        public int TotalTargetsHit = 0;

        // Replay & Telemetry Preferences (Added in Schema V3)
        public bool ReplayAutoRecord = true;
        public float ReplayCompactKbPerSec = 1.47f;
        public string HighScoreDate = "";

        /// <summary>
        /// Migrates JSON strings from older schema versions to current version.
        /// An old save file from V1 or V2 will still load cleanly without loss of data.
        /// </summary>
        public static GameSettingsData LoadAndMigrate(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return new GameSettingsData();
            }

            // Inspect schema version from json
            int version = 1;
            if (json.Contains("\"SchemaVersion\""))
            {
                int index = json.IndexOf("\"SchemaVersion\":", StringComparison.Ordinal);
                if (index >= 0)
                {
                    string sub = json.Substring(index + 16, 5);
                    int.TryParse(sub.Split(',', '}')[0].Trim(), out version);
                }
            }

            GameSettingsData data = JsonUtility.FromJson<GameSettingsData>(json);

            // Progressive Migration Pipeline
            if (version < 2)
            {
                data = MigrateV1ToV2(data);
            }
            if (version < 3)
            {
                data = MigrateV2ToV3(data);
            }

            data.SchemaVersion = CurrentSchemaVersion;
            return data;
        }

        private static GameSettingsData MigrateV1ToV2(GameSettingsData oldData)
        {
            // V1 -> V2 added statistics tracking and comfort vignette controls
            if (oldData.SnapTurnAngle <= 0f) oldData.SnapTurnAngle = 45.0f;
            if (oldData.SmoothTurnSpeed <= 0f) oldData.SmoothTurnSpeed = 90.0f;
            oldData.ComfortVignetteEnabled = true;
            oldData.VignetteIntensity = 0.65f;
            oldData.HighScore = 0;
            return oldData;
        }

        private static GameSettingsData MigrateV2ToV3(GameSettingsData oldData)
        {
            // V2 -> V3 added replay preferences and high score timestamp
            oldData.ReplayAutoRecord = true;
            oldData.ReplayCompactKbPerSec = 1.47f;
            if (string.IsNullOrEmpty(oldData.HighScoreDate))
            {
                oldData.HighScoreDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            return oldData;
        }
    }

    public static class SaveManager
    {
        private static string SaveFilePath => Path.Combine(Application.persistentDataPath, "shooting_range_save.json");

        public static void Save(GameSettingsData data)
        {
            try
            {
                data.SchemaVersion = GameSettingsData.CurrentSchemaVersion;
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(SaveFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Failed to save game settings: {ex.Message}");
            }
        }

        public static GameSettingsData Load()
        {
            try
            {
                if (!File.Exists(SaveFilePath))
                {
                    var defaultData = new GameSettingsData();
                    Save(defaultData);
                    return defaultData;
                }

                string json = File.ReadAllText(SaveFilePath);
                return GameSettingsData.LoadAndMigrate(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveManager] Failed to load game settings: {ex.Message}. Falling back to default.");
                return new GameSettingsData();
            }
        }
    }
}
