using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Weapons
{
    public class MagazineWell : MonoBehaviour
    {
        [SerializeField] private WeaponBase _weapon;
        [SerializeField] private WeaponType _acceptedWeaponType = WeaponType.Pistol;

        private void Awake()
        {
            if (_weapon == null) _weapon = GetComponentInParent<WeaponBase>();
            if (_weapon != null && _weapon.Definition != null)
            {
                _acceptedWeaponType = _weapon.Definition.Type;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryInsert(other);
        }

        private void TryInsert(Collider other)
        {
            if (_weapon == null || other == null) return;

            // Never process colliders that are part of this weapon or already parented to it
            if (other.transform.IsChildOf(_weapon.transform)) return;

            var mag = other.GetComponentInParent<Magazine>();
            if (mag == null) return;

            // Never re-insert the magazine already inside
            if (_weapon.InsertedMagazine == mag) return;

            // Never re-insert a magazine that was just ejected in the last 1.2s
            if (_weapon.LastEjectedMagazine == mag && Time.time - _weapon.LastEjectTime < 1.2f) return;

            // Don't insert spent empty magazines
            if (mag.IsEmpty) return;

            if (mag.CompatibleWeaponType == _acceptedWeaponType)
            {
                if (_weapon.InsertedMagazine != null)
                {
                    _weapon.EjectMagazine();
                }

                if (mag.IsGrabbed && mag.CurrentGrabbingHand.HasValue)
                {
                    mag.OnReleased(mag.CurrentGrabbingHand.Value, Vector3.zero, Vector3.zero);
                }
                _weapon.InsertMagazine(mag);
            }
        }
    }
}

