using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Everything the simulation needs to know about the ground it walks on. The
    /// diorama implements this; the AI never touches terrain generation directly,
    /// which is what lets the brain be unit tested against a flat stub.
    /// </summary>
    public interface IWorldSurface
    {
        /// <summary>Terrain height at a world XZ position.</summary>
        float SampleHeight(Vector3 world);

        /// <summary>True while the position sits on an unlocked tile.</summary>
        bool Contains(Vector3 world);

        /// <summary>Nearest legal position inside the unlocked area.</summary>
        Vector3 ClampInside(Vector3 world);

        /// <summary>A random walkable point, optionally biased near <paramref name="near"/>.</summary>
        Vector3 RandomPoint(Vector3 near, float radius);

        /// <summary>Water surface height at a position, if this spot is water.</summary>
        bool TryGetWater(Vector3 world, out float surfaceY);

        /// <summary>Closest water edge within <paramref name="maxDistance"/>, for creatures
        /// that like to play or drink at the shoreline.</summary>
        bool TryFindWaterEdge(Vector3 from, float maxDistance, out Vector3 point);

        /// <summary>Biome governing the tile under a position. Null outside the diorama.</summary>
        Data.BiomeDefinition BiomeAt(Vector3 world);
    }
}
