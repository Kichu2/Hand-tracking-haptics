using Meta.XR;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Tracks a marked desk cube on Quest 3 / 3S using Passthrough Camera Access
    /// (Horizon OS v74+). Converts marker pose from camera space to world space
    /// and smooths it. Keeps the last pose when the cube leaves the view.
    /// Camera frames are headset-only; Play Mode uses a look-ray fallback.
    /// </summary>
    public class QuestCameraCubeLinker : MonoBehaviour
    {
        [SerializeField] Transform virtualProp;
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] float markerSize = 0.08f;
        [SerializeField] float cubeSize = 0.12f;
        [SerializeField] float followLerp = 6f;
        [SerializeField] int processEveryNFrames = 6;

        Pose _pose;
        bool _hasPoint;
        bool _follow = true;
        float _lastSeenTime = -999f;
        bool _loggedLinkLimit;

        public bool HasPoint => _hasPoint;
        public Vector3 WorldPoint => _pose.position;
        public Pose WorldPose => _pose;
        public bool IsTracking => _hasPoint && Time.time - _lastSeenTime < 0.6f;

        public void SetVirtualProp(Transform prop) => virtualProp = prop;

        public void SetFollowEnabled(bool enabled) => _follow = enabled;

        void Awake()
        {
            EnsurePassthrough();
            HideEditorRoomOnDevice();
            HeadsetCameraPermissions.Request();
        }

        void Start()
        {
            if (virtualProp == null)
            {
                var prop = GameObject.Find("Passive Haptic Prop");
                if (prop != null)
                    virtualProp = prop.transform;
            }
        }

        void LateUpdate()
        {
            if (!_follow)
                return;

            if (cameraAccess != null && cameraAccess.IsPlaying &&
                Time.frameCount % Mathf.Max(1, processEveryNFrames) == 0)
            {
                TryDetectFromCamera();
            }
            else if (!_hasPoint)
            {
                FallbackLookRay();
            }

            if (!_hasPoint || virtualProp == null)
                return;

            var t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            virtualProp.position = Vector3.Lerp(virtualProp.position, _pose.position, t);
            virtualProp.rotation = Quaternion.Slerp(virtualProp.rotation, _pose.rotation, t);
        }

        public static bool HeadsetSupportsPassthroughCamera()
        {
            var headset = OVRPlugin.GetSystemHeadsetType();
            return headset == OVRPlugin.SystemHeadset.Meta_Quest_3 ||
                   headset == OVRPlugin.SystemHeadset.Meta_Quest_3S ||
                   headset == OVRPlugin.SystemHeadset.Meta_Link_Quest_3 ||
                   headset == OVRPlugin.SystemHeadset.Meta_Link_Quest_3S;
        }

        void EnsurePassthrough()
        {
            var manager = OVRManager.instance != null ? OVRManager.instance : FindFirstObjectByType<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                manager.requestPassthroughCameraAccessPermissionOnStartup = true;
            }

            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.GetComponent<OVRPassthroughLayer>() == null)
                rig.gameObject.AddComponent<OVRPassthroughLayer>();

            if (cameraAccess == null)
                cameraAccess = GetComponent<PassthroughCameraAccess>();
            if (cameraAccess == null)
                cameraAccess = gameObject.AddComponent<PassthroughCameraAccess>();

            cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            cameraAccess.RequestedResolution = new Vector2Int(640, 480);
        }

        static void HideEditorRoomOnDevice()
        {
#if !UNITY_EDITOR
            HideNamed("Desk");
            HideNamed("Floor");
#endif
        }

        static void HideNamed(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go != null)
                go.SetActive(false);
        }

        void TryDetectFromCamera()
        {
            if (!HeadsetSupportsPassthroughCamera())
            {
                LogLinkLimitOnce();
                FallbackLookRay();
                return;
            }

            var colors = cameraAccess.GetColors();
            if (!colors.IsCreated || colors.Length == 0)
            {
                LogLinkLimitOnce();
                return;
            }

            var resolution = cameraAccess.CurrentResolution;
            var detections = ArucoMarkerDetector.Detect(colors, resolution.x, resolution.y);
            if (detections.Count == 0)
                return;

            Vector3 accPos = Vector3.zero;
            var accRot = Quaternion.identity;
            var weight = 0f;
            var first = true;
            foreach (var detection in detections)
            {
                if (!MarkerPoseSolver.TryCubePose(detection, cameraAccess, markerSize, cubeSize, out var pose))
                    continue;
                accPos += pose.position * detection.Area;
                accRot = first ? pose.rotation : Quaternion.Slerp(accRot, pose.rotation, detection.Area / (weight + detection.Area));
                weight += detection.Area;
                first = false;
            }

            if (weight <= 0f)
                return;

            var wasLost = Time.time - _lastSeenTime > 0.6f;
            _pose = new Pose(accPos / weight, accRot);
            _hasPoint = true;
            _lastSeenTime = Time.time;
            if (wasLost)
                SessionLogger.Instance?.Log("cube_tracked", "prop_register", _pose.position.ToString("F3"));
        }

        void FallbackLookRay()
        {
            Transform eye = null;
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
                eye = rig.centerEyeAnchor;
            else if (Camera.main != null)
                eye = Camera.main.transform;

            if (eye == null)
                return;

            var ray = new Ray(eye.position, eye.forward);
            if (Mathf.Abs(ray.direction.y) < 0.02f)
                return;

            const float deskHeight = 0.72f;
            var t = (deskHeight - ray.origin.y) / ray.direction.y;
            if (t < 0.2f || t > 2.4f)
                return;

            var point = ray.origin + ray.direction * t;
            point.y = deskHeight + cubeSize * 0.5f;
            _pose = new Pose(point, Quaternion.identity);
            _hasPoint = true;
        }

        void LogLinkLimitOnce()
        {
            if (_loggedLinkLimit)
                return;
            _loggedLinkLimit = true;
            Debug.LogWarning("[Haptics Tutorial] Cube tracking needs a Quest 3/3S build on-device. The camera feed does not run over Quest Link.");
        }
    }
}
