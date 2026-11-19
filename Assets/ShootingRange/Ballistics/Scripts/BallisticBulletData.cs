using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ShootingRange.Ballistics
{
    public struct BallisticBulletData
    {
        public bool IsActive;
        public float3 Position;
        public float3 Velocity;
        public float3 PredictedNextPosition;
        public float MassKg;
        public float CaliberMm;
        public float BaseDamage;
        public float CurrentEnergyJoules;
        public float DistanceTraveled;
        public float MaxRange;
        public float TimeAlive;
        public ulong ShooterClientId;
        public int WeaponId;
        public int BulletId;
        public int PenetrationCount;
    }

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
    public struct BallisticsStepJob : IJobParallelFor
    {
        [NativeDisableParallelForRestriction]
        public NativeArray<BallisticBulletData> Bullets;

        [ReadOnly] public float DeltaTime;
        [ReadOnly] public float3 Gravity;
        [ReadOnly] public float3 Wind;
        [ReadOnly] public float AirDensity; // ~1.225 kg/m^3 at sea level

        public void Execute(int index)
        {
            var bullet = Bullets[index];
            if (!bullet.IsActive) return;

            float speed = math.length(bullet.Velocity);
            if (speed < 1.0f || bullet.DistanceTraveled >= bullet.MaxRange || bullet.TimeAlive > 10.0f)
            {
                bullet.IsActive = false;
                Bullets[index] = bullet;
                return;
            }

            // Aerodynamic drag: Fd = 0.5 * rho * v^2 * Cd * Area
            // Cross-sectional area from caliber mm
            float radiusM = (bullet.CaliberMm * 0.001f) * 0.5f;
            float crossSectionalArea = math.PI * radiusM * radiusM;
            float dragCoefficient = 0.295f; // Standard supersonic / transonic rifle projectile Cd

            float3 relativeAirVelocity = bullet.Velocity - Wind;
            float relativeAirSpeed = math.length(relativeAirVelocity);

            float dragForceMagnitude = 0.5f * AirDensity * (relativeAirSpeed * relativeAirSpeed) * dragCoefficient * crossSectionalArea;
            float3 dragAcceleration = -math.normalize(relativeAirVelocity) * (dragForceMagnitude / math.max(bullet.MassKg, 0.001f));

            // Acceleration = Gravity + Drag
            float3 totalAcceleration = Gravity + dragAcceleration;

            // Velocity integration
            bullet.Velocity += totalAcceleration * DeltaTime;

            // Compute predicted next position for continuous raycast sweep
            bullet.PredictedNextPosition = bullet.Position + bullet.Velocity * DeltaTime;
            bullet.DistanceTraveled += math.length(bullet.PredictedNextPosition - bullet.Position);
            bullet.TimeAlive += DeltaTime;

            // Recalculate kinetic energy = 0.5 * m * v^2
            float newSpeed = math.length(bullet.Velocity);
            bullet.CurrentEnergyJoules = 0.5f * bullet.MassKg * (newSpeed * newSpeed);

            Bullets[index] = bullet;
        }
    }
}

