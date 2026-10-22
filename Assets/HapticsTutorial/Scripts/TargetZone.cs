using UnityEngine;

namespace HapticsTutorial
{
    public class TargetZone : MonoBehaviour
    {
        [SerializeField] Transform trackedObject;

        public bool ContainsTrackedObject { get; private set; }

        public void SetTrackedObject(Transform target)
        {
            trackedObject = target;
        }

        void OnTriggerEnter(Collider other)
        {
            if (IsTracked(other))
                ContainsTrackedObject = true;
        }

        void OnTriggerStay(Collider other)
        {
            if (IsTracked(other))
                ContainsTrackedObject = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (IsTracked(other))
                ContainsTrackedObject = false;
        }

        bool IsTracked(Collider other)
        {
            if (trackedObject == null || other == null)
                return false;
            return other.transform == trackedObject || other.transform.IsChildOf(trackedObject);
        }
    }
}
