using System;
using System.Collections;
using System.Collections.Generic;
using LivingDiorama.Audio;
using LivingDiorama.Core;
using LivingDiorama.Meta;
using LivingDiorama.Presentation;
using LivingDiorama.Simulation;
using LivingDiorama.Unboxing;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The switchboard. Binds the HUD, routes buttons to the game controller, owns which
    /// screen is up, and sequences the opening. Individual screens live in their own
    /// classes; nothing here knows any game rules.
    /// </summary>
    public sealed class GameUI : MonoBehaviour
    {
        const int MaxToasts = 4;
        const float ToastLifetime = 4.5f;

        VisualElement _root;

        GameController _game;
        EconomyService _economy;
        EcosystemSimulation _sim;
        DioramaCamera _camera;
        AudioDirector _audio;
        UnboxingDirector _unboxing;

        // HUD
        Label _coinsValue, _essenceValue, _keysValue;
        Label _levelLabel, _levelXp, _clockLabel, _rateLabel, _populationLabel;
        VisualElement _levelFill, _hud;
        Button _turntableButton;

        VisualElement _toasts, _floaters, _modalLayer, _flash;
        VisualElement _subtitleBand;
        Label _subtitleText;

        VisualElement _revealLayer, _revealCard;
        Label _revealRarity, _revealName, _revealFlavour, _revealBadge;

        BoxPanel _boxPanel;
        CollectionPanel _collectionPanel;
        ExpandPanel _expandPanel;
        InspectorPanel _inspector;
        WelcomePanel _welcome;
        SettingsPanel _settings;
        TitleScreen _title;
        OnboardingDirector _onboarding;

        readonly List<VisualElement> _openModals = new(2);
        Coroutine _subtitleRoutine;
        float _hudRefreshTimer;
        bool _gameplayStarted;

        public OnboardingDirector Onboarding => _onboarding;

        public void Initialise(UIDocument document, GameController game, EconomyService economy,
                               EcosystemSimulation sim, DioramaCamera camera, AudioDirector audio,
                               UnboxingDirector unboxing, Action onEraseSave)
        {
            _game = game;
            _economy = economy;
            _sim = sim;
            _camera = camera;
            _audio = audio;
            _unboxing = unboxing;

            _root = document.rootVisualElement;

            QueryHud();
            QueryOverlays();
            BuildPanels(onEraseSave);
            WireButtons();
            ApplySkin();
            Subscribe();

            RefreshAll();
            SetHudVisible(false);
        }

        void QueryHud()
        {
            _hud = _root.Q<VisualElement>("hud");
            _coinsValue = _root.Q<Label>("coins-value");
            _essenceValue = _root.Q<Label>("essence-value");
            _keysValue = _root.Q<Label>("keys-value");
            _levelLabel = _root.Q<Label>("level-label");
            _levelXp = _root.Q<Label>("level-xp");
            _levelFill = _root.Q<VisualElement>("level-fill");
            _clockLabel = _root.Q<Label>("clock-label");
            _rateLabel = _root.Q<Label>("rate-label");
            _populationLabel = _root.Q<Label>("population-label");
            _turntableButton = _root.Q<Button>("btn-turntable");
        }

        void QueryOverlays()
        {
            _modalLayer = _root.Q<VisualElement>("modal-layer");
            _toasts = _root.Q<VisualElement>("toasts");
            _floaters = _root.Q<VisualElement>("floaters");
            _flash = _root.Q<VisualElement>("flash");

            _subtitleBand = _root.Q<VisualElement>("subtitle-band");
            _subtitleText = _root.Q<Label>("subtitle-text");

            _revealLayer = _root.Q<VisualElement>("reveal-layer");
            _revealCard = _root.Q<VisualElement>("reveal-card");
            _revealRarity = _root.Q<Label>("reveal-rarity");
            _revealName = _root.Q<Label>("reveal-name");
            _revealFlavour = _root.Q<Label>("reveal-flavour");
            _revealBadge = _root.Q<Label>("reveal-badge");
        }

        void BuildPanels(Action onEraseSave)
        {
            _boxPanel = new BoxPanel(_root.Q<VisualElement>("modal-box"), _game, this);
            _collectionPanel = new CollectionPanel(_root.Q<VisualElement>("modal-collection"), _game, this);
            _expandPanel = new ExpandPanel(_root.Q<VisualElement>("modal-expand"), _game, this);
            _welcome = new WelcomePanel(_root.Q<VisualElement>("modal-welcome"), _game, this);
            _inspector = new InspectorPanel(_root.Q<VisualElement>("inspector"), _game);

            _settings = new SettingsPanel(_root.Q<VisualElement>("modal-settings"), _audio);
            _settings.Bind(_camera, CloseModals, onEraseSave);

            _title = gameObject.AddComponent<TitleScreen>();
            _title.Initialise(_root, _audio, StartGameplay, OpenSettings);

            _onboarding = gameObject.AddComponent<OnboardingDirector>();
            _onboarding.Initialise(_root, _audio);
        }

        void WireButtons()
        {
            _root.Q<Button>("btn-box").clicked += () => { Click(); OpenModal(_boxPanel.Root, _boxPanel.Refresh); };
            _root.Q<Button>("btn-collection").clicked += () => { Click(); OpenModal(_collectionPanel.Root, _collectionPanel.Refresh); };
            _root.Q<Button>("btn-expand").clicked += () => { Click(); OpenModal(_expandPanel.Root, _expandPanel.Refresh); };
            _root.Q<Button>("btn-settings").clicked += () => { Click(); OpenSettings(); };

            _turntableButton.clicked += () =>
            {
                Click();
                _camera.Turntable = !_camera.Turntable;
                _turntableButton.EnableInClassList("button--primary", _camera.Turntable);
                _turntableButton.EnableInClassList("button--ghost", !_camera.Turntable);
            };

            _root.Q<Button>("btn-reveal-done").clicked += () => DismissReveal(openAnother: false);
            _root.Q<Button>("btn-reveal-again").clicked += () => DismissReveal(openAnother: true);
        }

        /// <summary>Swap the flat panels for the painted plates. Everything degrades to the
        /// plain styling if the art has not been generated, so this is never load-bearing.</summary>
        void ApplySkin()
        {
            UiSkin.SkinAll(_root);

            UiSkin.SkinIconButton(_turntableButton, "icon_turntable");
            UiSkin.SkinIconButton(_root.Q<Button>("btn-settings"), "icon_settings");

            UiSkin.PrefixIcon(_root.Q<Button>("btn-box"), "icon_chest");
            UiSkin.PrefixIcon(_root.Q<Button>("btn-collection"), "icon_collection");
            UiSkin.PrefixIcon(_root.Q<Button>("btn-expand"), "icon_expand");
        }

        void Subscribe()
        {
            _game.State.WalletChanged += RefreshWallet;
            _game.State.LevelChanged += OnLevelChanged;
            _game.State.CollectionChanged += RefreshPopulation;
            _game.State.TilesChanged += RefreshPopulation;

            _economy.MomentOccurred += OnMoment;
            _economy.CoinsPopped += OnCoinsPopped;

            _camera.CreatureTapped += OnCreatureTapped;

            _audio.NarrationStarted += ShowSubtitle;
            _audio.NarrationFinished += HideSubtitle;

            _unboxing.Revealed += ShowRevealCard;
            _unboxing.FlashRequested += Flash;
        }

        void OnDestroy()
        {
            if (_game != null)
            {
                _game.State.WalletChanged -= RefreshWallet;
                _game.State.LevelChanged -= OnLevelChanged;
                _game.State.CollectionChanged -= RefreshPopulation;
                _game.State.TilesChanged -= RefreshPopulation;
            }

            if (_economy != null)
            {
                _economy.MomentOccurred -= OnMoment;
                _economy.CoinsPopped -= OnCoinsPopped;
            }

            if (_camera != null) _camera.CreatureTapped -= OnCreatureTapped;

            if (_audio != null)
            {
                _audio.NarrationStarted -= ShowSubtitle;
                _audio.NarrationFinished -= HideSubtitle;
            }

            if (_unboxing != null)
            {
                _unboxing.Revealed -= ShowRevealCard;
                _unboxing.FlashRequested -= Flash;
            }
        }

        void Click() => _audio?.PlaySfx("ui_tap");

        // ---- opening ---------------------------------------------------------

        /// <summary>Splash, then the title over the live diorama. Ends when the player
        /// presses Begin, which calls into <see cref="StartGameplay"/>.</summary>
        public IEnumerator RunOpening()
        {
            _camera.SetMode(DioramaCamera.Mode.Cinematic);
            yield return _title.PlayOpening();
        }

        OfflineProgress.Report _pendingWelcome;
        bool _hasPendingWelcome;

        /// <summary>Hold the offline summary until the player has actually started playing;
        /// leading with a payout screen before the title has faded would be graceless.</summary>
        public void QueueWelcomeBack(in OfflineProgress.Report report)
        {
            _pendingWelcome = report;
            _hasPendingWelcome = true;
        }

        void StartGameplay()
        {
            if (_gameplayStarted) return;
            _gameplayStarted = true;

            _camera.SetMode(DioramaCamera.Mode.Interactive);
            SetHudVisible(true);

            _audio.SetAmbience(_sim.Clock.IsNight ? "amb_forest_night" : "amb_forest_day");
            _sim.Paused = false;

            if (_hasPendingWelcome)
            {
                _hasPendingWelcome = false;
                StartCoroutine(ShowWelcomeAfterSettle());
                return;
            }

            if (!OnboardingDirector.Completed) _onboarding.Begin();
        }

        IEnumerator ShowWelcomeAfterSettle()
        {
            // Let the camera finish its move into gameplay framing first.
            yield return new WaitForSecondsRealtime(0.8f);
            ShowWelcomeBack(_pendingWelcome);
        }

        void SetHudVisible(bool visible)
        {
            _hud.EnableInClassList("hidden", !visible);
            _toasts.EnableInClassList("hidden", !visible);
        }

        void OpenSettings()
        {
            OpenModal(_root.Q<VisualElement>("modal-settings"), _settings.Refresh);
        }

        // ---- HUD -------------------------------------------------------------

        void Update()
        {
            if (_sim == null || !_gameplayStarted) return;

            _clockLabel.text = _sim.Clock.Label;

            // The earning rate is derived from every creature's wellbeing, so recomputing
            // it every frame would be wasteful for a number that changes slowly.
            _hudRefreshTimer -= Time.deltaTime;
            if (_hudRefreshTimer <= 0f)
            {
                _hudRefreshTimer = 0.5f;
                _rateLabel.text = $"+{Mathf.RoundToInt(_economy.CoinsPerHour())} / hr";
                _inspector.Tick();
                UpdateAmbienceForTime();
            }
        }

        bool _wasNight;

        void UpdateAmbienceForTime()
        {
            bool night = _sim.Clock.IsNight;
            if (night == _wasNight) return;
            _wasNight = night;

            _audio.SetAmbience(night ? "amb_forest_night" : "amb_forest_day");

            if (night)
            {
                _audio.PlaySfx("night_fall", 0.7f);
                if (OnboardingDirector.Completed) NarrateOnce("milestone_first_night");
            }
        }

        readonly HashSet<string> _narratedOnce = new();

        /// <summary>Milestone lines fire once per install. Hearing the same observation a
        /// second time turns a nice moment into a nagging one.</summary>
        public void NarrateOnce(string key)
        {
            if (!_narratedOnce.Add(key)) return;
            if (PlayerPrefs.GetInt("vo." + key, 0) == 1) return;

            PlayerPrefs.SetInt("vo." + key, 1);
            _audio.Narrate(key, SettingsPanel.SubtitlesEnabled ? TitleScreen.SubtitleFor(key) : null);
        }

        void RefreshWallet()
        {
            _coinsValue.text = _game.State.Data.coins.ToString("N0");
            _essenceValue.text = _game.State.Data.essence.ToString("N0");
            _keysValue.text = _game.State.Data.boxKeys.ToString("N0");
        }

        void OnLevelChanged()
        {
            RefreshLevel();
            _audio.PlaySfx("level_up");
        }

        void RefreshLevel()
        {
            _levelLabel.text = $"Level {_game.State.Data.level}";
            _levelXp.text = $"{_game.State.Data.xp} / {_game.State.XpForNextLevel}";
            _levelFill.style.width = Length.Percent(_game.State.LevelProgress01 * 100f);
        }

        void RefreshPopulation()
        {
            _populationLabel.text = $"{_game.State.PlacedCount} / {_game.State.PopulationCap} creatures";
            RefreshLevel();
        }

        public void RefreshAll()
        {
            RefreshWallet();
            RefreshLevel();
            RefreshPopulation();
        }

        // ---- modals ----------------------------------------------------------

        public void OpenModal(VisualElement panel, Action onOpen = null)
        {
            if (panel == null) return;

            foreach (VisualElement open in _openModals) open.AddToClassList("hidden");
            _openModals.Clear();

            onOpen?.Invoke();
            // Lists are rebuilt on open, so the freshly created rows are skinned here.
            UiSkin.SkinAll(panel);

            panel.RemoveFromClassList("hidden");
            _openModals.Add(panel);

            _modalLayer.RemoveFromClassList("hidden");
            _camera.InputBlocked = true;
            _sim.Paused = true;
        }

        public void CloseModals()
        {
            foreach (VisualElement open in _openModals) open.AddToClassList("hidden");
            _openModals.Clear();

            _modalLayer.AddToClassList("hidden");
            _camera.InputBlocked = false;

            // Do not resume the world while a reveal is on screen behind the card.
            if (!_unboxing.IsRunning) _sim.Paused = false;
        }

        public void ShowWelcomeBack(in OfflineProgress.Report report)
        {
            _welcome.Show(report);
            OpenModal(_welcome.Root);
            _audio.Narrate("welcome_back",
                SettingsPanel.SubtitlesEnabled ? TitleScreen.SubtitleFor("welcome_back") : null);
        }

        // ---- unboxing --------------------------------------------------------

        /// <summary>Hand a rolled result to the 3D reveal. Called by the box screen.</summary>
        public void BeginUnboxing(LootRoller.Result result)
        {
            CloseModals();
            _sim.Paused = true;
            _camera.InputBlocked = true;
            _unboxing.Play(result);
            _onboarding.NotifyBoxOpened();
        }

        void ShowRevealCard(LootRoller.Result result)
        {
            _revealRarity.text = result.Rarity.ToString().ToUpperInvariant();
            _revealRarity.style.color = UnboxingStage.RarityColour(result.Rarity);
            _revealName.text = result.Creature.displayName;

            _revealFlavour.text = string.IsNullOrWhiteSpace(result.Creature.flavourText)
                ? string.Join("  -  ", result.Creature.tags)
                : result.Creature.flavourText;

            if (result.IsNewSpecies)
            {
                _revealBadge.text = "NEW SPECIES";
                _revealBadge.RemoveFromClassList("hidden");
            }
            else if (result.EssenceAwarded > 0)
            {
                _revealBadge.text = $"DUPLICATE   +{result.EssenceAwarded} ESSENCE";
                _revealBadge.RemoveFromClassList("hidden");
            }
            else
            {
                _revealBadge.AddToClassList("hidden");
            }

            _revealLayer.RemoveFromClassList("hidden");
            _revealCard.RemoveFromClassList("reveal-card--in");
            _revealCard.schedule.Execute(() => _revealCard.AddToClassList("reveal-card--in")).ExecuteLater(30);

            if (result.Rarity >= Data.Rarity.Epic) NarrateOnce("milestone_legendary");
            RefreshAll();
        }

        void DismissReveal(bool openAnother)
        {
            Click();
            _revealCard.RemoveFromClassList("reveal-card--in");
            _revealLayer.AddToClassList("hidden");

            _unboxing.Dismiss();
            _camera.InputBlocked = false;
            _sim.Paused = false;

            _onboarding.NotifyRevealDismissed();

            if (openAnother) OpenModal(_boxPanel.Root, _boxPanel.Refresh);
        }

        // ---- effects ---------------------------------------------------------

        void Flash(Color colour, float duration)
        {
            StartCoroutine(FlashRoutine(colour, duration));
        }

        IEnumerator FlashRoutine(Color colour, float duration)
        {
            _flash.style.backgroundColor = colour;
            float t = 0f;

            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                // Instant on, quick falloff: a flash, not a fade.
                _flash.style.opacity = Mathf.Pow(1f - t / duration, 2.2f) * 0.75f;
                yield return null;
            }

            _flash.style.opacity = 0f;
        }

        void ShowSubtitle(string text, float duration)
        {
            if (!SettingsPanel.SubtitlesEnabled || string.IsNullOrEmpty(text)) return;

            if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
            _subtitleText.text = text;
            _subtitleBand.RemoveFromClassList("hidden");
            _subtitleBand.RemoveFromClassList("subtitle--fading");
            _subtitleRoutine = StartCoroutine(HideSubtitleAfter(duration + 0.4f));
        }

        IEnumerator HideSubtitleAfter(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            HideSubtitle();
        }

        void HideSubtitle()
        {
            _subtitleBand.AddToClassList("subtitle--fading");
            _subtitleBand.schedule.Execute(() => _subtitleBand.AddToClassList("hidden")).ExecuteLater(320);
        }

        // ---- toasts and floaters ---------------------------------------------

        void OnMoment(SimEvent e)
        {
            _onboarding.NotifyInteraction(e);

            var toast = new Label(e.Describe());
            toast.AddToClassList("toast");
            _toasts.Add(toast);

            while (_toasts.childCount > MaxToasts) _toasts.RemoveAt(0);
            StartCoroutine(FadeToast(toast));

            if (e.Kind == SimEventKind.StoleFood) NarrateOnce("milestone_theft");
            else if (e.Kind == SimEventKind.StartedChase) NarrateOnce("milestone_chase");

            if (e.Kind == SimEventKind.PlayedInWater) _audio.PlaySfx("water_splash", 0.6f);
        }

        IEnumerator FadeToast(VisualElement toast)
        {
            yield return new WaitForSeconds(ToastLifetime);
            toast.AddToClassList("toast--fading");
            yield return new WaitForSeconds(0.4f);
            toast.RemoveFromHierarchy();
        }

        void OnCoinsPopped(int coins, Vector3 worldPosition)
        {
            if (_floaters == null) return;

            _audio.PlaySfx("coin", 0.45f);

            var label = new Label($"+{coins}");
            label.AddToClassList("floater");
            _floaters.Add(label);
            StartCoroutine(FloatUp(label, worldPosition));
        }

        IEnumerator FloatUp(VisualElement floater, Vector3 worldPosition)
        {
            const float duration = 1.15f;
            float t = 0f;
            var cam = _camera.GetComponent<Camera>();

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;

                Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(
                    _floaters.panel, worldPosition + Vector3.up * (0.6f + k * 0.8f), cam);

                floater.style.left = panelPoint.x - 14f;
                floater.style.top = panelPoint.y;
                floater.style.opacity = Mathf.Clamp01(1f - (k - 0.35f) / 0.65f);

                yield return null;
            }

            floater.RemoveFromHierarchy();
        }

        // ---- selection -------------------------------------------------------

        void OnCreatureTapped(CreatureAgent agent)
        {
            if (_unboxing.IsRunning) return;

            if (agent == null)
            {
                _inspector.Hide();
                return;
            }

            _audio.PlaySfx("ui_tap", 0.5f);
            _inspector.Show(agent);
            _camera.FocusOn(agent.Position, 7f);
        }
    }
}
