using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Particle sprites generated in code: a soft dot and a four-point twinkle. Two tiny
    /// textures cover every burst in the game, and generating them keeps the effects
    /// self-contained rather than depending on an art file that has to travel with them.
    /// </summary>
    public static class SparkTexture
    {
        static Texture2D _dot;
        static Texture2D _twinkle;

        /// <summary>Soft round falloff. The workhorse for dust, motes and glow.</summary>
        public static Texture2D Dot(int size = 64)
        {
            if (_dot != null) return _dot;
            _dot = Render(size, p =>
            {
                float d = p.magnitude;
                // Squared falloff keeps a bright core with a wide soft skirt.
                return Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
            }, "SparkDot");
            return _dot;
        }

        /// <summary>Four-point star, for the rarer reveals where a plain dot reads as dust
        /// rather than as magic.</summary>
        public static Texture2D Twinkle(int size = 64)
        {
            if (_twinkle != null) return _twinkle;
            _twinkle = Render(size, p =>
            {
                float d = p.magnitude;
                float core = Mathf.Pow(Mathf.Clamp01(1f - d), 3.5f);

                // Two crossed needles: narrow along one axis, long along the other.
                float horizontal = Mathf.Clamp01(1f - Mathf.Abs(p.y) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(p.x));
                float vertical = Mathf.Clamp01(1f - Mathf.Abs(p.x) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(p.y));

                return Mathf.Clamp01(core + (horizontal + vertical) * 0.55f);
            }, "SparkTwinkle");
            return _twinkle;
        }

        static Texture2D Render(int size, System.Func<Vector2, float> field, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
                    float a = Mathf.Clamp01(field(p));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
