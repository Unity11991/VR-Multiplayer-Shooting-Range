using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ShootingRange.Core;
using ShootingRange.Interaction;
using ShootingRange.Weapons;
using ShootingRange.Targets;

namespace ShootingRange.Tests.PlayMode
{
    public class InteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator GrabbingAndRelease_AppliesReleaseVelocity()
        {
            var grabbableObj = new GameObject("TestGrabbable");
            var rb = grabbableObj.AddComponent<Rigidbody>();
            var grabbable = grabbableObj.AddComponent<PhysicsGrabbable>();

            var handAnchor = new GameObject("HandAnchor").transform;
            handAnchor.position = new Vector3(0f, 1.5f, 0f);

            // Simulate grab
            grabbable.OnGrabbed(Handedness.Right, handAnchor, Vector3.zero, Vector3.zero);
            Assert.IsTrue(grabbable.IsGrabbed);

            yield return new WaitForFixedUpdate();

            // Simulate release with throwing velocity
            Vector3 releaseVel = new Vector3(0f, 2f, 5f);
            Vector3 releaseAngVel = Vector3.zero;
            grabbable.OnReleased(Handedness.Right, releaseVel, releaseAngVel);

            Assert.IsFalse(grabbable.IsGrabbed);
            Assert.IsTrue(grabbable.Rigidbody.useGravity);
            Assert.Greater(grabbable.Rigidbody.linearVelocity.magnitude, 0f);

            Object.Destroy(grabbableObj);
            Object.Destroy(handAnchor.gameObject);
        }

        [UnityTest]
        public IEnumerator HandToHandTransfer_TransfersOwnershipCleanly()
        {
            var grabbableObj = new GameObject("TransferObject");
            var rb = grabbableObj.AddComponent<Rigidbody>();
            var grabbable = grabbableObj.AddComponent<PhysicsGrabbable>();

            var handLeft = new GameObject("HandLeft").transform;
            var handRight = new GameObject("HandRight").transform;

            // Grab with Right Hand
            grabbable.OnGrabbed(Handedness.Right, handRight, Vector3.zero, Vector3.zero);
            Assert.AreEqual(Handedness.Right, grabbable.CurrentGrabbingHand);

            yield return null;

            // Transfer to Left Hand
            grabbable.OnHandTransfer(Handedness.Right, Handedness.Left, handLeft);
            Assert.AreEqual(Handedness.Left, grabbable.CurrentGrabbingHand);
            Assert.IsTrue(grabbable.IsGrabbed);

            Object.Destroy(grabbableObj);
            Object.Destroy(handLeft.gameObject);
            Object.Destroy(handRight.gameObject);
        }

        [UnityTest]
        public IEnumerator TargetDamage_ReducesHealthAndDestroysAtZero()
        {
            var targetObj = new GameObject("TestTarget");
            var target = targetObj.AddComponent<StrafingTarget>();
            target.Initialize(1, Vector3.zero, Quaternion.identity);

            float initialHealth = target.Health;
            target.ApplyDamage(40f, HitZone.Body, Vector3.zero, Vector3.up, 0);

            Assert.AreEqual(initialHealth - 40f, target.Health);
            Assert.IsFalse(target.IsDestroyed);

            // Apply lethal damage
            target.ApplyDamage(100f, HitZone.Head, Vector3.zero, Vector3.up, 0);
            Assert.AreEqual(0f, target.Health);
            Assert.IsTrue(target.IsDestroyed);

            yield return null;
            Object.Destroy(targetObj);
        }
    }
}

