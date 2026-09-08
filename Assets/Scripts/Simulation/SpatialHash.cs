using System.Collections.Generic;
using UnityEngine;

namespace LivingDiorama.Simulation
{
    /// <summary>
    /// Uniform grid for neighbour queries. Deliberately not Unity physics: the whole
    /// point of the diorama is dozens of creatures on a mid-range phone, and
    /// OverlapSphere against a rebuilt PhysX scene every tick is the wrong trade.
    /// Rebuilt once per simulation tick, queried many times.
    /// </summary>
    public sealed class SpatialHash
    {
        readonly Dictionary<long, List<CreatureAgent>> _cells = new(128);
        readonly Stack<List<CreatureAgent>> _pool = new();
        float _cellSize;

        public SpatialHash(float cellSize)
        {
            _cellSize = Mathf.Max(0.25f, cellSize);
        }

        public float CellSize
        {
            get => _cellSize;
            set => _cellSize = Mathf.Max(0.25f, value);
        }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        void CellOf(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt(p.x / _cellSize);
            z = Mathf.FloorToInt(p.z / _cellSize);
        }

        public void Clear()
        {
            foreach (KeyValuePair<long, List<CreatureAgent>> kv in _cells)
            {
                kv.Value.Clear();
                _pool.Push(kv.Value);
            }
            _cells.Clear();
        }

        public void Insert(CreatureAgent agent)
        {
            CellOf(agent.Position, out int x, out int z);
            long key = Key(x, z);
            if (!_cells.TryGetValue(key, out List<CreatureAgent> list))
            {
                list = _pool.Count > 0 ? _pool.Pop() : new List<CreatureAgent>(8);
                _cells[key] = list;
            }
            list.Add(agent);
        }

        public void Rebuild(IReadOnlyList<CreatureAgent> agents)
        {
            Clear();
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i] != null && agents[i].IsActive) Insert(agents[i]);
            }
        }

        /// <summary>
        /// Fills <paramref name="results"/> with agents within <paramref name="radius"/> of
        /// <paramref name="centre"/>, excluding <paramref name="exclude"/>, nearest first up
        /// to <paramref name="maxResults"/>. Returns the number written.
        /// </summary>
        public int Query(Vector3 centre, float radius, CreatureAgent exclude,
                         List<CreatureAgent> results, int maxResults)
        {
            results.Clear();
            if (maxResults <= 0) return 0;

            int span = Mathf.Max(1, Mathf.CeilToInt(radius / _cellSize));
            CellOf(centre, out int cx, out int cz);
            float sqrRadius = radius * radius;

            for (int dx = -span; dx <= span; dx++)
            {
                for (int dz = -span; dz <= span; dz++)
                {
                    if (!_cells.TryGetValue(Key(cx + dx, cz + dz), out List<CreatureAgent> list)) continue;

                    for (int i = 0; i < list.Count; i++)
                    {
                        CreatureAgent a = list[i];
                        if (a == null || a == exclude || !a.IsActive) continue;

                        Vector3 d = a.Position - centre;
                        float sqr = d.x * d.x + d.z * d.z;
                        if (sqr > sqrRadius) continue;

                        InsertSorted(results, a, sqr, maxResults, centre);
                    }
                }
            }

            return results.Count;
        }

        static void InsertSorted(List<CreatureAgent> results, CreatureAgent candidate,
                                 float candidateSqr, int maxResults, Vector3 centre)
        {
            int insertAt = results.Count;
            for (int i = 0; i < results.Count; i++)
            {
                Vector3 d = results[i].Position - centre;
                if (candidateSqr < d.x * d.x + d.z * d.z)
                {
                    insertAt = i;
                    break;
                }
            }

            if (insertAt >= maxResults) return;

            results.Insert(insertAt, candidate);
            if (results.Count > maxResults) results.RemoveAt(results.Count - 1);
        }
    }
}
