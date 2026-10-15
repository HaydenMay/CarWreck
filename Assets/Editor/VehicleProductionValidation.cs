using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DamageLab.EditorTools
{
    /// <summary>Editor-only observations for a manifest vehicle. Does not alter runtime code, tuning or source assets.
    /// Unity -executeMethod DamageLab.EditorTools.VehicleProductionValidation.Run -vehicleIndex 2 -evidenceDir ...</summary>
    public static class VehicleProductionValidation
    {
        [Serializable] public class PartResult { public string name; public int triangles, folded; public float maxDisplacement; }
        [Serializable] public class State { public string name; public bool applied, uvUnchanged; public PartResult[] parts; }
        [Serializable] public class MeshDump { public string name; public Vector3[] original, deformed; public int[] triangles, foldedTriangleStarts; }
        [Serializable] public class Report
        {
            public string vehicle, scope = "Editor-only supplemental observations; standard bridge suites reported separately";
            public string colorSpace;
            public Color primaryLinear, secondaryLinear;
            public bool independentPrimary, independentSecondary, maskLinearUncompressedClamped, switchFresh;
            public float bedColliderHeight, linerHeight, cargoBottomHeight, collisionAboveLiner;
            public string bedCollider;
            public int chaosSeed, chaosAttempted, chaosImpacts, chaosMisses;
            public State[] states;
        }

        static string Arg(string key, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        public static void Run()
        {
            int index = int.Parse(Arg("-vehicleIndex", "2"));
            string output = Arg("-evidenceDir", "Previews/VehicleProduction"); Directory.CreateDirectory(output);
            var prevMode = Physics.simulationMode;
            EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindAnyObjectByType<CollisionLabController>();
            lab.SelectVehicle(index); lab.model.Initialize(); lab.Setup(); lab.SetAnchored(true); lab.ResetAll();
            lab.receiver.logImpacts = false; Physics.simulationMode = SimulationMode.Script;
            var report = new Report { vehicle = lab.CurrentVehicle.displayName };
            var states = new List<State>(); var replacement = new List<(Renderer renderer, Material[] materials)>();
            var paints = lab.model.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.HasProperty("_PrimaryColor") && m.HasProperty("_StripeColor")).Distinct().ToList();
            if (paints.Count != 1) throw new InvalidOperationException("Expected one customizable two-color paint material");
            var paint = new Material(paints[0]); Color primary = paint.GetColor("_PrimaryColor"), secondary = paint.GetColor("_StripeColor");
            report.primaryLinear = primary; report.secondaryLinear = secondary; report.colorSpace = QualitySettings.activeColorSpace.ToString();
            foreach (var renderer in lab.model.GetComponentsInChildren<Renderer>())
            {
                Material[] original = renderer.sharedMaterials;
                if (!original.Any(m => m == paints[0])) continue;
                replacement.Add((renderer, original)); renderer.sharedMaterials = original.Select(m => m == paints[0] ? paint : m).ToArray();
            }
            void Shot(string name, float yaw = 135, float pitch = 20, float distance = 7.2f, Vector3? pivot = null)
            {
                var sled = lab.sled.GetComponent<Renderer>(); bool was = sled.enabled; sled.enabled = false;
                try
                {
                    string key = "90_" + name;
                    CollisionLabValidation.Shot(lab, lab.cameraController, key, yaw, pitch, distance,
                        pivot ?? lab.car.position + Vector3.up * .75f);
                    File.Copy(Path.Combine(DamageLabCapture.OutputFolder, key + ".png"), Path.Combine(output, name + ".png"), true);
                }
                finally { sled.enabled = was; }
            }
            void Step(int count)
            {
                for (int k = 0; k < count; k++) { Physics.Simulate(Time.fixedDeltaTime); lab.receiver.Step(Time.fixedDeltaTime); lab.AfterPhysicsStep(); lab.ChaosTick(); }
            }
            State Observe(string name, bool applied, bool dump = false)
            {
                var metrics = new List<PartResult>();
                foreach (var part in lab.model.Parts)
                {
                    Vector3[] source = part.sourceMesh.vertices, current = part.RuntimeMesh.vertices; int[] triangles = part.sourceMesh.triangles;
                    var folds = new List<int>(); float displacement = 0;
                    for (int k = 0; k < source.Length; k++) displacement = Mathf.Max(displacement, (source[k] - current[k]).magnitude);
                    for (int k = 0; k < triangles.Length; k += 3)
                    {
                        int a = triangles[k], b = triangles[k + 1], c = triangles[k + 2];
                        Vector3 n0 = Vector3.Cross(source[b] - source[a], source[c] - source[a]);
                        Vector3 n1 = Vector3.Cross(current[b] - current[a], current[c] - current[a]);
                        if (n0.sqrMagnitude > 1e-12f && Vector3.Dot(n0, n1) < 0) folds.Add(k);
                    }
                    metrics.Add(new PartResult { name = part.filter.name, triangles = triangles.Length / 3, folded = folds.Count, maxDisplacement = displacement });
                    if (dump && (folds.Count > 0 || part.filter.name == "Body_Hood"))
                        File.WriteAllText(Path.Combine(output, name + "_" + part.filter.name + "_mesh.json"), JsonUtility.ToJson(new MeshDump
                        { name = part.filter.name, original = source, deformed = current, triangles = triangles, foldedTriangleStarts = folds.ToArray() }));
                }
                var result = new State { name = name, applied = applied, parts = metrics.ToArray(),
                    uvUnchanged = lab.model.Parts.All(p => p.RuntimeMesh.uv.SequenceEqual(p.sourceMesh.uv) && p.RuntimeMesh.uv2.SequenceEqual(p.sourceMesh.uv2)) };
                states.Add(result); return result;
            }
            try
            {
                if (Arg("-legacySideProbe", "0") == "1")
                {
                    var rows = new List<string>();
                    foreach (var direction in new[] { ImpactDirection.LeftSide, ImpactDirection.RightSide })
                    {
                        lab.ResetAll(); lab.speed = 15; lab.mass = 1500; lab.narrowImpactor = true;
                        lab.SetDirection(direction); lab.Aim(direction, out Vector3 target, out _);
                        lab.aimOffset = 1.128f - target.z;
                        for (int hitIndex = 1; hitIndex <= 2; hitIndex++)
                        {
                            lab.ResetBlock(); Physics.SyncTransforms();
                            var bc = lab.sled.GetComponent<BoxCollider>(); var tr = bc.transform;
                            Vector3 center = tr.TransformPoint(bc.center), half = Vector3.Scale(bc.size, tr.lossyScale) * .5f;
                            var probeHits = Physics.BoxCastAll(center, half, tr.forward, tr.rotation, 20, ~0, QueryTriggerInteraction.Ignore)
                                .Where(h => h.distance > 0 && h.collider.transform.IsChildOf(lab.car.transform)).OrderBy(h => h.distance).ToArray();
                            rows.Add($"{direction} original fixed z=1.128 m hit {hitIndex}: first collider {(probeHits.Length > 0 ? probeHits[0].collider.name : "NONE")}");
                            lab.Launch(); Step(200);
                        }
                        Shot("original_side_line_" + direction, direction == ImpactDirection.LeftSide ? 135 : 225, 25);
                    }
                    File.WriteAllLines(Path.Combine(output, "original_side_line.txt"), rows);
                    return;
                }
                if (Arg("-probeOnly", "0") == "1")
                {
                    foreach (var direction in new[] { ImpactDirection.Front, ImpactDirection.LeftSide, ImpactDirection.Rear })
                    {
                        lab.ResetAll(); lab.speed = 15; lab.mass = 1500; lab.narrowImpactor = false; lab.aimOffset = 0;
                        lab.SetDirection(direction); lab.Launch(); Step(200);
                        Observe("single_" + direction, lab.receiver.Last.applied, true);
                    }
                    lab.ResetAll(); var g = lab.Geometry;
                    Vector3 start = new Vector3(g.body.center.x + .25f, g.cabin.max.y + 2, g.body.max.z - .13f * g.body.size.z);
                    bool hit = lab.model.SurfacePointAlong(start, Vector3.down, out Vector3 surface, .12f, 0);
                    bool dent = hit && lab.model.AddImpact(new LocalImpact { point = surface, direction = Vector3.down, normal = Vector3.up, strength = .8f });
                    Observe("hood_dent", dent, true);
                    report.states = states.ToArray(); File.WriteAllText(Path.Combine(output, "fold_probe.json"), JsonUtility.ToJson(report, true));
                    return;
                }
                Shot("paint_default");
                Shot("lamps_undamaged_oblique", 110, 12, 3.2f, lab.car.position + new Vector3(0, 1.0f, 1.7f));
                paint.SetColor("_PrimaryColor", new Color(.13f, .32f, .65f).linear);
                report.independentPrimary = paint.GetColor("_PrimaryColor") != primary && paint.GetColor("_StripeColor") == secondary;
                Shot("paint_primary_only"); paint.SetColor("_PrimaryColor", primary);
                paint.SetColor("_StripeColor", new Color(.75f, .24f, .12f).linear);
                report.independentSecondary = paint.GetColor("_PrimaryColor") == primary && paint.GetColor("_StripeColor") != secondary;
                Shot("paint_secondary_only"); paint.SetColor("_StripeColor", secondary);
                // Isolate the actual pigment from the Collision Lab's blue reflection cube.
                float reflection = RenderSettings.reflectionIntensity, metallic = paint.GetFloat("_Metallic"), smooth = paint.GetFloat("_Smoothness");
                var ambientMode = RenderSettings.ambientMode; Color ambient = RenderSettings.ambientLight;
                var lights = Object.FindObjectsByType<Light>(); var lightColors = lights.Select(l => l.color).ToArray();
                try
                {
                    RenderSettings.reflectionIntensity = 0; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.white * .9f;
                    foreach (var light in lights) light.color = Color.white;
                    paint.SetFloat("_Metallic", 0); paint.SetFloat("_Smoothness", 0);
                    Shot("paint_neutral_lighting_diagnostic");
                }
                finally
                {
                    RenderSettings.reflectionIntensity = reflection; RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
                    for (int k = 0; k < lights.Length; k++) lights[k].color = lightColors[k];
                    paint.SetFloat("_Metallic", metallic); paint.SetFloat("_Smoothness", smooth);
                }
                string texturePath = AssetDatabase.GetAssetPath(paint.GetTexture("_StripeMask")); var ti = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                report.maskLinearUncompressedClamped = !ti.sRGBTexture && ti.textureCompression == TextureImporterCompression.Uncompressed && ti.wrapMode == TextureWrapMode.Clamp;
                lab.ResetAll(); lab.speed = 15; lab.mass = 1500; lab.narrowImpactor = false; lab.SetDirection(ImpactDirection.Front); lab.Launch(); Step(200);
                Observe("front_wide", lab.receiver.Last.applied, true);
                Shot("front_15ms_close", 135, 24, 4.8f, lab.car.position + new Vector3(0, .85f, 1.1f));
                Shot("front_suspension_close", 135, 8, 4.8f, lab.car.position + new Vector3(0, .7f, 1.1f));
                bool showFootprint = lab.footprintDebug.show; lab.footprintDebug.SetVisible(false);
                try
                {
                    Shot("front_15ms_no_overlay", 135, 24, 4.8f, lab.car.position + new Vector3(0, .85f, 1.1f));
                    var indicator = lab.model.Parts.First(p => p.filter.name == "FrontIndicator_L").filter.GetComponent<Renderer>();
                    bool visible = indicator.enabled; indicator.enabled = false;
                    try { Shot("front_15ms_without_left_indicator_diagnostic", 135, 24, 4.8f, lab.car.position + new Vector3(0, .85f, 1.1f)); }
                    finally { indicator.enabled = visible; }
                }
                finally { lab.footprintDebug.SetVisible(showFootprint); }
                lab.ResetAll();

                var geometry = lab.Geometry; Bounds body = geometry.body, cabin = geometry.cabin;
                Vector3 hoodOrigin = new Vector3(body.center.x + .25f, cabin.max.y + 2, body.max.z - .13f * body.size.z);
                bool found = lab.model.SurfacePointAlong(hoodOrigin, Vector3.down, out Vector3 point, .12f, 0);
                bool applied = found && lab.model.AddImpact(new LocalImpact { point = point, direction = Vector3.down, normal = Vector3.up, strength = .8f });
                Observe("hood_dent", applied, true); Shot("hood_dent", 145, 48, 4.2f, lab.car.position + new Vector3(0, 1.1f, 1.1f));
                Shot("paint_deformed", 135, 20); lab.ResetAll();

                foreach (var target in new[] { ("bed_side_dent", new Vector3(-2, 1.02f, -1.25f), Vector3.right),
                    ("tailgate_dent", new Vector3(0, 1.0f, -3.5f), Vector3.forward),
                    ("liner_dent", new Vector3(.2f, 3, -1.25f), Vector3.down) })
                {
                    lab.ResetAll(); found = lab.model.SurfacePointAlong(target.Item2, target.Item3, out point, .12f, 0);
                    applied = found && lab.model.AddImpact(new LocalImpact { point = point, direction = target.Item3, normal = -target.Item3, strength = .8f });
                    Observe(target.Item1, applied); Shot(target.Item1, 315, target.Item1 == "liner_dent" ? 50 : 20, 6f);
                }
                lab.ResetAll(); Physics.SyncTransforms();
                Ray ray = new Ray(lab.car.transform.TransformPoint(new Vector3(0, 4, -1.28f)), Vector3.down);
                var hits = Physics.RaycastAll(ray, 6).Where(h => h.collider.transform.IsChildOf(lab.car.transform)).OrderBy(h => h.distance).ToArray();
                if (hits.Length == 0) throw new InvalidOperationException("Bed probe missed the car's collision rig");
                report.bedCollider = hits[0].collider.name; report.bedColliderHeight = hits[0].point.y;
                var liner = lab.model.Parts.First(p => p.filter.name == "BedLiner_Floor");
                report.linerHeight = liner.VehiclePositions.Where(v => Mathf.Abs(v.x) < .02f && Mathf.Abs(v.z + 1.28f) < .05f)
                    .Max(v => lab.car.transform.TransformPoint(v).y);
                report.collisionAboveLiner = report.bedColliderHeight - report.linerHeight;
                var cargo = GameObject.CreatePrimitive(PrimitiveType.Cube); cargo.name = "ValidationCargo";
                cargo.transform.localScale = Vector3.one * .2f; cargo.transform.position = ray.origin - Vector3.up;
                cargo.GetComponent<Renderer>().sharedMaterial = lab.sled.GetComponent<Renderer>().sharedMaterial;
                var cargoBody = cargo.AddComponent<Rigidbody>(); cargoBody.mass = 5;
                try { Step(200); report.cargoBottomHeight = cargo.GetComponent<Collider>().bounds.min.y; Shot("open_bed_collision_cargo", 315, 45, 5.5f); }
                finally { Object.DestroyImmediate(cargo); }
                lab.ResetAll(); lab.speed = 20; lab.mass = 2500; lab.randomOptions = new RandomImpactOptions { varyWidth = true }; lab.SetSeed(2026); lab.StartChaos(50);
                int steps = 0; while (lab.ChaosRunning && steps++ < 200000) Step(1);
                report.chaosSeed = 2026; report.chaosAttempted = lab.ChaosAttempted; report.chaosImpacts = lab.ChaosImpacts; report.chaosMisses = lab.ChaosMisses;
                if (lab.ChaosRunning || lab.ChaosAttempted != 50) throw new InvalidOperationException("Supplemental Chaos capture did not finish 50 shots");
                Observe("chaos_50", true); Shot("chaos_50_front", 135, 20); Shot("chaos_50_bed", 315, 45);

                lab.StopChaos(); lab.SelectVehicle(0); lab.SelectVehicle(index); lab.model.Initialize(); lab.Setup(); lab.ResetAll();
                report.switchFresh = lab.model.TotalImpactCount == 0 && !lab.receiver.HasReport && lab.VehicleWarnings.Count == 0 &&
                    lab.hitbox.Faces.All(f => f.current == f.original) && lab.model.Parts.All(p => p.sourceMesh.vertices.SequenceEqual(p.RuntimeMesh.vertices));
                report.states = states.ToArray();
                File.WriteAllText(Path.Combine(output, "supplemental_validation.json"), JsonUtility.ToJson(report, true));
                Debug.Log("[VehicleProduction] Supplemental observations written; independent paint " + report.independentPrimary + "/" + report.independentSecondary +
                    ", UV unchanged " + states.All(s => s.uvUnchanged) + ", fresh pickup after switch " + report.switchFresh +
                    ", bed collider " + report.bedCollider + " is " + report.collisionAboveLiner.ToString("F3") + " m above liner");
                if (!report.independentPrimary || !report.independentSecondary || !report.maskLinearUncompressedClamped || !report.switchFresh || states.Any(s => !s.applied || !s.uvUnchanged))
                    throw new InvalidOperationException("Supplemental validation failed; inspect JSON");
            }
            finally
            {
                foreach (var item in replacement) if (item.renderer != null) item.renderer.sharedMaterials = item.materials;
                Object.DestroyImmediate(paint); Physics.simulationMode = prevMode;
                lab.StopChaos(); lab.ResetAll(); lab.SelectVehicle(0); lab.model.Shutdown();
            }
        }
    }
}
