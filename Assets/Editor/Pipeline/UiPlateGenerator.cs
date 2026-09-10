using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Draws the interface's plates, buttons and frames as project assets.
    ///
    /// UI Toolkit has no gradients -- `linear-gradient` in USS is parsed and silently
    /// ignored -- so a plate built from stylesheet properties alone can only ever be a
    /// flat rectangle with a one-pixel border. Eleven of those on one screen is exactly
    /// what makes the HUD read as a prototype.
    ///
    /// These are generated rather than painted because a rounded rectangle with a bevel
    /// is geometry, not art: it comes out perfectly symmetrical, at the exact size and
    /// border the nine-slice wants, in the palette's real colours, for no credits. The
    /// art model was measured against this job and could not do it -- see
    /// docs/ui-redesign.md -- and painted plates are the reason the last set went unused.
    ///
    /// They are written to disk at edit time rather than made at runtime on purpose. A
    /// texture generated in play mode and handed to UI Toolkit as a background has
    /// already shipped blank on the device once here (see Scrim.cs); an imported sprite
    /// is the path that demonstrably works.
    /// </summary>
    public static class UiPlateGenerator
    {
        const string OutDir = "Assets/Resources/UI/Art";
        const string Manifest = OutDir + "/_import.json";

        // ---- the palette, matching LivingDiorama.uss -------------------------

        static readonly Color GlassTop = new(0.125f, 0.149f, 0.235f, 0.94f);
        static readonly Color GlassBottom = new(0.071f, 0.086f, 0.141f, 0.94f);
        static readonly Color GlassRaisedTop = new(0.169f, 0.196f, 0.294f, 0.96f);
        static readonly Color GlassRaisedBottom = new(0.098f, 0.118f, 0.184f, 0.96f);
        static readonly Color SunkenTop = new(0.020f, 0.027f, 0.051f, 0.92f);
        static readonly Color SunkenBottom = new(0.043f, 0.055f, 0.090f, 0.92f);

        static readonly Color BrassTop = new(1.000f, 0.835f, 0.541f, 1f);
        static readonly Color BrassBottom = new(0.690f, 0.502f, 0.227f, 1f);
        static readonly Color BrassDeep = new(0.365f, 0.259f, 0.106f, 1f);

        static readonly Color Rim = new(0.831f, 0.647f, 0.376f, 0.85f);
        static readonly Color RimFaint = new(0.831f, 0.647f, 0.376f, 0.52f);
        static readonly Color Sheen = new(0.85f, 0.90f, 1f, 0.13f);

        static readonly (string key, Color colour)[] Rarities =
        {
            ("common", new Color(0.620f, 0.651f, 0.722f)),
            ("uncommon", new Color(0.494f, 0.839f, 0.541f)),
            ("rare", new Color(0.408f, 0.690f, 1.000f)),
            ("epic", new Color(0.745f, 0.518f, 1.000f)),
            ("legendary", new Color(1.000f, 0.769f, 0.329f)),
        };

        [MenuItem("Living Diorama/Generate UI Plates", priority = 5)]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutDir);
            var borders = new List<(string key, int[] border, int maxSize)>();

            void Plate(string key, Texture2D texture, int border)
            {
                // Measured before writing: Write destroys the texture, and reading a
                // destroyed object's size is a null reference rather than a stale number.
                int maxSize = Mathf.Max(texture.width, texture.height);

                Write(key, texture);
                borders.Add((key, new[] { border, border, border, border }, maxSize));
            }

            // Glass surfaces. Radius scales with the plate: a modal wants a softer
            // corner than a chip, and one texture stretched to both would give them the
            // same one.
            Plate("ld_plate", Rounded(192, 192, 34f, GlassTop, GlassBottom, Rim, 5f, true), 38);
            Plate("ld_plate_raised", Rounded(192, 192, 34f, GlassRaisedTop, GlassRaisedBottom, Rim, 5f, true), 38);
            Plate("ld_plate_sunken", Sunken(192, 192, 30f), 34);
            Plate("ld_modal", Rounded(320, 320, 54f, GlassRaisedTop, GlassRaisedBottom, Rim, 7f, true), 60);
            Plate("ld_chip", Rounded(160, 96, 28f, GlassRaisedTop, GlassRaisedBottom, RimFaint, 4.5f, true), 32);

            // Buttons. The brass face is the one place in the interface that is allowed
            // to be bright, so it carries the strongest bevel.
            Plate("ld_button_brass", Brass(288, 112, 38f, false), 42);
            Plate("ld_button_brass_down", Brass(288, 112, 38f, true), 42);
            Plate("ld_button_glass", Rounded(288, 112, 38f, GlassRaisedTop, GlassRaisedBottom, Rim, 5f, true), 42);

            // Bars.
            Plate("ld_bar_track", Sunken(96, 40, 18f), 20);
            Plate("ld_bar_fill", Brass(96, 40, 18f, false), 20);

            foreach ((string key, Color colour) in Rarities)
            {
                Plate("ld_frame_" + key, RarityFrame(192, 192, 30f, colour), 34);
            }

            // Circles are not sliced: stretching a ring turns it into an oval.
            Write("ld_ring", Rounded(112, 112, 56f, GlassRaisedTop, GlassRaisedBottom, Rim, 4f, true));
            borders.Add(("ld_ring", null, 112));

            WriteManifest(borders);
            AssetDatabase.Refresh();
            UiArtImporter.ImportAll();

            Debug.Log($"[UiPlateGenerator] wrote {borders.Count} plates to {OutDir}");
        }

        // ---- drawing --------------------------------------------------------

        /// <summary>Distance to a rounded rectangle: negative inside, zero on the edge.
        /// Everything here is a band of this one function.</summary>
        static float RoundedBox(float px, float py, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(px) - halfW + radius;
            float qy = Mathf.Abs(py) - halfH + radius;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
                                       Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>A shader's smoothstep. Not Mathf.SmoothStep, which eases between two
        /// values rather than across a threshold -- a distinction that has already cost
        /// this project a set of mood icons that all rendered as solid squares.</summary>
        static float Threshold(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / Mathf.Max(1e-5f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        static Texture2D Blank(int width, int height, out Color[] pixels)
        {
            pixels = new Color[width * height];
            // sRGB, not linear: these colours are authored as they should appear, and
            // the PNG is read back as an ordinary sprite.
            return new Texture2D(width, height, TextureFormat.RGBA32, false, false);
        }

        /// <summary>A filled plate: vertical gradient body, a rim of its own colour, and
        /// an optional sheen along the top inner edge.</summary>
        static Texture2D Rounded(int width, int height, float radius,
                                 Color top, Color bottom, Color rim, float rimWidth, bool sheen)
        {
            Texture2D texture = Blank(width, height, out Color[] pixels);
            float halfW = width * 0.5f, halfH = height * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float py = y + 0.5f - halfH;
                    float d = RoundedBox(px, py, halfW - 1f, halfH - 1f, radius);

                    float inside = 1f - Threshold(-1f, 0.6f, d);
                    if (inside <= 0.001f) continue;

                    float v = y / (float)(height - 1);
                    Color body = Color.Lerp(bottom, top, v);

                    if (sheen)
                    {
                        // A thin highlight just inside the top edge, fading fast. This is
                        // what stops a flat fill reading as paper.
                        // Scaled by the sheen's own alpha, not added whole: `Sheen * k`
                        // multiplies every channel, and the colour channels are 1,1,1, so
                        // adding it directly blew the top of every plate out to pure white.
                        float fromTop = (height - 1 - y) / (float)height;
                        float lit = Sheen.a * (1f - Threshold(0f, 0.13f, fromTop));
                        body += new Color(Sheen.r, Sheen.g, Sheen.b, 0f) * lit;
                    }

                    // The rim is a band hugging the inside of the outline. Note the
                    // direction: d runs from very negative at the centre to zero at the
                    // edge, so this has to rise toward zero. Inverted, it floods the
                    // whole plate with rim colour -- which turned every glass panel to
                    // brass and was obvious only once they were laid out side by side.
                    float band = Threshold(-rimWidth, -rimWidth + 1.4f, d);
                    body = Color.Lerp(body, new Color(rim.r, rim.g, rim.b, body.a), band * rim.a);

                    body.a *= inside;
                    pixels[y * width + x] = body;
                }
            }

            return Finish(texture, pixels);
        }

        /// <summary>A recess: darkest at the top, where a real one would catch no light.</summary>
        static Texture2D Sunken(int width, int height, float radius)
        {
            Texture2D texture = Blank(width, height, out Color[] pixels);
            float halfW = width * 0.5f, halfH = height * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float py = y + 0.5f - halfH;
                    float d = RoundedBox(px, py, halfW - 1f, halfH - 1f, radius);

                    float inside = 1f - Threshold(-1f, 0.6f, d);
                    if (inside <= 0.001f) continue;

                    float v = y / (float)(height - 1);
                    Color body = Color.Lerp(SunkenBottom, SunkenTop, v);

                    // Inner shadow along the top and sides, strongest right at the lip.
                    float depth = Threshold(-9f, -1f, d);
                    float fromTop = (height - 1 - y) / (float)height;
                    float cast = depth * (0.35f + 0.45f * (1f - Threshold(0f, 0.35f, fromTop)));
                    body = Color.Lerp(body, new Color(0f, 0f, 0f, body.a), cast * 0.55f);

                    float band = Threshold(-4f, -1f, d);
                    body = Color.Lerp(body, new Color(RimFaint.r, RimFaint.g, RimFaint.b, body.a),
                                      band * RimFaint.a);

                    body.a *= inside;
                    pixels[y * width + x] = body;
                }
            }

            return Finish(texture, pixels);
        }

        /// <summary>The brass face. Pressed inverts the light so the bevel reads as a
        /// recess without changing the silhouette.</summary>
        static Texture2D Brass(int width, int height, float radius, bool pressed)
        {
            Texture2D texture = Blank(width, height, out Color[] pixels);
            float halfW = width * 0.5f, halfH = height * 0.5f;

            Color top = pressed ? BrassBottom : BrassTop;
            Color bottom = pressed ? BrassDeep : BrassBottom;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float py = y + 0.5f - halfH;
                    float d = RoundedBox(px, py, halfW - 1f, halfH - 1f, radius);

                    float inside = 1f - Threshold(-1f, 0.6f, d);
                    if (inside <= 0.001f) continue;

                    float v = y / (float)(height - 1);
                    // Eased rather than linear: metal turns over quickly near the top.
                    Color body = Color.Lerp(bottom, top, Threshold(0f, 1f, v) * 0.85f + v * 0.15f);

                    float fromTop = (height - 1 - y) / (float)height;
                    float fromBottom = y / (float)height;

                    if (!pressed)
                    {
                        body += new Color(1f, 0.95f, 0.85f, 0f) * 0.22f * (1f - Threshold(0f, 0.10f, fromTop));
                        body = Color.Lerp(body, new Color(BrassDeep.r, BrassDeep.g, BrassDeep.b, body.a),
                                          0.45f * (1f - Threshold(0f, 0.13f, fromBottom)));
                    }
                    else
                    {
                        body = Color.Lerp(body, new Color(BrassDeep.r, BrassDeep.g, BrassDeep.b, body.a),
                                          0.5f * (1f - Threshold(0f, 0.16f, fromTop)));
                    }

                    float band = Threshold(-5f, -1.5f, d);
                    body = Color.Lerp(body, new Color(BrassDeep.r, BrassDeep.g, BrassDeep.b, body.a), band * 0.7f);

                    body.a *= inside;
                    pixels[y * width + x] = body;
                }
            }

            return Finish(texture, pixels);
        }

        /// <summary>A hollow frame in a rarity colour, with the corners weighted so a
        /// Legendary reads as a different object from a Common across a grid.</summary>
        static Texture2D RarityFrame(int width, int height, float radius, Color tint)
        {
            Texture2D texture = Blank(width, height, out Color[] pixels);
            float halfW = width * 0.5f, halfH = height * 0.5f;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = x + 0.5f - halfW;
                    float py = y + 0.5f - halfH;
                    float d = RoundedBox(px, py, halfW - 1f, halfH - 1f, radius);

                    float inside = 1f - Threshold(-1f, 0.6f, d);
                    if (inside <= 0.001f) continue;

                    // Glass behind the artwork, so the frame is a card and not a cutout.
                    float v = y / (float)(height - 1);
                    Color body = Color.Lerp(GlassBottom, GlassTop, v);

                    // The frame band itself, brightest at the very edge.
                    float band = Threshold(-9f, -2f, d);

                    // Corners carry more of it: distance from the nearest corner, as a
                    // fraction of the plate.
                    float cx = Mathf.Abs(px) / halfW;
                    float cy = Mathf.Abs(py) / halfH;
                    float corner = Threshold(0.55f, 1f, Mathf.Min(cx, cy));

                    float strength = Mathf.Clamp01(band * (0.55f + corner * 0.75f));
                    Color edge = Color.Lerp(tint * 0.55f, tint, corner);
                    body = Color.Lerp(body, new Color(edge.r, edge.g, edge.b, body.a), strength);

                    // A faint wash of the rarity colour across the whole plate, so an
                    // Epic card is tinted even where the frame is not.
                    body = Color.Lerp(body, new Color(tint.r, tint.g, tint.b, body.a), 0.05f);

                    body.a *= inside;
                    pixels[y * width + x] = body;
                }
            }

            return Finish(texture, pixels);
        }

        // ---- writing --------------------------------------------------------

        /// <summary>
        /// Push colour outward under the transparent margin before saving.
        ///
        /// Every pixel outside the shape is transparent black, and bilinear filtering
        /// does not care about alpha when it averages colour: without this, every rounded
        /// corner picks up a dark halo the moment the sprite is drawn at any size other
        /// than the one it was authored at.
        /// </summary>
        static Texture2D Finish(Texture2D texture, Color[] pixels)
        {
            int w = texture.width, h = texture.height;

            for (int pass = 0; pass < 3; pass++)
            {
                var copy = (Color[])pixels.Clone();

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x;
                        if (pixels[i].a > 0.02f) continue;

                        Color sum = Color.clear;
                        int n = 0;

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, ny = y + dy;
                                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;

                                Color neighbour = pixels[ny * w + nx];
                                if (neighbour.a <= 0.02f) continue;

                                sum += neighbour;
                                n++;
                            }
                        }

                        if (n == 0) continue;
                        copy[i] = new Color(sum.r / n, sum.g / n, sum.b / n, pixels[i].a);
                    }
                }

                pixels = copy;
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        static void Write(string key, Texture2D texture)
        {
            File.WriteAllBytes($"{OutDir}/{key}.png", texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        static void WriteManifest(List<(string key, int[] border, int maxSize)> plates)
        {
            var entries = new List<string>(plates.Count);

            foreach ((string key, int[] border, int maxSize) in plates)
            {
                string b = border == null
                    ? "null"
                    : $"[{border[0]},{border[1]},{border[2]},{border[3]}]";
                entries.Add($"    {{ \"key\": \"{key}\", \"border\": {b}, \"maxSize\": {maxSize} }}");
            }

            File.WriteAllText(Manifest, "{\n  \"sprites\": [\n" + string.Join(",\n", entries) + "\n  ]\n}\n");
        }
    }
}
