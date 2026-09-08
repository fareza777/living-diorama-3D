using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// Pure world-space terrain functions.
    ///
    /// Height is a function of world position alone, which buys three things: tiles seam
    /// perfectly because shared edge vertices evaluate identically, the simulation can
    /// query ground height without a raycast, and the whole thing is deterministic from
    /// a seed so a save reloads the exact same landscape.
    /// </summary>
    public static class TerrainNoise
    {
        /// <summary>Deterministic 2D value hash in 0..1.</summary>
        public static float Hash(float x, float y, int seed)
        {
            // Integer mixing rather than trig, so results are stable across platforms.
            unchecked
            {
                int xi = Mathf.FloorToInt(x * 1024f);
                int yi = Mathf.FloorToInt(y * 1024f);
                int h = seed;
                h = h * 73856093 ^ xi * 19349663 ^ yi * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        static float ValueNoise(float x, float y, int seed)
        {
            float xi = Mathf.Floor(x), yi = Mathf.Floor(y);
            float xf = x - xi, yf = y - yi;

            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash(xi, yi, seed);
            float b = Hash(xi + 1f, yi, seed);
            float c = Hash(xi, yi + 1f, seed);
            float d = Hash(xi + 1f, yi + 1f, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        /// <summary>Three octaves is plenty for gentle diorama relief.</summary>
        public static float Fbm(float x, float y, int seed)
        {
            float sum = 0f, amplitude = 0.5f, frequency = 1f, norm = 0f;
            for (int i = 0; i < 3; i++)
            {
                sum += ValueNoise(x * frequency, y * frequency, seed + i * 7919) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.13f;
            }
            return sum / norm;
        }

        /// <summary>
        /// How much water sits at this point: 0 on dry land, 1 in the middle of the
        /// channel. A meandering sine line reads far better than blobby noise, and it
        /// gives creatures an obvious shoreline to gather along.
        /// </summary>
        public static float WaterBasin(float worldX, float worldZ, int seed, float width)
        {
            if (width <= 0.01f) return 0f;

            // Meander the channel so it never looks like a canal.
            float centre = Mathf.Sin(worldZ * 0.16f) * 2.6f
                           + Mathf.Sin(worldZ * 0.061f + seed * 0.017f) * 4.2f;

            float distance = Mathf.Abs(worldX - centre);
            // Wobble the bank so the edge is not a clean stripe.
            distance += (Fbm(worldX * 0.22f, worldZ * 0.22f, seed + 4111) - 0.5f) * width * 0.9f;

            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width, width * 0.25f, distance));
        }

        /// <summary>
        /// Ground height at a world position.
        /// </summary>
        /// <param name="relief">Vertical amplitude from the biome.</param>
        /// <param name="reliefScale">Horizontal frequency from the biome.</param>
        /// <param name="waterWidth">0 disables the channel entirely.</param>
        /// <param name="waterLevel">Surface height of the channel.</param>
        public static float Height(float worldX, float worldZ, int seed,
                                   float relief, float reliefScale,
                                   float waterWidth, float waterLevel)
        {
            float h = (Fbm(worldX * reliefScale, worldZ * reliefScale, seed) - 0.5f) * 2f * relief;

            // A second, much broader octave stops the terrain reading as uniform bumpiness.
            h += (Fbm(worldX * reliefScale * 0.23f, worldZ * reliefScale * 0.23f, seed + 991) - 0.5f)
                 * relief * 1.4f;

            if (waterWidth > 0.01f)
            {
                float basin = WaterBasin(worldX, worldZ, seed, waterWidth);
                if (basin > 0f)
                {
                    // Carve down to a bed below the water line, easing out at the banks.
                    float bed = waterLevel - 0.35f - basin * 0.25f;
                    h = Mathf.Lerp(h, bed, basin);
                }
            }

            return h;
        }
    }
}
