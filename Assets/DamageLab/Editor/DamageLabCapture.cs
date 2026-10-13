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
