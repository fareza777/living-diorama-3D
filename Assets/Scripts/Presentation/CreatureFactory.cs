using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using LivingDiorama.Data;
using UnityEngine;

namespace LivingDiorama.Presentation
{
    /// <summary>
    /// Turns a creature definition into a visible model.
    ///
    /// Two routes, both supported on purpose:
    ///   1. an authored/imported prefab, for everything that ships with the game;
    ///   2. a GLB in StreamingAssets/Creatures, parsed at runtime with glTFast.
    ///
    /// Route 2 is what makes the roster extensible -- a new Meshy export can be added
    /// without touching code or rebuilding the APK. Whichever route a model arrives by,
    /// it is normalised to the same world scale and re-skinned onto the stylised shader
    /// so the diorama stays visually coherent.
    /// </summary>
    public sealed class CreatureFactory
    {
        const string StreamingSubfolder = "Creatures";

        readonly Dictionary<string, GltfImport> _gltfCache = new();
        readonly Dictionary<string, Task<GltfImport>> _inFlight = new();
        readonly Shader _creatureShader;

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public CreatureFactory(Shader creatureShader)
        {
            _creatureShader = creatureShader;
        }

        /// <summary>Build the model for a species under <paramref name="parent"/>.
        /// Returns null when no model could be produced; the caller falls back to a
        /// placeholder so a bad asset never takes the whole diorama down.</summary>
        public async Task<GameObject> CreateAsync(CreatureDefinition def, Transform parent)
        {
            if (def == null) return null;

            GameObject instance = null;

            // A rigged model wins: it brings a skeleton and real clips, which no amount of
            // procedural motion matches. Everything else falls back in order of fidelity.
            if (def.riggedPrefab != null)
            {
                instance = UnityEngine.Object.Instantiate(def.riggedPrefab, parent);
                AttachAnimator(instance, def);
            }
            else if (def.modelPrefab != null)
            {
                instance = UnityEngine.Object.Instantiate(def.modelPrefab, parent);
            }
            else if (!string.IsNullOrWhiteSpace(def.streamingModelFile))
            {
                instance = await InstantiateFromStreamingAssets(def, parent);
            }

            if (instance == null) return null;

            instance.name = $"Model_{def.id}";
            instance.transform.localRotation = Quaternion.Euler(def.modelEuler);
            Normalise(instance, def);
            Restyle(instance, def);
            return instance;
        }

        /// <summary>Wire up the controller for a rigged species. Root motion stays off:
        /// the simulation decides where a creature is, and a clip that also moved it would
        /// fight the steering and drift the creature away from its own AI.</summary>
        static void AttachAnimator(GameObject instance, CreatureDefinition def)
        {
            if (def.animatorController == null) return;

            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();

            animator.runtimeAnimatorController = def.animatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.keepAnimatorStateOnDisable = false;
        }

        async Task<GameObject> InstantiateFromStreamingAssets(CreatureDefinition def, Transform parent)
        {
            try
            {
                GltfImport gltf = await LoadGltf(def.streamingModelFile);
                if (gltf == null) return null;

                var holder = new GameObject($"Gltf_{def.id}");
                holder.transform.SetParent(parent, false);

                bool ok = await gltf.InstantiateMainSceneAsync(holder.transform);
                if (!ok)
                {
                    UnityEngine.Object.Destroy(holder);
                    return null;
                }
                return holder;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CreatureFactory] failed to load '{def.streamingModelFile}': {e.Message}");
                return null;
            }
        }

        /// <summary>One parse per file, shared by every instance of that species.</summary>
        Task<GltfImport> LoadGltf(string fileName)
        {
            if (_gltfCache.TryGetValue(fileName, out GltfImport cached))
            {
                return Task.FromResult(cached);
            }
            if (_inFlight.TryGetValue(fileName, out Task<GltfImport> pending))
            {
                return pending;
            }

            Task<GltfImport> task = LoadGltfUncached(fileName);
            _inFlight[fileName] = task;
            return task;
        }

        async Task<GltfImport> LoadGltfUncached(string fileName)
        {
            try
            {
                string url = StreamingUrl(fileName);
                var gltf = new GltfImport();
                bool ok = await gltf.Load(url);

                if (!ok)
                {
                    Debug.LogError($"[CreatureFactory] glTFast could not parse '{url}'");
                    return null;
                }

                _gltfCache[fileName] = gltf;
                return gltf;
            }
            finally
            {
                _inFlight.Remove(fileName);
            }
        }

        /// <summary>StreamingAssets lives inside the APK on Android, so it has to be read
        /// through a URL rather than a plain file path.</summary>
        public static string StreamingUrl(string fileName)
        {
            string path = Path.Combine(Application.streamingAssetsPath, StreamingSubfolder, fileName);
            if (path.Contains("://")) return path;                 // Android jar:file://
            return "file://" + path.Replace('\\', '/');
        }

        /// <summary>Generated meshes arrive at arbitrary scale and are not necessarily
        /// centred on their feet. Rescale to the authored body height and drop the
        /// pivot to the ground so every species shares one convention.</summary>
        static void Normalise(GameObject instance, CreatureDefinition def)
        {
            instance.transform.localScale = Vector3.one;
            instance.transform.localPosition = Vector3.zero;

            if (!TryGetLocalBounds(instance, out Bounds bounds)) return;

            float scale = def.modelScale;
            if (def.autoFitToBodyHeight && bounds.size.y > 0.0001f)
            {
                scale *= def.bodyHeight / bounds.size.y;
            }

            instance.transform.localScale = Vector3.one * scale;
            // After scaling, lift so the lowest vertex sits at y = 0.
            instance.transform.localPosition = new Vector3(
                -bounds.center.x * scale,
                -bounds.min.y * scale,
                -bounds.center.z * scale);
        }

        /// <summary>
        /// True world-space bounds of a creature instance, skinned meshes included.
        ///
        /// Public because anything that has to fit a creature into a frame needs the same
        /// answer, and getting it wrong is not obvious: Renderer.bounds on a skinned mesh
        /// is a padded culling box, so measuring it puts the camera too far back and the
        /// creature ends up a third of the size it should be.
        /// </summary>
        public static bool TryGetWorldBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            if (instance == null) return false;

            if (!TryGetLocalBounds(instance, out Bounds local)) return false;

            bounds = TransformBounds(local, instance.transform.localToWorldMatrix);
            return true;
        }

        static bool TryGetLocalBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            if (renderers.Length == 0) return false;

            Matrix4x4 toLocal = instance.transform.worldToLocalMatrix;
            bool first = true;

            foreach (Renderer r in renderers)
            {
                // Measure the mesh, not the renderer.
                //
                // Renderer.bounds on a skinned mesh is a generous runtime box meant for
                // culling -- padded so no animation can escape it -- and the bind-pose
                // bounds on the shared mesh are in the skeleton's space, not the
                // renderer's. Either one puts the feet in the wrong place: measured
                // against the ground, the goblin stood a fifth of a unit underneath it.
                // Baking the pose gives the vertices where they actually are.
                Bounds wb;

                if (r is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                {
                    var baked = new Mesh();
                    skinned.BakeMesh(baked, true);
                    wb = TransformBounds(baked.bounds, skinned.transform.localToWorldMatrix);
                    UnityEngine.Object.Destroy(baked);
                }
                else
                {
                    Mesh mesh = r.GetComponent<MeshFilter>() is { } filter ? filter.sharedMesh : null;
                    wb = mesh != null
                        ? TransformBounds(mesh.bounds, r.transform.localToWorldMatrix)
                        : r.bounds;
                }

                // Transform the eight corners so a rotated model still measures correctly.
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? wb.min.x : wb.max.x,
                        (i & 2) == 0 ? wb.min.y : wb.max.y,
                        (i & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 local = toLocal.MultiplyPoint3x4(corner);

                    if (first)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }

            return !first;
        }

        /// <summary>World-space box enclosing a local box under a transform.</summary>
        static Bounds TransformBounds(Bounds local, Matrix4x4 matrix)
        {
            Vector3 centre = matrix.MultiplyPoint3x4(local.center);
            Vector3 e = local.extents;

            // Sum the absolute contribution of each axis: the tightest AABB that still
            // contains the rotated box.
            Vector3 extents = new(
                Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);

            return new Bounds(centre, extents * 2f);
        }

        /// <summary>Move every material onto the stylised creature shader, keeping the
        /// generated albedo. Without this a Meshy export renders with default PBR and
        /// sticks out badly against the hand-tuned toon world.</summary>
        void Restyle(GameObject instance, CreatureDefinition def)
        {
            if (_creatureShader == null) return;

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in renderers)
            {
                Material[] source = r.sharedMaterials;
                var styled = new Material[source.Length];

                for (int i = 0; i < source.Length; i++)
                {
                    Material src = source[i];
                    var mat = new Material(_creatureShader);

                    // The definition's own albedo wins. It is the only one guaranteed to
                    // exist: a rigged FBX brings no texture at all, and a glTF import can
                    // bind its base map under any of half a dozen property names.
                    Texture baseMap = def.albedo != null ? def.albedo : null;

                    if (src != null)
                    {
                        baseMap ??= FindBaseTexture(src);
                        mat.SetColor(BaseColorId, FindBaseColour(src));
                        mat.name = src.name + "_Styled";
                    }

                    if (baseMap != null) mat.SetTexture(BaseMapId, baseMap);

                    styled[i] = mat;
                }

                r.sharedMaterials = styled;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
        }

        /// <summary>
        /// Find the albedo on an imported material.
        ///
        /// Which property holds it depends on the shader glTFast happened to pick, and
        /// that varies with the render pipeline and the glTF's own material setup. Getting
        /// this wrong is not loud -- the creature simply renders white, which reads as a
        /// deliberate art choice rather than as a missing texture -- so the lookup tries
        /// the known names and then falls back to whatever texture the material has.
        /// </summary>
        static Texture FindBaseTexture(Material m)
        {
            foreach (string name in BaseMapNames)
            {
                if (!m.HasProperty(name)) continue;

                Texture texture = m.GetTexture(name);
                if (texture != null) return texture;
            }

            // Last resort: the first bound texture that is not obviously a normal map.
            string[] properties = m.GetTexturePropertyNames();
            foreach (string name in properties)
            {
                if (name.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("bump", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                Texture texture = m.GetTexture(name);
                if (texture != null) return texture;
            }

            return null;
        }

        static readonly string[] BaseMapNames =
        {
            "_BaseMap",              // URP Lit
            "baseColorTexture",      // glTFast's own shaders
            "_baseColorTexture",
            "_MainTex",              // built-in
            "_BaseColorMap",         // HDRP, in case the project ever moves
        };

        static Color FindBaseColour(Material m)
        {
            foreach (string name in BaseColourNames)
            {
                if (m.HasProperty(name)) return m.GetColor(name);
            }
            return Color.white;
        }

        static readonly string[] BaseColourNames =
        {
            "_BaseColor", "baseColorFactor", "_baseColorFactor", "_Color",
        };

        public void Dispose()
        {
            foreach (KeyValuePair<string, GltfImport> kv in _gltfCache) kv.Value?.Dispose();
            _gltfCache.Clear();
            _inFlight.Clear();
        }
    }
}
