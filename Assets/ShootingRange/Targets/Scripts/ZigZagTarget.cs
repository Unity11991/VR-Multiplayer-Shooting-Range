using UnityEngine;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Target type that moves in an evasive zig-zag path combining lateral weaving, depth oscillation, and banking turns.
    /// </summary>
    public class ZigZagTarget : TargetEntity
    {
        [Header("Zig-Zag Configuration")]
        [SerializeField] private float _forwardDistance = 3.2f;
        [SerializeField] private float _lateralDistance = 2.2f;
        [SerializeField] private float _frequency = 1.4f;
        [SerializeField] private float _bankAngle = 8.0f;

        private float _time;

        public override void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            base.Initialize(id, spawnPosition, spawnRotation);
            _time = UnityEngine.Random.Range(0f, 10f);
        }

        protected override void StepMovement(float deltaTime)
        {
            if (_spawnPosition == Vector3.zero)
            {
                if (transform.position != Vector3.zero) _spawnPosition = transform.position;
                else _spawnPosition = new Vector3(3.5f, 1.4f, 18f);
            }

            _time += deltaTime * _frequency;

            // Forward-backward oscillation combined with lateral sweep
            float lateralOffset = Mathf.Sin(_time * 2.0f) * _lateralDistance;
            float forwardOffset = Mathf.Cos(_time * 1.0f) * _forwardDistance;

            transform.position = _spawnPosition + transform.right * lateralOffset + transform.forward * forwardOffset;

            // Subtle roll banking into the turns for realistic evasive motion
            float roll = Mathf.Cos(_time * 2.0f) * -_bankAngle;
            transform.rotation = _spawnRotation * Quaternion.Euler(0f, 0f, roll);
        }

        public override void ResetTarget()
        {
            base.ResetTarget();
            if (_spawnPosition != Vector3.zero)
            {
                transform.position = _spawnPosition;
                transform.rotation = _spawnRotation;
            }
        }
    }
}

