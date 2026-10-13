using System.IO;
using DamageLab.Inspection;
using DamageLab.UI;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Renders the comparison screenshots (same camera for every state) straight from the scene camera,
    /// in edit mode, into &lt;project&gt;/Screenshots.
    /// </summary>
    public static class DamageLabCapture
    {
        public const string OutputFolder = "Screenshots";

        public struct Shot
        {
            public string name;
            public float front, rear, left, right;
            public Shot(string n, float f, float r, float l, float rt) { name = n; front = f; rear = r; left = l; right = rt; }
        }

        public static readonly Shot[] ComparisonSet =
        {
            new Shot("01_Damage_0", 0, 0, 0, 0),
            new Shot("02_Left_50", 0, 0, 50, 0),
            new Shot("03_Left_100", 0, 0, 100, 0),
            new Shot("04_Front_100", 100, 0, 0, 0),
            new Shot("05_Front80_Left60", 80, 0, 60, 0),
        };

        [MenuItem("Damage Lab/Capture Comparison Screenshots", priority = 20)]
        public static void CaptureComparisonMenu()
        {
            CaptureSet(ComparisonSet, 145f, 14f, 7.6f, true, "");
            EditorUtility.RevealInFinder(Path.GetFullPath(OutputFolder));
        }

        /// <summary>Capture a list of damage states from one camera view.</summary>
        public static void CaptureSet(Shot[] shots, float yaw, float pitch, float distance, bool withUI, string suffix, int width = 1920, int height = 1080, bool wireframe = false)
        {
            var ctx = Open();
            ctx.model.Initialize();
            ctx.model.SetWireframe(wireframe);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                ctx.cam.SetView(yaw, pitch, distance);
                // Warm-up render: compiles shader variants and settles UI layout before the real shots.
                Render(ctx, Path.Combine(OutputFolder, "_warmup.png"), withUI, width, height, yaw, pitch, distance);
                File.Delete(Path.Combine(OutputFolder, "_warmup.png"));
                foreach (var s in shots)
                {
                    ctx.model.SetDamageImmediate(s.front, s.rear, s.left, s.right);
                    if (ctx.ui != null) ctx.ui.SyncAll();
                    Render(ctx, Path.Combine(OutputFolder, s.name + suffix + ".png"), withUI, width, height, yaw, pitch, distance);
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                ctx.model.SetDamageImmediate(0, 0, 0, 0);
                ctx.model.SetWireframe(false);
                if (ctx.ui != null) ctx.ui.SyncAll();
                ctx.model.Shutdown();
                ctx.cam.SetView(ctx.cam.defaultYaw, ctx.cam.defaultPitch, ctx.cam.defaultDistance);
            }
        }

        /// <summary>A click from a camera view: the ray goes from the camera through a point given as a
        /// fraction of the body bounds (vehicle space), exactly like clicking that pixel.</summary>
        public struct Aim
        {
            public float yaw, pitch, distance, strength;
            public Vector3 target;
            public Aim(float yaw, float pitch, float distance, Vector3 target, float strength)
            { this.yaw = yaw; this.pitch = pitch; this.distance = distance; this.target = target; this.strength = strength; }
        }

        public struct ImpactShot
        {
            public string name;
            public float front, rear, left, right;
            public Aim[] aims;
            public float yaw, pitch, distance;
            public bool wireframe;
        }

        static Aim Door(float s) => new Aim(90, 6, 6.5f, new Vector3(-1f, 0.05f, -0.08f), s);
        static Aim Hood(float s) => new Aim(175, 74, 6.5f, new Vector3(0.38f, 0.3f, 0.5f), s);
        static Aim Grille(float s) => new Aim(180, 4, 6.5f, new Vector3(0f, -0.15f, 1f), s);
        static Aim FrontLeftCorner(float s) => new Aim(140, 28, 6.5f, new Vector3(-0.85f, 0.1f, 0.92f), s);
        static Aim Roof(float s) => new Aim(150, 80, 6.5f, new Vector3(0f, 0.6f, -0.12f), s);

        public static readonly ImpactShot[] ImpactSet =
        {
            new ImpactShot { name = "11_Impact_Hood_Above", aims = new[] { Hood(70) }, yaw = 150, pitch = 32, distance = 6.8f },
            new ImpactShot { name = "12_Impact_Door_Side", aims = new[] { Door(60) }, yaw = 115, pitch = 12, distance = 6.8f },
            new ImpactShot { name = "13_Impact_Grille_Front", aims = new[] { Grille(70) }, yaw = 155, pitch = 14, distance = 6.8f },
            new ImpactShot { name = "14_Impact_FrontCorner_Severe", aims = new[] { FrontLeftCorner(100) }, yaw = 158, pitch = 24, distance = 8.4f },
            new ImpactShot { name = "15_Impacts_Three", aims = new[] { Door(40), FrontLeftCorner(75), Hood(60) }, yaw = 132, pitch = 28, distance = 7.4f },
            new ImpactShot { name = "16_Sliders_F30_L50_plus_Roof70", front = 30, left = 50, aims = new[] { Roof(70) }, yaw = 145, pitch = 24, distance = 7.6f },
            new ImpactShot { name = "17_Impacts_Three_Wireframe", aims = new[] { Door(40), FrontLeftCorner(75), Hood(60) }, yaw = 132, pitch = 28, distance = 7.4f, wireframe = true },
        };

        public static void CaptureImpactSet(bool withUI = true)
        {
            foreach (var s in ImpactSet) CaptureImpactShot(s, withUI);
        }

        static Aim WheelArch(float s) => new Aim(100, 37, 6.5f, new Vector3(-1f, 0.4f, 0.6f), s);
        static Aim Windshield(float s) => new Aim(180, 37, 6.5f, new Vector3(0f, 1.4f, 0.25f), s);
        static Aim[] Repeat(Aim a, int n) { var r = new Aim[n]; for (int i = 0; i < n; i++) r[i] = a; return r; }

        /// <summary>Saturation stress shots: the same click repeated 1 / 3 / 5 times, plus other locations.</summary>
        public static readonly ImpactShot[] StressSet =
        {
            new ImpactShot { name = "21_Stress_Door_x1", aims = Repeat(Door(100), 1), yaw = 115, pitch = 12, distance = 6.8f },
            new ImpactShot { name = "22_Stress_Door_x3", aims = Repeat(Door(100), 3), yaw = 115, pitch = 12, distance = 6.8f },
            new ImpactShot { name = "23_Stress_Door_x5", aims = Repeat(Door(100), 5), yaw = 115, pitch = 12, distance = 6.8f },
            new ImpactShot { name = "24_Stress_Hood_x5", aims = Repeat(Hood(100), 5), yaw = 150, pitch = 32, distance = 6.8f },
            new ImpactShot { name = "25_Stress_WheelArch_x5", aims = Repeat(WheelArch(100), 5), yaw = 120, pitch = 14, distance = 6.8f },
            new ImpactShot { name = "26_Stress_Roof_x5", aims = Repeat(Roof(100), 5), yaw = 145, pitch = 24, distance = 7.2f },
            new ImpactShot { name = "27_Stress_Windshield_x3", aims = Repeat(Windshield(100), 3), yaw = 160, pitch = 22, distance = 7.2f },
            new ImpactShot { name = "28_Moderate_Door40_Corner55_Hood70", aims = new[] { Door(40), FrontLeftCorner(55), Hood(70) }, yaw = 132, pitch = 28, distance = 7.4f },
        };

        public static void CaptureStressSet(bool withUI = true)
        {
            foreach (var s in StressSet) CaptureImpactShot(s, withUI);
        }

        /// <summary>Apply the shot's slider values, fire its clicks through the real picking path, render.</summary>
        public static void CaptureImpactShot(ImpactShot s, bool withUI, int width = 1920, int height = 1080)
        {
            var ctx = Open();
            ctx.model.Initialize();
            var picker = ctx.cam.GetComponent<ImpactPicker>();
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                ctx.model.ClearImpacts();
                ctx.model.SetWireframe(s.wireframe);
                ctx.model.SetDamageImmediate(s.front, s.rear, s.left, s.right);
                Bounds b = ctx.model.BodyBounds;
                Debug.Log($"[DamageLab] Body bounds (vehicle space) center {b.center} extents {b.extents}");
                foreach (var aim in s.aims)
                {
                    ctx.cam.SetView(aim.yaw, aim.pitch, aim.distance);
                    Vector3 origin = ctx.cam.transform.position;
                    Vector3 target = ctx.model.transform.TransformPoint(b.center + Vector3.Scale(b.extents, aim.target));
                    bool ok = ctx.model.TryApplyImpact(new Ray(origin, (target - origin).normalized), aim.strength / 100f, out string msg);
                    var im = ok ? ctx.model.Impacts[ctx.model.ImpactCount - 1] : default;
                    Debug.Log($"[DamageLab] {s.name}: click yaw {aim.yaw} pitch {aim.pitch} strength {aim.strength} -> {(ok ? "OK" : "MISS")} {msg}" +
                              (ok ? $" at {im.point.ToString("F2")} dir {im.direction.ToString("F2")}" : ""));
                }
                if (picker != null) { picker.clickToDamage = true; picker.strength = s.aims.Length > 0 ? s.aims[s.aims.Length - 1].strength : 50; }
                if (ctx.ui != null) ctx.ui.SyncAll();
                ctx.cam.SetView(s.yaw, s.pitch, s.distance);
                Render(ctx, Path.Combine(OutputFolder, "_warmup.png"), withUI, width, height, s.yaw, s.pitch, s.distance);
                File.Delete(Path.Combine(OutputFolder, "_warmup.png"));
                Render(ctx, Path.Combine(OutputFolder, s.name + ".png"), withUI, width, height, s.yaw, s.pitch, s.distance);
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
                ctx.model.ClearImpacts();
                ctx.model.SetDamageImmediate(0, 0, 0, 0);
                ctx.model.SetWireframe(false);
                if (picker != null) { picker.clickToDamage = false; picker.strength = 50; }
                if (ctx.ui != null) ctx.ui.SyncAll();
                ctx.model.Shutdown();
                ctx.cam.SetView(ctx.cam.defaultYaw, ctx.cam.defaultPitch, ctx.cam.defaultDistance);
            }
        }

        struct Context
        {
            public VehicleDamageModel model;
            public InspectionCameraController cam;
            public DamageLabUI ui;
            public Canvas canvas;
        }

        static Context Open()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.path != DamageLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(DamageLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var ctx = new Context
            {
                model = Object.FindAnyObjectByType<VehicleDamageModel>(),
                cam = Object.FindAnyObjectByType<InspectionCameraController>(),
                ui = Object.FindAnyObjectByType<DamageLabUI>(),
            };
            if (ctx.ui != null) ctx.canvas = ctx.ui.GetComponent<Canvas>();
            if (ctx.model == null || ctx.cam == null) throw new System.Exception("VehicleDamageLab scene is missing the model or camera.");
            return ctx;
        }

        static void Render(Context ctx, string path, bool withUI, int width, int height, float yaw, float pitch, float distance)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var camera = ctx.cam.GetComponent<Camera>();
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            rt.Create();
            bool canvasWasOn = ctx.canvas != null && ctx.canvas.enabled;
            var prevTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = rt;
                // Re-apply the view now that the aspect ratio is the render target's.
                ctx.cam.SetView(yaw, pitch, distance);
                if (ctx.canvas != null)
                {
                    ctx.canvas.enabled = withUI;
                    if (withUI)
                    {
                        Canvas.ForceUpdateCanvases();
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)ctx.canvas.transform);
                        foreach (var vc in ctx.canvas.GetComponentsInChildren<ViewportCenter>()) vc.Apply();
                        Canvas.ForceUpdateCanvases();
                    }
                }
                camera.Render();
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log("[DamageLab] Saved " + Path.GetFullPath(path));
            }
            finally
            {
                camera.targetTexture = prevTarget;
                if (ctx.canvas != null) ctx.canvas.enabled = canvasWasOn;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

    }
}
