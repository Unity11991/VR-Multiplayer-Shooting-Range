using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Attach to target colliders to define hit multipliers, hit zone types, and base scoring.
    /// </summary>
    public class TargetHitZone : MonoBehaviour, ITargetHitZone
    {
        [SerializeField] private HitZone _zone = HitZone.Body;
        [SerializeField] private float _damageMultiplier = 1.0f;
        [SerializeField] private int _baseScore = 100;

        public HitZone Zone => _zone;
        public float DamageMultiplier => _damageMultiplier;
        public int BaseScore => _baseScore;

        public void Configure(HitZone zone, float damageMultiplier, int baseScore = 100)
        {
            _zone = zone;
            _damageMultiplier = damageMultiplier;
            _baseScore = baseScore;
        }
    }
}

