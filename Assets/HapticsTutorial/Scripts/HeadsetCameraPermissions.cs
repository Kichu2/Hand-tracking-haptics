using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HapticsTutorial
{
    /// <summary>
    /// Requests Passthrough Camera API permissions. The feed only works in a headset
    /// build (Quest 3 / 3S, Horizon OS v74+), not in the editor over Quest Link.
    /// </summary>
    public static class HeadsetCameraPermissions
    {
        public const string HeadsetCamera = "horizonos.permission.HEADSET_CAMERA";
        public const string AndroidCamera = "android.permission.CAMERA";

        public static bool HasCameraAccess
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return Permission.HasUserAuthorizedPermission(HeadsetCamera) &&
                       Permission.HasUserAuthorizedPermission(AndroidCamera);
#else
                return false;
#endif
            }
        }

        public static void Request()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Permission.RequestUserPermissions(new[] { HeadsetCamera, AndroidCamera });
#else
            Debug.Log("[Haptics Tutorial] Passthrough Camera API is a headset-only path. Build to a Quest 3 or 3S (not Quest Link).");
#endif
        }
    }
}
