using System;
using UnityEngine;

namespace ShootingRange.Core
{
    /// <summary>
    /// Composition Root for the Shooting Range application.
    /// Wire up all dependencies and providers explicitly with NO global singletons.
    /// </summary>
    public class ShootingRangeCompositionRoot : MonoBehaviour
    {
        public static ServiceContainer Container { get; private set; }

        [Header("Rig References")]
        [SerializeField] private Transform _playerRigTransform;
        [SerializeField] private Transform _headTransform;
        [SerializeField] private Transform _leftHandTransform;
        [SerializeField] private Transform _rightHandTransform;

        [Header("Subsystems")]
        [SerializeField] private MonoBehaviour _trackingAndInputSource;
        [SerializeField] private MonoBehaviour _ballisticsSource;
        [SerializeField] private MonoBehaviour _targetSource;
        [SerializeField] private MonoBehaviour _networkSource;
        [SerializeField] private MonoBehaviour _replaySource;

        private ITrackingProvider _trackingProvider;
        private IControllerInputProvider _inputProvider;

        public ITrackingProvider TrackingProvider => _trackingProvider;
        public IControllerInputProvider InputProvider => _inputProvider;

        private void Awake()
        {
            InitializeContainer();
        }

        public void InitializeContainer()
        {
            Container = new ServiceContainer();

            // Register composition root itself
            Container.Register<ShootingRangeCompositionRoot>(this);

            // Auto-resolve rig transforms if not bound
            if (_playerRigTransform == null) _playerRigTransform = transform;
            if (_headTransform == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null) _headTransform = cam.transform;
            }
            if (_leftHandTransform == null)
            {
                var t = transform.Find("LeftHand");
                if (t != null) _leftHandTransform = t;
            }
            if (_rightHandTransform == null)
            {
                var t = transform.Find("RightHand");
                if (t != null) _rightHandTransform = t;
            }

            // Auto-resolve tracking source if not bound
            if (_trackingAndInputSource == null)
            {
                var monoBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var mb in monoBehaviours)
                {
                    if (mb is ITrackingProvider)
                    {
                        _trackingAndInputSource = mb;
                        break;
                    }
                }
            }

            // Resolve and register Tracking & Input
            if (_trackingAndInputSource != null)
            {
                if (_trackingAndInputSource is ITrackingProvider tp)
                {
                    _trackingProvider = tp;
                    Container.Register<ITrackingProvider>(tp);
                }
                if (_trackingAndInputSource is IControllerInputProvider ip)
                {
                    _inputProvider = ip;
                    Container.Register<IControllerInputProvider>(ip);
                }
                if (_trackingAndInputSource is IHandPoseProvider hp)
                {
                    Container.Register<IHandPoseProvider>(hp);
                }
            }

            // Register Ballistics
            if (_ballisticsSource is IBallisticsSimulation bs)
            {
                Container.Register<IBallisticsSimulation>(bs);
            }
            else
            {
                var foundBs = FindFirstObjectByType<MonoBehaviour>() as IBallisticsSimulation;
                if (foundBs != null) Container.Register<IBallisticsSimulation>(foundBs);
            }

            // Register Targets
            if (_targetSource is ITargetSimulation ts)
            {
                Container.Register<ITargetSimulation>(ts);
            }

            // Register Network
            if (_networkSource is INetworkSession ns)
            {
                Container.Register<INetworkSession>(ns);
            }

            // Register Replay
            if (_replaySource is IReplayRecorder rr)
            {
                Container.Register<IReplayRecorder>(rr);
            }
            else
            {
                var foundRr = FindFirstObjectByType<MonoBehaviour>() as IReplayRecorder;
                if (foundRr != null) Container.Register<IReplayRecorder>(foundRr);
            }
        }

        private void Update()
        {
            if (_trackingProvider == null && Container != null)
            {
                Container.TryResolve(out _trackingProvider);
                Container.TryResolve(out _inputProvider);
            }

            UpdateRigTransforms();
        }

        private void LateUpdate()
        {
            UpdateRigTransforms();
        }

        private void UpdateRigTransforms()
        {
            if (_trackingProvider == null) return;

            if (_headTransform != null)
            {
                _headTransform.position = _trackingProvider.HeadPosition;
                _headTransform.rotation = _trackingProvider.HeadRotation;
            }

            if (_leftHandTransform != null)
            {
                _leftHandTransform.position = _trackingProvider.GetHandPosition(Handedness.Left);
                _leftHandTransform.rotation = _trackingProvider.GetHandRotation(Handedness.Left);
            }

            if (_rightHandTransform != null)
            {
                _rightHandTransform.position = _trackingProvider.GetHandPosition(Handedness.Right);
                _rightHandTransform.rotation = _trackingProvider.GetHandRotation(Handedness.Right);
            }
        }
    }
}
