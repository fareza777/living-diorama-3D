using System;
using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Meta
{
    /// <summary>
    /// Turns the ecosystem into income.
    ///
    /// Two streams: a steady trickle from contented creatures, and a bonus whenever
    /// something worth watching happens. The second is the important one -- it is what
    /// rewards the player for building a diorama where creatures actually interact,
    /// rather than one stuffed with whatever has the highest coin stat.
    /// </summary>
    public sealed class EconomyService : MonoBehaviour
    {
        readonly Dictionary<long, double> _pairCooldowns = new(64);

        GameState _state;
        GameDatabase _db;
        EcosystemSimulation _sim;
        double _coinFraction;

        /// <summary>Coins, world position. The HUD spawns a floating "+N" from this.</summary>
        public event Action<int, Vector3> CoinsPopped;

        /// <summary>Notable moments, for the toast feed.</summary>
        public event Action<SimEvent> MomentOccurred;

        public void Initialise(GameState state, GameDatabase db, EcosystemSimulation sim)
        {
            _state = state;
            _db = db;
            _sim = sim;
            SimEventBus.Raised += OnSimEvent;
        }

        void OnDestroy()
        {
            SimEventBus.Raised -= OnSimEvent;
        }

        // ---- event rewards --------------------------------------------------

        void OnSimEvent(SimEvent e)
        {
            if (!e.IsNotable || _state == null) return;

            MomentOccurred?.Invoke(e);

            if (!TryClaimPair(e)) return;

            int coins = _db.simulation.interactionCoinReward;
            coins = Mathf.RoundToInt(coins * RarityBonus(e));

            _state.Grant(CurrencyKind.Coins, coins);
            _state.AddXp(_db.progression.xpPerInteraction);
            _state.Data.stats.interactionsWitnessed++;

            CoinsPopped?.Invoke(coins, e.Position);
        }

        /// <summary>Rarer creatures produce more valuable moments, which gives the
        /// collection an economic point beyond completionism.</summary>
        float RarityBonus(in SimEvent e)
        {
            int rarity = 0;
            if (e.Actor != null) rarity = Mathf.Max(rarity, (int)e.Actor.Definition.rarity);
            if (e.Target != null) rarity = Mathf.Max(rarity, (int)e.Target.Definition.rarity);
            return 1f + rarity * 0.45f;
        }

        /// <summary>
        /// Rate limit per pair of creatures. Without this, two creatures that dislike each
        /// other would bicker in a corner and print money forever.
        /// </summary>
        bool TryClaimPair(in SimEvent e)
        {
            long key = PairKey(e.Actor, e.Target, e.Kind);
            double now = _sim.Clock.TotalHours;
            double cooldownHours = _db.simulation.rewardCooldownSeconds * _sim.Clock.HoursPerRealSecond;

            if (_pairCooldowns.TryGetValue(key, out double readyAt) && now < readyAt) return false;

            _pairCooldowns[key] = now + cooldownHours;
            return true;
        }

        static long PairKey(CreatureAgent a, CreatureAgent b, SimEventKind kind)
        {
            int ha = a != null ? a.InstanceId.GetHashCode() : 0;
            int hb = b != null ? b.InstanceId.GetHashCode() : 0;
            // Order-independent so A-chases-B and B-flees-A share one cooldown.
            if (ha > hb) (ha, hb) = (hb, ha);
            return ((long)ha << 32) ^ (uint)(hb ^ ((int)kind << 24));
        }

        // ---- idle income ----------------------------------------------------

        void Update()
        {
            if (_state == null || _sim == null || _sim.Paused) return;

            double hours = Time.deltaTime * _sim.Clock.HoursPerRealSecond;
            _coinFraction += CoinsPerHour() * hours;

            if (_coinFraction >= 1.0)
            {
                var whole = (int)_coinFraction;
                _coinFraction -= whole;
                _state.Grant(CurrencyKind.Coins, whole);
            }
        }

        /// <summary>Live earning rate, also shown in the HUD so the player can see the
        /// effect of keeping their creatures happy.</summary>
        public float CoinsPerHour()
        {
            if (_sim == null) return 0f;

            float total = 0f;
            IReadOnlyList<CreatureAgent> agents = _sim.Agents;

            for (int i = 0; i < agents.Count; i++)
            {
                CreatureAgent a = agents[i];
                if (a == null || !a.IsActive) continue;

                float rate = a.Definition.coinsPerHour;

                // Miserable creatures earn almost nothing; content ones earn full rate.
                rate *= Mathf.Lerp(0.15f, 1f, a.Wellbeing);

                // And a creature in a biome it actually likes earns a premium.
                BiomeDefinition biome = _sim.Surface?.BiomeAt(a.Position);
                if (biome != null && a.Definition.PrefersBiome(biome.id))
                {
                    rate *= biome.comfortCoinMultiplier;
                }

                total += rate;
            }

            return total;
        }
    }
}
