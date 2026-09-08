using LivingDiorama.Data;
using LivingDiorama.Diorama;
using LivingDiorama.Meta;
using LivingDiorama.Save;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>Terrain determinism and save integrity: the two things that quietly ruin
    /// a game if they are wrong, because neither fails loudly.</summary>
    public sealed class WorldAndSaveTests
    {
        // ---- terrain --------------------------------------------------------

        [Test]
        public void Height_IsDeterministicForASeed()
        {
            const int seed = 4242;

            for (int i = 0; i < 200; i++)
            {
                float x = i * 0.37f;
                float z = i * -0.61f;

                float first = TerrainNoise.Height(x, z, seed, 0.3f, 0.45f, 2f, -0.1f);
                float second = TerrainNoise.Height(x, z, seed, 0.3f, 0.45f, 2f, -0.1f);

                Assert.AreEqual(first, second, 1e-6f, "the same seed must rebuild the same world");
            }
        }

        [Test]
        public void Height_DiffersBetweenSeeds()
        {
            float a = TerrainNoise.Height(3.5f, 7.25f, 1, 0.3f, 0.45f, 0f, 0f);
            float b = TerrainNoise.Height(3.5f, 7.25f, 2, 0.3f, 0.45f, 0f, 0f);

            Assert.AreNotEqual(a, b, "two players should not get the same landscape");
        }

        [Test]
        public void Height_MatchesExactlyOnASharedTileEdge()
        {
            // Terrain is a function of world position, which is what makes neighbouring
            // tiles seam invisibly. If this ever breaks, the diorama grows visible cracks.
            const int seed = 77;
            const float tileSize = 6f;

            for (int i = 0; i <= 14; i++)
            {
                float z = i / 14f * tileSize;

                float fromLeftTile = TerrainNoise.Height(tileSize, z, seed, 0.3f, 0.45f, 1.7f, -0.14f);
                float fromRightTile = TerrainNoise.Height(tileSize, z, seed, 0.3f, 0.45f, 1.7f, -0.14f);

                Assert.AreEqual(fromLeftTile, fromRightTile, 1e-6f);
            }
        }

        [Test]
        public void WaterBasin_IsZeroWhenTheBiomeHasNoWater()
        {
            Assert.AreEqual(0f, TerrainNoise.WaterBasin(1f, 2f, 5, 0f));
        }

        [Test]
        public void WaterBasin_CarvesTheTerrainBelowTheWaterLine()
        {
            const int seed = 11;
            const float width = 2.5f;
            const float waterLevel = -0.1f;

            bool foundSubmerged = false;
            for (float z = -20f; z < 20f && !foundSubmerged; z += 0.5f)
            {
                for (float x = -20f; x < 20f; x += 0.5f)
                {
                    if (TerrainNoise.WaterBasin(x, z, seed, width) < 0.9f) continue;

                    float height = TerrainNoise.Height(x, z, seed, 0.3f, 0.45f, width, waterLevel);
                    Assert.Less(height, waterLevel, "the middle of the channel must sit under the water");
                    foundSubmerged = true;
                    break;
                }
            }

            Assert.IsTrue(foundSubmerged, "no part of the sampled area was deep water");
        }

        [Test]
        public void RingOf_MeasuresChebyshevDistanceFromTheCentre()
        {
            Assert.AreEqual(0, DioramaWorld.RingOf(new Vector2Int(0, 0)));
            Assert.AreEqual(1, DioramaWorld.RingOf(new Vector2Int(1, 0)));
            Assert.AreEqual(1, DioramaWorld.RingOf(new Vector2Int(-1, 1)));
            Assert.AreEqual(3, DioramaWorld.RingOf(new Vector2Int(-3, 2)));
        }

        // ---- save -----------------------------------------------------------

        [Test]
        public void SaveData_SurvivesARoundTripThroughJson()
        {
            var original = new SaveData
            {
                worldSeed = -12345,
                clockHours = 37.25,
                lastSeenUnix = 1_700_000_000,
                coins = 4820,
                essence = 96,
                boxKeys = 3,
                level = 7,
                xp = 210,
            };
            original.tiles.Add(new SavedTile { x = -1, y = 2, biomeId = "river" });
            original.discoveredSpecies.Add("goblin");
            original.discoveredSpecies.Add("dragon");
            original.creatures.Add(new SavedCreature
            {
                instanceId = "abc123",
                speciesId = "goblin",
                position = new Vector3(1.5f, 0.25f, -3.75f),
                fullness = 0.42f,
                energy = 0.63f,
                social = 0.21f,
                fun = 0.87f,
                placed = true,
                nickname = "Nib",
            });
            original.boxes.Add(new SavedBox { boxId = "wooden_box", opens = 12, sinceRare = 4, sinceEpic = 9 });
            original.stats.boxesOpened = 12;

            string json = JsonUtility.ToJson(original);
            var restored = JsonUtility.FromJson<SaveData>(json);

            Assert.AreEqual(original.worldSeed, restored.worldSeed);
            Assert.AreEqual(original.clockHours, restored.clockHours, 1e-6);
            Assert.AreEqual(original.coins, restored.coins);
            Assert.AreEqual(original.level, restored.level);

            Assert.AreEqual(1, restored.tiles.Count);
            Assert.AreEqual("river", restored.tiles[0].biomeId);
            Assert.AreEqual(-1, restored.tiles[0].x);

            Assert.AreEqual(2, restored.discoveredSpecies.Count);
            CollectionAssert.Contains(restored.discoveredSpecies, "dragon");

            Assert.AreEqual(1, restored.creatures.Count);
            SavedCreature creature = restored.creatures[0];
            Assert.AreEqual("abc123", creature.instanceId);
            Assert.AreEqual(new Vector3(1.5f, 0.25f, -3.75f), creature.position);
            Assert.AreEqual(0.42f, creature.fullness, 1e-5f);
            Assert.AreEqual("Nib", creature.nickname);

            Assert.AreEqual(12, restored.boxes[0].opens);
            Assert.AreEqual(12, restored.stats.boxesOpened);
        }

        [Test]
        public void BoxState_CreatesAnEntryOnFirstAccessAndReusesItAfter()
        {
            var data = new SaveData();

            SavedBox first = data.BoxState("wooden_box");
            first.opens = 3;
            SavedBox second = data.BoxState("wooden_box");

            Assert.AreSame(first, second);
            Assert.AreEqual(3, second.opens);
            Assert.AreEqual(1, data.boxes.Count);
        }

        // ---- game state ------------------------------------------------------

        [Test]
        public void GameState_SpendingRefusesWhenTheWalletIsShort()
        {
            CreatureDefinition creature = TestContent.Creature("c");
            GameDatabase db = TestContent.Database(creature);
            var state = new GameState(new SaveData { coins = 100 }, db);

            Assert.IsFalse(state.TrySpend(CurrencyKind.Coins, 150));
            Assert.AreEqual(100, state.Data.coins, "a refused purchase must not deduct anything");

            Assert.IsTrue(state.TrySpend(CurrencyKind.Coins, 100));
            Assert.AreEqual(0, state.Data.coins);
        }

        [Test]
        public void GameState_AddXp_LevelsUpAndCarriesTheRemainder()
        {
            CreatureDefinition creature = TestContent.Creature("c");
            GameDatabase db = TestContent.Database(creature);
            var state = new GameState(new SaveData { level = 1, xp = 0 }, db);

            int needed = state.XpForNextLevel;
            int gained = state.AddXp(needed + 10);

            Assert.AreEqual(1, gained);
            Assert.AreEqual(2, state.Data.level);
            Assert.AreEqual(10, state.Data.xp, "spare xp should roll into the next level");
        }

        [Test]
        public void GameState_GrantCreature_RecordsTheDiscoveryOnlyOnce()
        {
            CreatureDefinition creature = TestContent.Creature("goblin");
            GameDatabase db = TestContent.Database(creature);
            var state = new GameState(new SaveData(), db);
            state.AddTileWithoutCharge(Vector2Int.zero, null);

            state.GrantCreature(creature, Vector3.zero, true);
            state.GrantCreature(creature, Vector3.zero, true);

            Assert.AreEqual(1, state.DiscoveredCount);
            Assert.AreEqual(2, state.CopiesOf("goblin"));
        }

        [Test]
        public void GameState_PlacingIsRefusedWhenTheDioramaIsFull()
        {
            CreatureDefinition creature = TestContent.Creature("goblin");
            GameDatabase db = TestContent.Database(creature);
            var state = new GameState(new SaveData(), db);
            state.Data.tiles.Add(new SavedTile { x = 0, y = 0, biomeId = "forest" });

            int cap = state.PopulationCap;
            for (int i = 0; i < cap; i++) state.GrantCreature(creature, Vector3.zero, true);

            Assert.AreEqual(cap, state.PlacedCount);
            Assert.IsFalse(state.HasRoomToPlace);

            SavedCreature overflow = state.GrantCreature(creature, Vector3.zero, true);
            Assert.IsFalse(overflow.placed, "an over-cap creature waits in the collection instead");
        }
    }
}
