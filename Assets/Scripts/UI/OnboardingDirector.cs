using System;
using System.Collections;
using System.Collections.Generic;
using LivingDiorama.Audio;
using LivingDiorama.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The first five minutes.
    ///
    /// Every step is a sentence from the narrator plus, where there is something to
    /// press, a ring drawn around it. Nothing is explained that the player can work out
    /// by looking, and the steps that matter wait for the player to actually do the thing
    /// rather than for them to tap "Next" past it -- the point of the tour is the first
    /// box opening and the first interaction, and both have to be witnessed to land.
    /// </summary>
    public sealed class OnboardingDirector : MonoBehaviour
    {
        const string CompletedKey = "onboarding.completed";

        public static bool Completed
        {
            get => PlayerPrefs.GetInt(CompletedKey, 0) == 1;
            set => PlayerPrefs.SetInt(CompletedKey, value ? 1 : 0);
        }

        enum Gate
        {
            /// <summary>Advance when the player presses Next.</summary>
            Manual,

            /// <summary>Advance when the player opens a box.</summary>
            BoxOpened,

            /// <summary>Advance when the reveal card is dismissed.</summary>
            RevealDismissed,

            /// <summary>Advance on the first notable thing two creatures do to each other.</summary>
            Interaction,

            /// <summary>Advance after a fixed watch, with no button at all.</summary>
            Timer,
        }

        readonly struct Step
        {
            public readonly string VoiceKey;
            public readonly string Title;
            public readonly string HighlightElement;
            public readonly Gate Gate;
            public readonly float Seconds;

            public Step(string voiceKey, string title, Gate gate,
                        string highlight = null, float seconds = 0f)
            {
                VoiceKey = voiceKey;
                Title = title;
                Gate = gate;
                HighlightElement = highlight;
                Seconds = seconds;
            }
        }

        static readonly Step[] Steps =
        {
            new("tutor_box", "FIRST BOX", Gate.BoxOpened, highlight: "btn-box"),
            new("tutor_place", "SETTLING IN", Gate.RevealDismissed),
            new("tutor_watch", "LEAVE THEM BE", Gate.Timer, seconds: 9f),
            new("tutor_interact", "A MEETING", Gate.Interaction, seconds: 45f),
            new("tutor_economy", "WHAT IT IS WORTH", Gate.Manual),
            new("tutor_expand", "MORE GROUND", Gate.Manual, highlight: "btn-expand"),
            new("tutor_done", "OVER TO YOU", Gate.Manual),
        };

        VisualElement _root, _layer, _card, _spotlight;
        Label _title, _body;
        Button _next, _skip;
        AudioDirector _audio;

        int _index = -1;
        bool _running;
        bool _gateSatisfied;

        public event Action Finished;

        public bool IsRunning => _running;

        public void Initialise(VisualElement root, AudioDirector audio)
        {
            _root = root;
            _audio = audio;

            _layer = root.Q<VisualElement>("coach-layer");
            _card = root.Q<VisualElement>("coach-card");
            _title = root.Q<Label>("coach-title");
            _body = root.Q<Label>("coach-body");
            _next = root.Q<Button>("btn-coach-next");
            _skip = root.Q<Button>("btn-coach-skip");

            _spotlight = new VisualElement { pickingMode = PickingMode.Ignore };
            _spotlight.AddToClassList("coach__spotlight");
            _spotlight.style.display = DisplayStyle.None;
            _layer.Add(_spotlight);

            _next.clicked += () =>
            {
                _audio?.PlaySfx("ui_tap");
                _gateSatisfied = true;
            };
            _skip.clicked += () =>
            {
                _audio?.PlaySfx("ui_back");
                Skip();
            };
        }

        // ---- external signals ------------------------------------------------

        public void NotifyBoxOpened() => Satisfy(Gate.BoxOpened);
        public void NotifyRevealDismissed() => Satisfy(Gate.RevealDismissed);

        public void NotifyInteraction(SimEvent e)
        {
            // Only creature-to-creature moments count; a lone creature eating is not the
            // beat this step is promising.
            if (e.Target != null && e.Actor != null) Satisfy(Gate.Interaction);
        }

        void Satisfy(Gate gate)
        {
            if (!_running || _index < 0 || _index >= Steps.Length) return;
            if (Steps[_index].Gate == gate) _gateSatisfied = true;
        }

        // ---- flow ------------------------------------------------------------

        public void Begin()
        {
            if (_running || Completed) return;
            StartCoroutine(Run());
        }

        public void Skip()
        {
            if (!_running) return;
            StopAllCoroutines();
            _audio?.StopNarration();
            Complete();
        }

        IEnumerator Run()
        {
            _running = true;
            _layer.RemoveFromClassList("hidden");

            // A breath after the title screen before the first instruction lands.
            yield return new WaitForSecondsRealtime(1.2f);

            for (_index = 0; _index < Steps.Length; _index++)
            {
                Step step = Steps[_index];
                yield return ShowStep(step);
            }

            Complete();
        }

        IEnumerator ShowStep(Step step)
        {
            _gateSatisfied = false;

            _title.text = step.Title;
            _body.text = TitleScreen.SubtitleFor(step.VoiceKey);

            // Gates that wait on the world get no Next button: the step is a promise that
            // something will happen, and offering a way to skip past it undercuts that.
            bool manual = step.Gate is Gate.Manual;
            _next.style.display = manual ? DisplayStyle.Flex : DisplayStyle.None;

            _card.RemoveFromClassList("coach__card--in");
            yield return null;
            _card.AddToClassList("coach__card--in");

            _audio?.Narrate(step.VoiceKey, SettingsPanel.SubtitlesEnabled ? null : _body.text);

            Highlight(step.HighlightElement);

            float elapsed = 0f;
            float limit = step.Seconds > 0f ? step.Seconds : float.MaxValue;

            while (!_gateSatisfied)
            {
                elapsed += Time.unscaledDeltaTime;

                // Timer steps end on their own; the others use the timer only as a safety
                // net so a player who never triggers the event is not stuck forever.
                if (step.Gate == Gate.Timer && elapsed >= step.Seconds) break;
                if (step.Gate != Gate.Manual && step.Gate != Gate.Timer && elapsed >= limit) break;

                if (_spotlight.style.display == DisplayStyle.Flex) PulseSpotlight(elapsed);
                yield return null;
            }

            _card.RemoveFromClassList("coach__card--in");
            ClearHighlight();
            yield return new WaitForSecondsRealtime(0.4f);
        }

        void Highlight(string elementName)
        {
            if (string.IsNullOrEmpty(elementName))
            {
                ClearHighlight();
                return;
            }

            VisualElement target = _root.Q<VisualElement>(elementName);
            if (target == null)
            {
                ClearHighlight();
                return;
            }

            // Schedule rather than read immediately: layout may not have run yet on the
            // frame a step begins.
            target.schedule.Execute(() =>
            {
                Rect bounds = target.worldBound;
                if (bounds.width <= 0f) return;

                const float pad = 8f;
                _spotlight.style.display = DisplayStyle.Flex;
                _spotlight.style.left = bounds.x - pad;
                _spotlight.style.top = bounds.y - pad;
                _spotlight.style.width = bounds.width + pad * 2f;
                _spotlight.style.height = bounds.height + pad * 2f;
            }).ExecuteLater(50);
        }

        void PulseSpotlight(float elapsed)
        {
            float pulse = 0.55f + Mathf.Sin(elapsed * 4.2f) * 0.45f;
            _spotlight.style.opacity = pulse;
        }

        void ClearHighlight() => _spotlight.style.display = DisplayStyle.None;

        void Complete()
        {
            _running = false;
            _index = -1;
            Completed = true;

            ClearHighlight();
            _card.RemoveFromClassList("coach__card--in");
            _layer.AddToClassList("hidden");

            Finished?.Invoke();
        }
    }
}
