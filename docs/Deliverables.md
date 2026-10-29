# Deliverables

Hand Tracking Haptics is a two-stage Meta Quest tutorial: a whole-hand grasp into a target zone, then registration of a ~12 cm passive haptic cube against a virtual stand-in (Kim et al. 2023), with short onboarding copy (Vidal et al. 2024).

## Hardware

| Item | Plan | Current |
| --- | --- | --- |
| Headset | Meta Quest 2 | Meta Quest 3 or Quest 3S |
| OS | Horizon OS (unspecified) | Horizon OS v74 or later |
| Tracking | Hand tracking (OpenXR / Meta Hands V2) | Unchanged |
| Physical prop | ~12 cm desk cube | Same cube, with ArUco 4x4 IDs 0–5 on the faces |

### Change from plan — headset

The original proposal targeted **Meta Quest 2**. Camera-based cube tracking uses Meta’s **Passthrough Camera API**, which is available only on **Quest 3 and Quest 3S** running **Horizon OS v74 or later**. Quest 2 has Insight Passthrough but not this camera API.

Headset switch is accepted. No extra hardware on the headset: the work is in the Unity app and is the same for Quest 3 and 3S. Build targeting is Quest 3 / 3S, minimum Horizon OS SDK 74.

## Software

- Unity 6000.3.7f1 URP scene `Assets/Scenes/HapticsTutorial.unity`
- Package `com.hapticslab.handtrackinghaptics`
- Meta XR SDK 205 (Core ≥ v74), OpenXR, XR Hands, XRI
- Stage 1: whole-hand grab of the orange cube onto the green pad
- Stage 2 cube tracking (headset build only — not Quest Link):
  1. Developer mode; Android build
  2. Runtime request of `horizonos.permission.HEADSET_CAMERA` and `android.permission.CAMERA`
  3. Left-eye frames, intrinsics, and head-relative pose via MRUK `PassthroughCameraAccess` (Unity-PassthroughCameraApiSamples path; older samples used `WebCamTexture`)
  4. **Markers (chosen).** Different ArUco DICT_4X4_50 marker on each face; detect, SolvePnP, cube pose. YOLO/Sentis + Depth API is the documented alternative if faces cannot be marked
  5. Camera space → world space using the camera pose, then exponential smoothing
  6. Hold last pose when the cube leaves the view; expect lag on fast motion
- Session log on device; Android APK (`UnityPlayerGameActivity`)

## Not in scope

Quest 2 camera capture, Quest Link camera preview, and unmarked-cube CAD reconstruction.
