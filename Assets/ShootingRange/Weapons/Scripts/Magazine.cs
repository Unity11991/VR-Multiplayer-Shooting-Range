using System;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;

namespace ShootingRange.Weapons
{
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(100)]
    public class Magazine : PhysicsGrabbable, IMagazine
    {
        [Header("Magazine Data")]
        [SerializeField] private WeaponType _compatibleWeaponType = WeaponType.Pistol;
        [SerializeField] private int _maxAmmo = 15;
        [SerializeField] private int _currentAmmo = 15;

        private Vector3 _seatedLocalPosition;
        private Quaternion _seatedLocalRotation = Quaternion.identity;

        public WeaponType CompatibleWeaponType => _compatibleWeaponType;
        public int MaxAmmo => _maxAmmo;
        public int CurrentAmmo => _currentAmmo;
        public bool IsEmpty => _currentAmmo <= 0;
        public bool IsSeated { get; private set; }

        public event Action<int> OnAmmoChanged;

        public void SetSeated(bool seated, Transform parent = null, Vector3 localPos = default)
        {
            IsSeated = seated;
            _seatedLocalPosition = localPos;
            _seatedLocalRotation = Quaternion.identity;

            // Clear grabbing hand if seated
            if (seated && IsGrabbed && CurrentGrabbingHand.HasValue)
            {
                OnReleased(CurrentGrabbingHand.Value, Vector3.zero, Vector3.zero);
            }

            var cols = GetComponentsInChildren<Collider>();
            foreach (var c in cols)
            {
                c.enabled = !seated;
            }

            var rb = Rigidbody != null ? Rigidbody : GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.detectCollisions = !seated;
                rb.isKinematic = seated;
                rb.interpolation = seated ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
                if (seated)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                }
            }

            if (seated && parent != null)
            {
                transform.SetParent(parent, false);
                transform.localPosition = localPos;
                transform.localRotation = Quaternion.identity;
                if (rb != null)
                {
                    rb.position = transform.position;
                    rb.rotation = transform.rotation;
                }
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (IsSeated)
            {
                var rb = Rigidbody != null ? Rigidbody : GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
            }
        }

        private void FixedUpdate()
        {
            if (IsSeated && transform.parent != null)
            {
                transform.localPosition = _seatedLocalPosition;
                transform.localRotation = _seatedLocalRotation;
                var rb = Rigidbody != null ? Rigidbody : GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = transform.position;
                    rb.rotation = transform.rotation;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                return;
            }
        }

        private void LateUpdate()
        {
            if (IsSeated && transform.parent != null)
            {
                transform.localPosition = _seatedLocalPosition;
                transform.localRotation = _seatedLocalRotation;
                var rb = Rigidbody != null ? Rigidbody : GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = transform.position;
                    rb.rotation = transform.rotation;
                }
                return;
            }
        }

        public void Initialize(int ammoCount)
        {
            _maxAmmo = Mathf.Max(ammoCount, _maxAmmo);
            _currentAmmo = Mathf.Clamp(ammoCount, 0, _maxAmmo);
            OnAmmoChanged?.Invoke(_currentAmmo);
        }

        public void Initialize(int ammoCount, WeaponType weaponType)
        {
            _compatibleWeaponType = weaponType;
            _maxAmmo = Mathf.Max(ammoCount, 1);
            _currentAmmo = Mathf.Clamp(ammoCount, 0, _maxAmmo);
            OnAmmoChanged?.Invoke(_currentAmmo);
        }

        public bool TryConsumeRound()
        {
            if (_currentAmmo > 0)
            {
                _currentAmmo--;
                OnAmmoChanged?.Invoke(_currentAmmo);
                return true;
            }
            return false;
        }

        public void Refill()
        {
            _currentAmmo = _maxAmmo;
            OnAmmoChanged?.Invoke(_currentAmmo);
        }
    }
}
