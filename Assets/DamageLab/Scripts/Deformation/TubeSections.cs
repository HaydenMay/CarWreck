using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Finds the cross-sections of tube-like geometry (coil springs, rods, pipes) from the mesh alone: a curved vertex's
    /// local tube radius is the smallest |Δposition| / |Δnormal| over its edges, its section centre lies that far behind it
    /// along the normal, and edges whose two ends share a centre (the ring edges around the tube, not the edges along it)
    /// join one section. A group is accepted only when it has at least three members that all sit at about the same
    /// distance from the common centre. Flat faces and anything else stay unbound. Cap vertices whose neighbours all
    /// belong to one section join it. Nothing here is specific to a vehicle or a part name.
    /// </summary>
    public static class TubeSections
    {
        /// <summary>Section index per vertex (-1 = unbound); <paramref name="count"/> = number of sections.</summary>
        public static int[] Build(Vector3[] pos, Vector3[] normals, int[] tris, out int count)
        {
            int n = pos.Length;
            var section = new int[n];
            for (int i = 0; i < n; i++) section[i] = -1;
            count = 0;
            if (n == 0 || tris == null || tris.Length == 0) return section;

            // Weld coincident vertices (UV seams / hard edges) so a ring is one connected loop.
            var rep = new int[n];
            var weld = new Dictionary<Vector3Int, int>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = pos[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
                if (weld.TryGetValue(key, out int r)) rep[i] = r; else { weld[key] = i; rep[i] = i; }
            }

            var edgeSet = new HashSet<long>();
            var edges = new List<(int a, int b)>();
            var adj = new List<int>[n];
            void AddEdge(int a, int b)
            {
                a = rep[a]; b = rep[b];
                if (a == b) return;
                if (a > b) (a, b) = (b, a);
                if (!edgeSet.Add(((long)a << 32) | (uint)b)) return;
                edges.Add((a, b));
                (adj[a] ??= new List<int>(6)).Add(b);
                (adj[b] ??= new List<int>(6)).Add(a);
            }
            for (int t = 0; t + 2 < tris.Length; t += 3) { AddEdge(tris[t], tris[t + 1]); AddEdge(tris[t + 1], tris[t + 2]); AddEdge(tris[t + 2], tris[t]); }

            // Local tube radius and section centre per curved vertex.
            var radius = new float[n];
            for (int i = 0; i < n; i++) radius[i] = float.PositiveInfinity;
            foreach (var (a, b) in edges)
            {
                float dn = (normals[a] - normals[b]).magnitude;
                if (dn < 1e-3f) continue;   // flat across this edge
                float q = (pos[a] - pos[b]).magnitude / dn;
                if (q < radius[a]) radius[a] = q;
                if (q < radius[b]) radius[b] = q;
            }
            var centre = new Vector3[n];
            for (int i = 0; i < n; i++) if (rep[i] == i && !float.IsInfinity(radius[i])) centre[i] = pos[i] - radius[i] * normals[i];

            // Union the edges whose ends share a centre: ring edges merge, edges along the tube do not.
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            foreach (var (a, b) in edges)
            {
                if (float.IsInfinity(radius[a]) || float.IsInfinity(radius[b])) continue;
                if ((centre[a] - centre[b]).magnitude < 0.25f * (pos[a] - pos[b]).magnitude)
                {
                    int ra = Find(a), rb = Find(b);
                    if (ra != rb) parent[ra] = rb;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                if (rep[i] != i || float.IsInfinity(radius[i])) continue;
                int root = Find(i);
                if (!groups.TryGetValue(root, out var g)) groups[root] = g = new List<int>(8);
                g.Add(i);
            }
            var repSection = new int[n];
            for (int i = 0; i < n; i++) repSection[i] = -1;
            foreach (var g in groups.Values)
            {
                if (g.Count < 3) continue;
                Vector3 c = Vector3.zero;
                foreach (int i in g) c += centre[i];
                c /= g.Count;
                float mean = 0f;
                foreach (int i in g) mean += (pos[i] - c).magnitude;
                mean /= g.Count;
                if (mean <= 1e-5f) continue;
                bool round = true;
                foreach (int i in g) if (Mathf.Abs((pos[i] - c).magnitude - mean) > 0.15f * mean) { round = false; break; }
                if (!round) continue;
                foreach (int i in g) repSection[i] = count;
                count++;
            }

            // End caps: an unbound vertex whose neighbours all belong to one section joins it.
            for (int i = 0; i < n; i++)
            {
                if (rep[i] != i || repSection[i] >= 0 || adj[i] == null || adj[i].Count < 3) continue;
                int s = repSection[adj[i][0]];
                if (s < 0) continue;
                bool all = true;
                foreach (int j in adj[i]) if (repSection[j] != s) { all = false; break; }
                if (all) repSection[i] = s;
            }

            for (int i = 0; i < n; i++) section[i] = repSection[rep[i]];
            return section;
        }
    }
}
