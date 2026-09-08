using LivingDiorama.Data;
using LivingDiorama.Meta;
using LivingDiorama.Save;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// The economy curves and the offline payout. Cheap to get subtly wrong, and wrong
    /// in a way nobody notices until the game is either trivial or a grind.
    /// </summary>
    public sealed class ProgressionTests
    {
        [Test]
        public void TileCost_FirstTileIsFree_AndRisesWithDistance()
        {
            ProgressionSettings p = TestContent.Progression();

            Assert.AreEqual(0, p.TileCost(0), "the starting tile is not bought");

            int previous = p.TileCost(1);
            Assert.Greater(previous, 0);

            for (int ring = 2; ring <= 5; ring++)
            {
                int cost = p.TileCost(ring);
                Assert.Greater(cost, previous, $"ring {ring} should cost more than ring {ring - 1}");
                previous = cost;
            }
        }

        [Test]
        public void XpForLevel_IncreasesMonotonically()
        {
            ProgressionSettings p = TestContent.Progression();

            int previous = p.XpForLevel(1);
            for (int level = 2; level <= 40; level++)
            {
                int required = p.XpForLevel(level);
                Assert.Greater(required, previous, $"level {level} should require more xp than {level - 1}");
                previous = required;
            }
        }

        [Test]
        public void PopulationCap_GrowsWithTiles_AndStopsAtTheHardCap()
        {
            ProgressionSettings p = TestContent.Progression();

            Assert.AreEqual(5, p.PopulationCap(1));
            Assert.AreEqual(15, p.PopulationCap(3));
            Assert.AreEqual(p.hardPopulationCap, p.PopulationCap(1000));
        }

        [Test]
        public void PopulationCap_NeverReturnsZero()
        {
            ProgressionSettings p = TestContent.Progression();
            Assert.GreaterOrEqual(p.PopulationCap(0), 1, "a diorama with no tiles still needs a valid cap");
        }

        [Test]
        public void Daylight_IsBrightAtNoonAndDarkAtMidnight()
        {
            SimulationSettings s = TestContent.Simulation();

            Assert.Greater(s.DaylightAt(0.5f), 0.9f, "noon should be daylight");
            Assert.Less(s.DaylightAt(0.0f), 0.1f, "midnight should be dark");
            Assert.IsTrue(s.IsNight(0.02f));
            Assert.IsFalse(s.IsNight(0.5f));
        }

        [Test]
        public void Activity_NocturnalAndDiurnalCreaturesAreOppositeAcrossTheDay()
        {
            SimulationSettings s = TestContent.Simulation();

            float diurnalNoon = s.ActivityFor(ActivityCycle.Diurnal, 0.5f);
            float nocturnalNoon = s.ActivityFor(ActivityCycle.Nocturnal, 0.5f);
            float diurnalMidnight = s.ActivityFor(ActivityCycle.Diurnal, 0f);
            float nocturnalMidnight = s.ActivityFor(ActivityCycle.Nocturnal, 0f);

            Assert.Greater(diurnalNoon, nocturnalNoon);
            Assert.Greater(nocturnalMidnight, diurnalMidnight);
            Assert.AreEqual(1f, s.ActivityFor(ActivityCycle.Always, 0.3f),
                "an always-active creature ignores the clock");
        }

        [Test]
        public void OfflineProgress_CapsTheCreditedTime()
        {
            CreatureDefinition creature = TestContent.Creature("earner", coinsPerHour: 60f);
            GameDatabase db = TestContent.Database(creature);

            SaveData data = MakeSave(creature, hoursAway: 4);
            OfflineProgress.Report shortTrip = OfflineProgress.Compute(data, db, Now(data, 4));

            data = MakeSave(creature, hoursAway: 100);
            OfflineProgress.Report longTrip = OfflineProgress.Compute(data, db, Now(data, 100));

            Assert.AreEqual(4.0, shortTrip.CreditedHours, 0.01);
            Assert.AreEqual(db.simulation.offlineCapHours, longTrip.CreditedHours, 0.01,
                "time away beyond the cap must not be paid out");
            Assert.Greater(longTrip.ElapsedHours, longTrip.CreditedHours,
                "elapsed time is still reported honestly to the player");
        }

        [Test]
        public void OfflineProgress_UnplacedCreaturesEarnNothing()
        {
            CreatureDefinition creature = TestContent.Creature("earner", coinsPerHour: 60f);
            GameDatabase db = TestContent.Database(creature);

            SaveData placed = MakeSave(creature, hoursAway: 4);
            SaveData shelved = MakeSave(creature, hoursAway: 4);
            shelved.creatures[0].placed = false;

            int withPlaced = OfflineProgress.Compute(placed, db, Now(placed, 4)).Coins;
            int withShelved = OfflineProgress.Compute(shelved, db, Now(shelved, 4)).Coins;

            Assert.Greater(withPlaced, 0);
            Assert.AreEqual(0, withShelved);
        }

        [Test]
        public void OfflineProgress_HungryCreaturesEarnLess()
        {
            CreatureDefinition creature = TestContent.Creature("earner", coinsPerHour: 60f);
            GameDatabase db = TestContent.Database(creature);

            SaveData happy = MakeSave(creature, hoursAway: 4);
            SaveData miserable = MakeSave(creature, hoursAway: 4);
            miserable.creatures[0].fullness = 0.05f;
            miserable.creatures[0].energy = 0.05f;
            miserable.creatures[0].social = 0.05f;

            int happyCoins = OfflineProgress.Compute(happy, db, Now(happy, 4)).Coins;
            int miserableCoins = OfflineProgress.Compute(miserable, db, Now(miserable, 4)).Coins;

            Assert.Greater(happyCoins, miserableCoins,
                "wellbeing has to matter or there is no reason to look after anyone");
        }

        [Test]
        public void OfflineProgress_AShortAbsenceIsNotWorthAScreen()
        {
            CreatureDefinition creature = TestContent.Creature("earner", coinsPerHour: 60f);
            GameDatabase db = TestContent.Database(creature);

            SaveData data = MakeSave(creature, hoursAway: 0);
            OfflineProgress.Report report = OfflineProgress.Compute(data, db, data.lastSeenUnix + 5);

            Assert.IsFalse(report.WorthShowing);
        }

        [Test]
        public void OfflineProgress_Apply_AdvancesTheWorldClock()
        {
            CreatureDefinition creature = TestContent.Creature("earner");
            GameDatabase db = TestContent.Database(creature);

            SaveData data = MakeSave(creature, hoursAway: 6);
            double before = data.clockHours;

            OfflineProgress.Report report = OfflineProgress.Compute(data, db, Now(data, 6));
            OfflineProgress.Apply(data, db, report, new GameState(data, db));

            Assert.Greater(data.clockHours, before,
                "coming back should find the diorama at a different time of day");
        }

        static long Now(SaveData data, int hoursAway) => data.lastSeenUnix + hoursAway * 3600L;

        static SaveData MakeSave(CreatureDefinition creature, int hoursAway)
        {
            var data = new SaveData
            {
                lastSeenUnix = 1_700_000_000,
                clockHours = 12.0,
            };
            data.creatures.Add(new SavedCreature
            {
                instanceId = "a",
                speciesId = creature.id,
                placed = true,
                fullness = 1f,
                energy = 1f,
                social = 1f,
                fun = 1f,
                position = Vector3.zero,
            });
            return data;
        }
    }
}
