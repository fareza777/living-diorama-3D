using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Meta;
using LivingDiorama.Save;
using NUnit.Framework;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// Box odds and pity guarantees.
    ///
    /// This is the part of the game a player cannot verify by playing, so it is the part
    /// that has to be verified here. The rolls use a seeded Random so a failure is
    /// reproducible rather than "it happened once on CI".
    /// </summary>
    public sealed class LootRollerTests
    {
        static SavedBox NewState(string boxId = "box") => new() { boxId = boxId };

        [Test]
        public void Open_EmptyPool_ReturnsInvalidResult()
        {
            MysteryBoxDefinition box = TestContent.Box("box", new List<CreatureDefinition>());

            LootRoller.Result result = LootRoller.Open(box, NewState(), _ => false, new System.Random(1));

            Assert.IsFalse(result.IsValid, "an empty box must not produce a creature");
        }

        [Test]
        public void Open_AlwaysReturnsACreatureFromThePool()
        {
            CreatureDefinition common = TestContent.Creature("common");
            CreatureDefinition rare = TestContent.Creature("rare", Rarity.Rare);
            MysteryBoxDefinition box = TestContent.Box("box", new[] { common, rare }, new[]
            {
                (Rarity.Common, 90f),
                (Rarity.Rare, 10f),
            });

            var rng = new System.Random(7);
            SavedBox state = NewState();

            for (int i = 0; i < 500; i++)
            {
                LootRoller.Result result = LootRoller.Open(box, state, _ => false, rng);
                Assert.IsTrue(result.IsValid);
                Assert.IsTrue(result.Creature == common || result.Creature == rare,
                    "roll produced a creature outside the pool");
            }
        }

        [Test]
        public void Open_RarityDistribution_TracksTheConfiguredWeights()
        {
            CreatureDefinition common = TestContent.Creature("common");
            CreatureDefinition rare = TestContent.Creature("rare", Rarity.Rare);

            // Pity disabled so this measures the raw weights and nothing else.
            MysteryBoxDefinition box = TestContent.Box("box", new[] { common, rare }, new[]
            {
                (Rarity.Common, 80f),
                (Rarity.Rare, 20f),
            });

            var rng = new System.Random(12345);
            SavedBox state = NewState();

            const int rolls = 4000;
            int rareCount = 0;
            for (int i = 0; i < rolls; i++)
            {
                if (LootRoller.Open(box, state, _ => false, rng).Rarity >= Rarity.Rare) rareCount++;
            }

            float observed = rareCount / (float)rolls;
            // Three points of slack on a 4000 sample is comfortably outside noise while
            // still catching a genuinely wrong weighting.
            Assert.That(observed, Is.EqualTo(0.20f).Within(0.03f),
                $"expected roughly 20% rare, saw {observed:P1}");
        }

        [Test]
        public void Pity_ForcesARareWithinTheConfiguredWindow()
        {
            CreatureDefinition common = TestContent.Creature("common");
            CreatureDefinition rare = TestContent.Creature("rare", Rarity.Rare);

            // Rare is impossible by weight, so only pity can produce one.
            MysteryBoxDefinition box = TestContent.Box("box", new[] { common, rare },
                new[] { (Rarity.Common, 100f) }, pityRareAfter: 10);

            var rng = new System.Random(3);
            SavedBox state = NewState();

            var rareAt = new List<int>();
            for (int i = 1; i <= 40; i++)
            {
                if (LootRoller.Open(box, state, _ => false, rng).Rarity >= Rarity.Rare) rareAt.Add(i);
            }

            Assert.IsNotEmpty(rareAt, "pity never fired inside four windows");
            Assert.AreEqual(10, rareAt[0], "the first guaranteed rare should land on the tenth open");

            for (int i = 1; i < rareAt.Count; i++)
            {
                Assert.LessOrEqual(rareAt[i] - rareAt[i - 1], 10,
                    "the gap between guaranteed rares exceeded the pity window");
            }
        }

        [Test]
        public void Pity_CounterResetsWhenARareArrivesNaturally()
        {
            CreatureDefinition common = TestContent.Creature("common");
            CreatureDefinition rare = TestContent.Creature("rare", Rarity.Rare);
            MysteryBoxDefinition box = TestContent.Box("box", new[] { common, rare },
                new[] { (Rarity.Common, 50f), (Rarity.Rare, 50f) }, pityRareAfter: 10);

            var rng = new System.Random(99);
            SavedBox state = NewState();

            for (int i = 0; i < 50; i++)
            {
                LootRoller.Result result = LootRoller.Open(box, state, _ => false, rng);
                if (result.Rarity >= Rarity.Rare)
                {
                    Assert.AreEqual(0, state.sinceRare,
                        "a natural rare must reset the pity counter");
                }
            }
        }

        [Test]
        public void Open_FirstTimeSpecies_AwardsDiscoveryEssenceAndFlagsAsNew()
        {
            CreatureDefinition creature = TestContent.Creature("goblin", discoveryEssence: 25f);
            MysteryBoxDefinition box = TestContent.Box("box", new[] { creature });

            LootRoller.Result result = LootRoller.Open(box, NewState(), _ => false, new System.Random(1));

            Assert.IsTrue(result.IsNewSpecies);
            Assert.AreEqual(25, result.EssenceAwarded);
            Assert.IsTrue(result.GrantedCopy);
        }

        [Test]
        public void Open_Duplicate_AwardsBoxEssenceInstead()
        {
            CreatureDefinition creature = TestContent.Creature("goblin", discoveryEssence: 25f);
            MysteryBoxDefinition box = TestContent.Box("box", new[] { creature }, duplicateEssence: 7);

            LootRoller.Result result = LootRoller.Open(box, NewState(), _ => true, new System.Random(1));

            Assert.IsFalse(result.IsNewSpecies);
            Assert.AreEqual(7, result.EssenceAwarded);
        }

        [Test]
        public void Open_DuplicateWithCopiesDisabled_DoesNotGrantACopy()
        {
            CreatureDefinition creature = TestContent.Creature("goblin");
            MysteryBoxDefinition box = TestContent.Box("box", new[] { creature },
                duplicatesGrantCopy: false);

            LootRoller.Result newPull = LootRoller.Open(box, NewState(), _ => false, new System.Random(1));
            LootRoller.Result dupe = LootRoller.Open(box, NewState(), _ => true, new System.Random(1));

            Assert.IsTrue(newPull.GrantedCopy, "a first discovery is always granted");
            Assert.IsFalse(dupe.GrantedCopy);
        }

        [Test]
        public void Open_RarityMissingFromPool_FallsBackWithoutFailing()
        {
            // The box advertises Legendary but only stocks Common.
            CreatureDefinition common = TestContent.Creature("common");
            MysteryBoxDefinition box = TestContent.Box("box", new[] { common }, new[]
            {
                (Rarity.Legendary, 100f),
            });

            LootRoller.Result result = LootRoller.Open(box, NewState(), _ => false, new System.Random(5));

            Assert.IsTrue(result.IsValid, "an unstocked rarity must degrade, not fail");
            Assert.AreEqual(common, result.Creature);
            Assert.AreEqual(Rarity.Common, result.Rarity,
                "the reported rarity must be the creature's, not the roll's");
        }

        [Test]
        public void Open_AdvancesTheOpenCounter()
        {
            MysteryBoxDefinition box = TestContent.Box("box", new[] { TestContent.Creature("c") });
            SavedBox state = NewState();

            for (int i = 0; i < 5; i++) LootRoller.Open(box, state, _ => false, new System.Random(i));

            Assert.AreEqual(5, state.opens);
        }
    }
}
