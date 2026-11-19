using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using ShootingRange.Core;

namespace ShootingRange.Ballistics
{
    /// <summary>
    /// Centralized Ballistics Manager executing Burst/Jobs projectile flight,
    /// continuous anti-tunneling collision sweeps, penetration, and ricochet.
    /// Supports 1000+ concurrent active bullets with zero per-frame managed allocations.
    /// </summary>
    public class BallisticsManager : MonoBehaviour, IBallisticsSimulation
    {
        public const int MaxConcurrentBullets = 1024;

        [Header("Environmental Parameters")]
        [SerializeField] private Vector3 _gravity = new Vector3(0f, -9.81f, 0f);
        [SerializeField] private Vector3 _wind = Vector3.zero;
        [SerializeField] private float _airDensity = 1.225f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        [Header("Default Surface Fallbacks")]
        [SerializeField] private SurfaceMaterialDefinition _defaultConcrete;
        [SerializeField] private SurfaceMaterialDefinition _defaultMetal;
        [SerializeField] private SurfaceMaterialDefinition _defaultWood;
        [SerializeField] private SurfaceMaterialDefinition _defaultGlass;

        [Header("Visual Trails")]
        [SerializeField] private GameObject _tracerPrefab;
        [SerializeField] private int _prewarmTracers = 64;

        private NativeArray<BallisticBulletData> _bulletDataArray;
        private int _activeBulletCount;
        private int _nextBulletId = 1;

        // Visual tracer pool
        private readonly List<LineRenderer> _tracerPool = new List<LineRenderer>();
        private readonly Dictionary<int, LineRenderer> _activeTracers = new Dictionary<int, LineRenderer>();
        private INetworkSession _networkSession;

        public int ActiveBulletCount => _activeBulletCount;

        public event Action<BulletHitInfo> OnBulletHit;
        public event Action<Vector3, Vector3, SurfaceType> OnRicochet;

        private void Awake()
        {
            _bulletDataArray = new NativeArray<BallisticBulletData>(MaxConcurrentBullets, Allocator.Persistent);
            InitializeTracerPool();
        }

        private void Start()
        {
            if (_networkSession == null && ShootingRangeCompositionRoot.Container != null)
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

            if (_networkSession != null)
            {
                _networkSession.OnRemoteShotFired += HandleRemoteShotFired;
            }
        }

        private void OnDestroy()
        {
            if (_networkSession != null)
            {
                _networkSession.OnRemoteShotFired -= HandleRemoteShotFired;
            }
            if (_bulletDataArray.IsCreated)
            {
                _bulletDataArray.Dispose();
            }
        }

        private void HandleRemoteShotFired(ulong shooterId, Vector3 muzzlePos, Vector3 velocity, WeaponType weaponType)
        {
            bool isRifle = weaponType == WeaponType.Rifle;
            var spawnParams = new BulletSpawnParams
            {
                Origin = muzzlePos,
                Velocity = velocity,
                MassKg = isRifle ? 0.004f : 0.008f,
                CaliberMm = isRifle ? 5.56f : 9.0f,
                BaseDamage = isRifle ? 45f : 25f,
                MaxRange = isRifle ? 800f : 200f,
                ShooterClientId = shooterId,
                WeaponId = (int)shooterId + 1000
            };
            SpawnBullet(spawnParams);

            // Procedural 3D gunshot sound at remote muzzle position
            var clip = ProceduralAudioSynthesizer.GetGunshotClip("RemoteShot", isRifle);
            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, muzzlePos, 0.85f);
            }
        }

        private void InitializeTracerPool()
        {
            for (int i = 0; i < _prewarmTracers; i++)
            {
                var tracer = CreateNewTracer();
                tracer.gameObject.SetActive(false);
                _tracerPool.Add(tracer);
            }
        }

        private LineRenderer CreateNewTracer()
        {
            var obj = new GameObject("BulletTracer");
            obj.transform.SetParent(transform);
            var lr = obj.AddComponent<LineRenderer>();
            lr.startWidth = 0.015f;
            lr.endWidth = 0.005f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(1f, 0.85f, 0.4f, 0.8f);
            lr.endColor = new Color(1f, 0.4f, 0.1f, 0.0f);
            lr.positionCount = 2;
            return lr;
        }

        public void SetWind(Vector3 windVector)
        {
            _wind = windVector;
        }

        public void SpawnBullet(in BulletSpawnParams spawnParams)
        {
            // Find free slot in NativeArray
            int slot = -1;
            for (int i = 0; i < MaxConcurrentBullets; i++)
            {
                if (!_bulletDataArray[i].IsActive)
                {
                    slot = i;
                    break;
                }
            }

            if (slot == -1) return; // Pool saturated

            float initialSpeed = spawnParams.Velocity.magnitude;
            float initialEnergy = 0.5f * spawnParams.MassKg * (initialSpeed * initialSpeed);

            var bullet = new BallisticBulletData
            {
                IsActive = true,
                Position = spawnParams.Origin,
                Velocity = spawnParams.Velocity,
                PredictedNextPosition = spawnParams.Origin,
                MassKg = spawnParams.MassKg,
                CaliberMm = spawnParams.CaliberMm,
                BaseDamage = spawnParams.BaseDamage,
                CurrentEnergyJoules = initialEnergy,
                DistanceTraveled = 0f,
                MaxRange = spawnParams.MaxRange,
                TimeAlive = 0f,
                ShooterClientId = spawnParams.ShooterClientId,
                WeaponId = spawnParams.WeaponId,
                BulletId = _nextBulletId++,
                PenetrationCount = 0
            };

            _bulletDataArray[slot] = bullet;
            _activeBulletCount++;

            // Visual tracer allocation
            if (_tracerPool.Count > 0)
            {
                var tracer = _tracerPool[_tracerPool.Count - 1];
                _tracerPool.RemoveAt(_tracerPool.Count - 1);
                tracer.gameObject.SetActive(true);
                tracer.SetPosition(0, bullet.Position);
                tracer.SetPosition(1, bullet.Position);
                _activeTracers[bullet.BulletId] = tracer;
            }
        }

        private void FixedUpdate()
        {
            StepSimulation(Time.fixedDeltaTime);
        }

        public void StepSimulation(float deltaTime)
        {
            if (_activeBulletCount == 0) return;

            using (RangeProfilerMarkers.BallisticsSim.Auto())
            {
                // Step 1: Burst-compiled kinematics update (gravity, drag, wind)
                var job = new BallisticsStepJob
                {
                    Bullets = _bulletDataArray,
                    DeltaTime = deltaTime,
                    Gravity = _gravity,
                    Wind = _wind,
                    AirDensity = _airDensity
                };

                JobHandle handle = job.Schedule(MaxConcurrentBullets, 64);
                handle.Complete();

                // Step 2: Continuous sweep collision detection to prevent tunneling through thin barriers
                ResolveCollisions();
            }
        }

        private void ResolveCollisions()
        {
            int currentActive = 0;

            for (int i = 0; i < MaxConcurrentBullets; i++)
            {
                var bullet = _bulletDataArray[i];
                if (!bullet.IsActive) continue;

                Vector3 startPos = bullet.Position;
                Vector3 endPos = bullet.PredictedNextPosition;
                Vector3 travelVector = endPos - startPos;
                float travelDistance = travelVector.magnitude;

                if (travelDistance > 0.0001f)
                {
                    Ray sweepRay = new Ray(startPos, travelVector.normalized);
                    if (Physics.Raycast(sweepRay, out RaycastHit hit, travelDistance, _collisionMask, QueryTriggerInteraction.Ignore))
                    {
                        bool bulletTerminated = HandleHit(ref bullet, in hit, travelVector.normalized);
                        if (bulletTerminated)
                        {
                            bullet.IsActive = false;
                            RecycleTracer(bullet.BulletId);
                        }
                    }
                    else
                    {
                        // No collision: advance position
                        bullet.Position = bullet.PredictedNextPosition;
                        UpdateTracer(bullet.BulletId, startPos, bullet.Position);
                    }
                }

                _bulletDataArray[i] = bullet;
                if (bullet.IsActive) currentActive++;
            }

            _activeBulletCount = currentActive;
        }

        private bool HandleHit(ref BallisticBulletData bullet, in RaycastHit hit, Vector3 incomingDirection)
        {
            using (RangeProfilerMarkers.BallisticsCollision.Auto())
            {
                // Surface material identification
                SurfaceMaterialDefinition surfaceMat = null;
                var surfaceId = hit.collider.GetComponentInParent<SurfaceIdentifier>();
                if (surfaceId != null && surfaceId.MaterialDefinition != null)
                {
                    surfaceMat = surfaceId.MaterialDefinition;
                }
                else
                {
                    surfaceMat = GetFallbackSurface(surfaceId != null ? surfaceId.Type : SurfaceType.Concrete);
                }

                SurfaceType surfaceType = surfaceMat != null ? surfaceMat.Type : SurfaceType.Concrete;
                float angleOfIncidence = Vector3.Angle(-incomingDirection, hit.normal); // 0 = perpendicular, 90 = parallel grazing

                // Check 1: Ricochet
                if (surfaceMat != null && surfaceMat.CheckRicochet(90f - angleOfIncidence, bullet.CurrentEnergyJoules))
                {
                    Vector3 reflection = Vector3.Reflect(incomingDirection, hit.normal);
                    bullet.Velocity = reflection * (math.length(bullet.Velocity) * surfaceMat.RicochetRestitution);
                    bullet.Position = hit.point + hit.normal * 0.01f;
                    bullet.PredictedNextPosition = bullet.Position;

                    OnRicochet?.Invoke(hit.point, reflection, surfaceType);

                    var ricochetHitInfo = new BulletHitInfo
                    {
                        Point = hit.point,
                        Normal = hit.normal,
                        IncomingDirection = incomingDirection,
                        Surface = surfaceType,
                        Zone = HitZone.None,
                        DamageApplied = 0f,
                        RemainingEnergy = bullet.CurrentEnergyJoules,
                        RicochetOccurred = true,
                        Penetrated = false,
                        HitCollider = hit.collider,
                        ShooterClientId = bullet.ShooterClientId
                    };
                    OnBulletHit?.Invoke(ricochetHitInfo);
                    PlayImpactSound(hit.point, surfaceType, true);
                    return false; // Continues flight after bounce
                }

                // Check 2: Penetration
                float barrierThickness = surfaceId != null ? surfaceId.ThicknessMm : 25.0f;
                if (bullet.PenetrationCount < 2 && surfaceMat != null &&
                    surfaceMat.CheckPenetration(bullet.CurrentEnergyJoules, barrierThickness, out float exitEnergy))
                {
                    bullet.PenetrationCount++;
                    bullet.CurrentEnergyJoules = exitEnergy;
                    float newSpeed = Mathf.Sqrt((2f * exitEnergy) / Mathf.Max(bullet.MassKg, 0.001f));
                    bullet.Velocity = incomingDirection * newSpeed;
                    bullet.Position = hit.point + incomingDirection * (barrierThickness * 0.001f + 0.02f);
                    bullet.PredictedNextPosition = bullet.Position;

                    var penetrationHitInfo = new BulletHitInfo
                    {
                        Point = hit.point,
                        Normal = hit.normal,
                        IncomingDirection = incomingDirection,
                        Surface = surfaceType,
                        Zone = HitZone.None,
                        DamageApplied = bullet.BaseDamage * 0.5f,
                        RemainingEnergy = exitEnergy,
                        RicochetOccurred = false,
                        Penetrated = true,
                        HitCollider = hit.collider,
                        ShooterClientId = bullet.ShooterClientId
                    };
                    OnBulletHit?.Invoke(penetrationHitInfo);
                    return false; // Continues flight after punch-through
                }

                // Check 3: Full Terminal Impact (Target damage & surface stoppage)
                var hitZone = HitZone.Body;
                float damageMultiplier = 1.0f;

                // Hit zone evaluation if target collider
                var targetHitZone = hit.collider.GetComponent<ITargetHitZone>();
                if (targetHitZone != null)
                {
                    hitZone = targetHitZone.Zone;
                    damageMultiplier = targetHitZone.DamageMultiplier;
                }
                else
                {
                    string colName = hit.collider.name.ToLower();
                    if (colName.Contains("head") || colName.Contains("bullseye")) { hitZone = HitZone.Head; damageMultiplier = 2.5f; }
                    else if (colName.Contains("limb") || colName.Contains("arm") || colName.Contains("leg")) { hitZone = HitZone.Limb; damageMultiplier = 0.7f; }
                    else if (colName.Contains("weak")) { hitZone = HitZone.WeakPoint; damageMultiplier = 3.5f; }
                }

                float appliedDamage = bullet.BaseDamage * damageMultiplier;

                // Deliver damage to target entity if present
                var targetEntity = hit.collider.GetComponentInParent<ITargetEntity>();
                if (targetEntity != null)
                {
                    if (_networkSession == null && ShootingRangeCompositionRoot.Container != null)
                    {
                        ShootingRangeCompositionRoot.Container.TryResolve(out _networkSession);
                    }

                    if (_networkSession != null && _networkSession.IsActive)
                    {
                        // In multiplayer: if host/server, apply directly & sync; if client, predict VFX locally and report hit to server
                        if (_networkSession.IsHost)
                        {
                            targetEntity.ApplyDamage(appliedDamage, hitZone, hit.point, hit.normal, bullet.ShooterClientId);
                        }
                        _networkSession.ReportHit(bullet.ShooterClientId, targetEntity.TargetId, hitZone, appliedDamage, hit.point);
                    }
                    else
                    {
                        // Standalone offline
                        targetEntity.ApplyDamage(appliedDamage, hitZone, hit.point, hit.normal, bullet.ShooterClientId);
                    }
                }

                var terminalHitInfo = new BulletHitInfo
                {
                    Point = hit.point,
                    Normal = hit.normal,
                    IncomingDirection = incomingDirection,
                    Surface = surfaceType,
                    Zone = hitZone,
                    DamageApplied = appliedDamage,
                    RemainingEnergy = bullet.CurrentEnergyJoules,
                    RicochetOccurred = false,
                    Penetrated = false,
                    HitCollider = hit.collider,
                    ShooterClientId = bullet.ShooterClientId
                };
                OnBulletHit?.Invoke(terminalHitInfo);
                PlayImpactSound(hit.point, surfaceType, false, hitZone);

                return true; // Terminate bullet
            }
        }

        private void PlayImpactSound(Vector3 position, SurfaceType surface, bool isRicochet, HitZone zone = HitZone.None)
        {
            AudioClip clip;
            if (isRicochet)
            {
                clip = ProceduralAudioSynthesizer.GetRicochetWhineClip();
            }
            else if (surface == SurfaceType.Metal || zone != HitZone.None)
            {
                clip = ProceduralAudioSynthesizer.GetSteelPingClip();
            }
            else
            {
                clip = ProceduralAudioSynthesizer.GetImpactThudClip();
            }

            if (clip != null)
            {
                AudioSource.PlayClipAtPoint(clip, position, 0.75f);
            }
        }

        private SurfaceMaterialDefinition GetFallbackSurface(SurfaceType type)
        {
            switch (type)
            {
                case SurfaceType.Wood: return _defaultWood;
                case SurfaceType.Metal: return _defaultMetal;
                case SurfaceType.Glass: return _defaultGlass;
                case SurfaceType.Concrete:
                default: return _defaultConcrete;
            }
        }

        private void UpdateTracer(int bulletId, Vector3 fromPos, Vector3 toPos)
        {
            if (_activeTracers.TryGetValue(bulletId, out var tracer))
            {
                tracer.SetPosition(0, fromPos);
                tracer.SetPosition(1, toPos);
            }
        }

        private void RecycleTracer(int bulletId)
        {
            if (_activeTracers.TryGetValue(bulletId, out var tracer))
            {
                tracer.gameObject.SetActive(false);
                _activeTracers.Remove(bulletId);
                _tracerPool.Add(tracer);
            }
        }
    }
}

