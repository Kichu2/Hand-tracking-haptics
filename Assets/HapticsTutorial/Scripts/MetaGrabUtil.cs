using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace HapticsTutorial
{
    public static class MetaGrabUtil
    {
        public static HandGrabInteractable Find(GameObject root)
        {
            return root != null ? root.GetComponentInChildren<HandGrabInteractable>(true) : null;
        }

        public static IEnumerable<IInteractableView> Views(GameObject root)
        {
            if (root == null)
                yield break;

            foreach (var grab in root.GetComponentsInChildren<HandGrabInteractable>(true))
                yield return grab;
            foreach (var grab in root.GetComponentsInChildren<TouchHandGrabInteractable>(true))
                yield return grab;
            foreach (var grab in root.GetComponentsInChildren<GrabInteractable>(true))
                yield return grab;
        }

        public static void SetEnabled(GameObject root, bool enabled)
        {
            if (root == null)
                return;

            foreach (var grab in root.GetComponentsInChildren<HandGrabInteractable>(true))
                grab.enabled = enabled;
            foreach (var grab in root.GetComponentsInChildren<TouchHandGrabInteractable>(true))
                grab.enabled = enabled;
            foreach (var grab in root.GetComponentsInChildren<GrabInteractable>(true))
                grab.enabled = enabled;
        }
    }
}
