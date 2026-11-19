using System;
using System.Collections;
using UnityEngine;
using ShootingRange.Core;
using ShootingRange.Interaction;
using ShootingRange.Ballistics;

namespace ShootingRange.Weapons
{
    public enum ChamberState
    {
        Empty,
        ChamberedRound,
        SpentCasing,
        Jammed
    }

    /// <summary>
    /// Base class for weapons supporting frame-rate independent fire scheduling,
    /// magazine physical reload, slide charging, jam simulation, procedural recoil and sway.
    /// </summary>
    [RequireComponent(typeof(PhysicsGrabbable))]
    public abstract class WeaponBase : MonoBehaviour, IWeaponInteraction
    {
        [Header("Configuration")]
        [SerializeField] private WeaponDefinition _definition;
        [SerializeField] private Transform _muzzleTransform;
        [SerializeField] private Transform _ejectionPortTransform;
        [SerializeField] private Transform _magazineWellTransform;
        [SerializeField] private SlideChargingHandle _slideHandle;

        [Header("Procedural Visuals & Audio")]
        [SerializeField] private Transform _recoilPivotTransform;
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private ParticleSystem _muzzleFlashParticles;
        [SerializeField] private Material _magazineMaterial;
        [SerializeField] private Material _brassMaterial;

        // Dependencies
        private IBallisticsSimulation _ballistics;
        private IControllerInputProvider _inputProvider;
        private ITrackingProvider _trackingProvider;
        private INetworkSession _networkSession;
        private PhysicsGrabbable _grabbable;
        private TwoHandedRifleGrip _twoHandedGrip;

        // Weapon States
        private ChamberState _chamberState = ChamberState.ChamberedRound;
        [SerializeField] private Magazine _insertedMagazine;
        private FireMode _currentFireMode;
        private int _fireModeIndex;

        // Magazine insertion animation state
        private bool _isInsertingMagazine;
        private float _insertTimer;
        private float _insertDuration = 0.30f;
        private Vector3 _insertStartLocalPos;
        private Vector3 _insertTargetLocalPos;
        private Quaternion _insertStartLocalRot;
        private Magazine _currentInsertingMag;
        private Coroutine _reloadRoutine;

        public bool IsInsertingMagazine => _isInsertingMagazine;
        public bool IsReloading => _reloadRoutine != null || _isInsertingMagazine;

        // Firing schedule
        private float _fireTimer;
        private int _burstShotsRemaining;
        private bool _triggerWasPressed;

        // Recoil state
        private Vector3 _recoilOffset;
        private Vector3 _recoilRotationEuler;
        private float _currentSpread;

        public WeaponDefinition Definition => _definition;
        public ChamberState Chamber => _chamberState;
        public Magazine InsertedMagazine => _insertedMagazine;
        public FireMode CurrentFireMode => _currentFireMode;
        public bool IsJammed => _chamberState == ChamberState.Jammed;

        private Magazine _lastEjectedMagazine;
        private float _lastEjectTime = -99f;
        public Magazine LastEjectedMagazine => _lastEjectedMagazine;
        public float LastEjectTime => _lastEjectTime;

        // IWeaponInteraction Implementation
        public WeaponType Type => _definition != null ? _definition.Type : WeaponType.Pistol;
        public bool IsTwoHanded => _twoHandedGrip != null;
        public bool IsSecondaryGrabbed => _twoHandedGrip != null && _twoHandedGrip.IsSecondaryGrabbed;
        public int CurrentAmmo => _insertedMagazine != null ? _insertedMagazine.CurrentAmmo : 0;
        public int MagazineCapacity => _definition != null ? _definition.MagazineCapacity : 15;
        public bool IsChambered => _chamberState == ChamberState.ChamberedRound;
        public Vector3 RecoilOffset => _recoilOffset;
        public Quaternion RecoilRotation => Quaternion.Euler(_recoilRotationEuler);
        public event Action<float, float> OnRecoilFired;

        public void OnSecondaryGrabbed(Handedness hand, Transform secondaryAttachPoint) => _twoHandedGrip?.OnSecondaryGrabbed(hand, secondaryAttachPoint);
        public void OnSecondaryReleased(Handedness hand) => _twoHandedGrip?.OnSecondaryReleased(hand);

        // IGrabbable pass-through implementations
        public Transform Transform => transform;
        public Rigidbody Rigidbody => _grabbable != null ? _grabbable.Rigidbody : GetComponent<Rigidbody>();
        public bool IsGrabbed => _grabbable != null && _grabbable.IsGrabbed;
        public Handedness? CurrentGrabbingHand => _grabbable != null ? _grabbable.CurrentGrabbingHand : null;
        public void OnGrabbed(Handedness hand, Transform handAttachPoint, Vector3 grabLinearVelocity, Vector3 grabAngularVelocity)
        {
            _grabbable?.OnGrabbed(hand, handAttachPoint, grabLinearVelocity, grabAngularVelocity);
        }
        public void OnReleased(Handedness hand, Vector3 releaseLinearVelocity, Vector3 releaseAngularVelocity)
        {
            _grabbable?.OnReleased(hand, releaseLinearVelocity, releaseAngularVelocity);
        }
        public void OnHandTransfer(Handedness fromHand, Handedness toHand, Transform newAttachPoint)
        {
            _grabbable?.OnHandTransfer(fromHand, toHand, newAttachPoint);
        }

        public event Action OnFired;
        public event Action OnJammed;
        public event Action OnJamCleared;
        public event Action<FireMode> OnFireModeSwitched;
        public event Action OnMagazineInserted;
        public event Action OnMagazineEjected;

        protected virtual void Awake()
        {
            _grabbable = GetComponent<PhysicsGrabbable>();
            _twoHandedGrip = GetComponent<TwoHandedRifleGrip>();

            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1.0f;
                _audioSource.playOnAwake = false;
            }

            if (_recoilPivotTransform == null) _recoilPivotTransform = transform;

            if (_definition != null)
            {
                _currentFireMode = _definition.DefaultFireMode;
            }

            if (_slideHandle != null)
            {
                _slideHandle.OnFullyRetracted += HandleSlideRetracted;
                _slideHandle.OnSlideReturnedForward += HandleSlideForward;
            }
        }

        private void Start()
        {
            ResolveDependencies();

            // Prevent unheld resting weapons from falling off benches/tables when entering Play mode
            if (_grabbable != null && !_grabbable.IsGrabbed)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.isKinematic = true;
                }

                var rootCol = GetComponent<Collider>();
                if (rootCol != null)
                {
                    rootCol.isTrigger = false;
                }
            }

            // Auto-detect pre-existing child magazine if _insertedMagazine reference was unlinked
            if (_insertedMagazine == null)
            {
                _insertedMagazine = GetComponentInChildren<Magazine>();
            }

            Vector3 seatedLocalPos = _definition != null && _definition.Type == WeaponType.Rifle
                ? new Vector3(0f, -0.06f, 0f)
                : new Vector3(0f, -0.02f, 0f);
            Transform targetParent = _magazineWellTransform != null ? _magazineWellTransform : transform;

            // Remove any orphan duplicate magazines inside the well
            var existingMags = GetComponentsInChildren<Magazine>(true);
            foreach (var m in existingMags)
            {
                if (m != _insertedMagazine)
                {
                    Destroy(m.gameObject);
                }
            }

            if (_insertedMagazine != null)
            {
                _insertedMagazine.SetSeated(true, targetParent, seatedLocalPos);
                _chamberState = ChamberState.ChamberedRound;
            }
            else if (_definition != null)
            {
                _insertedMagazine = CreateDefaultMagazine();
                _insertedMagazine.SetSeated(true, targetParent, seatedLocalPos);
                _chamberState = ChamberState.ChamberedRound;
            }
        }

        private INetworkSession EnsureNetworkSession()
        {
            if (_networkSession != null && _networkSession.IsActive) return _networkSession;
            if (ShootingRangeCompositionRoot.Container != null && ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession))
            {
                return _networkSession;
            }
            var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var p in provs)
            {
                if (p is INetworkSession ns) { _networkSession = ns; return _networkSession; }
            }
            return _networkSession;
        }

        private void ResolveDependencies()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                if (_ballistics == null) ShootingRangeCompositionRoot.Container.TryResolve(out _ballistics);
                if (_inputProvider == null) ShootingRangeCompositionRoot.Container.TryResolve(out _inputProvider);
                if (_trackingProvider == null) ShootingRangeCompositionRoot.Container.TryResolve(out _trackingProvider);
                if (_networkSession == null) ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession);
            }

            if (_networkSession == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is INetworkSession ns) { _networkSession = ns; break; }
                }
            }

            if (_ballistics == null)
            {
                var ballisticsMgr = FindFirstObjectByType<MonoBehaviour>() as IBallisticsSimulation;
                if (ballisticsMgr != null) _ballistics = ballisticsMgr;
            }

            if (_inputProvider == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is IControllerInputProvider ip) { _inputProvider = ip; break; }
                }
            }

            if (_trackingProvider == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is ITrackingProvider tp) { _trackingProvider = tp; break; }
                }
            }

            if (_grabbable == null)
            {
                _grabbable = GetComponent<PhysicsGrabbable>();
            }

            if (_grabbable != null)
            {
                if (_grabbable.MagazineInsertHandler == null) _grabbable.MagazineInsertHandler = TryInsertMagazine;
                if (_grabbable.ReloadHandler == null) _grabbable.ReloadHandler = Reload;
                if (_grabbable.EjectMagazineHandler == null) _grabbable.EjectMagazineHandler = EjectMagazine;
                if (_grabbable.RackSlideHandler == null) _grabbable.RackSlideHandler = RackSlide;
                _grabbable.CurrentAmmoProvider = () => CurrentAmmo;
                _grabbable.MagazineCapacityProvider = () => MagazineCapacity;
                _grabbable.IsChamberedProvider = () => IsChambered;
                _grabbable.IsReloadingProvider = () => IsReloading;
                _grabbable.RecoilOffsetProvider = () => RecoilOffset;
                _grabbable.RecoilRotationProvider = () => RecoilRotation;
            }

            if (_twoHandedGrip != null)
            {
                _twoHandedGrip.IsReloadingProvider = () => IsReloading;
                _twoHandedGrip.RecoilOffsetProvider = () => RecoilOffset;
                _twoHandedGrip.RecoilRotationProvider = () => RecoilRotation;
                OnFired += _twoHandedGrip.TriggerFired;
                OnRecoilFired += _twoHandedGrip.TriggerRecoilFired;
            }

            if (_muzzleTransform == null)
            {
                var m = transform.Find("Muzzle");
                if (m != null) _muzzleTransform = m;
            }
        }

        protected virtual void Update()
        {
            ResolveDependencies();
            UpdateFireTimer();
            UpdateRecoilRecovery();
            UpdateMagazineInsertion();
            ProcessInput();
        }

        protected virtual void LateUpdate()
        {
            if (_insertedMagazine != null)
            {
                if (_isInsertingMagazine) return;

                Transform targetParent = _magazineWellTransform != null ? _magazineWellTransform : transform;
                Vector3 seatedLocalPos = _definition != null && _definition.Type == WeaponType.Rifle
                    ? new Vector3(0f, -0.06f, 0f)
                    : new Vector3(0f, -0.02f, 0f);

                if (_insertedMagazine.transform.parent != targetParent)
                {
                    _insertedMagazine.transform.SetParent(targetParent, false);
                }

                _insertedMagazine.transform.localPosition = seatedLocalPos;
                _insertedMagazine.transform.localRotation = Quaternion.identity;

                var rb = _insertedMagazine.Rigidbody != null ? _insertedMagazine.Rigidbody : _insertedMagazine.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.interpolation = RigidbodyInterpolation.None;
                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
            }
        }

        private void UpdateFireTimer()
        {
            if (_fireTimer > 0f)
            {
                _fireTimer -= Time.deltaTime;
            }
        }

        private void UpdateRecoilRecovery()
        {
            using (RangeProfilerMarkers.WeaponRecoil.Auto())
            {
                float recovery = _definition != null ? Mathf.Max(_definition.RecoilRecoverySpeed, 18f) : 22f;
                _recoilOffset = Vector3.Lerp(_recoilOffset, Vector3.zero, Time.deltaTime * recovery);
                _recoilOffset = Vector3.MoveTowards(_recoilOffset, Vector3.zero, Time.deltaTime * 0.12f);

                _recoilRotationEuler = Vector3.Lerp(_recoilRotationEuler, Vector3.zero, Time.deltaTime * recovery);
                _recoilRotationEuler = Vector3.MoveTowards(_recoilRotationEuler, Vector3.zero, Time.deltaTime * 12f);

                if (_definition != null)
                {
                    _currentSpread = Mathf.MoveTowards(_currentSpread, _definition.BaseSpreadDegrees, Time.deltaTime * 3.0f);
                }

                if (_recoilPivotTransform != null && _recoilPivotTransform != transform)
                {
                    _recoilPivotTransform.localPosition = _recoilOffset;
                    _recoilPivotTransform.localRotation = Quaternion.Euler(_recoilRotationEuler);
                }
            }
        }

        private void ProcessInput()
        {
            if (_grabbable == null || !_grabbable.IsGrabbed || !_grabbable.CurrentGrabbingHand.HasValue)
            {
                _triggerWasPressed = false;
                return;
            }

            Handedness hand = _grabbable.CurrentGrabbingHand.Value;
            if (_inputProvider == null) return;

            // Trigger input
            float triggerVal = _inputProvider.GetTrigger(hand);
            bool triggerHeld = triggerVal > 0.6f;
            bool triggerJustPressed = _inputProvider.GetTriggerDown(hand);

            // Magazine Ejection (Primary Button: R on desktop or A/X on controller)
            if (_inputProvider.GetPrimaryButtonDown(hand))
            {
                EjectMagazine();
            }

            // Charging handle rack (Secondary Button: F on desktop or B/Y on controller)
            if (_inputProvider.GetSecondaryButtonDown(hand))
            {
                RackSlide();
            }

            // Handle Fire Modes
            switch (_currentFireMode)
            {
                case FireMode.SemiAuto:
                    if (triggerJustPressed)
                    {
                        TryFireSingleShot();
                    }
                    break;

                case FireMode.Burst:
                    if (triggerJustPressed && _burstShotsRemaining == 0)
                    {
                        _burstShotsRemaining = _definition != null ? _definition.BurstCount : 3;
                    }

                    if (_burstShotsRemaining > 0 && _fireTimer <= 0f)
                    {
                        bool shotFired = TryFireSingleShot();
                        if (shotFired)
                        {
                            _burstShotsRemaining--;
                        }
                        else
                        {
                            _burstShotsRemaining = 0; // Stop burst on empty/jam
                        }
                    }
                    break;

                case FireMode.FullAuto:
                    if (triggerHeld && _fireTimer <= 0f)
                    {
                        TryFireSingleShot();
                    }
                    break;
            }

            _triggerWasPressed = triggerHeld;
        }

        public bool TryFireSingleShot()
        {
            using (RangeProfilerMarkers.WeaponFire.Auto())
            {
                if (_definition == null || _isInsertingMagazine) return false;

                // Fire rate interval enforcement
                _fireTimer = _definition.FireIntervalSeconds;

                // Check jam
                if (_chamberState == ChamberState.Jammed)
                {
                    PlayJamSound();
                    return false;
                }

                // If chamber is empty/spent but magazine has ammo, strip round into chamber
                if (_chamberState != ChamberState.ChamberedRound && _insertedMagazine != null && !_insertedMagazine.IsEmpty)
                {
                    CycleActionFromMagazine();
                }

                // Check chamber
                if (_chamberState != ChamberState.ChamberedRound)
                {
                    PlayDryFireSound();
                    return false;
                }

                // Jam simulation chance
                if (UnityEngine.Random.value < _definition.JamProbability)
                {
                    _chamberState = ChamberState.Jammed;
                    PlayJamSound();
                    OnJammed?.Invoke();
                    return false;
                }

                // Execute Shot
                ExecuteShot();

                // Chamber cycle
                _chamberState = ChamberState.SpentCasing;
                CycleActionFromMagazine();

                return true;
            }
        }

        private void ExecuteShot()
        {
            Vector3 muzzlePos = _muzzleTransform != null ? _muzzleTransform.position : transform.position;
            Vector3 muzzleForward = _muzzleTransform != null ? _muzzleTransform.forward : transform.forward;

            // Apply spread
            float spreadAngle = _currentSpread;
            Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * spreadAngle;
            Quaternion spreadRotation = Quaternion.Euler(randomCircle.y, randomCircle.x, 0f);
            Vector3 bulletVelocity = (spreadRotation * muzzleForward) * _definition.BulletVelocityMps;

            EnsureNetworkSession();
            ulong shooterId = _networkSession != null && _networkSession.IsActive ? _networkSession.LocalClientId : 0;

            // Spawn ballistic projectile into simulation
            if (_ballistics != null)
            {
                var spawnParams = new BulletSpawnParams
                {
                    Origin = muzzlePos,
                    Velocity = bulletVelocity,
                    MassKg = _definition.BulletMassKg,
                    CaliberMm = _definition.CaliberMm,
                    BaseDamage = _definition.BaseDamage,
                    MaxRange = _definition.MaxRange,
                    ShooterClientId = shooterId,
                    WeaponId = GetInstanceID()
                };
                _ballistics.SpawnBullet(spawnParams);
            }

            if (_networkSession != null && _networkSession.IsActive)
            {
                _networkSession.BroadcastShot(shooterId, muzzlePos, bulletVelocity, _definition != null ? _definition.Type : WeaponType.Pistol);
            }

            // Visual effects
            if (_muzzleFlashParticles != null)
            {
                _muzzleFlashParticles.Play();
            }

            // Sound
            PlayFireSound();

            // Recoil calculation
            ApplyRecoil();

            // Haptics
            TriggerRecoilHaptics();

            OnFired?.Invoke();
        }

        private void ApplyRecoil()
        {
            if (_definition == null) return;

            // Recoil dampening based on weapon weight
            float weightDampening = 1.0f / Mathf.Sqrt(Mathf.Max(_definition.WeightKg, 0.5f));

            // Grip dampening (two-handed rifle grip reduces recoil by 60%)
            float gripDampening = 1.0f;
            if (_twoHandedGrip != null && _twoHandedGrip.IsSecondaryGrabbed)
            {
                gripDampening = 0.40f;
            }

            float finalMultiplier = weightDampening * gripDampening;

            float pitch = _definition.RecoilPitchDegrees * finalMultiplier;
            float yaw = UnityEngine.Random.Range(-_definition.RecoilYawDegrees, _definition.RecoilYawDegrees) * finalMultiplier;
            float kick = _definition.RecoilKickBackMeters * finalMultiplier;

            // Physical weapon recoil: crisp linear kickback along -Z, subtle +Y rise, slight pitch up around -X
            _recoilRotationEuler += new Vector3(-pitch, yaw * 0.4f, 0f);
            _recoilOffset += new Vector3(0f, 0.08f * kick, -kick);

            // Clamp accumulated recoil so it remains controllable and normalizes cleanly
            _recoilOffset.z = Mathf.Max(_recoilOffset.z, -0.04f);
            _recoilRotationEuler.x = Mathf.Max(_recoilRotationEuler.x, -3.5f);

            _currentSpread = Mathf.Min(_currentSpread + _definition.SpreadPerShotDegrees, _definition.MaxSpreadDegrees);

            // Trigger dynamic recoil event for camera punch and visual listeners
            OnRecoilFired?.Invoke(pitch, yaw);
        }

        private void TriggerRecoilHaptics()
        {
            if (_inputProvider == null || _grabbable == null || !_grabbable.CurrentGrabbingHand.HasValue) return;

            Handedness primaryHand = _grabbable.CurrentGrabbingHand.Value;
            _inputProvider.TriggerHaptic(primaryHand, 0.85f, 0.08f);

            // Off-hand haptic feedback if holding foregrip
            if (_twoHandedGrip != null && _twoHandedGrip.IsSecondaryGrabbed && _twoHandedGrip.SecondaryHand.HasValue)
            {
                _inputProvider.TriggerHaptic(_twoHandedGrip.SecondaryHand.Value, 0.45f, 0.06f);
            }
        }

        private void CycleActionFromMagazine()
        {
            if (_insertedMagazine != null && !_insertedMagazine.IsEmpty)
            {
                if (_insertedMagazine.TryConsumeRound())
                {
                    _chamberState = ChamberState.ChamberedRound;
                }
                else
                {
                    _chamberState = ChamberState.Empty;
                }
            }
            else
            {
                _chamberState = ChamberState.Empty;
            }
        }

        public void RackSlide()
        {
            if (_slideHandle != null)
            {
                _slideHandle.QuickRack();
            }
            else
            {
                HandleSlideRetracted();
                HandleSlideForward();
            }
        }

        private void HandleSlideRetracted()
        {
            PlaySlideRackSound();

            // Jam clearing on slide retraction
            if (_chamberState == ChamberState.Jammed)
            {
                _chamberState = ChamberState.Empty;
                OnJamCleared?.Invoke();
            }
            else if (_chamberState == ChamberState.SpentCasing || _chamberState == ChamberState.ChamberedRound)
            {
                _chamberState = ChamberState.Empty;
            }
        }

        private void HandleSlideForward()
        {
            // Strip new round into chamber if available
            CycleActionFromMagazine();
        }

        private void UpdateMagazineInsertion()
        {
            if (!_isInsertingMagazine || _currentInsertingMag == null) return;

            _insertTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_insertTimer / Mathf.Max(_insertDuration, 0.001f));

            // Punchy easing curve: smooth acceleration, sharp lock-in
            float curve = t * t * (3f - 2f * t);

            _currentInsertingMag.transform.localPosition = Vector3.Lerp(_insertStartLocalPos, _insertTargetLocalPos, curve);
            _currentInsertingMag.transform.localRotation = Quaternion.Slerp(_insertStartLocalRot, Quaternion.identity, curve);

            if (t >= 1.0f)
            {
                _isInsertingMagazine = false;
                _currentInsertingMag.transform.localPosition = _insertTargetLocalPos;
                _currentInsertingMag.transform.localRotation = Quaternion.identity;

                Transform targetParent = _magazineWellTransform != null ? _magazineWellTransform : transform;
                _currentInsertingMag.SetSeated(true, targetParent, _insertTargetLocalPos);

                PlayMagazineInsertSound();
                OnMagazineInserted?.Invoke();

                // Realistic tactile palm slap bump (weapon jolts upward and forward from slap)
                _recoilOffset += new Vector3(0f, 0.016f, 0.008f);
                _recoilRotationEuler += new Vector3(3.0f, 0f, 0f);

                // Controller haptics
                if (_inputProvider != null && _grabbable != null && _grabbable.CurrentGrabbingHand.HasValue)
                {
                    _inputProvider.TriggerHaptic(_grabbable.CurrentGrabbingHand.Value, 0.75f, 0.08f);
                }

                // Auto-chamber round 1 if empty or spent so weapon is ready to fire
                if (_chamberState == ChamberState.Empty || _chamberState == ChamberState.SpentCasing)
                {
                    CycleActionFromMagazine();
                    PlaySlideRackSound();
                }

                EnsureNetworkSession();
                if (_networkSession != null && _networkSession.IsActive)
                {
                    ulong shooterId = _networkSession.LocalClientId;
                    WeaponType wt = _definition != null ? _definition.Type : WeaponType.Pistol;
                    _networkSession.BroadcastMagazineInserted(shooterId, wt);
                }
            }
        }

        public void InsertMagazine(Magazine mag)
        {
            InsertMagazine(mag, true);
        }

        public void InsertMagazine(Magazine mag, bool animate)
        {
            if (mag == null) return;
            if (_insertedMagazine == mag && !_isInsertingMagazine) return;

            // Auto-eject old magazine if still present and not already ejected
            if (_insertedMagazine != null && _insertedMagazine != mag)
            {
                EjectMagazine();
            }

            _insertedMagazine = mag;
            _currentInsertingMag = mag;

            Transform targetParent = _magazineWellTransform != null ? _magazineWellTransform : transform;
            Vector3 seatedLocalPos = _definition != null && _definition.Type == WeaponType.Rifle
                ? new Vector3(0f, -0.06f, 0f)
                : new Vector3(0f, -0.02f, 0f);

            _insertTargetLocalPos = seatedLocalPos;

            if (mag.transform.parent != targetParent)
            {
                mag.transform.SetParent(targetParent, false);
            }

            var rb = mag.Rigidbody != null ? mag.Rigidbody : mag.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.interpolation = RigidbodyInterpolation.None;
                rb.isKinematic = true;
                rb.detectCollisions = false;
            }

            var cols = mag.GetComponentsInChildren<Collider>();
            foreach (var c in cols) c.enabled = false;

            if (animate)
            {
                _isInsertingMagazine = true;
                _insertTimer = 0f;
                _insertDuration = _definition != null && _definition.Type == WeaponType.Rifle ? 0.30f : 0.25f;

                Vector3 entryOffset = _definition != null && _definition.Type == WeaponType.Rifle
                    ? new Vector3(0f, -0.22f, -0.03f)
                    : new Vector3(0f, -0.15f, 0.02f);

                _insertStartLocalPos = seatedLocalPos + entryOffset;
                _insertStartLocalRot = Quaternion.Euler(_definition != null && _definition.Type == WeaponType.Rifle ? -6f : 4f, 0f, 0f);

                mag.transform.localPosition = _insertStartLocalPos;
                mag.transform.localRotation = _insertStartLocalRot;

                PlayMagazineGuideSound();
            }
            else
            {
                _isInsertingMagazine = false;
                mag.transform.localPosition = seatedLocalPos;
                mag.transform.localRotation = Quaternion.identity;
                mag.SetSeated(true, targetParent, seatedLocalPos);

                PlayMagazineInsertSound();
                OnMagazineInserted?.Invoke();

                if (_chamberState == ChamberState.Empty || _chamberState == ChamberState.SpentCasing)
                {
                    CycleActionFromMagazine();
                }
            }
        }

        public bool TryInsertMagazine(IGrabbable magazine)
        {
            if (magazine == null) return false;
            var mag = magazine.Transform.GetComponent<Magazine>() ?? magazine.Transform.GetComponentInParent<Magazine>();
            if (mag == null) return false;

            if (_definition != null && mag.CompatibleWeaponType != _definition.Type)
            {
                return false;
            }

            // Release if currently held
            if (mag.IsGrabbed && mag.CurrentGrabbingHand.HasValue)
            {
                mag.OnReleased(mag.CurrentGrabbingHand.Value, Vector3.zero, Vector3.zero);
            }

            // Insert new magazine with visible insertion animation!
            InsertMagazine(mag, true);

            return true;
        }

        public void Reload()
        {
            int maxCap = _definition != null ? _definition.MagazineCapacity : 15;
            if (_insertedMagazine != null && _insertedMagazine.CurrentAmmo >= maxCap && _chamberState == ChamberState.ChamberedRound)
            {
                return;
            }

            EnsureNetworkSession();
            if (_networkSession != null && _networkSession.IsActive)
            {
                ulong shooterId = _networkSession.LocalClientId;
                WeaponType wt = _definition != null ? _definition.Type : WeaponType.Pistol;
                _networkSession.BroadcastReloadStarted(shooterId, wt);
            }

            if (_reloadRoutine != null)
            {
                StopCoroutine(_reloadRoutine);
            }
            _reloadRoutine = StartCoroutine(TacticalReloadRoutine());
        }

        private IEnumerator TacticalReloadRoutine()
        {
            // Step 1: Eject old magazine smoothly (falls to the floor under physics)
            if (_insertedMagazine != null)
            {
                EjectMagazine();
            }

            // Realistic off-hand reach pause: operator reaches down to hip tactical pouch
            yield return new WaitForSeconds(0.20f);

            // Step 2: Spawn fresh, full magazine with brass bullet top
            var newMag = CreateDefaultMagazine();

            // Step 3: Insert magazine with visible slide-in animation and slap lock!
            InsertMagazine(newMag, true);

            _reloadRoutine = null;
        }

        private Magazine CreateDefaultMagazine()
        {
            var magObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            magObj.name = _definition != null && _definition.Type == WeaponType.Rifle ? "RifleMagazine" : "PistolMagazine";

            // Immediately disable the collider so it doesn't trigger MagazineWell during spawn
            var col = magObj.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            Transform targetParent = _magazineWellTransform != null ? _magazineWellTransform : transform;
            magObj.transform.SetParent(targetParent, false);

            Vector3 seatedLocalPos = _definition != null && _definition.Type == WeaponType.Rifle
                ? new Vector3(0f, -0.06f, 0f)
                : new Vector3(0f, -0.02f, 0f);

            magObj.transform.localPosition = seatedLocalPos;
            magObj.transform.localRotation = Quaternion.identity;

            if (_definition != null && _definition.Type == WeaponType.Rifle)
            {
                magObj.transform.localScale = new Vector3(0.034f, 0.18f, 0.08f);
            }
            else
            {
                magObj.transform.localScale = new Vector3(0.028f, 0.11f, 0.045f);
            }

            // Apply magazine material
            Material magMat = _magazineMaterial;
            if (magMat == null)
            {
                var rend = GetComponentInChildren<Renderer>();
                if (rend != null) magMat = rend.sharedMaterial;
            }
            if (magMat != null)
            {
                magObj.GetComponent<Renderer>().sharedMaterial = magMat;
            }

            var rb = magObj.AddComponent<Rigidbody>();
            rb.mass = _definition != null && _definition.Type == WeaponType.Rifle ? 0.45f : 0.25f;
            rb.isKinematic = true;
            rb.detectCollisions = false;
            rb.interpolation = RigidbodyInterpolation.None;

            var mag = magObj.AddComponent<Magazine>();
            int cap = _definition != null ? _definition.MagazineCapacity : 15;
            WeaponType wt = _definition != null ? _definition.Type : WeaponType.Pistol;
            mag.Initialize(cap, wt);
            magObj.AddComponent<GrabPoint>();

            // Polished brass bullet visible at top of magazine before insertion
            var brassTip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            brassTip.name = "BrassBulletTip";
            brassTip.transform.SetParent(magObj.transform, false);
            brassTip.transform.localPosition = new Vector3(0f, 0.52f, 0.15f);
            brassTip.transform.localScale = new Vector3(0.55f, 0.25f, 0.65f);
            var colBrass = brassTip.GetComponent<Collider>();
            if (colBrass != null) Destroy(colBrass);

            if (_brassMaterial != null)
            {
                brassTip.GetComponent<Renderer>().sharedMaterial = _brassMaterial;
            }

            return mag;
        }

        public void EjectMagazine()
        {
            if (_insertedMagazine == null) return;

            var mag = _insertedMagazine;
            _insertedMagazine = null;
            _lastEjectedMagazine = mag;
            _lastEjectTime = Time.time;

            mag.transform.SetParent(null);
            mag.SetSeated(false);

            // Re-enable colliders on ejected magazine, but ignore collision with weapon
            var weaponColliders = GetComponentsInChildren<Collider>();
            var magColliders = mag.GetComponentsInChildren<Collider>();
            foreach (var magCol in magColliders)
            {
                magCol.enabled = true;
                foreach (var wepCol in weaponColliders)
                {
                    Physics.IgnoreCollision(magCol, wepCol, true);
                }
            }

            var rb = mag.Rigidbody != null ? mag.Rigidbody : mag.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.detectCollisions = true;
                rb.isKinematic = false;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = -transform.up * 1.2f + transform.forward * 0.15f;
                    rb.angularVelocity = transform.right * 1.5f;
                }
            }

            PlayMagazineEjectSound();
            OnMagazineEjected?.Invoke();

            EnsureNetworkSession();
            if (_networkSession != null && _networkSession.IsActive)
            {
                ulong shooterId = _networkSession.LocalClientId;
                Vector3 ejectPos = _magazineWellTransform != null ? _magazineWellTransform.position : transform.position;
                Quaternion ejectRot = _magazineWellTransform != null ? _magazineWellTransform.rotation : transform.rotation;
                Vector3 ejectVel = -transform.up * 1.2f + transform.forward * 0.15f;
                Vector3 ejectAngVel = transform.right * 1.5f;
                WeaponType wt = _definition != null ? _definition.Type : WeaponType.Pistol;
                _networkSession.BroadcastMagazineEjected(shooterId, wt, ejectPos, ejectRot, ejectVel, ejectAngVel);
            }

            // Destroy ejected empty magazine after 8 seconds so scene stays clean
            Destroy(mag.gameObject, 8.0f);
        }

        public void CycleFireMode()
        {
            if (_definition == null || _definition.SupportedFireModes.Length <= 1) return;

            _fireModeIndex = (_fireModeIndex + 1) % _definition.SupportedFireModes.Length;
            _currentFireMode = _definition.SupportedFireModes[_fireModeIndex];
            OnFireModeSwitched?.Invoke(_currentFireMode);
        }

        private void PlayFireSound()
        {
            AudioClip clip = _definition != null ? _definition.FireSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetGunshotClip(_definition != null ? _definition.WeaponName : gameObject.name, _definition != null && _definition.Type == WeaponType.Rifle);
            }
            PlaySound(clip);
        }

        private void PlayDryFireSound()
        {
            AudioClip clip = _definition != null ? _definition.DryFireSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetDryFireClip();
            }
            PlaySound(clip);
        }

        private void PlayJamSound()
        {
            AudioClip clip = _definition != null ? _definition.JamSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetDryFireClip();
            }
            PlaySound(clip);
        }

        private void PlaySlideRackSound()
        {
            AudioClip clip = _definition != null ? _definition.SlideRackSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetSlideRackClip();
            }
            PlaySound(clip);
        }

        private void PlayMagazineInsertSound()
        {
            AudioClip clip = _definition != null ? _definition.MagazineInsertSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetMagInsertClip();
            }
            PlaySound(clip);
        }

        private void PlayMagazineGuideSound()
        {
            AudioClip clip = ProceduralAudioSynthesizer.GetMagGuideClip();
            PlaySound(clip);
        }

        private void PlayMagazineEjectSound()
        {
            AudioClip clip = _definition != null ? _definition.MagazineEjectSound : null;
            if (clip == null)
            {
                clip = ProceduralAudioSynthesizer.GetMagEjectClip();
            }
            PlaySound(clip);
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(clip);
            }
        }
    }
}

