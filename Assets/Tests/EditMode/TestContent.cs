using System.Collections.Generic;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// In-memory content for the tests.
    ///
    /// Built here rather than loaded from the shipped assets on purpose: a test that
    /// fails because someone retuned the goblin is a test that teaches the team to
    /// ignore failures. These fixtures only ever change when the test's intent does.
    /// </summary>
    public static class TestContent
    {
        public static CreatureDefinition Creature(
            string id, Rarity rarity = Rarity.Common,
            string[] tags = null,
            BodySize size = BodySize.Small,
            float bravery = 0.5f,
            float aggression = 0.5f,
            float coinsPerHour = 10f,
            float discoveryEssence = 20f)
        {
            var creature = ScriptableObject.CreateInstance<CreatureDefinition>();
            creature.id = id;
            creature.displayName = id;
            creature.rarity = rarity;
            creature.tags = tags ?? System.Array.Empty<string>();
            creature.bodySize = size;
            creature.bravery = bravery;
            creature.aggression = aggression;
            creature.coinsPerHour = coinsPerHour;
            creature.discoveryEssence = discoveryEssence;
            creature.hungerRate = 0.1f;
            creature.socialRate = 0.05f;
            return creature;
        }

        public static MysteryBoxDefinition Box(
            string id,
            IEnumerable<CreatureDefinition> pool,
            IEnumerable<(Rarity rarity, float weight)> weights = null,
            int pityRareAfter = 0,
            int pityEpicAfter = 0,
            int duplicateEssence = 10,
            bool duplicatesGrantCopy = true)
        {
            var box = ScriptableObject.CreateInstance<MysteryBoxDefinition>();
            box.id = id;
            box.displayName = id;
            box.pool = new List<CreatureDefinition>(pool);
            box.pityRareAfter = pityRareAfter;
            box.pityEpicAfter = pityEpicAfter;
            box.duplicateEssence = duplicateEssence;
            box.duplicatesGrantCopy = duplicatesGrantCopy;

            box.weights = new List<MysteryBoxDefinition.RarityWeight>();
            if (weights == null)
            {
                box.weights.Add(new MysteryBoxDefinition.RarityWeight
                {
                    rarity = Rarity.Common,
                    weight = 1f,
                });
            }
            else
            {
                foreach ((Rarity rarity, float weight) in weights)
                {
                    box.weights.Add(new MysteryBoxDefinition.RarityWeight
                    {
                        rarity = rarity,
                        weight = weight,
                    });
                }
            }

            return box;
        }

        public static SimulationSettings Simulation()
        {
            var settings = ScriptableObject.CreateInstance<SimulationSettings>();
            settings.minutesPerDay = 12f;
            settings.dawn = 0.25f;
            settings.dusk = 0.78f;
            settings.offlineCapHours = 8f;
            settings.offlineEfficiency = 0.5f;
            return settings;
        }

        public static ProgressionSettings Progression()
        {
            var settings = ScriptableObject.CreateInstance<ProgressionSettings>();
            settings.baseTileCost = 250;
            settings.tileCostRingMultiplier = 1.85f;
            settings.creaturesPerTile = 5;
            settings.hardPopulationCap = 32;
            settings.baseXpPerLevel = 120;
            settings.xpCurveExponent = 1.35f;
            settings.maxLevel = 60;
            return settings;
        }

        public static GameDatabase Database(params CreatureDefinition[] creatures)
        {
            var db = ScriptableObject.CreateInstance<GameDatabase>();
            db.creatures = new List<CreatureDefinition>(creatures);
            db.simulation = Simulation();
            db.progression = Progression();
            db.relations = ScriptableObject.CreateInstance<RelationRuleSet>();
            db.BuildIndex();
            return db;
        }

        public static RelationRuleSet.Rule Rule(string selfTag, string otherTag, Stance stance,
                                                int priority, float minAggression = 0f,
                                                float minBravery = 0f) => new()
        {
            selfTag = selfTag,
            otherTag = otherTag,
            stance = stance,
            priority = priority,
            minAggression = minAggression,
            minBravery = minBravery,
        };
    }
}
