using System.Collections.Generic;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// Builds the full demo sequence (about 3–4 minutes) around the scene's two core stages:
    /// welcome slides, three grasp rounds with the target moving, a turn-the-cube stage,
    /// prop alignment, and a wrap-up with the session's results. Runs once after the scene
    /// loads, before StageManager starts, so the scene itself needs no changes.
    /// </summary>
    static class TutorialSequence
    {
        static float _startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Extend()
        {
            var manager = Object.FindFirstObjectByType<StageManager>();
            if (manager == null || manager.Stages == null)
                return;

            GraspStage grasp = null;
            PropRegistrationStage prop = null;
            foreach (var stage in manager.Stages)
            {
                if (stage is InfoStage)
                    return; // already extended
                grasp ??= stage as GraspStage;
                prop ??= stage as PropRegistrationStage;
            }

            if (grasp == null || prop == null)
                return;

            var presenter = manager.Presenter;
            var props = TutorialMode.WithProps;
            var zone = Object.FindFirstObjectByType<TargetZone>();
            var zoneStart = zone != null ? zone.transform.position : Vector3.zero;
            var cube = GameObject.Find("Grasp Cube")?.transform;

            var welcome = NewStage<InfoStage>("Welcome");
            welcome.ConfigureIdentity("welcome", "Welcome", "");
            welcome.ConfigureSlides(presenter,
                new InfoStage.Slide("Welcome", () =>
                {
                    _startedAt = Time.time;
                    return props
                        ? "This short tutorial shows how to use your bare hands in VR, and how a real object can make a virtual one feel solid."
                        : "This short tutorial shows how to use your bare hands in VR to pick up and move virtual objects.";
                }, 11f),
                new InfoStage.Slide("Hand tracking",
                    "No controllers needed. The headset's cameras follow your fingers. Hold your hands up in front of you and watch them move.", 11f),
                props
                    ? new InfoStage.Slide("Passive haptics",
                        "When a real object sits exactly where a virtual one appears, touching it feels real. The headset camera finds the printed markers on the cube to line the two up.", 12f)
                    : new InfoStage.Slide("Virtual objects",
                        "Everything you touch here is virtual: there is nothing real to hold, so your hand can pass through. Watch your fingers to judge when you are touching a cube.", 12f),
                new InfoStage.Slide("What's next",
                    "1. Move the cube to targets\n2. Turn it to show a face\n3. Align the larger prop", 10f));

            grasp.ConfigureIdentity(grasp.StageId, "1. Move the cube (1/3)", grasp.Instruction);
            var round2 = grasp.CreateRound("grasp_2", "1. Move the cube (2/3)",
                "The target moved. Carry the orange cube to the new green spot.", zoneStart + new Vector3(-0.24f, 0f, 0.2f));
            var round3 = grasp.CreateRound("grasp_3", "1. Move the cube (3/3)",
                "One more: bring the cube back to where it started.", zoneStart + new Vector3(-0.44f, 0f, 0f));

            var turn = NewStage<FaceTurnStage>("Turn The Cube");
            turn.ConfigureIdentity("face_turn", "2. Turn the cube",
                "Turn the cube so the yellow face points straight at you. Hold it there for a moment.");
            turn.Configure(cube, presenter, 5);

            if (props)
                prop.ConfigureIdentity(prop.StageId, "3. Align the physical prop", prop.Instruction);
            else
                prop.ConfigureIdentity(prop.StageId, "3. Touch the blue cube",
                    "Reach out and rest your hand on the blue cube. Hold it there until it turns solid.");

            var wrapUp = NewStage<InfoStage>("Wrap Up");
            wrapUp.ConfigureIdentity("wrap_up", "Nice work", "");
            wrapUp.ConfigureSlides(presenter,
                new InfoStage.Slide("Nice work", () =>
                {
                    var minutes = (Time.time - _startedAt) / 60f;
                    var drops = SessionLogger.Instance != null ? SessionLogger.Instance.DropCount : 0;
                    return $"You finished every step in {minutes:F1} minutes with {drops} drop{(drops == 1 ? "" : "s")}.";
                }, 11f),
                props
                    ? new InfoStage.Slide("Why it matters",
                        "Hand tracking removes the controller; the tracked cube gives your hand something real to hold. Together, virtual objects feel like they are there.", 12f)
                    : new InfoStage.Slide("Something to compare",
                        "Hand tracking removes the controller, but with nothing real to hold your hand passes through the cubes. Try the version with real cubes and compare how it feels.", 12f),
                new InfoStage.Slide("Thanks for trying it", "That's the end of the tutorial.", 10f));

            var sequence = new List<TutorialStage> { welcome, grasp, round2, round3, turn, prop, wrapUp };
            manager.Configure(sequence.ToArray(), presenter);
            Debug.Log($"[Haptics Tutorial] Demo sequence: {sequence.Count} stages.");
        }

        static T NewStage<T>(string name) where T : TutorialStage => new GameObject(name).AddComponent<T>();
    }
}
