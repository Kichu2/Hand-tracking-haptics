using System;
using Meta.XR.BuildingBlocks;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace HapticsTutorial.Editor
{
    /// <summary>
    /// Applies Meta's Quest 3s hand-tracking checklist: Android + OpenXR, Camera Rig,
    /// Hand Tracking building blocks, Controllers and Hands, V2 skeleton.
    /// </summary>
    [InitializeOnLoad]
    static class MetaQuestHandTrackingSetup
    {
        const string AppliedKey = "HapticsTutorial.MetaQuestGuideApplied.v1";
        const string ScenePath = "Assets/Scenes/HapticsTutorial.unity";
        const string CameraRigBlockId = "e47682b9-c270-40b1-b16d-90b627a5ce1b";
        const string HandTrackingBlockId = "8b26b298-7bf4-490e-b245-a039c0184303";
        const string CameraRigPrefabGuid = "126d619cf4daa52469682f85c1378b4a";
        const string HandPrefabGuid = "929a4e1a0ccfe2048b9390905f78fc4d";

        static MetaQuestHandTrackingSetup()
        {
            if (EditorPrefs.GetBool(AppliedKey, false))
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                try
                {
                    Apply();
                    EditorPrefs.SetBool(AppliedKey, true);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Haptics Tutorial] Meta Quest hand-tracking setup failed: {ex}");
                }
            };
        }

        [MenuItem("Haptics Tutorial/Apply Meta Quest Hand Tracking Guide")]
        public static void MenuApply()
        {
            Apply();
            EditorPrefs.SetBool(AppliedKey, true);
        }

        public static void Apply()
        {
            ConfigureOpenXr();
            ConfigureOvrProject();
            InstallBuildingBlocksInScene();
            TryFixProjectValidation();
            TrySwitchToAndroid();
            MetaHandGrabSetup.Apply();
            MetaPassthroughCameraSetup.Apply();
            Debug.Log("[Haptics Tutorial] Meta Quest hand tracking guide applied: OpenXR, Camera Rig, Hand Tracking, Controllers and Hands, OpenXR/V2 skeleton.");
        }

        static void ConfigureOpenXr()
        {
            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                AssignOpenXrLoader(group);
                EnableOpenXrFeatures(group);
            }

            AssetDatabase.SaveAssets();
        }

        static void AssignOpenXrLoader(BuildTargetGroup group)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
                return;

            var general = perTarget.SettingsForBuildTarget(group);
            if (general?.Manager == null)
                return;

            XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(general);
        }

        static void EnableOpenXrFeatures(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Android &&
                !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogWarning("[Haptics Tutorial] Android Build Support is missing, so Android OpenXR features cannot be created. Install it in Unity Hub, then run Haptics Tutorial > Apply Meta Quest Hand Tracking Guide.");
                return;
            }

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
                return;

            foreach (var feature in settings.GetFeatures<OpenXRFeature>())
            {
                if (feature == null)
                    continue;

                var typeName = feature.GetType().Name;
                var enable =
                    typeName == "HandTracking" ||
                    typeName == "MetaHandTrackingAim" ||
                    typeName == "MetaQuestFeature" ||
                    typeName == "MetaXRFeature" ||
                    typeName == "OculusTouchControllerProfile" ||
                    typeName == "HandInteractionProfile" ||
                    typeName == "HandCommonPosesInteraction" ||
                    typeName == "MetaQuestTouchPlusControllerProfile" ||
                    typeName == "MetaQuestTouchProControllerProfile" ||
                    typeName == "KHRSimpleControllerProfile";

                if (enable && !feature.enabled)
                {
                    feature.enabled = true;
                    EditorUtility.SetDirty(feature);
                }
            }

            EditorUtility.SetDirty(settings);
        }

        static void ConfigureOvrProject()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config != null)
            {
                config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
                config.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
                config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
                config.isPassthroughCameraAccessEnabled = true;
                config.minHorizonOsSdkVersion = 74;
                config.targetDeviceTypes.Remove(OVRProjectConfig.DeviceType.Quest);
                config.targetDeviceTypes.Remove(OVRProjectConfig.DeviceType.Quest2);
                config.targetDeviceTypes.Remove(OVRProjectConfig.DeviceType.QuestPro);
                if (!config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3))
                    config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3);
                if (!config.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3S))
                    config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3S);
                OVRProjectConfig.CommitProjectConfig(config);
            }

            var runtime = OVRRuntimeSettings.GetRuntimeSettings();
            if (runtime != null)
            {
                runtime.HandSkeletonVersion = OVRHandSkeletonVersion.OpenXR;
                EditorUtility.SetDirty(runtime);
            }

            AssetDatabase.SaveAssets();
        }

        static void InstallBuildingBlocksInScene()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    Debug.LogWarning("[Haptics Tutorial] Save the current scene, then run Haptics Tutorial > Apply Meta Quest Hand Tracking Guide to add the Camera Rig and hands.");
                    return;
                }

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var scene = EditorSceneManager.GetActiveScene();

            foreach (var origin in UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                origin.gameObject.SetActive(false);

            foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cam.CompareTag("MainCamera") && cam.GetComponentInParent<OVRCameraRig>() == null)
                    cam.gameObject.SetActive(false);
            }

            var cameraRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
                cameraRig = InstantiateCameraRig();

            ConfigureOvrManager(cameraRig.GetComponent<OVRManager>());
            cameraRig.EnsureGameObjectIntegrity();

            if (UnityEngine.Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
                InstantiateHands(cameraRig);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static OVRCameraRig InstantiateCameraRig()
        {
            var path = AssetDatabase.GUIDToAssetPath(CameraRigPrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("OVRCameraRig prefab not found. Meta XR SDK Core is missing.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "[BuildingBlock] Camera Rig";
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            AddBuildingBlock(instance, CameraRigBlockId);
            return instance.GetComponent<OVRCameraRig>();
        }

        static void InstantiateHands(OVRCameraRig cameraRig)
        {
            var path = AssetDatabase.GUIDToAssetPath(HandPrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new InvalidOperationException("Hand Tracking building-block prefab not found.");

            var version = OVRRuntimeSettings.Instance.HandSkeletonVersion;
            var leftAnchor = cameraRig.leftHandAnchor ?? cameraRig.transform.Find("TrackingSpace/LeftHandAnchor");
            var rightAnchor = cameraRig.rightHandAnchor ?? cameraRig.transform.Find("TrackingSpace/RightHandAnchor");
            SpawnHand(prefab, leftAnchor, "[BuildingBlock] Hand Tracking left", OVRHand.Hand.HandLeft, version);
            SpawnHand(prefab, rightAnchor, "[BuildingBlock] Hand Tracking right", OVRHand.Hand.HandRight, version);
        }

        static void SpawnHand(GameObject prefab, Transform parent, string name, OVRHand.Hand hand, OVRHandSkeletonVersion version)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            instance.SetActive(true);
            if (parent != null)
                instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            SetSerializedEnum(instance.GetComponent<OVRHand>(), "HandType", (int)hand);
            SetSerializedEnum(instance.GetComponent<OVRSkeleton>(), "_skeletonType", (int)hand.AsSkeletonType(version));
            SetSerializedEnum(instance.GetComponent<OVRMesh>(), "_meshType", (int)hand.AsMeshType(version));
            AddBuildingBlock(instance, HandTrackingBlockId);
        }

        static void ConfigureOvrManager(OVRManager manager)
        {
            if (manager == null)
                return;

            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            manager.SimultaneousHandsAndControllersEnabled = true;
            manager.launchSimultaneousHandsControllersOnStartup = true;
            manager.wideMotionModeHandPosesEnabled = true;
            manager.fastMotionModeHandPosesEnabled = true;
            EditorUtility.SetDirty(manager);
        }

        static void AddBuildingBlock(GameObject go, string blockId)
        {
            if (go.GetComponent<BuildingBlock>() != null)
                return;

            var block = go.AddComponent<BuildingBlock>();
            var so = new SerializedObject(block);
            so.FindProperty("blockId").stringValue = blockId;
            so.FindProperty("version").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetSerializedEnum(UnityEngine.Object target, string property, int value)
        {
            if (target == null)
                return;

            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop != null)
            {
                prop.intValue = value;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void TryFixProjectValidation()
        {
            try
            {
                OVRProjectSetup.FixAllAsync(BuildTargetGroup.Android);
                OVRProjectSetup.FixAllAsync(BuildTargetGroup.Standalone);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Haptics Tutorial] Project Validation Fix All could not run automatically: {ex.Message}. Open Edit > Project Settings > XR Plug-in Management > Project Validation and click Fix All.");
            }
        }

        static void TrySwitchToAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogWarning("[Haptics Tutorial] Cannot switch to Android: Android Build Support is not installed in Unity Hub.");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                return;

            Debug.Log("[Haptics Tutorial] Switching platform to Android. Wait for Unity to finish the switch.");
            EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android);
        }
    }
}
