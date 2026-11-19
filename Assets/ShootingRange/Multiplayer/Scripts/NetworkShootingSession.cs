using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using ShootingRange.Core;
using ShootingRange.Targets;

namespace ShootingRange.Multiplayer
{
    /// <summary>
    /// Server-authoritative multiplayer session using Netcode for GameObjects.
    /// Supports:
    /// - 2 players over LAN (One player acts as host, playing on Desktop or Quest simultaneously)
    /// - Server-authoritative hit registration with client-side prediction and ~80ms lag compensation
    /// - Shared target states and synchronized round score
    /// - Late-joining player state catch-up (receives full target and session state)
    /// - Graceful disconnect & reconnect without breaking the session
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class NetworkShootingSession : NetworkBehaviour, INetworkSession
    {
        [Header("Lag Compensation Settings")]
        [SerializeField] private float _maxLagCompensationSeconds = 0.25f; // Up to 250ms history buffer
        [SerializeField] private float _defaultLatencyEstimate = 0.08f;    // ~80ms baseline for LAN

        // Server-authoritative synchronized state via NetworkVariable
        public NetworkVariable<int> SynchronizedScore = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> CurrentRound = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> RoundTimeRemaining = new NetworkVariable<float>(
            300f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> SynchronizedPlayerCount = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private TargetManager _targetManager;
        private readonly Dictionary<ulong, int> _playerScores = new Dictionary<ulong, int>();
        private string _statusMessage = "Offline (Singleplayer)";
        private float _lastPingTime;
        private float _estimatedLatencyMs = 0f;

        // Circular history buffer for lag compensation rewind
        private struct TargetHistoricalSnapshot
        {
            public float Timestamp;
            public int TargetId;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private readonly List<TargetHistoricalSnapshot> _targetHistory = new List<TargetHistoricalSnapshot>(2048);

        public bool IsActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public bool IsHost => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        public bool IsClient => NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        public ulong LocalClientId => NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
        public int ConnectedPlayerCount
        {
            get
            {
                if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return 1;
                if (NetworkManager.Singleton.IsServer)
                {
                    try
                    {
                        return NetworkManager.Singleton.ConnectedClients != null ? NetworkManager.Singleton.ConnectedClients.Count : 1;
                    }
                    catch
                    {
                        return 1;
                    }
                }
                return SynchronizedPlayerCount != null ? SynchronizedPlayerCount.Value : 1;
            }
        }

        public float EstimatedLatencyMs
        {
            get
            {
                if (IsHost || !IsActive) return 0f;
                if (NetworkManager.Singleton != null)
                {
                    var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    if (transport != null)
                    {
                        ulong rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
                        if (rtt > 0) return rtt;
                    }
                }
                return _estimatedLatencyMs;
            }
        }
        public string StatusMessage => _statusMessage;

        public event Action<ulong, int, HitZone, float> OnNetworkHitValidated;
        public event Action<ulong, int> OnScoreUpdated;
        public event Action<ulong, Vector3, Vector3, WeaponType> OnRemoteShotFired;
        public event Action<ulong, WeaponType, Vector3, Quaternion, Vector3, Vector3> OnRemoteMagazineEjected;
        public event Action<ulong, WeaponType> OnRemoteReloadStarted;
        public event Action<ulong, WeaponType> OnRemoteMagazineInserted;
        public event Action<ulong, bool> OnRemoteHandSwitched;
        public event Action OnConnectionStateChanged;
        public event Action OnPlayerConnected;
        public event Action OnPlayerDisconnected;

        private void Awake()
        {
            _targetManager = FindFirstObjectByType<TargetManager>();
            EnsureNetworkSetup();
        }

        public static void EnsureNetworkSetup()
        {
            if (NetworkManager.Singleton == null)
            {
                var nmObj = new GameObject("NetworkManager");
                var nm = nmObj.AddComponent<NetworkManager>();
                var transport = nmObj.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();

                nm.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    TickRate = 60,
                    ConnectionApproval = true
                };
            }

            // Ensure ConnectionApprovalCallback & PlayerPrefab are assigned so players spawn at discrete lane stalls
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.NetworkConfig != null)
            {
                NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
                NetworkManager.Singleton.ConnectionApprovalCallback = ConnectionApprovalCallback;

                if (NetworkManager.Singleton.NetworkConfig.PlayerPrefab == null)
                {
                    var avatarPrefab = Resources.Load<GameObject>("NetworkPlayerAvatar");
                    if (avatarPrefab != null)
                    {
                        NetworkManager.Singleton.NetworkConfig.PlayerPrefab = avatarPrefab;
                        NetworkManager.Singleton.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = avatarPrefab });
                    }
                }
            }
        }

        private static void ConnectionApprovalCallback(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.Approved = true;
            response.CreatePlayerObject = true;
            ulong clientId = request.ClientNetworkId;
            response.Position = SpawnPointDefinitions.GetSpawnPosition(clientId);
            response.Rotation = SpawnPointDefinitions.GetSpawnRotation(clientId);
        }

        public Vector3 GetSpawnPosition(ulong clientId) => SpawnPointDefinitions.GetSpawnPosition(clientId);
        public Quaternion GetSpawnRotation(ulong clientId) => SpawnPointDefinitions.GetSpawnRotation(clientId);
        public string GetSpawnLaneName(ulong clientId) => SpawnPointDefinitions.GetLaneName(clientId);

        public override void OnNetworkSpawn()
        {
            SynchronizedScore.OnValueChanged += HandleScoreChanged;

            if (IsServer)
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients != null)
                {
                    SynchronizedPlayerCount.Value = NetworkManager.Singleton.ConnectedClients.Count;
                }
                _statusMessage = "Hosting LAN Session (Server + Player 1 Active)";
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
            else
            {
                _statusMessage = "Connected to Host (Co-Op Active)";
                _estimatedLatencyMs = 80f; // ~80ms baseline for LAN
            }

            OnConnectionStateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            SynchronizedScore.OnValueChanged -= HandleScoreChanged;

            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            _statusMessage = "Disconnected from session";
            OnConnectionStateChanged?.Invoke();
        }

        private void Update()
        {
            if (IsServer && IsActive)
            {
                UpdateLagCompensationSnapshots();
                if (RoundTimeRemaining.Value > 0f)
                {
                    RoundTimeRemaining.Value -= Time.deltaTime;
                }
            }

            if (IsClient && !IsHost && IsActive)
            {
                // Query hardware socket RTT directly from transport without unspawned RPC overhead
                if (Time.time - _lastPingTime > 1.5f)
                {
                    _lastPingTime = Time.time;
                    var transport = NetworkManager.Singleton?.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                    if (transport != null)
                    {
                        ulong rtt = transport.GetCurrentRtt(NetworkManager.ServerClientId);
                        if (rtt > 0) _estimatedLatencyMs = rtt;
                    }
                }
            }
        }

        private void UpdateLagCompensationSnapshots()
        {
            if (_targetManager == null) return;

            float now = Time.time;
            var targets = _targetManager.AllTargets;
            if (targets != null)
            {
                int count = targets.Count;
                for (int i = 0; i < count; i++)
                {
                    var t = targets[i];
                    if (t != null && t.Transform != null)
                    {
                        _targetHistory.Add(new TargetHistoricalSnapshot
                        {
                            Timestamp = now,
                            TargetId = t.TargetId,
                            Position = t.Transform.position,
                            Rotation = t.Transform.rotation
                        });
                    }
                }
            }

            // Cull snapshots older than lag compensation window (250ms)
            _targetHistory.RemoveAll(s => now - s.Timestamp > _maxLagCompensationSeconds);
        }

        public void StartHost(ushort port = 7777)
        {
            EnsureNetworkSetup();
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Port = port;
                transport.ConnectionData.ServerListenAddress = "0.0.0.0";
            }

            bool success = NetworkManager.Singleton.StartHost();
            if (success && NetworkObject != null && !NetworkObject.IsSpawned && NetworkManager.Singleton.IsServer)
            {
                NetworkObject.Spawn();
            }

            _statusMessage = success ? $"Hosting LAN Session on Port {port}" : "Failed to start Host";
            OnConnectionStateChanged?.Invoke();
        }

        public void StartClient(string ip = "127.0.0.1", ushort port = 7777)
        {
            EnsureNetworkSetup();
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }

            var transport = NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
            if (transport != null)
            {
                transport.ConnectionData.Address = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
                transport.ConnectionData.Port = port;
            }

            _statusMessage = $"Connecting to {ip}:{port}...";
            bool success = NetworkManager.Singleton.StartClient();
            if (!success) _statusMessage = "Failed to initiate client connection";
            OnConnectionStateChanged?.Invoke();
        }

        public void Disconnect()
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                NetworkManager.Singleton.Shutdown();
            }
            _statusMessage = "Offline (Singleplayer)";
            _targetHistory.Clear();
            _playerScores.Clear();
            OnConnectionStateChanged?.Invoke();
        }

        public void ReportHit(ulong shooterId, int targetId, HitZone zone, float damage, Vector3 hitPoint)
        {
            if (!IsActive) return;

            ReportHitServerRpc(shooterId, targetId, (int)zone, damage, hitPoint, Time.time);
        }

        public void BroadcastShot(ulong shooterId, Vector3 muzzlePos, Vector3 velocity, WeaponType weaponType)
        {
            if (!IsActive) return;

            BroadcastShotServerRpc(shooterId, muzzlePos, velocity, (int)weaponType);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BroadcastShotServerRpc(ulong shooterId, Vector3 muzzlePos, Vector3 velocity, int weaponTypeInt)
        {
            BroadcastShotClientRpc(shooterId, muzzlePos, velocity, weaponTypeInt);
        }

        [ClientRpc]
        private void BroadcastShotClientRpc(ulong shooterId, Vector3 muzzlePos, Vector3 velocity, int weaponTypeInt)
        {
            if (shooterId == LocalClientId) return; // Own shots are simulated locally

            WeaponType wt = (WeaponType)weaponTypeInt;
            OnRemoteShotFired?.Invoke(shooterId, muzzlePos, velocity, wt);

            // Trigger muzzle flash on the remote player's avatar
            var avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (var a in avatars)
            {
                if (!a.IsOwner && a.OwnerClientId == shooterId)
                {
                    a.FlashMuzzle();
                    break;
                }
            }
        }

        public void BroadcastMagazineEjected(ulong playerId, WeaponType weaponType, Vector3 dropPos, Quaternion dropRot, Vector3 velocity, Vector3 angularVelocity)
        {
            if (!IsActive) return;
            BroadcastMagazineEjectedServerRpc(playerId, (int)weaponType, dropPos, dropRot, velocity, angularVelocity);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BroadcastMagazineEjectedServerRpc(ulong playerId, int weaponTypeInt, Vector3 dropPos, Quaternion dropRot, Vector3 velocity, Vector3 angularVelocity)
        {
            BroadcastMagazineEjectedClientRpc(playerId, weaponTypeInt, dropPos, dropRot, velocity, angularVelocity);
        }

        [ClientRpc]
        private void BroadcastMagazineEjectedClientRpc(ulong playerId, int weaponTypeInt, Vector3 dropPos, Quaternion dropRot, Vector3 velocity, Vector3 angularVelocity)
        {
            if (playerId == LocalClientId) return; // Own magazine is already spawned locally

            WeaponType wt = (WeaponType)weaponTypeInt;
            OnRemoteMagazineEjected?.Invoke(playerId, wt, dropPos, dropRot, velocity, angularVelocity);

            // Spawn visual physical magazine for remote player
            SpawnRemoteEjectedMagazine(wt, dropPos, dropRot, velocity, angularVelocity);

            // Notify remote player's avatar
            var avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (var a in avatars)
            {
                if (!a.IsOwner && a.OwnerClientId == playerId)
                {
                    a.OnRemoteMagazineEjected(wt);
                    break;
                }
            }
        }

        private void SpawnRemoteEjectedMagazine(WeaponType wt, Vector3 dropPos, Quaternion dropRot, Vector3 velocity, Vector3 angularVelocity)
        {
            var magObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            magObj.name = wt == WeaponType.Rifle ? "RemoteEjectedRifleMag" : "RemoteEjectedPistolMag";
            magObj.transform.position = dropPos;
            magObj.transform.rotation = dropRot;

            if (wt == WeaponType.Rifle)
            {
                magObj.transform.localScale = new Vector3(0.034f, 0.18f, 0.08f);
            }
            else
            {
                magObj.transform.localScale = new Vector3(0.028f, 0.11f, 0.045f);
            }

            var rend = magObj.GetComponent<Renderer>();
            if (rend != null)
            {
#if UNITY_EDITOR
                var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/ShootingRange/Materials/Mat_TacticalPolymer.mat");
                if (mat != null) rend.sharedMaterial = mat;
#endif
            }

            var rb = magObj.AddComponent<Rigidbody>();
            rb.mass = wt == WeaponType.Rifle ? 0.45f : 0.25f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearVelocity = velocity;
            rb.angularVelocity = angularVelocity;

            // Audio: play ejection sound in 3D
            var clip = ProceduralAudioSynthesizer.GetMagEjectClip();
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, dropPos, 0.9f);
            }

            Destroy(magObj, 8.0f);
        }

        public void BroadcastReloadStarted(ulong playerId, WeaponType weaponType)
        {
            if (!IsActive) return;
            BroadcastReloadStartedServerRpc(playerId, (int)weaponType);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BroadcastReloadStartedServerRpc(ulong playerId, int weaponTypeInt)
        {
            BroadcastReloadStartedClientRpc(playerId, weaponTypeInt);
        }

        [ClientRpc]
        private void BroadcastReloadStartedClientRpc(ulong playerId, int weaponTypeInt)
        {
            if (playerId == LocalClientId) return;

            WeaponType wt = (WeaponType)weaponTypeInt;
            OnRemoteReloadStarted?.Invoke(playerId, wt);

            var avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (var a in avatars)
            {
                if (!a.IsOwner && a.OwnerClientId == playerId)
                {
                    a.OnRemoteReloadStarted(wt);
                    break;
                }
            }
        }

        public void BroadcastMagazineInserted(ulong playerId, WeaponType weaponType)
        {
            if (!IsActive) return;
            BroadcastMagazineInsertedServerRpc(playerId, (int)weaponType);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BroadcastMagazineInsertedServerRpc(ulong playerId, int weaponTypeInt)
        {
            BroadcastMagazineInsertedClientRpc(playerId, weaponTypeInt);
        }

        [ClientRpc]
        private void BroadcastMagazineInsertedClientRpc(ulong playerId, int weaponTypeInt)
        {
            if (playerId == LocalClientId) return;

            WeaponType wt = (WeaponType)weaponTypeInt;
            OnRemoteMagazineInserted?.Invoke(playerId, wt);

            var avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (var a in avatars)
            {
                if (!a.IsOwner && a.OwnerClientId == playerId)
                {
                    a.OnRemoteMagazineInserted(wt);
                    break;
                }
            }
        }

        public void BroadcastHandSwitched(ulong playerId, bool isLeftHand)
        {
            if (!IsActive) return;
            BroadcastHandSwitchedServerRpc(playerId, isLeftHand);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BroadcastHandSwitchedServerRpc(ulong playerId, bool isLeftHand)
        {
            BroadcastHandSwitchedClientRpc(playerId, isLeftHand);
        }

        [ClientRpc]
        private void BroadcastHandSwitchedClientRpc(ulong playerId, bool isLeftHand)
        {
            if (playerId == LocalClientId) return;

            OnRemoteHandSwitched?.Invoke(playerId, isLeftHand);

            var avatars = FindObjectsByType<NetworkPlayerAvatar>(FindObjectsSortMode.None);
            foreach (var a in avatars)
            {
                if (!a.IsOwner && a.OwnerClientId == playerId)
                {
                    a.OnRemoteHandSwitched(isLeftHand);
                    break;
                }
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReportHitServerRpc(ulong shooterId, int targetId, int zoneInt, float claimedDamage, Vector3 hitPoint, float clientTimestamp)
        {
            using (RangeProfilerMarkers.NetworkLagComp.Auto())
            {
                HitZone zone = (HitZone)zoneInt;

                // Server validates hit with historical lag compensation (~80ms buffer)
                float latencySeconds = Mathf.Clamp(Time.time - clientTimestamp, 0f, _maxLagCompensationSeconds);
                bool isValidHit = ValidateHitWithLagCompensation(targetId, hitPoint, latencySeconds);

                if (isValidHit && _targetManager != null)
                {
                    var target = _targetManager.GetTargetById(targetId);
                    if (target != null)
                    {
                        int points = CalculateScorePoints(zone, claimedDamage);
                        SynchronizedScore.Value += points;

                        if (!_playerScores.ContainsKey(shooterId)) _playerScores[shooterId] = 0;
                        _playerScores[shooterId] += points;

                        // Authoritatively apply damage on server
                        target.ApplyDamage(claimedDamage, zone, hitPoint, Vector3.up, shooterId);

                        // Broadcast confirmation to all clients
                        HitConfirmedClientRpc(shooterId, targetId, zoneInt, claimedDamage, hitPoint);

                        // Synchronize updated target state to all clients
                        bool isDestroyed = target is TargetEntity te ? te.IsDestroyed : (target.Health <= 0f);
                        SyncTargetStateClientRpc(targetId, target.Health, isDestroyed, target.Transform != null ? target.Transform.position : Vector3.zero);
                    }
                }
            }
        }

        private bool ValidateHitWithLagCompensation(int targetId, Vector3 hitPoint, float latency)
        {
            if (_targetManager == null) return true;

            var currentTarget = _targetManager.GetTargetById(targetId);
            if (currentTarget == null) return false;

            // Host shot (near zero latency) -> test against current position directly
            if (latency < 0.015f)
            {
                return Vector3.Distance(hitPoint, currentTarget.Transform.position) <= 2.2f;
            }

            // Remote client with ~80ms latency -> rewind target to historical position at clientTimestamp
            float rewindTime = Time.time - latency;
            TargetHistoricalSnapshot closest = default;
            float minDiff = float.MaxValue;
            bool found = false;

            for (int i = _targetHistory.Count - 1; i >= 0; i--)
            {
                if (_targetHistory[i].TargetId == targetId)
                {
                    float diff = Mathf.Abs(_targetHistory[i].Timestamp - rewindTime);
                    if (diff < minDiff)
                    {
                        minDiff = diff;
                        closest = _targetHistory[i];
                        found = true;
                        if (diff < 0.008f) break;
                    }
                }
            }

            Vector3 testPos = found ? closest.Position : currentTarget.Transform.position;
            float dist = Vector3.Distance(hitPoint, testPos);

            // Distance tolerance accommodates target bounding dimensions + network jitter
            return dist <= 2.2f;
        }

        [ClientRpc]
        private void HitConfirmedClientRpc(ulong shooterId, int targetId, int zoneInt, float damage, Vector3 hitPoint)
        {
            HitZone zone = (HitZone)zoneInt;
            OnNetworkHitValidated?.Invoke(shooterId, targetId, zone, damage);
        }

        [ClientRpc]
        private void SyncTargetStateClientRpc(int targetId, float health, bool isDestroyed, Vector3 position)
        {
            if (_targetManager != null)
            {
                var target = _targetManager.GetTargetById(targetId);
                if (target != null)
                {
                    target.ApplySynchronizedState(health, isDestroyed, position);
                }
            }
        }

        [ClientRpc]
        private void InitialStateSyncClientRpc(int score, float timeRemaining, int[] targetIds, float[] healths, bool[] destroyed, Vector3[] positions, ClientRpcParams clientRpcParams = default)
        {
            if (_targetManager != null && targetIds != null)
            {
                for (int i = 0; i < targetIds.Length; i++)
                {
                    var target = _targetManager.GetTargetById(targetIds[i]);
                    if (target != null)
                    {
                        target.ApplySynchronizedState(healths[i], destroyed[i], positions[i]);
                    }
                }
            }

            _statusMessage = $"Late-Join Synchronized! Score: {score}";
            OnScoreUpdated?.Invoke(LocalClientId, score);
            OnConnectionStateChanged?.Invoke();
        }

        public void SyncScore(ulong clientId, int deltaScore)
        {
            if (IsServer)
            {
                SynchronizedScore.Value += deltaScore;
            }
        }

        private void HandleScoreChanged(int oldScore, int newScore)
        {
            OnScoreUpdated?.Invoke(LocalClientId, newScore);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients != null)
            {
                SynchronizedPlayerCount.Value = NetworkManager.Singleton.ConnectedClients.Count;
            }

            _statusMessage = $"Player {clientId} joined the session!";
            OnPlayerConnected?.Invoke();
            OnConnectionStateChanged?.Invoke();

            // Synchronize full target and session state to late-joining client
            if (IsServer && clientId != NetworkManager.Singleton.LocalClientId && _targetManager != null)
            {
                var targets = _targetManager.AllTargets;
                if (targets != null && targets.Count > 0)
                {
                    int count = targets.Count;
                    int[] ids = new int[count];
                    float[] healths = new float[count];
                    bool[] destroyed = new bool[count];
                    Vector3[] positions = new Vector3[count];

                    for (int i = 0; i < count; i++)
                    {
                        ids[i] = targets[i].TargetId;
                        healths[i] = targets[i].Health;
                        destroyed[i] = targets[i] is TargetEntity te ? te.IsDestroyed : (targets[i].Health <= 0f);
                        positions[i] = targets[i].Transform != null ? targets[i].Transform.position : Vector3.zero;
                    }

                    var clientParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams
                        {
                            TargetClientIds = new ulong[] { clientId }
                        }
                    };

                    InitialStateSyncClientRpc(SynchronizedScore.Value, RoundTimeRemaining.Value, ids, healths, destroyed, positions, clientParams);
                }
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients != null)
            {
                SynchronizedPlayerCount.Value = NetworkManager.Singleton.ConnectedClients.Count;
            }

            if (clientId == LocalClientId)
            {
                _statusMessage = "Disconnected from host";
            }
            else
            {
                _statusMessage = $"Player {clientId} left the session";
            }
            _playerScores.Remove(clientId);
            OnPlayerDisconnected?.Invoke();
            OnConnectionStateChanged?.Invoke();
        }

        private int CalculateScorePoints(HitZone zone, float damage)
        {
            int baseScore = Mathf.RoundToInt(damage);
            switch (zone)
            {
                case HitZone.Head: return baseScore * 3;
                case HitZone.WeakPoint: return baseScore * 4;
                case HitZone.Limb: return Mathf.RoundToInt(baseScore * 0.8f);
                case HitZone.Body:
                default:
                    return baseScore;
            }
        }
    }
}
