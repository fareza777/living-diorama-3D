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
