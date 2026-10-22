using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace HapticsTutorial
{
    /// <summary>
    /// XR Interaction Simulator starts in controller mode. This tutorial needs
    /// tracked hands in editor Play Mode, so switch as soon as the simulator is ready.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SimulatedHandsPlayMode : MonoBehaviour
    {
        void Start()
        {
#if UNITY_EDITOR
            StartCoroutine(SwitchToHandsWhenReady());
#endif
        }

#if UNITY_EDITOR
        IEnumerator SwitchToHandsWhenReady()
        {
            SimulatedDeviceLifecycleManager manager = null;
            for (var i = 0; i < 8 && manager == null; i++)
            {
                manager = SimulatedDeviceLifecycleManager.instance;
                if (manager == null)
                    manager = FindFirstObjectByType<SimulatedDeviceLifecycleManager>();
                if (manager == null)
                    yield return null;
            }

            if (manager == null)
            {
                Debug.LogWarning("[Haptics Tutorial] No XR Interaction Simulator in the scene, so Play Mode has no hands.");
                yield break;
            }

            if (manager.deviceMode == SimulatedDeviceLifecycleManager.DeviceMode.Hand)
                yield break;

            var method = typeof(SimulatedDeviceLifecycleManager).GetMethod(
                "SwitchDeviceMode",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                Debug.LogWarning("[Haptics Tutorial] Could not switch the simulator into hand mode.");
                yield break;
            }

            method.Invoke(manager, null);
        }
#endif
    }
}
