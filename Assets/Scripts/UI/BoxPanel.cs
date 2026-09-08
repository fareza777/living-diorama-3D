using System;
using LivingDiorama.Core;
using LivingDiorama.Data;
using LivingDiorama.Meta;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The mystery box screen. Odds are shown up front for every box, including the pity
    /// guarantee and how many opens are left on it -- a collection game only feels good
    /// when the player can see they are not being strung along.
    /// </summary>
    public sealed class BoxPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly ScrollView _list;
        readonly Label _message;

        public VisualElement Root => _root;

        public BoxPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _list = root.Q<ScrollView>("box-list");
            _message = root.Q<Label>("box-message");

            root.Q<Button>("btn-box-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _message.AddToClassList("hidden");
            _list.Clear();

            foreach (MysteryBoxDefinition box in _game.Database.boxes)
            {
                if (box != null) _list.Add(BuildEntry(box));
            }
        }

        VisualElement BuildEntry(MysteryBoxDefinition box)
        {
            var entry = new VisualElement();
            entry.AddToClassList("box-entry");

            var art = new VisualElement();
            art.AddToClassList("box-entry__art");
            art.style.backgroundColor = box.accentColour;
            entry.Add(art);

            var text = new VisualElement { style = { flexGrow = 1 } };
            text.Add(new Label(box.displayName) { });
            text.Q<Label>().AddToClassList("box-entry__name");

            var odds = new Label(DescribeOdds(box));
            odds.AddToClassList("box-entry__odds");
            text.Add(odds);
            entry.Add(text);

            var actions = new VisualElement { style = { alignItems = Align.FlexEnd } };

            var buy = new Button(() => OnBuy(box))
            {
                text = $"{box.cost:N0} {box.costCurrency}",
            };
            buy.AddToClassList("button");
            buy.AddToClassList("button--primary");
            buy.style.minHeight = 40;
            buy.style.fontSize = 13;
            buy.SetEnabled(_game.CanAffordBox(box));
            actions.Add(buy);

            if (box.rewardedAdEligible)
            {
                bool ready = _game.IsFreeOpenAvailable(box);
                var free = new Button(() => OnWatchAd(box))
                {
                    text = ready ? "Free (watch ad)" : $"Free in {FormatCooldown(_game.FreeOpenSecondsRemaining(box))}",
                };
                free.AddToClassList("button");
                free.AddToClassList("button--ad");
                free.style.minHeight = 36;
                free.style.fontSize = 12;
                free.style.marginTop = 6;
                free.SetEnabled(ready);
                actions.Add(free);
            }

            entry.Add(actions);
            return entry;
        }

        static string FormatCooldown(int seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600}h";
            if (seconds >= 60) return $"{seconds / 60}m";
            return $"{seconds}s";
        }

        string DescribeOdds(MysteryBoxDefinition box)
        {
            string odds =
                $"Rare {box.ChanceOf(Rarity.Rare) * 100f:0.#}%  |  " +
                $"Epic {box.ChanceOf(Rarity.Epic) * 100f:0.#}%  |  " +
                $"Legendary {box.ChanceOf(Rarity.Legendary) * 100f:0.##}%";

            if (box.pityRareAfter > 0)
            {
                Save.SavedBox saved = _game.State.Data.BoxState(box.id);
                int left = Mathf.Max(0, box.pityRareAfter - saved.sinceRare);
                odds += $"\nGuaranteed Rare within {left} more";
            }

            return odds;
        }

        // ---- actions --------------------------------------------------------

        void OnBuy(MysteryBoxDefinition box)
        {
            GameController.OpenFailure failure = _game.TryOpenBox(box, out LootRoller.Result result);
            HandleResult(failure, result);
        }

        void OnWatchAd(MysteryBoxDefinition box)
        {
            _game.OpenBoxWithAd(box, HandleResult);
        }

        void HandleResult(GameController.OpenFailure failure, LootRoller.Result result)
        {
            if (failure != GameController.OpenFailure.None || !result.IsValid)
            {
                ShowMessage(failure switch
                {
                    GameController.OpenFailure.CannotAfford => "Not enough to open that one yet.",
                    GameController.OpenFailure.OnCooldown => "The free open is still on cooldown.",
                    GameController.OpenFailure.AdNotReady => "No ad available right now.",
                    GameController.OpenFailure.EmptyPool => "That box has nothing in it.",
                    _ => "Could not open that box.",
                });
                return;
            }

            // The roll is already resolved and banked; the 3D sequence is presentation.
            // Handing it off here means the reveal cannot desync from the actual reward.
            _ui.BeginUnboxing(result);
        }

        void ShowMessage(string message)
        {
            _message.text = message;
            _message.RemoveFromClassList("hidden");
        }

        public static Color RarityColour(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => new Color(0.49f, 0.84f, 0.54f),
            Rarity.Rare => new Color(0.41f, 0.69f, 1f),
            Rarity.Epic => new Color(0.75f, 0.52f, 1f),
            Rarity.Legendary => new Color(1f, 0.77f, 0.33f),
            _ => new Color(0.62f, 0.65f, 0.72f),
        };
    }
}
