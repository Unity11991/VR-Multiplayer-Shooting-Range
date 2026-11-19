using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Interaction
{
    public class GrabPoint : MonoBehaviour
    {
        [Header("Grab Configuration")]
        [SerializeField] private bool _isSecondaryGrip = false;
        [SerializeField] private Handedness _allowedHand = Handedness.Right;
        [SerializeField] private bool _anyHandAllowed = true;
        [SerializeField] private float _grabRadius = 0.12f;
        [SerializeField] private Vector3 _handPoseRotationOffset = Vector3.zero;
        [SerializeField] private HandPoseType _preferredPose = HandPoseType.GripPistol;

        public bool IsSecondaryGrip => _isSecondaryGrip;
        public float GrabRadius => _grabRadius;
        public HandPoseType PreferredPose => _preferredPose;

        public bool CanBeGrabbedBy(Handedness hand)
        {
            return _anyHandAllowed || _allowedHand == hand;
        }

        public Quaternion GetTargetRotation(Quaternion handRotation)
        {
            return handRotation * Quaternion.Euler(_handPoseRotationOffset);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _isSecondaryGrip ? Color.cyan : Color.green;
            Gizmos.DrawWireSphere(transform.position, _grabRadius);
        }
    }
}

