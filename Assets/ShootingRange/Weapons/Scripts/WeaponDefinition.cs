using System;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Weapons
{
    [CreateAssetMenu(fileName = "WeaponDef_", menuName = "ShootingRange/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("General")]
        [SerializeField] private string _weaponName = "Tactical Weapon";
        [SerializeField] private WeaponType _weaponType = WeaponType.Pistol;
        [Tooltip("Weapon weight in kilograms. Affects recoil dampening and inertia.")]
        [SerializeField] private float _weightKg = 1.1f;

        [Header("Firing Rates & Modes")]
        [Tooltip("Rounds per minute.")]
        [SerializeField] private float _fireRateRpm = 450f;
        [SerializeField] private FireMode _defaultFireMode = FireMode.SemiAuto;
        [SerializeField] private FireMode[] _supportedFireModes = new FireMode[] { FireMode.SemiAuto };
        [SerializeField] private int _burstCount = 3;

        [Header("Ammunition & Chamber")]
        [SerializeField] private int _magazineCapacity = 15;
        [SerializeField] private int _chamberCapacity = 1;
        [Tooltip("Base probability of a jam per shot fired (0 to 1).")]
        [Range(0f, 0.05f)]
        [SerializeField] private float _jamProbability = 0.005f;

        [Header("Ballistics")]
        [Tooltip("Muzzle velocity in meters per second.")]
        [SerializeField] private float _bulletVelocityMps = 380f;
        [Tooltip("Bullet mass in kilograms.")]
        [SerializeField] private float _bulletMassKg = 0.008f;
        [Tooltip("Bullet caliber in millimeters.")]
        [SerializeField] private float _caliberMm = 9.0f;
        [Tooltip("Base damage dealt on center body hit.")]
        [SerializeField] private float _baseDamage = 35.0f;
        [Tooltip("Effective maximum range in meters.")]
        [SerializeField] private float _maxRange = 150f;

        [Header("Recoil & Accuracy")]
        [SerializeField] private float _recoilPitchDegrees = 3.5f;
        [SerializeField] private float _recoilYawDegrees = 0.8f;
        [SerializeField] private float _recoilKickBackMeters = 0.035f;
        [SerializeField] private float _recoilRecoverySpeed = 12.0f;
        [SerializeField] private float _baseSpreadDegrees = 0.15f;
        [SerializeField] private float _maxSpreadDegrees = 2.5f;
        [SerializeField] private float _spreadPerShotDegrees = 0.35f;

        [Header("Audio")]
        [SerializeField] private AudioClip _fireSound;
        [SerializeField] private AudioClip _dryFireSound;
        [SerializeField] private AudioClip _jamSound;
        [SerializeField] private AudioClip _slideRackSound;
        [SerializeField] private AudioClip _magazineInsertSound;
        [SerializeField] private AudioClip _magazineEjectSound;

        // Public Properties
        public string WeaponName => _weaponName;
        public WeaponType Type => _weaponType;
        public float WeightKg => _weightKg;

        public float FireRateRpm => _fireRateRpm;
        public float FireIntervalSeconds => 60.0f / Mathf.Max(_fireRateRpm, 1.0f);
        public FireMode DefaultFireMode => _defaultFireMode;
        public FireMode[] SupportedFireModes => _supportedFireModes;
        public int BurstCount => _burstCount;

        public int MagazineCapacity => _magazineCapacity;
        public int ChamberCapacity => _chamberCapacity;
        public float JamProbability => _jamProbability;

        public float BulletVelocityMps => _bulletVelocityMps;
        public float BulletMassKg => _bulletMassKg;
        public float CaliberMm => _caliberMm;
        public float BaseDamage => _baseDamage;
        public float MaxRange => _maxRange;

        public float RecoilPitchDegrees => _recoilPitchDegrees;
        public float RecoilYawDegrees => _recoilYawDegrees;
        public float RecoilKickBackMeters => _recoilKickBackMeters;
        public float RecoilRecoverySpeed => _recoilRecoverySpeed;
        public float BaseSpreadDegrees => _baseSpreadDegrees;
        public float MaxSpreadDegrees => _maxSpreadDegrees;
        public float SpreadPerShotDegrees => _spreadPerShotDegrees;

        public AudioClip FireSound => _fireSound;
        public AudioClip DryFireSound => _dryFireSound;
        public AudioClip JamSound => _jamSound;
        public AudioClip SlideRackSound => _slideRackSound;
        public AudioClip MagazineInsertSound => _magazineInsertSound;
        public AudioClip MagazineEjectSound => _magazineEjectSound;
    }
}

