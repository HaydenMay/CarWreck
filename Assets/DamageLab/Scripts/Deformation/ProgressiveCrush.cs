using System;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Progressive crushing: how a new dent is SHOWN. The damage solve is unchanged and still runs once per impact; the
    /// displayed mesh then travels from what was on screen to the solved shape over a short time that grows with how far the
    /// metal moves. Metal near the struck surface starts first and the metal behind it follows (a stagger along the impact
    /// direction), so a nose crushes from the bumper back. Interpolation cannot add folds: the final shape is the solver's.
    /// </summary>
    [Serializable]
    public class ProgressiveCrushSettings
    {
        [Tooltip("Show each impact's dent as a short crush instead of in one frame. Display only: the damage solve, the logical deformed " +
                 "state (hitbox fitting, footprints) and physics are unchanged. Off = instant dents (Damage Lab, Collision Lab).")]
        public bool enabled;
        [Tooltip("Crush time (s) of a dent that barely moves the metal.")]
        public float minDuration = 0.06f;
        [Tooltip("Crush time (s) of the deepest dents (fullTravel and beyond).")]
        public float maxDuration = 0.24f;
        [Tooltip("Metal travel (m: the largest vertex movement of the hit) that takes maxDuration. In between the time grows linearly.")]
        public float fullTravel = 0.6f;
        [Tooltip("Fraction of the crush time by which the deepest metal (behind the struck surface, along the impact direction) starts " +
                 "after the surface. 0 = all the metal moves together. Above about 0.35 the leading metal can briefly fold through the metal " +
                 "behind it on repeated hits (synthetic-body test: 0.3 added no folds the final shape lacks, 0.5 did).")]
        [Range(0f, 0.5f)] public float stagger = 0.3f;

        /// <summary>Crush time (s) for a hit whose metal travels <paramref name="travel"/> m.</summary>
        public float DurationFor(float travel)
        {
            float k = fullTravel > 0f ? Mathf.Clamp01(travel / fullTravel) : 1f;
            return Mathf.Max(1e-3f, Mathf.Lerp(minDuration, maxDuration, k));
        }

        /// <summary>
        /// Displayed fraction (0..1) of a vertex's travel at ramp time <paramref name="t"/> (0..1) when it starts <paramref name="delay"/>
        /// (fraction of the ramp) late. Ease-out: the metal moves fastest at the start of its crush and settles into place, with zero
        /// speed at the end, so the last frame never jumps. Every vertex arrives at t = 1.
        /// </summary>
        public static float Ease(float t, float delay)
        {
            float u = Mathf.Clamp01((t - delay) / (1f - delay));
            return 1f - (1f - u) * (1f - u);
        }
    }
}
