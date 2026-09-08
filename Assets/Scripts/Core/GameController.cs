using System;
using System.Collections.Generic;
using LivingDiorama.Ads;
using LivingDiorama.Data;
using LivingDiorama.Diorama;
using LivingDiorama.Meta;
using LivingDiorama.Save;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Core
{
    /// <summary>
    /// The application service layer. Every player action goes through here, which keeps
    /// the UI free of rules and means the whole game loop can be driven from a test or a
    /// debug console without a single button existing.
    /// </summary>
    public sealed class GameController
    {
        readonly GameDatabase _db;
        readonly GameState _state;
        readonly DioramaWorld _world;
        readonly EcosystemSimulation _sim;
        readonly AdPacing _pacing;
        readonly System.Random _rng;

        public GameState State => _state;
        public GameDatabase Database => _db;
        public DioramaWorld World => _world;
        public EcosystemSimulation Simulation => _sim;

        public event Action<LootRoller.Result> BoxOpened;
        public event Action<Vector2Int, BiomeDefinition> TileUnlocked;
        public event Action SaveRequested;

        public GameController(GameDatabase db, GameState state, DioramaWorld world,
                              EcosystemSimulation sim, AdPacing pacing, System.Random rng = null)
        {
            _db = db;
            _state = state;
            _world = world;
            _sim = sim;
            _pacing = pacing;
            _rng = rng ?? new System.Random();
        }

        // ---- boxes ----------------------------------------------------------

        public enum OpenFailure
        {
            None,
            UnknownBox,
            CannotAfford,
            EmptyPool,
            AdNotReady,
            OnCooldown,
        }

        public bool CanAffordBox(MysteryBoxDefinition box) =>
            box != null && _state.CanAfford(box.costCurrency, box.cost);

        public bool IsFreeOpenAvailable(MysteryBoxDefinition box)
        {
            if (box == null || !box.rewardedAdEligible) return false;
            SavedBox saved = _state.Data.BoxState(box.id);
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= saved.nextFreeUnix;
        }

        public int FreeOpenSecondsRemaining(MysteryBoxDefinition box)
        {
            if (box == null) return 0;
            SavedBox saved = _state.Data.BoxState(box.id);
            long remaining = saved.nextFreeUnix - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return (int)Math.Max(0, remaining);
        }

        /// <summary>
        /// Open a box, paying with currency. The reward is granted and, when there is room,
        /// the creature walks straight into the diorama.
        /// </summary>
        public OpenFailure TryOpenBox(MysteryBoxDefinition box, out LootRoller.Result result)
        {
            result = default;
            if (box == null) return OpenFailure.UnknownBox;
            if (box.pool == null || box.pool.Count == 0) return OpenFailure.EmptyPool;
            if (!_state.TrySpend(box.costCurrency, box.cost)) return OpenFailure.CannotAfford;

            result = RollAndGrant(box);
            return result.IsValid ? OpenFailure.None : OpenFailure.EmptyPool;
        }

        /// <summary>Open a box for free after a rewarded ad. Starts the cooldown.</summary>
        public void OpenBoxWithAd(MysteryBoxDefinition box, Action<OpenFailure, LootRoller.Result> onFinished)
        {
            if (box == null || !box.rewardedAdEligible)
            {
                onFinished?.Invoke(OpenFailure.UnknownBox, default);
                return;
            }

            if (!IsFreeOpenAvailable(box))
            {
                onFinished?.Invoke(OpenFailure.OnCooldown, default);
                return;
            }

            AdHub.Current.ShowRewarded(AdPlacement.FreeBoxOpen, earned =>
            {
                if (!earned)
                {
                    onFinished?.Invoke(OpenFailure.AdNotReady, default);
                    return;
                }

                SavedBox saved = _state.Data.BoxState(box.id);
                saved.nextFreeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                                     + box.rewardedAdCooldownSeconds;

                LootRoller.Result rolled = RollAndGrant(box);
                onFinished?.Invoke(rolled.IsValid ? OpenFailure.None : OpenFailure.EmptyPool, rolled);
            });
        }

        LootRoller.Result RollAndGrant(MysteryBoxDefinition box)
        {
            SavedBox saved = _state.Data.BoxState(box.id);
            LootRoller.Result result = LootRoller.Open(box, saved, _state.IsDiscovered, _rng);
            if (!result.IsValid) return result;

            if (result.EssenceAwarded > 0) _state.Grant(CurrencyKind.Essence, result.EssenceAwarded);

            if (result.GrantedCopy)
            {
                Vector3 spawn = SpawnPoint();
                SavedCreature entry = _state.GrantCreature(result.Creature, spawn, true);
                if (entry != null && entry.placed) SpawnAgent(entry, result.Creature);
            }
            else if (!_state.IsDiscovered(result.Creature.id))
            {
                // Duplicates-as-essence boxes still have to register the discovery.
                _state.Data.discoveredSpecies.Add(result.Creature.id);
            }

            _state.AddXp(_db.progression.xpPerBoxOpen);
            _state.Data.stats.boxesOpened++;

            BoxOpened?.Invoke(result);
            RequestSave();

            if (_pacing != null && _pacing.ShouldShowInterstitialAfterBoxOpen())
            {
                AdHub.Current.ShowInterstitial(null);
            }

            return result;
        }

        // ---- creatures ------------------------------------------------------

        Vector3 SpawnPoint()
        {
            if (_world == null || _world.TileCount == 0) return Vector3.zero;

            // Drop new arrivals near the middle of the diorama so the player sees them.
            Bounds bounds = _world.WorldBounds;
            return _world.RandomPoint(new Vector3(bounds.center.x, 0f, bounds.center.z),
                                      _world.TileSize * 0.4f);
        }

        public CreatureAgent SpawnAgent(SavedCreature entry, CreatureDefinition definition = null)
        {
            if (entry == null || _sim == null) return null;

            definition ??= _db.GetCreature(entry.speciesId);
            if (definition == null)
            {
                Debug.LogError($"[GameController] save references unknown species '{entry.speciesId}'");
                return null;
            }

            Vector3 position = entry.position;
            if (_world != null && !_world.Contains(position)) position = SpawnPoint();

            CreatureAgent agent = _sim.Spawn(definition, entry.instanceId, position);
            if (agent == null) return null;

            agent.Fullness = entry.fullness;
            agent.Energy = entry.energy;
            agent.Social = entry.social;
            agent.Fun = entry.fun;
            return agent;
        }

        public bool PlaceFromCollection(string instanceId)
        {
            if (!_state.HasRoomToPlace) return false;
            if (!_state.SetPlaced(instanceId, true)) return false;

            SavedCreature entry = _state.FindCreature(instanceId);
            entry.position = SpawnPoint();
            SpawnAgent(entry);
            RequestSave();
            return true;
        }

        public bool RecallToCollection(string instanceId)
        {
            CreatureAgent agent = _sim.FindByInstanceId(instanceId);
            if (agent != null)
            {
                CaptureState(agent);
                _sim.Despawn(agent);
            }

            bool ok = _state.SetPlaced(instanceId, false);
            if (ok) RequestSave();
            return ok;
        }

        /// <summary>Copy live simulation state back into the save model.</summary>
        public void CaptureState(CreatureAgent agent)
        {
            SavedCreature entry = _state.FindCreature(agent.InstanceId);
            if (entry == null) return;

            entry.position = agent.Position;
            entry.fullness = agent.Fullness;
            entry.energy = agent.Energy;
            entry.social = agent.Social;
            entry.fun = agent.Fun;
        }

        public void CaptureAll()
        {
            IReadOnlyList<CreatureAgent> agents = _sim.Agents;
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null && agents[i].IsActive) CaptureState(agents[i]);
            }
            _state.Data.clockHours = _sim.Clock.TotalHours;
        }

        // ---- tiles ----------------------------------------------------------

        public int TileCost(Vector2Int coord, BiomeDefinition biome) =>
            _state.TileCost(coord) + (biome != null ? biome.unlockCost : 0);

        public bool CanUnlock(Vector2Int coord, BiomeDefinition biome)
        {
            if (biome == null || _state.OwnsTile(coord)) return false;
            if (_state.Data.level < biome.requiredLevel) return false;
            return _state.CanAfford(CurrencyKind.Coins, TileCost(coord, biome));
        }

        public bool TryUnlockTile(Vector2Int coord, BiomeDefinition biome)
        {
            if (!_state.TryUnlockTile(coord, biome)) return false;

            DioramaTile tile = _world.BuildTile(coord, biome);
            RegisterTileFood(tile);

            TileUnlocked?.Invoke(coord, biome);
            RequestSave();
            return true;
        }

        public void RegisterTileFood(DioramaTile tile)
        {
            if (tile == null) return;
            for (int i = 0; i < tile.Food.Count; i++) _sim.RegisterFood(tile.Food[i]);
        }

        public List<Vector2Int> AvailableCoords(List<Vector2Int> buffer) =>
            _world.AvailableCoords(buffer);

        // ---- persistence ----------------------------------------------------

        public void RequestSave() => SaveRequested?.Invoke();
    }
}
