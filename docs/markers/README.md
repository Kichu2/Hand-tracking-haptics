# Cube markers (DICT_4X4_50)

Standard OpenCV ArUco 4x4 markers. There are two sets, and each ID has a fixed size, so the app needs no settings:

| Sheet | IDs | Black square | Cut size | Use it on | Drives in the tutorial |
| --- | --- | --- | --- | --- | --- |
| `rubiks_cube_net_print.pdf` | 6–11 | 4.2 cm | Fold-up net with glue flaps for a 6.0 cm cube | A 6.0 cm 4x4 Rubik's cube: cut, fold, glue | The orange cube in Stage 1; tracks straight away, no learning |
| `small_cube_markers_print.pdf` | 6–11 | 4.2 cm | 5.6 cm | The same markers as separate squares, for any cube of 5.7 cm or more | The orange cube in Stage 1 |
| `cube_markers_print.pdf` | 0–5 | 7 cm | 9.3 cm | A cube with faces of 9.5 cm or more | The blue prop in Stage 2 |

## Print

Print the sheet at **100% / Actual size** (A4 or US Letter). Check that its ruler measures exactly 10 cm. Cut along the dotted lines and keep the white border.

## Cube net (orange cube)

The app has the folded net's layout and the 6.0 cm size built in (`QuestCameraCubeLinker.GraspCubeEdge` and `CubeMarkerMap.CrossNetLayout`), so the orange cube locks on as soon as any marker is visible. For a cube of a different size, change `GraspCubeEdge` and make a matching net.

## Separate markers: stick and go

1. Stick one marker on each face of the cube. Any face, any way up, roughly centred and flat.
2. Run the app and turn the cube slowly in view for 20–30 seconds, so every face is seen together with a neighbouring face. Hold small cubes about 30–40 cm from your eyes.
3. The app learns which marker is where, plus the cube's centre and edge length, and remembers them between runs. If you move a marker later, it relearns that marker on its own.

The virtual cube follows the real one and resizes to match it. Nothing needs to be set in Unity. Progress shows in `adb logcat -s Unity` (`grasp cube: N/6 learned, X cm`).

Use good lighting and avoid glossy paper. Tracking holds the last pose when the cube leaves the camera view.

The PNGs here are the same markers without a fixed print size. In Unity, **Haptics Tutorial > Export ArUco Cube Markers** rewrites them. If you print them yourself, make the black squares exactly 7 cm (IDs 0–5) or 4.2 cm (IDs 6–11).
