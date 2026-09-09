using System.Collections.Generic;
using System.Threading.Tasks;
using LivingDiorama.Presentation;
using UnityEngine;

namespace LivingDiorama.Unboxing
{
    /// <summary>
    /// Loads the modelled treasure chest and cuts it into a body and a lid.
    ///
    /// The chest arrives from Meshy as one closed mesh, because that is what a chest is;
    /// an unboxing needs it in two pieces. Splitting it horizontally by triangle height
    /// gets a real lid off a real model, which beats both a crude box built out of
    /// primitives and a "chest" that never actually opens.
    /// </summary>
    public static class ChestModel
    {
        /// <summary>Where to cut, as a fraction of the model's height. Chest lids sit high;
        /// this lands just under the barrel top on the generated model.</summary>
        const float LidSeam = 0.68f;

        public readonly struct Parts
        {
            public readonly Mesh Body;
            public readonly Mesh Lid;
            public readonly Material Material;

            /// <summary>Hinge position in the model's local space: the back edge of the seam.</summary>
            public readonly Vector3 Hinge;

            public readonly Bounds Bounds;

            public Parts(Mesh body, Mesh lid, Material material, Vector3 hinge, Bounds bounds)
            {
                Body = body;
                Lid = lid;
                Material = material;
                Hinge = hinge;
                Bounds = bounds;
            }

            public bool IsValid => Body != null && Lid != null;
        }

        /// <summary>Load and split the chest. Returns an invalid Parts when the model is
        /// missing, and the stage falls back to its generated chest.</summary>
        public static async Task<Parts> LoadAsync(string fileName, Shader shader)
        {
            GameObject holder = await GlbProps.LoadAsync(fileName);
            if (holder == null) return default;

            var filter = holder.GetComponentInChildren<MeshFilter>();
            var renderer = holder.GetComponentInChildren<MeshRenderer>();

            if (filter == null || filter.sharedMesh == null)
            {
                Object.Destroy(holder);
                return default;
            }

            // Bake the hierarchy's transform into the vertices so the split works in one
            // space and the pieces can be re-parented freely afterwards.
            Mesh baked = Bake(filter);
            Material material = GlbProps.Restyle(renderer, shader, "chest");

            Bounds bounds = baked.bounds;
            float seamY = Mathf.Lerp(bounds.min.y, bounds.max.y, LidSeam);

            Split(baked, seamY, out Mesh body, out Mesh lid);
            Object.Destroy(holder);
            Object.Destroy(baked);

            var hinge = new Vector3(bounds.center.x, seamY, bounds.max.z);
            return new Parts(body, lid, material, hinge, bounds);
        }

        static Mesh Bake(MeshFilter filter)
        {
            Mesh source = filter.sharedMesh;
            Matrix4x4 matrix = filter.transform.localToWorldMatrix;

            var baked = new Mesh { name = source.name + "_Baked" };
            baked.indexFormat = source.indexFormat;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            source.GetVertices(vertices);
            source.GetNormals(normals);
            source.GetUVs(0, uvs);

            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            }
            for (int i = 0; i < normals.Count; i++)
            {
                normals[i] = matrix.MultiplyVector(normals[i]).normalized;
            }

            // Re-centre on the origin so the caller can place it wherever it likes.
            var bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) bounds.Encapsulate(v);

            Vector3 offset = new(bounds.center.x, bounds.min.y, bounds.center.z);
            for (int i = 0; i < vertices.Count; i++) vertices[i] -= offset;

            baked.SetVertices(vertices);
            if (normals.Count == vertices.Count) baked.SetNormals(normals);
            if (uvs.Count == vertices.Count) baked.SetUVs(0, uvs);
            baked.SetTriangles(source.triangles, 0);
            baked.RecalculateBounds();
            if (normals.Count != vertices.Count) baked.RecalculateNormals();

            return baked;
        }

        /// <summary>Partition triangles by the height of their centroid. Vertices are
        /// duplicated per side rather than shared, which keeps the two meshes independent
        /// and avoids re-indexing.</summary>
        static void Split(Mesh source, float seamY, out Mesh below, out Mesh above)
        {
            Vector3[] vertices = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            int[] triangles = source.triangles;

            var lower = new Builder(vertices.Length);
            var upper = new Builder(vertices.Length / 4);

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                float centroid = (vertices[a].y + vertices[b].y + vertices[c].y) / 3f;

                Builder target = centroid >= seamY ? upper : lower;
                target.Add(vertices, normals, uvs, a, b, c);
            }

            below = lower.Bake("ChestBody");
            above = upper.Bake("ChestLid");
        }

        sealed class Builder
        {
            readonly List<Vector3> _vertices;
            readonly List<Vector3> _normals;
            readonly List<Vector2> _uvs;
            readonly List<int> _triangles;

            public Builder(int capacity)
            {
                _vertices = new List<Vector3>(capacity);
                _normals = new List<Vector3>(capacity);
                _uvs = new List<Vector2>(capacity);
                _triangles = new List<int>(capacity);
            }

            public void Add(Vector3[] vertices, Vector3[] normals, Vector2[] uvs, int a, int b, int c)
            {
                foreach (int index in stackalloc[] { a, b, c })
                {
                    _triangles.Add(_vertices.Count);
                    _vertices.Add(vertices[index]);
                    if (normals.Length > index) _normals.Add(normals[index]);
                    if (uvs.Length > index) _uvs.Add(uvs[index]);
                }
            }

            public Mesh Bake(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.indexFormat = _vertices.Count > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;

                mesh.SetVertices(_vertices);
                if (_normals.Count == _vertices.Count) mesh.SetNormals(_normals);
                if (_uvs.Count == _vertices.Count) mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateBounds();
                if (_normals.Count != _vertices.Count) mesh.RecalculateNormals();
                return mesh;
            }
        }
    }
}
