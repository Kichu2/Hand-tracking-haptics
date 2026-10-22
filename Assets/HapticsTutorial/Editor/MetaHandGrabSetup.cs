using System;
using System.Reflection;
using Oculus.Interaction;
using Oculus.Interaction.Editor;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.Grab;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsTutorial.Editor
{
    [InitializeOnLoad]
    static class MetaHandGrabSetup
    {
        const string AppliedKey = "HapticsTutorial.HandGrab.v3";
        const string ScenePath = "Assets/Scenes/HapticsTutorial.unity";
        const string ComprehensiveRigGuid = "0a7d2469f24041c4284c66706f84c45e";

        static MetaHandGrabSetup()
        {
            EditorApplication.delayCall += () => EditorApplication.delayCall += TryApplyOnce;
        }

        static void TryApplyOnce()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                return;

            if (EditorPrefs.GetBool(AppliedKey, false) && SceneAlreadyHasGrab())
                return;

            try
            {
                if (Apply())
                    EditorPrefs.SetBool(AppliedKey, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Haptics Tutorial] Hand grab setup failed: {ex}");
            }
        }

        [MenuItem("Haptics Tutorial/Enable Hand Grab")]
        public static void MenuApply()
        {
            if (Apply())
                EditorPrefs.SetBool(AppliedKey, true);
        }

        public static bool Apply()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != ScenePath)
            {
                if (active.isDirty)
                {
                    Debug.LogWarning("[Haptics Tutorial] Save the current scene, then run Haptics Tutorial > Enable Hand Grab.");
                    return false;
                }

                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            var cameraRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            if (cameraRig == null)
            {
                Debug.LogWarning("[Haptics Tutorial] No OVRCameraRig in the scene. Run Haptics Tutorial > Apply Meta Quest Hand Tracking Guide first.");
                return false;
            }

            EnsureInteractionRig(cameraRig);
            DisableDuplicateOvrHandMeshes(cameraRig);
            EnableHandPhysics();

            MakeGrabbable(GameObject.Find("Grasp Cube"), enableNow: true);
            MakeGrabbable(GameObject.Find("Passive Haptic Prop"), enableNow: false);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Haptics Tutorial] Hands can now grab by pinch or wrap, and the sides of the hands can push objects.");
            return true;
        }

        static bool SceneAlreadyHasGrab()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                return false;

            var cube = GameObject.Find("Grasp Cube");
            var capsules = UnityEngine.Object.FindFirstObjectByType<HandPhysicsCapsules>();
            return cube != null
                && cube.GetComponentInChildren<HandGrabInteractable>(true) != null
                && cube.GetComponentInChildren<TouchHandGrabInteractable>(true) != null
                && UnityEngine.Object.FindFirstObjectByType<OVRCameraRigRef>() != null
                && capsules != null
                && capsules.enabled;
        }

        static void EnsureInteractionRig(OVRCameraRig cameraRig)
        {
            var existing = cameraRig.GetComponentInChildren<OVRCameraRigRef>(true)
                ?? UnityEngine.Object.FindFirstObjectByType<OVRCameraRigRef>();

            GameObject instance;
            if (existing != null)
            {
                instance = InteractionRigRoot(existing, cameraRig);
            }
            else
            {
                var path = AssetDatabase.GUIDToAssetPath(ComprehensiveRigGuid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    throw new InvalidOperationException("OVR Comprehensive Interaction Rig prefab not found.");

                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.name = "[BuildingBlock] Interaction Rig";
            }

            if (instance.transform.parent != cameraRig.transform)
            {
                instance.transform.SetParent(cameraRig.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
            }

            UnityObjectAddedBroadcaster.HandleObjectWasAdded(instance);

            foreach (var rigRef in instance.GetComponentsInChildren<OVRCameraRigRef>(true))
            {
                var so = new SerializedObject(rigRef);
                var cameraProp = so.FindProperty("_ovrCameraRig");
                if (cameraProp != null)
                    cameraProp.objectReferenceValue = cameraRig;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static GameObject InteractionRigRoot(OVRCameraRigRef rigRef, OVRCameraRig cameraRig)
        {
            var t = rigRef.transform;
            while (t.parent != null && t.parent != cameraRig.transform && t.parent.GetComponent<OVRCameraRig>() == null)
                t = t.parent;
            return t.gameObject;
        }

        static void DisableDuplicateOvrHandMeshes(OVRCameraRig cameraRig)
        {
            var space = cameraRig.trackingSpace != null ? cameraRig.trackingSpace : cameraRig.transform;
            foreach (var hand in space.GetComponentsInChildren<OVRHand>(true))
            {
                if (hand.TryGetComponent<OVRSkeletonRenderer>(out var skeletonRenderer))
                    skeletonRenderer.enabled = false;
                if (hand.TryGetComponent<OVRMesh>(out var mesh))
                    mesh.enabled = false;
                if (hand.TryGetComponent<OVRMeshRenderer>(out var meshRenderer))
                    meshRenderer.enabled = false;
                if (hand.TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer))
                    skinnedMeshRenderer.enabled = false;
            }
        }

        static void EnableHandPhysics()
        {
            var hands = UnityEngine.Object.FindObjectsByType<SyntheticHand>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (hands.Length == 0)
            {
                EnableOvrSkeletonCapsules();
                return;
            }

            foreach (var syntheticHand in hands)
                AddPhysicsCapsules(syntheticHand);
        }

        static void AddPhysicsCapsules(SyntheticHand syntheticHand)
        {
            var visual = syntheticHand.GetComponentInChildren<HandVisual>(true);
            var visualGo = visual != null ? visual.gameObject : syntheticHand.gameObject;

            var radius = syntheticHand.GetComponent<JointsRadiusFeature>()
                ?? syntheticHand.GetComponentInChildren<JointsRadiusFeature>(true)
                ?? syntheticHand.GetComponentInParent<JointsRadiusFeature>();
            if (radius == null && syntheticHand.transform.parent != null)
                radius = syntheticHand.transform.parent.GetComponentInChildren<JointsRadiusFeature>(true);

            if (radius == null)
            {
                radius = syntheticHand.gameObject.AddComponent<JointsRadiusFeature>();
            }

            var radiusSo = new SerializedObject(radius);
            var handProp = radiusSo.FindProperty("_hand");
            if (handProp != null && handProp.objectReferenceValue == null)
                handProp.objectReferenceValue = syntheticHand;
            radiusSo.ApplyModifiedPropertiesWithoutUndo();

            var capsules = visualGo.GetComponent<HandPhysicsCapsules>()
                ?? visualGo.AddComponent<HandPhysicsCapsules>();
            capsules.enabled = true;
            capsules.InjectAllOVRHandPhysicsCapsules(syntheticHand, false, 0);
            capsules.InjectMask(HandFingerJointFlags.All);
            capsules.InjectJointsRadiusFeature(radius);

            var so = new SerializedObject(capsules);
            var asTriggers = so.FindProperty("_asTriggers");
            if (asTriggers != null)
                asTriggers.boolValue = false;
            var mask = so.FindProperty("_mask");
            if (mask != null)
                mask.intValue = (int)HandFingerJointFlags.All;
            var handField = so.FindProperty("_hand");
            if (handField != null)
                handField.objectReferenceValue = syntheticHand;
            var visualField = so.FindProperty("_handVisual");
            if (visualField != null && visual != null)
                visualField.objectReferenceValue = visual;
            var joints = so.FindProperty("_jointsRadiusFeature");
            if (joints != null)
                joints.objectReferenceValue = radius;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(capsules);
        }

        static void EnableOvrSkeletonCapsules()
        {
            var skeletons = UnityEngine.Object.FindObjectsByType<OVRSkeleton>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var skeleton in skeletons)
            {
                var so = new SerializedObject(skeleton);
                var enable = so.FindProperty("_enablePhysicsCapsules");
                if (enable != null)
                    enable.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void MakeGrabbable(GameObject target, bool enableNow)
        {
            if (target == null)
                return;

            if (target.GetComponentInChildren<HandGrabInteractable>(true) == null)
                QuickActionsAPI.AddGrabInteraction(target);

            AddTouchGrab(target);
            EnableWholeHandGrab(target);
            EnablePalmHoverOnHands();

            var xri = target.GetComponent<XRGrabInteractable>();
            if (xri != null)
                xri.enabled = false;

            var rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }

            var grabbable = target.GetComponent<Grabbable>();
            if (grabbable != null)
            {
                var so = new SerializedObject(grabbable);
                var throwProp = so.FindProperty("_throwWhenUnselected");
                if (throwProp != null)
                    throwProp.boolValue = true;
                var kinProp = so.FindProperty("_kinematicWhileSelected");
                if (kinProp != null)
                    kinProp.boolValue = true;
                var rbProp = so.FindProperty("_rigidbody");
                if (rbProp != null && rb != null)
                    rbProp.objectReferenceValue = rb;
                var targetProp = so.FindProperty("_targetTransform");
                if (targetProp != null)
                    targetProp.objectReferenceValue = target.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            MetaGrabUtil.SetEnabled(target, enableNow);
        }

        static void EnableWholeHandGrab(GameObject target)
        {
            foreach (var grab in target.GetComponentsInChildren<HandGrabInteractable>(true))
            {
                var so = new SerializedObject(grab);
                var types = so.FindProperty("_supportedGrabTypes");
                if (types != null)
                    types.intValue = (int)GrabTypeFlags.All;
                var align = so.FindProperty("_handAligment");
                if (align != null)
                    align.intValue = (int)HandAlignType.None;
                SetFingerRule(so.FindProperty("_palmGrabRules"), optional: false);
                SetFingerRule(so.FindProperty("_pinchGrabRules"), optional: true);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void SetFingerRule(SerializedProperty rule, bool optional)
        {
            if (rule == null)
                return;

            // Optional pinch: thumb/index/middle optional. Whole-hand palm: index+middle+ring required.
            rule.FindPropertyRelative("_thumbRequirement").enumValueIndex = 1;
            rule.FindPropertyRelative("_indexRequirement").enumValueIndex = optional ? 1 : 2;
            rule.FindPropertyRelative("_middleRequirement").enumValueIndex = optional ? 1 : 2;
            rule.FindPropertyRelative("_ringRequirement").enumValueIndex = optional ? 0 : 2;
            rule.FindPropertyRelative("_pinkyRequirement").enumValueIndex = optional ? 0 : 1;
            rule.FindPropertyRelative("_unselectMode").enumValueIndex = 0;
        }

        static void EnablePalmHoverOnHands()
        {
            var interactors = UnityEngine.Object.FindObjectsByType<HandGrabInteractor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                var so = new SerializedObject(interactor);
                var hover = so.FindProperty("_hoverOnZeroStrength");
                if (hover != null)
                    hover.boolValue = true;
                var types = so.FindProperty("_supportedGrabTypes");
                if (types != null)
                    types.intValue = (int)GrabTypeFlags.All;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static void AddTouchGrab(GameObject target)
        {
            if (target.GetComponentInChildren<TouchHandGrabInteractable>(true) != null)
                return;

            var assembly = typeof(QuickActionsAPI).Assembly;
            var wizardType = assembly.GetType("Oculus.Interaction.Editor.QuickActions.TouchGrabWizard");
            var wizardBase = assembly.GetType("Oculus.Interaction.Editor.QuickActions.QuickActionsWizard");
            if (wizardType == null || wizardBase == null)
            {
                Debug.LogWarning("[Haptics Tutorial] TouchGrabWizard was not found; palm grab is still enabled.");
                return;
            }

            var method = wizardBase.GetMethod(
                "CreateWithDefaults",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogWarning("[Haptics Tutorial] Could not add wrap-around grab interactors automatically.");
                return;
            }

            try
            {
                method.MakeGenericMethod(wizardType).Invoke(null, new object[] { target, false, null });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Haptics Tutorial] Wrap-around grab setup skipped: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}
