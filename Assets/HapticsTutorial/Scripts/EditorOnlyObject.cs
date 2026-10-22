using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Keeps the XR Device / Interaction Simulator in editor Play Mode, off on the headset.
    /// </summary>
    public class EditorOnlyObject : MonoBehaviour
    {
        void Awake()
        {
#if !UNITY_EDITOR
            gameObject.SetActive(false);
#endif
        }
    }
}
