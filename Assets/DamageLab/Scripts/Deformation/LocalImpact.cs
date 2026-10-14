using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>One click-to-damage event, stored in vehicle space. The mesh is always rebuilt from the
    /// original geometry plus the list of these, so impacts are deterministic and removable.</summary>
    [System.Serializable]
    public struct LocalImpact
    {
        public Vector3 point;     // hit point on the original surface
        public Vector3 direction; // incoming ray direction (unit), the way the metal is pushed
        public Vector3 normal;    // outward surface normal at the hit point
        public float strength;    // 0..1
        // Optional: the impacting object's shape. When set (physical collisions), the dent takes the shape of the
        // contact patch instead of the round localized profile; the footprint is resolved when the impact is added.
        public BoxImpactor impactor;
        public ImpactFootprint footprint;
    }

    /// <summary>Tuning for localized impacts (all lengths in metres).</summary>
    [System.Serializable]
    public class ImpactSettings
    {
        [Tooltip("Crush depth at the impact centre for strength 100.")]
        public float maxDepth = 0.42f;
        public float depthExponent = 1.15f;
        [Tooltip("Dent radius at strength 0 / 100.")]
        public float radiusMin = 0.16f;
        public float radiusMax = 0.60f;
        [Tooltip("Depth multiplier for a grazing hit (ray parallel to the surface); a square-on hit is 1.")]
        public float grazingDepth = 0.45f;
        [Tooltip("Higher = flatter dent floor with a steeper wall.")]
        public float profileSharpness = 2.6f;
        public float footprintIrregularity = 0.22f;
        [Tooltip("Number of radial buckles around the dent.")]
        public int buckleLobes = 6;
        public float buckleStrength = 0.26f;
        public float rimCrease = 0.15f;
        public float floorWrinkle = 0.05f;
        [Tooltip("Small outward lip just outside the dent.")]
        public float lip = 0.06f;
        public float drawIn = 0.12f;
        [Header("Structural limits (crush capacity per vertex, m)")]
        [Tooltip("Outer body panels: doors, fenders, hood, trunk.")]
        public float panelLimit = 0.50f;
        [Tooltip("Passenger compartment (above the roof line start): pillars, roof.")]
        public float cabinLimit = 0.36f;
        [Tooltip("Metal around the wheel mounts.")]
        public float wheelLimit = 0.20f;
        [Tooltip("Distance from a wheel centre where the wheel limit applies / fades out to the panel limit.")]
        public float wheelInner = 0.50f;
        public float wheelOuter = 0.95f;
        [Tooltip("Fraction of the limit that is reached linearly; beyond it further damage has diminishing returns.")]
        [Range(0.2f, 0.95f)] public float saturationKnee = 0.6f;
        [Header("Spreading on repeated hits")]
        [Tooltip("Footprint radius growth per unit of earlier damage at the hit point.")]
        public float spreadPerHit = 0.15f;
        public float maxSpread = 1.5f;
        [Tooltip("Work hardening: depth of a new hit is divided by (1 + hardening · earlier damage at that spot).")]
        public float hardening = 0.6f;
        [Header("Geometry-aware impacts (box impactor)")]
        [Tooltip("Contact area (m²) at which the crush depth is unchanged; smaller patches crush deeper, larger ones shallower.")]
        public float referenceArea = 0.5f;
        public float areaExponent = 0.35f;
        public float areaFactorMin = 0.6f;
        public float areaFactorMax = 1.6f;
        [Tooltip("Fraction of the overlap that is crushed flat; the rest stays as bunched-up metal.")]
        [Range(0.5f, 1f)] public float crushCompression = 0.85f;
        [Tooltip("Variation of the crush depth across the contact face, so the crushed wall is not perfectly flat.")]
        public float faceUnevenness = 0.15f;
        [Tooltip("Length (m) of the accordion folds along the crush direction, and their height relative to the crush.")]
        public float foldWavelength = 0.14f;
        public float foldStrength = 0.35f;
        [Tooltip("Width (m) over which metal just outside the impactor's outline is dragged along (+ per metre of crush).")]
        public float edgeFalloff = 0.08f;
        public float edgeFalloffPerDepth = 0.3f;
        [Tooltip("Softening (m) of the crease where crushed metal meets undamaged metal.")]
        public float edgeSoftness = 0.03f;
        [Tooltip("Vertical pleats across the crushed face: spacing (m) and depth (fraction of the crush).")]
        public float pleatWavelength = 0.24f;
        public float pleatDepth = 0.12f;
        [Tooltip("Panels behind the crushed metal tent outward: length (× crush depth, + 0.1 m) and height (× crush depth).")]
        public float tentLength = 2.5f;
        public float tentHeight = 0.22f;
    }

    /// <summary>
    /// Localized crush for an arbitrary 3D impact direction. Same ideas as <see cref="CrushField"/>, but in
    /// a frame built from the click: depth a is measured along the ray, footprint distance l across it.
    ///   push    D·g(l)·w(a): super-gaussian footprint with an irregular outline, monotonic depth falloff
    ///           w = 1 - smoothstep(a / L), L ≥ 1.5·D, so the dent cannot fold through itself.
    ///   buckles a few radial triangle-wave folds on the dent wall (sheet metal gathers into creases
    ///           when it is pushed into a smaller area) plus ridged creases on the rim.
    ///   lip     a slight outward roll just outside the dent so the edge reads as bent metal.
    /// Several impacts are summed, then the total is saturated against a per-vertex structural limit
    /// (soft panels, stiffer cabin, stiff wheel mounts): linear up to a knee, then diminishing returns.
    /// A hit on metal that is already damaged spreads wider (and rolls a smaller lip), so repeated hits
    /// widen the crushed area instead of drilling one spot ever deeper.
    /// </summary>
    public sealed class ImpactField
    {
        struct State
        {
            public Vector3 p, d, e1, e2, noiseOffset;
            public float depth, radius, invRadius, zone, reach2, phase, lipScale;
            // Geometry-aware (box impactor) impacts
            public BoxProjection box;
            public float gapMin, travel, rho;
            public Vector3 pleatAxis, centreLat;
        }

        readonly ImpactSettings cfg;
        readonly DeterministicNoise noise = new DeterministicNoise(71);
        readonly List<State> states = new List<State>();
        Vector3[] wheelCenters = new Vector3[0];
        float cabinStart = 1.05f;

        public ImpactField(ImpactSettings settings) { cfg = settings ?? new ImpactSettings(); }

        public int Count => states.Count;

        public void Clear() => states.Clear();

        /// <summary>Forget the oldest impact record (its displacement stays in the parts' cached sums).</summary>
        public void RemoveOldest() { if (states.Count > 0) states.RemoveAt(0); }

        /// <summary>Lightweight structure map: wheel mount positions and the height where the cabin starts (vehicle space).</summary>
        public void SetStructure(Vector3[] wheels, float cabinLineStart)
        {
            wheelCenters = wheels ?? new Vector3[0];
            cabinStart = cabinLineStart;
        }

        /// <summary>How far (m) the metal at x can be crushed by localized impacts before it is saturated.</summary>
        public float Capacity(Vector3 x, float crumple)
        {
            // Passenger compartment is stiffer than the outer panels; blend over ±0.1 m.
            float cabin = Mathf.Clamp01((x.y - (cabinStart - 0.1f)) / 0.2f);
            float c = Mathf.Lerp(cfg.panelLimit, cfg.cabinLimit, cabin * cabin * (3f - 2f * cabin));
            // Wheel mounts: the fender can crumple around the wheel but not wander away from it.
            foreach (var w in wheelCenters)
            {
                float t = Mathf.Clamp01(((x - w).magnitude - cfg.wheelInner) / Mathf.Max(0.01f, cfg.wheelOuter - cfg.wheelInner));
                c = Mathf.Min(c, Mathf.Lerp(cfg.wheelLimit, c, t * t * (3f - 2f * t)));
            }
            // Stiff parts (bumpers, lights) give less.
            return c * (0.6f + 0.4f * Mathf.Clamp01(crumple));
        }

        /// <summary>Add one impact; returns its index for <see cref="Contribution"/>.</summary>
        public int Add(LocalImpact im)
        {
            var st = new State();
            st.p = im.point;
            st.d = im.direction.normalized;
            float s = Mathf.Clamp01(im.strength);
            float prior = Prior(im.point);
            // Work hardening: crushed metal absorbs less of a new hit, so repeats give diminishing depth.
            st.depth = BaseDepth(im) / (1f + cfg.hardening * prior);
            st.lipScale = 1f / (1f + prior);
            if (im.impactor.valid && im.footprint.valid)
            {
                // Geometry-aware: the depth was resolved with the footprint (incl. hardening and contact area).
                st.box = new BoxProjection(im.impactor, st.d);
                st.gapMin = im.footprint.firstContact;
                st.travel = im.footprint.travel;
                st.depth = st.travel;
                st.radius = Mathf.Max(0.1f, Mathf.Sqrt(im.footprint.halfU * im.footprint.halfV));
                st.rho = cfg.edgeFalloff + cfg.edgeFalloffPerDepth * st.travel;
                Vector3 h = Vector3.Cross(Vector3.up, st.d);
                st.pleatAxis = h.sqrMagnitude > 1e-4f ? h.normalized : st.box.e1;
                Vector3 rc = im.footprint.center - st.box.c;
                st.centreLat = rc - st.box.d * Vector3.Dot(rc, st.box.d);
            }
            else
                st.radius = Mathf.Lerp(cfg.radiusMin, cfg.radiusMax, s) * (1f + cfg.spreadPerHit * Mathf.Min(prior, cfg.maxSpread));
            st.invRadius = 1f / st.radius;
            st.zone = 1.5f * st.depth + 0.12f;
            float r = 1.8f * st.radius;
            st.reach2 = r * r;
            Vector3 helper = Mathf.Abs(st.d.y) < 0.9f ? Vector3.up : Vector3.right;
            st.e1 = Vector3.Cross(helper, st.d).normalized;
            st.e2 = Vector3.Cross(st.d, st.e1);
            // Noise offset and buckle phase come from the hit position, so the same click always
            // produces the same dent pattern (its spread still depends on the damage already there).
            // Physics contacts jitter by fractions of a millimetre between otherwise identical runs, and any quantised
            // position can flip at a cell edge. Geometry-aware impacts are therefore seeded by their ordinal in the impact
            // sequence (stored with the footprint): the same sequence always gives the same wrinkles, jitter or not.
            Vector3 seed = st.box != null ? new Vector3(im.footprint.seed * 0.731f, im.footprint.seed * 1.379f, im.footprint.seed * 2.113f) : im.point;
            st.noiseOffset = new Vector3(Hash(seed.x * 13.1f + seed.z * 3.7f), Hash(seed.y * 17.3f + seed.x * 5.1f), Hash(seed.z * 11.7f + seed.y * 7.9f)) * 40f;
            st.phase = Hash(seed.x * 7.3f + seed.y * 3.1f + seed.z * 5.9f);
            states.Add(st);
            return states.Count - 1;
        }

        /// <summary>Crush depth of a fresh hit from its strength and how square-on it is (before hardening / area).</summary>
        float BaseDepth(in LocalImpact im)
        {
            float facing = Mathf.Clamp01(Vector3.Dot(-im.direction.normalized, im.normal));
            return cfg.maxDepth * Mathf.Pow(Mathf.Clamp01(im.strength), cfg.depthExponent) * Mathf.Lerp(cfg.grazingDepth, 1f, facing);
        }

        /// <summary>Earlier damage at a spot (sum of earlier footprints, weighted by their depth): the metal there is
        /// already near its limit, so a new hit spreads into the surrounding panels.</summary>
        float Prior(Vector3 point)
        {
            float prior = 0f;
            foreach (var o in states)
            {
                Vector3 r0 = point - o.p;
                Vector3 lat0 = r0 - o.d * Vector3.Dot(r0, o.d);
                prior += o.depth / Mathf.Max(1e-3f, cfg.maxDepth) * Mathf.Exp(-lat0.sqrMagnitude * o.invRadius * o.invRadius);
            }
            return prior;
        }

        /// <summary>
        /// Contact patch of a box impactor against the CURRENT bodywork (vehicle-space vertex positions: the original
        /// mesh plus the saturated impact damage so far; on an undamaged car that is the original mesh):
        ///   1. first contact: the vertex whose gap to the box's leading surface (measured along the impact
        ///      direction, inside the box's silhouette) is smallest;
        ///   2. patch: every vertex the leading surface would sweep through over the crush depth, i.e. the
        ///      overlap of the impactor with the car (face-on = broad, edge = a strip, corner = a small patch);
        ///   3. contact area: the same energy over a smaller patch crushes deeper, over a larger one shallower.
        /// Fills <see cref="LocalImpact.footprint"/> and moves the impact point to the patch centre; returns false
        /// (and leaves the impact as an ordinary localized one) if the box does not overlap the car at all.
        /// </summary>
        public bool ResolveFootprint(ref LocalImpact im, IEnumerable<Vector3[]> vertexSets, bool damagedSurface = false)
        {
            if (!im.impactor.valid) return false;
            var pj = new BoxProjection(im.impactor, im.direction);
            var inside = new List<Vector3>(4096);      // (u, v, gap) of vertices within the silhouette
            float gapMin = float.MaxValue;
            Vector3 first = im.point;
            foreach (var verts in vertexSets)
                foreach (var x in verts)
                {
                    Vector3 r = x - pj.c;
                    float u = Vector3.Dot(r, pj.e1), v = Vector3.Dot(r, pj.e2);
                    if (pj.SignedDistance(u, v, out _, out _) > 0f) continue;
                    float lead = pj.Lead(u, v);
                    if (float.IsNaN(lead)) continue;
                    float gap = Vector3.Dot(r, pj.d) - lead;
                    inside.Add(new Vector3(u, v, gap));
                    if (gap < gapMin) { gapMin = gap; first = x; }
                }
            if (inside.Count == 0) return false;
            // On already-damaged bodywork the impactor stops at the (retracted) physical hitbox, which can sit behind
            // metal that still sticks out. That metal is crushed from the impactor face, not from its own tip.
            if (damagedSurface && gapMin < 0f) gapMin = 0f;

            float t0 = BaseDepth(im) / (1f + cfg.hardening * Prior(first));
            var hull0 = BoxProjection.ConvexHull(PatchPoints(inside, gapMin, t0));
            float area0 = BoxProjection.PolygonArea(hull0);
            float factor = Mathf.Clamp(Mathf.Pow(cfg.referenceArea / Mathf.Max(0.01f, area0), cfg.areaExponent), cfg.areaFactorMin, cfg.areaFactorMax);
            float travel = t0 * factor;

            var pts = PatchPoints(inside, gapMin, travel);
            Vector2 mean = Vector2.zero;
            foreach (var p in pts) mean += p;
            mean /= Mathf.Max(1, pts.Count);
            float cxx = 0f, cyy = 0f, cxy = 0f;
            foreach (var p in pts) { Vector2 q = p - mean; cxx += q.x * q.x; cyy += q.y * q.y; cxy += q.x * q.y; }
            float ang = 0.5f * Mathf.Atan2(2f * cxy, cxx - cyy);
            Vector2 du = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), dv = new Vector2(-du.y, du.x);
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in pts)
            {
                float a = Vector2.Dot(p - mean, du), b = Vector2.Dot(p - mean, dv);
                minU = Mathf.Min(minU, a); maxU = Mathf.Max(maxU, a); minV = Mathf.Min(minV, b); maxV = Mathf.Max(maxV, b);
            }
            Vector2 mid = mean + du * (0.5f * (minU + maxU)) + dv * (0.5f * (minV + maxV));
            var hull = BoxProjection.ConvexHull(pts);

            var fp = new ImpactFootprint
            {
                valid = true,
                axisU = pj.e1 * du.x + pj.e2 * du.y,
                axisV = pj.e1 * dv.x + pj.e2 * dv.y,
                halfU = Mathf.Max(0.01f, 0.5f * (maxU - minU)),
                halfV = Mathf.Max(0.01f, 0.5f * (maxV - minV)),
                area = BoxProjection.PolygonArea(hull),
                firstContact = gapMin,
                travel = travel,
                areaFactor = factor,
            };
            if (fp.halfV > fp.halfU) { (fp.axisU, fp.axisV) = (fp.axisV, -fp.axisU); (fp.halfU, fp.halfV) = (fp.halfV, fp.halfU); }
            int corners = pj.CornersWithin(travel);
            fp.kind = corners >= 4 ? ContactKind.Face : corners >= 2 ? ContactKind.Edge : ContactKind.Corner;
            fp.center = SurfaceAt(pj, mid, gapMin, first);
            // Outline on the crushed surface (slightly in front of it so it stays visible).
            fp.outline = new Vector3[hull.Count];
            float crushed = gapMin + travel * cfg.crushCompression - 0.02f;
            for (int i = 0; i < hull.Count; i++) fp.outline[i] = SurfaceAt(pj, hull[i] + (mid - hull[i]).normalized * 0.002f, crushed, first);

            im.footprint = fp;
            im.point = fp.center;
            return true;
        }

        /// <summary>Lateral positions (2 cm grid, de-duplicated) of the vertices within <paramref name="depth"/> of the first contact.</summary>
        static List<Vector2> PatchPoints(List<Vector3> inside, float gapMin, float depth)
        {
            var seen = new HashSet<long>();
            var pts = new List<Vector2>();
            foreach (var g in inside)
            {
                if (g.z - gapMin > depth) continue;
                long key = ((long)Mathf.RoundToInt(g.x * 50f) << 32) ^ (uint)Mathf.RoundToInt(g.y * 50f);
                if (seen.Add(key)) pts.Add(new Vector2(g.x, g.y));
            }
            return pts;
        }

        static Vector3 SurfaceAt(BoxProjection pj, Vector2 uv, float offset, Vector3 fallback)
        {
            float lead = pj.Lead(uv.x, uv.y);
            if (float.IsNaN(lead)) lead = Vector3.Dot(fallback - pj.c, pj.d) - offset;
            return pj.c + pj.e1 * uv.x + pj.e2 * uv.y + pj.d * (lead + offset);
        }

        static float Hash(float x)
        {
            float h = Mathf.Sin(x * 12.9898f + 4.1414f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Raw displacement (vehicle space) from impact <paramref name="index"/> of a vertex at original
        /// position x with smoothed normal n. Parts cache the sum of these, so a new click costs one impact.</summary>
        public Vector3 Contribution(int index, Vector3 x, Vector3 n, float crumple) => Contribution(index, x, n, crumple, Vector3.zero, 1f);

        /// <summary>As above, given the vertex's summed raw displacement from the EARLIER impacts and its capacity.
        /// Geometry-aware impacts crush the CURRENT surface: the vertex's present position (original + saturated
        /// earlier damage) decides how far the impactor's face is from it. The result is still a pure function of
        /// the original mesh and the impact sequence, so rebuilding stays deterministic.</summary>
        public Vector3 Contribution(int index, Vector3 x, Vector3 n, float crumple, Vector3 priorSum, float capacity)
        {
            var st = states[index];
            if (st.box == null) return One(st, x, n, crumple);
            float priorAlong = priorSum.sqrMagnitude > 0f ? Vector3.Dot(Limit(priorSum, capacity), st.box.d) : 0f;
            return OneBox(st, x, n, crumple, priorAlong);
        }

        /// <summary>Saturate the summed contributions of all impacts against the vertex's crush capacity:
        /// linear up to knee·capacity, then easing toward the capacity (diminishing returns, no hard stop).</summary>
        public Vector3 Limit(Vector3 sum, float capacity)
        {
            float m = sum.magnitude;
            float knee = cfg.saturationKnee * capacity;
            if (m <= knee) return sum;
            float span = Mathf.Max(1e-3f, capacity - knee);
            float limited = knee + span * Tanh((m - knee) / span);
            return sum * (limited / m);
        }

        static float Tanh(float x)
        {
            float e = Mathf.Exp(-2f * x);
            return (1f - e) / (1f + e);
        }

        /// <summary>
        /// Geometry-aware crush: the impactor's leading surface advances <c>travel</c> past the first contact and
        /// the bodywork it passes through is pushed back to (nearly) that surface - a face gives a broad flat crush,
        /// an edge a crease, a corner a concentrated pocket.
        ///   gap      distance along d from the leading surface to the original vertex, minus the first-contact gap
        ///   crush    travel - gap (softened at 0 so the boundary is a crease, not a tear); only a fraction
        ///            (crushCompression) is applied so the compressed metal keeps some spacing; the crush plane is
        ///            made uneven (waves + vertical pleats) using noise in lateral coordinates only
        ///   tent     behind the crush, panels running along d bow outward (hood / fender buckling)
        ///   folds    panels running along d (fender sides, hood) bunch up: an accordion triangle wave along
        ///            the original depth, displacing the panel out of its plane (away from the crush axis)
        ///   edge     metal just outside the impactor's silhouette is dragged along with a short falloff and
        ///            pulled in toward the crushed area; ridged creases mark the boundary of the patch
        /// The result is summed with the other impacts and saturated per vertex like every localized impact.
        /// </summary>
        Vector3 OneBox(in State st, Vector3 x, Vector3 n, float crumple, float priorAlong)
        {
            var pj = st.box;
            Vector3 r = x - pj.c;
            float a = Vector3.Dot(r, pj.d) + priorAlong;   // current depth along d (original + earlier crush)
            float soft = cfg.edgeSoftness;
            float tentLen = cfg.tentLength * st.travel + 0.1f;
            float deepest = st.travel * (1f + cfg.faceUnevenness + cfg.pleatDepth);
            if (a - pj.frontA - st.gapMin > deepest + tentLen) return Vector3.zero;   // behind the affected zone
            float u = Vector3.Dot(r, pj.e1), v = Vector3.Dot(r, pj.e2);
            float sd = pj.SignedDistance(u, v, out float qu, out float qv);
            if (sd > 3f * st.rho) return Vector3.zero;
            float lead = pj.Lead(qu, qv);
            if (float.IsNaN(lead)) return Vector3.zero;
            float gap = a - lead - st.gapMin;
            float lat = sd > 0f ? Mathf.Exp(-(sd / st.rho) * (sd / st.rho)) : 1f;

            // Noise in lateral coordinates only (constant along d), so crushing never reorders metal along d.
            Vector3 latPos = pj.e1 * u + pj.e2 * v;
            Vector3 ql = latPos + st.noiseOffset;
            // Crush plane = the impactor face, made uneven: soft waves plus vertical pleats (sheet metal gathering).
            float tp = Vector3.Dot(latPos, st.pleatAxis) / cfg.pleatWavelength + st.phase + 0.5f * noise.Fbm(ql * 1.3f, 2);
            float pleat = 1f - 4f * Mathf.Abs(tp - Mathf.Floor(tp) - 0.5f);
            float plane = st.travel * (1f + cfg.faceUnevenness * noise.Fbm(ql * 2.4f, 2) + cfg.pleatDepth * pleat);
            float over = Mathf.Min(plane - gap, plane);
            Vector3 nl = n - pj.d * Vector3.Dot(n, pj.d);

            // Behind the crushed metal, panels running along d (hood, fenders, door skin) tent outward.
            float behind = -over;
            float tent = behind > 0f && behind < tentLen
                ? cfg.tentHeight * st.travel * Mathf.Sin(Mathf.PI * behind / tentLen) * lat * crumple * (0.6f + 0.4f * noise.Perlin(ql * 2.1f))
                : 0f;
            // Out-of-plane offsets (tent, folds, creases) follow a smooth field pointing away from the crush axis,
            // scaled by how well the panel faces it: both skins of a thin part (grille slats) move the same way
            // instead of along their opposite normals, so they never pass through each other.
            Vector3 radial = latPos - st.centreLat;
            float rl = radial.magnitude;
            Vector3 outward = radial / (rl + 0.15f);
            float align = rl > 1e-5f ? Mathf.Abs(Vector3.Dot(nl, radial)) / rl : 0f;
            if (over <= -soft) return outward * (tent * align);

            float c = over >= soft ? over : (over + soft) * (over + soft) / (4f * soft);
            float push = c * cfg.crushCompression * lat;

            // Accordion folds on panels that run along the crush direction.
            Vector3 q = x + st.noiseOffset;
            float tf = gap / cfg.foldWavelength + st.phase + 0.45f * noise.Fbm(q * 1.7f, 2);
            float tri = 1f - 4f * Mathf.Abs(tf - Mathf.Floor(tf) - 0.5f);
            float fold = cfg.foldStrength * Mathf.Min(c, cfg.foldWavelength) * lat * tri * crumple * (0.6f + 0.4f * noise.Perlin(q * 3.1f));

            // Creases along the boundary of the crushed patch (depth edge and silhouette edge).
            float bw = 0.25f * st.travel + 0.02f;
            float band = Mathf.Max(Mathf.Exp(-(over / bw) * (over / bw)), 4f * lat * (1f - lat));
            float crease = cfg.rimCrease * st.travel * crumple * band * noise.Ridged(q * 3.4f);

            Vector3 disp = pj.d * push + outward * ((fold + crease + tent) * align);
            if (sd > 1e-4f)   // pulled in toward the crushed area
                disp += (pj.e1 * (qu - u) + pj.e2 * (qv - v)) * (cfg.drawIn * push / sd);
            return disp;
        }

        Vector3 One(in State st, Vector3 x, Vector3 n, float crumple)
        {
            Vector3 r = x - st.p;
            float a = Vector3.Dot(r, st.d);
            if (a > st.zone) return Vector3.zero;
            Vector3 lat = r - st.d * a;
            float l2 = lat.sqrMagnitude;
            if (l2 > st.reach2) return Vector3.zero;

            // Depth falloff along the ray: full at/above the hit surface, gone at the zone length.
            float w;
            if (a <= 0f)
            {
                // Geometry standing proud of the hit point (e.g. a pillar next to the hood) still
                // moves, but fades out if it is far in front of the impact.
                float tf = (-a * st.invRadius - 0.6f) / 0.6f;
                w = tf <= 0f ? 1f : tf >= 1f ? 0f : 1f - tf * tf * (3f - 2f * tf);
            }
            else
            {
                float t = a / st.zone;
                w = 1f - t * t * (3f - 2f * t);
            }
            if (w <= 0f) return Vector3.zero;

            Vector3 q = x + st.noiseOffset;
            float l = Mathf.Sqrt(l2);
            float wob = noise.Fbm(q * 2.2f, 2);
            float le = l * st.invRadius / (1f + cfg.footprintIrregularity * wob);
            float g = Mathf.Exp(-Mathf.Pow(le, cfg.profileSharpness));
            float push = st.depth * g * w;
            float rim = 4f * g * (1f - g);

            // Radial buckles: triangle wave around the impact axis with a meandering phase.
            float theta = Mathf.Atan2(Vector3.Dot(lat, st.e2), Vector3.Dot(lat, st.e1)) * 0.1591549f; // turns
            float tb = theta * cfg.buckleLobes + st.phase + 0.35f * noise.Fbm(q * 1.6f, 2);
            float tri = 1f - 4f * Mathf.Abs(tb - Mathf.Floor(tb) - 0.5f);
            float buckleH = 0.6f + 0.4f * noise.Perlin(q * 3.1f);
            float buckle = cfg.buckleStrength * st.depth * rim * tri * buckleH * crumple;

            // Ridged creases on the rim and light wrinkling of the floor, along the surface normal.
            float crease = st.depth * crumple * (cfg.rimCrease * rim * noise.Ridged(q * 3.4f) + cfg.floorWrinkle * g * noise.Perlin(q * 5.3f));

            // Outward lip just past the dent edge.
            float dl = (le - 1.25f) / 0.25f;
            float lip = cfg.lip * st.depth * st.lipScale * Mathf.Exp(-dl * dl);

            Vector3 disp = st.d * (push + buckle * w - lip * w) + n * (crease * w);
            // Material is dragged in toward the dent instead of stretching over its wall.
            disp -= lat * (st.invRadius * cfg.drawIn * push);
            return disp;
        }
    }
}
