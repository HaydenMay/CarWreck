using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>
    /// Where cars start and come back. The arena authors a ring of spawn points (child transforms, heading towards the middle); for more cars than that, or for
    /// respawns, extra candidates are generated on rings of the arena, so the count is not limited by the authored layout. Initial layout: the candidates that
    /// are furthest apart. Respawn: a candidate with clear ground (no car or obstacle in the car's footprint) that is as far as possible from every other car;
    /// if every candidate is blocked the least bad one is used rather than none.
    /// </summary>
    public static class SpawnPlanner
    {
        public struct Spot { public Vector3 position; public float yaw; }

        /// <summary>Authored points first, then generated ones (rings at 70 / 45 / 20 % of the half size, at least ~16 m apart) until there are <paramref name="minCount"/>.</summary>
        public static List<Spot> Candidates(Transform authored, float half, int minCount)
        {
            var list = new List<Spot>();
            if (authored != null)
                for (int i = 0; i < authored.childCount; i++)
                {
                    var t = authored.GetChild(i);
                    list.Add(new Spot { position = new Vector3(t.position.x, 0f, t.position.z), yaw = t.eulerAngles.y });
                }
            float[] radii = { 0.7f, 0.45f, 0.2f };
            for (int ring = 0; list.Count < minCount && ring < radii.Length * 3; ring++)
            {
                float r = half * radii[ring % radii.Length] * (ring < radii.Length ? 1f : ring < 2 * radii.Length ? 0.9f : 0.8f);
                int n = Mathf.Max(3, Mathf.FloorToInt(2f * Mathf.PI * r / 16f));
                float offset = 17f * ring;
                for (int i = 0; i < n && list.Count < Mathf.Max(minCount, 0) + 0; i++)
                {
                    float a = (offset + 360f * i / n) * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    if (TooClose(list, p, 12f)) continue;
                    list.Add(new Spot { position = p, yaw = FaceCentre(p) });
                }
            }
            return list;
        }

        /// <summary>A denser pool for respawns: the candidates plus rings every ~12 m, all facing the middle.</summary>
        public static List<Spot> RespawnPool(Transform authored, float half)
        {
            var list = Candidates(authored, half, 0);
            foreach (float f in new[] { 0.75f, 0.5f, 0.25f })
            {
                float r = half * f;
                int n = Mathf.Max(6, Mathf.FloorToInt(2f * Mathf.PI * r / 12f));
                for (int i = 0; i < n; i++)
                {
                    float a = 360f * i / n * Mathf.Deg2Rad;
                    var p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    if (!TooClose(list, p, 6f)) list.Add(new Spot { position = p, yaw = FaceCentre(p) });
                }
            }
            return list;
        }

        public static float FaceCentre(Vector3 p) => Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;

        static bool TooClose(List<Spot> list, Vector3 p, float d)
        {
            foreach (var s in list) if ((s.position - p).sqrMagnitude < d * d) return true;
            return false;
        }

        /// <summary>The <paramref name="count"/> candidates that are furthest apart (farthest-point sampling, starting from the one nearest the given start angle).</summary>
        public static Spot[] InitialLayout(List<Spot> candidates, int count, int seed)
        {
            var result = new List<Spot>();
            if (candidates.Count == 0) return result.ToArray();
            var rng = new System.Random(seed);
            var used = new bool[candidates.Count];
            int first = rng.Next(candidates.Count);
            result.Add(candidates[first]); used[first] = true;
            while (result.Count < count && result.Count < candidates.Count)
            {
                int best = -1; float bestD = -1f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (used[i]) continue;
                    float d = float.MaxValue;
                    foreach (var r in result) d = Mathf.Min(d, (r.position - candidates[i].position).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = i; }
                }
                result.Add(candidates[best]); used[best] = true;
            }
            return result.ToArray();
        }

        /// <summary>Best respawn spot: not blocked, then furthest from every car in <paramref name="others"/>; random tie-break among the near-best.</summary>
        public static Spot PickRespawn(List<Spot> pool, IList<Vector3> others, Func<Spot, bool> blocked, System.Random rng, out bool wasClear)
        {
            Spot best = pool[0]; float bestScore = float.NegativeInfinity; wasClear = false;
            for (int i = 0; i < pool.Count; i++)
            {
                var s = pool[i];
                float d = float.MaxValue;
                for (int k = 0; k < others.Count; k++) d = Mathf.Min(d, (others[k] - s.position).magnitude);
                if (others.Count == 0) d = 100f;
                bool free = !blocked(s);
                float score = (free ? 1000f : 0f) + Mathf.Min(d, 60f) + (float)rng.NextDouble() * 6f;   // clear ground first, then distance (with a little variety)
                if (score > bestScore) { bestScore = score; best = s; wasClear = free; }
            }
            return best;
        }
    }
}
