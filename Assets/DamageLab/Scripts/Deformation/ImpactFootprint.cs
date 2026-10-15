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
        public readonly Vector2 hullMin, hullMax;   // its axis-aligned extent
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
            hullMin = hullMax = hull.Length > 0 ? hull[0] : Vector2.zero;
            foreach (var hp in hull) { hullMin = Vector2.Min(hullMin, hp); hullMax = Vector2.Max(hullMax, hp); }

            // The envelope differs from the exact surface only when a face is within the draft of d (and not exactly
            // parallel to it, which ExactLead treats as an unbounded slab and which projects to no area).
            float draft = Mathf.Clamp(GrazingDraftDegrees, 0f, 45f);
            bool grazing = false;
            for (int i = 0; i < 3; i++) { float a = Mathf.Abs(dl[i]); if (a >= 1e-6f && a < Mathf.Sin(draft * Mathf.Deg2Rad)) grazing = true; }
            if (draft > 0f && grazing)
            {
                slopeLimit = 1f / Mathf.Tan(draft * Mathf.Deg2Rad);
                edgeP = new Vector2[12]; edgeW = new Vector2[12]; edgeT = new float[12]; edgeDt = new float[12];
                edgeLo = new Vector2[12]; edgeHi = new Vector2[12]; edgeTop = new float[12];
                int e = 0;
                for (int i = 0; i < 8; i++)
                    for (int bit = 1; bit < 8; bit <<= 1)
                    {
                        if ((i & bit) != 0) continue;
                        Vector3 a = corners[i], b = corners[i | bit];
                        edgeP[e] = new Vector2(Vector3.Dot(a, e1), Vector3.Dot(a, e2));
                        edgeW[e] = new Vector2(Vector3.Dot(b - a, e1), Vector3.Dot(b - a, e2));
                        edgeT[e] = Vector3.Dot(a, d);
                        edgeDt[e] = Vector3.Dot(b - a, d);
                        edgeLo[e] = Vector2.Min(edgeP[e], edgeP[e] + edgeW[e]);
                        edgeHi[e] = Vector2.Max(edgeP[e], edgeP[e] + edgeW[e]);
                        edgeTop[e] = Mathf.Max(edgeT[e], edgeT[e] + edgeDt[e]);
                        e++;
                    }
            }
        }

        /// <summary>Whether the draft envelope is active for this projection (a face is within the draft of d).</summary>
        public bool DraftLimited => slopeLimit > 0f;

        /// <summary>Axis-aligned bounds of the box (vehicle space).</summary>
        public Bounds BoxBounds()
        {
            var b = new Bounds(c, Vector3.zero);
            foreach (var k in corners) b.Encapsulate(c + k);
            return b;
        }

        /// <summary>Number of box corners within <paramref name="depth"/> of the leading point (1 corner, 2 edge, 4 face).</summary>
        public int CornersWithin(float depth)
        {
            int n = 0;
            foreach (var k in corners) if (Vector3.Dot(k, d) >= frontA - depth) n++;
            return n;
        }

        /// <summary>
        /// Contact draft (degrees). A box face within this angle of the impact direction projects to a sliver a few
        /// millimetres wide across which the exact leading surface drops by up to the whole box length, so a 0.1 mm shift
        /// of the impactor moved the contact depth there by centimetres (slope 1/tan of the grazing angle, ~280 for a face
        /// 0.2° off the direction). <see cref="Lead"/> is the exact surface's upper envelope under a cone of this half-angle
        /// about -d: the box swept by a 2° draft, i.e. a contact band of finite thickness. It never reaches outside the
        /// true silhouette or past the true front (orientation, width and height are the box's own), it equals the exact
        /// surface wherever every visible face is steeper than the draft against the direction (face-on and ordinary angled
        /// boxes: the exact surface is concave, so the envelope adds nothing), and a lateral shift δ changes it by at most
        /// δ / tan(draft). 0 = the exact surface. Read when the projection is built.
        /// </summary>
        public static float GrazingDraftDegrees = 2f;

        // Draft envelope: the 12 box edges projected along d (lateral start, lateral span, depth at start, depth change).
        readonly float slopeLimit;   // 1 / tan(draft); 0 = exact surface
        readonly Vector2[] edgeP, edgeW;
        readonly float[] edgeT, edgeDt;
        readonly Vector2[] edgeLo, edgeHi;   // lateral bounds of each projected edge
        readonly float[] edgeTop;            // deepest reach along the edge

        /// <summary>How far along d (relative to c) the box reaches on the line through lateral position (u, v), with the
        /// grazing faces limited to the contact draft (see <see cref="GrazingDraftDegrees"/>); NaN if the line misses the box.</summary>
        public float Lead(float u, float v)
        {
            float t = ExactLead(u, v);
            if (slopeLimit <= 0f || float.IsNaN(t)) return t;
            return Envelope(u, v, t);
        }

        /// <summary>
        /// The lead that a point at signed distance <paramref name="sd"/> outside the silhouette takes, given the
        /// silhouette point (qu, qv) from <see cref="SignedDistance"/>. On the exact surface this is Lead at that point,
        /// nudged 2 mm inside so the line cannot miss the box. A 2 mm step across a face within the draft of d is 2 mm / tan(draft)
        /// of depth (57 mm at 2°), so a vertex that a 0.1 mm shift carried across the silhouette jumped by that much. With the
        /// draft the envelope is read at the nearest silhouette point itself. It is defined there even when the exact line
        /// only grazes the edge, so the lead is continuous across the silhouette and changes by at most δ / tan(draft).
        /// </summary>
        public float OutsideLead(float u, float v, float sd, float qu, float qv)
        {
            if (sd <= 0f || slopeLimit <= 0f) return Lead(qu, qv);
            Nearest(new Vector2(u, v), out Vector2 b, out _);
            float t = ExactLead(b.x, b.y);
            return Envelope(b.x, b.y, float.IsNaN(t) ? float.MinValue : t);
        }

        float Envelope(float u, float v, float t)
        {
            // sup over the silhouette of ExactLead(q) - k|q - p|: attained at p or on a projected box edge (ExactLead is
            // concave and piecewise linear, its creases and boundary are projected edges). Hidden edges lie behind the
            // surface and only give lower bounds, so all 12 can be taken. Along an edge, depth is affine in the lateral
            // arc length x and the cone term is k·sqrt(x² + h²), so the maximum is closed-form.
            float k = slopeLimit;
            var p = new Vector2(u, v);
            for (int i = 0; i < 12; i++)
            {
                // The cone from this edge is at most edgeTop - k * (lateral distance to the edge), so further than reach
                // from the edge's bounds it stays more than k * 0.1 mm (~3 mm) below t and cannot raise it. The margin
                // dwarfs float rounding, so culling never changes the result; it skips most edges for most vertices.
                float reach = (edgeTop[i] - t) / k + 1e-4f;
                if (reach < 0f || p.x < edgeLo[i].x - reach || p.x > edgeHi[i].x + reach || p.y < edgeLo[i].y - reach || p.y > edgeHi[i].y + reach) continue;
                Vector2 w0 = edgeP[i] - p, w = edgeW[i];
                float t0 = edgeT[i], dt = edgeDt[i];
                float ww = w.sqrMagnitude;
                float best;
                if (ww < 1e-12f) best = Mathf.Max(t0, t0 + dt) - k * w0.magnitude;   // edge along d
                else
                {
                    float W = Mathf.Sqrt(ww), m = dt / W;
                    float s;
                    if (Mathf.Abs(m) < k)
                    {
                        float s0 = -Vector2.Dot(w0, w) / ww;
                        float hh = Mathf.Abs(w0.x * w.y - w0.y * w.x) / W;
                        s = Mathf.Clamp01(s0 + hh * m / Mathf.Sqrt(k * k - m * m) / W);
                    }
                    else s = m > 0f ? 1f : 0f;
                    best = t0 + s * dt - k * (w0 + s * w).magnitude;
                }
                if (best > t) t = best;
            }
            return t;
        }

        /// <summary>How far along d (relative to c) the exact box reaches on the line through lateral position (u, v); NaN if the line misses it.</summary>
        public float ExactLead(float u, float v)
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
            float best = Nearest(new Vector2(u, v), out Vector2 bestP, out bool inside);
            if (inside) { qu = u; qv = v; return best; }
            Vector2 inward = (Centroid() - bestP);
            if (inward.sqrMagnitude > 1e-12f) bestP += inward.normalized * 0.002f;
            qu = bestP.x; qv = bestP.y;
            return best;
        }

        // Signed distance from q to the silhouette (negative inside) and the nearest point of its boundary.
        float Nearest(Vector2 q, out Vector2 bestP, out bool inside)
        {
            inside = true;
            float best = float.MaxValue;
            bestP = q;
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
            return inside ? -best : best;
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
