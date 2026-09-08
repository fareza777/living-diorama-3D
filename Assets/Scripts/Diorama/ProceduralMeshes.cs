using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// Flat-shaded primitives built in code.
    ///
    /// The diorama needs trees, rocks and bushes in every biome. Generating them beats
    /// authoring them: the silhouettes stay consistent with the terrain's faceted look,
    /// each biome can re-tint the same generator, and the whole prop set costs nothing
    /// in download size.
    /// </summary>
    public static class ProceduralMeshes
    {
        /// <summary>Accumulates flat-shaded triangles, then bakes a mesh.</summary>
        public sealed class Builder
        {
            readonly List<Vector3> _vertices = new(512);
            readonly List<Vector3> _normals = new(512);
            readonly List<Color> _colors = new(512);
            readonly List<Vector2> _uvs = new(512);
            readonly List<int> _triangles = new(512);

            public void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color colour,
                                    float uvHeightScale = 1f)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-10f) return;
                n.Normalize();

                int i = _vertices.Count;
                Push(a, n, colour, uvHeightScale);
                Push(b, n, colour, uvHeightScale);
                Push(c, n, colour, uvHeightScale);

                _triangles.Add(i);
                _triangles.Add(i + 1);
                _triangles.Add(i + 2);
            }

            public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
            {
                AddTriangle(a, b, c, colour);
                AddTriangle(a, c, d, colour);
            }

            void Push(Vector3 p, Vector3 n, Color c, float uvHeightScale)
            {
                _vertices.Add(p);
                _normals.Add(n);
                _colors.Add(c);
                // V follows height so the foliage shader can tint tips without extra data.
                _uvs.Add(new Vector2(p.x + p.z, p.y * uvHeightScale));
            }

            public Mesh Bake(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_vertices);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colors);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            public bool IsEmpty => _triangles.Count == 0;
        }

        // ---- primitives -----------------------------------------------------

        /// <summary>Cone with a closed base. Sides is deliberately low: 6-8 reads as
        /// stylised, 16 reads as an untextured render.</summary>
        public static void Cone(Builder b, Vector3 baseCentre, float radius, float height,
                                int sides, Color side, Color bottom)
        {
            Vector3 apex = baseCentre + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;

                Vector3 p0 = baseCentre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                Vector3 p1 = baseCentre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;

                b.AddTriangle(p0, apex, p1, side);
                b.AddTriangle(p0, p1, baseCentre, bottom);
            }
        }

        public static void Cylinder(Builder b, Vector3 baseCentre, float radiusBottom,
                                    float radiusTop, float height, int sides, Color colour)
        {
            Vector3 topCentre = baseCentre + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;

                Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));

                Vector3 b0 = baseCentre + d0 * radiusBottom;
                Vector3 b1 = baseCentre + d1 * radiusBottom;
                Vector3 t0 = topCentre + d0 * radiusTop;
                Vector3 t1 = topCentre + d1 * radiusTop;

                b.AddQuad(b0, t0, t1, b1, colour);
                b.AddTriangle(t0, topCentre, t1, colour);
            }
        }

        /// <summary>Low-frequency deformed sphere. The workhorse for bushes, boulders and
        /// tree canopies -- one generator, three different sets of parameters.</summary>
        public static void Blob(Builder b, Vector3 centre, Vector3 radii, int rings, int segments,
                                float lumpiness, int seed, Color colour, float colourJitter = 0.06f)
        {
            for (int r = 0; r < rings; r++)
            {
                float phi0 = Mathf.PI * r / rings;
                float phi1 = Mathf.PI * (r + 1) / rings;

                for (int s = 0; s < segments; s++)
                {
                    float th0 = Mathf.PI * 2f * s / segments;
                    float th1 = Mathf.PI * 2f * (s + 1) / segments;

                    Vector3 p00 = Point(phi0, th0);
                    Vector3 p01 = Point(phi0, th1);
                    Vector3 p10 = Point(phi1, th0);
                    Vector3 p11 = Point(phi1, th1);

                    float j = (TerrainNoise.Hash(r * 3.7f, s * 5.3f, seed) - 0.5f) * 2f * colourJitter;
                    Color c = new(Mathf.Clamp01(colour.r + j), Mathf.Clamp01(colour.g + j),
                                  Mathf.Clamp01(colour.b + j), 1f);

                    if (r == 0) b.AddTriangle(p00, p11, p10, c);
                    else if (r == rings - 1) b.AddTriangle(p00, p01, p10, c);
                    else b.AddQuad(p00, p01, p11, p10, c);
                }
            }

            Vector3 Point(float phi, float theta)
            {
                var dir = new Vector3(
                    Mathf.Sin(phi) * Mathf.Cos(theta),
                    Mathf.Cos(phi),
                    Mathf.Sin(phi) * Mathf.Sin(theta));

                // Deform along the surface so the silhouette is irregular but still convex.
                float n = TerrainNoise.Fbm(dir.x * 2.3f + 10f, dir.z * 2.3f + dir.y * 1.7f + 10f, seed);
                float scale = 1f + (n - 0.5f) * 2f * lumpiness;
                return centre + Vector3.Scale(dir * scale, radii);
            }
        }

        /// <summary>Crossed quads for grass tufts, drawn with the foliage shader so they
        /// pick up the shared wind.</summary>
        public static void CrossedBlades(Builder b, Vector3 baseCentre, float width, float height,
                                         int blades, int seed, Color root, Color tip)
        {
            for (int i = 0; i < blades; i++)
            {
                float angle = (i / (float)blades) * Mathf.PI + TerrainNoise.Hash(i, seed, seed) * 0.7f;
                Vector3 right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (width * 0.5f);

                float h = height * (0.7f + TerrainNoise.Hash(i * 1.3f, seed * 0.7f, seed) * 0.6f);
                Vector3 lean = right.normalized * (h * 0.12f);

                Vector3 a = baseCentre - right;
                Vector3 d = baseCentre + right;
                Vector3 bb = baseCentre - right * 0.35f + Vector3.up * h + lean;
                Vector3 c = baseCentre + right * 0.35f + Vector3.up * h + lean;

                b.AddTriangle(a, bb, c, root);
                b.AddTriangle(a, c, d, tip);
            }
        }

        // ---- props ----------------------------------------------------------

        public static Mesh PineTree(int seed, Color trunk, Color needles)
        {
            var b = new Builder();
            float height = 1.5f + TerrainNoise.Hash(seed, 3f, seed) * 1.3f;
            float trunkHeight = height * 0.28f;

            Cylinder(b, Vector3.zero, 0.09f, 0.07f, trunkHeight, 6, trunk);

            // Three shrinking skirts give the classic stylised conifer read.
            int tiers = 3;
            for (int i = 0; i < tiers; i++)
            {
                float t = i / (float)tiers;
                float y = trunkHeight + height * 0.5f * t;
                float radius = Mathf.Lerp(0.46f, 0.18f, t);
                float tierHeight = Mathf.Lerp(0.6f, 0.42f, t) * height * 0.55f;

                Color c = Color.Lerp(needles * 0.82f, needles, t);
                c.a = 1f;
                Cone(b, new Vector3(0f, y, 0f), radius, tierHeight, 7, c, needles * 0.6f);
            }

            return b.Bake("PineTree");
        }

        public static Mesh BroadleafTree(int seed, Color trunk, Color canopy)
        {
            var b = new Builder();
            float trunkHeight = 0.55f + TerrainNoise.Hash(seed, 9f, seed) * 0.4f;
            Cylinder(b, Vector3.zero, 0.11f, 0.08f, trunkHeight, 6, trunk);

            // Three overlapping blobs read as a clumpy canopy rather than a lollipop.
            Blob(b, new Vector3(0f, trunkHeight + 0.42f, 0f), new Vector3(0.55f, 0.46f, 0.55f),
                 4, 7, 0.16f, seed, canopy);
            Blob(b, new Vector3(0.24f, trunkHeight + 0.28f, 0.14f), new Vector3(0.33f, 0.29f, 0.33f),
                 3, 6, 0.2f, seed + 31, canopy * 0.92f);
            Blob(b, new Vector3(-0.2f, trunkHeight + 0.34f, -0.18f), new Vector3(0.3f, 0.27f, 0.3f),
                 3, 6, 0.2f, seed + 77, canopy * 1.06f);

            return b.Bake("BroadleafTree");
        }

        public static Mesh Rock(int seed, Color colour)
        {
            var b = new Builder();
            float s = 0.22f + TerrainNoise.Hash(seed, 5f, seed) * 0.34f;
            Blob(b, new Vector3(0f, s * 0.6f, 0f), new Vector3(s, s * 0.72f, s * 0.9f),
                 3, 6, 0.3f, seed, colour, 0.08f);
            return b.Bake("Rock");
        }

        public static Mesh Bush(int seed, Color colour)
        {
            var b = new Builder();
            Blob(b, new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.25f, 0.3f), 3, 6, 0.24f, seed, colour);
            Blob(b, new Vector3(0.17f, 0.14f, 0.08f), new Vector3(0.2f, 0.17f, 0.2f), 3, 6, 0.26f,
                 seed + 13, colour * 0.9f);
            return b.Bake("Bush");
        }

        public static Mesh GrassTuft(int seed, Color root, Color tip)
        {
            var b = new Builder();
            CrossedBlades(b, Vector3.zero, 0.26f, 0.3f, 3, seed, root, tip);
            return b.Bake("GrassTuft");
        }

        public static Mesh Mushroom(int seed, Color stalk, Color cap)
        {
            var b = new Builder();
            Cylinder(b, Vector3.zero, 0.045f, 0.035f, 0.14f, 6, stalk);
            Blob(b, new Vector3(0f, 0.16f, 0f), new Vector3(0.13f, 0.08f, 0.13f), 2, 7, 0.08f, seed, cap);
            return b.Bake("Mushroom");
        }

        public static Mesh Crystal(int seed, Color colour)
        {
            var b = new Builder();
            for (int i = 0; i < 3; i++)
            {
                float a = TerrainNoise.Hash(seed + i, 2f, seed) * Mathf.PI * 2f;
                var offset = new Vector3(Mathf.Cos(a) * 0.09f, 0f, Mathf.Sin(a) * 0.09f);
                float h = 0.2f + TerrainNoise.Hash(seed + i * 5, 7f, seed) * 0.28f;
                Cone(b, offset, 0.07f, h, 5, colour, colour * 0.7f);
            }
            return b.Bake("Crystal");
        }

        /// <summary>Axis-aligned box between two corners. The building block for anything
        /// man-made in the diorama.</summary>
        public static void Box(Builder b, Vector3 min, Vector3 max, Color colour)
        {
            Vector3 a = new(min.x, min.y, min.z);
            Vector3 bb = new(max.x, min.y, min.z);
            Vector3 c = new(max.x, min.y, max.z);
            Vector3 d = new(min.x, min.y, max.z);
            Vector3 e = new(min.x, max.y, min.z);
            Vector3 f = new(max.x, max.y, min.z);
            Vector3 g = new(max.x, max.y, max.z);
            Vector3 h = new(min.x, max.y, max.z);

            Color side = colour;
            Color top = colour * 1.10f; top.a = 1f;
            Color bottom = colour * 0.72f; bottom.a = 1f;

            b.AddQuad(e, f, g, h, top);        // +Y
            b.AddQuad(d, c, bb, a, bottom);    // -Y
            b.AddQuad(a, bb, f, e, side);      // -Z
            b.AddQuad(c, d, h, g, side);       // +Z
            b.AddQuad(d, a, e, h, side);       // -X
            b.AddQuad(bb, c, g, f, side);      // +X
        }

        // ---- the mystery chest ---------------------------------------------
        // Sized in a 1-unit cube so the unboxing stage can scale it freely.

        const float ChestHalf = 0.42f;
        const float ChestBodyHeight = 0.44f;
        const float BandThickness = 0.035f;

        public static Mesh ChestBody(Color wood, Color metal)
        {
            var b = new Builder();

            // Planked body: five slats with a sliver of shadow between them, which reads
            // as carpentry rather than as a cube.
            const int planks = 5;
            float plankWidth = ChestHalf * 2f / planks;

            for (int i = 0; i < planks; i++)
            {
                float x0 = -ChestHalf + i * plankWidth + 0.006f;
                float x1 = x0 + plankWidth - 0.012f;
                Color shade = wood * (0.92f + (i % 2) * 0.12f);
                shade.a = 1f;

                Box(b, new Vector3(x0, 0f, -ChestHalf), new Vector3(x1, ChestBodyHeight, ChestHalf), shade);
            }

            // Iron banding around the girth and up the corners.
            Box(b, new Vector3(-ChestHalf - 0.012f, ChestBodyHeight * 0.30f, -ChestHalf - 0.012f),
                   new Vector3(ChestHalf + 0.012f, ChestBodyHeight * 0.30f + BandThickness, ChestHalf + 0.012f),
                   metal);

            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    var corner = new Vector3(sx * ChestHalf, 0f, sz * ChestHalf);
                    Box(b, corner + new Vector3(-0.045f * sx, 0f, -0.045f * sz) - new Vector3(0.012f, 0f, 0.012f),
                           corner + new Vector3(0.012f, ChestBodyHeight, 0.012f), metal);
                }
            }

            // Lock plate on the front face.
            Box(b, new Vector3(-0.09f, ChestBodyHeight * 0.42f, -ChestHalf - 0.03f),
                   new Vector3(0.09f, ChestBodyHeight * 0.94f, -ChestHalf - 0.012f), metal * 1.15f);

            return b.Bake("ChestBody");
        }

        /// <summary>Barrel-topped lid, pivoted at its own origin so it can be flung off.</summary>
        public static Mesh ChestLid(Color wood, Color metal)
        {
            var b = new Builder();

            const int segments = 7;
            const float radius = ChestHalf;
            const float lidHeight = 0.28f;

            // Half-barrel sweep from one long edge to the other.
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.PI * i / segments;
                float a1 = Mathf.PI * (i + 1) / segments;

                Vector3 p0 = new(-Mathf.Cos(a0) * radius, Mathf.Sin(a0) * lidHeight, 0f);
                Vector3 p1 = new(-Mathf.Cos(a1) * radius, Mathf.Sin(a1) * lidHeight, 0f);

                Vector3 f0 = p0 + Vector3.forward * -ChestHalf;
                Vector3 f1 = p1 + Vector3.forward * -ChestHalf;
                Vector3 b0 = p0 + Vector3.forward * ChestHalf;
                Vector3 b1 = p1 + Vector3.forward * ChestHalf;

                Color shade = wood * (0.88f + i / (float)segments * 0.24f);
                shade.a = 1f;

                b.AddQuad(f0, f1, b1, b0, shade);

                // End caps.
                b.AddTriangle(f0, new Vector3(0f, 0f, -ChestHalf), f1, wood * 0.8f);
                b.AddTriangle(b1, new Vector3(0f, 0f, ChestHalf), b0, wood * 0.8f);
            }

            // A single band over the crown.
            Box(b, new Vector3(-0.03f, -0.01f, -ChestHalf - 0.012f),
                   new Vector3(0.03f, lidHeight + 0.012f, ChestHalf + 0.012f), metal);

            return b.Bake("ChestLid");
        }

        /// <summary>The seam that glows brighter as the box builds up to opening. Drawn as
        /// a separate mesh so it can use an unlit additive material and be animated on its
        /// own without touching the chest's shading.</summary>
        public static Mesh ChestSeam()
        {
            var b = new Builder();
            const float y = ChestBodyHeight;
            const float t = 0.012f;
            float r = ChestHalf + 0.02f;

            Box(b, new Vector3(-r, y - t, -r), new Vector3(r, y + t, -r + t * 2f), Color.white);
            Box(b, new Vector3(-r, y - t, r - t * 2f), new Vector3(r, y + t, r), Color.white);
            Box(b, new Vector3(-r, y - t, -r), new Vector3(-r + t * 2f, y + t, r), Color.white);
            Box(b, new Vector3(r - t * 2f, y - t, -r), new Vector3(r, y + t, r), Color.white);

            return b.Bake("ChestSeam");
        }

        /// <summary>Where the lid sits when closed, in the chest's local space.</summary>
        public static Vector3 ChestLidAnchor => new(0f, ChestBodyHeight, 0f);

        /// <summary>A small pile of berries used as the tile stockpile marker.</summary>
        public static Mesh BerryPile(int seed, Color basket, Color berries)
        {
            var b = new Builder();
            Cylinder(b, Vector3.zero, 0.19f, 0.22f, 0.12f, 8, basket);
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 0.09f, 0.14f, Mathf.Sin(a) * 0.09f);
                Blob(b, p, Vector3.one * 0.055f, 2, 5, 0.1f, seed + i, berries);
            }
            Blob(b, new Vector3(0f, 0.17f, 0f), Vector3.one * 0.06f, 2, 5, 0.1f, seed + 9, berries);
            return b.Bake("BerryPile");
        }
    }
}
