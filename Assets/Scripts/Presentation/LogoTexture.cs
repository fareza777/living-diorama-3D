using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// The title emblem, drawn in code: a small world under glass.
    ///
    /// Signed-distance fields rather than a PNG, for the same reason the mood icons are
    /// generated -- it stays razor sharp on any screen density, costs a few kilobytes,
    /// and the mark can be adjusted by changing a number instead of reopening a paint
    /// program.
    /// </summary>
    public static class LogoTexture
    {
        static Texture2D _cached;

        public static Texture2D Emblem(int size = 512)
        {
            if (_cached != null) return _cached;
            _cached = Render(size);
            return _cached;
        }

        static Texture2D Render(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
                name = "DioramaEmblem",
            };

            var gold = new Color(1f, 0.80f, 0.42f);
            var deepGold = new Color(0.86f, 0.60f, 0.26f);
            var glass = new Color(0.62f, 0.86f, 0.92f);
            var leaf = new Color(0.44f, 0.80f, 0.56f);

            var pixels = new Color32[size * size];
            float aa = 2.4f / size;   // roughly one pixel of antialiasing

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);

                    Color colour = Color.clear;

                    // The outer ring, drawn first so everything else sits inside it.
                    Blend(ref colour, Ring(p, 0.94f, 0.045f), gold);

                    // Two small notches at the base of the ring make it read as a stand
                    // rather than a plain circle.
                    Blend(ref colour, Base(p), deepGold);

                    // The glass dome.
                    Blend(ref colour, DomeGlass(p), glass * 0.55f);
                    Blend(ref colour, Dome(p), glass);

                    // Contents: a hill, a pine, a moon.
                    Blend(ref colour, Hill(p), deepGold);
                    Blend(ref colour, Pine(p), leaf);
                    Blend(ref colour, Moon(p), gold);
                    Blend(ref colour, Stars(p), gold);

                    pixels[y * size + x] = colour;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, false);
            return tex;

            // Coverage from a distance: positive outside, negative inside.
            float Cover(float d) => 1f - Mathf.SmoothStep(-aa, aa, d);

            void Blend(ref Color dst, float coverage, Color src)
            {
                if (coverage <= 0.001f) return;
                float a = Mathf.Clamp01(coverage);
                dst = new Color(
                    Mathf.Lerp(dst.r, src.r, a),
                    Mathf.Lerp(dst.g, src.g, a),
                    Mathf.Lerp(dst.b, src.b, a),
                    Mathf.Clamp01(dst.a + a * src.a));
            }

            float Ring(Vector2 p, float radius, float thickness)
                => Cover(Mathf.Abs(p.magnitude - radius) - thickness);

            float Base(Vector2 p)
            {
                // A plinth: a rounded bar under the dome.
                Vector2 q = p - new Vector2(0f, -0.62f);
                float bar = RoundedBox(q, new Vector2(0.52f, 0.055f), 0.05f);
                return Cover(bar);
            }

            float DomeShape(Vector2 p, float radius)
            {
                // Circle intersected with the half-plane above the plinth.
                float circle = (p - new Vector2(0f, -0.16f)).magnitude - radius;
                float above = -(p.y + 0.56f);
                return Mathf.Max(circle, above);
            }

            float Dome(Vector2 p) => Cover(Mathf.Abs(DomeShape(p, 0.62f)) - 0.022f);

            float DomeGlass(Vector2 p)
            {
                float inside = DomeShape(p, 0.60f);
                if (inside > 0f) return 0f;
                // A soft diagonal sheen across the glass.
                float sheen = Mathf.Abs(p.x * 0.7f - p.y * 0.7f + 0.22f) - 0.07f;
                return Cover(Mathf.Max(inside, sheen)) * 0.7f;
            }

            float Hill(Vector2 p)
            {
                float inside = DomeShape(p, 0.60f);
                if (inside > 0f) return 0f;

                // A gentle mound sitting on the plinth.
                float surface = -0.40f + Mathf.Cos(p.x * 2.4f) * 0.12f;
                return Cover(Mathf.Max(p.y - surface, inside));
            }

            float Pine(Vector2 p)
            {
                float inside = DomeShape(p, 0.58f);
                if (inside > 0f) return 0f;

                Vector2 q = p - new Vector2(-0.10f, -0.30f);
                float trunk = RoundedBox(q - new Vector2(0f, 0.02f), new Vector2(0.022f, 0.09f), 0.01f);

                // Three stacked triangles.
                float tiers = 1e9f;
                for (int i = 0; i < 3; i++)
                {
                    float k = i / 2f;
                    Vector2 c = q - new Vector2(0f, 0.14f + i * 0.13f);
                    float width = Mathf.Lerp(0.17f, 0.085f, k);
                    float height = Mathf.Lerp(0.16f, 0.13f, k);
                    tiers = Mathf.Min(tiers, Triangle(c, width, height));
                }

                return Cover(Mathf.Min(trunk, tiers));
            }

            float Moon(Vector2 p)
            {
                float inside = DomeShape(p, 0.56f);
                if (inside > 0f) return 0f;

                Vector2 q = p - new Vector2(0.24f, 0.22f);
                float full = q.magnitude - 0.115f;
                float bite = (q - new Vector2(0.055f, 0.035f)).magnitude - 0.10f;
                return Cover(Mathf.Max(full, -bite));
            }

            float Stars(Vector2 p)
            {
                float inside = DomeShape(p, 0.56f);
                if (inside > 0f) return 0f;

                float d = 1e9f;
                Vector2[] positions =
                {
                    new(-0.26f, 0.30f), new(0.02f, 0.36f), new(-0.05f, 0.16f),
                };
                float[] radii = { 0.022f, 0.016f, 0.012f };

                for (int i = 0; i < positions.Length; i++)
                {
                    d = Mathf.Min(d, (p - positions[i]).magnitude - radii[i]);
                }
                return Cover(d);
            }
        }

        // ---- distance primitives -------------------------------------------

        static float RoundedBox(Vector2 p, Vector2 halfSize, float radius)
        {
            Vector2 d = new(Mathf.Abs(p.x) - halfSize.x + radius, Mathf.Abs(p.y) - halfSize.y + radius);
            float outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(d.x, d.y), 0f);
            return outside + inside - radius;
        }

        /// <summary>Isosceles triangle with its base at y = 0, pointing up.</summary>
        static float Triangle(Vector2 p, float halfWidth, float height)
        {
            p.x = Mathf.Abs(p.x);
            // Distance to the slanted edge, then clipped by the base.
            Vector2 edge = new Vector2(halfWidth, -height).normalized;
            float slant = Vector2.Dot(new Vector2(p.x, p.y - height), edge);
            float baseLine = -p.y;
            return Mathf.Max(slant, baseLine);
        }
    }
}
