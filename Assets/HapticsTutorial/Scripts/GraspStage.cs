using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace HapticsTutorial
{
    /// <summary>
    /// Stage 1: pick up a grab interactable with a tracked (or simulated) hand and place it in the target zone.
    /// </summary>
    public class GraspStage : TutorialStage
    {
        [SerializeField] XRGrabInteractable grabInteractable;
        [SerializeField] HandGrabInteractable metaGrab;
        [SerializeField] TargetZone targetZone;
        [SerializeField] float settleSeconds = 0.35f;

        readonly List<IInteractableView> _metaViews = new List<IInteractableView>();
        const float PhysicalPickupDistance = 0.04f;

        bool _wasGrabbed;
        float _insideTimer;
        QuestCameraCubeLinker _linker;
        Vector3 _physicalStart;
        bool _hasPhysicalStart;

        Vector3? _zonePosition;

        public void Configure(XRGrabInteractable grab, TargetZone zone)
        {
            grabInteractable = grab;
            targetZone = zone;
        }

        /// <summary>Another round with the same cube and zone; the zone moves to <paramref name="zonePosition"/>.</summary>
        public GraspStage CreateRound(string id, string title, string instruction, Vector3 zonePosition)
        {
            var round = new GameObject(title).AddComponent<GraspStage>();
            round.Configure(grabInteractable, targetZone);
            round.metaGrab = metaGrab;
            round.ConfigureIdentity(id, title, instruction);
            round._zonePosition = zonePosition;
            return round;
        }

        public override void BeginStage()
        {
            base.BeginStage();
            _wasGrabbed = false;
            _insideTimer = 0f;
            _hasPhysicalStart = false;
            ResolveMetaGrab();
            if (_zonePosition.HasValue && targetZone != null)
                targetZone.MoveTo(_zonePosition.Value);
            if (grabInteractable != null && metaGrab == null)
                grabInteractable.enabled = true; // the previous round's EndStage turned it off

            var tracked = grabInteractable != null
                ? grabInteractable.transform
                : metaGrab != null ? metaGrab.transform.parent : null;
            if (targetZone != null && tracked != null)
                targetZone.SetTrackedObject(tracked);

            if (grabInteractable != null && metaGrab == null)
            {
                grabInteractable.selectEntered.AddListener(OnSelectEntered);
                grabInteractable.selectExited.AddListener(OnSelectExited);
            }

            var root = grabInteractable != null ? grabInteractable.gameObject : null;
            if (root != null && metaGrab != null)
            {
                MetaGrabUtil.SetEnabled(root, true);
                BindMetaViews(root);
            }
        }

        public override void EndStage()
        {
            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
                grabInteractable.enabled = false;
            }

            UnbindMetaViews();
            base.EndStage();
        }

        void Update()
        {
            if (!IsActive || targetZone == null)
                return;

            var tracked = grabInteractable != null
                ? grabInteractable.transform
                : metaGrab != null ? metaGrab.transform.parent : null;
            if (tracked == null)
                return;

            DetectPhysicalPickup();

            if (_wasGrabbed && targetZone.ContainsTrackedObject)
            {
                _insideTimer += Time.deltaTime;
                if (_insideTimer >= settleSeconds)
                    CompleteStage();
            }
            else
            {
                _insideTimer = 0f;
            }
        }

        // With a real marked cube driving the virtual one, the hand grabs the real cube, so no
        // virtual grab event fires. Moving the real cube counts as picking it up instead.
        void DetectPhysicalPickup()
        {
            if (_wasGrabbed)
                return;
            if (_linker == null)
                _linker = FindFirstObjectByType<QuestCameraCubeLinker>();
            if (_linker == null || !_linker.GraspCubeVisible)
                return;

            var position = _linker.GraspCubePose.position;
            if (!_hasPhysicalStart)
            {
                _physicalStart = position;
                _hasPhysicalStart = true;
                return;
            }

            if (Vector3.Distance(position, _physicalStart) < PhysicalPickupDistance)
                return;
            _wasGrabbed = true;
            SessionLogger.Instance?.Log("grab", StageId, "physical cube");
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            _wasGrabbed = true;
            SessionLogger.Instance?.Log("grab", StageId, args.interactorObject?.transform.name);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (targetZone == null || !targetZone.ContainsTrackedObject)
                SessionLogger.Instance?.RecordDrop(StageId);
        }

        void ResolveMetaGrab()
        {
            if (metaGrab != null)
                return;

            var root = grabInteractable != null ? grabInteractable.gameObject : gameObject;
            metaGrab = MetaGrabUtil.Find(root);
        }

        void BindMetaViews(GameObject root)
        {
            UnbindMetaViews();
            foreach (var view in MetaGrabUtil.Views(root))
            {
                view.WhenSelectingInteractorViewAdded += OnMetaGrabbed;
                view.WhenSelectingInteractorViewRemoved += OnMetaReleased;
                _metaViews.Add(view);
            }
        }

        void UnbindMetaViews()
        {
            foreach (var view in _metaViews)
            {
                if (view == null)
                    continue;
                view.WhenSelectingInteractorViewAdded -= OnMetaGrabbed;
                view.WhenSelectingInteractorViewRemoved -= OnMetaReleased;
            }

            _metaViews.Clear();
        }

        void OnMetaGrabbed(IInteractorView view)
        {
            _wasGrabbed = true;
            SessionLogger.Instance?.Log("grab", StageId, view?.Data?.ToString() ?? "hand");
        }

        void OnMetaReleased(IInteractorView view)
        {
            if (targetZone == null || !targetZone.ContainsTrackedObject)
                SessionLogger.Instance?.RecordDrop(StageId);
        }
    }
}
