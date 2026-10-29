# Development diary

## 2026-09-11 — Proposal baseline

Planned a two-stage Quest hand-tracking haptics tutorial (grasp into a zone; Kim-style passive cube). Hardware in the proposal: **Meta Quest 2**. Editor playtesting via XR Device Simulator; headset deploy as a later Android build.

## 2026-09 — Scene and Meta hands

Unity 6 URP project, XR Origin hands for editor, then Meta Camera Rig + OVRHand Building Blocks for Quest. Instruction canvas flipped for headset readability. Play Mode arms restored with the interaction simulator. Meta SDK editor prompts around Android SDK were patched so Play Mode does not require an Android module.

## 2026-10 — Grab, physics, Quest 3S deploy

XRI grab did not drive Meta tracked hands. Switched tutorial cubes to ISDK HandGrab / TouchHandGrab (pinch + palm) and HandPhysicsCapsules so the sides of the hands can push objects. Built for Android; Quest showed up over ADB as Quest 3S (`panther`). Gradle `ProcessLibraryManifest` and a disabled `UnityPlayerGameActivity` blocked launch until the custom manifest and post-generate patcher forced an enabled, exported GameActivity.

## 2026-10-27 — Change from plan: Quest 2 → Quest 3 / 3S for camera methods

Stage 2 needed a real desk cube linked into the tutorial. Insight Passthrough on Quest 2 is not enough: the **Passthrough Camera API** requires **Quest 3 or Quest 3S** and **Horizon OS v74+**.

Recorded as a **change from plan**. Switching headsets is accepted. Nothing on the headset hardware changes; the pipeline lives in the Unity app and is the same for 3 and 3S.

Implemented that pipeline:

1. Project targets Quest 3 / 3S, min Horizon OS SDK 74, Insight Passthrough + Passthrough Camera Access. Developer mode stays as already used for ADB.
2. Manifest and runtime request for `horizonos.permission.HEADSET_CAMERA` and `android.permission.CAMERA`.
3. `PassthroughCameraAccess` (left camera) for frames, intrinsics, and pose relative to the head — the current Unity-PassthroughCameraApiSamples API. Older samples used `WebCamTexture`; Meta’s migration guide replaces that. The feed is a **headset build**, not Quest Link.
4. Detection is **markers**, the recommended option: ArUco DICT_4X4_50 IDs 0–5, one per cube face. Print via **Haptics Tutorial > Export ArUco Cube Markers**. OpenCV for Unity is not required. YOLO/Sentis remains the fallback if faces cannot be marked (2D box only unless combined with Depth API).
5. Marker pose is converted with the camera pose from step 3, then smoothed with an exponential filter.
6. Last pose is kept when the cube leaves the view. Camera rate and latency will lag fast motion; markers need good lighting and ~8 cm print size.

Editor Play Mode still uses a look-ray fallback because PCA does not run in the editor.
