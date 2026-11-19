using NUnit.Framework;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Ballistics;

namespace ShootingRange.Tests.EditMode
{
    [TestFixture]
    public class BulletCollisionTests
    {
        [Test]
        public void ContinuousRaycast_DetectsThinBarrierCollision()
        {
            // Create thin barrier (5mm = 0.005m)
            var barrierObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrierObj.transform.position = new Vector3(0f, 1f, 10f);
            barrierObj.transform.localScale = new Vector3(2f, 2f, 0.005f);

            // Fast bullet travelling 900 m/s with 0.02s step = 18m travel step
            Vector3 startPos = new Vector3(0f, 1f, 0f);
            Vector3 endPos = new Vector3(0f, 1f, 18f);
            Vector3 direction = (endPos - startPos).normalized;
            float distance = Vector3.Distance(startPos, endPos);

            bool hitDetected = Physics.Raycast(startPos, direction, out RaycastHit hit, distance);

            Assert.IsTrue(hitDetected, "Continuous sweep must intercept thin barrier even with large travel distance per frame.");
            Assert.AreEqual(barrierObj.GetComponent<Collider>(), hit.collider);

            Object.DestroyImmediate(barrierObj);
        }
    }
}

