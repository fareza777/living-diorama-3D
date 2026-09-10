using System.Collections.Generic;
using System.Threading.Tasks;
using LivingDiorama.Simulation;
using LivingDiorama.Unboxing;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// Everything the player has put down, and the answers the simulation asks about it.
    ///
    /// Scenery is scattered by the world generator and never moves; these are different
    /// in one way that matters to every other system -- a creature has a reason to walk
    /// to them. So they are kept apart from the scatter, individually addressable, and
    /// they answer "where is the nearest bed" rather than merely occupying space.
    /// </summary>
    public sealed class PlacementService : MonoBehaviour
    {
        readonly List<Placement> _placed = new(8);
        readonly Dictionary<string, Mesh> _meshes = new(4);
        readonly Dictionary<string, Material> _materials = new(4);

        Transform _root;
        DioramaWorld _world;
        Shader _shader;

        public IReadOnlyList<Placement> Placed => _placed;

        /// <summary>Raised whenever the set changes, so the interface and the save can
        /// follow without polling.</summary>
        public event System.Action Changed;

        public static PlacementService Create(DioramaWorld world)
        {
            var go = new GameObject("Placements");
            go.transform.SetParent(world.transform, false);

            var service = go.AddComponent<PlacementService>();
            service._world = world;
            service._root = go.transform;
            service._shader = Shader.Find("Living Diorama/Creature");
            return service;
        }

        // ---- loading the models ---------------------------------------------

        /// <summary>Pull the four models in once. They are small and there are four of
        /// them, so they are loaded together at startup rather than on first use -- a
        /// creature deciding to go to bed should not have to wait for a file.</summary>
        public async Task LoadAsync()
        {
            foreach (Placeable placeable in Placeables.All)
            {
                if (_meshes.ContainsKey(placeable.Id)) continue;

                GameObject holder = await GlbProps.LoadAsync(placeable.Model + ".glb");
                if (holder == null) continue;

                var filter = holder.GetComponentInChildren<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    _meshes[placeable.Id] = PropLibrary.NormaliseMesh(filter);
                }

                var texture = Resources.Load<Texture2D>("Props/" + placeable.Model + "_albedo");
                _materials[placeable.Id] = BuildMaterial(placeable.Id, texture);

                Destroy(holder);
            }
        }

        Material BuildMaterial(string id, Texture texture)
        {
            var material = new Material(_shader) { name = "Placeable_" + id };

            // Same treatment the scenery gets: the creature shader is the one that reads
            // a base map, but its rim light and subsurface belong on skin, not on wood.
            if (material.HasProperty("_RimStrength")) material.SetFloat("_RimStrength", 0.06f);
            if (material.HasProperty("_SSSStrength")) material.SetFloat("_SSSStrength", 0f);
            if (material.HasProperty("_SpecStrength")) material.SetFloat("_SpecStrength", 0f);
            if (material.HasProperty("_Gloss")) material.SetFloat("_Gloss", 0f);
            if (texture != null) material.SetTexture(BaseMapId, texture);

            return material;
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public bool IsReady => _meshes.Count > 0;

        public Mesh MeshFor(string id) => _meshes.GetValueOrDefault(id);
        public Material MaterialFor(string id) => _materials.GetValueOrDefault(id);

        // ---- putting things down ---------------------------------------------

        public Placement Place(string placeableId, Vector3 position, float yaw, string instanceId = null)
        {
            if (!Placeables.TryGet(placeableId, out Placeable definition)) return null;

            var placement = new Placement
            {
                Id = instanceId ?? System.Guid.NewGuid().ToString("N")[..8],
                Definition = definition,
                Position = position,
                Yaw = yaw,
            };

            placement.Position.y = _world.SampleHeight(position);
            placement.Visual = BuildVisual(placement);

            _placed.Add(placement);
            RefreshObstacles();
            Changed?.Invoke();
            return placement;
        }

        public void Remove(Placement placement)
        {
            if (placement == null || !_placed.Remove(placement)) return;

            if (placement.Visual != null) Destroy(placement.Visual);
            RefreshObstacles();
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (Placement placement in _placed)
            {
                if (placement.Visual != null) Destroy(placement.Visual);
            }

            _placed.Clear();
            RefreshObstacles();
        }

        GameObject BuildVisual(Placement placement)
        {
            var go = new GameObject("Placeable_" + placement.Definition.Id);
            go.transform.SetParent(_root, false);
            go.transform.position = placement.Position;
            go.transform.rotation = Quaternion.Euler(0f, placement.Yaw, 0f);

            Mesh mesh = MeshFor(placement.Definition.Id);
            if (mesh == null) return go;

            // Library meshes are normalised to unit height, so the definition's height is
            // the real one.
            go.transform.localScale = Vector3.one * placement.Definition.Height;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(placement.Definition.Id);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            return go;
        }

        /// <summary>
        /// Tell the world to steer creatures around what is now standing there.
        ///
        /// Rebuilt wholesale rather than patched: there are never more than a handful of
        /// these, and a list that is rebuilt cannot drift out of step with the objects it
        /// describes the way an incrementally maintained one can.
        /// </summary>
        void RefreshObstacles()
        {
            _world.SetPlacementObstacles(_placed);
        }

        // ---- what the simulation asks ----------------------------------------

        /// <summary>
        /// The nearest free thing of this kind, within its own draw distance.
        ///
        /// Claimed ones are skipped so two creatures do not walk to the same bed and end
        /// up standing inside each other, which is the sort of thing that reads as a bug
        /// rather than as a queue.
        /// </summary>
        public Placement FindFree(PlaceableRole role, Vector3 from, string claimant)
        {
            Placement best = null;
            float bestDistance = float.MaxValue;

            foreach (Placement placement in _placed)
            {
                if (placement.Definition.Role != role) continue;
                if (!string.IsNullOrEmpty(placement.ClaimedBy) && placement.ClaimedBy != claimant) continue;

                float distance = Vector3.Distance(from, placement.Position);
                if (distance > placement.Definition.Draw || distance >= bestDistance) continue;

                best = placement;
                bestDistance = distance;
            }

            return best;
        }

        public bool Has(PlaceableRole role)
        {
            foreach (Placement placement in _placed)
            {
                if (placement.Definition.Role == role) return true;
            }
            return false;
        }

        public void Claim(Placement placement, string claimant)
        {
            if (placement != null) placement.ClaimedBy = claimant;
        }

        public void Release(string claimant)
        {
            foreach (Placement placement in _placed)
            {
                if (placement.ClaimedBy == claimant) placement.ClaimedBy = null;
            }
        }

        /// <summary>Whether a spot is legal to build on: inside the diorama, out of the
        /// water, and not on top of something else.</summary>
        public bool CanPlaceAt(Vector3 world, float radius, out string reason)
        {
            if (!_world.Contains(world))
            {
                reason = "Outside the diorama";
                return false;
            }

            float ground = _world.SampleHeight(world);
            if (_world.TryGetWater(world, out float surface) && ground <= surface + 0.03f)
            {
                reason = "That is under water";
                return false;
            }

            var at = new Vector3(world.x, ground, world.z);
            if ((_world.ResolveObstacles(at, radius) - at).sqrMagnitude > 0.0001f)
            {
                reason = "Something is already there";
                return false;
            }

            foreach (Placement placement in _placed)
            {
                float gap = placement.Definition.Radius + radius;
                if (Vector3.Distance(placement.Position, at) < gap)
                {
                    reason = "Too close to the " + placement.Definition.DisplayName.ToLowerInvariant();
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
