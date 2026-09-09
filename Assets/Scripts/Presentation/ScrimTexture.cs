using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// The vertical scrim that sits between the diorama and the title screen's buttons.
    ///
    /// This wants to be a CSS gradient, and was one, but UI Toolkit's style reader does
    /// not accept a function where it expects an image -- it warns and draws nothing, so
    /// the buttons ended up sitting directly on the diorama with nothing behind them.
    /// Generating the ramp as a texture gets the same result through a path the runtime
    /// actually supports, and costs half a kilobyte.
    /// </summary>
    public static class ScrimTexture
    {
        static Texture2D _scrim;

        /// <summary>Heaviest at the bottom, gone before the halfway line. The falloff is
        /// smoothed rather than linear: a straight ramp reads as a wall with a visible
        /// edge across the middle of the screen, and the diorama behind the title is the
        /// best thing on it -- the scrim is meant to seat the buttons, not hide the
        /// world.</summary>
        public static Texture2D Scrim()
        {
            if (_scrim != null) return _scrim;

            const int width = 4;
            const int height = 128;

            _scrim = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "TitleScrim",
            };

            var ink = new Color(6f / 255f, 7f / 255f, 14f / 255f);
            var pixels = new Color[width * height];

            for (int y = 0; y < height; y++)
            {
                // Row 0 is the bottom of the texture and the bottom of the screen.
                float t = y / (float)(height - 1);
                float k = Mathf.Clamp01(t / 0.58f);
                float alpha = 0.88f * (1f - k * k * (3f - 2f * k));

                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = new Color(ink.r, ink.g, ink.b, alpha);
                }
            }

            _scrim.SetPixels(pixels);
            _scrim.Apply(false, true);
            return _scrim;
        }
    }
}
