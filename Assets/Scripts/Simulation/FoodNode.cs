using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// A place a creature can feed. Berry bushes, carcasses, crystal clusters and the
    /// tile stockpile are all the same thing with different art and diets.
    /// </summary>
    public sealed class FoodNode : MonoBehaviour
    {
        [Tooltip("Which diets this node satisfies.")]
        public Diet[] feeds = { Diet.Herbivore, Diet.Omnivore };

        [Tooltip("Servings left before the node depletes and starts regrowing.")]
        [Min(1)] public int servings = 3;

        [Tooltip("In-game hours to regrow one serving.")]
        [Min(0.05f)] public float regrowHours = 2.5f;

        [Tooltip("A stockpile is the tile's shared larder. Stealing from it is what makes " +
                 "a goblin a nuisance rather than just another mouth to feed.")]
        public bool isStockpile;

        [Tooltip("Visual that hides when the node is empty.")]
        public GameObject bounty;

        int _remaining;
        double _nextRegrowAtHours;

        public int Remaining => _remaining;
        public bool HasFood => _remaining > 0;
        public Vector3 Position => transform.position;

        void Awake()
        {
            _remaining = Mathf.Max(1, servings);
            RefreshVisual();
        }

        public bool Satisfies(Diet diet)
        {
            if (feeds == null) return false;
            for (int i = 0; i < feeds.Length; i++)
            {
                if (feeds[i] == diet) return true;
                if (feeds[i] == Diet.Omnivore && (diet == Diet.Herbivore || diet == Diet.Carnivore)) return true;
                if (diet == Diet.Omnivore && (feeds[i] == Diet.Herbivore || feeds[i] == Diet.Carnivore)) return true;
            }
            return false;
        }

        /// <summary>Take one serving. Returns false when empty.</summary>
        public bool Consume(double nowHours)
        {
            if (_remaining <= 0) return false;
            _remaining--;
            if (_nextRegrowAtHours <= 0) _nextRegrowAtHours = nowHours + regrowHours;
            RefreshVisual();
            return true;
        }

        /// <summary>Called by the simulation on its slow tick.</summary>
        public void Regrow(double nowHours)
        {
            if (_remaining >= servings)
            {
                _nextRegrowAtHours = 0;
                return;
            }
            if (_nextRegrowAtHours <= 0 || nowHours < _nextRegrowAtHours) return;

            _remaining++;
            _nextRegrowAtHours = _remaining >= servings ? 0 : nowHours + regrowHours;
            RefreshVisual();
        }

        void RefreshVisual()
        {
            if (bounty != null && bounty.activeSelf != (_remaining > 0))
            {
                bounty.SetActive(_remaining > 0);
            }
        }
    }
}
