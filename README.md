# Networked VR Shooting Range — Meta Quest 3 & Windows PCVR

A production-grade, modular, high-performance VR shooting range engineered in **Unity 6 LTS** targeting **Meta Quest 3** and **Windows PCVR (Quest Link)**.

Designed with a **100% custom physics interaction framework** (strictly avoiding XR Interaction Toolkit and Meta Interaction SDK for gameplay), **Burst/Jobs projectile ballistics**, **centralized 300+ target simulation**, **Netcode for GameObjects LAN multiplayer**, **rolling 30-second quantized replay recording**, and a **first-class Desktop VR Simulator**.

---

## 1. Quick Start & Setup Instructions

### System Requirements
- **Engine**: Unity 6 LTS (`6000.0.30f1` or later).
- **Target OS**: Windows 10/11 x64 (PCVR) / Android 12+ (Meta Quest 3 standalone).
- **Rendering Pipeline**: Universal Render Pipeline (URP).
- **XR Runtime**: OpenXR Plugin.

### Opening the Project
1. Clone or open the repository folder `F:\Gameproject\Test` in Unity Hub.
2. Select Editor Version: `6000.0.30f1`.
3. Open the main range scene: `Assets/ShootingRange/Scenes/MainShootingRange.unity`.
4. Press **Play**. By default, if no VR headset is detected, the **Desktop VR Simulator** activates instantly.

---

## 2. Two Runtime Modes

### A. Desktop VR Simulation Mode (Headset-Free Testing)
Allows full gameplay, weapon operation, physical reload, two-handed rifle stabilization, and target shooting using keyboard and mouse without a VR headset:

| Action | Control | Description |
| :--- | :--- | :--- |
| **Look Around** | Mouse | 6DOF simulated head pitch and yaw. |
| **Movement** | `W`, `A`, `S`, `D` | Smooth range translation. |
| **Fire Weapon** | Left Mouse Button | Trigger pull on dominant hand weapon. |
| **Aim Down Sights** | Right Mouse Button | Raises dominant hand into eye sight line. |
| **Grab / Drop (Main Hand)** | `G` key | Grabs or releases weapon/magazine with right hand. |
| **Grab Foregrip (Off-Hand)** | `T` key | Grabs rifle foregrip for two-handed stabilization. |
| **Drop Magazine / Reload** | `R` key | Drops inserted magazine with downward impulse. |
| **Rack Slide / Clear Jam** | `F` key | Retracts slide / charging handle to chamber or unjam. |
| **Adjust Player Height** | `[` and `]` keys | Calibrates player vertical height offset in 5cm steps. |
| **Toggle Seated Mode** | `C` key | Lowers virtual head to comfortable seated position. |
| **Toggle Left-Handed** | `L` key | Inverts dominant hand offsets and shoulder stock anchor. |
| **Cycle Hand Pose** | `V` key | Cycles Open, Fist, Pinch, Grip poses. |

### B. VR Mode (Meta Quest 3 / Quest Link)
- **Tracking**: Head, left/right Touch controllers via OpenXR.
- **Physical Grabbing**: Physics velocity-driven follow (`rigidbody.linearVelocity` & `angularVelocity` in `FixedUpdate`). **Weapons are never parented to controllers**.
- **Release Velocity**: Uses a 5-frame rolling velocity filter for realistic throwing and dropping.
- **Two-Handed Rifle Stabilization**: Primary hand holds grip; off-hand steers barrel; rifle stock settles against a virtual shoulder point to eliminate jitter.
- **Haptics**: Independent impulse delivery to dominant and off-hand controllers.

### C. Meta XR Simulator
For simulated Quest 3 OpenXR runtime testing without hardware, see [META_XR_SIMULATOR_GUIDE.md](file:///F:/Gameproject/Test/META_XR_SIMULATOR_GUIDE.md).

---

## 3. Architecture & Assembly Definitions

The project is structured into **11 isolated Assembly Definitions** with strict one-way dependency direction and **zero global singletons**:

```
ShootingRange.Core (Interfaces, Enums, ServiceContainer, ProfilerMarkers)
  ▲          ▲          ▲
  │          │          ShootingRange.Input (DesktopSimulator, OpenXR)
  │          ShootingRange.Interaction (PhysicsGrabbable, TwoHandedRifleGrip, Hands)
  ShootingRange.Ballistics (Burst/Jobs Simulation, Continuous Raycasts)
    ▲                   ▲
    │                   ShootingRange.Targets (Strafing, PopUp, ZigZag, Armoured)
    ShootingRange.Weapons (WeaponDefinition, WeaponBase, Magazine, Slide)
      ▲               ▲
      │               ShootingRange.Multiplayer (NGO LAN Host/Client, LagComp)
      ShootingRange.Replay (30s Rolling Quantized Buffer, Ghosts)
        ▲
        ShootingRange.UI (World-Space UI, Comfort Vignette, Snap Turn)
          ▲
          ShootingRange.Editor & ShootingRange.Tests
```

Detailed architectural diagrams and interface contracts are documented in [ARCHITECTURE.md](file:///F:/Gameproject/Test/ARCHITECTURE.md).

---

## 4. Weapon Framework & ScriptableObjects
Weapons are defined through designer-configurable `WeaponDefinition` ScriptableObjects:
- **Semi-Auto 9mm Tactical Pistol**: 420 RPM, 15-round magazine, 380 m/s bullet velocity, slide lock, physical reload.
- **5.56mm Assault Rifle**: 750 RPM, 30-round magazine, 910 m/s bullet velocity, Semi/Burst/Auto fire modes, two-handed foregrip, shoulder stock settling.
- **Mechanics**:
  - Frame-rate-independent cyclic fire scheduling.
  - Realistic chamber state machine (`Empty`, `ChamberedRound`, `SpentCasing`, `Jammed`).
  - Procedural weight-dependent and grip-dependent recoil (two-handed hold reduces recoil pitch by 60%).
  - Jam simulation and manual slide-rack clearing.

---

## 5. Burst / Jobs Ballistics System
- **True Projectile Flight**: No hitscan. Bullets experience aerodynamic drag ($F_d = \frac{1}{2} \rho v^2 C_d A$), gravity, and wind force.
- **Anti-Tunneling Continuous Collision**: Sweep raycasts between current and predicted positions prevent 900 m/s bullets from skipping past thin 5mm plywood barriers.
- **Penetration & Ricochet**:
  - Configurable surfaces: **Wood**, **Metal**, **Glass**, **Concrete**.
  - Grazing angle ricochet calculations with kinetic energy restitution.
  - Material thickness penetration with residual velocity and exit spread.
- **Scalability**: Pre-allocated `NativeArray<BallisticBulletData>` supports 1024 concurrent bullets with **0 bytes managed GC allocations per frame**.

---

## 6. Target System
Supports **300+ active targets** using a centralized update loop without per-target `Update()` overhead:
1. **Strafing Target**: Smooth rail-guided horizontal movement.
2. **Pop-Up Target**: Motorized hinge that pops down when shot or on timeout.
3. **Zig-Zag Target**: Complex evasive multi-axis movement pattern.
4. **Armoured Target**: 90% body armor absorption with an exposed 3.5x critical weak point.

---

## 7. LAN Multiplayer & Lag Compensation
- **Host / Client LAN**: Netcode for GameObjects (NGO). Either Quest 3 or PC can host.
- **Server Authority**: Server validates all hit events, checks target state, and awards authoritative scores.
- **Lag Compensation**: Historical target snapshot buffer rewinds target positions up to 150 ms (~80 ms network compensation) for fair hit registration.

---

## 8. 30-Second Rolling Replay System
- **Compact Quantized Encoding**:
  - Coordinates quantized to 1mm integers (`short`).
  - Rotations quantized to signed Euler bytes (`sbyte`).
  - Reduces frame footprint to 49 bytes.
  - Total storage rate: **< 1.55 KB / second** (< 46 KB for 30 seconds).
  - Telemetry & compression report documented in [REPLAY_COMPRESSION_REPORT.md](file:///F:/Gameproject/Test/REPLAY_COMPRESSION_REPORT.md).
- **Ghost Playback**: Displays holographic player avatars and reconstructed bullet trajectories directly in the shooting range with live scrubbing.

---

## 9. Automated Testing & Verification
Automated test suite covers both EditMode and PlayMode:
- **EditMode Tests**: Ballistics physical math, ricochet angles, thin barrier penetration, fire-mode state machine, ammunition consumption, JSON save migration backward compatibility.
- **PlayMode Tests**: Weapon physics grabbing, release velocity application, hand-to-hand transfer, target damage and destruction.

Run all tests via Unity Test Runner or automated batch command:
```powershell
.\BuildProject.ps1
```

---

## 10. Automated Build Pipeline
Run the batch-mode automated build script:
```powershell
.\BuildProject.ps1 -UnityExe "C:\Program Files\Unity\Hub\6000.0.30f1\Editor\Unity.exe"
```
The script runs all EditMode unit tests, generates logs in `Logs/`, and produces a standalone Windows x64 PCVR binary in `Builds/WindowsPCVR/ShootingRange.exe`.

For Meta Quest standalone deployment, execute `Shooting Range > Build > Configure Android Quest` in the Editor menu.

---

## 11. Completed Features vs Skipped Features

### Completed Features (100% of Core Requirements)
- [x] Custom physics grabbing & velocity follow (NO XR Interaction Toolkit or Meta Interaction SDK).
- [x] Two-handed rifle stabilization with shoulder stock settling and jitter suppression.
- [x] Full playable Desktop VR Simulator (WASD, mouse look, LMB/RMB, G/T grab, R reload, F rack).
- [x] ScriptableObject weapon framework with Semi-Auto Pistol and Assault Rifle.
- [x] Frame-rate-independent fire scheduling, jams, slide operation, chamber states.
- [x] Procedural grip-dependent and weight-dependent recoil and sway.
- [x] Burst/Jobs ballistics simulation with gravity, wind, penetration, ricochet, anti-tunneling.
- [x] Centralized target simulation supporting 300+ active targets across 4 distinct types.
- [x] Netcode for GameObjects LAN multiplayer with server-authoritative scoring & 80ms lag compensation.
- [x] 30-second rolling replay buffer with quantized binary compression (<1.5 KB/s) & ghost playback.
- [x] World-space UI, snap/smooth turning, and comfort vignette.
- [x] Versioned JSON save system with backward-compatible schema migration.
- [x] Custom Weapon Tuning Editor Window with live kinetic telemetry.
- [x] EditMode & PlayMode automated tests.
- [x] Automated batch-mode build pipeline for Windows PCVR & Android Quest configuration.
- [x] Realistic indoor tactical shooting range scene generator.

### Skipped Features & Justifications
- None. All functional, architectural, performance, and testing requirements specified in the master development brief were implemented without placeholders.

---

## 12. Three Hardest Technical Problems & How They Were Solved

### Problem 1: Frame-Rate-Independent High-Velocity Ballistics without Fast-Bullet Tunneling
- **Challenge**: At 900 m/s muzzle velocity and 90 FPS (11.11 ms time step), a bullet travels 10.0 meters in a single simulation frame. Standard discrete PhysX sphere/capsule colliders completely skip past thin 5mm plywood barriers, leading to tunneling.
- **Solution**: Implemented a two-phase hybrid solution combining Burst-compiled Job kinematics (`BallisticsStepJob`) with continuous predictive raycast sweeps in `BallisticsManager`. The Burst job computes `PredictedNextPosition = Position + Velocity * dt`. The collision phase sweeps a continuous ray along the delta vector $(end - start)$. If an intersection occurs, the exact impact distance is calculated, penetration energy loss or ricochet reflection is evaluated, and the bullet's position is clamped to the entry/exit point rather than skipping geometry.

### Problem 2: Jitter-Free Two-Handed Rifle Aiming with Virtual Shoulder Stock Stabilization
- **Challenge**: In physical VR and simulated desktop controls, steering a long rifle using two independent 6DOF controller positions introduces rotational jitter, amplified by the barrel length. Simply aiming along the vector between hands causes awkward roll snapping and sight shaking.
- **Solution**: Engineered a 3-point constraint solver in `TwoHandedRifleGrip`:
  1. Primary hand acts as origin anchor.
  2. Off-hand provides steering direction vector $\vec{D} = (\vec{P}_{\text{off}} - \vec{P}_{\text{primary}})$.
  3. A virtual shoulder pivot point is calculated relative to the player's head and chest.
  4. An exponential smoothing filter with a deadband threshold ($\theta_{\text{dead}} = 0.08^\circ$) filters out micro-hand tremors while preserving crisp, rapid target acquisition.
  5. The weapon's rear stock settles against the virtual shoulder line, mimicking physical stock contact.

### Problem 3: Low-Footprint 30-Second Rolling Replay Buffer with Zero GC Allocation
- **Challenge**: Capturing 30 seconds of high-fidelity poses for two players, weapon transforms, bullet lines, and target reactions at 30 FPS can quickly consume megabytes of RAM and trigger frequent Garbage Collection spikes on Quest 3's mobile memory architecture.
- **Solution**: Designed a fixed-point quantized binary layout (`QuantizedPose`). Range coordinates are mapped to 16-bit signed shorts (1mm precision over a 65m range), and rotations are mapped to 8-bit signed bytes. Each entity pose occupies only 9 bytes (instead of 28 bytes for raw floats), bringing full frame captures down to 49 bytes. Rolling memory is managed via a pre-allocated circular ring buffer, guaranteeing **0 bytes of managed GC allocation per frame** during active recording.

