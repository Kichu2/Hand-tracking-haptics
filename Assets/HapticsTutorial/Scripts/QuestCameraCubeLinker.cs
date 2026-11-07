using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Meta.XR;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsTutorial
{
    /// <summary>
    /// Tracks real marked cubes on Quest 3 / 3S using Passthrough Camera Access (Horizon OS v74+)
    /// and moves their virtual twins to match:
    /// markers 0–5 (4.8 cm) drive the blue haptic prop, markers 6–11 (4.2 cm, fits a Rubik's cube)
    /// drive the orange grasp cube. The marker size comes from the id, so nothing needs setting.
    /// Both cubes start from the layout of their printed nets (blue_cube_net_print.pdf,
    /// rubiks_cube_net_print.pdf), so they track from the first marker seen; markers stuck on
    /// some other way are relearned while in view. Keeps the last pose when a cube leaves
    /// the view. Camera frames are headset-only; Play Mode uses a look-ray fallback for the prop.
    /// </summary>
    public class QuestCameraCubeLinker : MonoBehaviour
    {
        /// <summary>Black square size of markers 0–5 (blue_cube_net_print.pdf or cube_markers_print.pdf at 100%).</summary>
        public const float MarkerSize = 0.048f;

        /// <summary>Black square size of markers 6–11 (docs/markers/small_cube_markers_print.pdf at 100%).</summary>
        public const float SmallMarkerSize = 0.042f;

        /// <summary>Edge of the cube the Rubik's cube net (page 1 of rubiks_cube_net_print.pdf) is folded onto.</summary>
        public const float GraspCubeEdge = 0.060f;

        /// <summary>Edge of the blue cube built from blue_cube_net_print.pdf.</summary>
        public const float PropCubeEdge = 0.065f;

        // Smaller markers give unreliable single-marker poses, so they don't teach the layout.
        const float MinLearnArea = 1200f;
        const float VisibleSeconds = 0.6f;

        [SerializeField] Transform virtualProp;
        [SerializeField] Transform graspCube;
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] int minFramesBetweenReads = 2;

        // Smoothing rate (1/s): gentle while the cube is still, quick once it really moves.
        const float StillLerp = 8f;
        const float MovingLerp = 30f;

        class TrackedCube
        {
            public readonly string Name;
            public readonly string LayoutKey;
            public readonly int FirstId;
            public readonly float MarkerSize;
            public readonly CubeMarkerMap Map;
            public readonly List<ArucoDetection> Solved = new List<ArucoDetection>(6);
            public readonly List<Pose> SolvedPoses = new List<Pose>(6);
            public readonly List<int> LearnIds = new List<int>(6);
            public readonly List<Pose> LearnPoses = new List<Pose>(6);
            public readonly MarkerLayout Layout;
            public Transform Target;
            public bool Follow = true;
            public Pose Pose;
            public bool HasPose;
            public bool FromMarkers;
            public float LastSeen = -999f;
            public float NextSave;
            public readonly PoseGate Gate = new PoseGate();

            public TrackedCube(string name, string layoutKey, int firstId, float markerSize)
            {
                Name = name;
                LayoutKey = layoutKey;
                FirstId = firstId;
                MarkerSize = markerSize;
                Map = new CubeMarkerMap(markerSize);
                Layout = (int id, out Pose pose) => Map.TryMarkerInCube(id - FirstId, out pose);
            }

            public bool Owns(int id) => id >= FirstId && id < FirstId + CubeMarkerMap.MaxMarkers;
            public bool IsVisible => FromMarkers && Time.time - LastSeen < VisibleSeconds;
        }

        // No saved layouts: both always start from their known net layouts (see Awake).
        readonly TrackedCube _prop = new TrackedCube("prop", null, 0, MarkerSize);
        readonly TrackedCube _grasp = new TrackedCube("grasp cube", null, 6, SmallMarkerSize);
        TrackedCube[] _cubes;
        bool _loggedLinkLimit;
        float _nextStatusLog;
        bool _graspTakenOver;
        Rigidbody _graspBody;
        XRGrabInteractable _graspXrGrab;
        Grabbable[] _graspGrabbables;

        // Camera frames are read back without stalling the main thread; the camera pose is
        // captured with each request so it matches the image.
        NativeArray<Color32> _frame;
        bool _readPending;
        Pose _frameCameraPose;
        Vector2Int _frameResolution;
        DateTime _lastFrameStamp;
        int _lastReadFrame = -999;
        int _framesProcessed;
        float _rateWindowStart;
        float _framesPerSecond;

        /// <summary>Learned edge length of the real haptic prop (a default until two faces are linked).</summary>
        public float CubeSize => _prop.Map.CubeSize;

        public bool HasPoint => _prop.HasPose;
        public Vector3 WorldPoint => _prop.Pose.position;
        public Pose WorldPose => _prop.Pose;
        public bool IsTracking => _prop.HasPose && Time.time - _prop.LastSeen < VisibleSeconds;

        /// <summary>True once the real grasp cube (markers 6–11) has been seen; the virtual one then follows it.</summary>
        public bool GraspCubeTracked => _grasp.FromMarkers;
        public bool GraspCubeVisible => _grasp.IsVisible;
        public Pose GraspCubePose => _grasp.Pose;

        public void SetVirtualProp(Transform prop) => _prop.Target = prop;

        public void SetFollowEnabled(bool enabled) => _prop.Follow = enabled;

        void Awake()
        {
            _cubes = new[] { _prop, _grasp };
            _prop.Target = virtualProp;
            _grasp.Target = graspCube;
            EnsurePassthrough();
            HeadsetCameraPermissions.Request();
            // The printed nets fix where each marker sits, so both cubes track from the first
            // marker seen. The blue two-piece net folds to the same layout as the orange cross net.
            _prop.Map.UseLayout(CubeMarkerMap.CrossNetLayout(PropCubeEdge));
            _grasp.Map.UseLayout(CubeMarkerMap.CrossNetLayout(GraspCubeEdge));
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveLayouts();
        }

        void OnDisable() => SaveLayouts();

        void SaveLayouts()
        {
            if (_cubes == null)
                return;
            foreach (var cube in _cubes)
                SaveLayout(cube);
        }

        static void SaveLayout(TrackedCube cube)
        {
            if (cube.LayoutKey == null || !cube.Map.Dirty || cube.Map.LockedCount < 2)
                return;
            PlayerPrefs.SetString(cube.LayoutKey, cube.Map.Serialize());
            PlayerPrefs.Save();
            cube.Map.Dirty = false;
        }

        void Start()
        {
            if (_prop.Target == null)
                _prop.Target = GameObject.Find("Passive Haptic Prop")?.transform;
            if (_grasp.Target == null)
                _grasp.Target = GameObject.Find("Grasp Cube")?.transform;
        }

        void OnDestroy()
        {
            if (_readPending)
                AsyncGPUReadback.WaitAllRequests();
            if (_frame.IsCreated)
                _frame.Dispose();
        }

        void LateUpdate()
        {
            var cameraRunning = cameraAccess != null && cameraAccess.IsPlaying;
            if (cameraRunning)
                RequestFrame();
            else
                LogCameraWaiting();

            if (_prop.Follow && !_prop.HasPose && !cameraRunning)
                FallbackLookRay();

            foreach (var cube in _cubes)
            {
                if (!cube.Follow || !cube.HasPose || cube.Target == null)
                    continue;
                if (cube == _grasp)
                    TakeOverGraspCube();

                var target = cube.Target;
                var distance = Vector3.Distance(target.position, cube.Pose.position);
                var angle = Quaternion.Angle(target.rotation, cube.Pose.rotation);
                var moving = Mathf.Clamp01(Mathf.Max((distance - 0.004f) / 0.03f, (angle - 2f) / 20f));
                var t = 1f - Mathf.Exp(-Mathf.Lerp(StillLerp, MovingLerp, moving) * Time.deltaTime);
                target.position = Vector3.Lerp(target.position, cube.Pose.position, t);
                target.rotation = Quaternion.Slerp(target.rotation, cube.Pose.rotation, t);
                if (cube.FromMarkers)
                    target.localScale = Vector3.Lerp(target.localScale, Vector3.one * cube.Map.CubeSize, t);
            }
        }

        // The real cube now carries the virtual one: stop physics and hand-grab from fighting it.
        // Checked every frame because stages re-enable grabbing, and Meta's Grabbable restores a
        // non-kinematic body (and throws it) when it is released or disabled.
        void TakeOverGraspCube()
        {
            var target = _grasp.Target;
            if (!_graspTakenOver)
            {
                _graspTakenOver = true;
                _graspBody = target.GetComponent<Rigidbody>();
                _graspXrGrab = target.GetComponent<XRGrabInteractable>();
                _graspGrabbables = target.GetComponentsInChildren<Grabbable>(true);
                Debug.Log("[Haptics Tutorial] Real grasp cube found; the orange cube now follows it.");
            }

            if (_graspXrGrab != null && _graspXrGrab.enabled)
                _graspXrGrab.enabled = false;
            MetaGrabUtil.SetEnabled(target.gameObject, false);
            foreach (var grabbable in _graspGrabbables)
            {
                if (grabbable != null && grabbable.enabled)
                    grabbable.enabled = false;
            }

            if (_graspBody != null && (!_graspBody.isKinematic || _graspBody.useGravity ||
                                       _graspBody.interpolation != RigidbodyInterpolation.None))
            {
                // Interpolation would overwrite the transform we set each frame.
                _graspBody.isKinematic = true;
                _graspBody.useGravity = false;
                _graspBody.interpolation = RigidbodyInterpolation.None;
            }
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
                manager.isInsightPassthroughEnabled = true;

            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig != null && rig.GetComponent<OVRPassthroughLayer>() == null)
                rig.gameObject.AddComponent<OVRPassthroughLayer>();

            if (cameraAccess == null)
                cameraAccess = GetComponent<PassthroughCameraAccess>();
            if (cameraAccess == null)
                cameraAccess = gameObject.AddComponent<PassthroughCameraAccess>();

            cameraAccess.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
            // Full sensor resolution: small markers (Rubik's-cube size) need the pixels. The
            // detector downsamples for its blob search, so the extra cost is mostly the readback.
            cameraAccess.RequestedResolution = new Vector2Int(1280, 960);
        }

        // One read in flight at a time, only for a new camera image.
        void RequestFrame()
        {
            if (_readPending || Time.frameCount - _lastReadFrame < Mathf.Max(1, minFramesBetweenReads) ||
                cameraAccess.Timestamp == _lastFrameStamp)
                return;
            if (!HeadsetSupportsPassthroughCamera())
            {
                LogLinkLimitOnce();
                return;
            }

            var texture = cameraAccess.GetTexture();
            if (texture == null)
                return;

            var resolution = cameraAccess.CurrentResolution;
            if (!_frame.IsCreated || _frame.Length != resolution.x * resolution.y)
            {
                if (_frame.IsCreated)
                    _frame.Dispose();
                _frame = new NativeArray<Color32>(resolution.x * resolution.y, Allocator.Persistent);
            }

            _frameCameraPose = cameraAccess.GetCameraPose();
            _frameResolution = resolution;
            _lastFrameStamp = cameraAccess.Timestamp;
            _lastReadFrame = Time.frameCount;
            _readPending = true;
            AsyncGPUReadback.RequestIntoNativeArray(ref _frame, texture, 0, OnFrameRead);
        }

        void OnFrameRead(AsyncGPUReadbackRequest request)
        {
            _readPending = false;
            if (request.hasError || this == null || !_frame.IsCreated || cameraAccess == null || !cameraAccess.IsPlaying)
                return;

            _framesProcessed++;
            var detections = ArucoMarkerDetector.Detect(_frame, _frameResolution.x, _frameResolution.y);
            LogStatus(_frameResolution, detections);
            foreach (var cube in _cubes)
                UpdateCube(cube, detections);
        }

        void UpdateCube(TrackedCube cube, List<ArucoDetection> detections)
        {
            cube.Solved.Clear();
            cube.SolvedPoses.Clear();
            cube.LearnIds.Clear();
            cube.LearnPoses.Clear();
            foreach (var detection in detections)
            {
                if (!cube.Owns(detection.Id) ||
                    !MarkerPoseSolver.TryMarkerPose(detection, cameraAccess, cube.MarkerSize, out var markerWorld, _frameCameraPose))
                    continue;
                cube.Solved.Add(detection);
                cube.SolvedPoses.Add(markerWorld);
                if (detection.Area < MinLearnArea)
                    continue;
                cube.LearnIds.Add(detection.Id - cube.FirstId);
                cube.LearnPoses.Add(markerWorld);
            }

            if (cube.Solved.Count == 0)
                return;

            cube.Map.Observe(cube.LearnIds, cube.LearnPoses);
            if (cube.Map.Dirty && Time.time > cube.NextSave)
            {
                cube.NextSave = Time.time + 5f;
                SaveLayout(cube);
            }

            Pose? previous = cube.FromMarkers && Time.time - cube.LastSeen < VisibleSeconds ? cube.Pose : (Pose?)null;
            if (!MarkerPoseSolver.TryBodyPose(cube.Solved, cube.SolvedPoses, cameraAccess, cube.MarkerSize, cube.Layout,
                    out var pose, _frameCameraPose, previous))
                return;
            if (!cube.Gate.Accept(pose, cube.Solved.Count, Time.time))
                return;

            var wasLost = Time.time - cube.LastSeen > VisibleSeconds;
            cube.Pose = pose;
            cube.HasPose = true;
            cube.FromMarkers = true;
            cube.LastSeen = Time.time;
            if (wasLost)
                SessionLogger.Instance?.Log("cube_tracked", cube.Name, pose.position.ToString("F3"));
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
            point.y = deskHeight + _prop.Map.CubeSize * 0.5f;
            _prop.Pose = new Pose(point, Quaternion.identity);
            _prop.HasPose = true;
        }

        // Every few seconds, so `adb logcat -s Unity` shows what the tracker sees.
        void LogStatus(Vector2Int resolution, List<ArucoDetection> detections)
        {
            if (Time.time < _nextStatusLog)
                return;
            _nextStatusLog = Time.time + 3f;

            var ids = new StringBuilder();
            foreach (var detection in detections)
                ids.Append(ids.Length == 0 ? "" : ",").Append(detection.Id);
            var now = Time.time;
            if (now - _rateWindowStart > 0f)
                _framesPerSecond = _framesProcessed / (now - _rateWindowStart);
            _framesProcessed = 0;
            _rateWindowStart = now;
            var sb = new StringBuilder($"[Haptics Tutorial] Cube tracker: camera {resolution.x}x{resolution.y} at {_framesPerSecond:F0} fps processed, markers [{ids}], " +
                                       $"rows {(ArucoMarkerDetector.ImageTopDown ? "top-down" : "bottom-up")}");
            foreach (var cube in _cubes)
            {
                sb.Append($" | {cube.Name}: {cube.Map.LockedCount}/{CubeMarkerMap.MaxMarkers} learned, " +
                          $"{cube.Map.CubeSize * 100f:F1} cm, ");
                sb.Append(cube.FromMarkers ? $"seen {Time.time - cube.LastSeen:F1}s ago" : "not seen yet");
            }

            Debug.Log(sb.ToString());
        }

        void LogCameraWaiting()
        {
            if (Time.time < _nextStatusLog)
                return;
            _nextStatusLog = Time.time + 5f;
            Debug.Log("[Haptics Tutorial] Cube tracker: waiting for the passthrough camera " +
                      "(needs a Quest 3/3S build, not Quest Link, and the headset camera permission).");
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
