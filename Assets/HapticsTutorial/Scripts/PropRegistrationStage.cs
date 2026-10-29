using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsTutorial
{
    /// <summary>
    /// Stage 2: register a virtual stand-in for a passive physical prop.
    /// Kim et al. 2023: the virtual mesh only needs to be roughly the same size/shape.
    /// On Quest 3/3S the ghost follows ArUco-tracked cube pose; dwell or stable tracking confirms.
    /// </summary>
    public class PropRegistrationStage : TutorialStage
    {
        [SerializeField] Transform virtualProp;
        [SerializeField] XRGrabInteractable propGrab;
        [SerializeField] HandGrabInteractable metaGrab;
        [SerializeField] Collider propCollider;
        [SerializeField] float dwellSeconds = 1.25f;
        [SerializeField] Vector3 defaultLocalOffset = new Vector3(0.18f, 0.78f, 0.52f);
        [SerializeField] Renderer propRenderer;
        [SerializeField] QuestCameraCubeLinker cameraLinker;
        [SerializeField] Color ghostColor = new Color(0.35f, 0.75f, 1f, 0.45f);
        [SerializeField] Color solidColor = new Color(0.2f, 0.55f, 0.85f, 1f);

        readonly List<IInteractableView> _metaViews = new List<IInteractableView>();
        float _dwell;
        bool _registered;
        Material _runtimeMat;

        public Vector3 RegisteredPosition => virtualProp != null ? virtualProp.position : Vector3.zero;

        public void Configure(Transform prop, XRGrabInteractable grab, Collider collider, Renderer renderer)
        {
            virtualProp = prop;
            propGrab = grab;
            propCollider = collider;
            propRenderer = renderer;
        }

        void Awake()
        {
            if (cameraLinker == null)
                cameraLinker = FindFirstObjectByType<QuestCameraCubeLinker>();
            if (cameraLinker == null)
                cameraLinker = new GameObject("Quest Camera Cube Linker").AddComponent<QuestCameraCubeLinker>();
        }

        public override void BeginStage()
        {
            base.BeginStage();
            _dwell = 0f;
            _registered = false;

            if (cameraLinker == null)
                cameraLinker = FindFirstObjectByType<QuestCameraCubeLinker>();
            cameraLinker?.SetVirtualProp(virtualProp);
            cameraLinker?.SetFollowEnabled(true);

            if (virtualProp != null && (cameraLinker == null || !cameraLinker.HasPoint))
                virtualProp.position = defaultLocalOffset;

            ApplyColor(ghostColor);
            ResolveMetaGrab();

            if (propGrab != null && metaGrab == null)
            {
                propGrab.enabled = true;
                propGrab.selectExited.AddListener(OnPropReleased);
            }

            if (metaGrab != null && virtualProp != null)
            {
                MetaGrabUtil.SetEnabled(virtualProp.gameObject, true);
                BindMetaViews(virtualProp.gameObject);
            }
        }

        public override void EndStage()
        {
            if (propGrab != null)
            {
                propGrab.selectExited.RemoveListener(OnPropReleased);
                propGrab.enabled = false;
            }

            UnbindMetaViews();
            cameraLinker?.SetFollowEnabled(false);
            if (virtualProp != null)
                MetaGrabUtil.SetEnabled(virtualProp.gameObject, false);

            var rb = virtualProp != null ? virtualProp.GetComponent<Rigidbody>() : null;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            ApplyColor(solidColor);
            base.EndStage();
        }

        void Update()
        {
            if (!IsActive || _registered)
                return;

            var tracking = cameraLinker != null && cameraLinker.IsTracking;
            if (IsHandOverProp() || tracking)
            {
                _dwell += Time.deltaTime;
                if (_dwell >= dwellSeconds)
                    RegisterAndComplete();
            }
            else
            {
                _dwell = Mathf.Max(0f, _dwell - Time.deltaTime);
            }
        }

        void OnPropReleased(SelectExitEventArgs args)
        {
            SessionLogger.Instance?.Log("prop_moved", StageId, virtualProp.position.ToString("F3"));
        }

        void ResolveMetaGrab()
        {
            if (metaGrab != null)
                return;

            var root = virtualProp != null ? virtualProp.gameObject : gameObject;
            metaGrab = MetaGrabUtil.Find(root);
        }

        void BindMetaViews(GameObject root)
        {
            UnbindMetaViews();
            foreach (var view in MetaGrabUtil.Views(root))
            {
                view.WhenSelectingInteractorViewRemoved += OnMetaPropReleased;
                _metaViews.Add(view);
            }
        }

        void UnbindMetaViews()
        {
            foreach (var view in _metaViews)
            {
                if (view == null)
                    continue;
                view.WhenSelectingInteractorViewRemoved -= OnMetaPropReleased;
            }

            _metaViews.Clear();
        }

        void OnMetaPropReleased(IInteractorView view)
        {
            SessionLogger.Instance?.Log("prop_moved", StageId, virtualProp.position.ToString("F3"));
        }

        void RegisterAndComplete()
        {
            _registered = true;
            SessionLogger.Instance?.Log("prop_registered", StageId, RegisteredPosition.ToString("F3"));
            CompleteStage();
        }

        bool IsHandOverProp()
        {
            if (propCollider == null)
                return false;

            var hits = Physics.OverlapBox(
                propCollider.bounds.center,
                propCollider.bounds.extents * 1.15f,
                virtualProp.rotation);

            foreach (var hit in hits)
            {
                if (hit == null || hit == propCollider)
                    continue;
                if (hit.transform == virtualProp || hit.transform.IsChildOf(virtualProp))
                    continue;
                if (LooksLikeHand(hit.transform))
                    return true;
            }

            return false;
        }

        static bool LooksLikeHand(Transform t)
        {
            var name = t.name.ToLowerInvariant();
            if (name.Contains("hand") || name.Contains("poke") || name.Contains("direct") || name.Contains("interactor"))
                return true;

            var parent = t.parent;
            while (parent != null)
            {
                var parentName = parent.name.ToLowerInvariant();
                if (parentName.Contains("hand") || parentName.Contains("interactor"))
                    return true;
                parent = parent.parent;
            }

            return false;
        }

        void ApplyColor(Color color)
        {
            if (propRenderer == null)
                return;

            if (_runtimeMat == null)
            {
                _runtimeMat = new Material(propRenderer.sharedMaterial);
                propRenderer.material = _runtimeMat;
            }

            if (_runtimeMat.HasProperty("_BaseColor"))
                _runtimeMat.SetColor("_BaseColor", color);
            _runtimeMat.color = color;
        }
    }
}
