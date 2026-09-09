using LivingDiorama.Core;
using LivingDiorama.Data;
using UnityEngine.UIElements;

namespace LivingDiorama.UI
{
    /// <summary>
    /// The Chronicle: what your diorama has been caught doing, and what it has not.
    ///
    /// This is the screen the collection cannot be. A collection says what you own, which
    /// is a function of how many boxes you bought; the Chronicle says what you have
    /// arranged, which is a function of who you put together and when you were watching.
    /// Unwitnessed entries keep their hint on show for exactly that reason -- the list of
    /// things you have not managed yet is the part that makes anyone come back.
    /// </summary>
    public sealed class ChroniclePanel
    {
        readonly VisualElement _root;
        readonly GameController _game;

        readonly ScrollView _list;
        readonly Label _progress;

        public VisualElement Root => _root;

        public ChroniclePanel(VisualElement root, GameController game, GameUI ui)
        {
            _root = root;
            _game = game;

            _list = root.Q<ScrollView>("chronicle-list");
            _progress = root.Q<Label>("chronicle-progress");

            root.Q<Button>("btn-chronicle-close").clicked += ui.CloseModals;
        }

        public void Refresh()
        {
            _list.Clear();

            var chronicle = _game.Chronicle;
            int found = chronicle.WitnessedCount;
            int total = chronicle.All.Count;

            _progress.text = found == 0
                ? $"Nothing witnessed yet — {total} to find"
                : $"{found} of {total} witnessed";

            foreach (MomentDefinition moment in chronicle.All)
            {
                if (moment == null) continue;
                _list.Add(Row(moment, chronicle.HasWitnessed(moment.id)));
            }
        }

        static VisualElement Row(MomentDefinition moment, bool found)
        {
            var row = new VisualElement();
            row.AddToClassList("moment");
            if (found) row.AddToClassList("moment--found");

            var seal = new Label(found ? "✦" : "?");
            seal.AddToClassList("moment__seal");
            if (found) seal.style.color = RarityColour(moment.prestige);
            row.Add(seal);

            var body = new VisualElement();
            body.AddToClassList("moment__body");

            var title = new Label(found ? moment.title : "Not yet witnessed");
            title.AddToClassList("moment__title");
            if (!found) title.AddToClassList("moment__title--locked");
            body.Add(title);

            var text = new Label(found ? moment.flavour : moment.hint);
            text.AddToClassList("moment__text");
            body.Add(text);

            if (!found && moment.essence > 0)
            {
                var reward = new Label($"+{moment.essence} essence");
                reward.AddToClassList("moment__reward");
                body.Add(reward);
            }

            row.Add(body);
            return row;
        }

        static UnityEngine.Color RarityColour(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => new UnityEngine.Color(0.49f, 0.84f, 0.54f),
            Rarity.Rare => new UnityEngine.Color(0.41f, 0.69f, 1f),
            Rarity.Epic => new UnityEngine.Color(0.75f, 0.52f, 1f),
            Rarity.Legendary => new UnityEngine.Color(1f, 0.77f, 0.33f),
            _ => new UnityEngine.Color(0.62f, 0.65f, 0.72f),
        };
    }
}
