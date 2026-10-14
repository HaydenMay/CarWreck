using System;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>Optional extra randomization for random launches (approach angle and lateral offset are always random).</summary>
    [Serializable]
    public class RandomImpactOptions
    {
        [Tooltip("Pick the narrow block or the wide sled at random for each shot (off = the selected impactor).")]
        public bool varyWidth;
        [Tooltip("Scale each shot's speed by a random factor in [1 - this, 1] (0 = the selected speed).")]
        [Range(0f, 0.6f)] public float speedVariation;
        [Tooltip("Lateral offsets span this fraction of the body's width as seen from the approach direction.")]
        [Range(0.1f, 1f)] public float lateralSpread = 0.85f;
    }

    /// <summary>The parameters chosen for one random shot. A pure function of (seed, shot index, options).</summary>
    [Serializable]
    public struct RandomShot
    {
        public int seed, shot;
        /// <summary>Bearing (degrees, car space) the impactor comes FROM: 0 = in front of the car, 90 = its right side,
        /// 180 = behind, 270 = its left side. It travels towards the car.</summary>
        public float approachDeg;
        /// <summary>Lateral offset of the aim line in [-1, 1] of the body's half-width seen from the approach (+ = to the
        /// impactor's right).</summary>
        public float lateral;
        public bool narrow;
        public float speedScale;

        /// <summary>Car-space travel direction of the impactor (horizontal, unit).</summary>
        public Vector3 Direction
        {
            get { float a = approachDeg * Mathf.Deg2Rad; return new Vector3(-Mathf.Sin(a), 0f, -Mathf.Cos(a)); }
        }

        public override string ToString() =>
            $"seed {seed} shot {shot}: approach {approachDeg:0.0}°, lateral {lateral:+0.000;-0.000}, {(narrow ? "narrow" : "wide")}, speed ×{speedScale:0.00}";
    }

    /// <summary>
    /// Deterministic random impact parameters. Every value is a hash of (seed, shot, stream) through SplitMix64 in
    /// integer arithmetic, so a seed gives the same sequence on every platform and build, and shot k does not depend on
    /// what happened in shots 0..k-1 (physics can differ between runs; the selections cannot).
    /// </summary>
    public static class RandomImpact
    {
        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform value in [0, 1) for (seed, shot, stream).</summary>
        public static double Value(int seed, int shot, int stream)
        {
            ulong h = Mix(Mix(Mix((ulong)(uint)seed) ^ (ulong)(uint)shot) ^ (ulong)(uint)stream);
            return (h >> 11) * (1.0 / 9007199254740992.0);   // 53 bits
        }

        public static RandomShot Plan(int seed, int shot, RandomImpactOptions o, bool selectedNarrow)
        {
            o ??= new RandomImpactOptions();
            var s = new RandomShot { seed = seed, shot = shot };
            s.approachDeg = (float)(Value(seed, shot, 1) * 360.0);
            s.lateral = (float)(Value(seed, shot, 2) * 2.0 - 1.0) * Mathf.Clamp(o.lateralSpread, 0.1f, 1f);
            s.narrow = o.varyWidth ? Value(seed, shot, 3) < 0.5 : selectedNarrow;
            s.speedScale = 1f - Mathf.Clamp(o.speedVariation, 0f, 0.6f) * (float)Value(seed, shot, 4);
            return s;
        }

        /// <summary>A fresh seed (for the NEW SEED button): 1..999999.</summary>
        public static int NewSeed() => 1 + (int)(Mix((ulong)DateTime.UtcNow.Ticks) % 999999UL);
    }
}
