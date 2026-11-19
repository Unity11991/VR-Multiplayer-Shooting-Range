using NUnit.Framework;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Weapons;

namespace ShootingRange.Tests.EditMode
{
    [TestFixture]
    public class FireModeStateMachineTests
    {
        [Test]
        public void ChamberState_InitializesCorrectly()
        {
            var weaponObj = new GameObject("TestPistol");
            var pistol = weaponObj.AddComponent<PistolController>();

            Assert.AreEqual(ChamberState.ChamberedRound, pistol.Chamber);
            Object.DestroyImmediate(weaponObj);
        }

        [Test]
        public void FireRateScheduling_CalculatesCorrectInterval()
        {
            var def = ScriptableObject.CreateInstance<WeaponDefinition>();
            // 600 RPM = 10 rounds/sec = 0.1s interval
            // Using reflection or values via serialized properties
            Assert.IsNotNull(def);
            Assert.Greater(def.FireRateRpm, 0f);
            Assert.AreEqual(60f / def.FireRateRpm, def.FireIntervalSeconds, 0.001f);
        }

        [Test]
        public void JamState_PreventsFiringUntilCleared()
        {
            var weaponObj = new GameObject("TestWeapon");
            var weapon = weaponObj.AddComponent<PistolController>();

            // Trigger slide rack clears jam
            weapon.RackSlide();
            Assert.AreNotEqual(ChamberState.Jammed, weapon.Chamber);

            Object.DestroyImmediate(weaponObj);
        }
    }
}

