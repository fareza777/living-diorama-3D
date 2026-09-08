using System;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// A biome is both a look and a set of simulation affordances. The palette here
    /// drives the procedural terrain mesh and the lighting, so unlocking "snow"
    /// visibly repaints a chunk of the diorama without any authored art.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Biome", fileName = "Biome_")]
    public sealed class BiomeDefinition : ScriptableObject
    {
        /// <summary>Which procedural generator to use for a scatter layer. Props are
        /// generated rather than authored so a new biome is a data change, not an art task.</summary>
        public enum PropKind
        {
            PineTree,
            BroadleafTree,
            Rock,
            Bush,
            GrassTuft,
            Mushroom,
            Crystal,
        }

        [Serializable]
        public struct ScatterEntry
        {
            public PropKind kind;

            [Tooltip("Instances per square world unit.")]
            [Range(0f, 4f)] public float density;

            public Vector2 scaleRange;

            [Tooltip("Keep this far from other props in the same layer.")]
            [Min(0f)] public float minSpacing;

            [Tooltip("Primary colour. The generator blends between this and the secondary.")]
            public Color primary;
            public Color secondary;

            [Tooltip("Render through the foliage shader so this layer picks up the wind. " +
                     "Leave off for rocks and crystals.")]
            public bool windSwept;

            [Tooltip("Allow placement in the riverbed. Off for anything that would look " +
                     "silly standing in water.")]
            public bool allowInWater;
        }

        [Header("Identity")]
        public string id = "forest";
        public string displayName = "Forest";
        [TextArea(2, 4)] public string description = "";
        public Sprite icon;

        [Header("Unlock")]
        public CurrencyKind unlockCurrency = CurrencyKind.Coins;
        [Min(0)] public int unlockCost = 0;
        [Tooltip("Player level required before this biome can be purchased at all.")]
        [Min(1)] public int requiredLevel = 1;

        [Header("Terrain palette")]
        public Color groundLow = new(0.24f, 0.42f, 0.20f);
        public Color groundHigh = new(0.42f, 0.60f, 0.28f);
        public Color cliffColour = new(0.38f, 0.35f, 0.32f);
        [Tooltip("Vertical noise amplitude of the tile surface, in world units.")]
        [Range(0f, 1.5f)] public float reliefHeight = 0.28f;
        [Range(0.05f, 2f)] public float reliefScale = 0.45f;

        [Header("Water")]
        public bool hasWater = false;
        [Tooltip("Height of the water plane relative to the tile base.")]
        public float waterLevel = -0.12f;
        public Color waterShallow = new(0.35f, 0.75f, 0.78f, 0.75f);
        public Color waterDeep = new(0.07f, 0.28f, 0.42f, 0.95f);

        [Header("Lighting & atmosphere")]
        public Color sunTintDay = Color.white;
        public Color sunTintNight = new(0.42f, 0.52f, 0.85f);
        public Color fogColour = new(0.68f, 0.78f, 0.86f);
        [Range(0f, 0.3f)] public float fogDensity = 0.02f;
        [Tooltip("Extra ambient boost, useful to keep caves readable without washing them out.")]
        [Range(0f, 1f)] public float ambientLift = 0f;

        [Header("Scatter")]
        public ScatterEntry[] scatter = Array.Empty<ScatterEntry>();

        [Header("Simulation affordances")]
        [Tooltip("Food nodes (berry bushes, carcasses, crystals) spawned per tile.")]
        [Min(0)] public int foodNodes = 3;

        [Tooltip("Which diets the food in this biome satisfies.")]
        public Diet[] foodDiets = { Diet.Herbivore, Diet.Omnivore };

        [Tooltip("Nocturnal creatures placed here get this activity bonus. " +
                 "Graveyards make skeletons livelier.")]
        [Range(-1f, 1f)] public float nocturnalAffinity = 0f;

        [Tooltip("Multiplies coin generation for creatures that prefer this biome.")]
        [Min(0f)] public float comfortCoinMultiplier = 1.25f;

        [Header("Audio")]
        public string ambienceKey = "";

        void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(id)) id = name.ToLowerInvariant();
            if (unlockCost < 0) unlockCost = 0;
        }

        public bool Feeds(Diet diet)
        {
            if (foodDiets == null) return false;
            for (int i = 0; i < foodDiets.Length; i++)
            {
                if (foodDiets[i] == diet) return true;
                if (foodDiets[i] == Diet.Omnivore && (diet == Diet.Herbivore || diet == Diet.Carnivore)) return true;
            }
            return false;
        }
    }
}
