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

        /// <summary>Trunks and boulders creatures have to walk around, as world-space
        /// XZ centres with a radius. Foliage is not in here: pushing out of every blade
        /// of grass would cost more than it is worth and look worse.</summary>
        public readonly List<Vector3> Obstacles = new(64);

        /// <summary>Height of the tallest thing standing on this tile. The camera has to
        /// frame the trees, not just the ground they are rooted in.</summary>
        public float CanopyTop;
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
            _pedestal = DioramaPedestal.Create(transform, prop);
        }

        DioramaPedestal _pedestal;

        /// <summary>Roughly how far the stand hangs below the soil slab.</summary>
        const float PedestalDrop = 0.75f;

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

        /// <summary>
        /// What the camera should frame: the land and its soil slab, without the stand.
        ///
        /// WorldBounds includes the plinth hanging below, and aiming at the middle of that
        /// puts the terrain -- the part worth looking at -- noticeably above the centre of
        /// the screen with a wall of sky over it. The stand is framing, not subject; it is
        /// allowed to sit low in the shot.
        /// </summary>
        public Bounds FramingBounds
        {
            get
            {
                Bounds bounds = WorldBounds;

                // Up to the treetops. Framing to the ground plane cropped the canopies
                // off the moment the scenery stopped being knee-high.
                float top = 0f;
                foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
                {
                    top = Mathf.Max(top, kv.Value.CanopyTop);
                }

                float bottom = SlabBottom;

                var min = new Vector3(bounds.min.x, bottom, bounds.min.z);
                var max = new Vector3(bounds.max.x, top, bounds.max.z);

                var framing = new Bounds();
                framing.SetMinMax(min, max);
                return framing;
            }
        }

        public Bounds WorldBounds
        {
            get
            {
                if (_tiles.Count == 0) return new Bounds(Vector3.zero, Vector3.one * _tileSize);

                // The vertical extent matters: the diorama is a slab on a plinth, and a
                // flat footprint puts the framing pivot well above the object's real
                // centre, which is what drops the whole thing into the bottom of frame.
                float top = 0f;
                float bottom = SlabBottom - PedestalDrop;

                var min = new Vector3(float.MaxValue, bottom, float.MaxValue);
                var max = new Vector3(float.MinValue, top, float.MinValue);

                foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
                {
                    Vector3 o = TileOrigin(kv.Key);
                    min = new Vector3(Mathf.Min(min.x, o.x), bottom, Mathf.Min(min.z, o.z));
                    max = new Vector3(Mathf.Max(max.x, o.x + _tileSize), top,
                                      Mathf.Max(max.z, o.z + _tileSize));
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

        readonly Dictionary<BiomeDefinition, Material> _waterMaterials = new(8);

        Material WaterMaterialFor(BiomeDefinition biome)
        {
            if (_waterMaterials.TryGetValue(biome, out Material cached) && cached != null) return cached;

            var material = new Material(_waterMaterial) { name = $"Water_{biome.id}" };
            material.SetColor(ShallowId, biome.waterShallow);
            material.SetColor(DeepId, biome.waterDeep);

            _waterMaterials[biome] = material;
            return material;
        }

        void BuildWater(DioramaTile tile)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(tile.Root.transform, false);
            go.transform.localPosition = new Vector3(0f, tile.Biome.waterLevel, 0f);

            Mesh mesh = TileMeshBuilder.BuildWater(tile.Biome, TileOrigin(tile.Coord), _tileSize, _seed);
            if (mesh == null)
            {
                // No cell on this tile is under water after all.
                Destroy(go);
                return;
            }

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // A material per biome, not a property block.
            //
            // The water shader is SRP Batcher compatible, and the batcher ignores
            // per-renderer property blocks -- so every river was drawn in the material's
            // default colours rather than its own biome's. There are a handful of biomes,
            // so a material each costs nothing.
            mr.sharedMaterial = WaterMaterialFor(tile.Biome);
        }

        /// <summary>
        /// Place props by rejection sampling, then merge each layer into a single mesh.
        /// Individual prop objects would mean hundreds of renderers per tile; merged, a
        /// fully dressed tile costs four draws.
        /// </summary>
        void BuildScatter(DioramaTile tile)
        {
            if (tile.Biome.scatter == null || tile.Biome.scatter.Length == 0) return;

            // Everything the scatter emits hangs off one node, so upgrading to modelled
            // scenery later is a matter of throwing that node away and running again.
            Transform previous = tile.Root.transform.Find("Scatter");
            if (previous != null) Destroy(previous.gameObject);

            var scatterRoot = new GameObject("Scatter");
            scatterRoot.transform.SetParent(tile.Root.transform, false);
            tile.Obstacles.Clear();
            tile.CanopyTop = 0f;

            var solid = new List<CombineInstance>(128);
            var windSwept = new List<CombineInstance>(128);
            var placed = new List<Vector3>(128);

            // One bucket per modelled kind: a combined mesh has a single material, and
            // each model carries its own texture.
            var modelled = new Dictionary<BiomeDefinition.PropKind, List<CombineInstance>>(4);

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

                    // Keep dry-land props out of the river.
                    //
                    // The old test asked the basin function whether this was "river-ish",
                    // which is not the same question as whether the ground here is under
                    // water -- so trees grew along the bank and then stood waist deep in
                    // the stream. Comparing the ground against the waterline is the actual
                    // question, and it is the same surface the water plane is drawn at.
                    if (tile.Biome.hasWater && !entry.allowInWater)
                    {
                        if (SampleHeight(world) < tile.Biome.waterLevel + 0.04f) continue;
                    }

                    if (TooClose(placed, local, entry.minSpacing)) continue;

                    local.y = SampleHeight(world) - 0.02f;
                    placed.Add(local);

                    int propSeed = layerSeed + i * 131;

                    bool isModel = Props != null && Props.Has(entry.kind);
                    Mesh mesh = isModel ? Props.Mesh(entry.kind) : BuildProp(entry, propSeed);
                    if (mesh == null) continue;

                    float scale = Mathf.Lerp(
                        Mathf.Max(0.05f, entry.scaleRange.x),
                        Mathf.Max(0.05f, entry.scaleRange.y),
                        TerrainNoise.Hash(i * 4.4f, layer * 7.7f, layerSeed + 41));

                    // Library meshes are normalised to one unit tall, so they have to be
                    // put back to the size the generated prop would have been. Measuring
                    // that rather than writing it down means the two cannot drift: a
                    // mushroom came out as tall as a pine when the scale was assumed.
                    if (isModel) scale *= NaturalHeight(entry);

                    float blocking = BlockingRadius(entry.kind) * scale;
                    if (blocking > 0f)
                    {
                        tile.Obstacles.Add(new Vector3(world.x, blocking, world.z));
                    }

                    float yaw = TerrainNoise.Hash(i * 6.1f, layer * 2.2f, layerSeed + 83) * 360f;

                    var ci = new CombineInstance
                    {
                        mesh = mesh,
                        transform = Matrix4x4.TRS(local, Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale),
                    };

                    tile.CanopyTop = Mathf.Max(tile.CanopyTop, local.y + mesh.bounds.size.y * scale);

                    if (isModel)
                    {
                        if (!modelled.TryGetValue(entry.kind, out List<CombineInstance> bucket))
                        {
                            bucket = new List<CombineInstance>(64);
                            modelled[entry.kind] = bucket;
                        }
                        bucket.Add(ci);
                    }
                    else
                    {
                        (entry.windSwept ? windSwept : solid).Add(ci);
                    }
                }
            }

            EmitCombined(scatterRoot.transform, solid, "Props", _propMaterial, true, true);
            EmitCombined(scatterRoot.transform, windSwept, "Foliage", _foliageMaterial, false, true);

            foreach (KeyValuePair<BiomeDefinition.PropKind, List<CombineInstance>> kv in modelled)
            {
                // The library's meshes are shared between every instance and every tile,
                // so this path must not destroy its sources the way the generated one does.
                EmitCombined(scatterRoot.transform, kv.Value, kv.Key.ToString(),
                             ModelledMaterial(kv.Key), true, disposeSources: false);
            }
        }

        /// <summary>How wide a prop is at knee height, which is all a walking creature
        /// cares about. A pine is a trunk, not a canopy: blocking the whole crown would
        /// have creatures swerving around thin air.</summary>
        static float BlockingRadius(BiomeDefinition.PropKind kind) => kind switch
        {
            BiomeDefinition.PropKind.PineTree => 0.15f,
            BiomeDefinition.PropKind.BroadleafTree => 0.17f,
            BiomeDefinition.PropKind.Rock => 0.34f,
            BiomeDefinition.PropKind.Crystal => 0.22f,
            _ => 0f,
        };

        /// <summary>
        /// Push a position out of any solid scenery it has walked into.
        ///
        /// Creatures used to walk straight through trunks and boulders, which undoes the
        /// illusion faster than almost anything else: the world stops being a place and
        /// becomes a picture. Resolving the overlap after the move keeps the steering
        /// simple -- the brain never has to know the scenery is there.
        /// </summary>
        public Vector3 ResolveObstacles(Vector3 world, float radius)
        {
            if (!_tiles.TryGetValue(CoordAt(world), out DioramaTile tile)) return world;

            List<Vector3> obstacles = tile.Obstacles;
            for (int i = 0; i < obstacles.Count; i++)
            {
                Vector3 o = obstacles[i];
                float minimum = o.y + radius;

                float dx = world.x - o.x;
                float dz = world.z - o.z;
                float sqr = dx * dx + dz * dz;
                if (sqr >= minimum * minimum) continue;

                float distance = Mathf.Sqrt(sqr);
                if (distance < 0.0001f)
                {
                    // Dead centre: any direction will do, so pick a stable one.
                    world.x = o.x + minimum;
                    continue;
                }

                float push = (minimum - distance) / distance;
                world.x += dx * push;
                world.z += dz * push;
            }

            return world;
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

        readonly Dictionary<BiomeDefinition.PropKind, float> _naturalHeight = new(8);

        /// <summary>How tall the generated version of a prop stands, measured once.</summary>
        /// <summary>
        /// How tall a grazing bush should stand, in world units.
        ///
        /// Taken from the biome's own bush scatter entry, so a food bush is the size of
        /// the bushes growing around it. Library meshes are normalised to unit height, so
        /// without this a food bush would be exactly one metre tall regardless of what
        /// the rest of the plants in that biome look like.
        /// </summary>
        float BushHeight(BiomeDefinition biome)
        {
            const float fallback = 0.62f;
            if (biome.scatter == null) return fallback;

            foreach (BiomeDefinition.ScatterEntry entry in biome.scatter)
            {
                if (entry.kind != BiomeDefinition.PropKind.Bush) continue;

                float mid = (entry.scaleRange.x + entry.scaleRange.y) * 0.5f;
                return NaturalHeight(entry) * Mathf.Max(0.1f, mid);
            }

            return fallback;
        }

        float NaturalHeight(BiomeDefinition.ScatterEntry entry)
        {
            if (_naturalHeight.TryGetValue(entry.kind, out float cached)) return cached;

            Mesh sample = BuildProp(entry, 4242);
            float height = sample != null ? Mathf.Max(0.05f, sample.bounds.size.y) : 1f;
            if (sample != null) Destroy(sample);

            _naturalHeight[entry.kind] = height;
            return height;
        }

        /// <summary>Re-dress every tile, after modelled scenery has finished loading.</summary>
        public void RefreshScatter()
        {
            foreach (KeyValuePair<Vector2Int, DioramaTile> kv in _tiles)
            {
                BuildScatter(kv.Value);

                // The food bushes too. The library finishes loading after the first tile
                // is already standing, so a bush a creature grazes from kept the generated
                // mesh it was built with while every bush around it was upgraded -- three
                // brown lumps on the lawn, in a diorama of modelled plants.
                foreach (FoodNode node in kv.Value.Food) RestyleFood(kv.Value, node);
            }
        }

        /// <summary>Swap one food node's visual over to the modelled bush, if there is
        /// one and this node is a bush rather than the berry stockpile.</summary>
        void RestyleFood(DioramaTile tile, FoodNode node)
        {
            if (node == null || node.isStockpile || node.bounty == null) return;
            if (Props == null || !Props.Has(BiomeDefinition.PropKind.Bush)) return;

            var filter = node.bounty.GetComponent<MeshFilter>();
            var renderer = node.bounty.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) return;

            filter.sharedMesh = Props.Mesh(BiomeDefinition.PropKind.Bush);
            renderer.sharedMaterial = ModelledMaterial(BiomeDefinition.PropKind.Bush);
            node.bounty.transform.localScale = Vector3.one * BushHeight(tile.Biome);
        }

        /// <summary>Modelled scenery, if any was loaded. Null means everything falls back
        /// to the generated meshes.</summary>
        public PropLibrary Props { get; set; }

        readonly Dictionary<BiomeDefinition.PropKind, Material> _modelledMaterials = new(8);

        Material ModelledMaterial(BiomeDefinition.PropKind kind)
        {
            if (_modelledMaterials.TryGetValue(kind, out Material cached) && cached != null) return cached;

            // The creature shader, because it is the one that reads a base map. The ground
            // shader takes its albedo from vertex colours, which a modelled prop has none
            // of -- that is what made the unboxing chest render black the first time.
            var material = new Material(Shader.Find("Living Diorama/Creature")) { name = $"Prop_{kind}" };

            // Turn the character lighting off.
            //
            // The creature shader carries a rim light and a hard specular so a creature
            // reads against the scenery. Applied to the scenery itself it draws a shiny
            // white edge around every trunk and boulder, which is the one thing a tree
            // must not have. Subsurface goes too: leaves are not skin.
            if (material.HasProperty("_RimStrength")) material.SetFloat("_RimStrength", 0.06f);
            if (material.HasProperty("_SSSStrength")) material.SetFloat("_SSSStrength", 0f);
            if (material.HasProperty("_SpecStrength")) material.SetFloat("_SpecStrength", 0f);
            if (material.HasProperty("_Gloss")) material.SetFloat("_Gloss", 0f);
            if (material.HasProperty("_Wrap")) material.SetFloat("_Wrap", 0.2f);

            Texture texture = Props?.Texture(kind);
            if (texture != null) material.SetTexture(BaseMapId, texture);

            _modelledMaterials[kind] = material;
            return material;
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

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

        void EmitCombined(Transform parent, List<CombineInstance> parts, string name,
                          Material material, bool castShadows, bool disposeSources)
        {
            if (parts.Count == 0) return;

            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;

            // Generated meshes were only ever scratch data for the combine; modelled ones
            // belong to the library and are reused by every other tile.
            if (disposeSources)
            {
                for (int i = 0; i < parts.Count; i++) Destroy(parts[i].mesh);
            }
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
                //
                // The grazing bush is the modelled one wherever it loaded. Left
                // procedural it sat next to the modelled bushes of the scatter looking
                // like a brown crate someone had left on the lawn -- the same plant,
                // drawn two different ways, a few metres apart.
                bool modelled = i != 0 && Props != null && Props.Has(BiomeDefinition.PropKind.Bush);

                Mesh mesh = i == 0
                    ? ProceduralMeshes.BerryPile(seed, new Color(0.55f, 0.36f, 0.20f), new Color(0.82f, 0.18f, 0.28f))
                    : modelled
                        ? Props.Mesh(BiomeDefinition.PropKind.Bush)
                        : ProceduralMeshes.Bush(seed, tile.Biome.groundHigh * 0.85f);

                visual.AddComponent<MeshFilter>().sharedMesh = mesh;
                visual.AddComponent<MeshRenderer>().sharedMaterial = modelled
                    ? ModelledMaterial(BiomeDefinition.PropKind.Bush)
                    : _propMaterial;

                // Library meshes are normalised to unit height, so they need the real
                // one back; the procedural bush is already the size it should be.
                visual.transform.localScale = Vector3.one *
                    (i == 0 ? 1.15f : modelled ? BushHeight(tile.Biome) : 0.9f);

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

            // Sample the faceted mesh, not the smooth function behind it, so a creature
            // stands on the ground the player can see.
            return TileMeshBuilder.SampleSurface(biome, TileOrigin(CoordAt(world)), _tileSize,
                                                 _seed, world.x, world.z);
        }

        public BiomeDefinition BiomeAt(Vector3 world)
        {
            return _tiles.TryGetValue(CoordAt(world), out DioramaTile tile) ? tile.Biome : null;
        }

        public bool Contains(Vector3 world)
        {
            if (!_tiles.TryGetValue(CoordAt(world), out _)) return false;

            // Keep a margin at the outside edge of the diorama, but only there.
            //
            // The margin used to apply on all four sides of every tile regardless of what
            // was next to it, which put a two-thirds-of-a-unit strip of illegal ground
            // along every internal seam: buy a new tile and the creatures cannot reach it,
            // because there is a wall between the two. An edge with an unlocked neighbour
            // is ground, not a boundary.
            Vector2Int coord = CoordAt(world);
            Vector3 local = world - TileOrigin(coord);
            const float margin = 0.35f;

            if (!_tiles.ContainsKey(coord + Vector2Int.left) && local.x < margin) return false;
            if (!_tiles.ContainsKey(coord + Vector2Int.right) && local.x > _tileSize - margin) return false;
            if (!_tiles.ContainsKey(coord + Vector2Int.down) && local.z < margin) return false;
            if (!_tiles.ContainsKey(coord + Vector2Int.up) && local.z > _tileSize - margin) return false;

            return true;
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
