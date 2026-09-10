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
                    Mesh mesh = Normalise(filter);

                    // The bush came back standing on a slab of ground. Meshy threw one in
                    // despite being asked not to, and it reads as a crate under every
                    // bush in the diorama. A prop that sits on the floor has no use for a
                    // floor of its own.
                    if (kind == BiomeDefinition.PropKind.Bush) TrimGroundPlate(mesh);

                    _meshes[kind] = mesh;
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

        /// <summary>
        /// Drop the flat disc of ground a generated model was standing on.
        ///
        /// Everything near the floor and facing up or down within the bottom slice of the
        /// model: that is a base plate and nothing else. Real foliage at that height
        /// faces outwards. Taking it away leaves the underside open, which nobody sees,
        /// because the thing is sitting on grass.
        /// </summary>
        static void TrimGroundPlate(Mesh mesh)
        {
            const float slice = 0.10f;      // of the model's own height, which is 1 here
            const float flatness = 0.80f;   // how vertical a triangle's normal has to be

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var kept = new List<int>(triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                bool low = a.y < slice && b.y < slice && c.y < slice;
                bool flat = Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) > flatness;

                if (low && flat) continue;

                kept.Add(triangles[i]);
                kept.Add(triangles[i + 1]);
                kept.Add(triangles[i + 2]);
            }

            if (kept.Count == triangles.Length || kept.Count == 0) return;

            mesh.SetTriangles(kept, 0);
            mesh.RecalculateBounds();
            Debug.Log($"[PropLibrary] {mesh.name}: dropped {(triangles.Length - kept.Count) / 3} " +
                      "triangles of base plate");
        }

        /// <summary>Bake the hierarchy's transform in, stand the model on y = 0 and scale
        /// it to one unit tall, so a scatter entry's height and scale range still describe
        /// what they used to.</summary>
        public static Mesh NormaliseMesh(MeshFilter filter) => Normalise(filter);

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
