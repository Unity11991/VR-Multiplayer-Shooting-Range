using System;
using System.Collections;
using UnityEngine;
using Unity.Netcode;
using ShootingRange.Core;
using ShootingRange.Weapons;

namespace ShootingRange.Multiplayer
{
    /// <summary>
    /// Synchronizes VR and Desktop player avatars across LAN using Netcode for GameObjects.
    /// Provides head, torso, and hand tracking, visual nametags, held weapon models (pistol/rifle),
    /// left/right handedness switching, magazine reload/ejection synchronization,
    /// and muzzle flash synchronization so remote players are clearly visible.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkPlayerAvatar : NetworkBehaviour
    {
        [Header("Avatar Transforms")]
        [SerializeField] private Transform _headVisual;
        [SerializeField] private Transform _torsoVisual;
        [SerializeField] private Transform _leftHandVisual;
        [SerializeField] private Transform _rightHandVisual;
        [SerializeField] private Renderer _visorRenderer;
        [SerializeField] private TextMesh _nametagText;

        [Header("Weapon Models & Magazines")]
        [SerializeField] private GameObject _pistolVisual;
        [SerializeField] private GameObject _rifleVisual;
        [SerializeField] private Transform _pistolMagazine;
        [SerializeField] private Transform _rifleMagazine;
        [SerializeField] private Light _muzzleFlashLight;
        [SerializeField] private GameObject _muzzleFlashFlare;

        // Synchronized held weapon type: 0 = None, 1 = Pistol, 2 = Rifle
        private readonly NetworkVariable<int> _heldWeaponType = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        // Synchronized held weapon hand: 0 = Right Hand, 1 = Left Hand
        private readonly NetworkVariable<int> _heldWeaponHand = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private ITrackingProvider _localTracking;
        private float _sendTimer;
        private const float NetworkUpdateInterval = 0.033f; // ~30 Hz network sync rate

        // Interpolation targets for remote player
        private Vector3 _targetHeadPos;
        private Quaternion _targetHeadRot = Quaternion.identity;
        private Vector3 _targetLeftHandPos;
        private Quaternion _targetLeftHandRot = Quaternion.identity;
        private Vector3 _targetRightHandPos;
        private Quaternion _targetRightHandRot = Quaternion.identity;
        private int _remoteHeldWeaponType = 0;
        private bool _remoteHeldInLeftHand = false;
        private float _muzzleFlashTimer = 0f;
        private Coroutine _remoteReloadRoutine;

        private void Awake()
        {
            EnsureVisuals();
        }

        public override void OnNetworkSpawn()
        {
            EnsureVisuals();

            // Cache magazine references if not linked
            if (_rifleMagazine == null && _rifleVisual != null) _rifleMagazine = _rifleVisual.transform.Find("Magazine");
            if (_pistolMagazine == null && _pistolVisual != null) _pistolMagazine = _pistolVisual.transform.Find("Magazine") ?? _pistolVisual.transform.Find("PistolMagazine");

            if (_pistolMagazine == null && _pistolVisual != null)
            {
                var pMag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pMag.name = "Magazine";
                pMag.transform.SetParent(_pistolVisual.transform, false);
                pMag.transform.localPosition = new Vector3(0f, -0.09f, -0.035f);
                pMag.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                pMag.transform.localScale = new Vector3(0.028f, 0.04f, 0.045f);
                DestroyCollider(pMag);
                var rend = pMag.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = LoadMaterial("Mat_TacticalPolymer");
                _pistolMagazine = pMag.transform;
            }

            _heldWeaponType.OnValueChanged += HandleHeldWeaponChanged;
            _heldWeaponHand.OnValueChanged += HandleHeldWeaponHandChanged;

            if (IsOwner)
            {
                // Hide local head, torso, and hand visuals so they do not block first-person camera or duplicate local hands
                if (_headVisual != null) _headVisual.gameObject.SetActive(false);
                if (_torsoVisual != null) _torsoVisual.gameObject.SetActive(false);
                if (_leftHandVisual != null) _leftHandVisual.gameObject.SetActive(false);
                if (_rightHandVisual != null) _rightHandVisual.gameObject.SetActive(false);

                ResolveLocalTracking();

                // Teleport local player camera & tracking to assigned discrete lane spawn point
                Vector3 spawnPos = SpawnPointDefinitions.GetSpawnPosition(OwnerClientId);
                Quaternion spawnRot = SpawnPointDefinitions.GetSpawnRotation(OwnerClientId);

                if (_localTracking != null)
                {
                    _localTracking.Teleport(spawnPos, spawnRot);
                }
                else
                {
                    var allTrackers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                    foreach (var m in allTrackers)
                    {
                        if (m is ITrackingProvider tp)
                        {
                            _localTracking = tp;
                            tp.Teleport(spawnPos, spawnRot);
                            break;
                        }
                    }
                }
            }
            else
            {
                // Remote player is fully visible
                if (_headVisual != null) _headVisual.gameObject.SetActive(true);
                if (_torsoVisual != null) _torsoVisual.gameObject.SetActive(true);
                if (_leftHandVisual != null) _leftHandVisual.gameObject.SetActive(true);
                if (_rightHandVisual != null) _rightHandVisual.gameObject.SetActive(true);

                // Customize nametag and visor based on ClientId and assigned Lane
                ulong clientId = OwnerClientId;
                bool isHostPlayer = clientId == 0;
                string laneName = SpawnPointDefinitions.GetLaneName(clientId);
                string label = isHostPlayer ? $"PLAYER 1 [HOST - {laneName}]" : $"PLAYER {clientId + 1} [GUEST - {laneName}]";
                if (_nametagText != null)
                {
                    _nametagText.text = label;
                    _nametagText.color = isHostPlayer ? new Color(0.2f, 0.9f, 1.0f) : new Color(1.0f, 0.7f, 0.2f);
                }

                if (_visorRenderer != null && _visorRenderer.material != null)
                {
                    _visorRenderer.material.color = isHostPlayer ? new Color(0f, 1f, 1f) : new Color(1f, 0.6f, 0.1f);
                }

                // Initial position at designated spawn point before receiving network frames
                Vector3 spawnPos = SpawnPointDefinitions.GetSpawnPosition(clientId);
                Quaternion spawnRot = SpawnPointDefinitions.GetSpawnRotation(clientId);
                transform.position = spawnPos;
                transform.rotation = spawnRot;
                _targetHeadPos = spawnPos + Vector3.up * 1.7f;
                _targetHeadRot = spawnRot;
                if (_headVisual != null)
                {
                    _headVisual.position = _targetHeadPos;
                    _headVisual.rotation = _targetHeadRot;
                }

                _remoteHeldInLeftHand = _heldWeaponHand.Value == 1;
                UpdateWeaponAttachmentAndVisibility(_heldWeaponType.Value, _remoteHeldInLeftHand);
            }
        }

        public override void OnNetworkDespawn()
        {
            _heldWeaponType.OnValueChanged -= HandleHeldWeaponChanged;
            _heldWeaponHand.OnValueChanged -= HandleHeldWeaponHandChanged;
        }

        private void HandleHeldWeaponChanged(int previous, int current)
        {
            if (!IsOwner)
            {
                _remoteHeldWeaponType = current;
                UpdateWeaponAttachmentAndVisibility(current, _remoteHeldInLeftHand);
            }
        }

        private void HandleHeldWeaponHandChanged(int previous, int current)
        {
            if (!IsOwner)
            {
                _remoteHeldInLeftHand = current == 1;
                UpdateWeaponAttachmentAndVisibility(_remoteHeldWeaponType, _remoteHeldInLeftHand);
            }
        }

        private void ResolveLocalTracking()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _localTracking);
            }

            if (_localTracking == null)
            {
                var allTrackers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var m in allTrackers)
                {
                    if (m is ITrackingProvider tp)
                    {
                        _localTracking = tp;
                        break;
                    }
                }
            }
        }

        private void Update()
        {
            if (IsOwner)
            {
                // Authoritative local weapon tracking & handedness
                var (currentWeapon, isLeftHand) = DetectLocalHeldWeapon();
                int currentHandInt = isLeftHand ? 1 : 0;
                if (currentWeapon != _heldWeaponType.Value || currentHandInt != _heldWeaponHand.Value)
                {
                    if (IsServer)
                    {
                        _heldWeaponType.Value = currentWeapon;
                        _heldWeaponHand.Value = currentHandInt;
                    }
                    else
                    {
                        SetHeldWeaponServerRpc(currentWeapon, currentHandInt);
                    }
                }

                // Rate-limited network pose updates (30 Hz)
                _sendTimer += Time.deltaTime;
                if (_sendTimer >= NetworkUpdateInterval)
                {
                    _sendTimer = 0f;
                    SampleAndSendLocalPose(currentWeapon, isLeftHand);
                }
            }
            else
            {
                // Smooth interpolation for remote player avatar
                InterpolateRemoteAvatar();
            }

            // Muzzle flash light & flare decay
            if (_muzzleFlashTimer > 0f)
            {
                _muzzleFlashTimer -= Time.deltaTime;
                if (_muzzleFlashTimer <= 0f)
                {
                    if (_muzzleFlashLight != null) _muzzleFlashLight.enabled = false;
                    if (_muzzleFlashFlare != null) _muzzleFlashFlare.SetActive(false);
                }
            }
        }

        [ServerRpc]
        private void SetHeldWeaponServerRpc(int type, int hand)
        {
            _heldWeaponType.Value = type;
            _heldWeaponHand.Value = hand;
        }

        private void SampleAndSendLocalPose(int currentWeapon, bool isLeftHand)
        {
            if (_localTracking == null)
            {
                ResolveLocalTracking();
            }

            Vector3 headPos = _localTracking != null ? _localTracking.HeadPosition : transform.position;
            Quaternion headRot = _localTracking != null ? _localTracking.HeadRotation : transform.rotation;
            Vector3 leftHandPos = _localTracking != null ? _localTracking.GetHandPosition(Handedness.Left) : headPos + new Vector3(-0.25f, -0.4f, 0.3f);
            Quaternion leftHandRot = _localTracking != null ? _localTracking.GetHandRotation(Handedness.Left) : headRot;
            Vector3 rightHandPos = _localTracking != null ? _localTracking.GetHandPosition(Handedness.Right) : headPos + new Vector3(0.25f, -0.4f, 0.3f);
            Quaternion rightHandRot = _localTracking != null ? _localTracking.GetHandRotation(Handedness.Right) : headRot;

            UpdatePoseServerRpc(headPos, headRot, leftHandPos, leftHandRot, rightHandPos, rightHandRot, currentWeapon, isLeftHand);
        }

        private (int weaponType, bool isLeftHand) DetectLocalHeldWeapon()
        {
            // 1. Direct IHandInteractor check from ShootingRange.Core (clean interface decoupling!)
            var monobehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in monobehaviours)
            {
                if (mb is IHandInteractor interactor && interactor.HeldObject != null)
                {
                    var weapon = (interactor.HeldObject as IWeaponInteraction)
                              ?? interactor.HeldObject.Transform.GetComponent<IWeaponInteraction>()
                              ?? interactor.HeldObject.Transform.GetComponentInParent<IWeaponInteraction>();
                    if (weapon != null)
                    {
                        int wt = weapon.Type == WeaponType.Rifle ? 2 : 1;
                        bool isLeft = interactor.Hand == Handedness.Left;
                        return (wt, isLeft);
                    }
                }
            }

            // 2. Fallback across all active WeaponBase instances (from ShootingRange.Weapons)
            var weapons = FindObjectsByType<WeaponBase>(FindObjectsSortMode.None);
            foreach (var w in weapons)
            {
                if (w != null && w.IsGrabbed)
                {
                    int wt = w.Type == WeaponType.Rifle ? 2 : 1;
                    bool isLeft = w.CurrentGrabbingHand == Handedness.Left;
                    return (wt, isLeft);
                }
            }

            return (0, false); // None
        }

        [ServerRpc]
        private void UpdatePoseServerRpc(
            Vector3 headPos, Quaternion headRot,
            Vector3 leftHandPos, Quaternion leftHandRot,
            Vector3 rightHandPos, Quaternion rightHandRot,
            int heldWeaponType,
            bool heldInLeftHand)
        {
            UpdatePoseClientRpc(headPos, headRot, leftHandPos, leftHandRot, rightHandPos, rightHandRot, heldWeaponType, heldInLeftHand);
        }

        [ClientRpc]
        private void UpdatePoseClientRpc(
            Vector3 headPos, Quaternion headRot,
            Vector3 leftHandPos, Quaternion leftHandRot,
            Vector3 rightHandPos, Quaternion rightHandRot,
            int heldWeaponType,
            bool heldInLeftHand)
        {
            if (IsOwner) return;

            _targetHeadPos = headPos;
            _targetHeadRot = headRot;
            _targetLeftHandPos = leftHandPos;
            _targetLeftHandRot = leftHandRot;
            _targetRightHandPos = rightHandPos;
            _targetRightHandRot = rightHandRot;
            _remoteHeldWeaponType = heldWeaponType;
            _remoteHeldInLeftHand = heldInLeftHand;

            UpdateWeaponAttachmentAndVisibility(heldWeaponType, heldInLeftHand);
        }

        private void InterpolateRemoteAvatar()
        {
            float dt = Time.deltaTime;

            if (_headVisual != null && _targetHeadPos != Vector3.zero)
            {
                _headVisual.position = Vector3.Lerp(_headVisual.position, _targetHeadPos, dt * 24f);
                _headVisual.rotation = Quaternion.Slerp(_headVisual.rotation, _targetHeadRot, dt * 24f);

                // Billboard nametag to face local camera
                if (_nametagText != null && Camera.main != null)
                {
                    _nametagText.transform.rotation = Quaternion.LookRotation(_nametagText.transform.position - Camera.main.transform.position);
                }
            }

            if (_torsoVisual != null && _targetHeadPos != Vector3.zero)
            {
                Vector3 torsoPos = _targetHeadPos - Vector3.up * 0.42f;
                _torsoVisual.position = Vector3.Lerp(_torsoVisual.position, torsoPos, dt * 22f);
                _torsoVisual.rotation = Quaternion.Euler(0f, _headVisual != null ? _headVisual.eulerAngles.y : 0f, 0f);
            }

            if (_leftHandVisual != null && _targetLeftHandPos != Vector3.zero)
            {
                _leftHandVisual.position = Vector3.Lerp(_leftHandVisual.position, _targetLeftHandPos, dt * 28f);
                _leftHandVisual.rotation = Quaternion.Slerp(_leftHandVisual.rotation, _targetLeftHandRot, dt * 28f);
            }

            if (_rightHandVisual != null && _targetRightHandPos != Vector3.zero)
            {
                _rightHandVisual.position = Vector3.Lerp(_rightHandVisual.position, _targetRightHandPos, dt * 28f);
                _rightHandVisual.rotation = Quaternion.Slerp(_rightHandVisual.rotation, _targetRightHandRot, dt * 28f);
            }

            int activeType = _remoteHeldWeaponType > 0 ? _remoteHeldWeaponType : _heldWeaponType.Value;
            bool isLeftHand = _remoteHeldInLeftHand || (_heldWeaponHand.Value == 1);
            UpdateWeaponAttachmentAndVisibility(activeType, isLeftHand);
        }

        public void UpdateWeaponAttachmentAndVisibility(int weaponType, bool isLeftHand)
        {
            if (IsOwner) return; // Never show on local player camera

            Transform targetHand = isLeftHand ? _leftHandVisual : _rightHandVisual;
            if (targetHand != null)
            {
                if (_pistolVisual != null && _pistolVisual.transform.parent != targetHand)
                {
                    _pistolVisual.transform.SetParent(targetHand, false);
                    _pistolVisual.transform.localPosition = new Vector3(0f, -0.02f, 0.06f);
                    _pistolVisual.transform.localRotation = Quaternion.identity;
                }

                if (_rifleVisual != null && _rifleVisual.transform.parent != targetHand)
                {
                    _rifleVisual.transform.SetParent(targetHand, false);
                    _rifleVisual.transform.localPosition = new Vector3(0f, -0.02f, 0.10f);
                    _rifleVisual.transform.localRotation = Quaternion.identity;
                }

                if (_muzzleFlashLight != null && _muzzleFlashLight.transform.parent != targetHand)
                {
                    _muzzleFlashLight.transform.SetParent(targetHand, false);
                }

                if (_muzzleFlashFlare != null && _muzzleFlashFlare.transform.parent != targetHand)
                {
                    _muzzleFlashFlare.transform.SetParent(targetHand, false);
                }
            }

            bool showPistol = weaponType == 1;
            bool showRifle = weaponType == 2;

            if (_pistolVisual != null && _pistolVisual.activeSelf != showPistol)
            {
                _pistolVisual.SetActive(showPistol);
            }

            if (_rifleVisual != null && _rifleVisual.activeSelf != showRifle)
            {
                _rifleVisual.SetActive(showRifle);
            }
        }

        public void OnRemoteMagazineEjected(WeaponType weaponType)
        {
            if (IsOwner) return;

            if (weaponType == WeaponType.Rifle && _rifleMagazine != null)
            {
                _rifleMagazine.gameObject.SetActive(false);
            }
            else if (weaponType == WeaponType.Pistol && _pistolMagazine != null)
            {
                _pistolMagazine.gameObject.SetActive(false);
            }
        }

        public void OnRemoteReloadStarted(WeaponType weaponType)
        {
            if (IsOwner) return;

            if (_remoteReloadRoutine != null)
            {
                StopCoroutine(_remoteReloadRoutine);
            }
            _remoteReloadRoutine = StartCoroutine(RemoteMagazineSlideRoutine(weaponType));
        }

        private IEnumerator RemoteMagazineSlideRoutine(WeaponType weaponType)
        {
            Transform mag = weaponType == WeaponType.Rifle ? _rifleMagazine : _pistolMagazine;
            if (mag == null)
            {
                _remoteReloadRoutine = null;
                yield break;
            }

            Vector3 targetPos = weaponType == WeaponType.Rifle
                ? new Vector3(0f, -0.06f, 0.04f)
                : new Vector3(0f, -0.09f, -0.035f);

            Vector3 startPos = targetPos - mag.up * 0.22f;

            mag.gameObject.SetActive(true);
            mag.localPosition = startPos;

            // Play 3D insertion guide audio at weapon position
            var guideClip = ProceduralAudioSynthesizer.GetMagGuideClip();
            if (guideClip != null)
            {
                AudioSource.PlayClipAtPoint(guideClip, mag.position, 0.8f);
            }

            float elapsed = 0f;
            float duration = 0.55f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float curve = Mathf.SmoothStep(0f, 1f, t);
                mag.localPosition = Vector3.Lerp(startPos, targetPos, curve);
                yield return null;
            }

            mag.localPosition = targetPos;

            // Play 3D mag seat / lock click audio
            var seatClip = ProceduralAudioSynthesizer.GetMagInsertClip();
            if (seatClip != null)
            {
                AudioSource.PlayClipAtPoint(seatClip, mag.position, 0.9f);
            }

            _remoteReloadRoutine = null;
        }

        public void OnRemoteMagazineInserted(WeaponType weaponType)
        {
            if (IsOwner) return;

            if (_remoteReloadRoutine != null)
            {
                StopCoroutine(_remoteReloadRoutine);
                _remoteReloadRoutine = null;
            }

            Transform mag = weaponType == WeaponType.Rifle ? _rifleMagazine : _pistolMagazine;
            if (mag != null)
            {
                mag.gameObject.SetActive(true);
                mag.localPosition = weaponType == WeaponType.Rifle
                    ? new Vector3(0f, -0.06f, 0.04f)
                    : new Vector3(0f, -0.09f, -0.035f);

                var seatClip = ProceduralAudioSynthesizer.GetMagInsertClip();
                if (seatClip != null)
                {
                    AudioSource.PlayClipAtPoint(seatClip, mag.position, 0.9f);
                }
            }
        }

        public void OnRemoteHandSwitched(bool isLeftHand)
        {
            if (IsOwner) return;

            _remoteHeldInLeftHand = isLeftHand;
            int activeType = _remoteHeldWeaponType > 0 ? _remoteHeldWeaponType : _heldWeaponType.Value;
            UpdateWeaponAttachmentAndVisibility(activeType, isLeftHand);
        }

        public void FlashMuzzle()
        {
            int activeType = _remoteHeldWeaponType > 0 ? _remoteHeldWeaponType : _heldWeaponType.Value;

            Vector3 flashLocalPos = activeType == 2
                ? new Vector3(0f, 0.04f, 0.65f)
                : new Vector3(0f, 0.02f, 0.28f);

            if (_muzzleFlashLight != null)
            {
                _muzzleFlashLight.transform.localPosition = flashLocalPos;
                _muzzleFlashLight.enabled = true;
            }

            if (_muzzleFlashFlare != null)
            {
                _muzzleFlashFlare.transform.localPosition = flashLocalPos;
                _muzzleFlashFlare.SetActive(true);
            }

            _muzzleFlashTimer = 0.075f;
        }

        /// <summary>
        /// Procedurally guarantees that the avatar has complete, full-scale 3D geometry and weapon models.
        /// Unscaled hand roots guarantee weapon models are rendered at true 1:1 scale.
        /// </summary>
        public void EnsureVisuals(
            Material polymerMat = null,
            Material steelMat = null,
            Material tanMat = null,
            Material visorMat = null,
            Material redDotMat = null)
        {
            // Acquire materials from project palette if not provided
            if (polymerMat == null) polymerMat = LoadMaterial("Mat_TacticalPolymer");
            if (steelMat == null) steelMat = LoadMaterial("Mat_TacticalSteel");
            if (tanMat == null) tanMat = LoadMaterial("Mat_FDE_Tan");
            if (visorMat == null) visorMat = LoadMaterial("Mat_ScreenCyan");
            if (redDotMat == null) redDotMat = LoadMaterial("Mat_RedDotReticle");

            // 1. Head Visual Container (Scale 1, 1, 1)
            if (_headVisual == null)
            {
                var headObj = new GameObject("HeadVisual");
                headObj.transform.SetParent(transform, false);
                headObj.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                headObj.transform.localScale = Vector3.one;

                // VR Headset Helmet
                var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                helmet.name = "VRHelmet";
                helmet.transform.SetParent(headObj.transform, false);
                helmet.transform.localPosition = Vector3.zero;
                helmet.transform.localScale = new Vector3(0.24f, 0.26f, 0.24f);
                DestroyCollider(helmet);
                if (polymerMat != null) helmet.GetComponent<Renderer>().sharedMaterial = polymerMat;

                // Glowing Visor Strip
                var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visor.name = "VisorLens";
                visor.transform.SetParent(headObj.transform, false);
                visor.transform.localPosition = new Vector3(0f, 0.02f, 0.11f);
                visor.transform.localScale = new Vector3(0.18f, 0.08f, 0.05f);
                DestroyCollider(visor);
                _visorRenderer = visor.GetComponent<Renderer>();
                if (visorMat != null) _visorRenderer.sharedMaterial = visorMat;

                // Floating Nametag
                var nametagObj = new GameObject("Nametag");
                nametagObj.transform.SetParent(headObj.transform, false);
                nametagObj.transform.localPosition = new Vector3(0f, 0.28f, 0f);
                _nametagText = nametagObj.AddComponent<TextMesh>();
                _nametagText.characterSize = 0.035f;
                _nametagText.fontSize = 24;
                _nametagText.alignment = TextAlignment.Center;
                _nametagText.anchor = TextAnchor.MiddleCenter;
                _nametagText.text = "PLAYER";

                _headVisual = headObj.transform;
            }

            // 2. Torso Visual Container (Scale 1, 1, 1)
            if (_torsoVisual == null)
            {
                var torsoObj = new GameObject("TorsoVisual");
                torsoObj.transform.SetParent(transform, false);
                torsoObj.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                torsoObj.transform.localScale = Vector3.one;

                var chestRig = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chestRig.name = "TacticalChestRig";
                chestRig.transform.SetParent(torsoObj.transform, false);
                chestRig.transform.localPosition = Vector3.zero;
                chestRig.transform.localScale = new Vector3(0.34f, 0.44f, 0.22f);
                DestroyCollider(chestRig);
                if (polymerMat != null) chestRig.GetComponent<Renderer>().sharedMaterial = polymerMat;

                _torsoVisual = torsoObj.transform;
            }

            // 3. Left Hand Container (Scale 1, 1, 1)
            if (_leftHandVisual == null)
            {
                var lHandObj = new GameObject("LeftHandVisual");
                lHandObj.transform.SetParent(transform, false);
                lHandObj.transform.localPosition = new Vector3(-0.25f, 1.0f, 0.25f);
                lHandObj.transform.localScale = Vector3.one;

                var lGlove = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lGlove.name = "LeftHandGlove";
                lGlove.transform.SetParent(lHandObj.transform, false);
                lGlove.transform.localPosition = Vector3.zero;
                lGlove.transform.localScale = new Vector3(0.08f, 0.08f, 0.10f);
                DestroyCollider(lGlove);
                if (polymerMat != null) lGlove.GetComponent<Renderer>().sharedMaterial = polymerMat;

                _leftHandVisual = lHandObj.transform;
            }

            // 4. Right Hand Container (Scale 1, 1, 1 - houses glove and 1:1 weapon models)
            if (_rightHandVisual == null)
            {
                var rHandObj = new GameObject("RightHandVisual");
                rHandObj.transform.SetParent(transform, false);
                rHandObj.transform.localPosition = new Vector3(0.25f, 1.0f, 0.25f);
                rHandObj.transform.localScale = Vector3.one;

                // Tactical glove
                var rGlove = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rGlove.name = "RightHandGlove";
                rGlove.transform.SetParent(rHandObj.transform, false);
                rGlove.transform.localPosition = Vector3.zero;
                rGlove.transform.localScale = new Vector3(0.08f, 0.08f, 0.10f);
                DestroyCollider(rGlove);
                if (polymerMat != null) rGlove.GetComponent<Renderer>().sharedMaterial = polymerMat;

                // ===================================
                // FULL-SCALE 1:1 PISTOL VISUAL MODEL
                // ===================================
                _pistolVisual = new GameObject("PistolVisual");
                _pistolVisual.transform.SetParent(rHandObj.transform, false);
                _pistolVisual.transform.localPosition = new Vector3(0f, -0.02f, 0.06f);
                _pistolVisual.transform.localRotation = Quaternion.identity;
                _pistolVisual.transform.localScale = Vector3.one;

                var pSlide = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pSlide.name = "Slide";
                pSlide.transform.SetParent(_pistolVisual.transform, false);
                pSlide.transform.localPosition = new Vector3(0f, 0.04f, 0.03f);
                pSlide.transform.localScale = new Vector3(0.034f, 0.036f, 0.19f);
                DestroyCollider(pSlide);
                if (steelMat != null) pSlide.GetComponent<Renderer>().sharedMaterial = steelMat;

                var pGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pGrip.name = "Grip";
                pGrip.transform.SetParent(_pistolVisual.transform, false);
                pGrip.transform.localPosition = new Vector3(0f, -0.03f, -0.02f);
                pGrip.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                pGrip.transform.localScale = new Vector3(0.032f, 0.12f, 0.055f);
                DestroyCollider(pGrip);
                if (polymerMat != null) pGrip.GetComponent<Renderer>().sharedMaterial = polymerMat;

                var pMag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pMag.name = "Magazine";
                pMag.transform.SetParent(_pistolVisual.transform, false);
                pMag.transform.localPosition = new Vector3(0f, -0.09f, -0.035f);
                pMag.transform.localRotation = Quaternion.Euler(15f, 0f, 0f);
                pMag.transform.localScale = new Vector3(0.028f, 0.04f, 0.045f);
                DestroyCollider(pMag);
                if (polymerMat != null) pMag.GetComponent<Renderer>().sharedMaterial = polymerMat;
                _pistolMagazine = pMag.transform;

                _pistolVisual.SetActive(false);

                // ===================================
                // FULL-SCALE 1:1 RIFLE VISUAL MODEL
                // ===================================
                _rifleVisual = new GameObject("RifleVisual");
                _rifleVisual.transform.SetParent(rHandObj.transform, false);
                _rifleVisual.transform.localPosition = new Vector3(0f, -0.02f, 0.10f);
                _rifleVisual.transform.localRotation = Quaternion.identity;
                _rifleVisual.transform.localScale = Vector3.one;

                // Receiver
                var rReceiver = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rReceiver.name = "Receiver";
                rReceiver.transform.SetParent(_rifleVisual.transform, false);
                rReceiver.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                rReceiver.transform.localScale = new Vector3(0.045f, 0.09f, 0.24f);
                DestroyCollider(rReceiver);
                if (steelMat != null) rReceiver.GetComponent<Renderer>().sharedMaterial = steelMat;

                // Handguard (FDE Tan)
                var rHandguard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rHandguard.name = "Handguard";
                rHandguard.transform.SetParent(_rifleVisual.transform, false);
                rHandguard.transform.localPosition = new Vector3(0f, 0.04f, 0.24f);
                rHandguard.transform.localScale = new Vector3(0.042f, 0.065f, 0.28f);
                DestroyCollider(rHandguard);
                if (tanMat != null) rHandguard.GetComponent<Renderer>().sharedMaterial = tanMat;

                // Barrel
                var rBarrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rBarrel.name = "Barrel";
                rBarrel.transform.SetParent(_rifleVisual.transform, false);
                rBarrel.transform.localPosition = new Vector3(0f, 0.04f, 0.44f);
                rBarrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                rBarrel.transform.localScale = new Vector3(0.018f, 0.16f, 0.018f);
                DestroyCollider(rBarrel);
                if (steelMat != null) rBarrel.GetComponent<Renderer>().sharedMaterial = steelMat;

                // Crane Stock
                var rStock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rStock.name = "Stock";
                rStock.transform.SetParent(_rifleVisual.transform, false);
                rStock.transform.localPosition = new Vector3(0f, 0.01f, -0.26f);
                rStock.transform.localScale = new Vector3(0.036f, 0.07f, 0.16f);
                DestroyCollider(rStock);
                if (polymerMat != null) rStock.GetComponent<Renderer>().sharedMaterial = polymerMat;

                // Pistol Grip
                var rGrip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rGrip.name = "PistolGrip";
                rGrip.transform.SetParent(_rifleVisual.transform, false);
                rGrip.transform.localPosition = new Vector3(0f, -0.05f, -0.06f);
                rGrip.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
                rGrip.transform.localScale = new Vector3(0.032f, 0.13f, 0.052f);
                DestroyCollider(rGrip);
                if (polymerMat != null) rGrip.GetComponent<Renderer>().sharedMaterial = polymerMat;

                // STANAG Magazine
                var rMag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rMag.name = "Magazine";
                rMag.transform.SetParent(_rifleVisual.transform, false);
                rMag.transform.localPosition = new Vector3(0f, -0.06f, 0.04f);
                rMag.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
                rMag.transform.localScale = new Vector3(0.034f, 0.18f, 0.08f);
                DestroyCollider(rMag);
                if (polymerMat != null) rMag.GetComponent<Renderer>().sharedMaterial = polymerMat;
                _rifleMagazine = rMag.transform;

                // Red Dot Sight Housing & Lens
                var rOptic = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rOptic.name = "OpticHousing";
                rOptic.transform.SetParent(_rifleVisual.transform, false);
                rOptic.transform.localPosition = new Vector3(0f, 0.11f, 0.02f);
                rOptic.transform.localScale = new Vector3(0.036f, 0.045f, 0.075f);
                DestroyCollider(rOptic);
                if (steelMat != null) rOptic.GetComponent<Renderer>().sharedMaterial = steelMat;

                var rDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rDot.name = "DotReticle";
                rDot.transform.SetParent(rOptic.transform, false);
                rDot.transform.localPosition = new Vector3(0f, 0f, 0.04f);
                rDot.transform.localScale = new Vector3(0.15f, 0.15f, 0.08f);
                DestroyCollider(rDot);
                if (redDotMat != null) rDot.GetComponent<Renderer>().sharedMaterial = redDotMat;

                _rifleVisual.SetActive(false);

                // Muzzle Flash Light
                var flashObj = new GameObject("MuzzleFlashLight");
                flashObj.transform.SetParent(rHandObj.transform, false);
                flashObj.transform.localPosition = new Vector3(0f, 0.04f, 0.65f);
                _muzzleFlashLight = flashObj.AddComponent<Light>();
                _muzzleFlashLight.type = LightType.Point;
                _muzzleFlashLight.color = new Color(1.0f, 0.85f, 0.35f);
                _muzzleFlashLight.range = 4.5f;
                _muzzleFlashLight.intensity = 3.0f;
                _muzzleFlashLight.enabled = false;

                // Visual Muzzle Flash Flare
                var flareObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                flareObj.name = "MuzzleFlashFlare";
                flareObj.transform.SetParent(rHandObj.transform, false);
                flareObj.transform.localPosition = new Vector3(0f, 0.04f, 0.65f);
                flareObj.transform.localScale = new Vector3(0.14f, 0.14f, 0.18f);
                DestroyCollider(flareObj);
                if (redDotMat != null) flareObj.GetComponent<Renderer>().sharedMaterial = redDotMat;
                flareObj.SetActive(false);
                _muzzleFlashFlare = flareObj;

                _rightHandVisual = rHandObj.transform;
            }
        }

        private static void DestroyCollider(GameObject obj)
        {
            var col = obj.GetComponent<Collider>();
            if (col == null) return;

            if (Application.isPlaying)
            {
                Destroy(col);
            }
            else
            {
                DestroyImmediate(col);
            }
        }

        private static Material LoadMaterial(string matName)
        {
#if UNITY_EDITOR
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>($"Assets/ShootingRange/Materials/{matName}.mat");
            if (mat != null) return mat;
#endif
            var loaded = Resources.Load<Material>(matName);
            if (loaded != null) return loaded;

            // Find existing shared material from scene renderers
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                if (r.sharedMaterial != null && r.sharedMaterial.name.Contains(matName.Replace("Mat_", "")))
                {
                    return r.sharedMaterial;
                }
            }

            return null;
        }
    }
}
