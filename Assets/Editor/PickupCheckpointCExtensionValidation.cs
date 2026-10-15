using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DamageLab.EditorTools
{
    // Focused real-physics evidence; no tuning, runtime or source asset mutation.
    public static class PickupCheckpointCExtensionValidation
    {
        static string Arg(string k, string d) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }
        [Serializable] class Candidate { public string name; public float crumple; public Vector3[] vertices, normals; public int[] triangles; }
        [Serializable] class Candidates { public Candidate[] candidates; }
        static void EvaluateCandidates(CollisionLabController lab, string output, LocalImpact impact)
        {
            var field = new ImpactField(lab.model.profile.impact); field.SetStructure(lab.Geometry.wheelCenters, lab.model.profile.roofLineStart); field.Add(impact);
            var rows = new List<string> { "candidate,vertices,triangles,folded,fold_percent,stationary_axial" };
            foreach (var mesh in JsonUtility.FromJson<Candidates>(File.ReadAllText(Path.Combine(output, "topology_candidates.json"))).candidates)
            {
                var x = mesh.vertices; var v = new Vector3[x.Length]; int stationary = 0, folds = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    var delta = field.Limit(field.Contribution(0, x[i], mesh.normals[i], mesh.crumple), field.Capacity(x[i], mesh.crumple));
                    v[i] = x[i] + delta; if (Mathf.Abs(delta.z) < .00001f) stationary++;
                }
                var t = mesh.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2];
                    if (Vector3.Dot(Vector3.Cross(x[b]-x[a], x[c]-x[a]), Vector3.Cross(v[b]-v[a], v[c]-v[a])) < 0) folds++;
                }
                rows.Add(FormattableString.Invariant($"{mesh.name},{x.Length},{t.Length / 3},{folds},{folds * 300f / t.Length:F4},{stationary}"));
            }
            File.WriteAllLines(Path.Combine(output, "topology_candidate_results.csv"), rows);
        }
        static void ContractFixtures(string output)
        {
            var rows = new List<string>(); int failures = 0;
            var root = new GameObject("ContactContractFixture"); root.transform.position = new Vector3(100, 0, 100);
            Bounds expectedCabin = new Bounds(new Vector3(0, 1.58f, .115f), new Vector3(1.758f, .478f, 1.542f));
            var adaptive = new GameObject("AdaptiveFixture"); adaptive.transform.SetParent(root.transform, false);
            var shell = adaptive.AddComponent<BoxCollider>();
            var cabinObject = new GameObject("ProtectedCabinFixture"); cabinObject.transform.SetParent(root.transform, false);
            var cabin = cabinObject.AddComponent<BoxCollider>();
            void Check(string name, float z, float shellFace, Bounds actualCabin, float first, float retract, bool applied, bool expectedPass, float cabinScaleX = 1, float cabinYaw = 0)
            {
                shell.center = new Vector3((shellFace - .1f) * .5f, .92f, 1.128f); shell.size = new Vector3(shellFace + .1f, .84f, 1.128f);
                cabin.center = actualCabin.center; cabin.size = actualCabin.size;
                cabinObject.transform.localScale = new Vector3(cabinScaleX, 1, 1);
                cabinObject.transform.localRotation = Quaternion.Euler(0, cabinYaw, 0);
                Physics.SyncTransforms();
                Vector3 center = new Vector3(4, .923f, z), half = new Vector3(1.9f, .55f, .25f);
                var hits = Physics.BoxCastAll(root.transform.TransformPoint(center), half, Vector3.left, Quaternion.identity, 8,
                    ~0, QueryTriggerInteraction.Ignore).Where(h => h.distance > 0 && h.collider.transform.IsChildOf(root.transform)).OrderBy(h => h.distance).ToArray();
                float touch = hits.Length > 0 ? center.x - half.x - hits[0].distance : float.NaN;
                float face = AdaptiveContactValidation.ExpectedFace(.75f, expectedCabin, center, half, 0, 1, out bool cabinFirst);
                bool identity = hits.Length > 0 && hits[0].collider == (cabinFirst ? cabin : shell);
                bool pass = AdaptiveContactValidation.Accept(first, touch, face, retract, applied, identity,
                    AdaptiveContactValidation.CabinMatches(cabin, root.transform, expectedCabin));
                bool correct = pass == expectedPass; if (!correct) failures++;
                rows.Add($"{name}: swept {touch:F4}, expected {face:F4}, cabinFirst {cabinFirst}, acceptance {pass}, expected {expectedPass}: {(correct ? "PASS" : "FAIL")}");
            }
            try
            {
                Vector3 sideways = AdaptiveContactValidation.ProjectedHalfExtents(Quaternion.Euler(0, 90, 0), new Vector3(.25f, .55f, 1.9f));
                bool projected = (sideways - new Vector3(1.9f, .55f, .25f)).magnitude < .00001f;
                if (!projected) failures++;
                rows.Add("rotated side-shot footprint uses 25 cm lateral half-width, not 1.9 m length: " + (projected ? "PASS" : "FAIL"));
                Check("intentional cabin overlap (8 mm)", 1.128f, .75f, expectedCabin, .962f, .23f, true, true);
                Check("footprint clear of cabin", 1.18f, .75f, expectedCabin, .962f, .23f, true, true);
                Check("stale outer shell", 1.18f, .962f, expectedCabin, .962f, .23f, true, false);
                var moved = expectedCabin; moved.center += Vector3.right * .025f;
                Check("misplaced cabin even within 6 cm face tolerance", 1.128f, .75f, moved, .962f, .23f, true, false);
                var expanded = expectedCabin; expanded.Expand(new Vector3(.04f, 0, 0));
                Check("expanded cabin", 1.128f, .75f, expanded, .962f, .23f, true, false);
                Check("scaled cabin child preserves raw size but expands physical box", 1.128f, .75f, expectedCabin, .962f, .23f, true, false, 1.02f);
                Check("rotated cabin child changes protected volume", 1.128f, .75f, expectedCabin, .962f, .23f, true, false, 1, 2);
                Check("insufficient depth change", 1.18f, .75f, expectedCabin, .775f, .23f, true, false);
                Check("insufficient regional retraction", 1.18f, .75f, expectedCabin, .962f, .049f, true, false);
                Check("impact not applied", 1.18f, .75f, expectedCabin, .962f, .23f, false, false);
                Check("wrong swept surface over 6 cm", 1.18f, .82f, expectedCabin, .962f, .23f, true, false);
            }
            finally { Object.DestroyImmediate(root); }
            File.WriteAllLines(Path.Combine(output, "contact_contract_fixtures.txt"), rows);
            if (failures > 0) throw new InvalidOperationException($"{failures} physical contact contract fixtures failed");
            Debug.Log("[PickupExtension] 12 physical contact contract fixtures PASS");
        }
        static void Profile(string output)
        {
            var rows = new List<string> { "vehicle,pass,direction,resolve_ms,accumulate_ms,rebuild_ms,total_ms,evaluated_parts,rebuilt_parts,vertices,main_thread_allocated_bytes" };
            var previous = Physics.simulationMode;
            EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindAnyObjectByType<CollisionLabController>(); Physics.simulationMode = SimulationMode.Script;
            try
            {
                for (int vehicle = 0; vehicle < 3; vehicle++)
                {
                    lab.SelectVehicle(vehicle); lab.model.Initialize(); lab.Setup(); lab.SetAnchored(true); lab.receiver.logImpacts = false;
                    for (int pass = 0; pass < 3; pass++) foreach (var d in new[] { ImpactDirection.Front, ImpactDirection.Rear, ImpactDirection.LeftSide, ImpactDirection.RightSide, ImpactDirection.FrontCornerAngled, ImpactDirection.RearCornerAngled })
                    {
                        lab.ResetAll(); lab.speed = 15; lab.mass = 1500; lab.narrowImpactor = false; lab.aimOffset = 0; lab.SetDirection(d);
                        long bytes = GC.GetAllocatedBytesForCurrentThread(); lab.Launch();
                        for (int i = 0; i < 200; i++) { Physics.Simulate(Time.fixedDeltaTime); lab.receiver.Step(Time.fixedDeltaTime); lab.AfterPhysicsStep(); }
                        bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                        if (!lab.receiver.Last.applied) throw new InvalidOperationException("Profile hit missed: " + d);
                        var m = lab.model;
                        rows.Add(FormattableString.Invariant($"{vehicle},{pass},{d},{m.LastResolveMilliseconds:F4},{m.LastAccumulateMilliseconds:F4},{m.LastDeformMilliseconds:F4},{lab.receiver.Last.deformMilliseconds:F4},{m.LastImpactPartsEvaluated},{m.LastRebuiltParts},{m.DeformableVertices},{bytes}"));
                    }
                }
            }
            finally { lab.ResetAll(); lab.SelectVehicle(0); lab.model.Shutdown(); Physics.simulationMode = previous; }
            File.WriteAllLines(Path.Combine(output, "phase_profile.csv"), rows);
            Debug.Log("[PickupExtension] Warm-up + 12 measured impacts per vehicle profiled; allocations cover the full 200-step launch, main thread only");
        }
        public static void Deliver()
        {
            const string manifest = "Assets/Vehicles/CompactPickup/vehicle.json";
            string dry = VehicleManifestImporter.Import(manifest, true, out _, out _); Debug.Log(dry);
            if (!dry.Contains("RESULT: PASS")) throw new InvalidOperationException("Manifest dry run failed");
            string imported = VehicleManifestImporter.Import(manifest, false, out _, out _); Debug.Log(imported);
            if (!imported.Contains("RESULT: PASS")) throw new InvalidOperationException("Manifest import failed");
            VehicleProductionValidation.Run();
            DamageLabWebBuild.BuildCollision();
        }
        public static void Run()
        {
            string output = Arg("-evidenceDir", "Previews/CompactPickup/StageC/Revision2/Investigation");
            Directory.CreateDirectory(output);
            if (Arg("-coverageOnly", "0") == "1")
            {
                string report = CollisionPersistenceValidation.RunWholeVehicle(int.Parse(Arg("-vehicleIndex", "2")), false);
                File.WriteAllText(Path.Combine(output, "coverage.txt"), report);
                bool pass = report.Split('\n').Any(s => s.Contains("Coverage:") && s.Contains("PASS"));
                Debug.Log("[PickupExtension] Coverage acceptance " + (pass ? "PASS" : "FAIL"));
                EditorApplication.Exit(pass ? 0 : 1); return;
            }
            if (Arg("-captureChaos", "0") == "1")
            {
                CollisionPersistenceValidation.ChaosEvidenceDirectory = output;
                try
                {
                    for (int v = 2; v >= 1; v--) File.WriteAllText(Path.Combine(output, "capture_vehicle" + v + ".txt"), CollisionPersistenceValidation.RunRandom(v, 2026, 50));
                }
                finally { CollisionPersistenceValidation.ChaosEvidenceDirectory = null; }
                return;
            }
            if (Arg("-extensionSuite", "0") == "1")
            {
                ContractFixtures(output);
                for (int v = 0; v < 3; v++)
                {
                    string folder = Path.Combine(output, "Vehicle" + v); Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "integration.txt"), CollisionPersistenceValidation.RunVehicle(v, false, v == 2));
                    File.WriteAllText(Path.Combine(folder, "phase4.txt"), CollisionPersistenceValidation.RunWholeVehicle(v, v == 2));
                    File.WriteAllText(Path.Combine(folder, "overflow.txt"), CollisionPersistenceValidation.RunOverflow(false, v));
                    File.WriteAllText(Path.Combine(folder, "chaos.txt"), CollisionPersistenceValidation.RunRandom(v, 2026, 50));
                    Debug.Log("[PickupExtension] Completed integration, adaptive, overflow, replay, Chaos suites for vehicle " + v);
                }
                File.WriteAllText(Path.Combine(output, "switching.txt"), CollisionPersistenceValidation.RunSwitching());
                Profile(output);
                return;
            }
            if (Arg("-contractFixtures", "0") == "1") { ContractFixtures(output); return; }
            if (Arg("-profileOnly", "0") == "1") { Profile(output); return; }
            var prev = Physics.simulationMode;
            EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindAnyObjectByType<CollisionLabController>();
            lab.SelectVehicle(2); lab.model.Initialize(); lab.Setup(); lab.SetAnchored(true); lab.ResetAll();
            lab.receiver.logImpacts = false; Physics.simulationMode = SimulationMode.Script;
            var text = new StringBuilder();
            try
            {
                lab.speed = 15; lab.mass = 1500; lab.narrowImpactor = false; lab.aimOffset = 0;
                lab.SetDirection(ImpactDirection.Front); lab.Launch();
                for (int i = 0; i < 200; i++) { Physics.Simulate(Time.fixedDeltaTime); lab.receiver.Step(Time.fixedDeltaTime); lab.AfterPhysicsStep(); }
                if (!lab.receiver.Last.applied || lab.model.ImpactCount != 1) throw new InvalidOperationException("Expected one standard front impact");
                var impact = lab.model.Impacts[0];
                if (Arg("-topologyOnly", "0") == "1") { EvaluateCandidates(lab, output, impact); return; }
                text.AppendLine(JsonUtility.ToJson(impact, true));
                text.AppendLine($"Time resolve {lab.model.LastResolveMilliseconds:F3}, accumulate {lab.model.LastAccumulateMilliseconds:F3}, rebuild {lab.model.LastDeformMilliseconds:F3} ms");
                foreach (var name in new[] { "FrontIndicator_L", "FrontIndicator_R", "Headlight_L", "Headlight_R", "Suspension_Front" })
                {
                    var p = lab.model.Parts.First(x => x.filter.name == name);
                    var v = p.VehiclePositions; var current = p.RuntimeMesh.vertices;
                    var disp = Enumerable.Range(0, v.Length).Select(i => lab.car.transform.InverseTransformPoint(p.filter.transform.TransformPoint(current[i])) - v[i]).ToArray();
                    var tri = p.sourceMesh.triangles; var folds = new List<int>();
                    for (int j = 0; j < tri.Length; j += 3)
                    {
                        int a = tri[j], b = tri[j + 1], c = tri[j + 2];
                        if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), Vector3.Cross(v[b] + disp[b] - v[a] - disp[a], v[c] + disp[c] - v[a] - disp[a])) < 0) folds.Add(j);
                    }
                    text.AppendLine($"\n{name}: {v.Length} vertices, {folds.Count}/{tri.Length / 3} folded, zero axial {disp.Count(d => Mathf.Abs(d.z) < .00001f)}");
                    var candidates = new HashSet<int>(folds.Take(8).SelectMany(j => new[] { tri[j], tri[j + 1], tri[j + 2] }));
                    foreach (int i in Enumerable.Range(0, v.Length).Where(i => Mathf.Abs(disp[i].z) < .00001f).Take(12))
                    {
                        candidates.Add(i);
                        int nearest = Enumerable.Range(0, v.Length).Where(k => Mathf.Abs(disp[k].z) > .01f).OrderBy(k => (v[k] - v[i]).sqrMagnitude).DefaultIfEmpty(-1).First();
                        if (nearest >= 0) { candidates.Add(nearest); text.AppendLine($"stationary {i}, moved neighbour {nearest}, separation {(v[nearest] - v[i]).magnitude:F6} m"); }
                    }
                    foreach (int i in candidates.OrderBy(i => i)) text.AppendLine($"vertex {i}, actual {disp[i].ToString("F6")}: " + lab.model.ExplainImpactVertex(p.filter, i, 0, Vector3.zero));
                }
                File.WriteAllText(Path.Combine(output, "standard_front_vertex_explanations.txt"), text.ToString());
                Debug.Log("[PickupExtension] Standard front diagnostic captured");
            }
            finally { lab.ResetAll(); lab.SelectVehicle(0); lab.model.Shutdown(); Physics.simulationMode = prev; }
        }
    }
}
