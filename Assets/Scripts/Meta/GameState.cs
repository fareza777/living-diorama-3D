using System;
using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Save;
using UnityEngine;

namespace LivingDiorama.Meta
{
    /// <summary>
    /// The player's account: wallet, level, collection and which tiles they own.
    /// Wraps the raw save with validated operations and change events, so no other
    /// system ever writes to SaveData directly.
    /// </summary>
    public sealed class GameState
    {
        readonly GameDatabase _db;

        public SaveData Data { get; }

        public event Action WalletChanged;
        public event Action LevelChanged;
        public event Action CollectionChanged;
        public event Action TilesChanged;

        public GameState(SaveData data, GameDatabase db)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            _db = db;
        }

        // ---- wallet ---------------------------------------------------------

        public int Balance(CurrencyKind kind) => kind switch
        {
            CurrencyKind.Coins => Data.coins,
            CurrencyKind.Essence => Data.essence,
            CurrencyKind.BoxKeys => Data.boxKeys,
            _ => 0,
        };

        public bool CanAfford(CurrencyKind kind, int amount) => amount <= 0 || Balance(kind) >= amount;

        public bool TrySpend(CurrencyKind kind, int amount)
        {
            if (amount <= 0) return true;
            if (!CanAfford(kind, amount)) return false;

            switch (kind)
            {
                case CurrencyKind.Coins: Data.coins -= amount; break;
                case CurrencyKind.Essence: Data.essence -= amount; break;
                case CurrencyKind.BoxKeys: Data.boxKeys -= amount; break;
            }

            WalletChanged?.Invoke();
            return true;
        }

        // ---- the crate ------------------------------------------------------

        /// <summary>
        /// How many of a thing the player owns but has not put down yet.
        ///
        /// Two parallel lists rather than a dictionary because Unity's JsonUtility does
        /// not serialise dictionaries, and this has to survive being written to disk. The
        /// lists are short and only ever read through these three methods.
        /// </summary>
        public int StockOf(string placeableId)
        {
            int i = Data.stockIds.IndexOf(placeableId);
            return i < 0 ? 0 : Data.stockCounts[i];
        }

        public void AddStock(string placeableId, int amount)
        {
            int i = Data.stockIds.IndexOf(placeableId);
            if (i < 0)
            {
                Data.stockIds.Add(placeableId);
                Data.stockCounts.Add(Mathf.Max(0, amount));
            }
            else
            {
                Data.stockCounts[i] = Mathf.Max(0, Data.stockCounts[i] + amount);
            }

            StockChanged?.Invoke();
        }

        public bool TakeStock(string placeableId)
        {
            int i = Data.stockIds.IndexOf(placeableId);
            if (i < 0 || Data.stockCounts[i] <= 0) return false;

            Data.stockCounts[i]--;
            StockChanged?.Invoke();
            return true;
        }

        public event Action StockChanged;

        public void Grant(CurrencyKind kind, int amount)
        {
            if (amount <= 0) return;

            switch (kind)
            {
                case CurrencyKind.Coins:
                    Data.coins += amount;
                    Data.stats.totalCoinsEarned += amount;
                    break;
                case CurrencyKind.Essence: Data.essence += amount; break;
                case CurrencyKind.BoxKeys: Data.boxKeys += amount; break;
            }

            WalletChanged?.Invoke();
        }

        // ---- levels ---------------------------------------------------------

        public int XpForNextLevel => _db.progression.XpForLevel(Data.level);

        public float LevelProgress01 =>
            XpForNextLevel <= 0 ? 0f : Mathf.Clamp01((float)Data.xp / XpForNextLevel);

        /// <summary>Returns how many levels were gained, so the UI can celebrate.</summary>
        public int AddXp(int amount)
        {
            if (amount <= 0) return 0;

            Data.xp += amount;
            int gained = 0;

            while (Data.level < _db.progression.maxLevel && Data.xp >= XpForNextLevel)
            {
                Data.xp -= XpForNextLevel;
                Data.level++;
                gained++;
            }

            if (Data.level >= _db.progression.maxLevel) Data.xp = 0;

            if (gained > 0) LevelChanged?.Invoke();
            return gained;
        }

        // ---- collection -----------------------------------------------------

        public bool IsDiscovered(string speciesId) => Data.HasDiscovered(speciesId);

        public int DiscoveredCount => Data.discoveredSpecies.Count;

        public int SpeciesTotal => _db.CreatureCount;

        public int CopiesOf(string speciesId)
        {
            int n = 0;
            for (int i = 0; i < Data.creatures.Count; i++)
            {
                if (Data.creatures[i].speciesId == speciesId) n++;
            }
            return n;
        }

        public int PlacedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Data.creatures.Count; i++)
                {
                    if (Data.creatures[i].placed) n++;
                }
                return n;
            }
        }

        public int PopulationCap => _db.progression.PopulationCap(Data.tiles.Count);

        public bool HasRoomToPlace => PlacedCount < PopulationCap;

        /// <summary>Add a creature to the collection. Placed straight into the diorama when
        /// there is room, otherwise it waits in the collection screen.</summary>
        public SavedCreature GrantCreature(CreatureDefinition definition, Vector3 position, bool place)
        {
            if (definition == null) return null;

            var entry = new SavedCreature
            {
                instanceId = Guid.NewGuid().ToString("N")[..12],
                speciesId = definition.id,
                position = position,
                placed = place && HasRoomToPlace,
            };

            Data.creatures.Add(entry);

            if (!Data.HasDiscovered(definition.id))
            {
                Data.discoveredSpecies.Add(definition.id);
                AddXp(_db.progression.xpPerDiscovery);
            }

            CollectionChanged?.Invoke();
            return entry;
        }

        public SavedCreature FindCreature(string instanceId)
        {
            for (int i = 0; i < Data.creatures.Count; i++)
            {
                if (Data.creatures[i].instanceId == instanceId) return Data.creatures[i];
            }
            return null;
        }

        public bool SetPlaced(string instanceId, bool placed)
        {
            SavedCreature entry = FindCreature(instanceId);
            if (entry == null) return false;
            if (placed && !HasRoomToPlace) return false;

            entry.placed = placed;
            CollectionChanged?.Invoke();
            return true;
        }

        // ---- tiles ----------------------------------------------------------

        public bool OwnsTile(Vector2Int coord)
        {
            for (int i = 0; i < Data.tiles.Count; i++)
            {
                if (Data.tiles[i].x == coord.x && Data.tiles[i].y == coord.y) return true;
            }
            return false;
        }

        public int TileCost(Vector2Int coord) =>
            _db.progression.TileCost(Mathf.Max(Mathf.Abs(coord.x), Mathf.Abs(coord.y)));

        /// <summary>Buy a tile. Returns false when it is already owned, unaffordable, or
        /// the biome is still locked behind a level requirement.</summary>
        public bool TryUnlockTile(Vector2Int coord, BiomeDefinition biome)
        {
            if (biome == null || OwnsTile(coord)) return false;
            if (Data.level < biome.requiredLevel) return false;

            int cost = TileCost(coord) + biome.unlockCost;
            if (!TrySpend(CurrencyKind.Coins, cost)) return false;

            Data.tiles.Add(new SavedTile { x = coord.x, y = coord.y, biomeId = biome.id });
            Data.stats.tilesUnlocked++;
            AddXp(_db.progression.xpPerTileUnlock);

            TilesChanged?.Invoke();
            return true;
        }

        public void AddTileWithoutCharge(Vector2Int coord, BiomeDefinition biome)
        {
            if (biome == null || OwnsTile(coord)) return;
            Data.tiles.Add(new SavedTile { x = coord.x, y = coord.y, biomeId = biome.id });
            TilesChanged?.Invoke();
        }

        public IReadOnlyList<SavedTile> Tiles => Data.tiles;

        // ---- new game -------------------------------------------------------

        public static SaveData CreateNewGame(GameDatabase db, int seed)
        {
            ProgressionSettings p = db.progression;

            var data = new SaveData
            {
                worldSeed = seed,
                coins = p.startingCoins,
                essence = p.startingEssence,
                boxKeys = p.startingBoxKeys,
                clockHours = 7.0,
                lastSeenUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };
            data.stats.firstPlayedUnix = data.lastSeenUnix;

            // Something to build with on day one. A bed and a bowl are the two a creature
            // needs to be looked after at all, so they are a starting kit rather than a
            // reward -- the game cannot teach what building is for without them.
            data.stockIds.Add("bed");
            data.stockCounts.Add(1);
            data.stockIds.Add("food_bowl");
            data.stockCounts.Add(1);

            BiomeDefinition starting = db.biomes.Count > 0 ? db.biomes[0] : null;
            int half = p.startingTilesPerSide / 2;

            for (int x = 0; x < p.startingTilesPerSide; x++)
            {
                for (int y = 0; y < p.startingTilesPerSide; y++)
                {
                    data.tiles.Add(new SavedTile
                    {
                        x = x - half,
                        y = y - half,
                        biomeId = starting != null ? starting.id : "forest",
                    });
                }
            }

            foreach (ProgressionSettings.StartingGrant grant in p.startingCreatures)
            {
                CreatureDefinition def = db.GetCreature(grant.creatureId);
                if (def == null) continue;

                for (int i = 0; i < grant.count; i++)
                {
                    data.creatures.Add(new SavedCreature
                    {
                        instanceId = Guid.NewGuid().ToString("N")[..12],
                        speciesId = def.id,
                        position = Vector3.zero,
                        placed = true,
                    });
                }

                if (!data.discoveredSpecies.Contains(def.id)) data.discoveredSpecies.Add(def.id);
            }

            return data;
        }
    }
}
