using UnityEngine;

namespace LivingDiorama.Diorama
{
    /// <summary>
    /// The display stand the whole diorama sits on.
    ///
    /// Without it the terrain slab floats in space; with it the scene reads as a made
    /// object on a plinth, which is the entire fantasy. Rebuilt whenever the footprint
    /// changes so the stand always frames exactly the land the player owns.
    /// </summary>
    public sealed class DioramaPedestal : MonoBehaviour
    {
        static readonly Color RimTop = new(0.30f, 0.24f, 0.20f);
        static readonly Color RimEdge = new(0.62f, 0.47f, 0.26f);   // brass trim catches the key light
        static readonly Color BodyUpper = new(0.22f, 0.18f, 0.16f);
        static readonly Color BodyLower = new(0.13f, 0.11f, 0.10f);
        static readonly Color FootColour = new(0.09f, 0.08f, 0.08f);

        MeshFilter _filter;
        Material _material;

        public static DioramaPedestal Create(Transform parent, Material material)
        {
            var go = new GameObject("Pedestal");
            go.transform.SetParent(parent, false);

            var pedestal = go.AddComponent<DioramaPedestal>();
            pedestal._material = material;
            pedestal._filter = go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            return pedestal;
        }

        /// <summary>Rebuild to frame <paramref name="footprint"/>, whose Y is ignored.</summary>
        public void Rebuild(Bounds footprint, float slabBottom)
        {
            if (_filter == null) return;

            if (_filter.sharedMesh != null) Destroy(_filter.sharedMesh);
            _filter.sharedMesh = Build(footprint, slabBottom);
        }

        static Mesh Build(Bounds footprint, float slabBottom)
        {
            var b = new ProceduralMeshes.Builder();

            // Overhang the terrain a little so the stand reads as holding it, and step the
            // profile in as it descends -- a straight box looks like packaging, a stepped
            // and tapered one looks like furniture.
            const float overhang = 0.42f;
            const float trimHeight = 0.10f;
            const float rimHeight = 0.30f;
            const float footInset = 0.55f;
            const float footHeight = 0.22f;

            Vector3 min = footprint.min - new Vector3(overhang, 0f, overhang);
            Vector3 max = footprint.max + new Vector3(overhang, 0f, overhang);

            float top = slabBottom + 0.02f;              // just under the soil slab
            float trimBottom = top - trimHeight;
            float rimBottom = trimBottom - rimHeight;
            float footTop = rimBottom - footHeight;

            // Ledge under the terrain.
            Ring(b, min, max, top, trimBottom, 0f, 0f, RimEdge, RimEdge);

            // Main body, tapering inward.
            Ring(b, min, max, trimBottom, rimBottom, 0f, 0.16f, RimTop, BodyUpper);

            // Foot, stepped back in again.
            Ring(b, min, max, rimBottom, footTop, 0.16f, footInset, BodyLower, FootColour);

            // Cap the base so the stand is a closed solid from every angle.
            Cap(b, min, max, footTop, footInset, FootColour);

            // Top ledge surface, visible as a thin border around the terrain.
            TopLedge(b, min, max, footprint, top, RimEdge);

            return b.Bake("Pedestal");
        }

        /// <summary>One tapered band of the plinth: four walls between two heights, each
        /// inset by a different amount so the silhouette steps.</summary>
        static void Ring(ProceduralMeshes.Builder b, Vector3 min, Vector3 max,
                         float topY, float bottomY, float topInset, float bottomInset,
                         Color topColour, Color bottomColour)
        {
            Vector3 t0 = new(min.x + topInset, topY, min.z + topInset);
            Vector3 t1 = new(max.x - topInset, topY, max.z - topInset);
            Vector3 b0 = new(min.x + bottomInset, bottomY, min.z + bottomInset);
            Vector3 b1 = new(max.x - bottomInset, bottomY, max.z - bottomInset);

            // South (-Z)
            Wall(b, new Vector3(t1.x, topY, t0.z), new Vector3(t0.x, topY, t0.z),
                    new Vector3(b0.x, bottomY, b0.z), new Vector3(b1.x, bottomY, b0.z),
                    topColour, bottomColour);
            // North (+Z)
            Wall(b, new Vector3(t0.x, topY, t1.z), new Vector3(t1.x, topY, t1.z),
                    new Vector3(b1.x, bottomY, b1.z), new Vector3(b0.x, bottomY, b1.z),
                    topColour, bottomColour);
            // West (-X)
            Wall(b, new Vector3(t0.x, topY, t0.z), new Vector3(t0.x, topY, t1.z),
                    new Vector3(b0.x, bottomY, b1.z), new Vector3(b0.x, bottomY, b0.z),
                    topColour, bottomColour);
            // East (+X)
            Wall(b, new Vector3(t1.x, topY, t1.z), new Vector3(t1.x, topY, t0.z),
                    new Vector3(b1.x, bottomY, b0.z), new Vector3(b1.x, bottomY, b1.z),
                    topColour, bottomColour);
        }

        static void Wall(ProceduralMeshes.Builder b, Vector3 topA, Vector3 topB,
                         Vector3 bottomB, Vector3 bottomA, Color topColour, Color bottomColour)
        {
            // Split the quad in two so the vertical colour step survives flat shading:
            // the upper half takes the top colour, the lower half the bottom one.
            Vector3 midA = Vector3.Lerp(topA, bottomA, 0.5f);
            Vector3 midB = Vector3.Lerp(topB, bottomB, 0.5f);

            b.AddQuad(topA, topB, midB, midA, topColour);
            b.AddQuad(midA, midB, bottomB, bottomA, bottomColour);
        }

        static void Cap(ProceduralMeshes.Builder b, Vector3 min, Vector3 max, float y,
                        float inset, Color colour)
        {
            Vector3 a = new(min.x + inset, y, min.z + inset);
            Vector3 c = new(max.x - inset, y, max.z - inset);
            Vector3 bb = new(c.x, y, a.z);
            Vector3 d = new(a.x, y, c.z);

            // Wound to face down.
            b.AddTriangle(a, bb, c, colour);
            b.AddTriangle(a, c, d, colour);
        }

        /// <summary>The flat border between the plinth edge and the terrain slab.</summary>
        static void TopLedge(ProceduralMeshes.Builder b, Vector3 min, Vector3 max,
                             Bounds footprint, float y, Color colour)
        {
            Vector3 imin = footprint.min;
            Vector3 imax = footprint.max;

            // South strip
            b.AddQuad(new Vector3(min.x, y, min.z), new Vector3(max.x, y, min.z),
                      new Vector3(max.x, y, imin.z), new Vector3(min.x, y, imin.z), colour);
            // North strip
            b.AddQuad(new Vector3(min.x, y, imax.z), new Vector3(max.x, y, imax.z),
                      new Vector3(max.x, y, max.z), new Vector3(min.x, y, max.z), colour);
            // West strip
            b.AddQuad(new Vector3(min.x, y, imin.z), new Vector3(imin.x, y, imin.z),
                      new Vector3(imin.x, y, imax.z), new Vector3(min.x, y, imax.z), colour);
            // East strip
            b.AddQuad(new Vector3(imax.x, y, imin.z), new Vector3(max.x, y, imin.z),
                      new Vector3(max.x, y, imax.z), new Vector3(imax.x, y, imax.z), colour);
        }

        void OnDestroy()
        {
            if (_filter != null && _filter.sharedMesh != null) Destroy(_filter.sharedMesh);
        }
    }
}
