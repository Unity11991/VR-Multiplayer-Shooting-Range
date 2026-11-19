using System;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Input;

namespace ShootingRange.Interaction
{
    /// <summary>
    /// Two-handed rifle interaction system.
    /// Primary hand controls the main grip and trigger.
    /// Secondary hand steers the barrel toward the off-hand with shoulder stock stabilization.
    /// Never parents to controllers.
    /// </summary>
    public class TwoHandedRifleGrip : PhysicsGrabbable, IWeaponInteraction
    {
        [Header("Weapon Type & Interaction")]
        [SerializeField] private WeaponType _weaponType = WeaponType.Rifle;
        [SerializeField] private Transform _barrelTransform;
        [SerializeField] private Transform _stockTransform;

        [Header("Shoulder Stock Stabilization")]
        [SerializeField] private Vector3 _rightShoulderOffset = new Vector3(0.16f, -0.18f, -0.10f);
        [SerializeField] private Vector3 _leftShoulderOffset = new Vector3(-0.16f, -0.18f, -0.10f);
        [SerializeField] private float _shoulderStabilizationWeight = 0.45f;
        [SerializeField] private float _twoHandedTransitionSpeed = 8.0f;

        [Header("Jitter Suppression Filter")]
        [SerializeField] private float _smoothingFactor = 16.0f;
        [SerializeField] private float _deadbandAngleDegrees = 0.08f;

        private Transform _secondaryAttachTransform;
        private Handedness? _secondaryHand;
        private float _twoHandedWeight = 0.0f;
        private Vector3 _filteredSecondaryPos;
        private Quaternion _filteredAimRotation = Quaternion.identity;

        public WeaponType Type => _weaponType;
        public bool IsTwoHanded => true;
        public bool IsSecondaryGrabbed => _secondaryHand.HasValue;
        public Handedness? SecondaryHand => _secondaryHand;
        public int CurrentAmmo => CurrentAmmoProvider != null ? CurrentAmmoProvider() : 0;
        public int MagazineCapacity => MagazineCapacityProvider != null ? MagazineCapacityProvider() : 30;
        public bool IsChambered => IsChamberedProvider != null && IsChamberedProvider();
        public bool IsReloading => IsReloadingProvider != null && IsReloadingProvider();
        public Vector3 RecoilOffset => RecoilOffsetProvider != null ? RecoilOffsetProvider() : Vector3.zero;
        public Quaternion RecoilRotation => RecoilRotationProvider != null ? RecoilRotationProvider() : Quaternion.identity;

        public event Action OnFired;
        public event Action<float, float> OnRecoilFired;

        public void TriggerFired() => OnFired?.Invoke();
        public void TriggerRecoilFired(float pitch, float yaw) => OnRecoilFired?.Invoke(pitch, yaw);

        public event Action<Handedness> OnSecondaryGrabbedEvent;
        public event Action<Handedness> OnSecondaryReleasedEvent;

        public void OnSecondaryGrabbed(Handedness hand, Transform secondaryAttachPoint)
        {
            if (!IsGrabbed) return;
            if (hand == CurrentGrabbingHand) return; // Cannot grab foregrip with same hand

            _secondaryHand = hand;
            _secondaryAttachTransform = secondaryAttachPoint;
            _filteredSecondaryPos = secondaryAttachPoint.position;
            _filteredAimRotation = Rigidbody.rotation;

            OnSecondaryGrabbedEvent?.Invoke(hand);
        }

        public void OnSecondaryReleased(Handedness hand)
        {
            if (_secondaryHand != hand) return;

            _secondaryHand = null;
            _secondaryAttachTransform = null;

            OnSecondaryReleasedEvent?.Invoke(hand);
        }



        public override void OnReleased(Handedness hand, Vector3 releaseLinearVelocity, Vector3 releaseAngularVelocity)
        {
            // If primary hand releases, release secondary as well
            if (CurrentGrabbingHand == hand && _secondaryHand.HasValue)
            {
                OnSecondaryReleased(_secondaryHand.Value);
            }
            base.OnReleased(hand, releaseLinearVelocity, releaseAngularVelocity);
        }

        protected override void UpdatePhysicsFollow()
        {
            if (_currentAttachTransform == null) return;

            bool isDesktop = CheckIsDesktop();
            // Update blend weight between 1-hand and 2-hand hold
            float targetWeight = IsSecondaryGrabbed ? 1.0f : 0.0f;
            _twoHandedWeight = isDesktop
                ? targetWeight
                : Mathf.MoveTowards(_twoHandedWeight, targetWeight, Time.deltaTime * _twoHandedTransitionSpeed);

            if (_twoHandedWeight <= 0.001f || _secondaryAttachTransform == null)
            {
                // Single hand physics follow
                base.UpdatePhysicsFollow();
                return;
            }

            // Two-handed physics follow
            CalculateTwoHandedFollow();
        }

        private void CalculateTwoHandedFollow()
        {
            if (_currentAttachTransform == null || _secondaryAttachTransform == null) return;

            Vector3 primaryPos = PrimaryGrabPoint != null
                ? Rigidbody.position + Rigidbody.rotation * PrimaryGrabPoint.transform.localPosition
                : Rigidbody.position;

            bool isDesktop = CheckIsDesktop();
            ITrackingProvider tracking = null;
            if (!isDesktop && ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve<ITrackingProvider>(out tracking);
            }

            Quaternion twoHandedAimRot;
            if (isDesktop)
            {
                // In Desktop mode, direct attach rotation ensures seamless crosshair alignment without jitter
                twoHandedAimRot = _currentAttachTransform.rotation;
                _filteredAimRotation = twoHandedAimRot;
            }
            else
            {
                // Filter secondary hand position to eliminate small micro-jitters in true VR
                Vector3 rawSecondaryPos = _secondaryAttachTransform.position;
                _filteredSecondaryPos = Vector3.Lerp(_filteredSecondaryPos, rawSecondaryPos, Time.fixedDeltaTime * _smoothingFactor);

                // Vector from primary hand to secondary hand
                Vector3 aimDirection = (_filteredSecondaryPos - primaryPos).normalized;
                if (aimDirection.sqrMagnitude < 0.001f) aimDirection = _currentAttachTransform.forward;

                Vector3 secondaryUp = _secondaryAttachTransform.up;
                twoHandedAimRot = Quaternion.LookRotation(aimDirection, secondaryUp);

                // Shoulder stock anchoring in true VR
                if (tracking != null)
                {
                    bool isLeftHanded = tracking.IsLeftHanded;
                    Vector3 shoulderOffset = isLeftHanded ? _leftShoulderOffset : _rightShoulderOffset;
                    Vector3 shoulderPos = tracking.HeadPosition + tracking.HeadRotation * shoulderOffset;

                    Vector3 shoulderToSecondary = (_filteredSecondaryPos - shoulderPos).normalized;
                    Quaternion shoulderAimRot = Quaternion.LookRotation(shoulderToSecondary, secondaryUp);
                    twoHandedAimRot = Quaternion.Slerp(twoHandedAimRot, shoulderAimRot, _shoulderStabilizationWeight);
                }

                // Apply deadband in VR
                float angleDelta = Quaternion.Angle(_filteredAimRotation, twoHandedAimRot);
                if (angleDelta > _deadbandAngleDegrees)
                {
                    _filteredAimRotation = Quaternion.Slerp(_filteredAimRotation, twoHandedAimRot, Time.fixedDeltaTime * _smoothingFactor);
                }
            }

            // Blend one-handed base rotation with two-handed rotation
            Quaternion oneHandedTargetRot = CalculateTargetRotation();
            Quaternion finalTargetRotation = isDesktop ? twoHandedAimRot : Quaternion.Slerp(oneHandedTargetRot, _filteredAimRotation, _twoHandedWeight);

            // Target position driven by primary hand with two-handed rotation
            Vector3 targetPosition = CalculateTargetPosition(finalTargetRotation);

            Vector3 recoilOffset = RecoilOffset;
            Quaternion recoilRotation = RecoilRotation;
            if (recoilOffset != Vector3.zero || recoilRotation != Quaternion.identity)
            {
                // In two-handed stance, apply clean linear kickback and dampen pitch rotation so the optic reticle remains centered
                Quaternion dampedRot = Quaternion.Slerp(Quaternion.identity, recoilRotation, 0.25f);
                targetPosition += finalTargetRotation * recoilOffset;
                finalTargetRotation = finalTargetRotation * dampedRot;
            }

            if (isDesktop)
            {
                // Direct instant tracking in desktop mode for razor-sharp aiming
                if (!Rigidbody.isKinematic)
                {
                    Rigidbody.linearVelocity = Vector3.zero;
                    Rigidbody.angularVelocity = Vector3.zero;
                    Rigidbody.isKinematic = true;
                    Rigidbody.detectCollisions = false;
                }
                Rigidbody.position = targetPosition;
                Rigidbody.rotation = finalTargetRotation;
                transform.position = targetPosition;
                transform.rotation = finalTargetRotation;
            }
            else
            {
                // Apply single, unified physics follow without fighting
                ApplyPhysicsFollow(targetPosition, finalTargetRotation);
            }
        }
    }
}
