using LivingDiorama.Core;
using LivingDiorama.Data;
using LivingDiorama.Save;
using UnityEngine;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The collection screen: every species in the game, discovered ones in colour and
    /// the rest as silhouettes. Undiscovered entries still show their rarity border,
    /// because knowing a Legendary exists is half the reason to keep opening boxes.
    /// </summary>
    public sealed class CollectionPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly VisualElement _grid;
        readonly Label _progress;
        readonly VisualElement _detail;
        readonly Label _detailName, _detailFlavour, _detailTags;
        readonly Button _placeButton;

        CreatureDefinition _selected;

        public VisualElement Root => _root;

        public CollectionPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _grid = root.Q<VisualElement>("collection-grid-inner");
            _progress = root.Q<Label>("collection-progress");
            _detail = root.Q<VisualElement>("collection-detail");
            _detailName = root.Q<Label>("collection-detail-name");
            _detailFlavour = root.Q<Label>("collection-detail-flavour");
            _detailTags = root.Q<Label>("collection-detail-tags");
            _placeButton = root.Q<Button>("btn-place");

            _placeButton.clicked += OnPlace;
            root.Q<Button>("btn-collection-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _grid.Clear();
            _detail.AddToClassList("hidden");
            _selected = null;

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

        void Select(CreatureDefinition def)
        {
            _selected = def;
            _detail.RemoveFromClassList("hidden");

            _detailName.text = def.displayName;
            _detailFlavour.text = string.IsNullOrWhiteSpace(def.flavourText)
                ? $"{def.rarity} - {def.diet}, {def.activity}"
                : def.flavourText;

            _detailTags.text = def.tags is { Length: > 0 }
                ? "Traits: " + string.Join(", ", def.tags)
                : "";

            SavedCreature spare = FindUnplaced(def.id);
            bool canPlace = spare != null && _game.State.HasRoomToPlace;

            _placeButton.text = spare == null
                ? "All copies already placed"
                : _game.State.HasRoomToPlace
                    ? "Place in diorama"
                    : "Diorama is full - expand first";

            _placeButton.SetEnabled(canPlace);
        }

        SavedCreature FindUnplaced(string speciesId)
        {
            foreach (SavedCreature c in _game.State.Data.creatures)
            {
                if (c.speciesId == speciesId && !c.placed) return c;
            }
            return null;
        }

        void OnPlace()
        {
            if (_selected == null) return;

            SavedCreature spare = FindUnplaced(_selected.id);
            if (spare == null) return;

            if (_game.PlaceFromCollection(spare.instanceId))
            {
                _ui.RefreshAll();
                _ui.CloseModals();
            }
        }
    }
}
