using System.Collections.Generic;
using LivingDiorama.Data;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// The social rules.
    ///
    /// Every "creature A does something to creature B" moment in the design comes out of
    /// this one function, so these tests are really a specification of the game's premise:
    /// the wolf hunts the goblin because of what they are, not because anyone wrote
    /// wolf-versus-goblin anywhere.
    /// </summary>
    public sealed class RelationRuleSetTests
    {
        static RelationRuleSet Rules(params RelationRuleSet.Rule[] rules)
        {
            var set = ScriptableObject.CreateInstance<RelationRuleSet>();
            set.defaultStance = Stance.Neutral;
            set.intimidationSizeGap = 2;
            set.rules = new List<RelationRuleSet.Rule>(rules);
            set.overrides.Clear();
            return set;
        }

        [Test]
        public void Resolve_NoMatchingRule_ReturnsTheDefault()
        {
            RelationRuleSet rules = Rules();
            CreatureDefinition a = TestContent.Creature("a");
            CreatureDefinition b = TestContent.Creature("b");

            Assert.AreEqual(Stance.Neutral, rules.Resolve(a, b));
        }

        [Test]
        public void Resolve_PredatorHuntsMischief_WithoutNamingEitherSpecies()
        {
            RelationRuleSet rules = Rules(
                TestContent.Rule("predator", "mischief", Stance.Predatory, 10));

            CreatureDefinition wolf = TestContent.Creature("wolf", tags: new[] { "beast", "predator" });
            CreatureDefinition goblin = TestContent.Creature("goblin", tags: new[] { "humanoid", "mischief" });

            Assert.AreEqual(Stance.Predatory, rules.Resolve(wolf, goblin));
            Assert.AreEqual(Stance.Neutral, rules.Resolve(goblin, wolf),
                "the rule is directional; the goblin needs its own rule to be afraid");
        }

        [Test]
        public void Resolve_HighestPriorityRuleWins()
        {
            RelationRuleSet rules = Rules(
                TestContent.Rule("lawful", "mischief", Stance.Wary, 5),
                TestContent.Rule("lawful", "thief", Stance.Hostile, 40));

            CreatureDefinition knight = TestContent.Creature("knight",
                tags: new[] { "lawful" }, aggression: 1f);
            CreatureDefinition thief = TestContent.Creature("goblin",
                tags: new[] { "mischief", "thief" });

            Assert.AreEqual(Stance.Hostile, rules.Resolve(knight, thief));
        }

        [Test]
        public void Resolve_RuntimeTagsCountAsTags()
        {
            // This is the mechanism behind the theft beat: stealing attaches a transient
            // "thief" tag, and the knight's existing rule does the rest.
            RelationRuleSet rules = Rules(
                TestContent.Rule("lawful", "thief", Stance.Hostile, 40));

            CreatureDefinition knight = TestContent.Creature("knight", tags: new[] { "lawful" });
            CreatureDefinition goblin = TestContent.Creature("goblin", tags: new[] { "mischief" });

            Assert.AreEqual(Stance.Neutral, rules.Resolve(knight, goblin),
                "before the theft the knight has no quarrel");

            Stance afterTheft = rules.Resolve(knight, null, goblin, new[] { "thief" });
            Assert.AreEqual(Stance.Hostile, afterTheft);
        }

        [Test]
        public void Resolve_AggressionGate_SuppressesRulesForTimidCreatures()
        {
            RelationRuleSet rules = Rules(
                TestContent.Rule("lawful", "mischief", Stance.Hostile, 14, minAggression: 0.4f));

            CreatureDefinition timid = TestContent.Creature("scholar",
                tags: new[] { "lawful" }, aggression: 0.1f);
            CreatureDefinition fierce = TestContent.Creature("knight",
                tags: new[] { "lawful" }, aggression: 0.8f);
            CreatureDefinition goblin = TestContent.Creature("goblin", tags: new[] { "mischief" });

            Assert.AreEqual(Stance.Neutral, rules.Resolve(timid, goblin));
            Assert.AreEqual(Stance.Hostile, rules.Resolve(fierce, goblin));
        }

        [Test]
        public void Resolve_MuchLargerCreature_IsFearedWithoutAnyRule()
        {
            RelationRuleSet rules = Rules();

            CreatureDefinition slime = TestContent.Creature("slime", size: BodySize.Tiny, bravery: 0.2f);
            CreatureDefinition dragon = TestContent.Creature("dragon", size: BodySize.Huge);

            Assert.AreEqual(Stance.Fearful, rules.Resolve(slime, dragon));
        }

        [Test]
        public void Resolve_BraveCreature_IsWaryOfGiantsRatherThanTerrified()
        {
            RelationRuleSet rules = Rules();

            CreatureDefinition knight = TestContent.Creature("knight", size: BodySize.Medium, bravery: 0.95f);
            CreatureDefinition dragon = TestContent.Creature("dragon", size: BodySize.Huge);

            Assert.AreEqual(Stance.Wary, rules.Resolve(knight, dragon));
        }

        [Test]
        public void Resolve_SizeIntimidation_DoesNotOverrideAggression()
        {
            RelationRuleSet rules = Rules(
                TestContent.Rule("hunter", "", Stance.Predatory, 10));

            CreatureDefinition hunter = TestContent.Creature("hunter",
                tags: new[] { "hunter" }, size: BodySize.Tiny, bravery: 0.1f);
            CreatureDefinition giant = TestContent.Creature("giant", size: BodySize.Huge);

            Assert.AreEqual(Stance.Predatory, rules.Resolve(hunter, giant),
                "a creature that hunts everything should not be talked out of it by size");
        }

        [Test]
        public void Resolve_SpeciesOverride_BeatsEveryRule()
        {
            var rules = Rules(TestContent.Rule("predator", "critter", Stance.Predatory, 100));
            rules.overrides.Add(new RelationRuleSet.Override
            {
                selfSpeciesId = "wolf",
                otherSpeciesId = "slime",
                stance = Stance.Friendly,
            });

            CreatureDefinition wolf = TestContent.Creature("wolf", tags: new[] { "predator" });
            CreatureDefinition slime = TestContent.Creature("slime", tags: new[] { "critter" });

            Assert.AreEqual(Stance.Friendly, rules.Resolve(wolf, slime));
        }

        [Test]
        public void StanceHelpers_ClassifyCorrectly()
        {
            Assert.IsTrue(RelationRuleSet.IsAggressive(Stance.Hostile));
            Assert.IsTrue(RelationRuleSet.IsAggressive(Stance.Predatory));
            Assert.IsFalse(RelationRuleSet.IsAggressive(Stance.Wary));

            Assert.IsTrue(RelationRuleSet.IsAvoidant(Stance.Fearful));
            Assert.IsTrue(RelationRuleSet.IsAvoidant(Stance.Wary));
            Assert.IsFalse(RelationRuleSet.IsAvoidant(Stance.Friendly));
        }

        [Test]
        public void Resolve_EmptySelfTag_MatchesAnyObserver()
        {
            RelationRuleSet rules = Rules(
                TestContent.Rule("", "apex", Stance.Fearful, 18));

            CreatureDefinition anyone = TestContent.Creature("anyone");
            CreatureDefinition dragon = TestContent.Creature("dragon", tags: new[] { "apex" });

            Assert.AreEqual(Stance.Fearful, rules.Resolve(anyone, dragon));
        }
    }
}
