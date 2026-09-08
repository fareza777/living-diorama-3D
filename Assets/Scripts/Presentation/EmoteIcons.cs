using System.Collections.Generic;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Mood icons drawn in code as signed-distance fields. Generating them beats
    /// shipping a sprite sheet here: they stay crisp at any size, cost a few kilobytes
    /// of texture each, and a new mood needs one function rather than an art request.
    /// </summary>
    public static class EmoteIcons
    {
        const int Size = 96;
        static readonly Dictionary<Mood, Texture2D> Cache = new();

        public static Texture2D For(Mood mood)
        {
            if (Cache.TryGetValue(mood, out Texture2D cached) && cached != null) return cached;

            Texture2D tex = Render(mood);
            Cache[mood] = tex;
            return tex;
        }

        public static Color TintFor(Mood mood) => mood switch
        {
            Mood.Hungry => new Color(0.98f, 0.72f, 0.24f),
            Mood.Sleepy => new Color(0.62f, 0.74f, 0.96f),
            Mood.Scared => new Color(0.98f, 0.92f, 0.36f),
            Mood.Angry => new Color(0.95f, 0.33f, 0.28f),
            Mood.Playful => new Color(0.52f, 0.88f, 0.62f),
            Mood.Social => new Color(0.97f, 0.48f, 0.66f),
            Mood.KnockedOut => new Color(0.72f, 0.72f, 0.78f),
            _ => Color.white,
        };

        static Texture2D Render(Mood mood)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = $"Emote_{mood}",
            };

            Color fill = TintFor(mood);
            var outline = new Color(0.14f, 0.12f, 0.18f, 1f);
            var pixels = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Work in a -1..1 square so the shape maths reads cleanly.
                    var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                    float d = Distance(mood, p);

                    // Two bands: a dark outline just outside the shape, fill inside.
                    const float aa = 0.035f;
                    float inside = 1f - Mathf.SmoothStep(0f, aa, d);
                    float edge = 1f - Mathf.SmoothStep(0.06f, 0.06f + aa, d);

                    Color c = Color.Lerp(outline, fill, inside);
                    c.a = edge;
                    pixels[y * Size + x] = c;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        static float Distance(Mood mood, Vector2 p) => mood switch
        {
            Mood.Social => Heart(p * 1.25f),
            Mood.Sleepy => Letter_Z(p),
            Mood.Scared => Exclamation(p),
            Mood.Angry => Spark(p),
            Mood.Playful => Star(p, 5, 0.78f, 0.34f),
            Mood.Hungry => Drumstick(p),
            Mood.KnockedOut => Cross(p),
            _ => Circle(p, 0.55f),
        };

        // ---- primitives -----------------------------------------------------

        static float Circle(Vector2 p, float r) => p.magnitude - r;

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - thickness;
        }

        static float Union(float a, float b) => Mathf.Min(a, b);

        static float Heart(Vector2 p)
        {
            // Standard implicit heart, flipped so the point faces down.
            p.y = -p.y;
            p.y += 0.22f;
            float x2 = p.x * p.x;
            float y = p.y - Mathf.Pow(x2, 1f / 3f) * 0.9f;
            return (new Vector2(p.x, y).magnitude - 0.62f) * 0.55f;
        }

        static float Letter_Z(Vector2 p)
        {
            const float t = 0.10f;
            float top = Segment(p, new Vector2(-0.45f, 0.5f), new Vector2(0.45f, 0.5f), t);
            float diag = Segment(p, new Vector2(0.45f, 0.5f), new Vector2(-0.45f, -0.5f), t);
            float bottom = Segment(p, new Vector2(-0.45f, -0.5f), new Vector2(0.45f, -0.5f), t);
            return Union(Union(top, diag), bottom);
        }

        static float Exclamation(Vector2 p)
        {
            float bar = Segment(p, new Vector2(0f, 0.68f), new Vector2(0f, -0.18f), 0.14f);
            float dot = Circle(p - new Vector2(0f, -0.55f), 0.155f);
            return Union(bar, dot);
        }

        static float Spark(Vector2 p)
        {
            // A four-pointed anger burst: a star with sharper inner radius.
            return Star(p, 4, 0.85f, 0.24f);
        }

        static float Star(Vector2 p, int points, float outer, float inner)
        {
            float angle = Mathf.Atan2(p.y, p.x);
            float radius = p.magnitude;
            float sector = Mathf.PI * 2f / points;
            float a = Mathf.Repeat(angle + Mathf.PI * 0.5f, sector) / sector;   // 0..1 within a point
            float wave = Mathf.Abs(a - 0.5f) * 2f;                              // 1 at tip, 0 between
            float target = Mathf.Lerp(inner, outer, Mathf.Pow(wave, 1.4f));
            return radius - target;
        }

        static float Drumstick(Vector2 p)
        {
            float meat = Circle(p - new Vector2(-0.16f, 0.16f), 0.44f);
            float bone = Segment(p, new Vector2(0.02f, 0.02f), new Vector2(0.52f, -0.48f), 0.12f);
            float knob = Circle(p - new Vector2(0.56f, -0.52f), 0.16f);
            return Union(Union(meat, bone), knob);
        }

        static float Cross(Vector2 p)
        {
            float a = Segment(p, new Vector2(-0.45f, 0.45f), new Vector2(0.45f, -0.45f), 0.13f);
            float b = Segment(p, new Vector2(0.45f, 0.45f), new Vector2(-0.45f, -0.45f), 0.13f);
            return Union(a, b);
        }
    }
}
