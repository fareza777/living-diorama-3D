using System.Collections.Generic;
using LivingDiorama.Core;
using LivingDiorama.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// Buying more diorama.
    ///
    /// The player picks a biome, not a grid square: "I want a river" is how someone
    /// thinks about this, and the game can pick a sensible adjacent tile itself. Cost
    /// still rises with distance from the centre, so expansion stays paced.
    /// </summary>
    public sealed class ExpandPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;
        readonly ScrollView _list;
        readonly Label _subtitle;
        readonly List<Vector2Int> _availableBuffer = new(16);

        public VisualElement Root => _root;

        public ExpandPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _list = root.Q<ScrollView>("expand-list");
            _subtitle = root.Q<Label>("expand-subtitle");
            root.Q<Button>("btn-expand-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _list.Clear();
            _game.AvailableCoords(_availableBuffer);

            if (_availableBuffer.Count == 0)
            {
                _subtitle.text = "The diorama has reached its edge. Nothing more to claim.";
                return;
            }

            Vector2Int next = NearestToCentre(_availableBuffer);
            _subtitle.text = $"Each new tile raises your creature limit by " +
                             $"{_game.Database.progression.creaturesPerTile}. " +
                             $"Next tile sits in ring {Diorama.DioramaWorld.RingOf(next)}.";

            foreach (BiomeDefinition biome in _game.Database.biomes)
            {
                if (biome != null) _list.Add(BuildOption(biome, next));
            }
        }

        /// <summary>Grow compactly rather than in a long finger, which keeps the diorama
        /// framed nicely and keeps creatures within sight of each other.</summary>
        static Vector2Int NearestToCentre(List<Vector2Int> options)
        {
            Vector2Int best = options[0];
            int bestScore = int.MaxValue;

            foreach (Vector2Int c in options)
            {
                int score = Mathf.Abs(c.x) * 3 + Mathf.Abs(c.y) * 3 + Mathf.Abs(c.x * c.y);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        VisualElement BuildOption(BiomeDefinition biome, Vector2Int coord)
        {
            bool levelLocked = _game.State.Data.level < biome.requiredLevel;
            int cost = _game.TileCost(coord, biome);
            bool affordable = _game.State.CanAfford(CurrencyKind.Coins, cost);

            var option = new VisualElement();
            option.AddToClassList("tile-option");
            if (levelLocked) option.AddToClassList("tile-option--locked");

            var swatch = new VisualElement();
            swatch.AddToClassList("tile-option__swatch");
            swatch.style.backgroundColor = biome.groundHigh;
            option.Add(swatch);

            var text = new VisualElement { style = { flexGrow = 1 } };

            var name = new Label(biome.displayName);
            name.AddToClassList("tile-option__name");
            text.Add(name);

            var meta = new Label(levelLocked
                ? $"Unlocks at level {biome.requiredLevel}"
                : string.IsNullOrWhiteSpace(biome.description)
                    ? (biome.hasWater ? "Has water" : "Dry land")
                    : biome.description);
            meta.AddToClassList("tile-option__meta");
            text.Add(meta);
            option.Add(text);

            var actions = new VisualElement { style = { alignItems = Align.FlexEnd } };

            var price = new Label($"{cost:N0}");
            price.AddToClassList("tile-option__cost");
            actions.Add(price);

            var buy = new Button(() => OnBuy(coord, biome)) { text = "Claim" };
            buy.AddToClassList("button");
            buy.AddToClassList(affordable && !levelLocked ? "button--primary" : "button--ghost");
            buy.style.minHeight = 36;
            buy.style.fontSize = 13;
            buy.style.marginTop = 4;
            buy.SetEnabled(affordable && !levelLocked);
            actions.Add(buy);

            option.Add(actions);
            return option;
        }

        void OnBuy(Vector2Int coord, BiomeDefinition biome)
        {
            if (!_game.TryUnlockTile(coord, biome)) return;

            _ui.RefreshAll();
            Refresh();
        }
    }
}
