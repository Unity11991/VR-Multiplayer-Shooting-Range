using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;

namespace ShootingRange.Weapons
{
    [RequireComponent(typeof(TwoHandedRifleGrip))]
    public class RifleController : WeaponBase
    {
        [Header("Rifle Attachments")]
        [SerializeField] private Transform _opticalSightTransform;
        [SerializeField] private bool _hasBurstMode = true;

        public Transform OpticalSightTransform => _opticalSightTransform;
    }
}

