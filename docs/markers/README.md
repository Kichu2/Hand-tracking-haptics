# Cube markers (DICT_4X4_50)

Print these six OpenCV ArUco 4x4 markers at **8 cm × 8 cm** and stick one on each face of the ~12 cm haptic cube. In Unity, **Haptics Tutorial > Export ArUco Cube Markers** writes the PNGs into this folder.

| ID | Face |
| --- | --- |
| 0 | Front (+Z) |
| 1 | Back (−Z) |
| 2 | Right (+X) |
| 3 | Left (−X) |
| 4 | Top (+Y) |
| 5 | Bottom (−Y) |

Use good lighting and keep markers large enough to read from your usual desk distance. Tracking holds the last pose when a face leaves the camera. Any standard ArUco DICT_4X4_50 generator produces the same IDs 0–5.
