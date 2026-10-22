using UnityEngine;
using UnityEngine.XR.Hands;

namespace HapticsTutorial
{
    /// <summary>
    /// Turns on the already-rigged Quest hand meshes (wrist + finger bones mapped to
    /// <see cref="XRHandSkeletonDriver"/>) so OpenXR hand tracking can drive them.
    /// The Hands Interaction Demo leaves those meshes off for the sample visualizer.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class QuestHandRig : MonoBehaviour
    {
        void Awake()
        {
            DisableSampleVisualizer();
            ActivateQuestHands();
        }

        static void DisableSampleVisualizer()
        {
            var visualizers = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < visualizers.Length; i++)
            {
                if (visualizers[i].name == "Hand Visualizer")
                    visualizers[i].gameObject.SetActive(false);
            }
        }

        static void ActivateQuestHands()
        {
            var drivers = FindObjectsByType<XRHandSkeletonDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < drivers.Length; i++)
            {
                var go = drivers[i].gameObject;
                if (IsAndroidXrVisual(go))
                    continue;

                go.SetActive(true);

                var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (var r = 0; r < renderers.Length; r++)
                    renderers[r].gameObject.SetActive(true);
            }
        }

        static bool IsAndroidXrVisual(GameObject go)
        {
            var t = go.transform;
            while (t != null)
            {
                if (t.name.IndexOf("Android XR", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                t = t.parent;
            }

            return false;
        }
    }
}
