using LivingDiorama.Core;
using LivingDiorama.Data;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The collection screen: every species in the game, discovered ones in colour and
    /// the rest as silhouettes. Undiscovered entries still show their rarity border,
    /// because knowing a Legendary exists is half the reason to keep opening boxes.
    ///
    /// Tapping one hands over to <see cref="InspectPanel"/>, which gets the whole screen
    /// for it. The grid's job is to be a grid.
    /// </summary>
    public sealed class CollectionPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly VisualElement _grid;
        readonly Label _progress;

        public VisualElement Root => _root;

        public CollectionPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _grid = root.Q<VisualElement>("collection-grid-inner");
            _progress = root.Q<Label>("collection-progress");

            root.Q<Button>("btn-collection-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _grid.Clear();

            _progress.text = $"{_game.State.DiscoveredCount} of {_game.State.SpeciesTotal} discovered  -  " +
                             $"{_game.State.PlacedCount}/{_game.State.PopulationCap} placed";

            foreach (CreatureDefinition def in _game.Database.creatures)
            {
                if (def != null) _grid.Add(BuildCard(def));
            }
        }

        VisualElement BuildCard(CreatureDefinition def)
        {
            bool known = _game.State.IsDiscovered(def.id);

            var card = new VisualElement();
            card.AddToClassList("card");
            card.AddToClassList("card--" + def.rarity.ToString().ToLowerInvariant());
            if (!known) card.AddToClassList("card--locked");

            var portrait = new VisualElement();
            portrait.AddToClassList("card__portrait");
            if (known && def.icon != null)
            {
                portrait.style.backgroundImage = new StyleBackground(def.icon);
            }
            else if (!known)
            {
                portrait.AddToClassList("card__portrait--unknown");
            }
            else
            {
                // No portrait asset: fall back to the rarity colour so the grid still reads.
                portrait.style.backgroundColor = BoxPanel.RarityColour(def.rarity) * 0.5f;
            }
            card.Add(portrait);

            var name = new Label(known ? def.displayName : "???");
            name.AddToClassList("card__name");
            card.Add(name);

            int copies = _game.State.CopiesOf(def.id);
            var count = new Label(known ? (copies > 1 ? $"x{copies}" : "owned") : "undiscovered");
            count.AddToClassList("card__count");
            card.Add(count);

            if (known)
            {
                card.RegisterCallback<ClickEvent>(_ => Select(def));
            }

            return card;
        }

        void Select(CreatureDefinition def) => _ui.OpenInspector(def);
    }
}
