using System;
using UnityEngine;
using UnityEngine.Events;
using ShootingRange.Core;

namespace ShootingRange.UI
{
    /// <summary>
    /// Physical 3D push-button with mechanical depression depth, haptic response,
    /// and audio click. Interacts with VR controller poke or hand collision,
    /// and supports remote ray pointer clicks.
    /// </summary>
    public class VRPokeInteractable : MonoBehaviour, IPokeable
    {
        [Header("Poke Settings")]
        [SerializeField] private string _displayName = "Button";
        [SerializeField] private float _pressDepthMeters = 0.02f;
        [SerializeField] private Transform _buttonCapTransform;
        [SerializeField] private UnityEvent _onPressed;

        public string DisplayName => string.IsNullOrEmpty(_displayName) ? gameObject.name : _displayName;

        private Vector3 _originalCapPos;
        private bool _isPressed;

        public event Action OnButtonPushed;

        public UnityEvent OnPressed => _onPressed;
        public bool IsPressed => _isPressed;

        private void Awake()
        {
            if (_buttonCapTransform == null) _buttonCapTransform = transform;
            _originalCapPos = _buttonCapTransform.localPosition;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!_isPressed)
            {
                Press();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (_isPressed)
            {
                Release();
            }
        }

        public void Press()
        {
            if (_isPressed) return;

            _isPressed = true;
            if (_buttonCapTransform != null)
            {
                _buttonCapTransform.localPosition = _originalCapPos + Vector3.down * _pressDepthMeters;
            }

            PlayClickSound();
            _onPressed?.Invoke();
            OnButtonPushed?.Invoke();
        }

        public void Release()
        {
            if (!_isPressed) return;

            _isPressed = false;
            if (_buttonCapTransform != null)
            {
                _buttonCapTransform.localPosition = _originalCapPos;
            }
        }

        public void SimulateClick()
        {
            Press();
            CancelInvoke(nameof(Release));
            Invoke(nameof(Release), 0.15f);
        }

        private void PlayClickSound()
        {
            var clip = ProceduralAudioSynthesizer.GetSlideRackClip();
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, 0.45f);
            }
        }
    }
}
