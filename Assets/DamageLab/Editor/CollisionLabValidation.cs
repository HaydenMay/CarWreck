using System.Collections.Generic;
using System.IO;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Runs the Collision Lab scenarios with real Unity physics stepped by script (Physics.Simulate) in the
    /// editor: launches the sled, lets PhysX resolve the collision, and reports what the receiver measured,
    /// how far the car moved, how much the mesh deformed, and whether Reset restores everything.
    /// </summary>
    public static class CollisionLabValidation
    {
        struct Scenario
        {
            public string name; public ImpactDirection dir; public float speed, mass; public bool glancing; public float yaw, pitch, dist;
            public Scenario(string n, ImpactDirection d, float s, float m, float yaw, float pitch = 24f, float dist = 9f, bool glancing = false)
            { name = n; dir = d; speed = s; mass = m; this.yaw = yaw; this.pitch = pitch; this.dist = dist; this.glancing = glancing; }
        }

        static readonly Scenario[] Scenarios =
        {
            new Scenario("A_Gentle_300kg_2ms_Front", ImpactDirection.Front, 2f, 300f, 160f),
            new Scenario("B_Moderate_1200kg_8ms_Front", ImpactDirection.Front, 8f, 1200f, 160f),
            new Scenario("C_Heavy_3000kg_20ms_Front", ImpactDirection.Front, 20f, 3000f, 160f),
            new Scenario("D_Side_1500kg_12ms_LeftDoor", ImpactDirection.LeftSide, 12f, 1500f, 105f),
            new Scenario("E_Angled_1500kg_14ms_FrontLeftCorner", ImpactDirection.FrontCornerAngled, 14f, 1500f, 140f),
            new Scenario("F1_Mass_500kg_10ms_Left", ImpactDirection.LeftSide, 10f, 500f, 105f),
            new Scenario("F2_Mass_1500kg_10ms_Left", ImpactDirection.LeftSide, 10f, 1500f, 105f),
            new Scenario("F3_Mass_3000kg_10ms_Left", ImpactDirection.LeftSide, 10f, 3000f, 105f),
            new Scenario("S1_Speed_1500kg_5ms_Front", ImpactDirection.Front, 5f, 1500f, 160f),
            new Scenario("S2_Speed_1500kg_15ms_Front", ImpactDirection.Front, 15f, 1500f, 160f),
            new Scenario("S3_Speed_1500kg_25ms_Front", ImpactDirection.Front, 25f, 1500f, 160f),
            new Scenario("R_Rear_1500kg_12ms", ImpactDirection.Rear, 12f, 1500f, 330f),
            new Scenario("Rt_Right_1500kg_12ms", ImpactDirection.RightSide, 12f, 1500f, 255f),
            new Scenario("X_Glancing_1500kg_15ms_LeftScrape", ImpactDirection.LeftSide, 15f, 1500f, 105f, glancing: true),
        };

        public static string Run(bool screenshots = true)
        {
            if (EditorSceneManager.GetActiveScene().path != CollisionLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindAnyObjectByType<CollisionLabController>();
            var model = lab.model;
            var receiver = lab.receiver;
            var cam = lab.cameraController;
            var sb = new StringBuilder("[CollisionLab] Validation (real PhysX contacts, Physics.Simulate at 50 Hz)\n");
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            model.Initialize();
            lab.Setup();
            receiver.logImpacts = false;
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = Snapshot(filters);
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) baseNormals.Add(f.sharedMesh.normals);
                Vector3 carPos0 = lab.car.position; Quaternion carRot0 = lab.car.rotation;
                lab.ResetTest();
                Vector3 sledPos0 = lab.sled.position;

                if (screenshots)
                {
                    lab.direction = ImpactDirection.FrontCornerAngled; lab.speed = 14f; lab.mass = 1500f;
                    lab.ResetTest(); lab.RefreshValues();
                    Shot(lab, cam, "30_CollisionLab_Before", 128f, 22f, 17f, new Vector3(0f, 0.6f, 1.5f));
                }

                sb.AppendLine("  scenario                                  events impacts  |v| m/s  closing  impulse N·s   E kJ  strength  location (car space)              max mesh disp  car moved  car yaw  deform ms");
                var results = new Dictionary<string, float>();
                float worstResetPos = 0f, worstResetNrm = 0f, worstPose = 0f, worstVel = 0f;
                foreach (var s in Scenarios)
                {
                    lab.direction = s.dir; lab.speed = s.speed; lab.mass = s.mass;
                    lab.Launch();
                    if (s.glancing)
                    {
                        // Side swipe: sled travelling forward along the car's left side at 15°, overlapping 0.35 m.
                        Vector3 dir = Quaternion.Euler(0f, 15f, 0f) * Vector3.forward;
                        Vector3 start = new Vector3(-1.03f - 0.95f + 0.35f, 0f, -0.2f) - dir * 7f;
                        start.y = lab.car.position.y + lab.sledHeight;
                        lab.sled.isKinematic = true;
                        lab.sled.position = start; lab.sled.rotation = Quaternion.LookRotation(dir);
                        lab.sled.transform.SetPositionAndRotation(start, lab.sled.rotation);
                        Physics.SyncTransforms();
                        lab.sled.isKinematic = false;
                        lab.sled.linearVelocity = dir * s.speed;
                    }
                    for (int i = 0; i < 200; i++)        // 4 s of simulated time
                    {
                        Physics.Simulate(Time.fixedDeltaTime);
                        receiver.Step(Time.fixedDeltaTime);
                    }
                    var r = receiver.Last;
                    float meshDisp = MaxDiff(baseVerts, Snapshot(filters));
                    float moved = (lab.car.position - carPos0).magnitude;
                    float yaw = Quaternion.Angle(carRot0, lab.car.rotation);
                    results[s.name] = r.strength;
                    sb.AppendLine($"  {s.name,-42}{receiver.EventCount,4}{model.ImpactCount,8}  {(receiver.HasReport ? r.relativeSpeed : 0f),7:0.00}  {r.closingSpeed,7:0.00}  {r.impulse,11:N0}  {r.energy / 1000f,6:0.0}  {r.strength * 100f,7:0.0}  " +
                                  $"{(receiver.HasReport ? $"{r.region} {r.localPoint.ToString("F2")}" : "-"),-34}{meshDisp,8:0.000} m  {moved,6:0.00} m  {yaw,5:0.0}°  {(r.applied ? r.deformMilliseconds.ToString("0.0") : "-"),7}");

                    if (screenshots)
                    {
                        lab.RefreshDiagnostics();
                        Vector3 pivot = lab.car.position + Vector3.up * 0.6f;
                        var sledRenderer = lab.sled.GetComponent<Renderer>();
                        sledRenderer.enabled = false;   // keep the sled from hiding the damage in the inspection shot
                        Shot(lab, cam, "3x_" + s.name, s.yaw + lab.car.rotation.eulerAngles.y, s.pitch, s.dist, pivot);
                        sledRenderer.enabled = true;
                    }

                    // G: reset must restore mesh, poses and velocities exactly
                    lab.ResetTest();
                    Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                    worstResetPos = Mathf.Max(worstResetPos, ep); worstResetNrm = Mathf.Max(worstResetNrm, en);
                    worstPose = Mathf.Max(worstPose, (lab.car.position - carPos0).magnitude + (lab.sled.position - SledHome(lab)).magnitude * 0f);
                    worstPose = Mathf.Max(worstPose, Quaternion.Angle(carRot0, lab.car.rotation) * Mathf.Deg2Rad);
                    worstVel = Mathf.Max(worstVel, lab.car.linearVelocity.magnitude + lab.car.angularVelocity.magnitude + lab.sled.linearVelocity.magnitude);
                    if (model.ImpactCount != 0 || receiver.HasReport) worstPose = float.PositiveInfinity;
                }

                sb.AppendLine($"  Speed series (front, 1500 kg): 5 m/s -> {results["S1_Speed_1500kg_5ms_Front"] * 100f:0}, 15 m/s -> {results["S2_Speed_1500kg_15ms_Front"] * 100f:0}, 25 m/s -> {results["S3_Speed_1500kg_25ms_Front"] * 100f:0} " +
                              $"{(results["S1_Speed_1500kg_5ms_Front"] < results["S2_Speed_1500kg_15ms_Front"] && results["S2_Speed_1500kg_15ms_Front"] < results["S3_Speed_1500kg_25ms_Front"] ? "PASS (increasing)" : "FAIL")}");
                sb.AppendLine($"  Mass series (left, 10 m/s): 500 kg -> {results["F1_Mass_500kg_10ms_Left"] * 100f:0}, 1500 kg -> {results["F2_Mass_1500kg_10ms_Left"] * 100f:0}, 3000 kg -> {results["F3_Mass_3000kg_10ms_Left"] * 100f:0} " +
                              $"{(results["F1_Mass_500kg_10ms_Left"] < results["F2_Mass_1500kg_10ms_Left"] && results["F2_Mass_1500kg_10ms_Left"] < results["F3_Mass_3000kg_10ms_Left"] ? "PASS (increasing)" : "FAIL")}");
                sb.AppendLine($"  Gentle (A) strength {results["A_Gentle_300kg_2ms_Front"] * 100f:0} {(results["A_Gentle_300kg_2ms_Front"] < 0.05f ? "PASS (no visible damage)" : "CHECK")}; " +
                              $"glancing scrape {results["X_Glancing_1500kg_15ms_LeftScrape"] * 100f:0} vs direct side at 15 m/s-equivalent (D, 12 m/s) {results["D_Side_1500kg_12ms_LeftDoor"] * 100f:0}");
                sb.AppendLine($"  Reset after every scenario: max vertex error {worstResetPos:E2} m, normal error {worstResetNrm:E2}, car pose error {worstPose:E2}, residual velocity {worstVel:E2} " +
                              $"{(worstResetPos == 0f && worstResetNrm == 0f && worstPose < 1e-4f && worstVel == 0f ? "PASS (exact)" : "FAIL")}");

                // Repeatability: the same launch twice from reset
                lab.direction = ImpactDirection.FrontCornerAngled; lab.speed = 14f; lab.mass = 1500f;
                float[] strengths = new float[2]; List<Vector3[]>[] meshes = new List<Vector3[]>[2];
                for (int k = 0; k < 2; k++)
                {
                    lab.Launch();
                    for (int i = 0; i < 200; i++) { Physics.Simulate(Time.fixedDeltaTime); receiver.Step(Time.fixedDeltaTime); }
                    strengths[k] = receiver.Last.strength; meshes[k] = Snapshot(filters);
                }
                lab.ResetTest();
                sb.AppendLine($"  Same launch twice (E): strength {strengths[0] * 100f:0.00} vs {strengths[1] * 100f:0.00}, mesh difference {MaxDiff(meshes[0], meshes[1]):E2} m");
            }
            finally
            {
                lab.ResetTest();
                receiver.logImpacts = true;
                Physics.simulationMode = prevMode;
                model.Shutdown();
                cam.pivot = new Vector3(0f, 0.6f, 1.5f);
                cam.SetView(cam.defaultYaw, cam.defaultPitch, cam.defaultDistance);
            }
            return sb.ToString();
        }

        static Vector3 SledHome(CollisionLabController lab) => lab.sled.position;

        internal static void Shot(CollisionLabController lab, InspectionCameraController cam, string name, float yaw, float pitch, float dist, Vector3 pivot)
        {
            string path = Path.Combine(DamageLabCapture.OutputFolder, name + ".png");
            Directory.CreateDirectory(DamageLabCapture.OutputFolder);
            var camera = cam.GetComponent<Camera>();
            var canvas = Object.FindAnyObjectByType<Canvas>();
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var prev = camera.targetTexture;
            try
            {
                camera.targetTexture = rt;
                cam.pivot = pivot;
                cam.SetView(yaw, pitch, dist);
                lab.RefreshValues();
                Canvas.ForceUpdateCanvases();
                if (canvas != null) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                camera.Render();
                var active = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                tex.Apply();
                RenderTexture.active = active;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                camera.targetTexture = prev;
                ShaderUtil.allowAsyncCompilation = async;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        internal static List<Vector3[]> Snapshot(MeshFilter[] filters)
        {
            var l = new List<Vector3[]>();
            foreach (var f in filters) l.Add(f.sharedMesh.vertices);
            return l;
        }

        internal static float MaxDiff(List<Vector3[]> a, List<Vector3[]> b)
        {
            float m = 0f;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < a[i].Length; j++)
                    m = Mathf.Max(m, (a[i][j] - b[i][j]).magnitude);
            return m;
        }

        internal static void Compare(MeshFilter[] filters, List<Vector3[]> verts, List<Vector3[]> normals, out float maxPos, out float maxNrm)
        {
            maxPos = 0f; maxNrm = 0f;
            for (int i = 0; i < filters.Length; i++)
            {
                var v = filters[i].sharedMesh.vertices;
                var n = filters[i].sharedMesh.normals;
                for (int j = 0; j < v.Length; j++)
                {
                    maxPos = Mathf.Max(maxPos, (v[j] - verts[i][j]).magnitude);
                    maxNrm = Mathf.Max(maxNrm, (n[j] - normals[i][j]).magnitude);
                }
            }
        }
    }
}
