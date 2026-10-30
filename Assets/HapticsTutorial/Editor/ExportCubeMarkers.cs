using System.IO;
using HapticsTutorial;
using UnityEditor;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    static class ExportCubeMarkers
    {
        const string Folder = "docs/markers";

        // docs/markers/cube_markers_print.pdf is the exact-scale print sheet; these PNGs are the
        // same markers without a physical size. See docs/markers/README.md.
        [MenuItem("Haptics Tutorial/Export ArUco Cube Markers")]
        public static void Export()
        {
            Directory.CreateDirectory(Folder);
            for (var id = 0; id < ArucoDict4x4.MarkerCount; id++)
            {
                var tex = ArucoDict4x4.MakeTexture(id);
                var png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);
                File.WriteAllBytes(Path.Combine(Folder, $"aruco_4x4_{id}.png"), png);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Haptics Tutorial] Wrote ArUco 4x4 IDs 0-{ArucoDict4x4.MarkerCount - 1} to {Path.GetFullPath(Folder)}. " +
                      "Easiest is the print PDFs there at 100%; if printing these PNGs, make the black squares " +
                      $"{QuestCameraCubeLinker.MarkerSize * 100f:F1} cm (IDs 0-5) or {QuestCameraCubeLinker.SmallMarkerSize * 100f:F1} cm (IDs 6-11).");
            EditorUtility.RevealInFinder(Path.GetFullPath(Folder));
        }
    }
}
