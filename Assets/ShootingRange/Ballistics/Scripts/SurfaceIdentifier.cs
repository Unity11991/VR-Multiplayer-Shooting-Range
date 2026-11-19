using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Ballistics
{
    public class SurfaceIdentifier : MonoBehaviour
    {
        [SerializeField] private SurfaceType _surfaceType = SurfaceType.Concrete;
        [SerializeField] private SurfaceMaterialDefinition _materialDefinition;
        [SerializeField] private float _estimatedThicknessMm = 20.0f;

        public SurfaceType Type => _materialDefinition != null ? _materialDefinition.Type : _surfaceType;
        public SurfaceMaterialDefinition MaterialDefinition => _materialDefinition;
        public float ThicknessMm => _estimatedThicknessMm;
    }
}

