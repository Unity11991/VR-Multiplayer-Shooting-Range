using UnityEngine;
using UnityEngine.UI;
using ShootingRange.Core;

namespace ShootingRange.UI
{
    public class ComfortVignette : MonoBehaviour
    {
        [Header("Vignette Settings")]
        [SerializeField] private Image _vignetteImage;
        [SerializeField] private float _fadeSpeed = 8.0f;

        private float _targetAlpha = 0f;
        private float _currentAlpha = 0f;
        private bool _isEnabled = true;
        private float _maxIntensity = 0.7f;

        public bool IsEnabled { get => _isEnabled; set => _isEnabled = value; }
        public float MaxIntensity { get => _maxIntensity; set => _maxIntensity = value; }

        private void Update()
        {
            if (_vignetteImage == null) return;

            if (!_isEnabled)
            {
                _targetAlpha = 0f;
            }

            _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, Time.deltaTime * _fadeSpeed);
            Color c = _vignetteImage.color;
            c.a = _currentAlpha;
            _vignetteImage.color = c;
        }

        public void TriggerVignette(float intensityRatio)
        {
            if (!_isEnabled) return;
            _targetAlpha = Mathf.Clamp01(intensityRatio) * _maxIntensity;
        }

        public void ReleaseVignette()
        {
            _targetAlpha = 0f;
        }
    }
}

