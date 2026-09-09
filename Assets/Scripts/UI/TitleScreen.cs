using System;
using System.Collections;
using LivingDiorama.Audio;
using LivingDiorama.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The opening. A black card with the emblem, then the diorama itself fades up behind
    /// the title while the narrator introduces it.
    ///
    /// The title screen deliberately has no art of its own: what sits behind the buttons
    /// is the player's actual diorama, already running, on a slow presentation orbit. It
    /// is both the most honest advert for the game and the reason "Begin" feels like
    /// stepping into a room rather than loading a level.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        VisualElement _splash, _title, _credits, _subtitle;
        VisualElement _splashEmblem, _titleEmblem;
        AudioDirector _audio;

        Action _onBegin;
        Action _onSettings;
        bool _introPlaying;
        bool _dismissed;

        public bool IsVisible => !_dismissed;

        public void Initialise(VisualElement root, AudioDirector audio, Action onBegin, Action onSettings)
        {
            _audio = audio;
            _onBegin = onBegin;
            _onSettings = onSettings;

            _splash = root.Q<VisualElement>("splash");
            _title = root.Q<VisualElement>("title-screen");
            _credits = root.Q<VisualElement>("title-credits");

            _splashEmblem = root.Q<VisualElement>("splash-emblem");
            _titleEmblem = root.Q<VisualElement>("title-emblem");

            // Prefer the painted emblem; fall back to the procedural one so the title
            // screen is never blank on a checkout where the art has not been generated.
            Sprite painted = UiSkin.Sprite("title_emblem");
            StyleBackground emblem = painted != null
                ? new StyleBackground(painted)
                : new StyleBackground(Background.FromTexture2D(LogoTexture.Emblem()));

            _splashEmblem.style.backgroundImage = emblem;
            _titleEmblem.style.backgroundImage = emblem;

            root.Q<VisualElement>("title-vignette").style.backgroundImage =
                new StyleBackground(Background.FromTexture2D(ScrimTexture.Scrim()));

            _subtitle = root.Q<VisualElement>("subtitle-band");
            _subtitle?.AddToClassList("subtitle--raised");

            root.Q<Button>("btn-title-begin").clicked += Begin;
            root.Q<Button>("btn-title-settings").clicked += () =>
            {
                _audio?.PlaySfx("ui_tap");
                _onSettings?.Invoke();
            };
            root.Q<Button>("btn-title-credits").clicked += () =>
            {
                _audio?.PlaySfx("ui_tap");
                _credits.RemoveFromClassList("hidden");
            };
            root.Q<Button>("btn-credits-close").clicked += () =>
            {
                _audio?.PlaySfx("ui_back");
                _credits.AddToClassList("hidden");
            };

            // Start on the title but with the splash covering it, so the first frame is
            // black no matter how long the world takes to build.
            _title.style.opacity = 0f;
        }

        /// <summary>Run the splash, then reveal the title over the live diorama.</summary>
        public IEnumerator PlayOpening()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            _splash.AddToClassList("splash--lit");

            _audio?.PlayMusic("amb_menu", 3f);

            // Hold the mark long enough to register, not long enough to annoy.
            yield return new WaitForSecondsRealtime(1.8f);

            _splash.AddToClassList("splash--gone");
            _title.style.opacity = 1f;

            yield return new WaitForSecondsRealtime(0.9f);
            _splash.AddToClassList("hidden");

            StartCoroutine(PlayIntroNarration());
        }

        IEnumerator PlayIntroNarration()
        {
            _introPlaying = true;

            // Three lines with real gaps between them. The pauses are what make it read as
            // someone talking to you rather than a voice asset being played.
            foreach (string key in new[] { "intro_01", "intro_02", "intro_03" })
            {
                if (_dismissed) break;

                bool finished = false;
                _audio?.Narrate(key, SubtitleFor(key), () => finished = true);

                float guard = 0f;
                while (!finished && guard < 12f && !_dismissed)
                {
                    guard += Time.unscaledDeltaTime;
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(0.55f);
            }

            _introPlaying = false;
        }

        /// <summary>Subtitles mirror the recorded lines. Kept beside the audio keys so the
        /// two never drift apart.</summary>
        public static string SubtitleFor(string key) => key switch
        {
            "intro_01" => "Somewhere on a quiet shelf, in a box no bigger than your two hands, "
                          + "a small world is waking up.",
            "intro_02" => "You will not command it. You will not steer it. You will open the box, "
                          + "set someone down, and see what they decide to do.",
            "intro_03" => "Welcome to your living diorama.",

            "tutor_box" => "Every box holds someone. Go on. Open it.",
            "tutor_place" => "There. Set them down, and let them be. From here, everything they do "
                             + "is their own idea.",
            "tutor_watch" => "Watch a moment. The hunger is theirs. So is the curiosity, and the fear.",
            "tutor_interact" => "Ah. They have noticed one another. This is the part worth staying for.",
            "tutor_economy" => "Every moment you witness is worth something. A livelier diorama is a "
                               + "richer one.",
            "tutor_expand" => "The world can be larger. New ground brings new neighbours, and new trouble.",
            "tutor_done" => "That is enough from me. The rest belongs to them. Come back often. "
                            + "A great deal happens while you are away.",

            "milestone_first_night" => "The light is going. Some of them have been waiting all day for this.",
            "milestone_theft" => "Did you see that? Somebody has just helped themselves.",
            "milestone_chase" => "And now the reckoning.",
            "milestone_legendary" => "Oh. Now that is rare.",
            "milestone_expand" => "More ground. More room to get into things.",
            "welcome_back" => "You were missed. Mostly by the ones who wanted feeding.",
            _ => "",
        };

        void Begin()
        {
            if (_dismissed) return;
            _dismissed = true;

            _audio?.PlaySfx("ui_tap");
            if (_introPlaying) _audio?.StopNarration();

            StartCoroutine(Dismiss());
        }

        IEnumerator Dismiss()
        {
            _subtitle?.RemoveFromClassList("subtitle--raised");
            _title.AddToClassList("title--gone");
            yield return new WaitForSecondsRealtime(0.75f);

            _title.AddToClassList("hidden");
            _onBegin?.Invoke();
        }
    }
}
