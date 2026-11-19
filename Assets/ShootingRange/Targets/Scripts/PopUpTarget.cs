using UnityEngine;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Target type that pops up from cover / floor, stays up, then ducks down, or knocks flat when shot.
    /// </summary>
    public class PopUpTarget : TargetEntity
    {
        [Header("Pop-Up Hinge Configuration")]
        [SerializeField] private Transform _popHingeTransform;
        [SerializeField] private float _upAngle = 0f;
        [SerializeField] private float _downAngle = 90f;
        [SerializeField] private float _hingeSpeed = 16.0f;
        [SerializeField] private float _popupStayUpDuration = 5.5f;
        [SerializeField] private float _popupStayDownDuration = 3.0f;

        private float _currentAngle = 0f;
        private float _cycleTimer;
        private bool _isUp = true;

        protected override void Awake()
        {
            base.Awake();
            if (_popHingeTransform == null) _popHingeTransform = _visualMeshTransform != null ? _visualMeshTransform : transform;
            _cycleTimer = _popupStayUpDuration;
        }

        public override void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation)
        {
            base.Initialize(id, spawnPosition, spawnRotation);
            // Randomize cycle phase across targets so 300 targets don't pop up in lockstep
            _isUp = (id % 2 == 0);
            _cycleTimer = UnityEngine.Random.Range(1.5f, _popupStayUpDuration);
        }

        protected override void StepMovement(float deltaTime)
        {
            // Automatic popup duck-and-cover cycle
            _cycleTimer -= deltaTime;
            if (_cycleTimer <= 0f)
            {
                _isUp = !_isUp;
                _cycleTimer = _isUp ? _popupStayUpDuration : _popupStayDownDuration;
            }

            float targetAngle = _isUp ? _upAngle : _downAngle;
            _currentAngle = Mathf.MoveTowards(_currentAngle, targetAngle, deltaTime * _hingeSpeed * 30.0f);

            if (_popHingeTransform != null)
            {
                _popHingeTransform.localRotation = Quaternion.Euler(_currentAngle, 0f, 0f);
            }
        }

        protected override void OnDestroyedReaction()
        {
            base.OnDestroyedReaction();
            _isUp = false;
            _currentAngle = _downAngle;
            if (_popHingeTransform != null)
            {
                _popHingeTransform.localRotation = Quaternion.Euler(_downAngle, 0f, 0f);
            }
        }

        public override void ResetTarget()
        {
            base.ResetTarget();
            _isUp = true;
            _cycleTimer = _popupStayUpDuration;
            _currentAngle = _upAngle;
            if (_popHingeTransform != null)
            {
                _popHingeTransform.localRotation = Quaternion.Euler(_upAngle, 0f, 0f);
            }
        }
    }
}

