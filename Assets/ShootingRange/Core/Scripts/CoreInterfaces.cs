using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShootingRange.Core
{
    public static class UIInteractionState
    {
        public static bool IsPointerOverUI = false;
        public static bool IsAnyUIPanelOpen = false;
    }

    public interface ITrackingProvider
    {
        Vector3 HeadPosition { get; }
        Quaternion HeadRotation { get; }
        Vector3 HeadVelocity { get; }

        Vector3 GetHandPosition(Handedness hand);
        Quaternion GetHandRotation(Handedness hand);
        Vector3 GetHandVelocity(Handedness hand);
        Vector3 GetHandAngularVelocity(Handedness hand);
        bool IsHandTracked(Handedness hand);

        float PlayerHeightOffset { get; set; }
        bool IsSeatedMode { get; set; }
        bool IsLeftHanded { get; set; }

        void Teleport(Vector3 floorPosition, Quaternion rotation);
    }

    public static class SpawnPointDefinitions
    {
        public static readonly Vector3[] Positions = new Vector3[]
        {
            new Vector3(0.0f, 0f, 0.0f),    // Index 0: Lane 2 (Center Stall) - Primary Host Stall
            new Vector3(-3.5f, 0f, 0.0f),   // Index 1: Lane 1 (Left Stall)   - Guest Player 2 Stall
            new Vector3(3.5f, 0f, 0.0f),    // Index 2: Lane 3 (Right Stall)  - Guest Player 3 Stall
            new Vector3(-1.75f, 0f, -2.5f)  // Index 3: Ready Area (Stall 4)  - Guest Player 4 Stall
        };

        public static readonly Quaternion[] Rotations = new Quaternion[]
        {
            Quaternion.identity,
            Quaternion.identity,
            Quaternion.identity,
            Quaternion.identity
        };

        public static readonly string[] LaneNames = new string[]
        {
            "LANE 2 (CENTER)",
            "LANE 1 (LEFT)",
            "LANE 3 (RIGHT)",
            "READY AREA"
        };

        public static Vector3 GetSpawnPosition(ulong clientId)
        {
            int idx = (int)(clientId % (ulong)Positions.Length);
            return Positions[idx];
        }

        public static Quaternion GetSpawnRotation(ulong clientId)
        {
            int idx = (int)(clientId % (ulong)Rotations.Length);
            return Rotations[idx];
        }

        public static string GetLaneName(ulong clientId)
        {
            int idx = (int)(clientId % (ulong)LaneNames.Length);
            return LaneNames[idx];
        }
    }

    public interface IControllerInputProvider
    {
        float GetTrigger(Handedness hand);
        bool GetTriggerDown(Handedness hand);
        bool GetTriggerUp(Handedness hand);

        float GetGrip(Handedness hand);
        bool GetGripDown(Handedness hand);
        bool GetGripUp(Handedness hand);

        bool GetPrimaryButton(Handedness hand);
        bool GetPrimaryButtonDown(Handedness hand);
        bool GetSecondaryButton(Handedness hand);
        bool GetSecondaryButtonDown(Handedness hand);

        Vector2 GetThumbstick(Handedness hand);
        bool GetThumbstickClick(Handedness hand);

        void TriggerHaptic(Handedness hand, float amplitude, float durationSeconds);
    }

    public interface IHandPoseProvider
    {
        HandPoseType GetCurrentPose(Handedness hand);
        float GetFingerCurl(Handedness hand, FingerType finger);
    }

    public interface IGrabbable
    {
        Transform Transform { get; }
        Rigidbody Rigidbody { get; }
        bool IsGrabbed { get; }
        Handedness? CurrentGrabbingHand { get; }

        void OnGrabbed(Handedness hand, Transform handAttachPoint, Vector3 grabLinearVelocity, Vector3 grabAngularVelocity);
        void OnReleased(Handedness hand, Vector3 releaseLinearVelocity, Vector3 releaseAngularVelocity);
        void OnHandTransfer(Handedness fromHand, Handedness toHand, Transform newAttachPoint);
    }

    public interface IMagazine : IGrabbable
    {
        WeaponType CompatibleWeaponType { get; }
        int CurrentAmmo { get; }
        int MaxAmmo { get; }
        bool IsEmpty { get; }
        void Refill();
    }

    public interface IWeaponInteraction : IGrabbable
    {
        WeaponType Type { get; }
        bool IsTwoHanded { get; }
        bool IsSecondaryGrabbed { get; }
        int CurrentAmmo { get; }
        int MagazineCapacity { get; }
        bool IsChambered { get; }
        bool IsReloading { get; }
        Vector3 RecoilOffset { get; }
        Quaternion RecoilRotation { get; }
        event Action OnFired;
        event Action<float, float> OnRecoilFired;
        void OnSecondaryGrabbed(Handedness hand, Transform secondaryAttachPoint);
        void OnSecondaryReleased(Handedness hand);
        bool TryInsertMagazine(IGrabbable magazine);
        void Reload();
        void EjectMagazine();
        void RackSlide();
    }

    public interface IHandInteractor
    {
        Handedness Hand { get; }
        Transform AttachTransform { get; }
        IGrabbable HeldObject { get; }
        bool IsHoldingObject { get; }
        void GrabObject(IGrabbable grabbable);
        void TryRelease();
        void TransferOut();
    }

    public struct BulletSpawnParams
    {
        public Vector3 Origin;
        public Vector3 Velocity;
        public float MassKg;
        public float CaliberMm;
        public float BaseDamage;
        public float MaxRange;
        public ulong ShooterClientId;
        public int WeaponId;
    }

    public struct BulletHitInfo
    {
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 IncomingDirection;
        public SurfaceType Surface;
        public HitZone Zone;
        public float DamageApplied;
        public float RemainingEnergy;
        public bool RicochetOccurred;
        public bool Penetrated;
        public Collider HitCollider;
        public ulong ShooterClientId;
    }

    public interface IBallisticsSimulation
    {
        int ActiveBulletCount { get; }
        void SpawnBullet(in BulletSpawnParams spawnParams);
        void StepSimulation(float deltaTime);
        void SetWind(Vector3 windVector);
        event Action<BulletHitInfo> OnBulletHit;
    }

    public interface ITargetHitZone
    {
        HitZone Zone { get; }
        float DamageMultiplier { get; }
        int BaseScore { get; }
    }

    public interface ITargetEntity
    {
        int TargetId { get; }
        bool IsActive { get; }
        Transform Transform { get; }
        float Health { get; }
        bool IsNetworkControlled { get; set; }
        void Initialize(int id, Vector3 spawnPosition, Quaternion spawnRotation);
        void Step(float deltaTime);
        void ApplyDamage(float amount, HitZone zone, Vector3 hitPoint, Vector3 hitNormal, ulong attackerClientId);
        void ApplySynchronizedState(float health, bool isDestroyed, Vector3 position);
        void ApplySynchronizedState(float health, bool isDestroyed, Vector3 position, Quaternion rotation);
        void TriggerNetworkHitReaction(float amount, HitZone zone, Vector3 hitPoint);
        void ResetTarget();
        event Action<ITargetEntity, float, HitZone, ulong> OnTargetHit;
        event Action<ITargetEntity, ulong> OnTargetDestroyed;
    }

    public interface ITargetSimulation
    {
        int ActiveTargetCount { get; }
        int ActiveTargetLimit { get; }
        IReadOnlyList<ITargetEntity> AllTargets { get; }
        ITargetEntity GetTargetById(int id);
        void RegisterTarget(ITargetEntity target);
        void UnregisterTarget(ITargetEntity target);
        void StepTargets(float deltaTime);
        void ResetAllTargets();
        void SetActiveTargetCount(int count);
        void IncreaseTargets(int step = 4);
        void DecreaseTargets(int step = 4);
    }

    public interface INetworkSession
    {
        bool IsActive { get; }
        bool IsHost { get; }
        bool IsClient { get; }
        ulong LocalClientId { get; }
        int ConnectedPlayerCount { get; }
        float EstimatedLatencyMs { get; }
        string StatusMessage { get; }

        void StartHost(ushort port = 7777);
        void StartClient(string ip = "127.0.0.1", ushort port = 7777);
        void Disconnect();

        Vector3 GetSpawnPosition(ulong clientId);
        Quaternion GetSpawnRotation(ulong clientId);
        string GetSpawnLaneName(ulong clientId);
        void RequestChangeActiveTargetCount(int newCount);

        void ReportHit(ulong shooterId, int targetId, HitZone zone, float damage, Vector3 hitPoint);
        void BroadcastShot(ulong shooterId, Vector3 muzzlePos, Vector3 velocity, WeaponType weaponType);
        void BroadcastMagazineEjected(ulong playerId, WeaponType weaponType, Vector3 dropPos, Quaternion dropRot, Vector3 velocity, Vector3 angularVelocity);
        void BroadcastReloadStarted(ulong playerId, WeaponType weaponType);
        void BroadcastMagazineInserted(ulong playerId, WeaponType weaponType);
        void BroadcastHandSwitched(ulong playerId, bool isLeftHand);

        event Action<ulong, WeaponType, Vector3, Quaternion, Vector3, Vector3> OnRemoteMagazineEjected;
        event Action<ulong, WeaponType> OnRemoteReloadStarted;
        event Action<ulong, WeaponType> OnRemoteMagazineInserted;
        event Action<ulong, bool> OnRemoteHandSwitched;
        void SyncScore(ulong clientId, int deltaScore);
        event Action<ulong, int, HitZone, float> OnNetworkHitValidated;
        event Action<ulong, int> OnScoreUpdated;
        event Action<ulong, Vector3, Vector3, WeaponType> OnRemoteShotFired;
        event Action OnConnectionStateChanged;
        event Action OnPlayerConnected;
        event Action OnPlayerDisconnected;
    }

    public struct ReplayStats
    {
        public int TotalFrames;
        public float DurationSeconds;
        public long TotalSizeBytes;
        public float AverageKbPerSecond;
        public float AccuracyScore;
    }

    public interface IReplayRecorder
    {
        bool IsRecording { get; }
        bool IsPlayingBack { get; }
        float CurrentDuration { get; }
        void StartRecording();
        void StopRecording();
        void StartPlayback();
        void StopPlayback();
        void Scrub(float normalizedTime);
        ReplayStats GetStats();
    }

    public interface IPokeable
    {
        void Press();
        void Release();
        void SimulateClick();
        string DisplayName { get; }
    }
}

