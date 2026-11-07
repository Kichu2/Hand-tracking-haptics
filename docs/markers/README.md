# Cube markers (DICT_4X4_50)

Standard OpenCV ArUco 4x4 markers. There are two sets, and each ID has a fixed size, so the app needs no settings:

| Sheet | IDs | Black square | What you make | Drives in the tutorial |
| --- | --- | --- | --- | --- |
| `blue_cube_net_print.pdf` | 0–5 | 4.8 cm | A 6.5 cm paper box from one sheet, with glue flaps | The blue prop; tracks straight away |
| `rubiks_cube_net_print.pdf` | 6–11 | 4.2 cm | A one-piece net with glue flaps, wrapped round a 6.0 cm 4x4 Rubik's cube | The orange cube; tracks straight away |
| `cube_markers_print.pdf` | 0–5 | 4.8 cm | Separate 6.4 cm squares for your own cube (faces 6.5 cm or more) | The blue prop |
| `small_cube_markers_print.pdf` | 6–11 | 4.2 cm | Separate 5.6 cm squares for your own cube (faces 5.7 cm or more) | The orange cube |

## Print

Print at **100% / Actual size** (A4 or US Letter). Check that the ruler measures exactly 10 cm, or that a black square measures exactly its size above.

## Cube nets

The app has both folded layouts built in (`CubeMarkerMap.CrossNetLayout`, with `QuestCameraCubeLinker.PropCubeEdge` = 6.5 cm and `GraspCubeEdge` = 6.0 cm), so each cube locks on as soon as any of its markers is visible.

- **Blue cube:** print on card (160–200 gsm) so the box holds its shape. Cut, crease, fold into a box, and glue each flap under the edge it meets.
- **Orange cube:** cut, crease, wrap round the Rubik's cube, and glue the flaps. Don't twist the cube afterwards.

To use a different size, change the edge constant and make a matching net.

## Separate markers

Stick one marker per face, any face, any way up, roughly centred and flat. Because the app starts from the net layout, turn the cube slowly in view for 20–30 seconds so it can relearn where your markers really are. It does this every session; it doesn't save the result.

Use good lighting and avoid glossy paper. Tracking holds the last pose when the cube leaves the camera view.

The PNGs here are the same markers without a fixed print size. In Unity, **Haptics Tutorial > Export ArUco Cube Markers** rewrites them. If you print them yourself, make the black squares exactly 4.8 cm (IDs 0–5) or 4.2 cm (IDs 6–11).
