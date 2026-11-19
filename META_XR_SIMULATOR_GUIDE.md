# Meta XR Simulator Setup & Usage Guide

## Overview
The Meta XR Simulator allows testing Quest 3 OpenXR features (head tracking, 6DOF controller tracking, synthetic input, and boundary) directly on PC without an active physical headset.

This guide details how to configure Meta XR Simulator in Unity 6 LTS while keeping the custom `DesktopVRSimulator` as an instant fallback.

---

## 1. Prerequisites & Package Installation
1. Ensure **OpenXR Plugin** (`com.unity.xr.openxr`) is installed and active in **Project Settings > XR Plug-in Management**.
2. Download and install the **Meta XR Core SDK** (`com.meta.xr.sdk.core`) package via Unity Package Manager or Meta Quest Developer Hub (MQDH).
3. The Meta XR Simulator executable is bundled with the Meta XR Core SDK at:
   `Library/PackageCache/com.meta.xr.sdk.core@.../Simulator/` or installed via MQDH.

---

## 2. Enabling Meta XR Simulator in Unity
1. Navigate to **Project Settings > XR Plug-in Management > OpenXR**.
2. Under the **Windows / Standalone** tab:
   - Check **Meta XR Feature**.
   - Under OpenXR Interaction Profiles, add:
     - *Oculus Touch Controller Profile*
     - *Meta Quest Touch Pro Controller Profile*
     - *Meta Quest Touch Plus Controller Profile*
3. In Unity Editor Menu, select:
   **Oculus > Meta XR Simulator > Activate** (or toggle *Activate Simulator* in XR settings).

---

## 3. Keyboard & Mouse Shortcuts in Meta XR Simulator
When running Meta XR Simulator:
- **Left / Right Click + Drag**: Rotate headset view.
- **Alt + Mouse Move**: Move Headset in 3D space.
- **Shift + Mouse Move**: Move Controller in 3D space.
- **Space Bar**: Toggle between Left and Right controller control.
- **Left Click**: Controller Trigger.
- **Right Click**: Controller Grip.
- **G / B**: Primary (A/X) and Secondary (B/Y) buttons.

---

## 4. Fallback: Custom DesktopVRSimulator
If Meta XR Simulator is not running or OpenXR runtime is inactive:
- `InputModeManager` automatically switches to `DesktopVRSimulator`.
- Controls:
  - **Mouse Look**: Rotate Head.
  - **WASD**: Walk / Navigate range.
  - **Left Click**: Fire / Trigger.
  - **Right Click**: Aim weapon (align sights with eye).
  - **G**: Grab / Drop weapon (Right Hand).
  - **T**: Grab / Drop rifle foregrip (Left Hand two-handed stabilization).
  - **R**: Magazine release / reload.
  - **F**: Slide / charging handle pull / jam clear.
  - **[ and ]**: Adjust player height offset.
  - **C**: Toggle Seated mode.
  - **L**: Toggle Left-handed mode.

