using UnityEngine;
using UnityEngine.InputSystem;

namespace HapticsTutorial
{
    /// <summary>
    /// Sequential onboarding: one stage at a time, short instruction, advance only on success.
    /// </summary>
    public class StageManager : MonoBehaviour
    {
        [SerializeField] TutorialStage[] stages;
        [SerializeField] InstructionPresenter presenter;
        [SerializeField] float advanceDelay = 1.4f;
        [SerializeField] bool allowEditorSkip = true;

        int _index = -1;
        bool _waitingToAdvance;

        public TutorialStage[] Stages => stages;
        public InstructionPresenter Presenter => presenter;

        public void Configure(TutorialStage[] tutorialStages, InstructionPresenter instructionPresenter)
        {
            stages = tutorialStages;
            presenter = instructionPresenter;
        }

        void Start()
        {
            if (stages == null || stages.Length == 0)
            {
                Debug.LogError("[Haptics Tutorial] StageManager has no stages.");
                return;
            }

            foreach (var stage in stages)
            {
                if (stage == null)
                    continue;
                stage.Completed += OnStageCompleted;
                stage.gameObject.SetActive(false);
            }

            AdvanceTo(0);
        }

        void Update()
        {
            if (!allowEditorSkip || !Application.isEditor)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tKey.wasPressedThisFrame && _index >= 0 && _index < stages.Length)
            {
                SessionLogger.Instance?.Log("debug_skip", stages[_index].StageId);
                stages[_index].DebugForceComplete();
            }
        }

        void OnDestroy()
        {
            if (stages == null)
                return;
            foreach (var stage in stages)
            {
                if (stage != null)
                    stage.Completed -= OnStageCompleted;
            }
        }

        void OnStageCompleted(TutorialStage stage)
        {
            if (_waitingToAdvance)
                return;

            presenter?.Show("Done", $"{stage.Title} complete.", new Color(0.15f, 0.45f, 0.22f, 0.92f));
            _waitingToAdvance = true;
            Invoke(nameof(GoNext), advanceDelay);
        }

        void GoNext()
        {
            _waitingToAdvance = false;
            if (_index >= 0 && _index < stages.Length)
                stages[_index].EndStage();

            var next = _index + 1;
            if (next >= stages.Length)
            {
                presenter?.Show("Tutorial complete", "You can remove the headset. Logs are saved on device.", new Color(0.12f, 0.28f, 0.45f, 0.92f));
                SessionLogger.Instance?.Log("tutorial_complete", "", $"drops={SessionLogger.Instance.DropCount}");
                return;
            }

            AdvanceTo(next);
        }

        void AdvanceTo(int index)
        {
            _index = index;
            var stage = stages[_index];
            presenter?.Show(stage.Title, stage.Instruction, new Color(0.08f, 0.08f, 0.1f, 0.92f));
            stage.BeginStage();
        }
    }
}
