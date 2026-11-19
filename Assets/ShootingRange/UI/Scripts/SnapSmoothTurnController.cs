using System;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Save;

namespace ShootingRange.UI
{
    public class SnapSmoothTurnController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform _playAreaRoot;
        [SerializeField] private ComfortVignette _vignette;

        private IControllerInputProvider _input;
        private GameSettingsData _settings;
        private bool _canSnap = true;

        public bool UseSnapTurn => _settings != null && _settings.UseSnapTurn;
        public float SnapAngle => _settings != null ? _settings.SnapTurnAngle : 45f;
        public float SmoothSpeed => _settings != null ? _settings.SmoothTurnSpeed : 90f;

        private void Start()
        {
            if (_playAreaRoot == null) _playAreaRoot = transform;
            _settings = SaveManager.Load();
            if (_vignette == null) _vignette = FindFirstObjectByType<ComfortVignette>();
        }

        private void Update()
        {
            if (_input == null && ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _input);
            }

            if (_settings == null) _settings = SaveManager.Load();

            // Read turning thumbstick on dominant hand (Right) or secondary hand
            float horizontal = 0f;
            if (_input != null)
            {
                Vector2 thumbstick = _input.GetThumbstick(Handedness.Right);
                horizontal = thumbstick.x;
            }

            // Keyboard arrow fallback in Desktop Simulator
            if (Mathf.Abs(horizontal) < 0.1f)
            {
                if (UnityEngine.Input.GetKey(KeyCode.LeftArrow)) horizontal -= 1.0f;
                if (UnityEngine.Input.GetKey(KeyCode.RightArrow)) horizontal += 1.0f;
            }

            if (_settings.UseSnapTurn)
            {
                if (Mathf.Abs(horizontal) > 0.55f)
                {
                    if (_canSnap)
                    {
                        float turnAngle = Mathf.Sign(horizontal) * _settings.SnapTurnAngle;
                        _playAreaRoot.Rotate(0f, turnAngle, 0f, Space.World);
                        _canSnap = false;

                        // Comfort vignette trigger on snap turn
                        if (_vignette != null && _settings.ComfortVignetteEnabled)
                        {
                            _vignette.TriggerVignette(1.0f);
                            CancelInvoke(nameof(ReleaseVignetteDelayed));
                            Invoke(nameof(ReleaseVignetteDelayed), 0.12f);
                        }
                    }
                }
                else if (Mathf.Abs(horizontal) < 0.2f)
                {
                    _canSnap = true;
                }
            }
            else
            {
                // Smooth turning with proportional comfort vignette
                if (Mathf.Abs(horizontal) > 0.12f)
                {
                    float turnAngle = horizontal * _settings.SmoothTurnSpeed * Time.deltaTime;
                    _playAreaRoot.Rotate(0f, turnAngle, 0f, Space.World);

                    if (_vignette != null && _settings.ComfortVignetteEnabled)
                    {
                        _vignette.TriggerVignette(Mathf.Abs(horizontal));
                    }
                }
                else
                {
                    if (_vignette != null)
                    {
                        _vignette.ReleaseVignette();
                    }
                }
            }
        }

        private void ReleaseVignetteDelayed()
        {
            if (_vignette != null)
            {
                _vignette.ReleaseVignette();
            }
        }

        public void ToggleTurnMode()
        {
            if (_settings == null) _settings = SaveManager.Load();
            _settings.UseSnapTurn = !_settings.UseSnapTurn;
            SaveManager.Save(_settings);
        }

        public void SetSnapTurn(bool useSnap)
        {
            if (_settings == null) _settings = SaveManager.Load();
            _settings.UseSnapTurn = useSnap;
            SaveManager.Save(_settings);
        }

        public void SetSnapAngle(float angle)
        {
            if (_settings == null) _settings = SaveManager.Load();
            _settings.SnapTurnAngle = Mathf.Clamp(angle, 15f, 90f);
            SaveManager.Save(_settings);
        }

        public void SetSmoothSpeed(float speed)
        {
            if (_settings == null) _settings = SaveManager.Load();
            _settings.SmoothTurnSpeed = Mathf.Clamp(speed, 30f, 180f);
            SaveManager.Save(_settings);
        }

        public void UpdateSettings(GameSettingsData newSettings)
        {
            _settings = newSettings;
        }
    }
}
