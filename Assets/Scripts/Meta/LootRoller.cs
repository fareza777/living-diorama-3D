using System;
using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Save;

namespace LivingDiorama.Meta
{
    /// <summary>
    /// Decides what comes out of a mystery box.
    ///
    /// Kept as a pure static with an injected RNG so the odds and the pity guarantees can
    /// be tested exhaustively -- box maths is exactly the sort of thing that has to be
    /// right and is impossible to eyeball from play.
    /// </summary>
    public static class LootRoller
    {
        public readonly struct Result
        {
            public readonly CreatureDefinition Creature;
            public readonly Rarity Rarity;
            public readonly bool IsNewSpecies;
            public readonly int EssenceAwarded;
            public readonly bool GrantedCopy;
            public readonly bool PityTriggered;

            public Result(CreatureDefinition creature, Rarity rarity, bool isNew,
                          int essence, bool grantedCopy, bool pity)
            {
                Creature = creature;
                Rarity = rarity;
                IsNewSpecies = isNew;
                EssenceAwarded = essence;
                GrantedCopy = grantedCopy;
                PityTriggered = pity;
            }

            public bool IsValid => Creature != null;
        }

        /// <summary>
        /// Roll one open and advance the box's pity counters.
        /// </summary>
        /// <param name="isDiscovered">Whether the player already owns a species.</param>
        public static Result Open(MysteryBoxDefinition box, SavedBox state,
                                  Func<string, bool> isDiscovered, Random rng)
        {
            if (box == null || box.pool == null || box.pool.Count == 0)
            {
                return default;
            }

            rng ??= new Random();
            isDiscovered ??= _ => false;

            bool pity = false;
            Rarity rarity;

            // Pity beats the weighted roll. Epic pity is checked first because it is the
            // stronger promise; hitting it also satisfies the rare guarantee.
            if (box.pityEpicAfter > 0 && state.sinceEpic + 1 >= box.pityEpicAfter)
            {
                rarity = HighestAvailableAtLeast(box, Rarity.Epic);
                pity = true;
            }
            else if (box.pityRareAfter > 0 && state.sinceRare + 1 >= box.pityRareAfter)
            {
                rarity = LowestAvailableAtLeast(box, Rarity.Rare);
                pity = true;
            }
            else
            {
                rarity = RollRarity(box, rng);
            }

            CreatureDefinition creature = PickFromPool(box, rarity, rng);
            if (creature == null) return default;

            // Counters track what actually came out, not what was requested, so a fallback
            // to a lower rarity does not silently consume the guarantee.
            Rarity actual = creature.rarity;
            state.opens++;
            state.sinceRare = actual >= Rarity.Rare ? 0 : state.sinceRare + 1;
            state.sinceEpic = actual >= Rarity.Epic ? 0 : state.sinceEpic + 1;

            bool isNew = !isDiscovered(creature.id);
            int essence = isNew ? Math.Max(0, (int)creature.discoveryEssence) : box.duplicateEssence;
            bool grantedCopy = isNew || box.duplicatesGrantCopy;

            return new Result(creature, actual, isNew, essence, grantedCopy, pity);
        }

        static Rarity RollRarity(MysteryBoxDefinition box, Random rng)
        {
            float total = box.TotalWeight;
            if (total <= 0f) return Rarity.Common;

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < box.weights.Count; i++)
            {
                float w = Math.Max(0f, box.weights[i].weight);
                if (roll < w) return box.weights[i].rarity;
                roll -= w;
            }
            return box.weights[^1].rarity;
        }

        /// <summary>Cheapest rarity in the pool that still honours the guarantee.</summary>
        static Rarity LowestAvailableAtLeast(MysteryBoxDefinition box, Rarity minimum)
        {
            Rarity best = minimum;
            bool found = false;

            for (int i = 0; i < box.pool.Count; i++)
            {
                CreatureDefinition c = box.pool[i];
                if (c == null || c.rarity < minimum) continue;
                if (!found || c.rarity < best)
                {
                    best = c.rarity;
                    found = true;
                }
            }

            // Nothing that good in the pool: give the best there is rather than nothing.
            return found ? best : HighestInPool(box);
        }

        static Rarity HighestAvailableAtLeast(MysteryBoxDefinition box, Rarity minimum)
        {
            Rarity best = minimum;
            bool found = false;

            for (int i = 0; i < box.pool.Count; i++)
            {
                CreatureDefinition c = box.pool[i];
                if (c == null || c.rarity < minimum) continue;
                if (!found || c.rarity > best)
                {
                    best = c.rarity;
                    found = true;
                }
            }

            return found ? best : HighestInPool(box);
        }

        static Rarity HighestInPool(MysteryBoxDefinition box)
        {
            Rarity best = Rarity.Common;
            for (int i = 0; i < box.pool.Count; i++)
            {
                if (box.pool[i] != null && box.pool[i].rarity > best) best = box.pool[i].rarity;
            }
            return best;
        }

        /// <summary>Uniform pick among pool members of the rolled rarity, stepping down
        /// then up if that rarity is not represented.</summary>
        static CreatureDefinition PickFromPool(MysteryBoxDefinition box, Rarity rarity, Random rng)
        {
            var candidates = new List<CreatureDefinition>(box.pool.Count);

            for (int step = 0; step < 10; step++)
            {
                // Search outward from the rolled rarity: exact, then one lower, then one
                // higher, and so on, so an under-populated tier degrades gracefully.
                int offset = (step + 1) / 2;
                int direction = (step % 2 == 0) ? -1 : 1;
                if (step == 0) { offset = 0; direction = 0; }

                var target = (Rarity)Math.Clamp((int)rarity + offset * direction, 0, (int)Rarity.Legendary);

                candidates.Clear();
                for (int i = 0; i < box.pool.Count; i++)
                {
                    if (box.pool[i] != null && box.pool[i].rarity == target) candidates.Add(box.pool[i]);
                }

                if (candidates.Count > 0) return candidates[rng.Next(candidates.Count)];
            }

            // Pool is not empty but nothing matched -- fall back to anything valid.
            for (int i = 0; i < box.pool.Count; i++)
            {
                if (box.pool[i] != null) return box.pool[i];
            }
            return null;
        }
    }
}
