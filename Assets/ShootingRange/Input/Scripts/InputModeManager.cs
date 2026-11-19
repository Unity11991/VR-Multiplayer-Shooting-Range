using System;
using UnityEngine;
using UnityEngine.XR;
using ShootingRange.Core;

namespace ShootingRange.Input
{
    public enum ActiveInputMode
    {
        AutoDetect,
        DesktopSimulator,
        OpenXRVR
    }

    /// <summary>
    /// Manages the active input and tracking provider (VR vs Desktop Simulation).
    /// Implements ITrackingProvider, IControllerInputProvider, and IHandPoseProvider
    /// to transparently delegate to the active provider.
    /// </summary>
    public class InputModeManager : MonoBehaviour, ITrackingProvider, IControllerInputProvider, IHandPoseProvider
    {
        [SerializeField] private ActiveInputMode _inputMode = ActiveInputMode.AutoDetect;
        [SerializeField] private DesktopVRSimulator _desktopSimulator;
        [SerializeField] private OpenXRTrackingProvider _openXRProvider;

        public ITrackingProvider ActiveTrackingProvider { get; private set; }
        public IControllerInputProvider ActiveInputProvider { get; private set; }
        public IHandPoseProvider ActiveHandPoseProvider { get; private set; }

        public ActiveInputMode CurrentMode { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            if (_inputMode == ActiveInputMode.AutoDetect)
            {
                bool xrDeviceActive = XRSettings.isDeviceActive;
                var targetMode = xrDeviceActive ? ActiveInputMode.OpenXRVR : ActiveInputMode.DesktopSimulator;
                if (targetMode != CurrentMode)
                {
                    CurrentMode = targetMode;
                    ApplyMode();
                }
            }
        }

        public void EnsureInitialized()
        {
            if (_desktopSimulator == null)
            {
                _desktopSimulator = GetComponentInChildren<DesktopVRSimulator>(true);
                if (_desktopSimulator == null)
                {
                    var simObj = new GameObject("DesktopVRSimulator");
                    simObj.transform.SetParent(transform);
                    _desktopSimulator = simObj.AddComponent<DesktopVRSimulator>();
                }
            }

            if (_openXRProvider == null)
            {
                _openXRProvider = GetComponentInChildren<OpenXRTrackingProvider>(true);
                if (_openXRProvider == null)
                {
                    var xrObj = new GameObject("OpenXRTrackingProvider");
                    xrObj.transform.SetParent(transform);
                    _openXRProvider = xrObj.AddComponent<OpenXRTrackingProvider>();
                }
            }

            DetermineActiveMode();
            ApplyMode();
        }

        public void Initialize(ServiceContainer container)
        {
            EnsureInitialized();

            // Register into ServiceContainer
            if (container != null)
            {
                container.Register<ITrackingProvider>(this);
                container.Register<IControllerInputProvider>(this);
                container.Register<IHandPoseProvider>(this);
            }
        }

        private void DetermineActiveMode()
        {
            if (_inputMode == ActiveInputMode.AutoDetect)
            {
                bool xrDeviceActive = XRSettings.isDeviceActive;
                CurrentMode = xrDeviceActive ? ActiveInputMode.OpenXRVR : ActiveInputMode.DesktopSimulator;
            }
            else
            {
                CurrentMode = _inputMode;
            }
        }

        private void ApplyMode()
        {
            if (CurrentMode == ActiveInputMode.OpenXRVR)
            {
                if (_desktopSimulator != null) _desktopSimulator.gameObject.SetActive(false);
                if (_openXRProvider != null) _openXRProvider.gameObject.SetActive(true);

                ActiveTrackingProvider = _openXRProvider;
                ActiveInputProvider = _openXRProvider;
                ActiveHandPoseProvider = _openXRProvider;
            }
            else
            {
                if (_openXRProvider != null) _openXRProvider.gameObject.SetActive(false);
                if (_desktopSimulator != null) _desktopSimulator.gameObject.SetActive(true);

                ActiveTrackingProvider = _desktopSimulator;
                ActiveInputProvider = _desktopSimulator;
                ActiveHandPoseProvider = _desktopSimulator;
            }
        }

        public void SwitchMode(ActiveInputMode newMode, ServiceContainer container)
        {
            _inputMode = newMode;
            DetermineActiveMode();
            ApplyMode();

            if (container != null)
            {
                container.Register<ITrackingProvider>(this);
                container.Register<IControllerInputProvider>(this);
                container.Register<IHandPoseProvider>(this);
            }
        }

        // --- ITrackingProvider Forwarding ---
        public Vector3 HeadPosition => ActiveTrackingProvider != null ? ActiveTrackingProvider.HeadPosition : transform.position + Vector3.up * 1.75f;
        public Quaternion HeadRotation => ActiveTrackingProvider != null ? ActiveTrackingProvider.HeadRotation : Quaternion.identity;
        public Vector3 HeadVelocity => ActiveTrackingProvider != null ? ActiveTrackingProvider.HeadVelocity : Vector3.zero;

        public Vector3 GetHandPosition(Handedness hand) => ActiveTrackingProvider != null ? ActiveTrackingProvider.GetHandPosition(hand) : transform.position;
        public Quaternion GetHandRotation(Handedness hand) => ActiveTrackingProvider != null ? ActiveTrackingProvider.GetHandRotation(hand) : Quaternion.identity;
        public Vector3 GetHandVelocity(Handedness hand) => ActiveTrackingProvider != null ? ActiveTrackingProvider.GetHandVelocity(hand) : Vector3.zero;
        public Vector3 GetHandAngularVelocity(Handedness hand) => ActiveTrackingProvider != null ? ActiveTrackingProvider.GetHandAngularVelocity(hand) : Vector3.zero;
        public bool IsHandTracked(Handedness hand) => ActiveTrackingProvider != null && ActiveTrackingProvider.IsHandTracked(hand);

        public float PlayerHeightOffset
        {
            get => ActiveTrackingProvider != null ? ActiveTrackingProvider.PlayerHeightOffset : 0f;
            set { if (ActiveTrackingProvider != null) ActiveTrackingProvider.PlayerHeightOffset = value; }
        }

        public bool IsSeatedMode
        {
            get => ActiveTrackingProvider != null && ActiveTrackingProvider.IsSeatedMode;
            set { if (ActiveTrackingProvider != null) ActiveTrackingProvider.IsSeatedMode = value; }
        }

        public bool IsLeftHanded
        {
            get => ActiveTrackingProvider != null && ActiveTrackingProvider.IsLeftHanded;
            set { if (ActiveTrackingProvider != null) ActiveTrackingProvider.IsLeftHanded = value; }
        }

        public void Teleport(Vector3 floorPosition, Quaternion rotation)
        {
            EnsureInitialized();
            if (ActiveTrackingProvider != null)
            {
                ActiveTrackingProvider.Teleport(floorPosition, rotation);
            }
            else
            {
                if (_desktopSimulator != null) _desktopSimulator.Teleport(floorPosition, rotation);
                if (_openXRProvider != null) _openXRProvider.Teleport(floorPosition, rotation);
            }

            transform.position = floorPosition;
            transform.rotation = rotation;
            if (transform.parent != null)
            {
                transform.parent.position = floorPosition;
                transform.parent.rotation = rotation;
            }
        }

        // --- IControllerInputProvider Forwarding ---
        public float GetTrigger(Handedness hand) => ActiveInputProvider != null ? ActiveInputProvider.GetTrigger(hand) : 0f;
        public bool GetTriggerDown(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetTriggerDown(hand);
        public bool GetTriggerUp(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetTriggerUp(hand);

        public float GetGrip(Handedness hand) => ActiveInputProvider != null ? ActiveInputProvider.GetGrip(hand) : 0f;
        public bool GetGripDown(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetGripDown(hand);
        public bool GetGripUp(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetGripUp(hand);

        public bool GetPrimaryButton(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetPrimaryButton(hand);
        public bool GetPrimaryButtonDown(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetPrimaryButtonDown(hand);
        public bool GetSecondaryButton(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetSecondaryButton(hand);
        public bool GetSecondaryButtonDown(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetSecondaryButtonDown(hand);

        public Vector2 GetThumbstick(Handedness hand) => ActiveInputProvider != null ? ActiveInputProvider.GetThumbstick(hand) : Vector2.zero;
        public bool GetThumbstickClick(Handedness hand) => ActiveInputProvider != null && ActiveInputProvider.GetThumbstickClick(hand);

        public void TriggerHaptic(Handedness hand, float amplitude, float durationSeconds)
        {
            ActiveInputProvider?.TriggerHaptic(hand, amplitude, durationSeconds);
        }

        // --- IHandPoseProvider Forwarding ---
        public HandPoseType GetCurrentPose(Handedness hand) => ActiveHandPoseProvider != null ? ActiveHandPoseProvider.GetCurrentPose(hand) : HandPoseType.Open;
        public float GetFingerCurl(Handedness hand, FingerType finger) => ActiveHandPoseProvider != null ? ActiveHandPoseProvider.GetFingerCurl(hand, finger) : 0f;
    }
}
