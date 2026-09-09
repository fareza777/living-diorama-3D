using LivingDiorama.Data;
using LivingDiorama.Diorama;
using NUnit.Framework;
using UnityEngine;

namespace LivingDiorama.Tests
{
    /// <summary>
    /// The ground a creature stands on has to be the ground the player can see.
    ///
    /// The terrain is faceted -- flat triangles between grid samples -- while the height
    /// function behind it is smooth. Standing creatures on the function instead of the
    /// mesh leaves them hovering over every rise, which is exactly what it looks like.
    /// These tests measure the sampler against the triangles the mesh builder actually
    /// emitted, so the two cannot drift apart again.
    /// </summary>
    public sealed class GroundSamplingTests
    {
        const float TileSize = 10f;
        const int Seed = 1337;

        static BiomeDefinition MakeBiome(bool water)
        {
            var biome = ScriptableObject.CreateInstance<BiomeDefinition>();
            biome.reliefHeight = 0.9f;
            biome.reliefScale = 6.5f;
            biome.hasWater = water;
            biome.waterLevel = -0.15f;
            return biome;
        }

        /// <summary>Height of the built mesh directly under a point, by finding the
        /// surface triangle that covers it. Deliberately brute force: this is the
        /// independent measurement the sampler is checked against.</summary>
        static bool MeshHeightAt(Mesh mesh, Vector2 local, out float height)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;

            height = float.MinValue;
            bool found = false;

            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];

                // Barycentric coordinates in the XZ plane.
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Mathf.Abs(d) < 1e-6f) continue;   // vertical: a skirt or floor face

                float w0 = ((b.z - c.z) * (local.x - c.x) + (c.x - b.x) * (local.y - c.z)) / d;
                float w1 = ((c.z - a.z) * (local.x - c.x) + (a.x - c.x) * (local.y - c.z)) / d;
                float w2 = 1f - w0 - w1;

                const float slack = 1e-4f;
                if (w0 < -slack || w1 < -slack || w2 < -slack) continue;

                float y = w0 * a.y + w1 * b.y + w2 * c.y;
                if (y > height) height = y;
                found = true;
            }

            return found;
        }

        [Test]
        public void SampleSurface_MatchesTheGeneratedMesh([Values(false, true)] bool water)
        {
            BiomeDefinition biome = MakeBiome(water);
            var origin = new Vector3(-TileSize, 0f, TileSize * 2f);

            Mesh mesh = TileMeshBuilder.BuildGround(biome, origin, TileSize, Seed);

            var random = new System.Random(99);
            int checkedPoints = 0;

            for (int i = 0; i < 400; i++)
            {
                // Stay a hair inside the tile so a point never lands on the outer seam.
                float lx = (float)random.NextDouble() * (TileSize - 0.02f) + 0.01f;
                float lz = (float)random.NextDouble() * (TileSize - 0.02f) + 0.01f;

                if (!MeshHeightAt(mesh, new Vector2(lx, lz), out float expected)) continue;

                float actual = TileMeshBuilder.SampleSurface(
                    biome, origin, TileSize, Seed, origin.x + lx, origin.z + lz);

                Assert.AreEqual(expected, actual, 1e-3f,
                    $"ground sample disagrees with the mesh at local ({lx:F2}, {lz:F2})");
                checkedPoints++;
            }

            Assert.Greater(checkedPoints, 300, "not enough points landed on the surface to be meaningful");
            Object.DestroyImmediate(biome);
        }

    }
}
