using System;
using UnityEngine;

namespace HapticsTutorial
{
    public abstract class TutorialStage : MonoBehaviour
    {
        [SerializeField] string stageId = "stage";
        [SerializeField] string title = "Stage";
        [TextArea(2, 4)]
        [SerializeField] string instruction = "Follow the prompt.";

        public string StageId => stageId;
        public string Title => title;
        public string Instruction => instruction;

        public void ConfigureIdentity(string id, string stageTitle, string stageInstruction)
        {
            stageId = id;
            title = stageTitle;
            instruction = stageInstruction;
        }

        public bool IsActive { get; private set; }
        public bool IsComplete { get; private set; }

        public event Action<TutorialStage> Completed;

        float _startedAt;

        public virtual void BeginStage()
        {
            IsActive = true;
            IsComplete = false;
            _startedAt = Time.time;
            gameObject.SetActive(true);
            SessionLogger.Instance?.RecordStageStart(stageId);
        }

        public virtual void EndStage()
        {
            IsActive = false;
        }

        public void DebugForceComplete()
        {
            CompleteStage();
        }

        protected void CompleteStage()
        {
            if (!IsActive || IsComplete)
                return;

            IsComplete = true;
            IsActive = false;
            var duration = Time.time - _startedAt;
            SessionLogger.Instance?.RecordStageComplete(stageId, duration);
            Completed?.Invoke(this);
        }

        public float ElapsedSeconds => IsActive || IsComplete ? Time.time - _startedAt : 0f;
    }
}
