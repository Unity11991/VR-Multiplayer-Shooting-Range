using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;

namespace ShootingRange.Weapons
{
    [RequireComponent(typeof(PhysicsGrabbable))]
    public class PistolController : WeaponBase
    {
        [Header("Pistol Features")]
        [SerializeField] private bool _hasSlideLock = true;
        private bool _isSlideLocked;

        public bool IsSlideLocked => _isSlideLocked;

        protected override void Awake()
        {
            if (GetComponent<PhysicsGrabbable>() == null)
            {
                gameObject.AddComponent<PhysicsGrabbable>();
            }
            base.Awake();
        }

        public void ReleaseSlideLock()
        {
            if (_isSlideLocked)
            {
                _isSlideLocked = false;
                RackSlide();
            }
        }
    }
}
