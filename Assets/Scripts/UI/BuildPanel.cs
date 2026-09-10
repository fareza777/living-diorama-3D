using LivingDiorama.Core;
using LivingDiorama.Diorama;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// What the player has left to build with.
    ///
    /// A list rather than a grid of icons: with four kinds of thing, a row that can say
    /// what each one is *for* teaches the mechanic in the same space an icon would have
    /// used to be decorative. "Somewhere to sleep" is the whole tutorial.
    /// </summary>
    public sealed class BuildPanel
    {
        readonly VisualElement _root;
        readonly GameController _game;
        readonly GameUI _ui;

        readonly ScrollView _list;
        readonly Label _empty;

        public VisualElement Root => _root;

        public BuildPanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;
            _ui = ui;

            _list = root.Q<ScrollView>("build-list");
            _empty = root.Q<Label>("build-empty");

            root.Q<Button>("btn-build-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _list.Clear();

            int owned = 0;
            foreach (Placeable placeable in Placeables.All)
            {
                int count = _game.State.StockOf(placeable.Id);
                if (count > 0) owned++;

                _list.Add(BuildRow(placeable, count));
            }

            _empty.EnableInClassList("hidden", owned > 0);
            UiMotion.Stagger(_list.contentContainer);
        }

        VisualElement BuildRow(Placeable placeable, int count)
        {
            var row = new VisualElement();
            row.AddToClassList("stock");
            if (count <= 0) row.AddToClassList("stock--empty");

            var text = new VisualElement { style = { flexGrow = 1 } };

            var name = new Label(placeable.DisplayName);
            name.AddToClassList("stock__name");
            text.Add(name);

            var use = new Label(Describe(placeable.Role));
            use.AddToClassList("stock__use");
            text.Add(use);
            row.Add(text);

            var have = new Label(count.ToString());
            have.AddToClassList("stock__count");
            row.Add(have);

            var place = new Button(() => _ui.BeginBuilding(placeable.Id)) { text = "Place" };
            place.AddToClassList("button");
            place.AddToClassList("button--primary");
            place.style.minHeight = 42;
            place.style.width = 96;
            place.SetEnabled(count > 0);
            row.Add(place);

            return row;
        }

        /// <summary>Said as what the creature gets out of it, not as what the object is.
        /// The player is not furnishing a room, they are meeting a need.</summary>
        static string Describe(PlaceableRole role) => role switch
        {
            PlaceableRole.Rest => "Somewhere to sleep properly",
            PlaceableRole.Eat => "Food, without having to forage",
            PlaceableRole.Train => "Something to practise against",
            PlaceableRole.Play => "Something to do for the fun of it",
            _ => "",
        };
    }
}
