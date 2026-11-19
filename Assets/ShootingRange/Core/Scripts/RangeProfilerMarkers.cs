using Unity.Profiling;

namespace ShootingRange.Core
{
    public static class RangeProfilerMarkers
    {
        public static readonly ProfilerMarker BallisticsSim = new ProfilerMarker("ShootingRange.Ballistics.Simulation");
        public static readonly ProfilerMarker BallisticsRaycast = new ProfilerMarker("ShootingRange.Ballistics.BatchedRaycasts");
        public static readonly ProfilerMarker BallisticsCollision = new ProfilerMarker("ShootingRange.Ballistics.ResolveCollision");
        
        public static readonly ProfilerMarker TargetsSim = new ProfilerMarker("ShootingRange.Targets.Simulation");
        public static readonly ProfilerMarker TargetsMovement = new ProfilerMarker("ShootingRange.Targets.Movement");
        public static readonly ProfilerMarker TargetsHitReaction = new ProfilerMarker("ShootingRange.Targets.HitReaction");

        public static readonly ProfilerMarker WeaponUpdate = new ProfilerMarker("ShootingRange.Weapons.Update");
        public static readonly ProfilerMarker WeaponFire = new ProfilerMarker("ShootingRange.Weapons.Fire");
        public static readonly ProfilerMarker WeaponRecoil = new ProfilerMarker("ShootingRange.Weapons.RecoilSway");

        public static readonly ProfilerMarker InteractionGrab = new ProfilerMarker("ShootingRange.Interaction.PhysicsFollow");
        public static readonly ProfilerMarker InteractionVelocity = new ProfilerMarker("ShootingRange.Interaction.CalculateVelocity");

        public static readonly ProfilerMarker NetworkSync = new ProfilerMarker("ShootingRange.Network.Sync");
        public static readonly ProfilerMarker NetworkLagComp = new ProfilerMarker("ShootingRange.Network.LagCompensation");

        public static readonly ProfilerMarker ReplayRecord = new ProfilerMarker("ShootingRange.Replay.RecordFrame");
        public static readonly ProfilerMarker ReplayPlayback = new ProfilerMarker("ShootingRange.Replay.Playback");
    }
}

