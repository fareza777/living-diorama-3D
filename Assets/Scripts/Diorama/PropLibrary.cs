using System.Collections.Generic;
using System.Threading.Tasks;
using LivingDiorama.Data;
using LivingDiorama.Unboxing;
using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// Modelled scenery, loaded once before the world is built.
    ///
    /// The terrain has to stay procedural -- the simulation asks it for heights thousands
    /// of times a second, and a new tile has to meet the ones already there -- but the
    /// things standing on it do not, and cones and blobs made of primitives were the
    /// weakest thing on screen. Anything the library has a model for is used; anything it
    /// does not falls back to the generated mesh, so a missing file is a plainer tree
    /// rather than an empty world.
    ///
    /// Meshes are normalised to unit height here so the scatter's own scale ranges keep
    /// meaning what they meant before.
    /// </summary>
    public sealed class PropLibrary
    {
        readonly Dictionary<BiomeDefinition.PropKind, Mesh> _meshes = new(8);
        readonly Dictionary<BiomeDefinition.PropKind, Texture> _textures = new(8);

        static readonly (BiomeDefinition.PropKind Kind, string Name)[] Wanted =
        {
            (BiomeDefinition.PropKind.PineTree, "pine_tree"),
            (BiomeDefinition.PropKind.BroadleafTree, "broadleaf_tree"),
            (BiomeDefinition.PropKind.Rock, "rock"),
            (BiomeDefinition.PropKind.Bush, "bush"),
            (BiomeDefinition.PropKind.Mushroom, "mushroom"),
        };

        public bool Has(BiomeDefinition.PropKind kind) => _meshes.ContainsKey(kind);

        public Mesh Mesh(BiomeDefinition.PropKind kind) =>
            _meshes.TryGetValue(kind, out Mesh mesh) ? mesh : null;

        public Texture Texture(BiomeDefinition.PropKind kind) =>
            _textures.TryGetValue(kind, out Texture tex) ? tex : null;

        public async Task LoadAsync()
        {
            foreach ((BiomeDefinition.PropKind kind, string name) in Wanted)
            {
                GameObject holder = await GlbProps.LoadAsync(name + ".glb");
                if (holder == null) continue;

                var filter = holder.GetComponentInChildren<MeshFilter>();
                var renderer = holder.GetComponentInChildren<MeshRenderer>();

                if (filter != null && filter.sharedMesh != null)
                {
                    _meshes[kind] = Normalise(filter);
                }

                // Prefer the extracted texture.
                //
                // Reading the albedo back out of the imported glTF material works in the
                // editor and comes back empty in a build, where the importer's own
                // shaders are stripped -- which is exactly how the unboxing chest shipped
                // as a white box, and how these trees did on the first device install.
                var extracted = Resources.Load<Texture2D>("Props/" + name + "_albedo");
                if (extracted != null)
                {
                    _textures[kind] = extracted;
                }
                else if (renderer != null && renderer.sharedMaterial != null)
                {
                    foreach (string property in new[] { "_BaseMap", "baseColorTexture", "_MainTex" })
                    {
                        if (!renderer.sharedMaterial.HasProperty(property)) continue;

                        Texture texture = renderer.sharedMaterial.GetTexture(property);
                        if (texture == null) continue;

                        _textures[kind] = texture;
                        break;
                    }
                }

                Object.Destroy(holder);
            }
        }

        /// <summary>Bake the hierarchy's transform in, stand the model on y = 0 and scale
        /// it to one unit tall, so a scatter entry's height and scale range still describe
        /// what they used to.</summary>
        static Mesh Normalise(MeshFilter filter)
        {
            Mesh source = filter.sharedMesh;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            source.GetVertices(vertices);
            source.GetNormals(normals);
            source.GetUVs(0, uvs);

            for (int i = 0; i < vertices.Count; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            for (int i = 0; i < normals.Count; i++) normals[i] = matrix.MultiplyVector(normals[i]).normalized;

            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) bounds.Encapsulate(v);

            float height = Mathf.Max(0.0001f, bounds.size.y);
            var offset = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            for (int i = 0; i < vertices.Count; i++) vertices[i] = (vertices[i] - offset) / height;

            var baked = new Mesh { name = source.name + "_Prop" };
            baked.indexFormat = source.indexFormat;
            baked.SetVertices(vertices);
            if (normals.Count == vertices.Count) baked.SetNormals(normals);
            if (uvs.Count == vertices.Count) baked.SetUVs(0, uvs);
            baked.SetTriangles(source.triangles, 0);
            baked.RecalculateBounds();
            if (normals.Count != vertices.Count) baked.RecalculateNormals();

            return baked;
        }
    }
}
