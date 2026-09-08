using LivingDiorama.Ads;
using LivingDiorama.Core;
using LivingDiorama.Meta;
using LivingDiorama.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// Tap a creature to see what it is thinking. Showing the raw needs and the current
    /// behaviour turns the simulation from a black box into something the player can
    /// reason about -- which is the difference between "it wandered off" and "ah, it is
    /// hungry and there is no food on that side".
    /// </summary>
    public sealed class InspectorPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly Label _name, _species;
        readonly VisualElement _needs;

        CreatureAgent _agent;

        readonly VisualElement[] _fills = new VisualElement[4];
        readonly Label[] _labels = new Label[4];

        static readonly string[] NeedNames = { "Fed", "Rested", "Social", "Happy" };

        public InspectorPanel(VisualElement root, GameController game)
        {
            _root = root;
            _game = game;
            _name = root.Q<Label>("inspector-name");
            _species = root.Q<Label>("inspector-species");
            _needs = root.Q<VisualElement>("inspector-needs");

            BuildNeedBars();

            root.Q<Button>("btn-inspector-close").clicked += Hide;
            root.Q<Button>("btn-inspector-recall").clicked += OnRecall;
        }

        void BuildNeedBars()
        {
            for (int i = 0; i < NeedNames.Length; i++)
            {
                var block = new VisualElement();
                block.AddToClassList("need");

                var row = new VisualElement();
                row.AddToClassList("need__row");

                var caption = new Label(NeedNames[i]);
                caption.AddToClassList("need__label");
                row.Add(caption);

                var value = new Label("100%");
                value.AddToClassList("need__label");
                row.Add(value);
                _labels[i] = value;

                block.Add(row);

                var track = new VisualElement();
                track.AddToClassList("need__track");

                var fill = new VisualElement();
                fill.AddToClassList("need__fill");
                fill.AddToClassList("need__fill--good");
                track.Add(fill);
                _fills[i] = fill;

                block.Add(track);
                _needs.Add(block);
            }
        }

        public void Show(CreatureAgent agent)
        {
            _agent = agent;
            _root.RemoveFromClassList("hidden");
            Tick();
        }

        public void Hide()
        {
            _agent = null;
            _root.AddToClassList("hidden");
        }

        public void Tick()
        {
            if (_agent == null || !_agent.IsActive)
            {
                if (_agent != null) Hide();
                return;
            }

            _name.text = _agent.DisplayName;
            _species.text = $"{DescribeMood()}  -  {DescribeBehaviour()}";

            SetNeed(0, _agent.Fullness);
            SetNeed(1, _agent.Energy);
            SetNeed(2, _agent.Social);
            SetNeed(3, _agent.Fun);
        }

        string DescribeMood() => _agent.CurrentMood switch
        {
            Mood.Hungry => "Hungry",
            Mood.Sleepy => "Sleepy",
            Mood.Scared => "Frightened",
            Mood.Angry => "Furious",
            Mood.Playful => "Playful",
            Mood.Social => "Sociable",
            Mood.KnockedOut => "Out cold",
            _ => "Content",
        };

        string DescribeBehaviour() => _agent.Current?.Id switch
        {
            "idle" => "looking around",
            "wander" => "wandering",
            "sleep" => "asleep",
            "seek_food" => "looking for food",
            "flee" => "running away",
            "chase" => "giving chase",
            "attack" => "fighting",
            "socialise" => "keeping company",
            "play_water" => "playing in the water",
            "steal" => "up to no good",
            _ => "settling in",
        };

        void SetNeed(int index, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            _fills[index].style.width = Length.Percent(value01 * 100f);
            _labels[index].text = $"{Mathf.RoundToInt(value01 * 100f)}%";

            _fills[index].RemoveFromClassList("need__fill--good");
            _fills[index].RemoveFromClassList("need__fill--warn");
            _fills[index].RemoveFromClassList("need__fill--bad");
            _fills[index].AddToClassList(value01 > 0.6f ? "need__fill--good"
                                       : value01 > 0.3f ? "need__fill--warn"
                                       : "need__fill--bad");
        }

        void OnRecall()
        {
            if (_agent == null) return;
            _game.RecallToCollection(_agent.InstanceId);
            Hide();
        }
    }

    /// <summary>The "welcome back" screen. Offers a rewarded ad to double the offline
    /// haul, which is the one ad placement players actively want.</summary>
    public sealed class WelcomePanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;
        readonly Label _body, _amount;
        readonly Button _double, _claim;

        OfflineProgress.Report _report;
        bool _claimed;

        public VisualElement Root => _root;

        public WelcomePanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _body = root.Q<Label>("welcome-body");
            _amount = root.Q<Label>("welcome-amount");
            _double = root.Q<Button>("btn-welcome-double");
            _claim = root.Q<Button>("btn-welcome-claim");

            _double.clicked += OnDouble;
            _claim.clicked += () => Claim(1f);
        }

        public void Show(in OfflineProgress.Report report)
        {
            _report = report;
            _claimed = false;

            _body.text = $"Your diorama carried on without you for {report.ElapsedLabel}.";
            _amount.text = $"+{report.Coins:N0} coins";

            _double.style.display = AdHub.Current.RewardedReady ? DisplayStyle.Flex : DisplayStyle.None;
            _double.SetEnabled(true);
            _claim.SetEnabled(true);
        }

        void OnDouble()
        {
            _double.SetEnabled(false);
            AdHub.Current.ShowRewarded(AdPlacement.DoubleOfflineEarnings, earned =>
            {
                Claim(earned ? _game.Database.progression.rewardedOfflineMultiplier : 1f);
            });
        }

        void Claim(float multiplier)
        {
            if (_claimed) return;
            _claimed = true;

            OfflineProgress.Apply(_game.State.Data, _game.Database, _report, _game.State, multiplier);
            _game.RequestSave();
            _ui.RefreshAll();
            _ui.CloseModals();
        }
    }
}
