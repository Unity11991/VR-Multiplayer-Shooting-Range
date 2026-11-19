using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using UnityEngine.UI;
using ShootingRange.Core;

namespace ShootingRange.UI
{
    /// <summary>
    /// World-space VR LAN Multiplayer Lobby & Room Terminal.
    /// Fully usable in VR Headset (Meta Quest / OpenXR) with controller ray pointers,
    /// direct physical finger poke buttons, and in-headset floating summon.
    /// Features:
    /// - One-click Host (Server + Player 1)
    /// - In-headset VR Virtual Numpad & IP editor for entering Host IP without removing headset
    /// - Quick IP Presets (Localhost, My LAN IP, Octet Stepper +/- 1)
    /// - One-click Join (Player 2)
    /// - Real-time session stats: connection state, player count, ping (~80ms lag comp), spawn lane
    /// - Leave / Disconnect gracefully
    /// - In-Headset Floating Summon (Press Left Controller Y/Menu button or 'M' key)
    /// </summary>
    public class WorldSpaceNetworkUI : MonoBehaviour
    {
        [Header("LAN Configuration")]
        [SerializeField] private string _hostIp = "127.0.0.1";
        [SerializeField] private int _port = 7777;

        [Header("UI Text Displays")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _localIpText;
        [SerializeField] private Text _sessionStatsText;
        [SerializeField] private Text _hostIpDisplayText;
        [SerializeField] private Text _spawnLaneText;

        [Header("Primary Action Buttons")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _disconnectButton;
        [SerializeField] private Button _dockButton;

        [Header("Quick Preset Buttons")]
        [SerializeField] private Button _presetLocalhostBtn;
        [SerializeField] private Button _presetMyIpBtn;
        [SerializeField] private Button _stepOctetDownBtn;
        [SerializeField] private Button _stepOctetUpBtn;

        [Header("Physical Console Poke Buttons")]
        [SerializeField] private VRPokeInteractable _pokeHost;
        [SerializeField] private VRPokeInteractable _pokeJoin;
        [SerializeField] private VRPokeInteractable _pokeMyIp;
        [SerializeField] private VRPokeInteractable _pokeDisconnect;

        [Header("Kiosk Docking & In-Headset Floating Summon")]
        [SerializeField] private Transform _kioskAnchor;
        [SerializeField] private bool _isSummonedFloating = false;

        private INetworkSession _networkSession;
        private IControllerInputProvider _input;
        private string _localIpAddress = "127.0.0.1";
        private static readonly Vector3 s_DefaultStationaryPos = new Vector3(5.2f, 0f, 2.2f);
        private static readonly Quaternion s_DefaultStationaryRot = Quaternion.Euler(0f, -45f, 0f);

        public static WorldSpaceNetworkUI Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            _localIpAddress = DetectLocalLANIP();
            if (string.IsNullOrEmpty(_hostIp) || _hostIp == "127.0.0.1")
            {
                _hostIp = _localIpAddress;
            }

            if (_kioskAnchor == null)
            {
                _kioskAnchor = transform;
            }

            EnsureCanvasConstructed();
        }

        private void Start()
        {
            ResolveDependencies();
            BindButtons();
            UpdateDisplay();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (_networkSession != null)
            {
                _networkSession.OnConnectionStateChanged += UpdateDisplay;
                _networkSession.OnPlayerConnected += UpdateDisplay;
                _networkSession.OnPlayerDisconnected += UpdateDisplay;
            }
        }

        private void OnDisable()
        {
            if (_networkSession != null)
            {
                _networkSession.OnConnectionStateChanged -= UpdateDisplay;
                _networkSession.OnPlayerConnected -= UpdateDisplay;
                _networkSession.OnPlayerDisconnected -= UpdateDisplay;
            }
        }

        private void ResolveDependencies()
        {
            if (ShootingRangeCompositionRoot.Container != null)
            {
                if (_networkSession == null) ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession);
                if (_input == null) ShootingRangeCompositionRoot.Container.TryResolve(out _input);
            }

            if (_networkSession == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is INetworkSession ns) { _networkSession = ns; break; }
                }
            }

            if (_input == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is IControllerInputProvider ip) { _input = ip; break; }
                }
            }
        }

        private void Update()
        {
            ResolveDependencies();

            // Toggle In-Headset Floating Summon via Left Controller Menu/Y Button or 'M' Key
            bool vrMenuPressed = (_input != null && _input.GetSecondaryButtonDown(Handedness.Left));
            bool desktopTogglePressed = UnityEngine.Input.GetKeyDown(KeyCode.M);

            if (vrMenuPressed || desktopTogglePressed)
            {
                ToggleSummonInHeadset();
            }

            UpdateDisplay();
        }

        public void ToggleSummonInHeadset()
        {
            _isSummonedFloating = !_isSummonedFloating;

            if (_isSummonedFloating)
            {
                // Summon kiosk directly in front of player at comfortable eye level on the floor
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 forwardFlat = cam.transform.forward;
                    forwardFlat.y = 0f;
                    if (forwardFlat.sqrMagnitude < 0.05f) forwardFlat = cam.transform.forward;
                    forwardFlat.Normalize();

                    Vector3 floorPos = new Vector3(cam.transform.position.x + forwardFlat.x * 1.35f, 0f, cam.transform.position.z + forwardFlat.z * 1.35f);
                    transform.position = floorPos;
                    transform.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
                }
            }
            else
            {
                DockAtTerminal();
            }

            PlayClickAudio();
        }

        public void DockAtTerminal()
        {
            _isSummonedFloating = false;
            transform.position = s_DefaultStationaryPos;
            transform.rotation = s_DefaultStationaryRot;
            PlayClickAudio();
        }

        public void OnHostClicked()
        {
            ResolveDependencies();
            _networkSession?.StartHost((ushort)_port);
            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void OnJoinClicked()
        {
            ResolveDependencies();
            string targetIp = string.IsNullOrEmpty(_hostIp) ? _localIpAddress : _hostIp;
            _networkSession?.StartClient(targetIp, (ushort)_port);
            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void OnDisconnectClicked()
        {
            ResolveDependencies();
            _networkSession?.Disconnect();
            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void AppendChar(char c)
        {
            if (_hostIp.Length >= 22) return;

            if (c == '.')
            {
                if (string.IsNullOrEmpty(_hostIp) || _hostIp.EndsWith(".")) return;
                int dotCount = 0;
                for (int i = 0; i < _hostIp.Length; i++) if (_hostIp[i] == '.') dotCount++;
                if (dotCount >= 3) return;
            }

            _hostIp += c;
            TriggerHapticPulse(0.2f, 0.02f);
            PlayClickAudio();
            UpdateDisplay();
        }

        public void Backspace()
        {
            if (!string.IsNullOrEmpty(_hostIp))
            {
                _hostIp = _hostIp.Substring(0, _hostIp.Length - 1);
            }
            TriggerHapticPulse(0.25f, 0.02f);
            PlayClickAudio();
            UpdateDisplay();
        }

        public void ClearIP()
        {
            _hostIp = "";
            TriggerHapticPulse(0.3f, 0.03f);
            PlayClickAudio();
            UpdateDisplay();
        }

        public void SetToLocalhost()
        {
            _hostIp = "127.0.0.1";
            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void SetToMyLANIP()
        {
            _hostIp = _localIpAddress;
            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void StepLastOctet(int delta)
        {
            if (string.IsNullOrEmpty(_hostIp))
            {
                _hostIp = _localIpAddress;
                UpdateDisplay();
                return;
            }

            int lastDot = _hostIp.LastIndexOf('.');
            if (lastDot > 0 && lastDot < _hostIp.Length - 1)
            {
                string prefix = _hostIp.Substring(0, lastDot + 1);
                string octetStr = _hostIp.Substring(lastDot + 1);
                if (int.TryParse(octetStr, out int val))
                {
                    val = Mathf.Clamp(val + delta, 0, 255);
                    _hostIp = prefix + val;
                }
            }
            else
            {
                _hostIp = _localIpAddress;
            }

            TriggerHapticPulse();
            PlayClickAudio();
            UpdateDisplay();
        }

        public void UpdateDisplay()
        {
            bool isActive = _networkSession != null && _networkSession.IsActive;
            bool isHost = _networkSession != null && _networkSession.IsHost;
            bool isClient = _networkSession != null && _networkSession.IsClient && !isHost;

            // 1. Status Text & Color
            if (_statusText != null)
            {
                if (isActive)
                {
                    string lane = _networkSession.GetSpawnLaneName(_networkSession.LocalClientId);
                    if (isHost)
                    {
                        _statusText.text = $"STATUS: <color=#00FF88>HOSTING LAN ROOM [{lane}]</color>";
                    }
                    else
                    {
                        _statusText.text = $"STATUS: <color=#00FFFF>CONNECTED TO HOST [{lane}]</color>";
                    }
                }
                else
                {
                    _statusText.text = "STATUS: <color=#FFD700>SINGLEPLAYER (OFFLINE)</color>";
                }
            }

            // 2. Local IP Text
            if (_localIpText != null)
            {
                _localIpText.text = $"YOUR LOCAL DEVICE IP: <color=#00FFFF>{_localIpAddress}</color>  |  PORT: <color=#00FFFF>{_port}</color>";
            }

            // 3. Host IP Input Display
            if (_hostIpDisplayText != null)
            {
                string cur = string.IsNullOrEmpty(_hostIp) ? "<color=#888888>Type IP using VR Numpad...</color>" : $"<color=#00FFFF>{_hostIp}</color>";
                _hostIpDisplayText.text = $"TARGET HOST IP:  {cur}";
            }

            // 4. Session Telemetry / Stats
            if (_sessionStatsText != null)
            {
                if (isActive)
                {
                    int players = _networkSession.ConnectedPlayerCount;
                    float lat = _networkSession.EstimatedLatencyMs;
                    string latStr = isHost ? "0 ms (Host Server)" : $"~{lat:F0} ms (Lag Comp Active)";
                    _sessionStatsText.text = $"CONNECTED PLAYERS: <color=#00FFFF>{players} / 2</color>  |  PING: <color=#00FF88>{latStr}</color>\n2-Player LAN Room Active! Hits & Targets Synchronized authoritatively.";
                }
                else
                {
                    _sessionStatsText.text = "Room Idle. Host on PC/Quest, or enter host's IP on guest headset and click Join!";
                }
            }

            // 5. Spawn Lane Display
            if (_spawnLaneText != null)
            {
                if (isActive)
                {
                    string lane = _networkSession.GetSpawnLaneName(_networkSession.LocalClientId);
                    _spawnLaneText.text = $"ASSIGNED FIRING STALL: <color=#FFD700>{lane}</color>";
                }
                else
                {
                    _spawnLaneText.text = "ASSIGNED FIRING STALL: <color=#FFFFFF>LANE 2 (CENTER)</color>";
                }
            }

            // 6. Action Button Visibility
            if (_hostButton != null) _hostButton.gameObject.SetActive(!isActive);
            if (_joinButton != null) _joinButton.gameObject.SetActive(!isActive);
            if (_disconnectButton != null) _disconnectButton.gameObject.SetActive(isActive);

            if (_dockButton != null)
            {
                var txt = _dockButton.GetComponentInChildren<Text>();
                if (txt != null)
                {
                    txt.text = _isSummonedFloating ? "DOCK AT TERMINAL ◀" : "SUMMON IN FRONT ▶";
                }
            }
        }

        private void TriggerHapticPulse(float amp = 0.45f, float dur = 0.04f)
        {
            _input?.TriggerHaptic(Handedness.Right, amp, dur);
            _input?.TriggerHaptic(Handedness.Left, amp, dur);
        }

        private void PlayClickAudio()
        {
            var clip = ProceduralAudioSynthesizer.GetSlideRackClip();
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, transform.position, 0.45f);
            }
        }

        private void BindButtons()
        {
            if (_hostButton != null) _hostButton.onClick.AddListener(OnHostClicked);
            if (_joinButton != null) _joinButton.onClick.AddListener(OnJoinClicked);
            if (_disconnectButton != null) _disconnectButton.onClick.AddListener(OnDisconnectClicked);
            if (_dockButton != null) _dockButton.onClick.AddListener(ToggleSummonInHeadset);

            if (_presetLocalhostBtn != null) _presetLocalhostBtn.onClick.AddListener(SetToLocalhost);
            if (_presetMyIpBtn != null) _presetMyIpBtn.onClick.AddListener(SetToMyLANIP);
            if (_stepOctetDownBtn != null) _stepOctetDownBtn.onClick.AddListener(() => StepLastOctet(-1));
            if (_stepOctetUpBtn != null) _stepOctetUpBtn.onClick.AddListener(() => StepLastOctet(1));

            // Physical Poke Buttons
            if (_pokeHost != null) _pokeHost.OnPressed.AddListener(OnHostClicked);
            if (_pokeJoin != null) _pokeJoin.OnPressed.AddListener(OnJoinClicked);
            if (_pokeMyIp != null) _pokeMyIp.OnPressed.AddListener(SetToMyLANIP);
            if (_pokeDisconnect != null) _pokeDisconnect.OnPressed.AddListener(OnDisconnectClicked);
        }

        private string DetectLocalLANIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        /// <summary>
        /// Builds the entire World-Space VR UI Canvas with pedestal housing and console shelf.
        /// Elevated to eye level (y = 1.45m), completely above the floor!
        /// </summary>
        public void EnsureCanvasConstructed()
        {
            if (GetComponentInChildren<Canvas>(true) != null) return;

            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }

            // 1. Build Physical Kiosk Stand (Pedestal, Housing, Console Shelf) if not already built
            var existingPedestal = transform.Find("Pedestal");
            if (existingPedestal == null)
            {
                // Create Pedestal Base Cylinder from floor up to y = 1.1m
                var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pedestal.name = "Pedestal";
                pedestal.transform.SetParent(transform, false);
                pedestal.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                pedestal.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
            }

            var existingHousing = transform.Find("DisplayHousing");
            if (existingHousing == null)
            {
                // Create Display Housing at eye level (y = 1.45m)
                var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                housing.name = "DisplayHousing";
                housing.transform.SetParent(transform, false);
                housing.transform.localPosition = new Vector3(0f, 1.45f, 0f);
                housing.transform.localRotation = Quaternion.Euler(-5f, 0f, 0f);
                housing.transform.localScale = new Vector3(1.75f, 1.15f, 0.12f);

                // Dark Screen Face Inset
                var screenFace = GameObject.CreatePrimitive(PrimitiveType.Cube);
                screenFace.name = "ScreenFace";
                screenFace.transform.SetParent(housing.transform, false);
                screenFace.transform.localPosition = new Vector3(0f, 0.04f, -0.56f);
                screenFace.transform.localScale = new Vector3(0.94f, 0.88f, 0.08f);
                Destroy(screenFace.GetComponent<Collider>());
            }

            var existingShelf = transform.Find("ConsoleShelf");
            if (existingShelf == null)
            {
                // Console Shelf for Physical Poke Buttons
                var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shelf.name = "ConsoleShelf";
                shelf.transform.SetParent(transform, false);
                shelf.transform.localPosition = new Vector3(0f, 0.95f, -0.28f);
                shelf.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
                shelf.transform.localScale = new Vector3(1.65f, 0.08f, 0.38f);

                // 4 Physical 3D Push-Buttons on Front Console Shelf
                _pokeHost = CreatePokeButton(shelf.transform, "Poke_Host", "POKE: HOST", new Vector3(-0.52f, 0.05f, 0f), new Color(0.12f, 0.45f, 0.85f));
                _pokeJoin = CreatePokeButton(shelf.transform, "Poke_Join", "POKE: JOIN", new Vector3(-0.17f, 0.05f, 0f), new Color(0.12f, 0.70f, 0.35f));
                _pokeMyIp = CreatePokeButton(shelf.transform, "Poke_MyIP", "POKE: MY IP", new Vector3(0.17f, 0.05f, 0f), new Color(0.95f, 0.75f, 0.15f));
                _pokeDisconnect = CreatePokeButton(shelf.transform, "Poke_Disconnect", "POKE: LEAVE", new Vector3(0.52f, 0.05f, 0f), new Color(0.85f, 0.20f, 0.20f));
            }
            else
            {
                if (_pokeHost == null) _pokeHost = existingShelf.Find("Poke_Host")?.GetComponent<VRPokeInteractable>();
                if (_pokeJoin == null) _pokeJoin = existingShelf.Find("Poke_Join")?.GetComponent<VRPokeInteractable>();
                if (_pokeMyIp == null) _pokeMyIp = existingShelf.Find("Poke_MyIP")?.GetComponent<VRPokeInteractable>();
                if (_pokeDisconnect == null) _pokeDisconnect = existingShelf.Find("Poke_Disconnect")?.GetComponent<VRPokeInteractable>();
            }

            // 2. Root World-Space Canvas: positioned flush on the housing face at eye level (y = 1.48m)
            var canvasObj = new GameObject("VRNetworkWorldCanvas");
            canvasObj.transform.SetParent(transform, false);
            canvasObj.transform.localPosition = new Vector3(0f, 1.48f, -0.075f);
            canvasObj.transform.localRotation = Quaternion.Euler(-5f, 0f, 0f);
            canvasObj.transform.localScale = new Vector3(0.0014f, 0.0014f, 0.0014f);

            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1150f, 700f);
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();

            // Canvas Background Panel
            CreateUIPanel(canvasObj.transform, "CanvasBg", Vector2.zero, new Vector2(1150f, 700f), new Color(0.04f, 0.06f, 0.09f, 0.96f));

            // Header Section
            CreateUIText(canvasObj.transform, "Title", "LAN MULTIPLAYER ROOM (NGO)", 28, Color.cyan, TextAnchor.MiddleCenter, new Vector2(0f, 310f), new Vector2(1100f, 40f), font, true);
            CreateUIText(canvasObj.transform, "Subtitle", "2 Players | Server-Authoritative | ~80ms Lag Comp | Quest & PC Crossplay", 14, new Color(0.8f, 0.85f, 0.9f), TextAnchor.MiddleCenter, new Vector2(0f, 275f), new Vector2(1100f, 25f), font, false);

            _statusText = CreateUIText(canvasObj.transform, "StatusText", "STATUS: SINGLEPLAYER (OFFLINE)", 17, Color.yellow, TextAnchor.MiddleCenter, new Vector2(0f, 245f), new Vector2(1100f, 28f), font, true);
            _localIpText = CreateUIText(canvasObj.transform, "LocalIpText", $"YOUR LOCAL DEVICE IP: {_localIpAddress}  |  PORT: {_port}", 14, new Color(0.3f, 0.9f, 1f), TextAnchor.MiddleCenter, new Vector2(0f, 218f), new Vector2(1100f, 22f), font, false);

            // =====================================
            // LEFT COLUMN: CREATE / HOST (x = -285)
            // =====================================
            CreateUIPanel(canvasObj.transform, "HostPanel", new Vector2(-285f, -15f), new Vector2(520f, 420f), new Color(0.08f, 0.12f, 0.18f, 0.90f));
            CreateUIText(canvasObj.transform, "HostHeader", "=== CREATE / HOST LAN ROOM ===", 20, Color.yellow, TextAnchor.MiddleCenter, new Vector2(-285f, 165f), new Vector2(490f, 30f), font, true);
            CreateUIText(canvasObj.transform, "HostDesc", "One player hosts on PC or Quest and plays simultaneously.\nGuests can join using your Local Device IP above.\nLane: Host spawns authoritatively at Lane 2 (Center).", 14, Color.white, TextAnchor.MiddleCenter, new Vector2(-285f, 105f), new Vector2(490f, 75f), font, false);

            _hostButton = CreateUIButton(canvasObj.transform, "HostButton", "HOST LAN ROOM (Server + Player 1)", new Vector2(-285f, 25f), new Vector2(460f, 55f), new Color(0.12f, 0.45f, 0.75f, 0.98f), font, out _);

            _spawnLaneText = CreateUIText(canvasObj.transform, "SpawnLaneText", "ASSIGNED FIRING STALL: LANE 2 (CENTER)", 15, new Color(0.2f, 1f, 0.5f), TextAnchor.MiddleCenter, new Vector2(-285f, -40f), new Vector2(490f, 28f), font, true);
            _sessionStatsText = CreateUIText(canvasObj.transform, "SessionStatsText", "Room Idle. Host or Join above to begin co-op shooting!", 13, new Color(0.85f, 0.85f, 0.85f), TextAnchor.MiddleCenter, new Vector2(-285f, -110f), new Vector2(480f, 75f), font, false);

            // Large Disconnect Button (Overlay on Left Panel when Active)
            _disconnectButton = CreateUIButton(canvasObj.transform, "DisconnectButton", "LEAVE / DISCONNECT ROOM", new Vector2(-285f, 25f), new Vector2(460f, 55f), new Color(0.75f, 0.18f, 0.18f, 0.98f), font, out _);
            _disconnectButton.gameObject.SetActive(false);

            // =====================================
            // RIGHT COLUMN: JOIN & VR NUMPAD (x = +285)
            // =====================================
            CreateUIPanel(canvasObj.transform, "JoinPanel", new Vector2(285f, -15f), new Vector2(520f, 420f), new Color(0.08f, 0.12f, 0.18f, 0.90f));
            CreateUIText(canvasObj.transform, "JoinHeader", "=== JOIN EXISTING LAN ROOM ===", 20, Color.yellow, TextAnchor.MiddleCenter, new Vector2(285f, 165f), new Vector2(490f, 30f), font, true);

            // Target IP Display Field
            CreateUIPanel(canvasObj.transform, "IpDisplayBox", new Vector2(285f, 120f), new Vector2(480f, 38f), new Color(0.03f, 0.05f, 0.08f, 0.95f));
            _hostIpDisplayText = CreateUIText(canvasObj.transform, "HostIpDisplayText", $"TARGET HOST IP:  <color=#00FFFF>{_hostIp}</color>", 16, Color.white, TextAnchor.MiddleCenter, new Vector2(285f, 120f), new Vector2(470f, 34f), font, true);

            // Quick IP Preset Buttons
            _presetMyIpBtn = CreateUIButton(canvasObj.transform, "PresetMyIpBtn", "MY IP", new Vector2(100f, 80f), new Vector2(90f, 28f), new Color(0.18f, 0.32f, 0.48f, 0.95f), font, out _);
            _presetLocalhostBtn = CreateUIButton(canvasObj.transform, "PresetLocalhostBtn", "127.0.0.1", new Vector2(205f, 80f), new Vector2(100f, 28f), new Color(0.18f, 0.32f, 0.48f, 0.95f), font, out _);
            _stepOctetDownBtn = CreateUIButton(canvasObj.transform, "StepOctetDownBtn", "IP -1", new Vector2(320f, 80f), new Vector2(85f, 28f), new Color(0.22f, 0.35f, 0.50f, 0.95f), font, out _);
            _stepOctetUpBtn = CreateUIButton(canvasObj.transform, "StepOctetUpBtn", "IP +1", new Vector2(420f, 80f), new Vector2(85f, 28f), new Color(0.22f, 0.35f, 0.50f, 0.95f), font, out _);

            // VR Virtual Numpad (Keypad Grid)
            string[] keys = new string[]
            {
                "1", "2", "3",
                "4", "5", "6",
                "7", "8", "9",
                ".", "0", "⌫"
            };

            float numpadStartX = 145f;
            float numpadStartY = 35f;
            float btnW = 85f;
            float btnH = 34f;
            float gapX = 98f;
            float gapY = 38f;

            for (int i = 0; i < keys.Length; i++)
            {
                int row = i / 3;
                int col = i % 3;
                string keyChar = keys[i];
                float kx = numpadStartX + col * gapX;
                float ky = numpadStartY - row * gapY;

                Color kCol = (keyChar == "⌫") ? new Color(0.55f, 0.22f, 0.22f, 0.95f) : new Color(0.15f, 0.24f, 0.35f, 0.95f);
                var btn = CreateUIButton(canvasObj.transform, $"Key_{keyChar}", keyChar, new Vector2(kx, ky), new Vector2(btnW, btnH), kCol, font, out _);

                if (keyChar == "⌫")
                {
                    btn.onClick.AddListener(Backspace);
                }
                else
                {
                    char c = keyChar[0];
                    btn.onClick.AddListener(() => AppendChar(c));
                }
            }

            // Clear IP Button beside numpad
            var clearBtn = CreateUIButton(canvasObj.transform, "ClearBtn", "CLEAR", new Vector2(440f, 15f), new Vector2(75f, 72f), new Color(0.40f, 0.18f, 0.18f, 0.95f), font, out _);
            clearBtn.onClick.AddListener(ClearIP);

            // Big Green Join Button
            _joinButton = CreateUIButton(canvasObj.transform, "JoinButton", "JOIN LAN ROOM (Player 2)", new Vector2(285f, -145f), new Vector2(460f, 52f), new Color(0.10f, 0.60f, 0.35f, 0.98f), font, out _);

            // =====================================
            // BOTTOM BAR: DOCK / SUMMON TOGGLE (y = -315)
            // =====================================
            _dockButton = CreateUIButton(canvasObj.transform, "DockButton", "SUMMON IN FRONT ▶", new Vector2(-380f, -315f), new Vector2(240f, 38f), new Color(0.35f, 0.25f, 0.55f, 0.95f), font, out _);
            CreateUIText(canvasObj.transform, "HintText", "Tip: In VR Headset, press Left Controller 'Y' button (or 'M' on PC) anytime to summon this menu in front of you!", 14, new Color(0.75f, 0.85f, 0.95f), TextAnchor.MiddleLeft, new Vector2(100f, -315f), new Vector2(720f, 35f), font, false);
        }

        private static VRPokeInteractable CreatePokeButton(Transform parent, string name, string label, Vector3 localPos, Color capColor)
        {
            var buttonRoot = new GameObject(name);
            buttonRoot.transform.SetParent(parent, false);
            buttonRoot.transform.localPosition = localPos;
            buttonRoot.transform.localRotation = Quaternion.identity;

            // Base socket collar
            var socket = GameObject.CreatePrimitive(PrimitiveType.Cube);
            socket.name = "Socket";
            socket.transform.SetParent(buttonRoot.transform, false);
            socket.transform.localPosition = Vector3.zero;
            socket.transform.localScale = new Vector3(0.24f, 0.04f, 0.16f);
            Destroy(socket.GetComponent<Collider>());

            // Moving Cap
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "MovingCap";
            cap.transform.SetParent(buttonRoot.transform, false);
            cap.transform.localPosition = new Vector3(0f, 0.035f, 0f);
            cap.transform.localScale = new Vector3(0.20f, 0.05f, 0.13f);
            var rend = cap.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard"));
                mat.color = capColor;
                rend.sharedMaterial = mat;
            }
            Destroy(cap.GetComponent<Collider>());

            // 3D Floating TextMesh Label
            var labelObj = new GameObject("Label3D");
            labelObj.transform.SetParent(buttonRoot.transform, false);
            labelObj.transform.localPosition = new Vector3(0f, 0.075f, 0.02f);
            labelObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = labelObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.characterSize = 0.015f;
            tm.fontSize = 32;
            tm.fontStyle = FontStyle.Bold;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;

            // Trigger BoxCollider and Rigidbody for poke & ray interaction
            var rb = buttonRoot.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var col = buttonRoot.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.04f, 0f);
            col.size = new Vector3(0.24f, 0.12f, 0.20f);

            var poke = buttonRoot.AddComponent<VRPokeInteractable>();
            return poke;
        }

        private static GameObject CreateUIPanel(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static Text CreateUIText(Transform parent, string name, string text, int fontSize, Color color, TextAnchor alignment, Vector2 pos, Vector2 size, Font font, bool bold = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var txt = go.AddComponent<Text>();
            txt.text = text;
            txt.fontSize = fontSize;
            txt.color = color;
            txt.alignment = alignment;
            if (font != null) txt.font = font;
            if (bold) txt.fontStyle = FontStyle.Bold;
            txt.supportRichText = true;
            txt.raycastTarget = false;
            return txt;
        }

        private static Button CreateUIButton(Transform parent, string name, string labelText, Vector2 pos, Vector2 size, Color bgColor, Font font, out Text buttonText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.25f;
            colors.pressedColor = bgColor * 0.75f;
            btn.colors = colors;

            // BoxCollider so VRRayPointer, VRPokeInteractable, and Desktop Simulator can interact
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0f, -8f);
            col.size = new Vector3(size.x, size.y, 25f);

            buttonText = CreateUIText(go.transform, "Label", labelText, 16, Color.white, TextAnchor.MiddleCenter, Vector2.zero, size, font, true);
            return btn;
        }

        /// <summary>
        /// Auto-spawns the World-Space VR LAN Terminal at runtime if not present in the scene.
        /// Fully elevated at eye level (y = 1.45m), completely above the floor!
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoEnsureWorldSpaceTerminal()
        {
            if (FindFirstObjectByType<WorldSpaceNetworkUI>() != null) return;

            var terminalRoot = new GameObject("WorldSpaceLANTerminalKiosk");
            // Positioned at the right side of the firing line (mirroring Settings Kiosk at -5.2f, 0f, 2.2f)
            terminalRoot.transform.position = s_DefaultStationaryPos;
            terminalRoot.transform.rotation = s_DefaultStationaryRot;

            var ui = terminalRoot.AddComponent<WorldSpaceNetworkUI>();
            ui.EnsureCanvasConstructed();
        }
    }
}
