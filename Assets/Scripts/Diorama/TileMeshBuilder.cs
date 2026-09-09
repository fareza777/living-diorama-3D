using System.Collections.Generic;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// Builds the faceted low-poly mesh for one diorama tile.
    ///
    /// Two things make this read as a museum diorama rather than as terrain: every
    /// triangle carries its own flat normal, so the surface is crisply faceted; and each
    /// tile is a solid slab with visible soil strata down its sides, so the world has a
    /// cut edge and sits on something. Infinite ground would read as a landscape; a slab
    /// with a rim reads as an object on a stand.
    /// </summary>
    public static class TileMeshBuilder
    {
        public const int Resolution = 14;

        /// <summary>How deep the soil slab goes below the lowest terrain point.</summary>
        public const float SlabDepth = 1.35f;

        static readonly List<Vector3> Vertices = new(8192);
        static readonly List<Vector3> Normals = new(8192);
        static readonly List<Color> Colors = new(8192);
        static readonly List<Vector2> Uvs = new(8192);
        static readonly List<int> Triangles = new(8192);

        struct TileContext
        {
            public BiomeDefinition Biome;
            public Vector3 Origin;
            public float Size;
            public int Seed;
            public float WaterWidth;
            public float SlabBottom;
        }

        public static Mesh BuildGround(BiomeDefinition biome, Vector3 tileOrigin, float tileSize, int seed)
        {
            Vertices.Clear();
            Normals.Clear();
            Colors.Clear();
            Uvs.Clear();
            Triangles.Clear();

            var ctx = new TileContext
            {
                Biome = biome,
                Origin = tileOrigin,
                Size = tileSize,
                Seed = seed,
                WaterWidth = biome.hasWater ? tileSize * 0.28f : 0f,
            };
            ctx.SlabBottom = -SlabDepth - biome.reliefHeight;

            BuildSurface(in ctx);
            BuildSkirt(in ctx);
            BuildFloor(in ctx);

            var mesh = new Mesh { name = $"Tile_{biome.id}" };
            mesh.indexFormat = Vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colors);
            mesh.SetUVs(0, Uvs);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- top surface ----------------------------------------------------

        static void BuildSurface(in TileContext ctx)
        {
            float step = ctx.Size / Resolution;

            for (int gz = 0; gz < Resolution; gz++)
            {
                for (int gx = 0; gx < Resolution; gx++)
                {
                    float x0 = ctx.Origin.x + gx * step;
                    float z0 = ctx.Origin.z + gz * step;
                    float x1 = x0 + step;
                    float z1 = z0 + step;

                    Vector3 a = Corner(x0, z0, in ctx);
                    Vector3 b = Corner(x1, z0, in ctx);
                    Vector3 c = Corner(x0, z1, in ctx);
                    Vector3 d = Corner(x1, z1, in ctx);

                    // Alternate the diagonal in a checker pattern so the faceting does not
                    // form a visible directional grain across the whole tile.
                    if (((gx + gz) & 1) == 0)
                    {
                        AddSurfaceTriangle(a, c, b, in ctx);
                        AddSurfaceTriangle(b, c, d, in ctx);
                    }
                    else
                    {
                        AddSurfaceTriangle(a, c, d, in ctx);
                        AddSurfaceTriangle(a, d, b, in ctx);
                    }
                }
            }
        }

        /// <summary>
        /// Height of the ground mesh at a point, as drawn.
        ///
        /// The terrain is faceted: vertices are sampled on a coarse grid and the triangles
        /// between them are flat. Asking the noise function directly gives the height of a
        /// smooth surface that is not the one on screen, so creatures placed by it stood
        /// above the facets on every rise -- the "floating just off the ground" look. This
        /// walks the same grid cell and the same diagonal the mesh builder chose, and
        /// interpolates across the actual triangle.
        /// </summary>
        public static float SampleSurface(BiomeDefinition biome, Vector3 tileOrigin,
                                          float tileSize, int seed, float worldX, float worldZ)
        {
            float step = tileSize / Resolution;
            float waterWidth = biome.hasWater ? tileSize * 0.28f : 0f;

            float localX = Mathf.Clamp(worldX - tileOrigin.x, 0f, tileSize);
            float localZ = Mathf.Clamp(worldZ - tileOrigin.z, 0f, tileSize);

            int gx = Mathf.Clamp((int)(localX / step), 0, Resolution - 1);
            int gz = Mathf.Clamp((int)(localZ / step), 0, Resolution - 1);

            float x0 = tileOrigin.x + gx * step;
            float z0 = tileOrigin.z + gz * step;

            float H(float wx, float wz) => TerrainNoise.Height(
                wx, wz, seed, biome.reliefHeight, biome.reliefScale, waterWidth, biome.waterLevel);

            float ha = H(x0, z0);
            float hb = H(x0 + step, z0);
            float hc = H(x0, z0 + step);
            float hd = H(x0 + step, z0 + step);

            // Position within the cell, 0..1 on each axis.
            float u = Mathf.Clamp01((localX - gx * step) / step);
            float v = Mathf.Clamp01((localZ - gz * step) / step);

            // The builder alternates the diagonal in a checker pattern; follow it exactly,
            // or the interpolation is right on half the cells and wrong on the other half.
            if (((gx + gz) & 1) == 0)
            {
                // Triangles (a, c, b) and (b, c, d): the split runs from b to c.
                return u + v <= 1f
                    ? ha + (hb - ha) * u + (hc - ha) * v
                    : hd + (hc - hd) * (1f - u) + (hb - hd) * (1f - v);
            }

            // Triangles (a, c, d) and (a, d, b): the split runs from a to d.
            return v >= u
                ? ha + (hd - hc) * u + (hc - ha) * v
                : ha + (hb - ha) * u + (hd - hb) * v;
        }

        /// <summary>A point on the ground, in tile-local space.</summary>
        static Vector3 Corner(float worldX, float worldZ, in TileContext ctx)
        {
            float y = TerrainNoise.Height(worldX, worldZ, ctx.Seed, ctx.Biome.reliefHeight,
                                          ctx.Biome.reliefScale, ctx.WaterWidth, ctx.Biome.waterLevel);

            return new Vector3(worldX - ctx.Origin.x, y, worldZ - ctx.Origin.z);
        }

        /// <summary>Top of the slab wall at a point on the tile border.
        ///
        /// Where the river reaches the edge the riverbed is below the waterline, so a wall
        /// that stopped at the ground left the water plane projecting past the slab with
        /// daylight underneath it -- a blue lip hanging in mid-air. The wall carries on up
        /// to the surface of the water instead, which is also what the cut face of a real
        /// diorama looks like: soil holding the water in.
        ///
        /// This is the wall only. Raising the ground itself would fill the riverbed in.</summary>
        static Vector3 SkirtCorner(float worldX, float worldZ, in TileContext ctx)
        {
            Vector3 point = Corner(worldX, worldZ, in ctx);
            if (ctx.WaterWidth > 0.01f) point.y = Mathf.Max(point.y, ctx.Biome.waterLevel);
            return point;
        }

        static void AddSurfaceTriangle(Vector3 a, Vector3 b, Vector3 c, in TileContext ctx)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0f) normal = -normal;

            int baseIndex = Vertices.Count;
            AddSurfaceVertex(a, normal, in ctx);
            AddSurfaceVertex(b, normal, in ctx);
            AddSurfaceVertex(c, normal, in ctx);

            Triangles.Add(baseIndex);
            Triangles.Add(baseIndex + 1);
            Triangles.Add(baseIndex + 2);
        }

        static void AddSurfaceVertex(Vector3 local, Vector3 normal, in TileContext ctx)
        {
            Vertices.Add(local);
            Normals.Add(normal);
            Uvs.Add(new Vector2(local.x / ctx.Size, local.z / ctx.Size));

            float worldX = local.x + ctx.Origin.x;
            float worldZ = local.z + ctx.Origin.z;

            float span = Mathf.Max(0.001f, ctx.Biome.reliefHeight * 2.4f);
            float t = Mathf.InverseLerp(-span, span, local.y);
            Color colour = Color.Lerp(ctx.Biome.groundLow, ctx.Biome.groundHigh, t);

            // The riverbed gets a darker, wetter tint so the water reads as sitting in
            // something rather than painted on top.
            if (ctx.WaterWidth > 0.01f)
            {
                float basin = TerrainNoise.WaterBasin(worldX, worldZ, ctx.Seed, ctx.WaterWidth);
                if (basin > 0f)
                {
                    Color bed = ctx.Biome.groundLow * 0.55f;
                    bed.a = 1f;
                    colour = Color.Lerp(colour, bed, basin);
                }
            }

            Colors.Add(Jitter(colour, worldX, worldZ, ctx.Seed, 0.05f));
        }

        // ---- slab sides -----------------------------------------------------

        /// <summary>
        /// Extrude the tile border down to the slab floor, banding the colour by depth so
        /// the cut edge shows topsoil over subsoil over bedrock. This single detail is
        /// what sells the exhibition-piece framing.
        /// </summary>
        static void BuildSkirt(in TileContext ctx)
        {
            float step = ctx.Size / Resolution;
            float size = ctx.Size;

            for (int i = 0; i < Resolution; i++)
            {
                float t0 = i * step;
                float t1 = t0 + step;

                // South edge (z = 0), outward normal -Z.
                AddSkirtQuad(SkirtCorner(ctx.Origin.x + t1, ctx.Origin.z, in ctx),
                             SkirtCorner(ctx.Origin.x + t0, ctx.Origin.z, in ctx), in ctx);

                // North edge (z = size), outward normal +Z.
                AddSkirtQuad(SkirtCorner(ctx.Origin.x + t0, ctx.Origin.z + size, in ctx),
                             SkirtCorner(ctx.Origin.x + t1, ctx.Origin.z + size, in ctx), in ctx);

                // West edge (x = 0), outward normal -X.
                AddSkirtQuad(SkirtCorner(ctx.Origin.x, ctx.Origin.z + t0, in ctx),
                             SkirtCorner(ctx.Origin.x, ctx.Origin.z + t1, in ctx), in ctx);

                // East edge (x = size), outward normal +X.
                AddSkirtQuad(SkirtCorner(ctx.Origin.x + size, ctx.Origin.z + t1, in ctx),
                             SkirtCorner(ctx.Origin.x + size, ctx.Origin.z + t0, in ctx), in ctx);
            }
        }

        /// <summary>One wall segment, split into three bands so the strata are distinct
        /// rather than a smooth gradient.</summary>
        static void AddSkirtQuad(Vector3 topLeft, Vector3 topRight, in TileContext ctx)
        {
            const int bands = 3;
            float bottom = ctx.SlabBottom;

            for (int b = 0; b < bands; b++)
            {
                float k0 = b / (float)bands;
                float k1 = (b + 1) / (float)bands;

                Vector3 l0 = new(topLeft.x, Mathf.Lerp(topLeft.y, bottom, k0), topLeft.z);
                Vector3 l1 = new(topLeft.x, Mathf.Lerp(topLeft.y, bottom, k1), topLeft.z);
                Vector3 r0 = new(topRight.x, Mathf.Lerp(topRight.y, bottom, k0), topRight.z);
                Vector3 r1 = new(topRight.x, Mathf.Lerp(topRight.y, bottom, k1), topRight.z);

                Color colour = StrataColour(ctx.Biome, b, bands);
                AddSideTriangle(l0, l1, r0, colour, in ctx);
                AddSideTriangle(r0, l1, r1, colour, in ctx);
            }
        }

        static Color StrataColour(BiomeDefinition biome, int band, int bands)
        {
            // Topsoil borrows the surface colour so the rim never looks bolted on; below
            // that it desaturates and cools towards rock.
            float k = band / Mathf.Max(1f, bands - 1f);

            Color topsoil = biome.groundLow * 0.85f;
            Color subsoil = new(0.34f, 0.26f, 0.20f, 1f);
            Color bedrock = biome.cliffColour * 0.75f;

            Color result = k < 0.5f
                ? Color.Lerp(topsoil, subsoil, k * 2f)
                : Color.Lerp(subsoil, bedrock, (k - 0.5f) * 2f);

            result.a = 1f;
            return result;
        }

        static void AddSideTriangle(Vector3 a, Vector3 b, Vector3 c, Color colour, in TileContext ctx)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-10f) return;
            normal.Normalize();

            int baseIndex = Vertices.Count;
            PushSide(a, normal, colour, in ctx);
            PushSide(b, normal, colour, in ctx);
            PushSide(c, normal, colour, in ctx);

            Triangles.Add(baseIndex);
            Triangles.Add(baseIndex + 1);
            Triangles.Add(baseIndex + 2);
        }

        static void PushSide(Vector3 p, Vector3 normal, Color colour, in TileContext ctx)
        {
            Vertices.Add(p);
            Normals.Add(normal);
            // Park the sides in the middle of UV space so the shader's tile-seam darkening,
            // which keys off the UV border, leaves them alone.
            Uvs.Add(new Vector2(0.5f, 0.5f));
            Colors.Add(Jitter(colour, p.x + ctx.Origin.x, p.y * 7f, ctx.Seed + 313, 0.045f));
        }

        static void BuildFloor(in TileContext ctx)
        {
            float size = ctx.Size;
            float y = ctx.SlabBottom;
            var colour = new Color(0.16f, 0.14f, 0.14f, 1f);

            Vector3 a = new(0f, y, 0f);
            Vector3 b = new(size, y, 0f);
            Vector3 c = new(size, y, size);
            Vector3 d = new(0f, y, size);

            // Wound so the face points down.
            AddSideTriangle(a, b, c, colour, in ctx);
            AddSideTriangle(a, c, d, colour, in ctx);
        }

        static Color Jitter(Color colour, float x, float y, int seed, float amount)
        {
            float j = TerrainNoise.Hash(x * 3.1f, y * 3.1f, seed) * amount * 2f - amount;
            return new Color(
                Mathf.Clamp01(colour.r + j),
                Mathf.Clamp01(colour.g + j),
                Mathf.Clamp01(colour.b + j),
                1f);
        }

        // ---- water ----------------------------------------------------------

        /// <summary>
        /// The water surface, built only where there is actually water.
        ///
        /// This used to be one flat sheet over the whole tile. Anywhere the ground
        /// happened to dip below the waterline -- a hollow between two hills, a dozen
        /// metres from the river -- a puddle appeared out of nowhere, and the sheet ran
        /// right out to the tile edges. Emitting a cell only when its corners are under
        /// water keeps the river in its bed.
        /// </summary>
        public static Mesh BuildWater(BiomeDefinition biome, Vector3 tileOrigin, float tileSize, int seed)
        {
            var mesh = new Mesh { name = "TileWater" };
            const int res = 24;

            float step = tileSize / res;
            float waterWidth = biome.hasWater ? tileSize * 0.28f : 0f;

            var verts = new List<Vector3>((res + 1) * (res + 1));
            var uvs = new List<Vector2>(verts.Capacity);
            var norms = new List<Vector3>(verts.Capacity);
            var tris = new List<int>(res * res * 6);

            float Ground(int gx, int gz) => TerrainNoise.Height(
                tileOrigin.x + gx * step, tileOrigin.z + gz * step, seed,
                biome.reliefHeight, biome.reliefScale, waterWidth, biome.waterLevel);

            for (int z = 0; z <= res; z++)
            {
                for (int x = 0; x <= res; x++)
                {
                    verts.Add(new Vector3(x * step, 0f, z * step));
                    uvs.Add(new Vector2((float)x / res, (float)z / res));
                    norms.Add(Vector3.up);
                }
            }

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    // Two submerged corners, not one.
                    //
                    // Accepting a single wet corner pushed the surface a whole cell past
                    // the bank wherever the shoreline clipped a corner, which is what left
                    // water poking out into the grass. Two keeps the sheet inside the
                    // channel while still reaching the water's edge.
                    int wet = 0;
                    if (Ground(x, z) < biome.waterLevel) wet++;
                    if (Ground(x + 1, z) < biome.waterLevel) wet++;
                    if (Ground(x, z + 1) < biome.waterLevel) wet++;
                    if (Ground(x + 1, z + 1) < biome.waterLevel) wet++;
                    if (wet < 2) continue;

                    int i = z * (res + 1) + x;
                    tris.Add(i);
                    tris.Add(i + res + 1);
                    tris.Add(i + 1);
                    tris.Add(i + 1);
                    tris.Add(i + res + 1);
                    tris.Add(i + res + 2);
                }
            }

            if (tris.Count == 0) return null;

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

    }
}
