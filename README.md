# Hand Tracking Haptics

A short Meta Quest tutorial (about 3–4 minutes) for using your bare hands in VR. The headset camera tracks real paper cubes, so the virtual cubes you grab line up with something you can actually touch. A **no-props** version, with virtual cubes only, is included for comparison.

## What you need

- **Meta Quest 3 or 3S** on Horizon OS v74 or later, with [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/) on and hand tracking enabled (**Settings > Movement tracking**).
- A **USB-C cable** to connect the headset to your PC.
- **Unity 6000.3.7f1** with **Android Build Support** (including OpenJDK and Android SDK & NDK), installed through Unity Hub.
- **A printer and card** (160–200 gsm), for the with-props version.

## 1. Make the cubes (with-props version only)

Print these from [`docs/markers`](docs/markers) at **100% / Actual size**, and check that the ruler on the page measures 10 cm:

| Sheet | Makes | Used for |
| --- | --- | --- |
| [`rubiks_cube_net_print.pdf`](docs/markers/rubiks_cube_net_print.pdf) | A cover for a 6.0 cm 4x4 Rubik's cube | The orange cube (Steps 1–2) |
| [`blue_cube_net_print.pdf`](docs/markers/blue_cube_net_print.pdf) | A 6.5 cm paper box | The blue cube (Step 3) |

Cut along the solid lines, crease the dashed lines, fold, and glue the grey flaps. More detail is in [`docs/markers/README.md`](docs/markers/README.md).

## 2. Build and install

1. Clone this repo and open the folder in Unity Hub with Unity 6000.3.7f1. The first import takes a while.
2. Open **`Assets/Scenes/HapticsTutorial.unity`**.
3. Go to **File > Build Profiles**, select **Android**, and click **Switch Platform**.
4. Plug in the headset and accept **Allow USB debugging** inside it.
5. From the **Haptics Tutorial** menu, choose either:
   - **Build and Run: With Props**: the app **Hand Tracking Haptics**.
   - **Build and Run: Without Props**: the app **Hand Tracking Haptics (No Props)**.

   The two install side by side. The APKs are also saved to `Builds/`.

> It must run as a build on the headset. The camera tracking doesn't work over Quest Link.

## 3. Run the tutorial

1. On the headset, open **Library > Unknown Sources** and launch the app.
2. The first time, tap **Allow** when it asks for headset camera access (with-props version).
3. Put the cubes on a well-lit table in front of you and follow the instruction panel:
   - **Welcome:** timed slides, nothing to do.
   - **Step 1, Move the cube:** place the orange cube on the green target, 3 times.
   - **Step 2, Turn the cube:** turn it so the yellow face points at you, 5 times.
   - **Step 3, Align the prop:** hold or touch the blue cube until it turns solid.
   - **Wrap-up:** your time and the number of drops.

**Tips:** hold the cubes by their edges so more markers stay visible, and keep them about 30–60 cm from your eyes.

## Results

Each session writes a CSV log on the headset, with the condition (`props` or `no_props`), stage times and drops. Copy the logs to your PC with:

```bash
adb pull /sdcard/Android/data/com.hapticslab.handtrackinghaptics/files/ logs_props
```

For the no-props app, the package is `com.hapticslab.handtrackinghaptics.noprops`.

## Troubleshooting

| Problem | Fix |
| --- | --- |
| The cube doesn't lock on, or jumps around | Rebuild and reinstall; check the print scale (ruler = 10 cm); use brighter light. |
| Nothing is tracked | Check **Settings > Privacy & Safety > App permissions > Headset cameras** is on for the app. |
| The headset doesn't appear in Unity | Re-plug USB, accept USB debugging, and check developer mode. |
| You want to see what the tracker sees | `adb logcat -s Unity` shows a `Cube tracker:` status line every few seconds. |

## Project layout

- `Assets/HapticsTutorial/Scripts/`: tutorial stages, marker detection, cube tracking.
- `Assets/HapticsTutorial/Editor/`: setup and build menus.
- `docs/`: printable markers, the development diary, deliverables.
