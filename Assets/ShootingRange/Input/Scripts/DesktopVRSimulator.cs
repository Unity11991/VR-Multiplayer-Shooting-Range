using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Input
{
    /// <summary>
    /// Desktop VR Simulator allowing full gameplay testing without a physical VR headset.
    /// Exposes identical ITrackingProvider, IControllerInputProvider, and IHandPoseProvider interfaces.
    /// Decoupled from interaction implementation — communicates strictly via Core interfaces.
    /// </summary>
    public class DesktopVRSimulator : MonoBehaviour, ITrackingProvider, IControllerInputProvider, IHandPoseProvider
    {
        [Header("Movement & Look Settings")]
        [SerializeField] private float _moveSpeed = 4.0f;
        [SerializeField] private float _mouseSensitivity = 2.0f;
        [SerializeField] private float _defaultHeadHeight = 1.75f;
        [SerializeField] private float _seatedHeadHeight = 1.25f;

        [Header("Simulated Hand Offsets (Relative to Head)")]
        [SerializeField] private Vector3 _rightHandRestOffset = new Vector3(0.20f, -0.28f, 0.45f);
        [SerializeField] private Vector3 _leftHandRestOffset = new Vector3(-0.20f, -0.28f, 0.45f);
        [SerializeField] private Vector3 _aimingRightHandOffset = new Vector3(0.0f, -0.12f, 0.40f);
        [SerializeField] private Vector3 _twoHandedForegripOffset = new Vector3(0.0f, -0.16f, 0.70f);

        [Header("Velocity Estimation")]
        [SerializeField] private int _velocitySampleCount = 6;

        [Header("HUD")]
        [SerializeField] private bool _showDesktopHUD = true;

        // Tracking state
        private Vector3 _headPosition;
        private Quaternion _headRotation = Quaternion.identity;
        private Vector3 _headVelocity;
        private Vector3 _lastHeadPos;

        private Vector3 _rightHandPosition;
        private Quaternion _rightHandRotation = Quaternion.identity;
        private Vector3 _leftHandPosition;
        private Quaternion _leftHandRotation = Quaternion.identity;

        private readonly Queue<Vector3> _rightHandPosHistory = new Queue<Vector3>();
        private readonly Queue<float> _timeHistory = new Queue<float>();
        private readonly Queue<Vector3> _leftHandPosHistory = new Queue<Vector3>();

        private Vector3 _rightHandVelocity;
        private Vector3 _rightHandAngularVelocity;
        private Vector3 _leftHandVelocity;
        private Vector3 _leftHandAngularVelocity;
        private Quaternion _lastRightHandRot = Quaternion.identity;
        private Quaternion _lastLeftHandRot = Quaternion.identity;

        // Input state
        private float _mousePitch;
        private float _mouseYaw;
        private bool _isAiming;
        private bool _adsToggled;
        private bool _twoHandedGripActive;
        private bool _manualTwoHandLocked;
        private float _lastFireTime = -999f;
        private Vector2 _cameraRecoilTarget;
        private Vector2 _cameraRecoilCurrent;
        private IWeaponInteraction _subscribedWeapon;

        // Tactical Reload handling
        private bool _isReloading;
        private float _reloadTimer;
        private float _reloadDuration = 0.88f;

        // Procedural movement and weapon bobbing/sway
        private bool _isMoving;
        private bool _isRunning;
        private float _bobTimer;
        private float _idleTimer;
        private float _currentBobIntensity;
        private Vector2 _mouseLookSwayTarget;
        private Vector2 _mouseLookSwayCurrent;

        // Smooth positional and angular damping for ADS and two-handed transitions
        private Vector3 _smoothedRightLocalPos;
        private Quaternion _smoothedRightLocalRot = Quaternion.identity;
        private Vector3 _smoothedLeftLocalPos;
        private Quaternion _smoothedLeftLocalRot = Quaternion.identity;
        private bool _smoothedPosesInitialized;

        private bool _rightGripHeld;
        private bool _rightGripDown;
        private bool _rightGripUp;

        private bool _leftGripHeld;
        private bool _leftGripDown;
        private bool _leftGripUp;

        private bool _rightTriggerHeld;
        private bool _rightTriggerDown;
        private bool _rightTriggerUp;
        private bool _leftTriggerHeld;
        private bool _leftTriggerDown;
        private bool _leftTriggerUp;

        private bool _primaryButtonHeld;
        private bool _primaryButtonDown;
        private bool _secondaryButtonHeld;
        private bool _secondaryButtonDown;

        private Vector2 _simulatedThumbstick = Vector2.zero;
        private HandPoseType _rightHandPose = HandPoseType.Open;
        private HandPoseType _leftHandPose = HandPoseType.Open;

        // Hover detection using Core IGrabbable and IPokeable
        private IGrabbable _hoveredGrabbable;
        private IPokeable _hoveredPokeable;
        private UnityEngine.UI.Button _hoveredUIButton;
        private UnityEngine.UI.Slider _hoveredUISlider;
        private string _hoveredName = "";

        // Replay Recorder reference
        private IReplayRecorder _replayRecorder;

        // Hand Interactors resolved via Core interface
        private IHandInteractor _rightHandInteractor;
        private IHandInteractor _leftHandInteractor;

        // Config properties
        public float PlayerHeightOffset { get; set; } = 0.0f;
        public bool IsSeatedMode { get; set; } = false;

        private bool _isLeftHanded = false;
        public bool IsLeftHanded
        {
            get => _isLeftHanded;
            set
            {
                if (_isLeftHanded != value)
                {
                    _isLeftHanded = value;
                    OnHandednessChanged();
                }
            }
        }

        public IHandInteractor DominantInteractor => _isLeftHanded ? _leftHandInteractor : _rightHandInteractor;
        public IHandInteractor OffHandInteractor => _isLeftHanded ? _rightHandInteractor : _leftHandInteractor;
        public Handedness DominantHand => _isLeftHanded ? Handedness.Left : Handedness.Right;
        public Handedness OffHand => _isLeftHanded ? Handedness.Right : Handedness.Left;

        public Vector3 HeadPosition => _headPosition;
        public Quaternion HeadRotation => _headRotation;
        public Vector3 HeadVelocity => _headVelocity;

        public bool IsAiming => _isAiming;
        public bool IsRightGripHeld => _rightGripHeld;
        public bool IsLeftGripHeld => _leftGripHeld;

        private void Awake()
        {
            Vector3 basePos = transform.parent != null ? transform.parent.position : transform.position;
            _headPosition = basePos + Vector3.up * (IsSeatedMode ? _seatedHeadHeight : _defaultHeadHeight);
            _lastHeadPos = _headPosition;
            _mouseYaw = transform.eulerAngles.y;
        }

        private void Start()
        {
            LockCursor(true);
            FindInteractors();

            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.nearClipPlane = Mathf.Min(mainCam.nearClipPlane, 0.02f);
            }
        }

        private void FindInteractors()
        {
            var interactors = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in interactors)
            {
                if (mb is IHandInteractor hi)
                {
                    if (hi.Hand == Handedness.Right) _rightHandInteractor = hi;
                    else if (hi.Hand == Handedness.Left) _leftHandInteractor = hi;
                }
            }
            EnsureReplayRecorder();
        }

        private void OnHandednessChanged()
        {
            var newDominant = DominantInteractor;
            var prevDominant = OffHandInteractor;

            if (prevDominant != null && prevDominant.HeldObject != null && newDominant != null && newDominant.HeldObject == null)
            {
                var obj = prevDominant.HeldObject;
                var weapon = (obj as IWeaponInteraction) ?? obj.Transform.GetComponent<IWeaponInteraction>();
                if (weapon != null && weapon.IsSecondaryGrabbed)
                {
                    weapon.OnSecondaryReleased(DominantHand);
                }

                prevDominant.TryRelease();
                newDominant.GrabObject(obj);
            }

            var ns = GetNetworkSession();
            if (ns != null && ns.IsActive)
            {
                ns.BroadcastHandSwitched(ns.LocalClientId, _isLeftHanded);
            }
        }

        private INetworkSession _networkSession;
        private INetworkSession GetNetworkSession()
        {
            if (_networkSession != null) return _networkSession;
            if (ShootingRangeCompositionRoot.Container != null && ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession))
            {
                return _networkSession;
            }
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in behaviours)
            {
                if (mb is INetworkSession ns)
                {
                    _networkSession = ns;
                    return _networkSession;
                }
            }
            return null;
        }

        private void EnsureReplayRecorder()
        {
            if (_replayRecorder != null) return;
            if (ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _replayRecorder);
            }
            if (_replayRecorder == null)
            {
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var mb in behaviours)
                {
                    if (mb is IReplayRecorder rr)
                    {
                        _replayRecorder = rr;
                        break;
                    }
                }
            }
        }

        private ITargetSimulation _targetSim;
        private ITargetSimulation GetTargetSimulation()
        {
            if (_targetSim != null) return _targetSim;
            if (ShootingRangeCompositionRoot.Container != null && ShootingRangeCompositionRoot.Container.TryResolve(out _targetSim))
            {
                return _targetSim;
            }
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in behaviours)
            {
                if (mb is ITargetSimulation ts)
                {
                    _targetSim = ts;
                    break;
                }
            }
            return _targetSim;
        }

        public void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void TriggerReload(IWeaponInteraction weapon, IGrabbable targetMag = null)
        {
            if (weapon == null || _isReloading) return;
            _isReloading = true;
            _reloadTimer = 0f;

            if (targetMag != null)
            {
                weapon.TryInsertMagazine(targetMag);
            }
            else
            {
                weapon.Reload();
            }
        }

        private void Update()
        {
            if (_isReloading)
            {
                _reloadTimer += Time.deltaTime;
                if (_reloadTimer >= _reloadDuration)
                {
                    _isReloading = false;
                }
            }

            HandleDesktopInput();
            UpdateHeadPose();
            UpdateHandPoses();
            CalculateVelocities();
            UpdateHoverDetection();
        }

        private void UpdateHoverDetection()
        {
            _hoveredGrabbable = null;
            _hoveredPokeable = null;
            _hoveredUIButton = null;
            _hoveredUISlider = null;
            _hoveredName = "";

            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector3 rayOrigin = _headPosition;
            Vector3 rayDir = _headRotation * Vector3.forward;

            // 1. Direct crosshair raycast (instant pinpoint precision)
            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit directHit, 5.0f, ~0, QueryTriggerInteraction.Collide))
            {
                var poke = directHit.collider.GetComponentInParent<IPokeable>();
                if (poke != null)
                {
                    _hoveredPokeable = poke;
                    _hoveredName = poke.DisplayName;
                    return;
                }

                var btn = directHit.collider.GetComponentInParent<UnityEngine.UI.Button>();
                if (btn != null && btn.interactable)
                {
                    _hoveredUIButton = btn;
                    var txt = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                    _hoveredName = txt != null && !string.IsNullOrEmpty(txt.text) ? txt.text.Replace("\n", " ") : btn.gameObject.name;
                    return;
                }

                var slider = directHit.collider.GetComponentInParent<UnityEngine.UI.Slider>();
                if (slider != null && slider.interactable)
                {
                    _hoveredUISlider = slider;
                    _hoveredName = slider.gameObject.name;
                    return;
                }

                var grabbable = directHit.collider.GetComponentInParent<IGrabbable>();
                if (grabbable != null && !grabbable.IsGrabbed)
                {
                    _hoveredGrabbable = grabbable;
                    _hoveredName = grabbable.Transform.gameObject.name.Replace("(Clone)", "").Trim();
                    return;
                }
            }

            // 2. Multi-hit SphereCast to bypass non-grabbable geometry (tabletops, bench mats, walls)
            var hits = Physics.SphereCastAll(rayOrigin, 0.22f, rayDir, 5.0f, ~0, QueryTriggerInteraction.Collide);
            if (hits.Length > 0)
            {
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                {
                    var poke = hit.collider.GetComponentInParent<IPokeable>();
                    if (poke != null)
                    {
                        _hoveredPokeable = poke;
                        _hoveredName = poke.DisplayName;
                        return;
                    }

                    var btn = hit.collider.GetComponentInParent<UnityEngine.UI.Button>();
                    if (btn != null && btn.interactable)
                    {
                        _hoveredUIButton = btn;
                        var txt = btn.GetComponentInChildren<UnityEngine.UI.Text>();
                        _hoveredName = txt != null && !string.IsNullOrEmpty(txt.text) ? txt.text.Replace("\n", " ") : btn.gameObject.name;
                        return;
                    }

                    var slider = hit.collider.GetComponentInParent<UnityEngine.UI.Slider>();
                    if (slider != null && slider.interactable)
                    {
                        _hoveredUISlider = slider;
                        _hoveredName = slider.gameObject.name;
                        return;
                    }

                    var grabbable = hit.collider.GetComponentInParent<IGrabbable>();
                    if (grabbable != null && !grabbable.IsGrabbed)
                    {
                        _hoveredGrabbable = grabbable;
                        _hoveredName = grabbable.Transform.gameObject.name.Replace("(Clone)", "").Trim();
                        return;
                    }
                }
            }
        }

        private void HandleDesktopInput()
        {
            // Cursor lock toggle via Escape / Click
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                LockCursor(false);
            }
            else if (UnityEngine.Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                // Only lock cursor when clicking in 3D world, never when clicking on UI buttons
                if (!UIInteractionState.IsPointerOverUI && !UIInteractionState.IsAnyUIPanelOpen)
                {
                    LockCursor(true);
                }
            }

            // Mouse look (only when cursor is locked)
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                float rawMouseX = UnityEngine.Input.GetAxis("Mouse X");
                float rawMouseY = UnityEngine.Input.GetAxis("Mouse Y");
                float mouseX = rawMouseX * _mouseSensitivity;
                float mouseY = rawMouseY * _mouseSensitivity;

                _mouseYaw += mouseX;
                _mousePitch = Mathf.Clamp(_mousePitch - mouseY, -85f, 85f);

                _mouseLookSwayTarget.x = Mathf.Clamp(-rawMouseX * 1.5f, -3.0f, 3.0f);
                _mouseLookSwayTarget.y = Mathf.Clamp(rawMouseY * 1.2f, -2.5f, 2.5f);
            }
            else
            {
                _mouseLookSwayTarget = Vector2.zero;
            }

            _mouseLookSwayCurrent = Vector2.Lerp(_mouseLookSwayCurrent, _mouseLookSwayTarget, Time.deltaTime * 10f);
            _mouseLookSwayTarget = Vector2.MoveTowards(_mouseLookSwayTarget, Vector2.zero, Time.deltaTime * 12f);

            // Camera recoil recovery (snappy return to center when not firing)
            _cameraRecoilTarget = Vector2.MoveTowards(_cameraRecoilTarget, Vector2.zero, Time.deltaTime * 18f);
            _cameraRecoilCurrent = Vector2.Lerp(_cameraRecoilCurrent, _cameraRecoilTarget, Time.deltaTime * 26f);

            float currentPitch = Mathf.Clamp(_mousePitch + _cameraRecoilCurrent.x, -88f, 88f);
            float currentYaw = _mouseYaw + _cameraRecoilCurrent.y;
            _headRotation = Quaternion.Euler(currentPitch, currentYaw, 0f);

            // WASD Movement
            float horizontal = UnityEngine.Input.GetAxisRaw("Horizontal");
            float vertical = UnityEngine.Input.GetAxisRaw("Vertical");

            Vector3 moveForward = Vector3.ProjectOnPlane(_headRotation * Vector3.forward, Vector3.up).normalized;
            Vector3 moveRight = Vector3.ProjectOnPlane(_headRotation * Vector3.right, Vector3.up).normalized;
            Vector3 moveDir = (moveForward * vertical + moveRight * horizontal).normalized;

            _isMoving = moveDir.sqrMagnitude > 0.01f;
            _isRunning = _isMoving && (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift));

            float currentMoveSpeed = _isRunning ? (_moveSpeed * 1.45f) : _moveSpeed;

            Vector3 baseFloorPos = transform.parent != null ? transform.parent.position : transform.position;
            float currentHeadBaseHeight = (IsSeatedMode ? _seatedHeadHeight : _defaultHeadHeight) + PlayerHeightOffset;
            _headPosition += moveDir * (currentMoveSpeed * Time.deltaTime);
            _headPosition.y = baseFloorPos.y + currentHeadBaseHeight;

            // Sync parent rig position horizontally with player head
            if (transform.parent != null)
            {
                transform.parent.position = new Vector3(_headPosition.x, transform.parent.position.y, _headPosition.z);
            }

            // Height calibration controls ([ and ])
            if (UnityEngine.Input.GetKeyDown(KeyCode.LeftBracket)) PlayerHeightOffset -= 0.05f;
            if (UnityEngine.Input.GetKeyDown(KeyCode.RightBracket)) PlayerHeightOffset += 0.05f;

            // Seated mode toggle (C)
            if (UnityEngine.Input.GetKeyDown(KeyCode.C)) IsSeatedMode = !IsSeatedMode;

            // Left-handed toggle (L)
            if (UnityEngine.Input.GetKeyDown(KeyCode.L)) IsLeftHanded = !IsLeftHanded;

            // Replay Hotkeys: P = Toggle 30s Ghost Playback, O = Toggle Recording
            if (UnityEngine.Input.GetKeyDown(KeyCode.P))
            {
                EnsureReplayRecorder();
                if (_replayRecorder != null)
                {
                    if (_replayRecorder.IsPlayingBack) _replayRecorder.StopPlayback();
                    else _replayRecorder.StartPlayback();
                }
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.O))
            {
                EnsureReplayRecorder();
                if (_replayRecorder != null)
                {
                    if (_replayRecorder.IsRecording) _replayRecorder.StopRecording();
                    else _replayRecorder.StartRecording();
                }
            }

            // Target Count Controls (- and = keys to decrease/increase target count)
            if (UnityEngine.Input.GetKeyDown(KeyCode.Minus) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadMinus))
            {
                GetTargetSimulation()?.DecreaseTargets(4);
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Equals) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadPlus))
            {
                GetTargetSimulation()?.IncreaseTargets(4);
            }

            bool isCursorFree = Cursor.lockState != CursorLockMode.Locked;

            // ADS Toggle via I key, or hold via RMB / I key
            if (UnityEngine.Input.GetKeyDown(KeyCode.I))
            {
                _adsToggled = !_adsToggled;
            }
            if (UnityEngine.Input.GetMouseButtonDown(1))
            {
                _adsToggled = true;
            }
            if (UnityEngine.Input.GetMouseButtonUp(1))
            {
                _adsToggled = false;
            }

            _isAiming = !isCursorFree && (_adsToggled || UnityEngine.Input.GetMouseButton(1) || UnityEngine.Input.GetKey(KeyCode.I));

            if (_rightHandInteractor == null || _leftHandInteractor == null)
            {
                FindInteractors();
            }

            var dominantInteractor = DominantInteractor;
            var offHandInteractor = OffHandInteractor;

            var heldObj = dominantInteractor?.HeldObject ?? offHandInteractor?.HeldObject;
            var heldWeapon = (heldObj as IWeaponInteraction) ?? heldObj?.Transform.GetComponent<IWeaponInteraction>() ?? heldObj?.Transform.GetComponentInParent<IWeaponInteraction>();
            var heldMag = (heldObj as IMagazine) ?? heldObj?.Transform.GetComponent<IMagazine>() ?? heldObj?.Transform.GetComponentInParent<IMagazine>();
            bool isHolding = heldObj != null;

            // Manage dynamic recoil event subscription with currently held weapon
            if (heldWeapon != _subscribedWeapon)
            {
                if (_subscribedWeapon != null)
                {
                    _subscribedWeapon.OnRecoilFired -= HandleWeaponRecoil;
                }
                _subscribedWeapon = heldWeapon;
                if (_subscribedWeapon != null)
                {
                    _subscribedWeapon.OnRecoilFired += HandleWeaponRecoil;
                }
            }

            var hoveredWeapon = (_hoveredGrabbable as IWeaponInteraction) ?? _hoveredGrabbable?.Transform.GetComponent<IWeaponInteraction>() ?? _hoveredGrabbable?.Transform.GetComponentInParent<IWeaponInteraction>();
            var hoveredMag = (_hoveredGrabbable as IMagazine) ?? _hoveredGrabbable?.Transform.GetComponent<IMagazine>() ?? _hoveredGrabbable?.Transform.GetComponentInParent<IMagazine>();
            bool isLookingAtMagazine = hoveredMag != null || (_hoveredGrabbable != null && _hoveredName.ToLower().Contains("mag"));
            bool isLookingAtWeapon = hoveredWeapon != null;

            bool eKeyPressed = UnityEngine.Input.GetKeyDown(KeyCode.E);
            bool gKeyPressed = UnityEngine.Input.GetKeyDown(KeyCode.G);
            bool rKeyPressed = UnityEngine.Input.GetKeyDown(KeyCode.R);
            bool fKeyPressed = UnityEngine.Input.GetKeyDown(KeyCode.F);
            bool lmbDown = !isCursorFree && UnityEngine.Input.GetMouseButtonDown(0);
            bool lmbUp = UnityEngine.Input.GetMouseButtonUp(0);
            bool lmbHeld = !isCursorFree && UnityEngine.Input.GetMouseButton(0);

            // CASE 0: Looking at a Physical 3D Poke Button, UI Button, or Slider -> Interact!
            if (!isHolding && _hoveredPokeable != null && (lmbDown || eKeyPressed))
            {
                _hoveredPokeable.SimulateClick();
            }
            else if (!isHolding && _hoveredUIButton != null && (lmbDown || eKeyPressed))
            {
                _hoveredUIButton.onClick.Invoke();
            }
            else if (!isHolding && _hoveredUISlider != null && lmbHeld)
            {
                Ray sRay = new Ray(_headPosition, _headRotation * Vector3.forward);
                if (Physics.Raycast(sRay, out RaycastHit sHit, 5.0f))
                {
                    RectTransform sRect = _hoveredUISlider.GetComponent<RectTransform>();
                    Vector3 localHit = sRect.InverseTransformPoint(sHit.point);
                    float w = sRect.rect.width;
                    if (w > 0.001f)
                    {
                        float norm = Mathf.Clamp01((localHit.x + w * 0.5f) / w);
                        _hoveredUISlider.value = Mathf.Lerp(_hoveredUISlider.minValue, _hoveredUISlider.maxValue, norm);
                    }
                }
            }
            // CASE 1: Holding a Magazine and looking at a Weapon -> Press E, R, or Left Click to load & equip!
            else if (heldMag != null && isLookingAtWeapon && (eKeyPressed || rKeyPressed || lmbDown))
            {
                dominantInteractor?.TransferOut();
                hoveredWeapon.TryInsertMagazine(heldObj);
                dominantInteractor?.GrabObject(hoveredWeapon);
                _rightTriggerDown = false;
                _rightTriggerHeld = false;
                _primaryButtonDown = false;
                _primaryButtonHeld = false;
            }
            // CASE 2: Holding a Weapon and looking at a Magazine -> Press E or R to load that magazine!
            else if (heldWeapon != null && isLookingAtMagazine && (eKeyPressed || rKeyPressed))
            {
                TriggerReload(heldWeapon, _hoveredGrabbable);
                _rightTriggerDown = false;
                _rightTriggerHeld = false;
                _primaryButtonDown = false;
                _primaryButtonHeld = false;
            }
            // CASE 3: Holding a Weapon and looking at another Weapon -> Press E to swap weapons!
            else if (heldWeapon != null && isLookingAtWeapon && eKeyPressed)
            {
                dominantInteractor?.TryRelease();
                dominantInteractor?.GrabObject(hoveredWeapon);
                _rightTriggerDown = false;
                _rightTriggerHeld = false;
            }
            // CASE 4: Holding a Weapon and pressing R (or E when empty) -> Quick tactical reload!
            else if (heldWeapon != null && (rKeyPressed || (heldWeapon.CurrentAmmo <= 0 && eKeyPressed)))
            {
                if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
                {
                    // Shift + R: Explicitly eject magazine
                    heldWeapon.EjectMagazine();
                }
                else
                {
                    // R or E: Tactical reload with full human inserting animation
                    TriggerReload(heldWeapon);
                }
                _primaryButtonDown = false;
                _primaryButtonHeld = false;
            }
            // CASE 5: Holding a Weapon and pressing F -> Rack Slide (Manual Chamber / Jam Clear)
            else if (heldWeapon != null && fKeyPressed)
            {
                heldWeapon.RackSlide();
            }
            // CASE 6: Empty-handed and looking at any item (Gun / Magazine) -> Press E or Left Click to pick it up!
            else if (!isHolding && _hoveredGrabbable != null && (lmbDown || eKeyPressed))
            {
                if (dominantInteractor != null)
                {
                    // Pick up in natural single-hand hold
                    dominantInteractor.GrabObject(_hoveredGrabbable);
                    _twoHandedGripActive = false;
                    _manualTwoHandLocked = false;
                    _rightTriggerDown = false;
                    _rightTriggerHeld = false;
                }
            }
            // CASE 7: Holding an item and pressing G -> Drop item
            else if (isHolding && gKeyPressed)
            {
                _twoHandedGripActive = false;
                _manualTwoHandLocked = false;
                if (heldWeapon != null && heldWeapon.IsSecondaryGrabbed)
                {
                    heldWeapon.OnSecondaryReleased(OffHand);
                }
                if (dominantInteractor != null && dominantInteractor.IsHoldingObject)
                {
                    dominantInteractor.TryRelease();
                }
                else if (offHandInteractor != null && offHandInteractor.IsHoldingObject)
                {
                    offHandInteractor.TryRelease();
                }
            }
            // CASE 8: Holding a weapon -> Left click triggers weapon firing!
            else if (heldWeapon != null)
            {
                _rightGripDown = false;
                _rightGripUp = false;
                _rightTriggerDown = lmbDown;
                _rightTriggerUp = lmbUp;
                _rightTriggerHeld = lmbHeld;
                if (lmbDown || lmbHeld)
                {
                    _lastFireTime = Time.time;
                }
            }
            else
            {
                // When empty handed, Left Click acts as trigger down (for ray pointers, buttons, etc.)
                _rightGripDown = false;
                _rightGripUp = false;
                _rightTriggerDown = lmbDown;
                _rightTriggerUp = lmbUp;
                _rightTriggerHeld = lmbHeld;
            }

            // Foregrip Grab Toggle (T or Q key manual toggle override)
            bool leftGrabPressed = UnityEngine.Input.GetKeyDown(KeyCode.T) || UnityEngine.Input.GetKeyDown(KeyCode.Q);
            if (leftGrabPressed)
            {
                _manualTwoHandLocked = !_manualTwoHandLocked;
            }

            // Dynamic two-handed stance:
            // Single-hand hold while resting at idle; double-hand mode while moving, firing, or aiming!
            bool isFiring = (Time.time - _lastFireTime) < 0.35f || (heldWeapon != null && lmbHeld);
            bool shouldTwoHand = (_isAiming || isFiring || _isMoving || _manualTwoHandLocked) && heldWeapon != null && heldWeapon.IsTwoHanded;

            if (shouldTwoHand != _twoHandedGripActive)
            {
                _twoHandedGripActive = shouldTwoHand;
                if (_twoHandedGripActive)
                {
                    if (heldWeapon != null && !heldWeapon.IsSecondaryGrabbed && offHandInteractor != null)
                    {
                        heldWeapon.OnSecondaryGrabbed(OffHand, offHandInteractor.AttachTransform);
                    }
                }
                else
                {
                    if (heldWeapon != null && heldWeapon.IsSecondaryGrabbed)
                    {
                        heldWeapon.OnSecondaryReleased(OffHand);
                    }
                }
            }

            _leftGripDown = false;
            _leftGripUp = false;
            _rightGripDown = false;
            _rightGripUp = false;

            // Keep grip states synchronized across dominant and off-hand
            if (!IsLeftHanded)
            {
                _rightGripHeld = _rightHandInteractor != null && _rightHandInteractor.IsHoldingObject;
                _leftGripHeld = _twoHandedGripActive || (_leftHandInteractor != null && _leftHandInteractor.IsHoldingObject);
            }
            else
            {
                _leftGripHeld = _leftHandInteractor != null && _leftHandInteractor.IsHoldingObject;
                _rightGripHeld = _twoHandedGripActive || (_rightHandInteractor != null && _rightHandInteractor.IsHoldingObject);
            }

            // Secondary (F = Charging handle / slide)
            _secondaryButtonDown = fKeyPressed;
            _secondaryButtonHeld = UnityEngine.Input.GetKey(KeyCode.F);

            // Virtual thumbstick: Zero out so WASD doesn't conflict with SnapTurn
            _simulatedThumbstick = Vector2.zero;

            // Cycle hand pose (V)
            if (UnityEngine.Input.GetKeyDown(KeyCode.V))
            {
                _rightHandPose = (HandPoseType)(((int)_rightHandPose + 1) % Enum.GetValues(typeof(HandPoseType)).Length);
                _leftHandPose = _rightHandPose;
            }
        }

        private void UpdateHeadPose()
        {
            _headVelocity = (_headPosition - _lastHeadPos) / Mathf.Max(Time.deltaTime, 0.0001f);
            _lastHeadPos = _headPosition;
        }

        private void UpdateHandPoses()
        {
            var dominantInteractor = DominantInteractor;
            var offHandInteractor = OffHandInteractor;

            var heldObj = dominantInteractor?.HeldObject ?? offHandInteractor?.HeldObject;

            // Compute target local positions & rotations in head-space for dominant hand and off-hand
            Vector3 targetDominantPos;
            Quaternion targetDominantRot = Quaternion.identity;

            Vector3 targetOffHandPos;
            Quaternion targetOffHandRot = Quaternion.identity;

            bool isAiming = _isAiming;

            if (heldObj != null)
            {
                bool isRifle = heldObj.Transform.name.Contains("Rifle");
                bool isFiring = (Time.time - _lastFireTime) < 0.35f || _rightTriggerHeld;
                bool twoHandedActive = _twoHandedGripActive || _isAiming || isFiring || _isMoving;

                if (isAiming)
                {
                    // State 1: Aiming Down Sights (I or RMB) - Centered directly in front of camera
                    if (isRifle)
                    {
                        targetDominantPos = new Vector3(0.0f, -0.185f, 0.16f);
                    }
                    else
                    {
                        targetDominantPos = new Vector3(0.0f, -0.11f, 0.32f);
                    }
                    targetDominantRot = Quaternion.identity;
                }
                else if (isFiring)
                {
                    // State 2: Actively Firing from Hip - Double-Handed Hold on the side with recoil stabilization
                    if (isRifle)
                    {
                        targetDominantPos = new Vector3(0.14f, -0.21f, 0.36f);
                        targetDominantRot = Quaternion.Euler(0.6f, -0.6f, 0f);
                    }
                    else
                    {
                        targetDominantPos = new Vector3(0.12f, -0.17f, 0.36f);
                        targetDominantRot = Quaternion.Euler(0.5f, -0.5f, 0f);
                    }
                }
                else if (_isMoving)
                {
                    // State 3: Moving / Walking / Running (Tactical Low-Ready - Weapon DOWN, held with both hands)
                    if (_isRunning)
                    {
                        // Sprinting: Weapon pointed down across the chest in tactical sprint
                        if (isRifle)
                        {
                            targetDominantPos = new Vector3(0.12f, -0.27f, 0.32f);
                            targetDominantRot = Quaternion.Euler(22.0f, -7.0f, -5.0f);
                        }
                        else
                        {
                            targetDominantPos = new Vector3(0.11f, -0.24f, 0.32f);
                            targetDominantRot = Quaternion.Euler(20.0f, -6.0f, -4.0f);
                        }
                    }
                    else
                    {
                        // Walking: Weapon pointed down in tactical low-ready
                        if (isRifle)
                        {
                            targetDominantPos = new Vector3(0.13f, -0.24f, 0.34f);
                            targetDominantRot = Quaternion.Euler(15.0f, -4.5f, -3.0f);
                        }
                        else
                        {
                            targetDominantPos = new Vector3(0.12f, -0.20f, 0.34f);
                            targetDominantRot = Quaternion.Euler(12.0f, -3.5f, -2.5f);
                        }
                    }
                }
                else if (_twoHandedGripActive)
                {
                    // State 4: Standing Still in Manual Two-Handed Stance
                    if (isRifle)
                    {
                        targetDominantPos = new Vector3(0.14f, -0.21f, 0.36f);
                        targetDominantRot = Quaternion.Euler(0.6f, -0.6f, 0f);
                    }
                    else
                    {
                        targetDominantPos = new Vector3(0.12f, -0.17f, 0.36f);
                        targetDominantRot = Quaternion.Euler(0.5f, -0.5f, 0f);
                    }
                }
                else
                {
                    // State 5: Standing Still at Rest (Single-Hand Hold)
                    // Natural relaxed single-hand hold at the side!
                    if (isRifle)
                    {
                        targetDominantPos = new Vector3(0.15f, -0.23f, 0.36f);
                        targetDominantRot = Quaternion.Euler(3.5f, -1.5f, -1.0f);
                    }
                    else
                    {
                        targetDominantPos = new Vector3(0.13f, -0.19f, 0.36f);
                        targetDominantRot = Quaternion.Euler(3.0f, -1.5f, -1.0f);
                    }
                }

                // Procedural Walk/Run/Idle Harmonic Bobbing & Sway
                float bobSpeed = _isRunning ? 12.5f : 8.5f;
                if (_isMoving)
                {
                    _bobTimer += Time.deltaTime * bobSpeed;
                }
                else
                {
                    _idleTimer += Time.deltaTime * 1.8f;
                }

                float targetBobIntensity = _isMoving ? (_isRunning ? 1.0f : 0.65f) : 0.0f;
                _currentBobIntensity = Mathf.MoveTowards(_currentBobIntensity, targetBobIntensity, Time.deltaTime * 4.5f);

                Vector3 bobPosOffset = Vector3.zero;
                Vector3 bobRotEuler = Vector3.zero;

                if (_currentBobIntensity > 0.001f)
                {
                    // Harmonic walking/running bob (figure-8 horizontal + dual-frequency vertical footstep dip)
                    float bobX = Mathf.Sin(_bobTimer) * (_isRunning ? 0.024f : 0.013f) * _currentBobIntensity;
                    float bobY = -Mathf.Abs(Mathf.Sin(_bobTimer)) * (_isRunning ? 0.019f : 0.009f) * _currentBobIntensity;
                    float bobZ = Mathf.Sin(_bobTimer) * (_isRunning ? 0.008f : 0.005f) * _currentBobIntensity;

                    float rotRoll = Mathf.Cos(_bobTimer) * (_isRunning ? 3.8f : 2.0f) * _currentBobIntensity;
                    float rotPitch = Mathf.Sin(_bobTimer * 2.0f) * (_isRunning ? 2.6f : 1.3f) * _currentBobIntensity;
                    float rotYaw = Mathf.Sin(_bobTimer) * (_isRunning ? 2.0f : 1.0f) * _currentBobIntensity;

                    // When aiming, dampen movement bob significantly so ADS remains precise and usable
                    float aimDamp = isAiming ? 0.12f : 1.0f;
                    bobPosOffset = new Vector3(bobX, bobY, bobZ) * aimDamp;
                    bobRotEuler = new Vector3(rotPitch, rotYaw, rotRoll) * aimDamp;
                }
                else if (!isAiming)
                {
                    // Subtle breathing sway at idle
                    float idleX = Mathf.Sin(_idleTimer) * 0.0018f;
                    float idleY = Mathf.Cos(_idleTimer * 2.0f) * 0.0014f;
                    float idleRoll = Mathf.Cos(_idleTimer) * 0.35f;
                    bobPosOffset = new Vector3(idleX, idleY, 0f);
                    bobRotEuler = new Vector3(0f, 0f, idleRoll);
                }

                // Add mouse look sway / inertia
                bobRotEuler.x += _mouseLookSwayCurrent.y;
                bobRotEuler.y += _mouseLookSwayCurrent.x;
                bobRotEuler.z += -_mouseLookSwayCurrent.x * 0.5f;

                targetDominantPos += bobPosOffset;
                targetDominantRot = targetDominantRot * Quaternion.Euler(bobRotEuler);

                if (_isReloading)
                {
                    float rt = Mathf.Clamp01(_reloadTimer / Mathf.Max(_reloadDuration, 0.001f));
                    float reloadCurve = Mathf.Sin(rt * Mathf.PI);

                    // Tactical reload tilt: tilts receiver inward to expose magwell to off-hand
                    Vector3 reloadDominantOffset = new Vector3(-0.025f, 0.015f, -0.035f) * reloadCurve;
                    Quaternion reloadDominantRot = Quaternion.Euler(-5f * reloadCurve, 8f * reloadCurve, -14f * reloadCurve);

                    targetDominantPos += reloadDominantOffset;
                    targetDominantRot = targetDominantRot * reloadDominantRot;
                }

                // Off-Hand Position:
                if (_isReloading)
                {
                    float rt = Mathf.Clamp01(_reloadTimer / Mathf.Max(_reloadDuration, 0.001f));

                    // Foregrip resting position (where off-hand started)
                    Vector3 foregripPos;
                    if (isRifle)
                    {
                        Transform secGrip = heldObj.Transform.Find("SecondaryForegripPoint");
                        Transform primGrip = heldObj.Transform.Find("PrimaryGripPoint");
                        Vector3 gripOffset = (secGrip != null && primGrip != null)
                            ? heldObj.Transform.InverseTransformPoint(secGrip.position) - heldObj.Transform.InverseTransformPoint(primGrip.position)
                            : new Vector3(0f, 0.025f, 0.31f);
                        foregripPos = targetDominantPos + targetDominantRot * gripOffset;
                    }
                    else
                    {
                        foregripPos = targetDominantPos + targetDominantRot * new Vector3(-0.03f, -0.035f, 0.02f);
                    }

                    // Magwell position
                    Vector3 magWellLocal = isRifle ? new Vector3(0f, -0.06f, 0.08f) : new Vector3(0f, -0.05f, 0.02f);
                    Vector3 magWellPos = targetDominantPos + targetDominantRot * magWellLocal;

                    // Tactical pouch position on hip
                    Vector3 pouchPos = targetDominantPos + new Vector3(-0.16f, -0.32f, -0.08f);

                    // Just below magwell mouth
                    Vector3 belowMagWell = magWellPos + targetDominantRot * new Vector3(0f, -0.18f, -0.02f);

                    if (rt < 0.20f)
                    {
                        // Phase 1: Hand drops from foregrip to ammo pouch
                        float subT = rt / 0.20f;
                        float eased = Mathf.SmoothStep(0f, 1f, subT);
                        targetOffHandPos = Vector3.Lerp(foregripPos, pouchPos, eased);
                        targetOffHandRot = Quaternion.Slerp(targetDominantRot, Quaternion.Euler(22f, -18f, 12f), eased);
                    }
                    else if (rt < 0.48f)
                    {
                        // Phase 2: Hand grabs fresh magazine and brings it up right underneath the magwell!
                        float subT = (rt - 0.20f) / 0.28f;
                        float eased = Mathf.SmoothStep(0f, 1f, subT);
                        targetOffHandPos = Vector3.Lerp(pouchPos, belowMagWell, eased);
                        targetOffHandRot = Quaternion.Slerp(Quaternion.Euler(22f, -18f, 12f), targetDominantRot, eased);
                    }
                    else if (rt < 0.68f)
                    {
                        // Phase 3: Hand drives magazine UP into the magwell and palm slaps baseplate home!
                        float subT = (rt - 0.48f) / 0.20f;
                        float eased = Mathf.SmoothStep(0f, 1f, subT);
                        targetOffHandPos = Vector3.Lerp(belowMagWell, magWellPos, eased);
                        targetOffHandRot = targetDominantRot;
                    }
                    else
                    {
                        // Phase 4: Hand returns smoothly from magwell back to forward foregrip!
                        float subT = (rt - 0.68f) / 0.20f;
                        float eased = Mathf.SmoothStep(0f, 1f, subT);
                        targetOffHandPos = Vector3.Lerp(magWellPos, foregripPos, eased);
                        targetOffHandRot = targetDominantRot;
                    }
                }
                // Only attach off-hand to foregrip / supporting hand when two-handed active (moving, firing, aiming, or manual lock)!
                else if (twoHandedActive && isRifle)
                {
                    Transform secGripTransform = heldObj.Transform.Find("SecondaryForegripPoint");
                    Transform primGripTransform = heldObj.Transform.Find("PrimaryGripPoint");

                    if (secGripTransform != null)
                    {
                        Vector3 secGripLocal = heldObj.Transform.InverseTransformPoint(secGripTransform.position);
                        Vector3 primGripLocal = primGripTransform != null
                            ? heldObj.Transform.InverseTransformPoint(primGripTransform.position)
                            : new Vector3(0f, -0.05f, -0.06f);

                        // Multiply local grip offset by targetDominantRot so off-hand rotates with barrel pitch down
                        Vector3 gripOffset = secGripLocal - primGripLocal;
                        targetOffHandPos = targetDominantPos + targetDominantRot * gripOffset;
                        targetOffHandRot = targetDominantRot;
                    }
                    else
                    {
                        targetOffHandPos = targetDominantPos + targetDominantRot * new Vector3(0f, 0.025f, 0.31f);
                        targetOffHandRot = targetDominantRot;
                    }
                }
                else if (twoHandedActive && !isRifle)
                {
                    // For pistol: two-handed tactical support grip cups underneath the dominant hand
                    targetOffHandPos = targetDominantPos + targetDominantRot * new Vector3(-0.03f, -0.035f, 0.02f);
                    targetOffHandRot = targetDominantRot * Quaternion.Euler(8.0f, 12.0f, -4.0f);
                }
                else
                {
                    // Single-hand hold: Off-hand rests down at the side!
                    targetOffHandPos = _leftHandRestOffset;
                    targetOffHandRot = Quaternion.identity;
                }
            }
            else
            {
                // State 3: Empty-Handed (No weapon held)
                // Hands rest at their left and right side offsets with ray pointers
                targetDominantPos = _rightHandRestOffset;
                targetDominantRot = Quaternion.identity;

                targetOffHandPos = _leftHandRestOffset;
                targetOffHandRot = Quaternion.identity;
            }

            // Assign to Right and Left according to Handedness
            Vector3 targetRightLocalPos;
            Quaternion targetRightLocalRot;
            Vector3 targetLeftLocalPos;
            Quaternion targetLeftLocalRot;

            if (!IsLeftHanded)
            {
                // Right-Handed: dominant hand on right side
                targetRightLocalPos = targetDominantPos;
                targetRightLocalRot = targetDominantRot;

                targetLeftLocalPos = targetOffHandPos;
                targetLeftLocalRot = targetOffHandRot;
            }
            else
            {
                // Left-Handed: dominant hand mirrored on left side
                targetLeftLocalPos = new Vector3(-targetDominantPos.x, targetDominantPos.y, targetDominantPos.z);
                Vector3 euler = targetDominantRot.eulerAngles;
                if (euler.x > 180f) euler.x -= 360f;
                if (euler.y > 180f) euler.y -= 360f;
                if (euler.z > 180f) euler.z -= 360f;
                targetLeftLocalRot = Quaternion.Euler(euler.x, -euler.y, -euler.z);

                targetRightLocalPos = new Vector3(-targetOffHandPos.x, targetOffHandPos.y, targetOffHandPos.z);
                Vector3 offEuler = targetOffHandRot.eulerAngles;
                if (offEuler.x > 180f) offEuler.x -= 360f;
                if (offEuler.y > 180f) offEuler.y -= 360f;
                if (offEuler.z > 180f) offEuler.z -= 360f;
                targetRightLocalRot = Quaternion.Euler(offEuler.x, -offEuler.y, -offEuler.z);
            }

            // Frame-rate independent exponential smoothing for buttery-smooth weapon movement
            if (!_smoothedPosesInitialized)
            {
                _smoothedRightLocalPos = targetRightLocalPos;
                _smoothedRightLocalRot = targetRightLocalRot;
                _smoothedLeftLocalPos = targetLeftLocalPos;
                _smoothedLeftLocalRot = targetLeftLocalRot;
                _smoothedPosesInitialized = true;
            }
            else
            {
                float dt = Time.deltaTime;
                float posLerpSpeed = isAiming ? 16.0f : 14.0f;
                float rotLerpSpeed = isAiming ? 18.0f : 15.0f;

                float posFactor = 1.0f - Mathf.Exp(-posLerpSpeed * dt);
                float rotFactor = 1.0f - Mathf.Exp(-rotLerpSpeed * dt);

                _smoothedRightLocalPos = Vector3.Lerp(_smoothedRightLocalPos, targetRightLocalPos, posFactor);
                _smoothedRightLocalRot = Quaternion.Slerp(_smoothedRightLocalRot, targetRightLocalRot, rotFactor);

                _smoothedLeftLocalPos = Vector3.Lerp(_smoothedLeftLocalPos, targetLeftLocalPos, posFactor);
                _smoothedLeftLocalRot = Quaternion.Slerp(_smoothedLeftLocalRot, targetLeftLocalRot, rotFactor);
            }

            _rightHandPosition = _headPosition + _headRotation * _smoothedRightLocalPos;
            _rightHandRotation = _headRotation * _smoothedRightLocalRot;

            _leftHandPosition = _headPosition + _headRotation * _smoothedLeftLocalPos;
            _leftHandRotation = _headRotation * _smoothedLeftLocalRot;

            // Direct sync interactor attach transforms and camera so all transforms match in the exact same frame
            if (_rightHandInteractor != null && _rightHandInteractor.AttachTransform != null)
            {
                _rightHandInteractor.AttachTransform.position = _rightHandPosition;
                _rightHandInteractor.AttachTransform.rotation = _rightHandRotation;
            }

            if (_leftHandInteractor != null && _leftHandInteractor.AttachTransform != null)
            {
                _leftHandInteractor.AttachTransform.position = _leftHandPosition;
                _leftHandInteractor.AttachTransform.rotation = _leftHandRotation;
            }

            if (Camera.main != null)
            {
                Camera.main.transform.position = _headPosition;
                Camera.main.transform.rotation = _headRotation;
            }
        }

        private void CalculateVelocities()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            // Right hand velocity
            _rightHandPosHistory.Enqueue(_rightHandPosition);
            _timeHistory.Enqueue(dt);
            if (_rightHandPosHistory.Count > _velocitySampleCount)
            {
                _rightHandPosHistory.Dequeue();
                _timeHistory.Dequeue();
            }

            if (_rightHandPosHistory.Count >= 2)
            {
                var arr = _rightHandPosHistory.ToArray();
                float totalTime = 0f;
                foreach (var t in _timeHistory) totalTime += t;
                if (totalTime > 0.0001f)
                {
                    _rightHandVelocity = (arr[arr.Length - 1] - arr[0]) / totalTime;
                }
            }

            // Angular velocity Right
            Quaternion deltaRotRight = _rightHandRotation * Quaternion.Inverse(_lastRightHandRot);
            deltaRotRight.ToAngleAxis(out float angleRight, out Vector3 axisRight);
            if (angleRight > 180f) angleRight -= 360f;
            _rightHandAngularVelocity = (axisRight * (angleRight * Mathf.Deg2Rad)) / dt;
            _lastRightHandRot = _rightHandRotation;

            // Left hand velocity & angular velocity
            _leftHandPosHistory.Enqueue(_leftHandPosition);
            if (_leftHandPosHistory.Count > _velocitySampleCount)
            {
                _leftHandPosHistory.Dequeue();
            }

            if (_leftHandPosHistory.Count >= 2)
            {
                var arr = _leftHandPosHistory.ToArray();
                float totalTime = 0f;
                foreach (var t in _timeHistory) totalTime += t;
                if (totalTime > 0.0001f)
                {
                    _leftHandVelocity = (arr[arr.Length - 1] - arr[0]) / totalTime;
                }
            }

            Quaternion deltaRotLeft = _leftHandRotation * Quaternion.Inverse(_lastLeftHandRot);
            deltaRotLeft.ToAngleAxis(out float angleLeft, out Vector3 axisLeft);
            if (angleLeft > 180f) angleLeft -= 360f;
            _leftHandAngularVelocity = (axisLeft * (angleLeft * Mathf.Deg2Rad)) / dt;
            _lastLeftHandRot = _leftHandRotation;
        }

        // ITrackingProvider Implementation
        public Vector3 GetHandPosition(Handedness hand) => hand == Handedness.Right ? _rightHandPosition : _leftHandPosition;
        public Quaternion GetHandRotation(Handedness hand) => hand == Handedness.Right ? _rightHandRotation : _leftHandRotation;
        public Vector3 GetHandVelocity(Handedness hand) => hand == Handedness.Right ? _rightHandVelocity : _leftHandVelocity;
        public Vector3 GetHandAngularVelocity(Handedness hand) => hand == Handedness.Right ? _rightHandAngularVelocity : _leftHandAngularVelocity;
        public bool IsHandTracked(Handedness hand) => true;

        // IControllerInputProvider Implementation
        private bool IsActiveWeaponHand(Handedness hand)
        {
            bool rightHolding = _rightHandInteractor != null && _rightHandInteractor.HeldObject != null;
            bool leftHolding = _leftHandInteractor != null && _leftHandInteractor.HeldObject != null;

            if (rightHolding && !leftHolding) return hand == Handedness.Right;
            if (leftHolding && !rightHolding) return hand == Handedness.Left;
            if (rightHolding && leftHolding) return hand == DominantHand;

            // Empty-handed: dominant hand routes clicks for ray/poke
            return hand == DominantHand;
        }

        public float GetTrigger(Handedness hand)
        {
            return IsActiveWeaponHand(hand) ? (_rightTriggerHeld ? 1.0f : 0.0f) : 0.0f;
        }

        public bool GetTriggerDown(Handedness hand)
        {
            return IsActiveWeaponHand(hand) && _rightTriggerDown;
        }

        public bool GetTriggerUp(Handedness hand)
        {
            return IsActiveWeaponHand(hand) && _rightTriggerUp;
        }

        public float GetGrip(Handedness hand)
        {
            if (hand == Handedness.Right) return _rightGripHeld ? 1.0f : 0.0f;
            return _leftGripHeld ? 1.0f : 0.0f;
        }

        public bool GetGripDown(Handedness hand) => hand == Handedness.Right ? _rightGripDown : _leftGripDown;
        public bool GetGripUp(Handedness hand) => hand == Handedness.Right ? _rightGripUp : _leftGripUp;

        public bool GetPrimaryButton(Handedness hand) => _primaryButtonHeld;
        public bool GetPrimaryButtonDown(Handedness hand) => _primaryButtonDown;
        public bool GetSecondaryButton(Handedness hand) => _secondaryButtonHeld;
        public bool GetSecondaryButtonDown(Handedness hand) => _secondaryButtonDown;

        public Vector2 GetThumbstick(Handedness hand) => _simulatedThumbstick;
        public bool GetThumbstickClick(Handedness hand) => false;

        public void TriggerHaptic(Handedness hand, float amplitude, float durationSeconds)
        {
        }

        // IHandPoseProvider Implementation
        public HandPoseType GetCurrentPose(Handedness hand) => hand == Handedness.Right ? _rightHandPose : _leftHandPose;
        public float GetFingerCurl(Handedness hand, FingerType finger)
        {
            float grip = GetGrip(hand);
            float trigger = GetTrigger(hand);
            switch (finger)
            {
                case FingerType.Index: return trigger;
                case FingerType.Middle:
                case FingerType.Ring:
                case FingerType.Pinky: return grip;
                case FingerType.Thumb: return 0.2f;
                default: return 0f;
            }
        }

        private void OnGUI()
        {
            if (!_showDesktopHUD) return;

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            Color prevColor = GUI.color;

            var heldObj = DominantInteractor?.HeldObject ?? OffHandInteractor?.HeldObject;
            var heldWeapon = (heldObj as IWeaponInteraction) ?? heldObj?.Transform.GetComponent<IWeaponInteraction>() ?? heldObj?.Transform.GetComponentInParent<IWeaponInteraction>();
            var heldMag = (heldObj as IMagazine) ?? heldObj?.Transform.GetComponent<IMagazine>() ?? heldObj?.Transform.GetComponentInParent<IMagazine>();

            var hoveredWeapon = (_hoveredGrabbable as IWeaponInteraction) ?? _hoveredGrabbable?.Transform.GetComponent<IWeaponInteraction>() ?? _hoveredGrabbable?.Transform.GetComponentInParent<IWeaponInteraction>();
            var hoveredMag = (_hoveredGrabbable as IMagazine) ?? _hoveredGrabbable?.Transform.GetComponent<IMagazine>() ?? _hoveredGrabbable?.Transform.GetComponentInParent<IMagazine>();
            bool isLookingAtMag = hoveredMag != null || (_hoveredGrabbable != null && _hoveredName.ToLower().Contains("mag"));

            // Center Crosshair
            GUI.color = _isAiming ? Color.green : (_twoHandedGripActive ? new Color(0.2f, 0.9f, 1f) : ((_rightGripHeld || _leftGripHeld) ? new Color(1f, 0.4f, 0.1f) : Color.white));
            GUI.Box(new Rect(centerX - 3f, centerY - 3f, 6f, 6f), GUIContent.none);

            var promptStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            // Dynamic Contextual Center Prompt
            if (heldMag != null && hoveredWeapon != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 240f, centerY + 30f, 480f, 42f), GUIContent.none);
                GUI.color = Color.green;
                GUI.Label(new Rect(centerX - 240f, centerY + 30f, 480f, 42f), $"[ HELD: MAGAZINE ]  ->  [ {hoveredWeapon.Type} ]\nPress E, R, or Left Click to Insert Magazine & Equip!", promptStyle);
            }
            else if (heldWeapon != null && isLookingAtMag)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 240f, centerY + 30f, 480f, 42f), GUIContent.none);
                GUI.color = Color.yellow;
                GUI.Label(new Rect(centerX - 240f, centerY + 30f, 480f, 42f), $"[ HELD: {heldWeapon.Type} ({heldWeapon.CurrentAmmo}/{heldWeapon.MagazineCapacity}) ]  ->  [ MAGAZINE ]\nPress E, R, or Left Click to Insert Magazine", promptStyle);
            }
            else if (heldWeapon != null)
            {
                // Weapon Ammo Counter under crosshair
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.Box(new Rect(centerX - 160f, centerY + 40f, 320f, 32f), GUIContent.none);
                if (heldWeapon.CurrentAmmo <= 0)
                {
                    GUI.color = Color.red;
                    GUI.Label(new Rect(centerX - 160f, centerY + 40f, 320f, 32f), $"[ {heldWeapon.Type} ]  AMMO: EMPTY!  (Press R or E to Reload)", promptStyle);
                }
                else
                {
                    GUI.color = Color.cyan;
                    GUI.Label(new Rect(centerX - 160f, centerY + 40f, 320f, 32f), $"[ {heldWeapon.Type} ]  AMMO: {heldWeapon.CurrentAmmo} / {heldWeapon.MagazineCapacity}", promptStyle);
                }
            }
            else if (heldMag != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.Box(new Rect(centerX - 160f, centerY + 40f, 320f, 32f), GUIContent.none);
                GUI.color = Color.yellow;
                GUI.Label(new Rect(centerX - 160f, centerY + 40f, 320f, 32f), $"[ HELD: MAGAZINE ({heldMag.CurrentAmmo}/{heldMag.MaxAmmo}) ]\nLook at weapon & press E to insert", promptStyle);
            }
            else if (_hoveredPokeable != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), GUIContent.none);
                GUI.color = Color.cyan;
                GUI.Label(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), $"[ POKE: {_hoveredPokeable.DisplayName.ToUpper()} ]\nPress Left Click or E to Push Button", promptStyle);
            }
            else if (_hoveredUIButton != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), GUIContent.none);
                GUI.color = Color.cyan;
                GUI.Label(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), $"[ BUTTON: {_hoveredName.ToUpper()} ]\nPress Left Click or E to Activate", promptStyle);
            }
            else if (_hoveredUISlider != null)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), GUIContent.none);
                GUI.color = Color.cyan;
                GUI.Label(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), $"[ SLIDER: {_hoveredName.ToUpper()} ]\nHold Left Click to Adjust Value", promptStyle);
            }
            else if (!string.IsNullOrEmpty(_hoveredName))
            {
                GUI.color = new Color(0f, 0f, 0f, 0.85f);
                GUI.Box(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), GUIContent.none);
                GUI.color = Color.green;
                GUI.Label(new Rect(centerX - 180f, centerY + 30f, 360f, 35f), $"[ {_hoveredName.ToUpper()} ]\nPress E or Left Click to Pick Up", promptStyle);
            }

            // Tactical Controls Box in Top-Left Corner
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Box(new Rect(15f, 15f, 440f, 270f), GUIContent.none);

            GUI.color = Color.yellow;
            var headerStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(25f, 20f, 420f, 22f), "=== DESKTOP TACTICAL CONTROLS ===", headerStyle);

            GUI.color = Color.white;
            GUI.Label(new Rect(25f, 42f, 420f, 18f), "WASD: Move  |  Mouse: Look around");
            GUI.Label(new Rect(25f, 62f, 420f, 18f), "Left Click (Empty Hand): Pick up item / Poke UI button");
            GUI.Label(new Rect(25f, 82f, 420f, 18f), "Left Click (Holding Gun): Fire bullet");
            GUI.Label(new Rect(25f, 102f, 420f, 18f), "Right Click or I: Aim Down Sights (ADS)");
            GUI.Label(new Rect(25f, 122f, 420f, 18f), "T or Q: Toggle Two-Handed Foregrip (Sight Alignment)");
            GUI.Label(new Rect(25f, 142f, 420f, 18f), "L: Toggle Left/Right Hand  |  R: Quick Reload");
            GUI.Label(new Rect(25f, 162f, 420f, 18f), "F: Rack Slide (Manual Chamber / Jam Clear)");
            GUI.Label(new Rect(25f, 182f, 420f, 18f), "E: Pick Up / Insert Magazine / Push Poke Button");
            GUI.Label(new Rect(25f, 202f, 420f, 18f), "M: Toggle LAN Room  |  P: Replay (Play/Stop)  |  O: Rec");
            GUI.Label(new Rect(25f, 222f, 420f, 18f), "- / =: Dec/Inc Target Count (4 to 300) | Ammo never ends!");

            GUI.color = heldWeapon != null ? Color.green : (heldMag != null ? Color.yellow : Color.cyan);
            string statusStr;
            if (_replayRecorder != null && _replayRecorder.IsPlayingBack)
            {
                statusStr = "Status: PLAYING 30s GHOST REPLAY (Ghosts & Bullets) [Press P: Stop]";
            }
            else if (heldWeapon != null)
            {
                statusStr = $"Status: EQUIPPED {heldWeapon.Type} | Ammo: {heldWeapon.CurrentAmmo}/{heldWeapon.MagazineCapacity} [Press R: Reload]";
            }
            else if (heldMag != null)
            {
                statusStr = $"Status: HOLDING MAGAZINE ({heldMag.CurrentAmmo}/{heldMag.MaxAmmo}) | [Look at gun & Press E]";
            }
            else
            {
                statusStr = "Status: HANDS FREE | (Walk to table/kiosk & click item or button)";
            }
            GUI.Label(new Rect(25f, 245f, 420f, 20f), statusStr, headerStyle);

            GUI.color = prevColor;
        }

        private void HandleWeaponRecoil(float pitch, float yaw)
        {
            // Crisp, controllable camera punch
            float dampening = (_isAiming || _leftGripHeld) ? 0.35f : 0.60f;
            float pitchKick = pitch * dampening;
            float yawKick = yaw * dampening * 0.35f;

            _cameraRecoilTarget.x -= pitchKick; // pitch up (negative pitch in camera Euler)
            _cameraRecoilTarget.y += yawKick;

            // Clamp max camera recoil accumulation so it doesn't climb out of control
            _cameraRecoilTarget.x = Mathf.Clamp(_cameraRecoilTarget.x, -2.5f, 0.5f);
            _cameraRecoilTarget.y = Mathf.Clamp(_cameraRecoilTarget.y, -0.8f, 0.8f);
        }

        private void OnDisable()
        {
            if (_subscribedWeapon != null)
            {
                _subscribedWeapon.OnRecoilFired -= HandleWeaponRecoil;
                _subscribedWeapon = null;
            }
        }
    }
}
