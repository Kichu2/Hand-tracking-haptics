using System;
using UnityEngine;

namespace HapticsTutorial
{
    /// <summary>
    /// A stage of timed slides on the instruction panel (welcome, explanations, wrap-up).
    /// Completes after the last slide. Slide text can be produced when the slide shows,
    /// so a wrap-up can include the session's results.
    /// </summary>
    public class InfoStage : TutorialStage
    {
        public readonly struct Slide
        {
            public readonly string Title;
            public readonly Func<string> Body;
            public readonly float Seconds;

            public Slide(string title, string body, float seconds) : this(title, () => body, seconds) { }

            public Slide(string title, Func<string> body, float seconds)
            {
                Title = title;
                Body = body;
                Seconds = seconds;
            }
        }

        static readonly Color SlideTint = new Color(0.1f, 0.14f, 0.24f, 0.92f);

        Slide[] _slides = Array.Empty<Slide>();
        InstructionPresenter _presenter;
        int _slide;
        float _slideEnds;

        public void ConfigureSlides(InstructionPresenter presenter, params Slide[] slides)
        {
            _presenter = presenter;
            _slides = slides;
            if (slides.Length > 0)
                ConfigureIdentity(StageId, slides[0].Title, slides[0].Body());
        }

        public override void BeginStage()
        {
            base.BeginStage();
            ShowSlide(0);
        }

        void Update()
        {
            if (!IsActive || Time.time < _slideEnds)
                return;
            if (_slide + 1 < _slides.Length)
                ShowSlide(_slide + 1);
            else
                CompleteStage();
        }

        void ShowSlide(int index)
        {
            _slide = index;
            if (index >= _slides.Length)
                return;
            var slide = _slides[index];
            _slideEnds = Time.time + slide.Seconds;
            _presenter?.Show(slide.Title, slide.Body(), SlideTint);
        }
    }
}
