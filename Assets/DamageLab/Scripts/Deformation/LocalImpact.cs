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
        [Tooltip("Summed impact displacement is linear up to the knee and then eases toward the cap.")]
        public float stackKnee = 0.40f;
        public float stackCap = 0.70f;
    }

    /// <summary>
    /// Localized crush for an arbitrary 3D impact direction. Same ideas as <see cref="CrushField"/>, but in
    /// a frame built from the click: depth a is measured along the ray, footprint distance l across it.
    ///   push    D·g(l)·w(a): super-gaussian footprint with an irregular outline, monotonic depth falloff
    ///           w = 1 - smoothstep(a / L), L ≥ 1.5·D, so the dent cannot fold through itself.
    ///   buckles a few radial triangle-wave folds on the dent wall (sheet metal gathers into creases
    ///           when it is pushed into a smaller area) plus ridged creases on the rim.
    ///   lip     a slight outward roll just outside the dent so the edge reads as bent metal.
    /// Several impacts are summed and the total is soft-limited, so stacked hits deepen without exploding.
    /// </summary>
    public sealed class ImpactField
    {
        struct State
        {
            public Vector3 p, d, e1, e2, noiseOffset;
            public float depth, radius, invRadius, zone, reach2, phase;
        }

        readonly ImpactSettings cfg;
        readonly DeterministicNoise noise = new DeterministicNoise(71);
        readonly List<State> states = new List<State>();

        public ImpactField(ImpactSettings settings) { cfg = settings ?? new ImpactSettings(); }

        public int Count => states.Count;

        public void Clear() => states.Clear();

        /// <summary>Add one impact; returns its index for <see cref="Contribution"/>.</summary>
        public int Add(LocalImpact im)
        {
            var st = new State();
            st.p = im.point;
            st.d = im.direction.normalized;
            float facing = Mathf.Clamp01(Vector3.Dot(-st.d, im.normal));
            float s = Mathf.Clamp01(im.strength);
            st.depth = cfg.maxDepth * Mathf.Pow(s, cfg.depthExponent) * Mathf.Lerp(cfg.grazingDepth, 1f, facing);
            st.radius = Mathf.Lerp(cfg.radiusMin, cfg.radiusMax, s);
            st.invRadius = 1f / st.radius;
            st.zone = 1.5f * st.depth + 0.12f;
            float r = 1.8f * st.radius;
            st.reach2 = r * r;
            Vector3 helper = Mathf.Abs(st.d.y) < 0.9f ? Vector3.up : Vector3.right;
            st.e1 = Vector3.Cross(helper, st.d).normalized;
            st.e2 = Vector3.Cross(st.d, st.e1);
            // Noise offset and buckle phase come from the hit position, so the same click always
            // produces the same dent regardless of the order impacts were added.
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

        /// <summary>Soft limit applied to the summed contributions of all impacts.</summary>
        public Vector3 Limit(Vector3 sum)
        {
            float m = sum.magnitude;
            if (m <= cfg.stackKnee) return sum;
            float span = Mathf.Max(1e-3f, cfg.stackCap - cfg.stackKnee);
            float limited = cfg.stackKnee + span * Tanh((m - cfg.stackKnee) / span);
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
            float lip = cfg.lip * st.depth * Mathf.Exp(-dl * dl);

            Vector3 disp = st.d * (push + buckle * w - lip * w) + n * (crease * w);
            // Material is dragged in toward the dent instead of stretching over its wall.
            disp -= lat * (st.invRadius * cfg.drawIn * push);
            return disp;
        }
    }
}
