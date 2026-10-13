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
        }

        readonly ImpactSettings cfg;
        readonly DeterministicNoise noise = new DeterministicNoise(71);
        readonly List<State> states = new List<State>();
        Vector3[] wheelCenters = new Vector3[0];
        float cabinStart = 1.05f;

        public ImpactField(ImpactSettings settings) { cfg = settings ?? new ImpactSettings(); }

        public int Count => states.Count;

        public void Clear() => states.Clear();

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
            float facing = Mathf.Clamp01(Vector3.Dot(-st.d, im.normal));
            float s = Mathf.Clamp01(im.strength);
            st.depth = cfg.maxDepth * Mathf.Pow(s, cfg.depthExponent) * Mathf.Lerp(cfg.grazingDepth, 1f, facing);
            // Earlier damage at this spot (sum of earlier footprints, weighted by their strength): the
            // centre is already near its limit, so the new hit spreads into the surrounding panels.
            float prior = 0f;
            foreach (var o in states)
            {
                Vector3 r0 = im.point - o.p;
                Vector3 lat0 = r0 - o.d * Vector3.Dot(r0, o.d);
                prior += o.depth / Mathf.Max(1e-3f, cfg.maxDepth) * Mathf.Exp(-lat0.sqrMagnitude * o.invRadius * o.invRadius);
            }
            // Work hardening: crushed metal absorbs less of a new hit, so repeats give diminishing depth.
            st.depth /= 1f + cfg.hardening * prior;
            st.lipScale = 1f / (1f + prior);
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
            st.noiseOffset = new Vector3(Hash(im.point.x * 13.1f + im.point.z * 3.7f), Hash(im.point.y * 17.3f + im.point.x * 5.1f), Hash(im.point.z * 11.7f + im.point.y * 7.9f)) * 40f;
            st.phase = Hash(im.point.x * 7.3f + im.point.y * 3.1f + im.point.z * 5.9f);
            states.Add(st);
            return states.Count - 1;
        }

        static float Hash(float x)
        {
            float h = Mathf.Sin(x * 12.9898f + 4.1414f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        /// <summary>Raw displacement (vehicle space) from impact <paramref name="index"/> of a vertex at original
        /// position x with smoothed normal n. Parts cache the sum of these, so a new click costs one impact.</summary>
        public Vector3 Contribution(int index, Vector3 x, Vector3 n, float crumple) => One(states[index], x, n, crumple);

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
