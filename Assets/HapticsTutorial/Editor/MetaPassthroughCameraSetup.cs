using System;
using HapticsTutorial;
using Meta.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HapticsTutorial.Editor
{
    [InitializeOnLoad]
    static class MetaPassthroughCameraSetup
    {
        const string AppliedKey = "HapticsTutorial.PassthroughCamera.v1";
        const string ScenePath = "Assets/Scenes/HapticsTutorial.unity";

        static MetaPassthroughCameraSetup()
        {
            EditorApplication.delayCall += () => EditorApplication.delayCall += TryApplyOnce;
        }

        static void TryApplyOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;
            if (EditorPrefs.GetBool(AppliedKey, false) && SceneHasLinker())
                return;

            try
            {
                if (Apply())
                    EditorPrefs.SetBool(AppliedKey, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Haptics Tutorial] Passthrough camera setup failed: {ex}");
            }
        }

        [MenuItem("Haptics Tutorial/Enable Passthrough Cube Tracking")]
        public static void MenuApply()
        {
            if (Apply())
                EditorPrefs.SetBool(AppliedKey, true);
        }

        public static bool Apply()
        {
            ConfigureProject();

            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                    return false;
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var cameraRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
                return false;

            if (cameraRig.GetComponent<OVRPassthroughLayer>() == null)
                cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();

            var manager = cameraRig.GetComponent<OVRManager>();
            if (manager != null)
            {
                manager.isInsightPassthroughEnabled = true;
                // Internal in the SDK, but serialized.
                var managerSo = new SerializedObject(manager);
                var requestCamera = managerSo.FindProperty("requestPassthroughCameraAccessPermissionOnStartup");
                if (requestCamera != null)
                    requestCamera.boolValue = true;
                managerSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(manager);
            }

            var linker = UnityEngine.Object.FindFirstObjectByType<QuestCameraCubeLinker>(FindObjectsInactive.Include);
            if (linker == null)
            {
                var go = new GameObject("Quest Camera Cube Linker");
                linker = go.AddComponent<QuestCameraCubeLinker>();
                go.AddComponent<PassthroughCameraAccess>();
            }

            var prop = GameObject.Find("Passive Haptic Prop");
            if (prop != null)
                linker.SetVirtualProp(prop.transform);

            var stage = UnityEngine.Object.FindFirstObjectByType<PropRegistrationStage>(FindObjectsInactive.Include);
            if (stage != null)
            {
                var so = new SerializedObject(stage);
                so.FindProperty("instruction").stringValue =
                    "Stick the six printed markers on the cube, any face, any way up. Turn it slowly in view until the blue ghost matches it. Rest your hand on it to confirm.";
                var linkerProp = so.FindProperty("cameraLinker");
                if (linkerProp != null)
                    linkerProp.objectReferenceValue = linker;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Haptics Tutorial] Passthrough Camera API cube tracking enabled for Quest 3 / 3S.");
            return true;
        }

        static bool SceneHasLinker()
        {
            return UnityEngine.Object.FindFirstObjectByType<QuestCameraCubeLinker>(FindObjectsInactive.Include) != null;
        }

        static void ConfigureProject()
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            if (config == null)
                return;

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
            AssetDatabase.SaveAssets();
        }
    }
}
