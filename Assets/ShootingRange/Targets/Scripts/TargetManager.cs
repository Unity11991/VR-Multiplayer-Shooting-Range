using System;
using System.Collections.Generic;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Targets
{
    /// <summary>
    /// Centralized Target Simulation Manager.
    /// Manages 300 targets simultaneously in a single centralized update loop without per-target Update overhead.
    /// Fully pooled: zero Instantiate or Destroy calls during gameplay.
    /// All target materials are GPU instanced and SRP Batcher compatible.
    /// </summary>
    public class TargetManager : MonoBehaviour, ITargetSimulation
    {
        public const int MaxSimulatedTargets = 350;

        [Header("Target Pool Configuration")]
        [SerializeField] private bool _autoPopulate300TargetsOnAwake = true;
        [SerializeField] private int _targetPoolCount = 300;
        [SerializeField] private int _activeTargetCount = 8; // Default 8 targets active in scene

        [Header("Target Materials (GPU Instanced & SRP Batcher)")]
        [SerializeField] private Material _cardboardMaterial;
        [SerializeField] private Material _redBullseyeMaterial;
        [SerializeField] private Material _blueZoneMaterial;
        [SerializeField] private Material _armourPlateMaterial;
        [SerializeField] private Material _weakPointMaterial;
        [SerializeField] private Material _steelStandMaterial;

        [Header("Runtime Simulation State")]
        [SerializeField] private int _totalScore = 0;

        private readonly List<ITargetEntity> _activeTargets = new List<ITargetEntity>(MaxSimulatedTargets);
        private readonly List<ITargetEntity> _allRegisteredTargets = new List<ITargetEntity>(MaxSimulatedTargets);
        private int _nextTargetId = 1;
        private INetworkSession _networkSession;

        private INetworkSession EnsureNetworkSession()
        {
            if (_networkSession != null) return _networkSession;
            if (ShootingRangeCompositionRoot.Container != null)
            {
                ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession);
            }
            if (_networkSession == null)
            {
                var provs = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                foreach (var p in provs)
                {
                    if (p is INetworkSession ns) { _networkSession = ns; break; }
                }
            }
            return _networkSession;
        }

        public int ActiveTargetCount => _activeTargets.Count;
        public int TotalScore => _totalScore;
        public int TotalTargetPoolCount => _allRegisteredTargets.Count;
        public int ActiveTargetLimit => _activeTargetCount;
        public IReadOnlyList<ITargetEntity> AllTargets => _allRegisteredTargets;

        public ITargetEntity GetTargetById(int id)
        {
            for (int i = 0; i < _allRegisteredTargets.Count; i++)
            {
                if (_allRegisteredTargets[i].TargetId == id) return _allRegisteredTargets[i];
            }
            return null;
        }

        public event Action<int> OnScoreChanged;
        public event Action<ITargetEntity, float, HitZone, ulong> OnTargetHitEvent;
        public event Action<int> OnActiveTargetCountChanged;

        private void Awake()
        {
            // 1. Auto-discover pre-existing scene targets
            var sceneTargets = GetComponentsInChildren<TargetEntity>(true);
            foreach (var target in sceneTargets)
            {
                RegisterTarget(target);
                if (target.TargetId >= _nextTargetId)
                {
                    _nextTargetId = target.TargetId + 1;
                }
            }

            // 2. Pre-populate pool up to exactly 300 targets if not already in scene
            if (_autoPopulate300TargetsOnAwake && _allRegisteredTargets.Count < _targetPoolCount)
            {
                Prepopulate300TargetPool(_targetPoolCount - _allRegisteredTargets.Count);
            }

            // 3. Keep exactly _activeTargetCount (8 by default) active in scene, pooling the rest!
            SetActiveTargetCount(_activeTargetCount);
        }

        public void SetActiveTargetCount(int newCount)
        {
            newCount = Mathf.Clamp(newCount, 1, _allRegisteredTargets.Count);

            var ns = EnsureNetworkSession();
            if (ns != null && ns.IsActive && !ns.IsHost)
            {
                // Network client delegates request to authoritative host
                ns.RequestChangeActiveTargetCount(newCount);
                return;
            }

            // Server or singleplayer applies locally and notifies network session
            ApplyActiveTargetCount(newCount);
            if (ns != null && ns.IsActive && ns.IsHost)
            {
                ns.RequestChangeActiveTargetCount(newCount);
            }
        }

        public void ApplyActiveTargetCount(int newCount)
        {
            newCount = Mathf.Clamp(newCount, 1, _allRegisteredTargets.Count);
            _activeTargetCount = newCount;

            _activeTargets.Clear();
            for (int i = 0; i < _allRegisteredTargets.Count; i++)
            {
                var target = _allRegisteredTargets[i];
                if (target == null || target.Transform == null) continue;

                bool shouldBeActive = i < _activeTargetCount;
                target.Transform.gameObject.SetActive(shouldBeActive);

                if (shouldBeActive)
                {
                    _activeTargets.Add(target);
                }
            }

            OnActiveTargetCountChanged?.Invoke(_activeTargetCount);
        }

        public void IncreaseTargets(int step = 4)
        {
            SetActiveTargetCount(_activeTargetCount + step);
        }

        public void DecreaseTargets(int step = 4)
        {
            SetActiveTargetCount(_activeTargetCount - step);
        }

        private void Update()
        {
            StepTargets(Time.deltaTime);
        }

        public void StepTargets(float deltaTime)
        {
            using (RangeProfilerMarkers.TargetsSim.Auto())
            {
                var ns = EnsureNetworkSession();
                bool isNetworkClient = ns != null && ns.IsActive && !ns.IsHost;

                int count = _activeTargets.Count;
                for (int i = 0; i < count; i++)
                {
                    _activeTargets[i].IsNetworkControlled = isNetworkClient;
                    _activeTargets[i].Step(deltaTime);
                }
            }
        }

        public void RegisterTarget(ITargetEntity target)
        {
            if (target == null || _allRegisteredTargets.Contains(target)) return;

            _allRegisteredTargets.Add(target);
            _activeTargets.Add(target);

            target.OnTargetHit += HandleTargetHit;
            target.OnTargetDestroyed += HandleTargetDestroyed;
        }

        public void UnregisterTarget(ITargetEntity target)
        {
            if (target == null) return;

            target.OnTargetHit -= HandleTargetHit;
            target.OnTargetDestroyed -= HandleTargetDestroyed;

            _activeTargets.Remove(target);
            _allRegisteredTargets.Remove(target);
        }

        public void ResetAllTargets()
        {
            int count = _allRegisteredTargets.Count;
            for (int i = 0; i < count; i++)
            {
                _allRegisteredTargets[i].ResetTarget();
            }
        }

        private void HandleTargetHit(ITargetEntity target, float damage, HitZone zone, ulong shooterClientId)
        {
            int points = CalculateScore(zone, damage);
            _totalScore += points;
            OnScoreChanged?.Invoke(_totalScore);
            OnTargetHitEvent?.Invoke(target, damage, zone, shooterClientId);
        }

        private void HandleTargetDestroyed(ITargetEntity target, ulong shooterClientId)
        {
            _totalScore += 250; // Bonus for target destruction
            OnScoreChanged?.Invoke(_totalScore);
        }

        private int CalculateScore(HitZone zone, float damage)
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

        /// <summary>
        /// Pre-populates the 300 targets pool across downrange tactical tiers.
        /// 75 Pop-Up, 75 Strafing, 75 Zig-Zag, 75 Armoured with weak points.
        /// Zero Instantiate/Destroy during gameplay.
        /// </summary>
        public void Prepopulate300TargetPool(int targetsToSpawn)
        {
            EnsureMaterials();

            Transform poolRoot = transform.Find("PooledTargetField_300");
            if (poolRoot == null)
            {
                var go = new GameObject("PooledTargetField_300");
                go.transform.SetParent(transform, false);
                poolRoot = go.transform;
            }

            int createdCount = 0;

            // Spawn the 8 primary hero targets first (2 of each of the 4 types)
            if (_allRegisteredTargets.Count == 0 && targetsToSpawn >= 8)
            {
                // 2 Pop-Up (Lane 1, Left)
                SpawnPopUpTarget(poolRoot, _nextTargetId++, new Vector3(-3.8f, 1.4f, 11.0f));
                SpawnPopUpTarget(poolRoot, _nextTargetId++, new Vector3(-3.8f, 1.4f, 19.0f));

                // 2 Strafing (Lane 2, Center)
                SpawnStrafingTarget(poolRoot, _nextTargetId++, new Vector3(0.0f, 1.5f, 13.5f));
                SpawnStrafingTarget(poolRoot, _nextTargetId++, new Vector3(0.0f, 1.5f, 21.5f));

                // 2 Zig-Zag (Lane 3 & 2)
                SpawnZigZagTarget(poolRoot, _nextTargetId++, new Vector3(3.6f, 1.4f, 15.5f));
                SpawnZigZagTarget(poolRoot, _nextTargetId++, new Vector3(-1.8f, 1.4f, 24.0f));

                // 2 Armoured with Weak Points (Lane 3 & 2, Long Range)
                SpawnArmouredTarget(poolRoot, _nextTargetId++, new Vector3(3.6f, 1.6f, 26.5f));
                SpawnArmouredTarget(poolRoot, _nextTargetId++, new Vector3(0.8f, 1.6f, 29.0f));

                createdCount += 8;
            }

            // Fill the rest of the 300 targets pool across downrange grid
            int totalCols = 15;
            int totalRows = 20;

            for (int r = 0; r < totalRows && createdCount < targetsToSpawn; r++)
            {
                float zPos = 8.5f + r * 1.08f;
                int targetType = r / 5; // 0 = Pop-Up (r 0-4), 1 = Strafing (r 5-9), 2 = Zig-Zag (r 10-14), 3 = Armoured (r 15-19)

                for (int c = 0; c < totalCols && createdCount < targetsToSpawn; c++)
                {
                    float xPos = -6.3f + c * 0.90f + ((r % 2 == 1) ? 0.45f : 0.0f);
                    if (xPos > 6.6f) xPos -= 13.0f;

                    float yPos = 1.35f + ((r % 3) * 0.22f);
                    Vector3 pos = new Vector3(xPos, yPos, zPos);
                    int id = _nextTargetId++;

                    switch (targetType)
                    {
                        case 0:
                            SpawnPopUpTarget(poolRoot, id, pos);
                            break;
                        case 1:
                            SpawnStrafingTarget(poolRoot, id, pos);
                            break;
                        case 2:
                            SpawnZigZagTarget(poolRoot, id, pos);
                            break;
                        case 3:
                        default:
                            SpawnArmouredTarget(poolRoot, id, pos);
                            break;
                    }
                    createdCount++;
                }
            }
        }

        private void SpawnPopUpTarget(Transform parent, int id, Vector3 pos)
        {
            var root = new GameObject($"PopUpTarget_{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var pop = root.AddComponent<PopUpTarget>();

            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localScale = new Vector3(0.48f, 0.85f, 0.04f);
            torso.GetComponent<Renderer>().sharedMaterial = _cardboardMaterial;
            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);

            var vitalsRing = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vitalsRing.name = "VitalsRing";
            vitalsRing.transform.SetParent(torso.transform, false);
            vitalsRing.transform.localPosition = new Vector3(0f, 0.12f, -0.55f);
            vitalsRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            vitalsRing.transform.localScale = new Vector3(0.45f, 0.05f, 0.45f);
            vitalsRing.GetComponent<Renderer>().sharedMaterial = _redBullseyeMaterial;
            var ringCol = vitalsRing.GetComponent<Collider>();
            if (ringCol != null) DestroyImmediate(ringCol);
            var ringHitCol = vitalsRing.AddComponent<SphereCollider>();
            ringHitCol.radius = 0.28f;
            var headZone = vitalsRing.AddComponent<TargetHitZone>();
            headZone.Configure(HitZone.Head, 2.5f, 250);

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "StandPost";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, -0.9f, 0f);
            stand.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            stand.GetComponent<Renderer>().sharedMaterial = _steelStandMaterial;
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) DestroyImmediate(standCol);

            pop.Initialize(id, pos, Quaternion.identity);
            RegisterTarget(pop);
        }

        private void SpawnStrafingTarget(Transform parent, int id, Vector3 pos)
        {
            var root = new GameObject($"StrafingTarget_{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var strafe = root.AddComponent<StrafingTarget>();

            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Silhouette";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localScale = new Vector3(0.46f, 0.65f, 0.46f);
            torso.GetComponent<Renderer>().sharedMaterial = _blueZoneMaterial;
            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "CenterCore";
            core.transform.SetParent(torso.transform, false);
            core.transform.localPosition = new Vector3(0f, 0.15f, -0.45f);
            core.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
            core.GetComponent<Renderer>().sharedMaterial = _redBullseyeMaterial;
            var coreZone = core.AddComponent<TargetHitZone>();
            coreZone.Configure(HitZone.Head, 2.5f, 250);

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "StandPost";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            stand.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            stand.GetComponent<Renderer>().sharedMaterial = _steelStandMaterial;
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) DestroyImmediate(standCol);

            strafe.Initialize(id, pos, Quaternion.identity);
            RegisterTarget(strafe);
        }

        private void SpawnZigZagTarget(Transform parent, int id, Vector3 pos)
        {
            var root = new GameObject($"ZigZagTarget_{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var zigzag = root.AddComponent<ZigZagTarget>();

            var torso = GameObject.CreatePrimitive(PrimitiveType.Cube);
            torso.name = "Torso";
            torso.transform.SetParent(root.transform, false);
            torso.transform.localScale = new Vector3(0.50f, 0.95f, 0.05f);
            torso.GetComponent<Renderer>().sharedMaterial = _cardboardMaterial;
            var hitZone = torso.AddComponent<TargetHitZone>();
            hitZone.Configure(HitZone.Body, 1.0f, 100);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(torso.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.65f, 0f);
            head.transform.localScale = new Vector3(0.5f, 0.35f, 0.95f);
            head.GetComponent<Renderer>().sharedMaterial = _redBullseyeMaterial;
            var headZone = head.AddComponent<TargetHitZone>();
            headZone.Configure(HitZone.Head, 2.5f, 250);

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "StandPost";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, -0.95f, 0f);
            stand.transform.localScale = new Vector3(0.05f, 0.5f, 0.05f);
            stand.GetComponent<Renderer>().sharedMaterial = _steelStandMaterial;
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) DestroyImmediate(standCol);

            zigzag.Initialize(id, pos, Quaternion.identity);
            RegisterTarget(zigzag);
        }

        private void SpawnArmouredTarget(Transform parent, int id, Vector3 pos)
        {
            var root = new GameObject($"ArmouredTarget_{id}");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;

            var armour = root.AddComponent<ArmouredTarget>();

            var shield = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shield.name = "BallisticShieldPlate";
            shield.transform.SetParent(root.transform, false);
            shield.transform.localScale = new Vector3(0.70f, 1.45f, 0.12f);
            shield.GetComponent<Renderer>().sharedMaterial = _armourPlateMaterial;
            var shieldZone = shield.AddComponent<TargetHitZone>();
            shieldZone.Configure(HitZone.Body, 0.10f, 50); // Heavy armor absorbs 90% damage

            var weakPoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            weakPoint.name = "ArmourWeakPoint";
            weakPoint.transform.SetParent(shield.transform, false);
            weakPoint.transform.localPosition = new Vector3(0f, 0.15f, -0.65f);
            weakPoint.transform.localScale = new Vector3(0.32f, 0.32f, 0.32f);
            weakPoint.GetComponent<Renderer>().sharedMaterial = _weakPointMaterial;
            var weakZone = weakPoint.AddComponent<TargetHitZone>();
            weakZone.Configure(HitZone.WeakPoint, 3.5f, 350); // Critical weak point

            var stand = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stand.name = "HeavyStandPost";
            stand.transform.SetParent(root.transform, false);
            stand.transform.localPosition = new Vector3(0f, -1.15f, 0f);
            stand.transform.localScale = new Vector3(0.08f, 0.6f, 0.08f);
            stand.GetComponent<Renderer>().sharedMaterial = _steelStandMaterial;
            var standCol = stand.GetComponent<Collider>();
            if (standCol != null) DestroyImmediate(standCol);

            armour.Initialize(id, pos, Quaternion.identity);
            RegisterTarget(armour);
        }

        private void EnsureMaterials()
        {
            if (_cardboardMaterial == null) _cardboardMaterial = GetOrCreateMaterial("Mat_TargetCardboard", new Color(0.82f, 0.74f, 0.58f));
            if (_redBullseyeMaterial == null) _redBullseyeMaterial = GetOrCreateMaterial("Mat_TargetRedBullseye", new Color(0.88f, 0.15f, 0.12f));
            if (_blueZoneMaterial == null) _blueZoneMaterial = GetOrCreateMaterial("Mat_TargetBlueZone", new Color(0.15f, 0.35f, 0.70f));
            if (_armourPlateMaterial == null) _armourPlateMaterial = GetOrCreateMaterial("Mat_ArmourPlate", new Color(0.32f, 0.35f, 0.38f), 0.90f);
            if (_weakPointMaterial == null) _weakPointMaterial = GetOrCreateMaterial("Mat_WeakPointCore", new Color(0.1f, 0.9f, 1f), 0f, true);
            if (_steelStandMaterial == null) _steelStandMaterial = GetOrCreateMaterial("Mat_TacticalSteel", new Color(0.18f, 0.20f, 0.22f), 0.85f);
        }

        private Material GetOrCreateMaterial(string name, Color color, float metallic = 0.05f, bool isEmissive = false)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Standard");

            var mat = new Material(shader) { name = name };
            mat.enableInstancing = true;

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

            if (isEmissive)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 2.5f);
            }

            return mat;
        }

        public void SetTargetMaterials(Material cardboard, Material redBullseye, Material blueZone, Material armour, Material weakPoint, Material steel)
        {
            _cardboardMaterial = cardboard;
            _redBullseyeMaterial = redBullseye;
            _blueZoneMaterial = blueZone;
            _armourPlateMaterial = armour;
            _weakPointMaterial = weakPoint;
            _steelStandMaterial = steel;

            if (_cardboardMaterial != null) _cardboardMaterial.enableInstancing = true;
            if (_redBullseyeMaterial != null) _redBullseyeMaterial.enableInstancing = true;
            if (_blueZoneMaterial != null) _blueZoneMaterial.enableInstancing = true;
            if (_armourPlateMaterial != null) _armourPlateMaterial.enableInstancing = true;
            if (_weakPointMaterial != null) _weakPointMaterial.enableInstancing = true;
            if (_steelStandMaterial != null) _steelStandMaterial.enableInstancing = true;
        }
    }
}

