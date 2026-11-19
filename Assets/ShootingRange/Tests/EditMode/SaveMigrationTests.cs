using NUnit.Framework;
using UnityEngine;
using ShootingRange.Save;

namespace ShootingRange.Tests.EditMode
{
    [TestFixture]
    public class SaveMigrationTests
    {
        [Test]
        public void LoadAndMigrate_OldSchemaV1_MigratesToCurrentSchema()
        {
            // Legacy V1 JSON without HighScore, statistics, or Replay fields
            string legacyV1Json = @"{
                ""SchemaVersion"": 1,
                ""IsLeftHanded"": true,
                ""IsSeatedMode"": true,
                ""PlayerHeightOffset"": 0.15,
                ""UseSnapTurn"": false,
                ""MasterVolume"": 0.8
            }";

            GameSettingsData migrated = GameSettingsData.LoadAndMigrate(legacyV1Json);

            Assert.IsNotNull(migrated);
            Assert.AreEqual(GameSettingsData.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.IsTrue(migrated.IsLeftHanded, "Preserved legacy field IsLeftHanded");
            Assert.IsTrue(migrated.IsSeatedMode, "Preserved legacy field IsSeatedMode");
            Assert.AreEqual(0.15f, migrated.PlayerHeightOffset, 0.001f);
            Assert.AreEqual(0.8f, migrated.MasterVolume, 0.001f);

            // Verified newly added V2 fields received valid defaults
            Assert.Greater(migrated.SnapTurnAngle, 0f);
            Assert.Greater(migrated.SmoothTurnSpeed, 0f);
            Assert.IsTrue(migrated.ComfortVignetteEnabled);

            // Verified newly added V3 fields received valid defaults
            Assert.IsTrue(migrated.ReplayAutoRecord);
            Assert.Greater(migrated.ReplayCompactKbPerSec, 0f);
            Assert.IsFalse(string.IsNullOrEmpty(migrated.HighScoreDate));
        }

        [Test]
        public void LoadAndMigrate_OldSchemaV2_MigratesToCurrentSchema()
        {
            // Legacy V2 JSON with HighScore and comfort settings but without V3 replay fields
            string legacyV2Json = @"{
                ""SchemaVersion"": 2,
                ""IsLeftHanded"": false,
                ""IsSeatedMode"": false,
                ""HighScore"": 18500,
                ""TotalShotsFired"": 150,
                ""TotalTargetsHit"": 142,
                ""ComfortVignetteEnabled"": true
            }";

            GameSettingsData migrated = GameSettingsData.LoadAndMigrate(legacyV2Json);

            Assert.IsNotNull(migrated);
            Assert.AreEqual(GameSettingsData.CurrentSchemaVersion, migrated.SchemaVersion);
            Assert.AreEqual(18500, migrated.HighScore);
            Assert.AreEqual(150, migrated.TotalShotsFired);
            Assert.AreEqual(142, migrated.TotalTargetsHit);
            Assert.IsTrue(migrated.ReplayAutoRecord);
            Assert.Greater(migrated.ReplayCompactKbPerSec, 0f);
        }

        [Test]
        public void EmptyOrNullJson_GeneratesValidDefaultSettings()
        {
            GameSettingsData emptyData = GameSettingsData.LoadAndMigrate("");
            Assert.IsNotNull(emptyData);
            Assert.AreEqual(GameSettingsData.CurrentSchemaVersion, emptyData.SchemaVersion);
            Assert.IsTrue(emptyData.ReplayAutoRecord);
        }
    }
}
