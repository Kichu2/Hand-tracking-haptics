using System.IO;
using HapticsTutorial;
using UnityEditor;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    static class ExportCubeMarkers
    {
        const string Folder = "docs/markers";

        [MenuItem("Haptics Tutorial/Export ArUco Cube Markers")]
        public static void Export()
        {
            Directory.CreateDirectory(Folder);
            var names = new[] { "front_id0_+Z", "back_id1_-Z", "right_id2_+X", "left_id3_-X", "top_id4_+Y", "bottom_id5_-Y" };
            for (var id = 0; id < ArucoDict4x4.MarkerCount; id++)
            {
                var tex = ArucoDict4x4.MakeTexture(id);
                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                File.WriteAllBytes(Path.Combine(Folder, $"aruco_4x4_{id}_{names[id]}.png"), png);
            }

            File.WriteAllText(Path.Combine(Folder, "README.md"), Readme);
            AssetDatabase.Refresh();
            Debug.Log($"[Haptics Tutorial] Wrote ArUco 4x4 IDs 0-5 to {Path.GetFullPath(Folder)}. Print each at 8 cm and stick one on each face of the 12 cm cube.");
            EditorUtility.RevealInFinder(Path.GetFullPath(Folder));
        }

        const string Readme =
            "# Cube markers (DICT_4X4_50)\n\n" +
            "Print these six OpenCV ArUco 4x4 markers at **8 cm × 8 cm** and stick one on each face of the ~12 cm haptic cube.\n\n" +
            "| File | Face |\n| --- | --- |\n" +
            "| `aruco_4x4_0_front_id0_+Z.png` | Front |\n" +
            "| `aruco_4x4_1_back_id1_-Z.png` | Back |\n" +
            "| `aruco_4x4_2_right_id2_+X.png` | Right |\n" +
            "| `aruco_4x4_3_left_id3_-X.png` | Left |\n" +
            "| `aruco_4x4_4_top_id4_+Y.png` | Top |\n" +
            "| `aruco_4x4_5_bottom_id5_-Y.png` | Bottom |\n\n" +
            "Use good lighting. Keep the last printed pose if a face goes out of view. " +
            "Standard ArUco DICT_4X4_50 generators produce the same IDs 0–5.\n";
    }
}
