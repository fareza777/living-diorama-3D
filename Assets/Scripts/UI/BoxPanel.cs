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

        /// <summary>
        /// One box, as a card rather than a row.
        ///
        /// The old row put the product -- the box itself -- in a 62-pixel square beside
        /// three lines of prose, with two competing buttons crammed into a column at the
        /// right. The odds read as a sentence that wrapped mid-fact, and the widest,
        /// heaviest control on the screen was Close. This gives the art the top of the
        /// card, turns the odds into a chart with a legend, shows progress against the
        /// pity promise instead of merely stating it, and leaves exactly one primary
        /// action.
        /// </summary>
        VisualElement BuildEntry(MysteryBoxDefinition box)
        {
            var entry = new VisualElement();
            entry.AddToClassList("box-entry");

            // ---- the box itself ------------------------------------------------
            var top = new VisualElement();
            top.AddToClassList("box-entry__top");

            var art = new VisualElement();
            art.AddToClassList("box-entry__art");
            art.style.unityBackgroundImageTintColor = new Color(
                0.55f + box.accentColour.r * 0.45f,
                0.55f + box.accentColour.g * 0.45f,
                0.55f + box.accentColour.b * 0.45f, 1f);

            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("box-entry__glyph");
            UiSkin.ApplyIcon(glyph, "icon_chest");
            glyph.style.unityBackgroundImageTintColor = box.accentColour;
            art.Add(glyph);
            top.Add(art);

            // ---- name, odds chart, legend --------------------------------------
            var text = new VisualElement();
            text.AddToClassList("box-entry__body");

            var name = new Label(box.displayName);
            name.AddToClassList("box-entry__name");
            text.Add(name);

            text.Add(OddsBar(box));
            text.Add(OddsLegend(box));
            top.Add(text);
            entry.Add(top);

            // ---- the pity promise, with progress against it ---------------------
            if (box.pityRareAfter > 0)
            {
                Save.SavedBox saved = _game.State.Data.BoxState(box.id);
                int done = Mathf.Clamp(saved.sinceRare, 0, box.pityRareAfter);

                var pity = new VisualElement();
                pity.AddToClassList("pity");

                var caption = new Label("Rare guaranteed");
                caption.AddToClassList("pity__label");
                pity.Add(caption);

                var track = new VisualElement();
                track.AddToClassList("pity__track");

                var fill = new VisualElement();
                fill.AddToClassList("pity__fill");
                fill.style.width = Length.Percent(done / (float)box.pityRareAfter * 100f);
                track.Add(fill);
                pity.Add(track);

                var count = new Label($"{done} / {box.pityRareAfter}");
                count.AddToClassList("pity__count");
                pity.Add(count);

                entry.Add(pity);
            }

            // ---- one primary, one alternative ----------------------------------
            var actions = new VisualElement();
            actions.AddToClassList("box-entry__actions");

            var buy = new Button(() => OnBuy(box)) { text = $"{box.cost:N0} {box.costCurrency}" };
            buy.AddToClassList("button");
            buy.AddToClassList("button--primary");
            buy.AddToClassList("box-entry__buy");
            buy.SetEnabled(_game.CanAffordBox(box));
            actions.Add(buy);

            if (box.rewardedAdEligible)
            {
                bool ready = _game.IsFreeOpenAvailable(box);
                var free = new Button(() => OnWatchAd(box))
                {
                    text = ready ? "Watch an ad" : $"Free in {FormatCooldown(_game.FreeOpenSecondsRemaining(box))}",
                };
                free.AddToClassList("button");
                free.AddToClassList("button--ad");
                free.AddToClassList("box-entry__free");
                free.SetEnabled(ready);
                actions.Add(free);
            }

            entry.Add(actions);
            return entry;
        }

        /// <summary>
        /// The figures, tied to the bar above them by colour.
        ///
        /// A dot the same colour as its segment is what turns the bar from decoration
        /// into a chart. Tiers the box cannot give stay listed and dimmed: knowing a
        /// Legendary exists and that this box will never produce one is information the
        /// player is entitled to.
        /// </summary>
        static VisualElement OddsLegend(MysteryBoxDefinition box)
        {
            var legend = new VisualElement();
            legend.AddToClassList("legend");

            foreach (Rarity rarity in Tiers)
            {
                float share = box.ChanceOf(rarity);

                var row = new VisualElement();
                row.AddToClassList("legend__item");
                if (share <= 0.0001f) row.AddToClassList("legend__item--none");

                var dot = new VisualElement();
                dot.AddToClassList("legend__dot");
                dot.style.backgroundColor = RarityColour(rarity);
                row.Add(dot);

                var label = new Label($"{rarity} {share * 100f:0.#}%");
                label.AddToClassList("legend__label");
                row.Add(label);

                legend.Add(row);
            }

            return legend;
        }

        static readonly Rarity[] Tiers =
        {
            Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary,
        };

        static string FormatCooldown(int seconds)
        {
            if (seconds >= 3600) return $"{seconds / 3600}h";
            if (seconds >= 60) return $"{seconds / 60}m";
            return $"{seconds}s";
        }

        /// <summary>
        /// The odds as a band of colour rather than a row of percentages.
        ///
        /// A table of numbers is the most honest way to state a distribution and the
        /// worst way to feel one. The bar says the same thing at a glance -- mostly
        /// common, a sliver of gold -- and the exact figures stay underneath it for
        /// anyone who wants them.
        /// </summary>
        static VisualElement OddsBar(MysteryBoxDefinition box)
        {
            var bar = new VisualElement();
            bar.AddToClassList("odds");

            foreach (Rarity rarity in Tiers)
            {
                float share = box.ChanceOf(rarity);
                if (share <= 0.0001f) continue;

                var slice = new VisualElement();
                slice.AddToClassList("odds__slice");
                slice.style.flexGrow = share;
                slice.style.backgroundColor = RarityColour(rarity);
                bar.Add(slice);
            }

            return bar;
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
