using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Heavy Armoured Target with high-absorption ballistic plating and exposed glowing holographic weak points.
    /// </summary>
    public class ArmouredTarget : TargetEntity
    {
        [Header("Armoured Target Properties")]
        [Tooltip("Fraction of standard damage absorbed by body armor (e.g. 0.90 = 90% reduction).")]
        [Range(0.5f, 0.98f)]
        [SerializeField] private float _armorAbsorptionRatio = 0.90f;

        [SerializeField] private float _weakPointDamageMultiplier = 3.5f;

        [Header("Heavy Movement Patrol")]
        [SerializeField] private float _advanceDistance = 1.8f;
        [SerializeField] private float _advanceSpeed = 0.5f;

        private float _patrolTimer;

        public override void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            base.Initialize(id, spawnPosition, spawnRotation);
            _patrolTimer = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _knockdownAngleTarget = 52.0f; // Heavy shield slumps back on destruction
        }

        public override void ApplyDamage(float amount, HitZone zone, Vector3 hitPoint, Vector3 hitNormal, ulong attackerClientId)
        {
            float finalDamage = amount;

            if (zone == HitZone.WeakPoint)
            {
                // Critical strike on exposed weak point!
                finalDamage = amount * _weakPointDamageMultiplier;
                _maxWobbleDegrees = 20.0f; // Massive recoil impact
            }
            else
            {
                // Deflected / absorbed by heavy composite ballistic armor
                finalDamage = amount * (1.0f - _armorAbsorptionRatio);
                _maxWobbleDegrees = 4.0f; // Armor barely flinches
            }

            base.ApplyDamage(finalDamage, zone, hitPoint, hitNormal, attackerClientId);
        }

        protected override void StepMovement(float deltaTime)
        {
            if (_spawnPosition == Vector3.zero)
            {
                if (transform.position != Vector3.zero) _spawnPosition = transform.position;
                else _spawnPosition = new Vector3(-3.5f, 1.4f, 22f);
            }

            _patrolTimer += deltaTime * _advanceSpeed;
            float depthOffset = Mathf.Sin(_patrolTimer) * _advanceDistance;
            transform.position = _spawnPosition + transform.forward * depthOffset;
        }

        public override void ResetTarget()
        {
            base.ResetTarget();
            if (_spawnPosition != Vector3.zero)
            {
                transform.position = _spawnPosition;
            }
        }
    }
}

