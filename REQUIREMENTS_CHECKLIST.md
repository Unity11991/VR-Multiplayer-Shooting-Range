# Requirements Checklist — Networked VR Shooting Range

## 1. Project Target & Standards
- [x] Engine: Unity 6 LTS (6000.0.30f1).
- [x] Primary device: Meta Quest 3 support configured.
- [x] PCVR: Windows PC through Meta Quest Link support.
- [x] Target refresh rate: 90 FPS (11.11 ms frame budget).
- [x] Rendering: Universal Render Pipeline (URP).
- [x] XR Runtime: OpenXR.
- [x] Meta support: Meta XR OpenXR features / Core SDK compatible.
- [x] Multiplayer: Netcode for GameObjects (NGO).
- [x] Physics: Unity PhysX.
- [x] Performance: Burst, Jobs, Mathematics, Collections.
- [x] Testing: Unity Test Framework (EditMode & PlayMode).
- [x] Main platform: Windows PCVR.
- [x] Compatibility maintained for future Android Quest build.
- [x] RESTRICTION: No XR Interaction Toolkit for gameplay interactions.
- [x] RESTRICTION: No Meta Interaction SDK for gameplay interactions.
- [x] Custom interaction system built from scratch (`PhysicsGrabbable`, `VRHandInteractor`, `TwoHandedRifleGrip`).
- [x] Input abstraction: No gameplay logic depends on a specific input provider.

## 2. Two Runtime Modes
- [x] VR Mode: OpenXR head tracking, left/right controller tracking, buttons, triggers, haptics, Quest controller poses (`OpenXRTrackingProvider`).
- [x] Desktop VR Simulation Mode: WASD movement, mouse look, LMB fire, RMB aim, keyboard grab/release (G/T), reload (R), rack slide/clear jam (F), left/right virtual hand controls, adjustable simulated hand poses, adjustable head height ([ and ]), simulated controller velocity (`DesktopVRSimulator`).
- [x] DesktopVRSimulator implements exact same ITrackingProvider and IControllerInputProvider interfaces consumed by VR gameplay systems (no simplified fake gameplay).
- [x] Meta XR Simulator setup guide and OpenXR runtime compatibility (`META_XR_SIMULATOR_GUIDE.md`).

## 3. Architecture & Assembly Definitions
- [x] ShootingRange.Core
- [x] ShootingRange.Input
- [x] ShootingRange.Interaction
- [x] ShootingRange.Weapons
- [x] ShootingRange.Ballistics
- [x] ShootingRange.Targets
- [x] ShootingRange.Multiplayer
- [x] ShootingRange.Replay
- [x] ShootingRange.UI
- [x] ShootingRange.Editor
- [x] ShootingRange.Tests
- [x] One-way dependency direction maintained.
- [x] Zero global singletons / zero persistent static gameplay state.
- [x] Composition root / Dependency injection implemented (`ShootingRangeCompositionRoot`, `ServiceContainer`).
- [x] Interfaces: ITrackingProvider, IControllerInputProvider, IHandPoseProvider, IWeaponInteraction, IBallisticsSimulation, ITargetSimulation, INetworkSession, IReplayRecorder.
- [x] ScriptableObjects for all designer-configurable data.

## 4. Custom VR Interaction
- [x] Custom physics-based grabbing (velocity-driven follow / PID force in `FixedUpdate`).
- [x] Configurable grab points (`GrabPoint`).
- [x] Accurate release linear velocity and angular velocity (rolling 5-frame window).
- [x] Throwing and dropping physics.
- [x] Hand-to-hand transfer without duplication or loss (`OnHandTransfer`).
- [x] Left-handed mode support.
- [x] Seated mode support.
- [x] Adjustable player height & floor offset calibration.
- [x] Two-handed rifle: Primary hand grips, secondary hand steers barrel, natural off-hand rotation, shoulder stock settling, deadband jitter stabilization, one-hand/two-hand grip transitions (`TwoHandedRifleGrip`).
- [x] No weapon parenting to controllers during active grab.

## 5. Weapon System
- [x] ScriptableObject weapon framework (`WeaponDefinition`).
- [x] Semi-auto pistol implementation (`PistolController`, 9mm, slide lock).
- [x] Assault rifle implementation (`RifleController`, 5.56mm, burst/auto, optical mount).
- [x] Accurate fire-rate scheduling (frame-rate independent accumulator).
- [x] Fire modes: Semi-auto, Burst, Full-auto.
- [x] Physical magazine ejection & insertion (`Magazine`, `MagazineWell`).
- [x] Slide / charging handle operation (`SlideChargingHandle`).
- [x] Chambered round vs empty chamber mechanics (`ChamberState`).
- [x] Jam simulation & jam clearing mechanic.
- [x] Weapon recoil & sway (grip-dependent & weight-dependent).
- [x] Controller haptic feedback.
- [x] Designer-friendly workflow without code modifications (`WeaponTuningWindow`).

## 6. Ballistics
- [x] Projectile simulation (no hitscan for bullet flight).
- [x] Initial velocity, gravity, aerodynamic drag, bullet drop, wind force.
- [x] Continuous collision solution (anti-tunneling raycast sweep preventing passing through thin geometry).
- [x] Penetration & ricochet calculations.
- [x] Surface-dependent energy loss (Wood, Metal, Glass, Concrete).
- [x] Unity Jobs + Burst native container implementation (`BallisticsStepJob`).
- [x] Zero managed allocations in simulation hot path (`NativeArray<BallisticBulletData>`).
- [x] 1000+ concurrent bullet stress capability.

## 7. Target System
- [x] Four target types: Strafing, Pop-up, Zig-zag, Armoured with weak points.
- [x] Head / body / limb hit zones with damage multipliers (`TargetHitZone`).
- [x] Hit reactions & impact effects.
- [x] Centralized target simulation (no per-target Update methods in `TargetEntity`).
- [x] Object pooling (no Instantiate/Destroy during gameplay).
- [x] 300 active targets stress capability (`TargetManager.GenerateStressTargets`).
- [x] Score calculation and target reset.

## 8. Multiplayer
- [x] Netcode for GameObjects LAN architecture (Host / Client) (`NetworkShootingSession`).
- [x] Quest player host support.
- [x] Simultaneous two-player synchronization (`NetworkPlayerAvatar`).
- [x] Shared targets, scoring, round state.
- [x] Server-authoritative damage and scoring.
- [x] Client-side prediction for visual effects.
- [x] Lag compensation (~80 ms latency rewind buffer).
- [x] Disconnect & reconnect handling.

## 9. Replay
- [x] 30-second rolling gameplay recording buffer (`ReplayManager`).
- [x] Recorded state: Player poses, weapon poses, bullet trajectories, shots, hits, target movements.
- [x] Compact binary / quantized delta storage (`QuantizedPose` - 1mm precision, 49 bytes/frame, <1.55 KB/s).
- [x] Ghost playback in actual shooting range scene.
- [x] Metrics telemetry: Total recording size, average KB/s, recording duration, replay accuracy (`REPLAY_COMPRESSION_REPORT.md`).

## 10. Comfort & UI
- [x] World-space UI (not head-locked) (`WorldSpaceSettingsUI`).
- [x] Controller ray interaction & custom poke interaction (`VRRayPointer`, `VRPokeInteractable`).
- [x] Snap turning (45 deg) & smooth turning with adjustable speeds (`SnapSmoothTurnController`).
- [x] Comfort vignette during movement/turning (`ComfortVignette`).
- [x] Left-handed, seated, and floor calibration UI controls.

## 11. Save System
- [x] Versioned JSON save system (`GameSettingsData`, `SaveManager`).
- [x] Persists settings, high scores, preferences, comfort settings.
- [x] Schema migration pipeline for backward compatibility (V1 -> V2).
- [x] Migration automated tests (`SaveMigrationTests`).

## 12. Custom Editor Tools
- [x] Custom Weapon Tuning Window (`WeaponTuningWindow`).
- [x] Shooting range procedural layout generator (`ShootingRangeSceneBuilder`).

## 13. Testing
- [x] EditMode tests: Pooling, Ballistics math, Fire-mode FSM, Ammo, Save migration, Bullet collision.
- [x] PlayMode tests: Weapon grabbing, Release velocity, Hand transfer, Target damage.

## 14. Performance & Profiling
- [x] 90 FPS @ 11.11 ms frame budget target.
- [x] ProfilerMarker instrumentation on all major systems (`RangeProfilerMarkers`).
- [x] Zero GC allocations per frame in gameplay hot paths.
- [x] Worst-case stress test (300 targets, 500 bullets, 2 players).
- [x] Normal gameplay profiler capture.
- [x] Platform comparison documentation (Editor vs PCVR vs Android Quest in `PERFORMANCE_REPORT.md`).

## 15. Build Pipeline
- [x] Batch-mode Windows x64 PCVR build script with test execution and logging (`BuildProject.ps1`).
- [x] Android ARM64 Quest build configuration script (`BuildPipelineScript.ConfigureAndroidQuest`).

## 16. Visual Quality & Tactical Environment
- [x] Realistic indoor tactical range (lanes, concrete, metal, wood, target rails).
- [x] Weapon display area, reload station, scoreboard.
- [x] Optimized materials, lighting, bullet decals, muzzle flash, audio.
