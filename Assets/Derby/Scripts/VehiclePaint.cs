using System.Collections.Generic;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>The selectable paint colours (and the tone-on-tone stripe mask the muscle car needs). One asset, no per-vehicle data.</summary>
    [CreateAssetMenu(menuName = "CarWreck/Paint Palette", fileName = "PaintPalette")]
    public class PaintPalette : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string name;
            [Tooltip("Body colour (sRGB).")] public Color body;
            [Tooltip("Stripe colour of the two-colour paint (hatchback, pickup). The muscle car's stripes are always a darker tone of the body colour.")] public Color stripe;
        }

        public Entry[] colors;
        [Tooltip("White body, dark stripes at the same places as the muscle car's original livery. Multiplied by the body colour.")]
        public Texture2D muscleStripeMask;
        [Tooltip("Paint of a wrecked car (a burnt-out hulk).")]
        public Entry wreck = new Entry { name = "Wreck", body = new Color(0.12f, 0.115f, 0.11f), stripe = new Color(0.2f, 0.19f, 0.18f) };

        public int Count => colors != null ? colors.Length : 0;
        public Entry Get(int i) => colors[Mathf.Clamp(i, 0, Mathf.Max(0, colors.Length - 1))];
    }

    /// <summary>
    /// Re-paints one vehicle instance. Only the body paint material of a car (a material called "...Paint") is swapped; windows, tyres, lights, trim and chrome
    /// keep their shared materials. The replacement is a copy of the model's own paint material with another colour, cached per (source material, colour)
    /// and shared by every car that uses that colour: five cars cost at most a handful of extra materials, there is no per-instance copy, and nothing is created
    /// per hit or per respawn. Swapping a renderer's material does not touch its mesh, so deformation, progressive crushing and colliders are unaffected.
    ///
    /// Two paint styles exist in the project: the hatchback and pickup use a two-colour shader (_PrimaryColor + _StripeColor + a stripe mask on UV1), so both
    /// colours are set and the livery is kept. The muscle car's paint is a plain URP Lit material whose base map is a blue/white stripe texture, which would tint
    /// the wrong way, so its copy gets a neutral stripe mask instead (white body, dark stripes), multiplied by the body colour.
    /// </summary>
    public static class VehiclePainter
    {
        static readonly Dictionary<(Material, Color32, Color32), Material> cache = new Dictionary<(Material, Color32, Color32), Material>();
        static readonly List<Material> scratch = new List<Material>(8);
        static readonly int PrimaryColor = Shader.PropertyToID("_PrimaryColor"), StripeColor = Shader.PropertyToID("_StripeColor"),
                            BaseColor = Shader.PropertyToID("_BaseColor"), BaseMap = Shader.PropertyToID("_BaseMap"), MainTex = Shader.PropertyToID("_MainTex"), Color_ = Shader.PropertyToID("_Color");

        public static int CachedMaterials => cache.Count;

        public static bool IsPaint(Material m) => m != null && m.name.EndsWith("Paint") && (m.HasProperty(PrimaryColor) || m.HasProperty(BaseColor));

        /// <summary>Paints every body-paint slot of the car. Returns how many material slots were changed.</summary>
        public static int Apply(GameObject car, PaintPalette.Entry color, PaintPalette palette)
        {
            int changed = 0;
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>(true))
            {
                r.GetSharedMaterials(scratch);
                bool any = false;
                for (int i = 0; i < scratch.Count; i++)
                {
                    var src = Source(scratch[i]);
                    if (!IsPaint(src)) continue;
                    scratch[i] = Variant(src, color, palette);
                    any = true; changed++;
                }
                if (any) r.SetSharedMaterials(scratch);
            }
            return changed;
        }

        // A car that was painted before holds a cached variant; the original is what the variant was copied from.
        static readonly Dictionary<Material, Material> originals = new Dictionary<Material, Material>();
        static Material Source(Material m)
        {
            if (m == null) return null;
            return originals.TryGetValue(m, out var o) ? o : m;
        }

        static Material Variant(Material src, PaintPalette.Entry c, PaintPalette palette)
        {
            var key = (src, (Color32)c.body, (Color32)c.stripe);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + " (" + c.name + ")" };
            if (m.HasProperty(PrimaryColor))
            {
                m.SetColor(PrimaryColor, c.body);
                if (m.HasProperty(StripeColor)) m.SetColor(StripeColor, c.stripe);
            }
            else
            {
                m.SetColor(BaseColor, c.body);
                if (m.HasProperty(Color_)) m.SetColor(Color_, c.body);
                if (palette != null && palette.muscleStripeMask != null)
                {
                    m.SetTexture(BaseMap, palette.muscleStripeMask);
                    if (m.HasProperty(MainTex)) m.SetTexture(MainTex, palette.muscleStripeMask);
                }
            }
            cache[key] = m;
            originals[m] = src;
            return m;
        }

        /// <summary>Destroys the cached colour variants (the scene is going away). Cars must not be painted afterwards without being rebuilt.</summary>
        public static void ClearCache()
        {
            foreach (var m in cache.Values) if (m != null) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }
            cache.Clear();
            originals.Clear();
        }
    }
}
