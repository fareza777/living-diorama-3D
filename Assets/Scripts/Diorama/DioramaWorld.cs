using System.Collections.Generic;
using LivingDiorama.Data;
using LivingDiorama.Simulation;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>One unlocked square of the world.</summary>
    public sealed class DioramaTile
    {
        public Vector2Int Coord;
        public BiomeDefinition Biome;
        public GameObject Root;
        public readonly List<FoodNode> Food = new(4);
    }

    /// <summary>
    /// The diorama itself: a grid of tiles the player buys outward from the centre.
    /// Also the simulation's ground truth for height, bounds and water, which is why it
    /// implements IWorldSurface rather than the AI raycasting against colliders.
    /// </summary>
    public sealed class DioramaWorld : MonoBehaviour, IWorldSurface
    {
        readonly Dictionary<Vector2Int, DioramaTile> _tiles = new(32);

        GameDatabase _db;
        int _seed;
        float _tileSize;
        int _maxRing;

        Material _groundMaterial;
        Material _propMaterial;
        Material _foliageMaterial;
        Material _waterMaterial;

        MaterialPropertyBlock _waterBlock;

        static readonly int ShallowId = Shader.PropertyToID("_ShallowColor");
        static readonly int DeepId = Shader.PropertyToID("_DeepColor");

        public float TileSize => _tileSize;
        public int TileCount => _tiles.Count;
        public IReadOnlyDictionary<Vector2Int, DioramaTile> Tiles => _tiles;

        /// <summary>Raised whenever the footprint changes so the camera can reframe.</summary>
        public event System.Action<DioramaTile> TileBuilt;

        public void Initialise(GameDatabase db, int seed, Material ground, Material prop,
                               Material foliage, Material water)
        {
            _db = db;
            _seed = seed;
            _tileSize = db.progression.tileSize;
            _maxRing = db.progression.maxRingRadius;
            _groundMaterial = ground;
            _propMaterial = prop;
            _foliageMaterial = foliage;
            _waterMaterial = water;
            _waterBlock = new MaterialPropertyBlock();
            _pedestal = DioramaPedestal.Create(transform, prop);
        }

        DioramaPedestal _pedestal;

        /// <summary>Lowest point of any tile's soil slab, so the stand always meets it.</summary>
        float SlabBottom
        {
            get
            {
                float deepest = 0f;
                foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
                {
                    deepest = Mathf.Min(deepest, -TileMeshBuilder.SlabDepth - kv.Value.Biome.reliefHeight);
                }
                return deepest;
            }
        }

        // ---- grid -----------------------------------------------------------

        public static int RingOf(Vector2Int coord) =>
            Mathf.Max(Mathf.Abs(coord.x), Mathf.Abs(coord.y));

        public bool IsUnlocked(Vector2Int coord) => _tiles.ContainsKey(coord);

        public Vector3 TileOrigin(Vector2Int coord) =>
            new(coord.x * _tileSize, 0f, coord.y * _tileSize);

        public Vector3 TileCentre(Vector2Int coord) =>
            TileOrigin(coord) + new Vector3(_tileSize * 0.5f, 0f, _tileSize * 0.5f);

        public Vector2Int CoordAt(Vector3 world) => new(
            Mathf.FloorToInt(world.x / _tileSize),
            Mathf.FloorToInt(world.z / _tileSize));

        /// <summary>Coordinates the player could buy next: adjacent to something owned,
        /// not owned yet, and inside the ring cap.</summary>
        public List<Vector2Int> AvailableCoords(List<Vector2Int> results)
        {
            results.Clear();
            var offsets = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0),
                new Vector2Int(0, 1), new Vector2Int(0, -1),
            };

            foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
            {
                foreach (Vector2Int o in offsets)
                {
                    Vector2Int candidate = kv.Key + o;
                    if (_tiles.ContainsKey(candidate)) continue;
                    if (RingOf(candidate) > _maxRing) continue;
                    if (!results.Contains(candidate)) results.Add(candidate);
                }
            }
            return results;
        }

        public Bounds WorldBounds
        {
            get
            {
                if (_tiles.Count == 0) return new Bounds(Vector3.zero, Vector3.one * _tileSize);

                var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
                var max = new Vector3(float.MinValue, 0f, float.MinValue);

                foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
                {
                    Vector3 o = TileOrigin(kv.Key);
                    min = Vector3.Min(min, o);
                    max = Vector3.Max(max, o + new Vector3(_tileSize, 0f, _tileSize));
                }

                var bounds = new Bounds();
                bounds.SetMinMax(min, max);
                return bounds;
            }
        }

        // ---- building -------------------------------------------------------

        public DioramaTile BuildTile(Vector2Int coord, BiomeDefinition biome)
        {
            if (_tiles.TryGetValue(coord, out DioramaTile existing)) return existing;
            if (biome == null) return null;

            var root = new GameObject($"Tile_{coord.x}_{coord.y}_{biome.id}");
            root.transform.SetParent(transform, false);
            root.transform.position = TileOrigin(coord);

            var tile = new DioramaTile { Coord = coord, Biome = biome, Root = root };

            BuildGround(tile);
            if (biome.hasWater) BuildWater(tile);
            BuildScatter(tile);
            BuildFood(tile);

            _tiles[coord] = tile;
            if (_pedestal != null) _pedestal.Rebuild(WorldBounds, SlabBottom);

            TileBuilt?.Invoke(tile);
            return tile;
        }

        void BuildGround(DioramaTile tile)
        {
            var go = new GameObject("Ground");
            go.transform.SetParent(tile.Root.transform, false);

            Mesh mesh = TileMeshBuilder.BuildGround(tile.Biome, TileOrigin(tile.Coord), _tileSize, _seed);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _groundMaterial;
            mr.receiveShadows = true;
        }

        void BuildWater(DioramaTile tile)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(tile.Root.transform, false);
            go.transform.localPosition = new Vector3(0f, tile.Biome.waterLevel, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = TileMeshBuilder.BuildWater(_tileSize);

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // Per-biome tint without a material per biome.
            _waterBlock.Clear();
            _waterBlock.SetColor(ShallowId, tile.Biome.waterShallow);
            _waterBlock.SetColor(DeepId, tile.Biome.waterDeep);
            mr.SetPropertyBlock(_waterBlock);
        }

        /// <summary>
        /// Place props by rejection sampling, then merge each layer into a single mesh.
        /// Individual prop objects would mean hundreds of renderers per tile; merged, a
        /// fully dressed tile costs four draws.
        /// </summary>
        void BuildScatter(DioramaTile tile)
        {
            if (tile.Biome.scatter == null || tile.Biome.scatter.Length == 0) return;

            var solid = new List<CombineInstance>(128);
            var windSwept = new List<CombineInstance>(128);
            var placed = new List<Vector3>(128);

            Vector3 origin = TileOrigin(tile.Coord);
            float waterWidth = tile.Biome.hasWater ? _tileSize * 0.28f : 0f;

            for (int layer = 0; layer < tile.Biome.scatter.Length; layer++)
            {
                BiomeDefinition.ScatterEntry entry = tile.Biome.scatter[layer];
                if (entry.density <= 0f) continue;

                int attempts = Mathf.RoundToInt(_tileSize * _tileSize * entry.density * 2f);
                int layerSeed = _seed ^ (tile.Coord.x * 73856093) ^ (tile.Coord.y * 19349663) ^ (layer * 2654435761u).GetHashCode();
                placed.Clear();

                for (int i = 0; i < attempts; i++)
                {
                    float u = TerrainNoise.Hash(i * 1.7f, layer * 3.3f, layerSeed);
                    float v = TerrainNoise.Hash(i * 2.9f + 11f, layer * 5.1f, layerSeed + 17);

                    var local = new Vector3(u * _tileSize, 0f, v * _tileSize);
                    Vector3 world = origin + local;

                    if (waterWidth > 0.01f && !entry.allowInWater)
                    {
                        if (TerrainNoise.WaterBasin(world.x, world.z, _seed, waterWidth) > 0.12f) continue;
                    }

                    if (TooClose(placed, local, entry.minSpacing)) continue;

                    local.y = SampleHeight(world) - 0.02f;
                    placed.Add(local);

                    int propSeed = layerSeed + i * 131;
                    Mesh mesh = BuildProp(entry, propSeed);
                    if (mesh == null) continue;

                    float scale = Mathf.Lerp(
                        Mathf.Max(0.05f, entry.scaleRange.x),
                        Mathf.Max(0.05f, entry.scaleRange.y),
                        TerrainNoise.Hash(i * 4.4f, layer * 7.7f, layerSeed + 41));

                    float yaw = TerrainNoise.Hash(i * 6.1f, layer * 2.2f, layerSeed + 83) * 360f;

                    var ci = new CombineInstance
                    {
                        mesh = mesh,
                        transform = Matrix4x4.TRS(local, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale),
                    };

                    (entry.windSwept ? windSwept : solid).Add(ci);
                }
            }

            EmitCombined(tile, solid, "Props", _propMaterial, true);
            EmitCombined(tile, windSwept, "Foliage", _foliageMaterial, false);
        }

        static bool TooClose(List<Vector3> placed, Vector3 candidate, float minSpacing)
        {
            if (minSpacing <= 0f) return false;
            float sqr = minSpacing * minSpacing;
            for (int i = 0; i < placed.Count; i++)
            {
                Vector3 d = placed[i] - candidate;
                d.y = 0f;
                if (d.sqrMagnitude < sqr) return true;
            }
            return false;
        }

        static Mesh BuildProp(BiomeDefinition.ScatterEntry entry, int seed) => entry.kind switch
        {
            BiomeDefinition.PropKind.PineTree => ProceduralMeshes.PineTree(seed, entry.secondary, entry.primary),
            BiomeDefinition.PropKind.BroadleafTree => ProceduralMeshes.BroadleafTree(seed, entry.secondary, entry.primary),
            BiomeDefinition.PropKind.Rock => ProceduralMeshes.Rock(seed, entry.primary),
            BiomeDefinition.PropKind.Bush => ProceduralMeshes.Bush(seed, entry.primary),
            BiomeDefinition.PropKind.GrassTuft => ProceduralMeshes.GrassTuft(seed, entry.primary, entry.secondary),
            BiomeDefinition.PropKind.Mushroom => ProceduralMeshes.Mushroom(seed, entry.secondary, entry.primary),
            BiomeDefinition.PropKind.Crystal => ProceduralMeshes.Crystal(seed, entry.primary),
            _ => null,
        };

        void EmitCombined(DioramaTile tile, List<CombineInstance> parts, string name,
                          Material material, bool castShadows)
        {
            if (parts.Count == 0) return;

            var mesh = new Mesh { name = $"{name}_{tile.Coord.x}_{tile.Coord.y}" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(tile.Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            // The source meshes were only ever scratch data for the combine.
            for (int i = 0; i < parts.Count; i++) Destroy(parts[i].mesh);
            parts.Clear();
        }

        void BuildFood(DioramaTile tile)
        {
            int count = tile.Biome.foodNodes;
            Vector3 origin = TileOrigin(tile.Coord);
            float waterWidth = tile.Biome.hasWater ? _tileSize * 0.28f : 0f;

            for (int i = 0; i < count; i++)
            {
                int seed = _seed ^ (tile.Coord.x * 6151) ^ (tile.Coord.y * 3121) ^ (i * 911);

                Vector3 local = Vector3.zero;
                bool found = false;
                for (int attempt = 0; attempt < 12 && !found; attempt++)
                {
                    float u = TerrainNoise.Hash(i * 3.7f + attempt, 13f, seed);
                    float v = TerrainNoise.Hash(i * 8.3f + attempt, 29f, seed + 5);
                    local = new Vector3(Mathf.Lerp(1f, _tileSize - 1f, u), 0f,
                                        Mathf.Lerp(1f, _tileSize - 1f, v));

                    Vector3 world = origin + local;
                    found = waterWidth <= 0.01f ||
                            TerrainNoise.WaterBasin(world.x, world.z, _seed, waterWidth) < 0.08f;
                }

                local.y = SampleHeight(origin + local);

                var go = new GameObject(i == 0 ? "Stockpile" : $"Food_{i}");
                go.transform.SetParent(tile.Root.transform, false);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.Euler(0f, TerrainNoise.Hash(i, 2f, seed) * 360f, 0f);

                var visual = new GameObject("Bounty");
                visual.transform.SetParent(go.transform, false);

                // The stockpile is a basket of berries; ordinary nodes are a bush the
                // creature grazes from. Different silhouettes so theft reads clearly.
                Mesh mesh = i == 0
                    ? ProceduralMeshes.BerryPile(seed, new Color(0.55f, 0.36f, 0.20f), new Color(0.82f, 0.18f, 0.28f))
                    : ProceduralMeshes.Bush(seed, tile.Biome.groundHigh * 0.85f);

                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = _propMaterial;
                visual.transform.localScale = Vector3.one * (i == 0 ? 1.15f : 0.9f);

                var node = go.AddComponent<FoodNode>();
                node.isStockpile = i == 0;
                node.servings = i == 0 ? 6 : 3;
                node.regrowHours = i == 0 ? 3.5f : 2.5f;
                node.feeds = tile.Biome.foodDiets;
                node.bounty = visual;

                tile.Food.Add(node);
            }
        }

        // ---- IWorldSurface --------------------------------------------------

        public float SampleHeight(Vector3 world)
        {
            BiomeDefinition biome = BiomeAt(world);
            if (biome == null) return 0f;

            float waterWidth = biome.hasWater ? _tileSize * 0.28f : 0f;
            return TerrainNoise.Height(world.x, world.z, _seed, biome.reliefHeight,
                                       biome.reliefScale, waterWidth, biome.waterLevel);
        }

        public BiomeDefinition BiomeAt(Vector3 world)
        {
            return _tiles.TryGetValue(CoordAt(world), out DioramaTile tile) ? tile.Biome : null;
        }

        public bool Contains(Vector3 world)
        {
            if (!_tiles.TryGetValue(CoordAt(world), out _)) return false;

            // Keep a small margin so creatures never stand exactly on a tile seam.
            Vector2Int coord = CoordAt(world);
            Vector3 local = world - TileOrigin(coord);
            const float margin = 0.35f;
            return local.x >= margin && local.x <= _tileSize - margin &&
                   local.z >= margin && local.z <= _tileSize - margin;
        }

        public Vector3 ClampInside(Vector3 world)
        {
            if (Contains(world)) return world;
            if (_tiles.Count == 0) return world;

            // Walk back towards the nearest tile centre until we are legal again.
            Vector3 best = world;
            float bestDistance = float.MaxValue;

            foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
            {
                Vector3 centre = TileCentre(kv.Key);
                Vector3 origin = TileOrigin(kv.Key);
                const float margin = 0.4f;

                var clamped = new Vector3(
                    Mathf.Clamp(world.x, origin.x + margin, origin.x + _tileSize - margin),
                    world.y,
                    Mathf.Clamp(world.z, origin.z + margin, origin.z + _tileSize - margin));

                float d = (clamped - world).sqrMagnitude + (centre - world).sqrMagnitude * 0.001f;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = clamped;
                }
            }

            best.y = SampleHeight(best);
            return best;
        }

        public Vector3 RandomPoint(Vector3 near, float radius)
        {
            for (int i = 0; i < 10; i++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                var candidate = new Vector3(near.x + offset.x, 0f, near.z + offset.y);

                if (!Contains(candidate)) continue;
                // Do not send land creatures wading into the deep channel.
                if (TryGetWater(candidate, out float surface) && SampleHeight(candidate) < surface - 0.3f) continue;

                candidate.y = SampleHeight(candidate);
                return candidate;
            }

            return ClampInside(near);
        }

        public bool TryGetWater(Vector3 world, out float surfaceY)
        {
            surfaceY = 0f;
            BiomeDefinition biome = BiomeAt(world);
            if (biome == null || !biome.hasWater) return false;

            float basin = TerrainNoise.WaterBasin(world.x, world.z, _seed, _tileSize * 0.28f);
            if (basin <= 0.05f) return false;

            surfaceY = biome.waterLevel;
            return true;
        }

        public bool TryFindWaterEdge(Vector3 from, float maxDistance, out Vector3 point)
        {
            point = from;
            // Spiral outward looking for the shallow band just inside the waterline.
            const int rays = 12;
            int steps = Mathf.Max(3, Mathf.RoundToInt(maxDistance / 0.6f));

            for (int s = 1; s <= steps; s++)
            {
                float radius = maxDistance * s / steps;
                for (int r = 0; r < rays; r++)
                {
                    float angle = (r / (float)rays + s * 0.13f) * Mathf.PI * 2f;
                    var candidate = new Vector3(
                        from.x + Mathf.Cos(angle) * radius, 0f,
                        from.z + Mathf.Sin(angle) * radius);

                    if (!Contains(candidate)) continue;
                    if (!TryGetWater(candidate, out float surface)) continue;

                    float ground = SampleHeight(candidate);
                    // Ankle deep: in the water, but not swimming.
                    if (ground > surface - 0.28f && ground < surface - 0.02f)
                    {
                        candidate.y = ground;
                        point = candidate;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
