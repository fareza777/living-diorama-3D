using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

namespace LivingDiorama.Unboxing
{
    /// <summary>
    /// Loads the modelled props -- the chest and its pedestal -- from StreamingAssets.
    ///
    /// Same route as the creatures, and for the same reason: a prop can be replaced by
    /// dropping a new GLB in, with no rebuild. Everything here degrades to null rather
    /// than throwing, because the unboxing stage has a generated fallback and a missing
    /// prop must never break a reward the player has already paid for.
    /// </summary>
    public static class GlbProps
    {
        const string Subfolder = "Props";

        static readonly Dictionary<string, GltfImport> Cache = new(4);

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static async Task<GameObject> LoadAsync(string fileName)
        {
            try
            {
                if (!Cache.TryGetValue(fileName, out GltfImport gltf))
                {
                    string path = Path.Combine(Application.streamingAssetsPath, Subfolder, fileName);
                    string url = path.Contains("://") ? path : "file://" + path.Replace('\\', '/');

                    gltf = new GltfImport();
                    if (!await gltf.Load(url))
                    {
                        Debug.LogWarning($"[GlbProps] could not load '{fileName}'");
                        return null;
                    }
                    Cache[fileName] = gltf;
                }

                var holder = new GameObject("Prop_" + Path.GetFileNameWithoutExtension(fileName));
                if (await gltf.InstantiateMainSceneAsync(holder.transform)) return holder;

                UnityEngine.Object.Destroy(holder);
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[GlbProps] '{fileName}' failed: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Move a prop's material onto the stylised shader, keeping its albedo, so it
        /// lights the same way as everything else on the stage.
        ///
        /// <paramref name="propName"/> names a texture under Resources/Props, and it is
        /// tried first for a reason: reading the albedo back out of the imported glTF
        /// material works in the editor and produces a white prop in a build, where the
        /// importer's own shaders are stripped and the material comes back bare. The
        /// extracted texture is a normal asset, so it is always there.
        /// </summary>
        public static Material Restyle(Renderer renderer, Shader shader, string propName = null)
        {
            if (shader == null) return null;

            var material = new Material(shader) { name = "PropStyled" };

            if (!string.IsNullOrEmpty(propName))
            {
                var extracted = Resources.Load<Texture2D>("Props/" + propName + "_albedo");
                if (extracted != null) material.SetTexture(BaseMapId, extracted);
            }

            Material source = renderer != null ? renderer.sharedMaterial : null;
            if (source != null)
            {
                if (material.GetTexture(BaseMapId) == null)
                {
                    foreach (string name in new[] { "_BaseMap", "baseColorTexture", "_MainTex" })
                    {
                        if (!source.HasProperty(name)) continue;

                        Texture texture = source.GetTexture(name);
                        if (texture == null) continue;

                        material.SetTexture(BaseMapId, texture);
                        break;
                    }
                }

                if (source.HasProperty(BaseColorId)) material.SetColor(BaseColorId, source.GetColor(BaseColorId));
            }

            return material;
        }

        public static void Dispose()
        {
            foreach (KeyValuePair<string, GltfImport> pair in Cache) pair.Value?.Dispose();
            Cache.Clear();
        }
    }
}
