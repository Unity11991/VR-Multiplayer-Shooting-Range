using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Ballistics
{
    [CreateAssetMenu(fileName = "Surface_", menuName = "ShootingRange/Surface Material Definition")]
    public class SurfaceMaterialDefinition : ScriptableObject
    {
        [Header("Surface Classification")]
        [SerializeField] private SurfaceType _surfaceType = SurfaceType.Concrete;
        [SerializeField] private string _surfaceName = "Concrete";

        [Header("Ballistic Properties")]
        [Tooltip("Hardness from 0 (very soft like glass/thin wood) to 1 (hard armor steel).")]
        [Range(0f, 1f)]
        [SerializeField] private float _hardness = 0.75f;

        [Tooltip("Energy loss in Joules per millimeter of material penetrated.")]
        [SerializeField] private float _energyLossPerMm = 45.0f;

        [Tooltip("Maximum thickness (mm) this material can be penetrated by standard rounds.")]
        [SerializeField] private float _maxPenetrationDepthMm = 35.0f;

        [Tooltip("Grazing angle in degrees below which ricochet occurs.")]
        [Range(5f, 65f)]
        [SerializeField] private float _ricochetAngleThresholdDegrees = 25.0f;

        [Tooltip("Fraction of kinetic energy preserved after a ricochet bounce.")]
        [Range(0.1f, 0.95f)]
        [SerializeField] private float _ricochetRestitution = 0.55f;

        public SurfaceType Type => _surfaceType;
        public string SurfaceName => _surfaceName;
        public float Hardness => _hardness;
        public float EnergyLossPerMm => _energyLossPerMm;
        public float MaxPenetrationDepthMm => _maxPenetrationDepthMm;
        public float RicochetAngleThresholdDegrees => _ricochetAngleThresholdDegrees;
        public float RicochetRestitution => _ricochetRestitution;

        public bool CheckRicochet(float angleOfIncidenceDegrees, float kineticEnergy)
        {
            // Grazing impact angle (near 0 relative to tangent, near 90 to normal)
            return angleOfIncidenceDegrees <= _ricochetAngleThresholdDegrees;
        }

        public bool CheckPenetration(float kineticEnergy, float materialThicknessMm, out float exitEnergy)
        {
            float requiredEnergy = materialThicknessMm * _energyLossPerMm;
            if (kineticEnergy > requiredEnergy && materialThicknessMm <= _maxPenetrationDepthMm)
            {
                exitEnergy = kineticEnergy - requiredEnergy;
                return true;
            }
            exitEnergy = 0f;
            return false;
        }
    }
}

