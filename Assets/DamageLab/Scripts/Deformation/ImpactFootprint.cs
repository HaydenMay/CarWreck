using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>Which part of the impacting box meets the bodywork within the crush depth.</summary>
    public enum ContactKind { Point, Face, Edge, Corner }

    /// <summary>Box-shaped impacting object at its first-contact pose, in vehicle space.</summary>
    [System.Serializable]
    public struct BoxImpactor
    {
        public bool valid;
        public Vector3 center;
        public Quaternion rotation;
        public Vector3 halfExtents;
    }

    /// <summary>
    /// Contact patch of a geometry-aware impact (vehicle space): the bodywork the impactor's leading surface
    /// sweeps through over the crush depth. Resolved once from the ORIGINAL mesh when the impact is added.
    /// </summary>
    [System.Serializable]
    public struct ImpactFootprint
    {
        public bool valid;
        public ContactKind kind;
        public Vector3 center;        // patch centre on the first-contact surface
        public Vector3 axisU, axisV;  // principal axes of the patch (perpendicular to the impact direction), U = longer
        public float halfU, halfV;    // half extents along the axes (m)
        public float area;            // projected patch area (m²)
        public float firstContact;    // distance (m) the impactor's surface was from the bodywork at first touch (≤ 0 = overlapping)
        public float travel;          // crush depth applied (m)
        public float areaFactor;      // depth multiplier from the contact area (small patch = deeper)
        public Vector3[] outline;     // patch outline on the crushed surface (for the debug view)
        public int seed;              // wrinkle-pattern seed: the impact's ordinal on the car (set when it is added)
    }

    /// <summary>
    /// A box seen along a direction d: a lateral frame (e1, e2), its projected silhouette (convex polygon) and
    /// its leading surface - for every lateral position, how far along d the box reaches. A face-on box has a
    /// flat leading surface; an edge or corner leads with a ridge or a point.
    /// </summary>
    public sealed class BoxProjection
    {
        public readonly Vector3 c, d, e1, e2;
        public readonly Vector2[] hull;   // CCW silhouette in (e1, e2), relative to c
        public readonly float frontA;     // furthest box point along d, relative to c
        readonly Vector3 ax, ay, az, h, dl;
        readonly Vector3[] corners = new Vector3[8];

        public BoxProjection(BoxImpactor box, Vector3 direction)
        {
            c = box.center;
            d = direction.normalized;
            Vector3 helper = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
            e1 = Vector3.Cross(helper, d).normalized;
            e2 = Vector3.Cross(d, e1);
            ax = box.rotation * Vector3.right;
            ay = box.rotation * Vector3.up;
            az = box.rotation * Vector3.forward;
            h = new Vector3(Mathf.Abs(box.halfExtents.x), Mathf.Abs(box.halfExtents.y), Mathf.Abs(box.halfExtents.z));
            dl = new Vector3(Vector3.Dot(d, ax), Vector3.Dot(d, ay), Vector3.Dot(d, az));
            var pts = new List<Vector2>(8);
            frontA = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 k = ax * (((i & 1) == 0 ? -1f : 1f) * h.x) + ay * (((i & 2) == 0 ? -1f : 1f) * h.y) + az * (((i & 4) == 0 ? -1f : 1f) * h.z);
                corners[i] = k;
                pts.Add(new Vector2(Vector3.Dot(k, e1), Vector3.Dot(k, e2)));
                frontA = Mathf.Max(frontA, Vector3.Dot(k, d));
            }
            hull = ConvexHull(pts).ToArray();
        }

        /// <summary>Number of box corners within <paramref name="depth"/> of the leading point (1 corner, 2 edge, 4 face).</summary>
        public int CornersWithin(float depth)
        {
            int n = 0;
            foreach (var k in corners) if (Vector3.Dot(k, d) >= frontA - depth) n++;
            return n;
        }

        /// <summary>How far along d (relative to c) the box reaches on the line through lateral position (u, v); NaN if the line misses it.</summary>
        public float Lead(float u, float v)
        {
            Vector3 p = e1 * u + e2 * v;
            Vector3 o = new Vector3(Vector3.Dot(p, ax), Vector3.Dot(p, ay), Vector3.Dot(p, az));
            float tEnter = float.MinValue, tExit = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float oi = o[i], di = dl[i], hi = h[i];
                if (Mathf.Abs(di) < 1e-6f)
                {
                    if (Mathf.Abs(oi) > hi + 1e-4f) return float.NaN;
                    continue;
                }
                float t1 = (-hi - oi) / di, t2 = (hi - oi) / di;
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                tEnter = Mathf.Max(tEnter, t1);
                tExit = Mathf.Min(tExit, t2);
            }
            return tEnter <= tExit + 1e-4f ? tExit : float.NaN;
        }

        /// <summary>Signed distance (m) from (u, v) to the silhouette (negative inside) and the nearest point
        /// of the silhouette (the point itself when inside, nudged 2 mm inward when outside).</summary>
        public float SignedDistance(float u, float v, out float qu, out float qv)
        {
            var q = new Vector2(u, v);
            bool inside = true;
            float best = float.MaxValue;
            Vector2 bestP = q;
            for (int i = 0; i < hull.Length; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Length];
                Vector2 ab = b - a, aq = q - a;
                if (ab.x * aq.y - ab.y * aq.x < 0f) inside = false;
                float t = Mathf.Clamp01(Vector2.Dot(aq, ab) / Mathf.Max(1e-9f, ab.sqrMagnitude));
                Vector2 p = a + ab * t;
                float dist = (q - p).sqrMagnitude;
                if (dist < best) { best = dist; bestP = p; }
            }
            best = Mathf.Sqrt(best);
            if (inside) { qu = u; qv = v; return -best; }
            Vector2 inward = (Centroid() - bestP);
            if (inward.sqrMagnitude > 1e-12f) bestP += inward.normalized * 0.002f;
            qu = bestP.x; qv = bestP.y;
            return best;
        }

        Vector2 Centroid()
        {
            Vector2 s = Vector2.zero;
            foreach (var p in hull) s += p;
            return s / Mathf.Max(1, hull.Length);
        }

        /// <summary>Andrew's monotone chain; returns the hull counter-clockwise without collinear points.</summary>
        public static List<Vector2> ConvexHull(List<Vector2> pts)
        {
            pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>(pts.Count + 1);
            if (pts.Count < 3) { hull.AddRange(pts); return hull; }
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < pts.Count; k++)
                {
                    Vector2 p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
                    while (hull.Count >= start + 2)
                    {
                        Vector2 a = hull[hull.Count - 2], b = hull[hull.Count - 1];
                        if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) > 1e-9f) break;
                        hull.RemoveAt(hull.Count - 1);
                    }
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        public static float PolygonArea(List<Vector2> poly)
        {
            float a = 0f;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.x * q.y - q.x * p.y;
            }
            return Mathf.Abs(a) * 0.5f;
        }
    }
}
