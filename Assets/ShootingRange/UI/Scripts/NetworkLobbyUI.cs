using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.UI
{
    /// <summary>
    /// Dedicated LAN Multiplayer Lobby & Room Management Panel.
    /// Provides controls to:
    /// - Host LAN room (Server + Player simultaneously on PC/Quest)
    /// - Join LAN room with custom Host IP and Port
    /// - View connection status, local LAN IP, ping/latency (~80ms), player count, shared score, and round timer
    /// - Leave / Disconnect room cleanly without breaking session
    /// Toggleable with 'M' key or accessible on-screen.
    /// </summary>
    public class NetworkLobbyUI : MonoBehaviour
    {
        [Header("LAN Configuration")]
        [SerializeField] private string _hostIp = "127.0.0.1";
        [SerializeField] private string _port = "7777";
        [SerializeField] private bool _showPanel = true;

        private INetworkSession _networkSession;
        private string _localIpAddress = "127.0.0.1";
        private GUIStyle _panelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _statusStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _disconnectButtonStyle;
        private GUIStyle _textFieldStyle;
        private bool _stylesInitialized = false;

        public bool IsPanelVisible => _showPanel;

        private void Awake()
        {
            _localIpAddress = DetectLocalLANIP();
            _hostIp = _localIpAddress;
        }

        private void Start()
        {
            ResolveSession();
        }

        private void Update()
        {
            ResolveSession();

            // Toggle Multiplayer Panel visibility via 'M' key
            if (UnityEngine.Input.GetKeyDown(KeyCode.M))
            {
                SetPanelOpen(!_showPanel);
            }

            if (_showPanel && Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        public void SetPanelOpen(bool open)
        {
            _showPanel = open;
            UIInteractionState.IsAnyUIPanelOpen = open;
            if (open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void ResolveSession()
        {
            if (_networkSession == null && ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession);
            }

            if (_networkSession == null)
            {
                var allMonos = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var m in allMonos)
                {
                    if (m is INetworkSession ns)
                    {
                        _networkSession = ns;
                        break;
                    }
                }
            }
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            // Background panel
            _panelStyle = new GUIStyle(GUI.skin.box);
            var bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.12f, 0.94f));
            bgTex.Apply();
            _panelStyle.normal.background = bgTex;

            // Header
            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            _headerStyle.normal.textColor = new Color(0.2f, 0.85f, 1.0f);

            // Standard Label
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal
            };
            _labelStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);

            // Status Label
            _statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            _statusStyle.normal.textColor = new Color(0.4f, 1.0f, 0.5f);

            // Standard Action Button
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            var btnTex = new Texture2D(1, 1);
            btnTex.SetPixel(0, 0, new Color(0.12f, 0.45f, 0.75f, 1.0f));
            btnTex.Apply();
            _buttonStyle.normal.background = btnTex;
            _buttonStyle.normal.textColor = Color.white;

            // Disconnect Button
            _disconnectButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            var dcTex = new Texture2D(1, 1);
            dcTex.SetPixel(0, 0, new Color(0.75f, 0.2f, 0.2f, 1.0f));
            dcTex.Apply();
            _disconnectButtonStyle.normal.background = dcTex;
            _disconnectButtonStyle.normal.textColor = Color.white;

            // Text Input Field
            _textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            // In VR Headset, WorldSpaceNetworkUI handles all multiplayer interaction in stereo 3D
            if (UnityEngine.XR.XRSettings.isDeviceActive) return;

            InitStyles();

            float screenW = Screen.width;
            Rect toggleBtnRect = new Rect(screenW - 220f, 15f, 205f, 28f);

            float panelWidth = 360f;
            float panelHeight = 470f;
            float panelX = screenW - panelWidth - 20f;
            float panelY = 50f;
            Rect panelRect = new Rect(panelX, panelY, panelWidth, panelHeight);

            Vector2 mousePos = new Vector2(UnityEngine.Input.mousePosition.x, Screen.height - UnityEngine.Input.mousePosition.y);
            bool isOver = toggleBtnRect.Contains(mousePos) || (_showPanel && panelRect.Contains(mousePos));
            UIInteractionState.IsPointerOverUI = isOver;
            UIInteractionState.IsAnyUIPanelOpen = _showPanel;

            // Toggle hint button at top right
            if (GUI.Button(toggleBtnRect, _showPanel ? "[M] Hide LAN Room Panel" : "[M] Open LAN Room Panel"))
            {
                SetPanelOpen(!_showPanel);
            }

            if (!_showPanel) return;

            GUI.Box(panelRect, GUIContent.none, _panelStyle);

            GUILayout.BeginArea(new Rect(panelX + 15f, panelY + 12f, panelWidth - 30f, panelHeight - 24f));
            try
            {
                GUILayout.Label("LAN MULTIPLAYER ROOM (NGO)", _headerStyle);
                GUILayout.Label("2 Players | Server-Authoritative | ~80ms Lag Comp", _labelStyle);
                GUILayout.Space(6f);

                // Session State Readout
                bool isActive = _networkSession != null && _networkSession.IsActive;
                bool isHost = _networkSession != null && _networkSession.IsHost;
                bool isClient = _networkSession != null && _networkSession.IsClient && !isHost;

                string statusStr = "Status: Singleplayer (Offline)";
                if (isActive)
                {
                    string myLane = _networkSession != null ? _networkSession.GetSpawnLaneName(_networkSession.LocalClientId) : "LANE 2";
                    if (isHost) statusStr = $"Status: HOSTING LAN ROOM [{myLane}]";
                    else if (isClient) statusStr = $"Status: CONNECTED TO HOST [{myLane}]";
                }
                GUILayout.Label(statusStr, _statusStyle);

                GUILayout.Label($"My Local LAN IP: {_localIpAddress}", _labelStyle);

                if (isActive)
                {
                    int players = _networkSession.ConnectedPlayerCount;
                    GUILayout.Label($"Connected Players: {players} / 2", _labelStyle);

                    float latency = _networkSession.EstimatedLatencyMs;
                    string latStr = isHost ? "0 ms (Host Server)" : $"~{latency:F0} ms [Lag Comp Active]";
                    GUILayout.Label($"Ping / Latency: {latStr}", _labelStyle);

                    if (!string.IsNullOrEmpty(_networkSession.StatusMessage))
                    {
                        GUILayout.Label($"Info: {_networkSession.StatusMessage}", _labelStyle);
                    }
                }

                GUILayout.Space(10f);

                // SECTION 1: HOST LAN ROOM
                if (!isActive)
                {
                    GUILayout.Label("--- CREATE / HOST LAN ROOM ---", _labelStyle);
                    GUILayout.Label("One player hosts on PC or Quest and plays simultaneously.", _labelStyle);

                    if (GUILayout.Button("HOST LAN ROOM (Server + Player 1)", _buttonStyle, GUILayout.Height(34f)))
                    {
                        if (ushort.TryParse(_port, out ushort p))
                        {
                            _networkSession?.StartHost(p);
                        }
                        else
                        {
                            _networkSession?.StartHost(7777);
                        }
                    }

                    GUILayout.Space(12f);

                    // SECTION 2: JOIN LAN ROOM
                    GUILayout.Label("--- JOIN EXISTING LAN ROOM ---", _labelStyle);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Host IP:", _labelStyle, GUILayout.Width(60f));
                    _hostIp = GUILayout.TextField(_hostIp, _textFieldStyle);
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Port:", _labelStyle, GUILayout.Width(60f));
                    _port = GUILayout.TextField(_port, _textFieldStyle, GUILayout.Width(80f));
                    GUILayout.EndHorizontal();

                    GUILayout.Space(4f);

                    if (GUILayout.Button("JOIN LAN ROOM (Player 2)", _buttonStyle, GUILayout.Height(34f)))
                    {
                        if (ushort.TryParse(_port, out ushort p))
                        {
                            _networkSession?.StartClient(_hostIp, p);
                        }
                        else
                        {
                            _networkSession?.StartClient(_hostIp, 7777);
                        }
                    }
                }
                else
                {
                    // SECTION 3: CONNECTED SESSION CONTROLS
                    GUILayout.Label("--- ACTIVE SESSION CONTROLS ---", _labelStyle);
                    if (isHost)
                    {
                        GUILayout.Label($"Share IP with Player 2: {_localIpAddress}:{_port}", _statusStyle);
                    }

                    GUILayout.Space(12f);

                    if (GUILayout.Button("LEAVE / DISCONNECT ROOM", _disconnectButtonStyle, GUILayout.Height(36f)))
                    {
                        _networkSession?.Disconnect();
                    }

                    GUILayout.Space(6f);
                    GUILayout.Label("Disconnecting gracefully returns to singleplayer mode.", _labelStyle);
                }

                GUILayout.Space(10f);
                if (GUILayout.Button("Close Panel (Resume Game)", GUILayout.Height(26f)))
                {
                    SetPanelOpen(false);
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            finally
            {
                GUILayout.EndArea();
            }
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
    }
}

