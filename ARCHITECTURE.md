# Architecture Specification — Networked VR Shooting Range

## 1. High-Level Architectural Diagram

```
+----------------------------------------------------------------------------------------------------+
|                                    ShootingRange.Tests                                             |
|                             (EditMode & PlayMode Integration Tests)                                |
+----------------------------------------------------------------------------------------------------+
                                                  |
+----------------------------------------------------------------------------------------------------+
|                                    ShootingRange.Editor                                            |
|                  (Weapon Tuning Window, Layout Generator, Build Pipelines)                         |
+----------------------------------------------------------------------------------------------------+
               |                         |                         |                    |
+-----------------------------+ +-----------------------------+ +----------------------------+
|      ShootingRange.UI       | |   ShootingRange.Multiplayer | |    ShootingRange.Replay    |
| (World-Space, Ray/Poke,     | | (NGO Host/Client LAN,       | | (30s Rolling Buffer,       |
|  Comfort Vignette, Turning) | |  Server-Authority, LagComp) | |  Quantized Delta, Ghosts)  |
+-----------------------------+ +-----------------------------+ +----------------------------+
       |             |                         |                         |
       |             v                         v                         v
       |   +------------------------------------------------------------------+
       |   |                     ShootingRange.Weapons                        |
       |   |  (Pistol, Rifle, Magazine, Slide/Charging, Firing FSM, Recoil)   |
       |   +------------------------------------------------------------------+
       |                 |                               |
       v                 v                               v
+-----------------------------+           +-----------------------------+
|  ShootingRange.Interaction  |           |   ShootingRange.Ballistics  |
| (PhysicsGrabbable, PID,     |           | (Burst/Jobs Projectiles,    |
|  TwoHandedRifleGrip, Hands) |           |  Continuous Raycasts, Pool) |
+-----------------------------+           +-----------------------------+
               |                                         |
               v                                         v
+-----------------------------+           +-----------------------------+
|     ShootingRange.Input     |           |    ShootingRange.Targets    |
| (DesktopVRSimulator, OpenXR,|           | (Strafing, Pop-up, Zig-zag, |
|  Meta XR Simulator compat)  |           |  Armoured, Centralized Loop)|
+-----------------------------+           +-----------------------------+
               \                                         /
                \                                       /
                 v                                     v
+----------------------------------------------------------------------------------------------------+
|                                     ShootingRange.Core                                             |
|        (Interfaces: ITrackingProvider, IControllerInputProvider, IWeaponInteraction,               |
|         IBallisticsSimulation, ITargetSimulation, INetworkSession, IReplayRecorder;                |
|         Enums, ServiceContainer / Composition Root, RangeProfilerMarkers)                         |
+----------------------------------------------------------------------------------------------------+
```

---

## 2. Dependency Direction Rules
1. **Strict One-Way Hierarchy**: Dependencies flow downward into `ShootingRange.Core`.
2. **Zero Global Singletons**: No persistent static gameplay variables (`static Weapon`, `static Target`, etc.). All dependencies are injected via `ShootingRangeCompositionRoot` and resolved cleanly from `ServiceContainer`.
3. **Decoupled Input Abstraction**: Gameplay systems (`Weapons`, `Interaction`, `UI`) interact exclusively through interfaces (`ITrackingProvider`, `IControllerInputProvider`, `IHandPoseProvider`). They do not know or care whether input originates from an OpenXR Quest 3 headset or the `DesktopVRSimulator`.

---

## 3. Subsystem Breakdown

### Core Subsystem (`ShootingRange.Core`)
- **Interfaces**:
  - `ITrackingProvider`: Head & hand 6DOF poses, velocities, angular velocities.
  - `IControllerInputProvider`: Triggers, grips, buttons, thumbsticks, haptic triggers.
  - `IHandPoseProvider`: Poses (Open, Fist, Pinch, Grip), finger curl values.
  - `IGrabbable`: Physics grab contract (linear/angular velocity, attach points, hand transfer).
  - `IWeaponInteraction`: Two-handed grip coordination, foregrip steering.
  - `IBallisticsSimulation`: Ballistic projectile spawning, stepping, hit callbacks.
  - `ITargetSimulation`: Target registration, centralized updating, scoring.
  - `INetworkSession`: Host/client state, lag-compensated hit validation, round synchronization.
  - `IReplayRecorder`: Rolling buffer recording, compact telemetry, scrubbing, ghost playback.
- **ServiceContainer**: Lightweight, fast type-based dependency injection registry.
- **RangeProfilerMarkers**: Zero-allocation `Unity.Profiling.ProfilerMarker` instances instrumented across all hot paths.

### Input Subsystem (`ShootingRange.Input`)
- **DesktopVRSimulator**: Full 6DOF desktop simulation:
  - Mouse pitch & yaw look.
  - WASD translation.
  - LMB trigger firing, RMB weapon sight aiming.
  - G / T key grabbing and releasing (dominant & off-hand).
  - R key magazine drop / reload, F key charging handle rack / jam clear.
  - Simulated controller velocity filter for throwing.
- **OpenXRTrackingProvider**: Queries raw `UnityEngine.XR.InputDevice` nodes without relying on toolkit abstractions.
- **InputModeManager**: Seamlessly toggles or auto-detects VR vs Desktop simulation.

### Interaction Subsystem (`ShootingRange.Interaction`)
- **PhysicsGrabbable**:
  - **Never parents weapons to controllers**.
  - Follows hands using velocity/PID drive (`rigidbody.linearVelocity` and `rigidbody.angularVelocity`) in `FixedUpdate`.
  - Preserves momentum and applies filtered release velocity and angular velocity on throw/drop.
  - Seamless hand-to-hand transfer without duplicate instantiation or loss.
- **TwoHandedRifleGrip**:
  - Primary hand controls grip and trigger.
  - Secondary hand steers barrel toward off-hand.
  - Natural roll/cant alignment.
  - Shoulder stock settling against a virtual shoulder point for jitter-free sight acquisition.

### Ballistics Subsystem (`ShootingRange.Ballistics`)
- **Burst / Jobs Projectile Flight**:
  - Parallel kinematic integration of gravity, aerodynamic drag ($F_d = \frac{1}{2} \rho v^2 C_d A$), and wind force.
- **Anti-Tunneling Continuous Sweeps**:
  - Continuous raycast sweeps between current and predicted positions prevent fast bullets from skipping past thin 5mm barriers.
- **Penetration & Ricochet**:
  - Surface-dependent energy loss: Wood, Metal, Glass, Concrete.
  - Grazing angle ricochet reflection with kinetic energy restitution.
  - Material thickness penetration with residual velocity.
- **Native Container Allocation**:
  - Pre-allocated `NativeArray<BallisticBulletData>` for 1024 concurrent bullets. Zero managed GC allocations per frame.

### Targets Subsystem (`ShootingRange.Targets`)
- **Centralized Simulation**:
  - Up to 300+ targets updated via `TargetManager.StepTargets()` in a single loop.
  - Zero per-target `Update()` calls.
- **Four Distinct Behaviors**:
  1. *Strafing*: Rail-guided sinusoidal translation.
  2. *Pop-Up*: Hinged flap that pops down on hit or timer.
  3. *Zig-Zag*: Multi-axis evasion sweep.
  4. *Armoured*: 90% body armor reduction with a 3.5x critical weak point.

### Multiplayer Subsystem (`ShootingRange.Multiplayer`)
- **Host / Client Architecture**:
  - Built with Netcode for GameObjects.
  - Quest 3 or PC can act as host.
- **Server Authority**:
  - Server confirms valid hits and computes authoritative scores.
  - Clients predict visual tracers and effects immediately.
- **Lag Compensation**:
  - Rewinds target snapshots based on client latency (~80ms buffer) to ensure fair hit registration under network variance.

### Replay Subsystem (`ShootingRange.Replay`)
- **Rolling 30-Second Ring Buffer**:
  - Quantized 1mm coordinates (`short`) and Euler/smallest-three rotations (`sbyte`).
  - Achieves < 1.5 KB/sec storage rate (under 45 KB total for 30s of multi-entity motion).
  - Ghost avatar and tracer playback.

### Comfort & UI Subsystem (`ShootingRange.UI`)
- **World-Space UI**:
  - Physical kiosks and scoreboards in 3D world space (zero head-locked menus).
  - Controller ray pointer and physical poke interaction.
- **Comfort**:
  - Snap turning (45 deg) and smooth turning with configurable speeds.
  - Dynamic peripheral comfort vignette during rotational movement.

