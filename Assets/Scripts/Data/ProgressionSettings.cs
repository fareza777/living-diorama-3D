using System;
using UnityEngine;

namespace LivingDiorama.Data
{
    /// <summary>
    /// Expansion costs, level curve and ad pacing. The diorama grows outward in rings,
    /// so the cost formula only needs to know which ring a tile sits in.
    /// </summary>
    [CreateAssetMenu(menuName = "Living Diorama/Progression Settings", fileName = "ProgressionSettings")]
    public sealed class ProgressionSettings : ScriptableObject
    {
        [Serializable]
        public struct StartingGrant
        {
            public string creatureId;
            [Min(1)] public int count;
        }

        [Header("Diorama grid")]
        [Tooltip("World size of one diorama tile.")]
        [Min(1f)] public float tileSize = 6f;

        [Tooltip("Tiles unlocked at the very start, as a square of this many per side.")]
        [Range(1, 3)] public int startingTilesPerSide = 1;

        [Tooltip("Hard cap on grid radius so the camera and budget stay sane.")]
        [Range(1, 6)] public int maxRingRadius = 4;

        [Header("Expansion cost")]
        [Min(0)] public int baseTileCost = 250;

        [Tooltip("Each ring further out multiplies the cost by this.")]
        [Min(1f)] public float tileCostRingMultiplier = 1.85f;

        [Header("Population")]
        [Tooltip("Creatures allowed per unlocked tile. Keeps the frame budget bounded and "
                 + "gives expansion a mechanical reason to exist beyond looking nice.")]
        [Range(1, 12)] public int creaturesPerTile = 5;

        [Tooltip("Absolute ceiling regardless of tiles owned.")]
        [Range(4, 80)] public int hardPopulationCap = 40;

        [Header("Level curve")]
        [Min(1)] public int baseXpPerLevel = 120;
        [Min(1f)] public float xpCurveExponent = 1.35f;
        [Min(1)] public int maxLevel = 60;

        [Header("XP awards")]
        [Min(0)] public int xpPerDiscovery = 40;
        [Min(0)] public int xpPerBoxOpen = 8;
        [Min(0)] public int xpPerInteraction = 2;
        [Min(0)] public int xpPerTileUnlock = 60;

        [Header("Starting loadout")]
        [Min(0)] public int startingCoins = 350;
        [Min(0)] public int startingEssence = 0;
        [Min(0)] public int startingBoxKeys = 1;
        public StartingGrant[] startingCreatures = Array.Empty<StartingGrant>();

        [Header("Ads")]
        [Tooltip("Interstitials are deliberately rare -- this is a cosy watch-the-world game "
                 + "and aggressive ads would break the mood.")]
        [Min(0)] public int interstitialEveryNBoxOpens = 6;

        [Min(0)] public int interstitialMinSecondsBetween = 240;

        [Tooltip("Coins granted for a rewarded ad view.")]
        [Min(0)] public int rewardedAdCoins = 150;

        [Tooltip("Multiplier applied to accumulated offline earnings when the player watches "
                 + "a rewarded ad on the welcome-back screen.")]
        [Min(1f)] public float rewardedOfflineMultiplier = 2f;

        /// <summary>Cost of the next tile in the given ring (ring 0 is the origin tile).</summary>
        public int TileCost(int ring)
        {
            if (ring <= 0) return 0;
            double cost = baseTileCost * Math.Pow(tileCostRingMultiplier, ring - 1);
            return (int)Math.Round(cost / 10.0) * 10;
        }

        /// <summary>Total XP required to go from <paramref name="level"/> to the next one.</summary>
        public int XpForLevel(int level)
        {
            if (level < 1) level = 1;
            double xp = baseXpPerLevel * Math.Pow(level, xpCurveExponent);
            return (int)Math.Round(xp / 5.0) * 5;
        }

        public int PopulationCap(int unlockedTiles)
        {
            return Mathf.Min(hardPopulationCap, Mathf.Max(1, unlockedTiles) * creaturesPerTile);
        }
    }
}
