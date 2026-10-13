using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Seeded 3D gradient (Perlin) noise. The same seed always produces the same field, so the
    /// damage pattern is identical every time the slider passes through a given value.
    /// </summary>
    public sealed class DeterministicNoise
    {
        readonly int[] perm = new int[512];

        public DeterministicNoise(int seed)
        {
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            var rng = new System.Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p[i], p[j]) = (p[j], p[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }

        /// <summary>Gradient noise, roughly in [-1, 1].</summary>
        public float Perlin(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            xi &= 255; yi &= 255; zi &= 255;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);
            int a = perm[xi] + yi, aa = perm[a] + zi, ab = perm[a + 1] + zi;
            int b = perm[xi + 1] + yi, ba = perm[b] + zi, bb = perm[b + 1] + zi;
            float x1 = Mathf.Lerp(Grad(perm[aa], xf, yf, zf), Grad(perm[ba], xf - 1, yf, zf), u);
            float x2 = Mathf.Lerp(Grad(perm[ab], xf, yf - 1, zf), Grad(perm[bb], xf - 1, yf - 1, zf), u);
            float y1 = Mathf.Lerp(x1, x2, v);
            x1 = Mathf.Lerp(Grad(perm[aa + 1], xf, yf, zf - 1), Grad(perm[ba + 1], xf - 1, yf, zf - 1), u);
            x2 = Mathf.Lerp(Grad(perm[ab + 1], xf, yf - 1, zf - 1), Grad(perm[bb + 1], xf - 1, yf - 1, zf - 1), u);
            float y2 = Mathf.Lerp(x1, x2, v);
            return Mathf.Lerp(y1, y2, w);
        }

        public float Perlin(Vector3 p) => Perlin(p.x, p.y, p.z);

        /// <summary>Fractal sum of Perlin octaves, roughly in [-1, 1].</summary>
        public float Fbm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(p) * amp;
                norm += amp;
                p = p * 2.03f + new Vector3(17.1f, 3.7f, 9.2f);
                amp *= 0.5f;
            }
            return sum / norm * 1.6f;
        }

        /// <summary>
        /// Ridged noise: thin sharp crests separated by broad valleys - the signature of creased metal.
        /// Returns roughly [-0.3, 0.7] (zero-mean-ish so it does not inflate the panel).
        /// </summary>
        public float Ridged(Vector3 p)
        {
            // Domain warp so creases meander instead of following the noise lattice.
            Vector3 warp = new Vector3(Perlin(p + new Vector3(5.2f, 1.3f, 7.7f)),
                                       Perlin(p + new Vector3(1.7f, 9.2f, 3.1f)),
                                       Perlin(p + new Vector3(8.3f, 2.8f, 4.6f))) * 0.6f;
            float n1 = 1f - Mathf.Abs(Perlin(p + warp));
            float n2 = 1f - Mathf.Abs(Perlin(p * 2.1f + warp + new Vector3(3.3f, 3.3f, 3.3f)));
            float r = n1 * n1 * n1 * 0.7f + n2 * n2 * n2 * 0.3f;
            return r - 0.3f;
        }
    }
}
