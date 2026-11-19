using NUnit.Framework;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Ballistics;

namespace ShootingRange.Tests.EditMode
{
    [TestFixture]
    public class BallisticsMathTests
    {
        [Test]
        public void KineticEnergy_MatchesPhysicalFormula()
        {
            float massKg = 0.008f; // 8 grams (9mm)
            float velocityMps = 380f;
            float expectedJoules = 0.5f * massKg * (velocityMps * velocityMps);

            float calculatedJoules = 0.5f * massKg * Mathf.Pow(velocityMps, 2);

            Assert.AreEqual(expectedJoules, calculatedJoules, 0.01f);
            Assert.AreEqual(577.6f, expectedJoules, 0.1f);
        }

        [Test]
        public void RicochetCheck_GrazingAngle_DetectsRicochet()
        {
            var surfaceMat = ScriptableObject.CreateInstance<SurfaceMaterialDefinition>();
            // Ricochet threshold = 25 degrees grazing
            bool ricochetAt15Deg = surfaceMat.CheckRicochet(15f, 500f);
            bool ricochetAt45Deg = surfaceMat.CheckRicochet(45f, 500f);

            Assert.IsTrue(ricochetAt15Deg, "Grazing impact at 15 degrees should cause ricochet.");
            Assert.IsFalse(ricochetAt45Deg, "Direct impact at 45 degrees should not cause ricochet.");
        }

        [Test]
        public void PenetrationCheck_ThinWoodVsArmor_CalculatesEnergyLoss()
        {
            var woodSurface = ScriptableObject.CreateInstance<SurfaceMaterialDefinition>();
            // Wood penetration check
            float bulletEnergy = 600f;
            float woodThicknessMm = 15f; // 15mm board

            bool penetrated = woodSurface.CheckPenetration(bulletEnergy, woodThicknessMm, out float exitEnergy);

            Assert.IsTrue(penetrated, "High energy round should penetrate 15mm wood.");
            Assert.Less(exitEnergy, bulletEnergy, "Exit energy must be strictly less than initial energy.");
            Assert.Greater(exitEnergy, 0f, "Bullet retains remaining energy after penetrating thin barrier.");
        }
    }
}

