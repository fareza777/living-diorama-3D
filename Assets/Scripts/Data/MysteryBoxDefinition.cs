using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// A box is a weighted rarity roll plus a pool. Pity counters guarantee the player
    /// eventually sees the good stuff, which is what keeps opening boxes feeling fair.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Mystery Box", fileName = "Box_")]
    public sealed class MysteryBoxDefinition : ScriptableObject
    {
        [Serializable]
        public struct RarityWeight
        {
            public Rarity rarity;
            [Min(0f)] public float weight;
        }

        [Header("Identity")]
        public string id = "wooden_box";
        public string displayName = "Wooden Box";
        [TextArea(2, 3)] public string description = "";
        public Sprite icon;
        public Color accentColour = new(0.72f, 0.52f, 0.28f);

        [Header("Cost")]
        public CurrencyKind costCurrency = CurrencyKind.Coins;
        [Min(0)] public int cost = 100;

        [Tooltip("If true this box can also be opened for free by watching a rewarded ad.")]
        public bool rewardedAdEligible = true;

        [Tooltip("Seconds before another free rewarded-ad open is offered.")]
        [Min(0)] public int rewardedAdCooldownSeconds = 900;

        [Header("Odds")]
        public List<RarityWeight> weights = new()
        {
            new RarityWeight { rarity = Rarity.Common, weight = 62f },
            new RarityWeight { rarity = Rarity.Uncommon, weight = 25f },
            new RarityWeight { rarity = Rarity.Rare, weight = 10f },
            new RarityWeight { rarity = Rarity.Epic, weight = 2.5f },
            new RarityWeight { rarity = Rarity.Legendary, weight = 0.5f },
        };

        [Header("Pity")]
        [Tooltip("Opens without a Rare-or-better before one is forced. 0 disables.")]
        [Min(0)] public int pityRareAfter = 10;

        [Tooltip("Opens without an Epic-or-better before one is forced. 0 disables.")]
        [Min(0)] public int pityEpicAfter = 45;

        [Header("Pool")]
        [Tooltip("Candidates. The roll picks a rarity first, then a uniform pick among " +
                 "pool members of that rarity, falling back to the nearest rarity present.")]
        public List<CreatureDefinition> pool = new();

        [Header("Duplicate handling")]
        [Tooltip("Essence granted instead when the player already owns the rolled species.")]
        [Min(0)] public int duplicateEssence = 15;

        [Tooltip("Duplicates still add a copy the player can place. Off means dupes convert " +
                 "to essence only.")]
        public bool duplicatesGrantCopy = true;

        public float TotalWeight
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < weights.Count; i++) t += Mathf.Max(0f, weights[i].weight);
                return t;
            }
        }

        /// <summary>Odds shown in the UI, normalised so they always read as percentages.</summary>
        public float ChanceOf(Rarity rarity)
        {
            float total = TotalWeight;
            if (total <= 0f) return 0f;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i].rarity == rarity) return Mathf.Max(0f, weights[i].weight) / total;
            }
            return 0f;
        }

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name.ToLowerInvariant();
        }
    }
}
