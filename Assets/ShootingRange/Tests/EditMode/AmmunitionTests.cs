using NUnit.Framework;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Weapons;

namespace ShootingRange.Tests.EditMode
{
    [TestFixture]
    public class AmmunitionTests
    {
        [Test]
        public void Magazine_ConsumesRoundsUntilEmpty()
        {
            var magObj = new GameObject("TestMagazine");
            var mag = magObj.AddComponent<Magazine>();
            mag.Initialize(3);

            Assert.AreEqual(3, mag.CurrentAmmo);
            Assert.IsFalse(mag.IsEmpty);

            Assert.IsTrue(mag.TryConsumeRound());
            Assert.AreEqual(2, mag.CurrentAmmo);

            Assert.IsTrue(mag.TryConsumeRound());
            Assert.AreEqual(1, mag.CurrentAmmo);

            Assert.IsTrue(mag.TryConsumeRound());
            Assert.AreEqual(0, mag.CurrentAmmo);
            Assert.IsTrue(mag.IsEmpty);

            Assert.IsFalse(mag.TryConsumeRound(), "Cannot consume round from empty magazine.");

            Object.DestroyImmediate(magObj);
        }
    }
}

