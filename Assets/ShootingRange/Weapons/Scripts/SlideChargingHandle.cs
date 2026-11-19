using System;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;

namespace ShootingRange.Weapons
{
    public class SlideChargingHandle : MonoBehaviour
    {
        [Header("Slide Travel Settings")]
        [SerializeField] private Vector3 _slideTravelAxis = Vector3.back;
        [SerializeField] private float _maxTravelDistanceMeters = 0.055f;
        [SerializeField] private float _springReturnSpeed = 12.0f;
        [SerializeField] private float _triggerThreshold = 0.90f;

        private Vector3 _restingLocalPos;
        private float _currentTravelRatio; // 0 = forward resting, 1 = fully retracted
        private bool _hasTriggeredEjection;
        private WeaponBase _weapon;

        public float CurrentTravelRatio => _currentTravelRatio;
        public bool IsFullyRetracted => _currentTravelRatio >= _triggerThreshold;

        public event Action OnFullyRetracted;
        public event Action OnSlideReturnedForward;

        private void Awake()
        {
            _restingLocalPos = transform.localPosition;
            _weapon = GetComponentInParent<WeaponBase>();
        }

        private void Update()
        {
            // Spring return
            if (_currentTravelRatio > 0f)
            {
                _currentTravelRatio = Mathf.MoveTowards(_currentTravelRatio, 0f, Time.deltaTime * _springReturnSpeed);
                transform.localPosition = _restingLocalPos + _slideTravelAxis.normalized * (_currentTravelRatio * _maxTravelDistanceMeters);

                if (_currentTravelRatio <= 0.01f && _hasTriggeredEjection)
                {
                    _hasTriggeredEjection = false;
                    OnSlideReturnedForward?.Invoke();
                }
            }
        }

        public void PullBack(float ratio)
        {
            _currentTravelRatio = Mathf.Clamp01(ratio);
            transform.localPosition = _restingLocalPos + _slideTravelAxis.normalized * (_currentTravelRatio * _maxTravelDistanceMeters);

            if (_currentTravelRatio >= _triggerThreshold && !_hasTriggeredEjection)
            {
                _hasTriggeredEjection = true;
                OnFullyRetracted?.Invoke();
            }
        }

        public void QuickRack()
        {
            PullBack(1.0f);
        }
    }
}

