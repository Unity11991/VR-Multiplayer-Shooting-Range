using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using ShootingRange.Core;

namespace ShootingRange.Input
{
    /// <summary>
    /// OpenXR & Meta XR runtime tracking provider.
    /// Strictly extracts raw tracking, pose, and button data WITHOUT using XR Interaction Toolkit or Meta Interaction SDK.
    /// </summary>
    public class OpenXRTrackingProvider : MonoBehaviour, ITrackingProvider, IControllerInputProvider, IHandPoseProvider
    {
        [Header("Configuration")]
        [SerializeField] private float _defaultHeadHeight = 1.75f;
        [SerializeField] private float _seatedHeadHeight = 1.25f;

        private InputDevice _leftDevice;
        private InputDevice _rightDevice;
        private InputDevice _headDevice;

        private Vector3 _headPos;
        private Quaternion _headRot = Quaternion.identity;
        private Vector3 _headVel;

        private Vector3 _leftHandPos;
        private Quaternion _leftHandRot = Quaternion.identity;
        private Vector3 _leftHandVel;
        private Vector3 _leftHandAngVel;

        private Vector3 _rightHandPos;
        private Quaternion _rightHandRot = Quaternion.identity;
        private Vector3 _rightHandVel;
        private Vector3 _rightHandAngVel;

        // Button caching for edge detection
        private bool _lastLeftTriggerDown;
        private bool _lastRightTriggerDown;
        private bool _lastLeftGripDown;
        private bool _lastRightGripDown;
        private bool _lastLeftPrimary;
        private bool _lastRightPrimary;
        private bool _lastLeftSecondary;
        private bool _lastRightSecondary;

        public float PlayerHeightOffset { get; set; } = 0.0f;
        public bool IsSeatedMode { get; set; } = false;
        public bool IsLeftHanded { get; set; } = false;

        public Vector3 HeadPosition => _headPos + Vector3.up * PlayerHeightOffset;
        public Quaternion HeadRotation => _headRot;
        public Vector3 HeadVelocity => _headVel;

        private void OnEnable()
        {
            InputDevices.deviceConnected += OnDeviceConnected;
            InputDevices.deviceDisconnected += OnDeviceDisconnected;
            RefreshDevices();
        }

        private void OnDisable()
        {
            InputDevices.deviceConnected -= OnDeviceConnected;
            InputDevices.deviceDisconnected -= OnDeviceDisconnected;
        }

        private void OnDeviceConnected(InputDevice device) => RefreshDevices();
        private void OnDeviceDisconnected(InputDevice device) => RefreshDevices();

        private void RefreshDevices()
        {
            var leftDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Left, leftDevices);
            if (leftDevices.Count > 0) _leftDevice = leftDevices[0];

            var rightDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller | InputDeviceCharacteristics.Right, rightDevices);
            if (rightDevices.Count > 0) _rightDevice = rightDevices[0];

            var headDevices = new List<InputDevice>();
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, headDevices);
            if (headDevices.Count > 0) _headDevice = headDevices[0];
        }

        private void Update()
        {
            if (!_headDevice.isValid || !_leftDevice.isValid || !_rightDevice.isValid)
            {
                RefreshDevices();
            }

            // Head tracking
            if (_headDevice.isValid)
            {
                if (_headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 hp)) _headPos = hp;
                if (_headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion hr)) _headRot = hr;
                if (_headDevice.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 hv)) _headVel = hv;
            }

            // Left Hand tracking
            if (_leftDevice.isValid)
            {
                if (_leftDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 lp)) _leftHandPos = lp;
                if (_leftDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion lr)) _leftHandRot = lr;
                if (_leftDevice.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 lv)) _leftHandVel = lv;
                if (_leftDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 lav)) _leftHandAngVel = lav;
            }

            // Right Hand tracking
            if (_rightDevice.isValid)
            {
                if (_rightDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 rp)) _rightHandPos = rp;
                if (_rightDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rr)) _rightHandRot = rr;
                if (_rightDevice.TryGetFeatureValue(CommonUsages.deviceVelocity, out Vector3 rv)) _rightHandVel = rv;
                if (_rightDevice.TryGetFeatureValue(CommonUsages.deviceAngularVelocity, out Vector3 rav)) _rightHandAngVel = rav;
            }
        }

        private void LateUpdate()
        {
            // Cache edge detection values
            _lastLeftTriggerDown = GetTrigger(Handedness.Left) > 0.5f;
            _lastRightTriggerDown = GetTrigger(Handedness.Right) > 0.5f;
            _lastLeftGripDown = GetGrip(Handedness.Left) > 0.5f;
            _lastRightGripDown = GetGrip(Handedness.Right) > 0.5f;
            _lastLeftPrimary = GetPrimaryButton(Handedness.Left);
            _lastRightPrimary = GetPrimaryButton(Handedness.Right);
            _lastLeftSecondary = GetSecondaryButton(Handedness.Left);
            _lastRightSecondary = GetSecondaryButton(Handedness.Right);
        }

        // ITrackingProvider
        public Vector3 GetHandPosition(Handedness hand) => (hand == Handedness.Right ? _rightHandPos : _leftHandPos) + Vector3.up * PlayerHeightOffset;
        public Quaternion GetHandRotation(Handedness hand) => hand == Handedness.Right ? _rightHandRot : _leftHandRot;
        public Vector3 GetHandVelocity(Handedness hand) => hand == Handedness.Right ? _rightHandVel : _leftHandVel;
        public Vector3 GetHandAngularVelocity(Handedness hand) => hand == Handedness.Right ? _rightHandAngVel : _leftHandAngVel;
        public bool IsHandTracked(Handedness hand) => hand == Handedness.Right ? _rightDevice.isValid : _leftDevice.isValid;

        // IControllerInputProvider
        public float GetTrigger(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.trigger, out float val) ? val : 0.0f;
        }

        public bool GetTriggerDown(Handedness hand)
        {
            bool current = GetTrigger(hand) > 0.5f;
            bool last = hand == Handedness.Right ? _lastRightTriggerDown : _lastLeftTriggerDown;
            return current && !last;
        }

        public bool GetTriggerUp(Handedness hand)
        {
            bool current = GetTrigger(hand) > 0.5f;
            bool last = hand == Handedness.Right ? _lastRightTriggerDown : _lastLeftTriggerDown;
            return !current && last;
        }

        public float GetGrip(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.grip, out float val) ? val : 0.0f;
        }

        public bool GetGripDown(Handedness hand)
        {
            bool current = GetGrip(hand) > 0.5f;
            bool last = hand == Handedness.Right ? _lastRightGripDown : _lastLeftGripDown;
            return current && !last;
        }

        public bool GetGripUp(Handedness hand)
        {
            bool current = GetGrip(hand) > 0.5f;
            bool last = hand == Handedness.Right ? _lastRightGripDown : _lastLeftGripDown;
            return !current && last;
        }

        public bool GetPrimaryButton(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.primaryButton, out bool val) && val;
        }

        public bool GetPrimaryButtonDown(Handedness hand)
        {
            bool current = GetPrimaryButton(hand);
            bool last = hand == Handedness.Right ? _lastRightPrimary : _lastLeftPrimary;
            return current && !last;
        }

        public bool GetSecondaryButton(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.secondaryButton, out bool val) && val;
        }

        public bool GetSecondaryButtonDown(Handedness hand)
        {
            bool current = GetSecondaryButton(hand);
            bool last = hand == Handedness.Right ? _lastRightSecondary : _lastLeftSecondary;
            return current && !last;
        }

        public Vector2 GetThumbstick(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 val) ? val : Vector2.zero;
        }

        public bool GetThumbstickClick(Handedness hand)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            return dev.isValid && dev.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out bool val) && val;
        }

        public void TriggerHaptic(Handedness hand, float amplitude, float durationSeconds)
        {
            var dev = hand == Handedness.Right ? _rightDevice : _leftDevice;
            if (dev.isValid && dev.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            {
                dev.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Clamp(durationSeconds, 0.01f, 2.0f));
            }
        }

        // IHandPoseProvider
        public HandPoseType GetCurrentPose(Handedness hand)
        {
            float grip = GetGrip(hand);
            float trigger = GetTrigger(hand);
            if (grip > 0.5f && trigger > 0.5f) return HandPoseType.Fist;
            if (grip > 0.5f) return HandPoseType.GripPistol;
            if (trigger > 0.5f) return HandPoseType.Pinch;
            return HandPoseType.Open;
        }

        public float GetFingerCurl(Handedness hand, FingerType finger)
        {
            switch (finger)
            {
                case FingerType.Index: return GetTrigger(hand);
                case FingerType.Middle:
                case FingerType.Ring:
                case FingerType.Pinky: return GetGrip(hand);
                case FingerType.Thumb: return GetPrimaryButton(hand) || GetSecondaryButton(hand) ? 0.8f : 0.1f;
                default: return 0.0f;
            }
        }
    }
}

