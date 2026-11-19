using UnityEngine;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Target type that moves laterally side-to-side on a continuous rail or path.
    /// </summary>
    public class StrafingTarget : TargetEntity
    {
        [Header("Strafing Configuration")]
        [SerializeField] private float _strafeDistance = 2.8f;
        [SerializeField] private float _strafeSpeed = 1.8f;

        private float _strafeTimer;

        public override void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            base.Initialize(id, spawnPosition, spawnRotation);
            // Randomize phase so adjacent targets don't move in lockstep
            _strafeTimer = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        protected override void StepMovement(float deltaTime)
        {
            if (_spawnPosition == Vector3.zero)
            {
                if (transform.position != Vector3.zero) _spawnPosition = transform.position;
                else _spawnPosition = new Vector3(0f, 1.5f, 16f);
            }

            _strafeTimer += deltaTime * _strafeSpeed;
            float offset = Mathf.Sin(_strafeTimer) * _strafeDistance;
            transform.position = _spawnPosition + transform.right * offset;
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

