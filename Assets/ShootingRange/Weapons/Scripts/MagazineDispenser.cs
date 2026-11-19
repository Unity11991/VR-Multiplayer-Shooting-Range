using System;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;

namespace ShootingRange.Weapons
{
    /// <summary>
    /// Infinite magazine dispenser on the shooting range bench.
    /// Whenever a magazine is picked up or removed, automatically spawns a fresh
    /// full magazine at the dispenser spawn point so ammunition never runs out.
    /// </summary>
    public class MagazineDispenser : MonoBehaviour
    {
        [Header("Dispenser Configuration")]
        [SerializeField] private WeaponType _weaponType = WeaponType.Pistol;
        [SerializeField] private int _magazineCapacity = 15;
        [SerializeField] private Material _magazineMaterial;
        [SerializeField] private Material _brassTipMaterial;
        [SerializeField] private float _respawnDelay = 0.15f;

        private Magazine _currentDispensedMag;
        private bool _isRespawning;

        public WeaponType DispenserType => _weaponType;
        public int Capacity => _magazineCapacity;

        private void Start()
        {
            if (_currentDispensedMag == null)
            {
                SpawnNewMagazine();
            }
        }

        public void Initialize(WeaponType weaponType, int capacity, Material magMat, Material brassMat)
        {
            _weaponType = weaponType;
            _magazineCapacity = capacity;
            _magazineMaterial = magMat;
            _brassTipMaterial = brassMat;

            if (_currentDispensedMag == null)
            {
                SpawnNewMagazine();
            }
        }

        public Magazine SpawnNewMagazine()
        {
            _isRespawning = false;

            var magObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            magObj.name = _weaponType == WeaponType.Rifle ? "InfiniteRifleMagazine" : "InfinitePistolMagazine";
            magObj.transform.SetParent(transform);
            magObj.transform.localPosition = Vector3.zero;
            magObj.transform.localRotation = Quaternion.identity;

            if (_weaponType == WeaponType.Rifle)
            {
                magObj.transform.localScale = new Vector3(0.034f, 0.18f, 0.08f);
            }
            else
            {
                magObj.transform.localScale = new Vector3(0.028f, 0.11f, 0.045f);
            }

            if (_magazineMaterial != null)
            {
                magObj.GetComponent<Renderer>().sharedMaterial = _magazineMaterial;
            }

            var rb = magObj.AddComponent<Rigidbody>();
            rb.mass = _weaponType == WeaponType.Rifle ? 0.45f : 0.25f;
            rb.isKinematic = true;

            var mag = magObj.AddComponent<Magazine>();
            mag.Initialize(_magazineCapacity, _weaponType);
            magObj.AddComponent<GrabPoint>();

            // Polished brass bullet tip visible on top
            var brassTip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            brassTip.name = "BrassBulletTip";
            brassTip.transform.SetParent(magObj.transform);
            brassTip.transform.localPosition = new Vector3(0f, 0.52f, 0.15f);
            brassTip.transform.localScale = new Vector3(0.55f, 0.25f, 0.65f);
            if (_brassTipMaterial != null)
            {
                brassTip.GetComponent<Renderer>().sharedMaterial = _brassTipMaterial;
            }
            if (Application.isPlaying)
            {
                Destroy(brassTip.GetComponent<Collider>());
            }
            else
            {
                DestroyImmediate(brassTip.GetComponent<Collider>());
            }

            // Listen to when this magazine is grabbed by the player!
            mag.OnGrabbedEvent += HandleMagazineGrabbed;
            _currentDispensedMag = mag;
            return mag;
        }

        private void Update()
        {
            // If the current magazine was taken, moved, or grabbed:
            if (_currentDispensedMag != null)
            {
                if (_currentDispensedMag.transform.parent != transform ||
                    _currentDispensedMag.IsGrabbed ||
                    Vector3.Distance(_currentDispensedMag.transform.position, transform.position) > 0.15f)
                {
                    HandleMagazineGrabbed(Handedness.Right);
                }
            }
        }

        private void HandleMagazineGrabbed(Handedness hand)
        {
            if (_currentDispensedMag != null)
            {
                _currentDispensedMag.OnGrabbedEvent -= HandleMagazineGrabbed;
                if (_currentDispensedMag.transform.parent == transform)
                {
                    _currentDispensedMag.transform.SetParent(null);
                    var rb = _currentDispensedMag.Rigidbody;
                    if (rb != null && !_currentDispensedMag.IsGrabbed)
                    {
                        rb.isKinematic = false;
                    }
                }
                _currentDispensedMag = null;
            }

            if (!_isRespawning)
            {
                _isRespawning = true;
                Invoke(nameof(SpawnNewMagazine), _respawnDelay);
            }
        }
    }
}

