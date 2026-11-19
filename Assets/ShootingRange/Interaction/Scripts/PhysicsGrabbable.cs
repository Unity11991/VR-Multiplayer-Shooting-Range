using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Input;

namespace ShootingRange.Interaction
{
    [RequireComponent(typeof(Rigidbody))]
    public class PhysicsGrabbable : MonoBehaviour, IGrabbable
    {
        [Header("Physics Follow PID Tuning")]
        [SerializeField] private float _positionProportionalGain = 32f;
        [SerializeField] private float _rotationProportionalGain = 28f;
        [SerializeField] private float _maxLinearVelocity = 30f;
        [SerializeField] private float _maxAngularVelocity = 35f;
        [SerializeField] private float _throwVelocityMultiplier = 1.15f;

        [Header("Grab Points")]
        [SerializeField] private GrabPoint _primaryGrabPoint;
        [SerializeField] private GrabPoint _secondaryGrabPoint;

        protected Rigidbody _rigidbody;
        protected Transform _currentAttachTransform;
        protected Handedness? _currentHand;
        protected Vector3 _grabLocalPositionOffset;
        protected Quaternion _grabLocalRotationOffset;

        private readonly Queue<Vector3> _velocityHistory = new Queue<Vector3>();
        private readonly Queue<Vector3> _angularVelocityHistory = new Queue<Vector3>();
        private const int VelocityHistorySize = 5;

        public Transform Transform => transform;
        public Rigidbody Rigidbody
        {
            get
            {
                if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
                return _rigidbody;
            }
        }
        public bool IsGrabbed => _currentHand.HasValue;
        public Handedness? CurrentGrabbingHand => _currentHand;

        public GrabPoint PrimaryGrabPoint => _primaryGrabPoint;
        public GrabPoint SecondaryGrabPoint => _secondaryGrabPoint;

        public event Action<Handedness> OnGrabbedEvent;
        public event Action<Handedness> OnReleasedEvent;
        public event Action<Handedness, Handedness> OnHandTransferEvent;

        public Func<IGrabbable, bool> MagazineInsertHandler { get; set; }
        public Action ReloadHandler { get; set; }
        public Action EjectMagazineHandler { get; set; }
        public Action RackSlideHandler { get; set; }
        public Func<int> CurrentAmmoProvider { get; set; }
        public Func<int> MagazineCapacityProvider { get; set; }
        public Func<bool> IsChamberedProvider { get; set; }
        public Func<bool> IsReloadingProvider { get; set; }
        public Func<Vector3> RecoilOffsetProvider { get; set; }
        public Func<Quaternion> RecoilRotationProvider { get; set; }

        public virtual bool TryInsertMagazine(IGrabbable magazine)
        {
            return MagazineInsertHandler != null && MagazineInsertHandler(magazine);
        }

        public virtual void Reload() => ReloadHandler?.Invoke();
        public virtual void EjectMagazine() => EjectMagazineHandler?.Invoke();
        public virtual void RackSlide() => RackSlideHandler?.Invoke();

        protected virtual void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.maxAngularVelocity = _maxAngularVelocity;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            if (_primaryGrabPoint == null)
            {
                _primaryGrabPoint = GetComponentInChildren<GrabPoint>();
            }
        }

        public virtual Quaternion CalculateTargetRotation()
        {
            if (_primaryGrabPoint != null && _currentAttachTransform != null)
            {
                Quaternion grabPointLocalRot = _primaryGrabPoint.transform.localRotation;
                return _primaryGrabPoint.GetTargetRotation(_currentAttachTransform.rotation) * Quaternion.Inverse(grabPointLocalRot);
            }
            return _currentAttachTransform != null ? _currentAttachTransform.rotation : transform.rotation;
        }

        public virtual Vector3 CalculateTargetPosition(Quaternion targetRotation)
        {
            if (_primaryGrabPoint != null && _currentAttachTransform != null)
            {
                Vector3 grabPointLocalPos = _primaryGrabPoint.transform.localPosition;
                return _currentAttachTransform.position - targetRotation * grabPointLocalPos;
            }
            return _currentAttachTransform != null ? _currentAttachTransform.position : transform.position;
        }

        protected bool CheckIsDesktop()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                if (ShootingRangeCompositionRoot.Container.TryResolve<ITrackingProvider>(out var tracking) && tracking != null)
                {
                    if (tracking is DesktopVRSimulator ||
                        (tracking is InputModeManager imm && imm.CurrentMode == ActiveInputMode.DesktopSimulator))
                    {
                        return true;
                    }
                }
            }
            return FindAnyObjectByType<DesktopVRSimulator>() != null;
        }

        public virtual void OnGrabbed(Handedness hand, Transform handAttachPoint, Vector3 grabLinearVelocity, Vector3 grabAngularVelocity)
        {
            using (RangeProfilerMarkers.InteractionGrab.Auto())
            {
                _currentHand = hand;
                _currentAttachTransform = handAttachPoint;

                if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

                bool isDesktop = CheckIsDesktop();
                if (isDesktop)
                {
                    _rigidbody.isKinematic = true;
                    _rigidbody.useGravity = false;
                    _rigidbody.detectCollisions = false;
                }
                else
                {
                    _rigidbody.isKinematic = false;
                    _rigidbody.useGravity = false;
                    _rigidbody.linearDamping = 1.5f;
                    _rigidbody.angularDamping = 1.5f;
                    _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }

                // Snap directly into hand position to avoid initial jump
                if (handAttachPoint != null)
                {
                    Quaternion targetRot = CalculateTargetRotation();
                    Vector3 targetPos = CalculateTargetPosition(targetRot);
                    _rigidbody.position = targetPos;
                    _rigidbody.rotation = targetRot;
                    transform.position = targetPos;
                    transform.rotation = targetRot;
                    if (!_rigidbody.isKinematic)
                    {
                        _rigidbody.linearVelocity = Vector3.zero;
                        _rigidbody.angularVelocity = Vector3.zero;
                    }
                }

                _velocityHistory.Clear();
                _angularVelocityHistory.Clear();

                OnGrabbedEvent?.Invoke(hand);
            }
        }

        public virtual void OnReleased(Handedness hand, Vector3 releaseLinearVelocity, Vector3 releaseAngularVelocity)
        {
            using (RangeProfilerMarkers.InteractionVelocity.Auto())
            {
                if (_currentHand != hand) return;

                _currentHand = null;
                _currentAttachTransform = null;

                if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
                _rigidbody.isKinematic = false;
                _rigidbody.useGravity = true;
                _rigidbody.detectCollisions = true;
                _rigidbody.linearDamping = 0.05f;
                _rigidbody.angularDamping = 0.05f;

                // Compute smoothed release velocities from history
                Vector3 finalLinearVel = releaseLinearVelocity * _throwVelocityMultiplier;
                Vector3 finalAngVel = releaseAngularVelocity;

                if (_velocityHistory.Count > 0)
                {
                    Vector3 sumVel = Vector3.zero;
                    foreach (var v in _velocityHistory) sumVel += v;
                    finalLinearVel = (sumVel / _velocityHistory.Count) * _throwVelocityMultiplier;
                }

                if (_angularVelocityHistory.Count > 0)
                {
                    Vector3 sumAng = Vector3.zero;
                    foreach (var a in _angularVelocityHistory) sumAng += a;
                    finalAngVel = sumAng / _angularVelocityHistory.Count;
                }

                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.linearVelocity = Vector3.ClampMagnitude(finalLinearVel, _maxLinearVelocity);
                    _rigidbody.angularVelocity = Vector3.ClampMagnitude(finalAngVel, _maxAngularVelocity);
                }

                _velocityHistory.Clear();
                _angularVelocityHistory.Clear();

                OnReleasedEvent?.Invoke(hand);
            }
        }

        public virtual void OnHandTransfer(Handedness fromHand, Handedness toHand, Transform newAttachPoint)
        {
            if (_currentHand != fromHand) return;

            _currentHand = toHand;
            _currentAttachTransform = newAttachPoint;

            _velocityHistory.Clear();
            _angularVelocityHistory.Clear();

            OnHandTransferEvent?.Invoke(fromHand, toHand);
        }

        private void FixedUpdate()
        {
            if (!IsGrabbed || _currentAttachTransform == null) return;

            if (CheckIsDesktop())
            {
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.isKinematic = true;
                    _rigidbody.detectCollisions = false;
                }
                return;
            }

            if (_rigidbody != null && _rigidbody.useGravity)
            {
                _rigidbody.useGravity = false;
            }

            using (RangeProfilerMarkers.InteractionGrab.Auto())
            {
                UpdatePhysicsFollow();
            }
        }

        private void LateUpdate()
        {
            if (!IsGrabbed || _currentAttachTransform == null) return;

            // In Desktop mode, follow the hand attach transform directly in LateUpdate
            // right after the camera and hand positions have updated in Update().
            // This guarantees 100% zero-lag, jitter-free, ultra-smooth movement every frame!
            if (CheckIsDesktop())
            {
                UpdatePhysicsFollow();
            }
        }

        protected virtual void UpdatePhysicsFollow()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
            if (_currentAttachTransform == null) return;

            Quaternion targetRotation = CalculateTargetRotation();
            Vector3 targetPosition = CalculateTargetPosition(targetRotation);

            Vector3 recoilOffset = RecoilOffsetProvider != null ? RecoilOffsetProvider() : Vector3.zero;
            Quaternion recoilRotation = RecoilRotationProvider != null ? RecoilRotationProvider() : Quaternion.identity;
            if (recoilOffset != Vector3.zero || recoilRotation != Quaternion.identity)
            {
                targetPosition += targetRotation * recoilOffset;
                targetRotation = targetRotation * recoilRotation;
            }

            bool isDesktop = CheckIsDesktop();
            if (isDesktop)
            {
                _rigidbody.position = targetPosition;
                _rigidbody.rotation = targetRotation;
                transform.position = targetPosition;
                transform.rotation = targetRotation;
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.linearVelocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
                return;
            }

            ApplyPhysicsFollow(targetPosition, targetRotation);
        }

        public void ApplyPhysicsFollow(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            Vector3 positionDelta = targetPosition - _rigidbody.position;

            // Anti-drop / anti-separation safety:
            // If the weapon gets separated from the hand by > 0.35m (e.g. table snag or fast move), snap directly
            if (positionDelta.sqrMagnitude > 0.12f)
            {
                _rigidbody.position = targetPosition;
                _rigidbody.rotation = targetRotation;
                transform.position = targetPosition;
                transform.rotation = targetRotation;
                if (!_rigidbody.isKinematic)
                {
                    _rigidbody.linearVelocity = Vector3.zero;
                    _rigidbody.angularVelocity = Vector3.zero;
                }
                return;
            }

            if (!_rigidbody.isKinematic)
            {
                Vector3 targetVelocity = positionDelta * _positionProportionalGain;
                _rigidbody.linearVelocity = Vector3.ClampMagnitude(targetVelocity, _maxLinearVelocity);

                // Angular velocity follow
                Quaternion rotationDelta = targetRotation * Quaternion.Inverse(_rigidbody.rotation);
                rotationDelta.ToAngleAxis(out float angleInDegrees, out Vector3 rotationAxis);

                if (angleInDegrees > 180f) angleInDegrees -= 360f;

                if (Mathf.Abs(angleInDegrees) > 0.01f && !float.IsNaN(rotationAxis.x))
                {
                    Vector3 targetAngularVelocity = rotationAxis.normalized * (angleInDegrees * Mathf.Deg2Rad * _rotationProportionalGain);
                    _rigidbody.angularVelocity = Vector3.ClampMagnitude(targetAngularVelocity, _maxAngularVelocity);
                }
                else
                {
                    _rigidbody.angularVelocity = Vector3.zero;
                }
            }

            // Track velocity samples for release calculation
            _velocityHistory.Enqueue(_rigidbody.linearVelocity);
            _angularVelocityHistory.Enqueue(_rigidbody.angularVelocity);
            if (_velocityHistory.Count > VelocityHistorySize)
            {
                _velocityHistory.Dequeue();
                _angularVelocityHistory.Dequeue();
            }
        }
    }
}
