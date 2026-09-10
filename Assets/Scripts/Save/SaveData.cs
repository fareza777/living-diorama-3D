using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Save
{
    /// <summary>
    /// The entire persisted state of a player's diorama.
    ///
    /// Everything is plain serialisable fields with string ids rather than asset
    /// references, so content can be re-authored, re-ordered or replaced without
    /// invalidating a save. Lists rather than dictionaries because JsonUtility cannot
    /// serialise the latter.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>Bump when the shape changes; SaveMigrations handles the upgrade.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        [Header("World")]
        public int worldSeed;
        public double clockHours = 6.0;
        public long lastSeenUnix;

        [Header("Wallet")]
        public int coins;
        public int essence;
        public int boxKeys;

        [Header("Progression")]
        public int level = 1;
        public int xp;

        public List<SavedTile> tiles = new();
        public List<SavedCreature> creatures = new();

        /// <summary>Furniture the player built, and what is left in the crate.
        /// Both are progress, so both survive a reinstall.</summary>
        public List<SavedPlacement> placements = new();
        public List<string> stockIds = new();
        public List<int> stockCounts = new();
        public List<string> discoveredSpecies = new();

        /// <summary>Moments the player has actually witnessed, by id. The Chronicle is
        /// progress in its own right, so it has to survive a reinstall like anything
        /// else the player earned.</summary>
        public List<string> witnessedMoments = new();
        public List<SavedBox> boxes = new();
        public SavedStats stats = new();

        public bool HasDiscovered(string speciesId) => discoveredSpecies.Contains(speciesId);

        public SavedBox BoxState(string boxId)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].boxId == boxId) return boxes[i];
            }
            var created = new SavedBox { boxId = boxId };
            boxes.Add(created);
            return created;
        }
    }

    [Serializable]
    public sealed class SavedTile
    {
        public int x;
        public int y;
        public string biomeId;
    }

    [Serializable]
    public sealed class SavedCreature
    {
        public string instanceId;
        public string speciesId;

        /// <summary>Where it was standing when the game closed. Restored so reopening the
        /// app feels like walking back into the same room rather than a reset.</summary>
        public Vector3 position;

        [Range(0f, 1f)] public float fullness = 1f;
        [Range(0f, 1f)] public float energy = 1f;
        [Range(0f, 1f)] public float social = 0.7f;
        [Range(0f, 1f)] public float fun = 0.7f;

        /// <summary>False while the creature sits in the collection rather than the diorama.</summary>
        public bool placed = true;

        public string nickname = "";

        /// <summary>
        /// How far along it is from newborn to grown, 0 to 1.
        ///
        /// Continuous rather than a stage index, because a creature that changes three
        /// times in three weeks is invisible on any given day, and a player who sees
        /// nothing happen for five days stops opening the app. It grows a little every
        /// day; Baby, Young and Adult are thresholds crossed along the way, not the
        /// thing being stored.
        /// </summary>
        [Range(0f, 1f)] public float growth;

        /// <summary>Unix day the growth allowance below belongs to. Growth is capped per
        /// real day, so the only way forward is to come back tomorrow -- which is the
        /// whole point of raising something.</summary>
        public long growthDay;

        /// <summary>How much of today's allowance has already been earned.</summary>
        public float growthToday;
    }

    /// <summary>One thing the player built, and where they put it.</summary>
    [Serializable]
    public sealed class SavedPlacement
    {
        public string id;
        public string placeableId;
        public Vector3 position;
        public float yaw;
    }

    [Serializable]
    public sealed class SavedBox
    {
        public string boxId;
        public int opens;

        /// <summary>Opens since the last Rare-or-better, for the pity guarantee.</summary>
        public int sinceRare;
        public int sinceEpic;

        /// <summary>Unix seconds when the free rewarded-ad open becomes available again.</summary>
        public long nextFreeUnix;
    }

    [Serializable]
    public sealed class SavedStats
    {
        public int boxesOpened;
        public int interactionsWitnessed;
        public int tilesUnlocked;
        public int totalCoinsEarned;
        public long firstPlayedUnix;
    }
}
