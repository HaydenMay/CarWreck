using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Per-vertex constants for one impact direction. They depend only on the ORIGINAL mesh, so they
    /// are computed once at start-up; at runtime the slider only scales/shapes them. This is what makes
    /// the deformation deterministic, order-independent and exactly reversible.
    /// </summary>
    public struct CrushSample
    {
        public float s;       // depth measured inward from the impacted face (m)
        public float u;       // position along the face (m)
        public float v;       // height (m)
        public float fold;    // accordion fold phase, sin(2π(s+φ)/λ) in [-1, 1]
        public float ridge;   // ridged crease noise, ~[-0.3, 0.7]
        public float wobble;  // low-frequency footprint irregularity, ~[-1, 1]
    }

    /// <summary>Axes for one impact direction in vehicle space.</summary>
    public readonly struct ImpactFrame
    {
        public readonly Vector3 outward;   // points out of the impacted face
        public readonly Vector3 inward;    // direction the face is pushed
        public readonly Vector3 uAxis;     // along the face
        public readonly Vector3 vAxis;     // up
        public readonly float outerExtent; // position of the face plane along 'outward'

        public ImpactFrame(DamageDirection d, Bounds vehicleBounds)
        {
            vAxis = Vector3.up;
            switch (d)
            {
                case DamageDirection.Front: outward = Vector3.forward; uAxis = Vector3.right; break;
                case DamageDirection.Rear: outward = Vector3.back; uAxis = Vector3.right; break;
                case DamageDirection.Left: outward = Vector3.left; uAxis = Vector3.forward; break;
                default: outward = Vector3.right; uAxis = Vector3.forward; break;
            }
            inward = -outward;
            Vector3 ext = vehicleBounds.extents;
            outerExtent = Vector3.Dot(vehicleBounds.center, outward) + Mathf.Abs(Vector3.Dot(ext, outward));
        }

        public float Depth(Vector3 p) => outerExtent - Vector3.Dot(p, outward);
    }

    /// <summary>Values derived once per update from the slider amount for one direction.</summary>
    public struct RegionState
    {
        public bool active;
        public float depth;        // D: crush depth at the impact centre
        public float invZone;      // 1 / crumple zone length
        public float ru, rv, rvUp; // current footprint radii
        public float foldRamp;     // fades folds in over the first part of the slider
        public float invRoofZone;  // 1 / reach of the roof sag
        public float reach;        // vertices deeper than this are untouched
    }

    /// <summary>
    /// The procedural crush model for one direction.
    ///
    /// For a vertex at depth s behind the impacted face and footprint position (u, v):
    ///   1. Footprint   P(u,v) = base + (1-base)·exp(-q^k), q = elliptical distance to the impact centre.
    ///                  Radii grow with damage so the dent spreads; low-frequency noise breaks symmetry.
    ///   2. Crush       the face is pushed in by δ = D·P. Material behind it is pushed less, following
    ///                  w(s) = 1 - smoothstep(s / L). Because L ≥ 1.5·D the map s → s + δ·w(s) is monotonic,
    ///                  so the crush never folds the mesh through itself along the impact axis.
    ///   3. Folds       where the zone is compressed (strain c = -δ·w'(s)) the panel must buckle to keep its
    ///                  length: a sine of wavelength λ and amplitude λ·√c/π (arc-length conservation),
    ///                  applied along the smoothed surface normal. This gives accordion hoods and buckled
    ///                  roofs/fenders rather than a squeezed-balloon look.
    ///   4. Creases     ridged noise concentrated on the dent rim (4g(1-g)) gives sharp crease lines; a
    ///                  weaker wrinkle term roughens the dent floor.
    ///   5. Structure   surrounding panels are drawn toward the dent, the roof sags near heavy impacts.
    /// Everything is a pure function of the original vertex and the slider value.
    /// </summary>
    public sealed class CrushField
    {
        public readonly DamageRegion region;
        public readonly ImpactFrame frame;
        readonly DeterministicNoise noise;

        public CrushField(DamageRegion region, Bounds vehicleBounds)
        {
            this.region = region;
            frame = new ImpactFrame(region.direction, vehicleBounds);
            noise = new DeterministicNoise(region.seed);
        }

        /// <summary>Precompute the per-vertex constants. Thread-safe (read-only noise tables).</summary>
        public CrushSample Sample(Vector3 p)
        {
            float lambda = Mathf.Max(0.02f, region.foldWavelength);
            CrushSample cs;
            cs.s = frame.Depth(p);
            cs.u = Vector3.Dot(p, frame.uAxis);
            cs.v = Vector3.Dot(p, frame.vAxis);
            // Fold phase meanders across the panel so ridges are not perfectly straight / periodic,
            // and the ridge height varies along its length.
            float phase = 0.45f * lambda * noise.Fbm(p * 1.4f + new Vector3(3.1f, 7.3f, 1.9f), 2);
            float heightVar = 1f - region.foldVariation * 0.5f * (1f + noise.Perlin(p * 1.7f + new Vector3(9.4f, 4.4f, 0.7f)));
            // Mostly a triangle wave: buckled sheet metal forms flat facets meeting at sharp crests,
            // rather than the rounded ripples of a sine (which reads as cloth or plastic).
            float x = 2f * Mathf.PI * (cs.s + phase) / lambda;
            float sine = Mathf.Sin(x);
            float tri = Mathf.Asin(Mathf.Clamp(sine, -1f, 1f)) * 0.63662f; // 2/p � asin(sin x)
            cs.fold = (0.75f * tri + 0.25f * sine) * heightVar;
            // Creases are stretched along the face (u) so they run like real body creases:
            // horizontal on the doors, across the hood for front/rear hits.
            Vector3 q = p * region.creaseFrequency;
            q += frame.uAxis * (Vector3.Dot(q, frame.uAxis) * (0.45f - 1f));
            q += frame.vAxis * (Vector3.Dot(q, frame.vAxis) * (1.35f - 1f));
            cs.ridge = noise.Ridged(q);
            cs.wobble = noise.Fbm(p * 1.1f + new Vector3(11.3f, 2.2f, 5.4f), 2);
            return cs;
        }

        public RegionState Evaluate(float amount01)
        {
            var r = region;
            RegionState st = default;
            float a = Mathf.Clamp01(amount01);
            if (a <= 0f) return st;
            st.active = true;
            st.depth = r.maxCrushDepth * Mathf.Pow(a, Mathf.Max(0.1f, r.damageExponent));
            float zone = Mathf.Max(1.5f, r.zoneLengthScale) * st.depth + Mathf.Max(0.01f, r.zoneLengthMin);
            st.invZone = 1f / zone;
            st.ru = Mathf.Max(0.05f, Mathf.Lerp(r.radiusAtLowDamage.x, r.radiusAtFullDamage.x, a));
            st.rv = Mathf.Max(0.05f, Mathf.Lerp(r.radiusAtLowDamage.y, r.radiusAtFullDamage.y, a));
            st.rvUp = st.rv * Mathf.Max(0.05f, r.upperRadiusScale);
            st.foldRamp = Smooth01(a / 0.35f);
            float roofZone = 1.5f * zone + 0.6f;
            st.invRoofZone = 1f / roofZone;
            st.reach = Mathf.Max(zone, r.roofSag > 0f ? roofZone : 0f);
            return st;
        }

        static float Smooth01(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>Displacement (vehicle space) of one vertex. <paramref name="n"/> is the smoothed original normal.</summary>
        public Vector3 Displacement(in CrushSample cs, Vector3 n, in RegionState st, float crumple, float roofStart, float roofEnd)
        {
            if (cs.s > st.reach) return Vector3.zero;
            var r = region;

            // 1. footprint
            float du = (cs.u - r.impactCenter.x) / st.ru;
            float dvRaw = cs.v - r.impactCenter.y;
            float dv = dvRaw / (dvRaw > 0f ? st.rvUp : st.rv);
            float g = Mathf.Exp(-Mathf.Pow(du * du, r.profileSharpness) - Mathf.Pow(Mathf.Abs(dv), r.verticalShape));
            float footprint = (r.baseCoverage + (1f - r.baseCoverage) * g) * (1f + r.profileIrregularity * cs.wobble);
            float delta = st.depth * footprint;

            // 2. crush with depth falloff
            float t = cs.s * st.invZone;
            float w, dw;
            if (t <= 0f) { w = 1f; dw = 0f; }
            else if (t >= 1f) { w = 0f; dw = 0f; }
            else { w = 1f - t * t * (3f - 2f * t); dw = 6f * t * (1f - t); }
            float push = delta * w;

            // 3. folds from compression strain (arc-length conservation: A = λ·√c / π). Only the strain
            //    beyond the elastic limit buckles, and only panels lying along the impact axis fold
            //    (a grille facing the impactor is pushed back, it does not ripple).
            float strain = Mathf.Max(0f, delta * dw * st.invZone - r.elasticStrain);
            float along = Vector3.Dot(n, frame.outward);
            float foldAmp = r.foldStrength * r.foldWavelength * 0.3183099f * Mathf.Sqrt(strain) * st.foldRamp * crumple * (1f - along * along);

            // 4. creases on the impacted skin, strongest on the dent rim
            float surface = t <= 0f ? 1f : 1f - Smooth01(t / 0.45f);
            float rim = 4f * g * (1f - g);
            float creases = st.depth * crumple * surface * (r.creaseStrength * rim + r.wrinkleStrength * g) * cs.ridge;

            float normalOffset = foldAmp * (cs.fold + r.outwardBuckle) + creases;
            Vector3 d = frame.inward * push + n * normalOffset;

            // 5a. surrounding metal is drawn toward the dent
            d += frame.uAxis * (-(cs.u - r.impactCenter.x) / st.ru * r.drawIn * push);

            // 5b. roof sag near heavy impacts
            if (r.roofSag > 0f && cs.v > roofStart)
            {
                float ts = cs.s * st.invRoofZone;
                if (ts < 1f)
                {
                    float ws = 1f - ts * ts * (3f - 2f * ts);
                    float h = Smooth01((cs.v - roofStart) / Mathf.Max(0.01f, roofEnd - roofStart));
                    d -= frame.vAxis * (r.roofSag * st.depth * ws * h * (0.5f + 0.5f * footprint));
                }
            }
            return d;
        }
    }
}
