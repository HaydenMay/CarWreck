using System;
using System.IO;
using DamageLab.Deformation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DamageLab.EditorTools
{
    /// <summary>Creates (or refreshes) the lab's materials, UI sprites, procedural meshes and settings assets.</summary>
    public static class DamageLabAssets
    {
        public const string Root = "Assets/DamageLab";
        public const string MaterialFolder = Root + "/Materials";
        public const string SpriteFolder = Root + "/Art/UI";
        public const string MeshFolder = Root + "/Art/Meshes";
        public const string SettingsFolder = Root + "/Settings";
        public const string ModelPath = Root + "/Art/Models/MuscleCar.fbx";
        public const string StripeTexturePath = Root + "/Art/Textures/CarPaint_Stripes.png";
        public const string ProfilePath = SettingsFolder + "/MuscleCarDamageProfile.asset";
        public const string VolumePath = SettingsFolder + "/DamageLabVolumeProfile.asset";

        public static readonly string[] CarMaterialNames =
        {
            "Paint", "Chrome", "BlackTrim", "Glass", "Tire", "Rim", "Headlight", "Taillight", "Indicator", "Underbody", "Caliper", "DarkMetal",
        };

        public static void EnsureFolders()
        {
            foreach (var f in new[] { MaterialFolder, SpriteFolder, MeshFolder, SettingsFolder, Root + "/Scenes" })
                CreateFolderRecursive(f);
        }

        static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------------ materials

        static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

        public static Material LitMaterial(string name, Color color, float metallic, float smoothness,
                                           Color? emission = null, Texture baseMap = null, bool doubleSided = false)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Lit) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Lit;
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", baseMap);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.doubleSidedGI = doubleSided;
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        public static void CreateCarMaterials()
        {
            var stripes = AssetDatabase.LoadAssetAtPath<Texture2D>(StripeTexturePath);
            var ti = AssetImporter.GetAtPath(StripeTexturePath) as TextureImporter;
            if (ti != null && (ti.wrapMode != TextureWrapMode.Clamp || ti.anisoLevel != 8))
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            LitMaterial("Paint", Color.white, 0.35f, 0.86f, baseMap: stripes);
            LitMaterial("Chrome", new Color(0.88f, 0.89f, 0.9f), 1f, 0.93f);
            LitMaterial("BlackTrim", new Color(0.03f, 0.03f, 0.033f), 0f, 0.45f);
            LitMaterial("Glass", new Color(0.02f, 0.025f, 0.035f), 0.1f, 0.96f);
            LitMaterial("Tire", new Color(0.035f, 0.035f, 0.037f), 0f, 0.18f);
            LitMaterial("Rim", new Color(0.12f, 0.125f, 0.13f), 0.85f, 0.62f);
            LitMaterial("Headlight", new Color(1f, 0.97f, 0.88f), 0f, 0.9f, new Color(1f, 0.93f, 0.78f) * 1.6f);
            LitMaterial("Taillight", new Color(0.6f, 0.02f, 0.02f), 0f, 0.85f, new Color(1f, 0.06f, 0.04f) * 1.3f);
            LitMaterial("Indicator", new Color(1f, 0.4f, 0.05f), 0f, 0.8f, new Color(1f, 0.4f, 0.05f) * 0.9f);
            LitMaterial("Underbody", new Color(0.05f, 0.05f, 0.055f), 0.3f, 0.25f, doubleSided: true);
            LitMaterial("Caliper", new Color(0.75f, 0.06f, 0.04f), 0.1f, 0.55f);
            LitMaterial("DarkMetal", new Color(0.07f, 0.07f, 0.075f), 0.8f, 0.45f, doubleSided: true);
        }

        public static Material WireframeMaterial()
        {
            string path = $"{MaterialFolder}/Wireframe.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("DamageLab/WireframeLines")) { name = "Wireframe" };
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_Color", new Color(0.06f, 0.09f, 0.16f));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ settings assets

        public static VehicleDamageProfile DamageProfile(bool resetToDefaults)
        {
            var p = AssetDatabase.LoadAssetAtPath<VehicleDamageProfile>(ProfilePath);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VehicleDamageProfile>();
                p.ResetToDefaults();
                AssetDatabase.CreateAsset(p, ProfilePath);
            }
            else if (resetToDefaults)
            {
                p.ResetToDefaults();
                EditorUtility.SetDirty(p);
            }
            return p;
        }

        public static VolumeProfile LabVolumeProfile()
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);
            if (existing != null) AssetDatabase.DeleteAsset(VolumePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(6f);
            color.postExposure.Override(0.15f);

            foreach (var c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ------------------------------------------------------------------ procedural studio meshes

        /// <summary>A lathe-revolved mesh. Profile is (radius, height); normals face away from the axis/up for an outward profile.</summary>
        public static Mesh Lathe(string name, Vector2[] profile, int segments, bool flip)
        {
            int rings = profile.Length;
            var verts = new Vector3[rings * (segments + 1)];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i < rings; i++)
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    verts[i * (segments + 1) + s] = new Vector3(Mathf.Cos(a) * profile[i].x, profile[i].y, Mathf.Sin(a) * profile[i].x);
                    uvs[i * (segments + 1) + s] = new Vector2(s / (float)segments, i / (float)(rings - 1));
                }
            var tris = new int[(rings - 1) * segments * 6];
            int t = 0;
            for (int i = 0; i < rings - 1; i++)
                for (int s = 0; s < segments; s++)
                {
                    int a = i * (segments + 1) + s, b = a + 1, c = a + segments + 1, d = c + 1;
                    if (!flip) { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
                    else { tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = b; tris[t++] = d; tris[t++] = c; }
                }
            var m = new Mesh { name = name, indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static Mesh SaveMesh(Mesh mesh, string fileName)
        {
            string path = $"{MeshFolder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        /// <summary>Ensure a lathe mesh's first face points the requested way; flips winding if not.</summary>
        public static Mesh OrientedLathe(string name, Vector2[] profile, int segments, Vector3 probePoint, Vector3 desiredNormal)
        {
            var m = Lathe(name, profile, segments, false);
            var n = m.normals;
            var v = m.vertices;
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                float d = (v[i] - probePoint).sqrMagnitude;
                if (d < bestD) { bestD = d; best = i; }
            }
            if (Vector3.Dot(n[best], desiredNormal) < 0f) m = Lathe(name, profile, segments, true);
            return m;
        }

        // ------------------------------------------------------------------ UI sprites (signed-distance drawn)

        public static Sprite RoundedRectSprite => GetSprite("RoundedRect");
        public static Sprite GetSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFolder}/{name}.png");

        public static void CreateSprites()
        {
            const float AA = 1f;
            MakeSprite("RoundedRect", 64, p => Sd.RoundBox(p, new Vector2(32, 32), new Vector2(32, 32), 14), AA, new Vector4(18, 18, 18, 18));
            MakeSprite("RoundedRectSmall", 32, p => Sd.RoundBox(p, new Vector2(16, 16), new Vector2(16, 16), 6), AA, new Vector4(8, 8, 8, 8));
            MakeSprite("Circle", 64, p => Sd.Circle(p, new Vector2(32, 32), 31), AA, Vector4.zero);
            MakeSprite("ArrowLeft", 128, p => Sd.Chevron(p, new Vector2(70, 64), -1), AA, Vector4.zero);
            MakeSprite("ArrowRight", 128, p => Sd.Chevron(p, new Vector2(58, 64), 1), AA, Vector4.zero);
            MakeSprite("Check", 64, p => Mathf.Min(Sd.Segment(p, new Vector2(15, 33), new Vector2(27, 20), 4.5f), Sd.Segment(p, new Vector2(27, 20), new Vector2(50, 46), 4.5f)), AA, Vector4.zero);
            MakeSprite("IconReset", 128, p =>
            {
                float arc = Sd.Arc(p, new Vector2(64, 64), 38, 9, 40f, 330f);
                float head = Sd.Triangle(p, new Vector2(82, 96), new Vector2(112, 86), new Vector2(92, 62));
                return Mathf.Min(arc, head);
            }, AA, Vector4.zero);
            MakeSprite("IconCar", 128, p =>
            {
                float body = Sd.RoundBox(p, new Vector2(64, 60), new Vector2(54, 18), 9);
                float cab = Sd.RoundBox(p, new Vector2(60, 84), new Vector2(28, 14), 9);
                float shape = Mathf.Min(body, cab);
                float w1 = Sd.Circle(p, new Vector2(34, 40), 15);
                float w2 = Sd.Circle(p, new Vector2(94, 40), 15);
                shape = Mathf.Max(shape, -(Mathf.Min(w1, w2) - 4));
                return Mathf.Min(shape, Mathf.Min(w1 + 4, w2 + 4));
            }, AA, Vector4.zero);
            MakeSprite("IconOrbit", 64, p =>
            {
                float ring = Mathf.Abs(Sd.Ellipse(p, new Vector2(32, 32), new Vector2(26, 12))) - 2.5f;
                float dot = Sd.Circle(p, new Vector2(32, 32), 6);
                float tip = Sd.Triangle(p, new Vector2(50, 46), new Vector2(62, 40), new Vector2(52, 34));
                return Mathf.Min(ring, Mathf.Min(dot, tip));
            }, AA, Vector4.zero);
            MakeSprite("IconPan", 64, p =>
            {
                float d = Mathf.Min(Sd.Segment(p, new Vector2(10, 32), new Vector2(54, 32), 2.5f), Sd.Segment(p, new Vector2(32, 10), new Vector2(32, 54), 2.5f));
                d = Mathf.Min(d, Sd.Triangle(p, new Vector2(32, 62), new Vector2(24, 50), new Vector2(40, 50)));
                d = Mathf.Min(d, Sd.Triangle(p, new Vector2(32, 2), new Vector2(40, 14), new Vector2(24, 14)));
                d = Mathf.Min(d, Sd.Triangle(p, new Vector2(2, 32), new Vector2(14, 24), new Vector2(14, 40)));
                d = Mathf.Min(d, Sd.Triangle(p, new Vector2(62, 32), new Vector2(50, 40), new Vector2(50, 24)));
                return d;
            }, AA, Vector4.zero);
            MakeSprite("IconZoom", 64, p =>
            {
                float ring = Mathf.Abs(Sd.Circle(p, new Vector2(27, 37), 16)) - 3f;
                float handle = Sd.Segment(p, new Vector2(39, 25), new Vector2(54, 10), 4.5f);
                return Mathf.Min(ring, handle);
            }, AA, Vector4.zero);
            MakeSprite("IconTarget", 64, p =>
            {
                float ring = Mathf.Abs(Sd.Circle(p, new Vector2(32, 32), 18)) - 2.5f;
                float d = Mathf.Min(Sd.Segment(p, new Vector2(32, 4), new Vector2(32, 18), 2.5f), Sd.Segment(p, new Vector2(32, 46), new Vector2(32, 60), 2.5f));
                d = Mathf.Min(d, Mathf.Min(Sd.Segment(p, new Vector2(4, 32), new Vector2(18, 32), 2.5f), Sd.Segment(p, new Vector2(46, 32), new Vector2(60, 32), 2.5f)));
                return Mathf.Min(Mathf.Min(ring, d), Sd.Circle(p, new Vector2(32, 32), 4));
            }, AA, Vector4.zero);
            MakeSprite("IconWrench", 128, p =>
            {
                Vector2 a = new Vector2(38, 38), b = new Vector2(84, 84);
                float shaft = Sd.Segment(p, a, b, 10);
                float head = Sd.Circle(p, new Vector2(92, 92), 24);
                float slot = Sd.Segment(p, new Vector2(98, 98), new Vector2(118, 118), 11);
                float tail = Sd.Circle(p, a, 14);
                return Mathf.Max(Mathf.Min(Mathf.Min(shaft, head), tail), -slot);
            }, AA, Vector4.zero);
            MakeSprite("Glow", 64, p => Mathf.Clamp01((p - new Vector2(32, 32)).magnitude / 32f), 0f, Vector4.zero, radialAlpha: true);
            AssetDatabase.Refresh();
        }

        static void MakeSprite(string name, int size, Func<Vector2, float> sdf, float aa, Vector4 border, bool radialAlpha = false)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float a = radialAlpha ? Mathf.Pow(1f - sdf(p), 2f) : Mathf.Clamp01(0.5f - sdf(p) / Mathf.Max(aa, 0.0001f));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(px);
            string path = $"{SpriteFolder}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.spriteBorder = border;
            ti.spritePixelsPerUnit = 100;
            ti.SaveAndReimport();
        }

        /// <summary>2D signed distance helpers (pixel units, y up).</summary>
        static class Sd
        {
            public static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

            public static float RoundBox(Vector2 p, Vector2 c, Vector2 half, float r)
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - c.x), Mathf.Abs(p.y - c.y)) - half + new Vector2(r, r);
                return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
            }

            public static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
            {
                Vector2 pa = p - a, ba = b - a;
                float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
                return (pa - ba * h).magnitude - r;
            }

            public static float Ellipse(Vector2 p, Vector2 c, Vector2 r)
            {
                Vector2 d = new Vector2((p.x - c.x) / r.x, (p.y - c.y) / r.y);
                return (d.magnitude - 1f) * Mathf.Min(r.x, r.y);
            }

            public static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                float d = Mathf.Min(Segment(p, a, b, 0), Mathf.Min(Segment(p, b, c, 0), Segment(p, c, a, 0)));
                float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
                bool inside = (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
                return inside ? -d : d;
            }

            static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

            public static float Chevron(Vector2 p, Vector2 c, int dir)
            {
                Vector2 tip = c + new Vector2(22 * dir, 0);
                Vector2 top = c + new Vector2(-18 * dir, 38);
                Vector2 bot = c + new Vector2(-18 * dir, -38);
                return Mathf.Min(Segment(p, top, tip, 10), Segment(p, bot, tip, 10));
            }

            /// <summary>Ring arc between two angles (degrees, CCW from +x).</summary>
            public static float Arc(Vector2 p, Vector2 c, float r, float thickness, float a0, float a1)
            {
                Vector2 d = p - c;
                float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                if (ang < 0) ang += 360f;
                float ring = Mathf.Abs(d.magnitude - r) - thickness * 0.5f;
                if (ang >= a0 && ang <= a1) return ring;
                Vector2 e0 = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
                Vector2 e1 = c + new Vector2(Mathf.Cos(a1 * Mathf.Deg2Rad), Mathf.Sin(a1 * Mathf.Deg2Rad)) * r;
                return Mathf.Min((p - e0).magnitude, (p - e1).magnitude) - thickness * 0.5f;
            }
        }
    }
}
