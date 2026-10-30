using System;
using System.IO;
using System.Linq;
using HapticsTutorial;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace HapticsTutorial.Editor
{
    [InitializeOnLoad]
    static class XrTutorialSetup
    {
        const string PhaseKey = "HapticsTutorial.SetupPhase.v2";
        const string ScenePath = "Assets/Scenes/HapticsTutorial.unity";
        const int PhaseXr = 0;
        const int PhaseSamples = 1;
        const int PhaseScene = 2;
        const int PhaseAndroid = 3;
        const int PhaseDone = 100;

        static XrTutorialSetup()
        {
            EditorApplication.delayCall += ContinueSetupIfNeeded;
        }

        [MenuItem("Haptics Tutorial/Run Full Setup")]
        public static void RunFullSetup()
        {
            EditorPrefs.SetInt(PhaseKey, PhaseXr);
            ContinueSetupIfNeeded();
        }

        [MenuItem("Haptics Tutorial/Configure XR Plug-in Management")]
        public static void MenuConfigureXr()
        {
            ConfigureXrPlugins();
        }

        [MenuItem("Haptics Tutorial/Import XR Samples")]
        public static void MenuImportSamples()
        {
            ImportRequiredSamples();
        }

        [MenuItem("Haptics Tutorial/Build Tutorial Scene")]
        public static void MenuBuildScene()
        {
            BuildTutorialScene();
        }

        [MenuItem("Haptics Tutorial/Configure Quest Android Settings")]
        public static void MenuAndroid()
        {
            ConfigureQuestAndroid();
        }

        [MenuItem("Haptics Tutorial/Switch Platform to Android")]
        public static void MenuSwitchAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log("[Haptics Tutorial] Switching active build target to Android. Wait for the reimport to finish.");
        }

        [MenuItem("Haptics Tutorial/Build And Run")]
        public static void MenuBuildAndRun()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUtility.DisplayDialog(
                    "Haptics Tutorial",
                    "Switch the platform to Android first (Haptics Tutorial > Switch Platform to Android), then run Build And Run after the switch finishes.",
                    "OK");
                return;
            }

            const string output = "Builds/HapticsTutorial.apk";
            Directory.CreateDirectory("Builds");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.AutoRunPlayer
            };

            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Haptics Tutorial] Build result: {report.summary.result}");
        }

        static void ContinueSetupIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            var phase = EditorPrefs.GetInt(PhaseKey, PhaseXr);
            if (phase >= PhaseDone)
                return;

            try
            {
                switch (phase)
                {
                    case PhaseXr:
                        ConfigureXrPlugins();
                        EditorPrefs.SetInt(PhaseKey, PhaseSamples);
                        EditorApplication.delayCall += ContinueSetupIfNeeded;
                        break;
                    case PhaseSamples:
                        var imported = ImportRequiredSamples();
                        EditorPrefs.SetInt(PhaseKey, PhaseScene);
                        if (imported)
                            EditorApplication.delayCall += ContinueSetupIfNeeded;
                        else
                            EditorApplication.delayCall += ContinueSetupIfNeeded;
                        break;
                    case PhaseScene:
                        if (!PrefabsReady())
                        {
                            var retries = SessionState.GetInt("HapticsTutorial.PrefabRetries", 0);
                            if (retries < 8)
                            {
                                SessionState.SetInt("HapticsTutorial.PrefabRetries", retries + 1);
                                Debug.Log("[Haptics Tutorial] Waiting for XR sample prefabs to finish importing...");
                                EditorApplication.delayCall += ContinueSetupIfNeeded;
                                return;
                            }

                            Debug.LogWarning("[Haptics Tutorial] Sample prefabs not found yet. Building a fallback XR Origin.");
                        }

                        if (!File.Exists(ScenePath))
                            BuildTutorialScene();
                        else
                            Debug.Log("[Haptics Tutorial] Tutorial scene already exists; skipping rebuild.");
                        EditorPrefs.SetInt(PhaseKey, PhaseAndroid);
                        EditorApplication.delayCall += ContinueSetupIfNeeded;
                        break;
                    case PhaseAndroid:
                        ConfigureQuestAndroid();
                        EditorPrefs.SetInt(PhaseKey, PhaseDone);
                        Debug.Log("[Haptics Tutorial] Setup finished. Open Scenes/HapticsTutorial and press Play to verify simulated hands. Connect a Quest 3s (Developer Mode) after installing Android Build Support, then use Haptics Tutorial > Switch Platform to Android and Build And Run.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Haptics Tutorial] Setup failed at phase {phase}: {ex}");
            }
        }

        static void ConfigureXrPlugins()
        {
            EnsureFolder("Assets/XR");
            EnsureFolder("Assets/XR/Settings");

            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                AssignOpenXrLoader(group);
                EnableOpenXrFeatures(group);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Haptics Tutorial] Enabled OpenXR for Standalone and Android, including Hand Tracking Subsystem and Meta Hand Tracking Aim.");
        }

        static void AssignOpenXrLoader(BuildTargetGroup group)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/Settings/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }

            var general = perTarget.SettingsForBuildTarget(group);
            if (general == null)
            {
                general = ScriptableObject.CreateInstance<XRGeneralSettings>();
                general.name = $"{group} Settings";
                AssetDatabase.AddObjectToAsset(general, perTarget);
                perTarget.SetSettingsForBuildTarget(group, general);
            }

            if (general.Manager == null)
            {
                var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
                manager.name = $"{group} Providers";
                AssetDatabase.AddObjectToAsset(manager, perTarget);
                general.Manager = manager;
            }

            XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(general);
        }

        static void EnableOpenXrFeatures(BuildTargetGroup group)
        {
            if (group == BuildTargetGroup.Android &&
                !BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogWarning("[Haptics Tutorial] Unity Android Build Support is not installed, so OpenXR Android features (Meta Quest Support, Hand Tracking Subsystem on device) cannot be created yet. Install Android Build Support in Unity Hub, then run Haptics Tutorial > Configure XR Plug-in Management.");
                return;
            }

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null)
            {
                Debug.LogWarning($"[Haptics Tutorial] OpenXR settings missing for {group}.");
                return;
            }

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
                    Debug.Log($"[Haptics Tutorial] Enabled OpenXR feature '{typeName}' for {group}.");
                }
            }

            EditorUtility.SetDirty(settings);
        }

        static bool ImportRequiredSamples()
        {
            var importedAny = false;
            importedAny |= CopyPackageSamples(
                "com.unity.xr.interaction.toolkit",
                "Starter Assets",
                "XR Interaction Simulator",
                "XR Device Simulator",
                "Hands Interaction Demo");
            importedAny |= CopyPackageSamples(
                "com.unity.xr.hands",
                "HandVisualizer");

            if (importedAny)
                AssetDatabase.Refresh();
            return importedAny;
        }

        static bool CopyPackageSamples(string packageName, params string[] sampleFolders)
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == packageName);
            if (package == null)
            {
                Debug.LogWarning($"[Haptics Tutorial] Package {packageName} is not registered yet.");
                return false;
            }

            var imported = false;
            foreach (var folder in sampleFolders)
            {
                var src = Path.Combine(package.resolvedPath, "Samples~", folder);
                var dest = Path.Combine("Assets", "Samples", package.displayName, package.version, folder)
                    .Replace("\\", "/");
                if (Directory.Exists(dest))
                    continue;
                if (!Directory.Exists(src))
                {
                    Debug.LogWarning($"[Haptics Tutorial] Sample source missing: {src}");
                    continue;
                }

                EnsureFolder(Path.GetDirectoryName(dest)?.Replace("\\", "/"));
                FileUtil.CopyFileOrDirectory(src.Replace("\\", "/"), dest);
                Debug.Log($"[Haptics Tutorial] Imported sample '{folder}' to {dest}");
                imported = true;
            }

            return imported;
        }

        static bool PrefabsReady()
        {
            return FindPrefab("XR Origin Hands") != null ||
                   FindPrefab("XR Origin (XR Rig)") != null ||
                   FindPrefab("XR Origin") != null;
        }

        static void BuildTutorialScene()
        {
            EnsureFolder("Assets/HapticsTutorial");
            EnsureFolder("Assets/HapticsTutorial/Materials");
            EnsureFolder("Assets/Scenes");

            var floorMat = GetOrCreateLit("Floor", new Color(0.18f, 0.18f, 0.2f));
            var tableMat = GetOrCreateLit("Table", new Color(0.45f, 0.32f, 0.2f));
            var cubeMat = GetOrCreateLit("GraspCube", new Color(0.95f, 0.45f, 0.15f));
            var zoneMat = GetOrCreateLit("TargetZone", new Color(0.2f, 0.85f, 0.35f, 0.35f), true);
            var propMat = GetOrCreateLit("HapticProp", new Color(0.35f, 0.75f, 1f, 0.5f), true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLight();
            CreateVolume();

            var origin = PlaceXrOrigin();
            PlaceSimulator();

            CreateBox("Desk", new Vector3(0f, 0.72f, 0.55f), new Vector3(1.1f, 0.04f, 0.55f), tableMat);
            CreateBox("Floor", new Vector3(0f, -0.01f, 0.4f), new Vector3(6f, 0.02f, 6f), floorMat);

            var grasp = CreateGrabObject("Grasp Cube", new Vector3(-0.22f, 0.8f, 0.42f), Vector3.one * 0.08f, cubeMat);
            var zoneGo = CreateBox("Target Zone", new Vector3(0.22f, 0.745f, 0.42f), new Vector3(0.18f, 0.03f, 0.18f), zoneMat);
            var zoneCol = zoneGo.GetComponent<BoxCollider>();
            zoneCol.isTrigger = true;
            var zoneRb = zoneGo.AddComponent<Rigidbody>();
            zoneRb.isKinematic = true;
            var zone = zoneGo.AddComponent<TargetZone>();
            zone.SetTrackedObject(grasp.transform);

            var prop = CreateGrabObject("Passive Haptic Prop", new Vector3(0.0f, 0.82f, 0.58f), Vector3.one * 0.12f, propMat);
            prop.GetComponent<XRGrabInteractable>().enabled = false;

            var presenter = CreateInstructionCanvas();

            var loggerGo = new GameObject("Session Logger");
            loggerGo.AddComponent<SessionLogger>();

            var graspStageGo = new GameObject("Stage 1 Grasp");
            var graspStage = graspStageGo.AddComponent<GraspStage>();
            graspStage.ConfigureIdentity(
                "grasp",
                "1. Pick up the cube",
                "Grab the orange cube with your hand and drop it on the green pad.");
            graspStage.Configure(grasp.GetComponent<XRGrabInteractable>(), zone);

            var propStageGo = new GameObject("Stage 2 Prop");
            var propStage = propStageGo.AddComponent<PropRegistrationStage>();
            propStage.ConfigureIdentity(
                "prop_register",
                "2. Align the physical prop",
                "Stick the six printed markers on the cube, any face, any way up. Turn it slowly in view until the blue ghost matches it. Rest your hand on it to confirm.");
            propStage.Configure(
                prop.transform,
                prop.GetComponent<XRGrabInteractable>(),
                prop.GetComponent<Collider>(),
                prop.GetComponent<Renderer>());

            var managerGo = new GameObject("Stage Manager");
            var manager = managerGo.AddComponent<StageManager>();
            manager.Configure(new TutorialStage[] { graspStage, propStage }, presenter);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            Debug.Log($"[Haptics Tutorial] Saved {ScenePath} (origin={(origin != null ? origin.name : "fallback")}).");
        }

        static GameObject PlaceXrOrigin()
        {
            var prefab =
                FindPrefab("XR Origin Hands") ??
                FindPrefab("Complete XR Origin") ??
                FindPrefab("XR Origin (XR Rig)") ??
                FindPrefab("XR Origin");

            if (prefab != null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                if (instance.GetComponent<QuestHandRig>() == null)
                    instance.AddComponent<QuestHandRig>();
                Debug.Log($"[Haptics Tutorial] Instantiated XR Origin prefab '{prefab.name}'.");
                return instance;
            }

            Debug.LogWarning("[Haptics Tutorial] Building a minimal XR Origin. Import Hands Interaction Demo for tracked-hand visuals.");
            var originGo = new GameObject("XR Origin");
            var origin = originGo.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform);
            offset.transform.localPosition = new Vector3(0f, 1.36144f, 0f);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform);
            camGo.transform.localPosition = Vector3.zero;
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            camGo.AddComponent<AudioListener>();
            AddUrpCameraData(camGo);

            origin.Camera = cam;
            origin.Origin = originGo;
            origin.CameraFloorOffsetObject = offset;

            if (UnityEngine.Object.FindFirstObjectByType<XRInteractionManager>() == null)
                new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            return originGo;
        }

        static void PlaceSimulator()
        {
            var prefab =
                FindPrefab("XR Interaction Simulator") ??
                FindPrefab("XR Device Simulator");

            if (prefab == null)
            {
                Debug.LogWarning("[Haptics Tutorial] Simulator prefab not found. Import the XR Interaction Simulator sample.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.AddComponent<EditorOnlyObject>();
            Debug.Log($"[Haptics Tutorial] Instantiated simulator prefab '{prefab.name}'.");
        }

        static GameObject CreateGrabObject(string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var grab = go.AddComponent<XRGrabInteractable>();
            grab.throwOnDetach = true;
            grab.useDynamicAttach = true;
            return go;
        }

        static GameObject CreateBox(string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
                UnityEngine.Object.DestroyImmediate(rb);
            return go;
        }

        static InstructionPresenter CreateInstructionCanvas()
        {
            var canvasGo = new GameObject("Instruction Canvas");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(900f, 280f);
            canvasGo.transform.position = new Vector3(0f, 1.45f, 1.05f);
            canvasGo.transform.rotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.0014f;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            var panelImage = panelGo.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);

            var title = CreateText(panelGo.transform, "Title", 52, FontStyle.Bold, new Vector2(0f, 70f), new Vector2(840f, 80f));
            var body = CreateText(panelGo.transform, "Body", 36, FontStyle.Normal, new Vector2(0f, -30f), new Vector2(840f, 140f));
            title.text = "Haptics tutorial";
            body.text = "Loading...";

            var presenter = canvasGo.AddComponent<InstructionPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.FindProperty("panel").objectReferenceValue = panelImage;
            so.ApplyModifiedPropertiesWithoutUndo();
            return presenter;
        }

        static Text CreateText(Transform parent, string name, int fontSize, FontStyle style, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPos;
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                        ?? Font.CreateDynamicFontFromOSFont("Arial", fontSize);
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        static void CreateLight()
        {
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var urpLight = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");
            if (urpLight != null)
                lightGo.AddComponent(urpLight);
        }

        static void CreateVolume()
        {
            var profile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            var volumeGo = new GameObject("Global Volume");
            var volume = volumeGo.AddComponent<UnityEngine.Rendering.Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static void AddUrpCameraData(GameObject cameraGo)
        {
            var urpCam = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCam != null && cameraGo.GetComponent(urpCam) == null)
                cameraGo.AddComponent(urpCam);
        }

        static void ConfigureQuestAndroid()
        {
            PlayerSettings.companyName = "HapticsLab";
            PlayerSettings.productName = "Hand Tracking Haptics";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.hapticslab.handtrackinghaptics");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.Android.androidTVCompatibility = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            AddSceneToBuildSettings(ScenePath);

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogWarning("[Haptics Tutorial] Android player settings were written, but Unity Hub does not have Android Build Support for 6000.3.7f1. Install it (Android Build Support + OpenJDK + Android SDK) before Switch Platform / Build And Run.");
                return;
            }

            Debug.Log("[Haptics Tutorial] Quest Android player settings applied (ARM64, IL2CPP, min SDK 32, Vulkan, OpenXR).");
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existing = scenes.FindIndex(s => s.path == path);
            if (existing >= 0)
            {
                var scene = scenes[existing];
                scene.enabled = true;
                scenes.RemoveAt(existing);
                scenes.Insert(0, scene);
            }
            else
            {
                scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static GameObject FindPrefab(string nameFragment)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{nameFragment} t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.IndexOf("/Samples/", StringComparison.OrdinalIgnoreCase) < 0 &&
                    path.IndexOf("HapticsTutorial", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var file = Path.GetFileNameWithoutExtension(path);
                if (file.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            foreach (var guid in AssetDatabase.FindAssets($"{nameFragment} t:Prefab"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var file = Path.GetFileNameWithoutExtension(path);
                if (file.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return null;
        }

        static Material GetOrCreateLit(string name, Color color, bool transparent = false)
        {
            var path = $"Assets/HapticsTutorial/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace("\\", "/");
            var leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && parent != path)
            {
                EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, leaf);
            }
        }
    }
}
