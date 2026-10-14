using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Collision Lab phase 2: checks that the impactor's geometry shapes the damage. Same real PhysX setup as
    /// <see cref="CollisionLabValidation"/>; measures the resolved contact footprint and the region of the
    /// mesh that actually moved (width across the impact, height, and how evenly the crush is spread).
    /// </summary>
    public static class CollisionGeometryValidation
    {
        struct Case
        {
            public string name; public ImpactDirection dir; public float speed, mass; public bool narrow, glancing, pointOnly;
            public float yaw, pitch, dist;
        }

        static Case C(string n, ImpactDirection d, float speed, float mass, bool narrow, float yaw, float pitch = 20f, float dist = 7.5f, bool glancing = false, bool pointOnly = false)
            => new Case { name = n, dir = d, speed = speed, mass = mass, narrow = narrow, yaw = yaw, pitch = pitch, dist = dist, glancing = glancing, pointOnly = pointOnly };

        struct Result
        {
            public CollisionDamageReceiver.Report r;
            public float maxDisp, width, height, centre, outer;
            public int moved, nan, flipped, tris;
        }

        public static string Run(bool screenshots = true)
        {
            if (EditorSceneManager.GetActiveScene().path != CollisionLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var lab = Object.FindAnyObjectByType<CollisionLabController>();
            var model = lab.model;
            var receiver = lab.receiver;
            var cam = lab.cameraController;
            var debug = Object.FindAnyObjectByType<ImpactFootprintDebug>();
            var sb = new StringBuilder("[CollisionLab] Phase 2 geometry-aware footprint validation (real PhysX contacts, Physics.Simulate at 50 Hz)\n");
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            model.Initialize();
            bool wasAnchored = lab.anchored;
            lab.SetAnchored(false);   // these suites measure the free-moving car
            lab.Setup();
            receiver.logImpacts = false;
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var filtersDeform = new List<MeshFilter>();
                foreach (var f in filters) if (f.GetComponent<LineRenderer>() == null) filtersDeform.Add(f);
                filters = filtersDeform.ToArray();
                lab.narrowImpactor = false;
                lab.ResetAll();
                var baseVerts = CollisionLabValidation.Snapshot(filters);
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) baseNormals.Add(f.sharedMesh.normals);

                var cases = new[]
                {
                    C("P1_PointImpact_Front_12ms", ImpactDirection.Front, 12f, 1500f, false, 135f, 12f, 6.5f, pointOnly: true),
                    C("G1_Wide_Front_12ms", ImpactDirection.Front, 12f, 1500f, false, 135f, 12f, 6.5f),
                    C("G2_Narrow_Front_12ms", ImpactDirection.Front, 12f, 1500f, true, 135f, 12f, 6.5f),
                    C("G3_Wide_Rear_12ms", ImpactDirection.Rear, 12f, 1500f, false, 315f, 12f, 6.5f),
                    C("G4_Wide_LeftDoor_12ms", ImpactDirection.LeftSide, 12f, 1500f, false, 95f, 12f, 6.5f),
                    C("G5_Narrow_LeftDoor_12ms", ImpactDirection.LeftSide, 12f, 1500f, true, 95f, 12f, 6.5f),
                    C("G6_Wide_FrontLeftCorner_14ms", ImpactDirection.FrontCornerAngled, 14f, 1500f, false, 125f, 12f, 6.5f),
                    C("G7_Glancing_LeftScrape_15ms", ImpactDirection.LeftSide, 15f, 1500f, false, 105f, 18f, 8f, glancing: true),
                    C("V1_Wide_Front_6ms", ImpactDirection.Front, 6f, 1500f, false, 135f, 12f, 6.5f),
                    C("V2_Wide_Front_20ms", ImpactDirection.Front, 20f, 1500f, false, 135f, 12f, 6.5f),
                    C("M1_Wide_Front_12ms_500kg", ImpactDirection.Front, 12f, 500f, false, 135f, 12f, 6.5f),
                    C("M2_Wide_Front_12ms_3000kg", ImpactDirection.Front, 12f, 3000f, false, 135f, 12f, 6.5f),
                };

                sb.AppendLine("  case                            closing  strength  footprint (kind  U x V m  area m²  crush m  area×)      moved verts  damaged width x height  max disp  centre/outer crush  flipped");
                var res = new Dictionary<string, Result>();
                bool signOk = true;
                foreach (var c in cases)
                {
                    var r = Launch(lab, receiver, c, keepDamage: false);
                    var m = Measure(filters, baseVerts, model.transform, r);
                    DamageLabValidation.CheckSanity(filters, baseVerts, out m.nan, out m.flipped, out m.tris);
                    res[c.name] = m;
                    if (receiver.HasReport && r.strength > 0f && r.rawClosing <= 0f) signOk = false;
                    if (m.flipped > 100) sb.AppendLine($"    flipped in {c.name}: {FlipReport(filters, baseVerts, model.transform)}");
                    var fp = r.footprint;
                    string fps = fp.valid ? $"{fp.kind,-6} {fp.halfU * 2f:0.00} x {fp.halfV * 2f:0.00}  {fp.area,5:0.00}  {fp.travel,6:0.000}  {fp.areaFactor:0.00}" : "point (phase 1 radial dent)          ";
                    sb.AppendLine($"  {c.name,-31} {r.closingSpeed,6:0.00}  {r.strength * 100f,7:0.0}   {fps,-44} {m.moved,8}     {m.width,5:0.00} x {m.height,4:0.00} m      {m.maxDisp,6:0.000}   {m.centre,5:0.000} / {m.outer,5:0.000}     {m.flipped}{(m.nan > 0 ? $" NaN {m.nan}" : "")}");
                    if (screenshots)
                    {
                        if (debug != null) debug.SetVisible(true);
                        ShotAfter(lab, cam, "4x_" + c.name, c);
                        if (debug != null && (c.name.StartsWith("G1") || c.name.StartsWith("G2") || c.name.StartsWith("P1")))
                        {
                            debug.SetVisible(false);
                            ShotAfter(lab, cam, "4x_" + c.name + "_NoOverlay", c);
                            debug.SetVisible(true);
                        }
                    }
                    lab.ResetAll();
                }

                // ---------------------------------------------------------------- comparisons
                Result wide = res["G1_Wide_Front_12ms"], narrow = res["G2_Narrow_Front_12ms"], point = res["P1_PointImpact_Front_12ms"];
                sb.AppendLine($"  WIDE vs NARROW (front, 1500 kg, 12 m/s, same strength {wide.r.strength * 100f:0.0} / {narrow.r.strength * 100f:0.0}): " +
                              $"footprint {wide.r.footprint.halfU * 2f:0.00} m vs {narrow.r.footprint.halfU * 2f:0.00} m wide, area {wide.r.footprint.area:0.00} vs {narrow.r.footprint.area:0.00} m², " +
                              $"crush depth {wide.r.footprint.travel:0.000} vs {narrow.r.footprint.travel:0.000} m; damaged width {wide.width:0.00} vs {narrow.width:0.00} m, " +
                              $"centre/outer crush {wide.centre:0.000}/{wide.outer:0.000} vs {narrow.centre:0.000}/{narrow.outer:0.000} " +
                              $"{(wide.r.footprint.halfU > 2.2f * narrow.r.footprint.halfV && wide.width > narrow.width + 0.5f && narrow.r.footprint.travel > wide.r.footprint.travel ? "PASS (wide = broad & shallower, narrow = concentrated & deeper)" : "CHECK")}");
                sb.AppendLine($"  Wide flat front vs phase-1 point impact: outer/centre crush ratio {Ratio(wide):0.00} vs {Ratio(point):0.00} " +
                              $"{(Ratio(wide) > 0.6f && Ratio(wide) > 1.5f * Ratio(point) ? "PASS (broad compression, no central V)" : "CHECK")}");
                var corner = res["G6_Wide_FrontLeftCorner_14ms"].r.footprint;
                sb.AppendLine($"  Corner: {corner.kind}, {corner.halfU * 2f:0.00} x {corner.halfV * 2f:0.00} m, area {corner.area:0.00} m² vs flat front {wide.r.footprint.area:0.00} m² " +
                              $"{(corner.valid && corner.area < 0.75f * wide.r.footprint.area ? "PASS (concentrated)" : "CHECK")}");
                var gl = res["G7_Glancing_LeftScrape_15ms"];
                var door = res["G4_Wide_LeftDoor_12ms"];
                sb.AppendLine($"  Glancing (15 m/s): closing {gl.r.closingSpeed:0.00} m/s, strength {gl.r.strength * 100f:0.0}, max disp {gl.maxDisp:0.000} m vs direct door hit at 12 m/s {door.maxDisp:0.000} m " +
                              $"{(gl.maxDisp < 0.6f * door.maxDisp ? "PASS (much less crushing)" : "CHECK")}");
                sb.AppendLine($"  Speed (wide front): 6 / 12 / 20 m/s -> crush {res["V1_Wide_Front_6ms"].r.footprint.travel:0.000} / {wide.r.footprint.travel:0.000} / {res["V2_Wide_Front_20ms"].r.footprint.travel:0.000} m " +
                              $"{(res["V1_Wide_Front_6ms"].r.footprint.travel < wide.r.footprint.travel && wide.r.footprint.travel < res["V2_Wide_Front_20ms"].r.footprint.travel ? "PASS" : "FAIL")}");
                sb.AppendLine($"  Mass (wide front, 12 m/s): 500 / 1500 / 3000 kg -> crush {res["M1_Wide_Front_12ms_500kg"].r.footprint.travel:0.000} / {wide.r.footprint.travel:0.000} / {res["M2_Wide_Front_12ms_3000kg"].r.footprint.travel:0.000} m " +
                              $"{(res["M1_Wide_Front_12ms_500kg"].r.footprint.travel < wide.r.footprint.travel && wide.r.footprint.travel < res["M2_Wide_Front_12ms_3000kg"].r.footprint.travel ? "PASS" : "FAIL")}");
                sb.AppendLine($"  Closing-speed sign: every approaching collision had relativeVelocity·n > 0 {(signOk ? "PASS" : "FAIL")}; " +
                              $"separating input (-3 m/s along n) -> closing {CollisionDamageReceiver.ClosingSpeed(-3f * Vector3.forward, Vector3.forward, out bool sep):0.00}, separating {sep} {(sep ? "PASS (no impact)" : "FAIL")}");

                // ---------------------------------------------------------------- accumulation + saturation
                lab.SetAnchored(true);    // repeated hits on the same spot: anchored, since Launch no longer resets the car pose
                lab.ResetAll();
                lab.narrowImpactor = false;
                Launch(lab, receiver, C("acc1", ImpactDirection.Front, 12f, 1500f, false, 160f), false);
                float after1 = CollisionLabValidation.MaxDiff(baseVerts, CollisionLabValidation.Snapshot(filters));
                Launch(lab, receiver, C("acc2", ImpactDirection.FrontCornerAngled, 14f, 1500f, false, 125f, 12f, 6.5f), true);
                float after2 = CollisionLabValidation.MaxDiff(baseVerts, CollisionLabValidation.Snapshot(filters));
                int count2 = model.ImpactCount;
                if (screenshots) ShotAfter(lab, cam, "4x_A1_Accumulated_Front_then_Corner", C("acc", ImpactDirection.FrontCornerAngled, 0, 0, false, 125f, 12f, 6.5f));
                sb.AppendLine($"  Accumulation (anchored): front then corner without reset -> {count2} impacts, max disp {after1:0.000} -> {after2:0.000} m {(count2 == 2 && after2 >= after1 ? "PASS" : "FAIL")}");
                lab.ResetAll();
                var sat = new float[4];
                for (int k = 0; k < 4; k++)
                {
                    Launch(lab, receiver, C("sat", ImpactDirection.Front, 20f, 3000f, false, 160f), k > 0);
                    sat[k] = CollisionLabValidation.MaxDiff(baseVerts, CollisionLabValidation.Snapshot(filters));
                }
                DamageLabValidation.CheckSanity(filters, baseVerts, out int satNan, out int satFlip, out int satTris);
                if (screenshots) ShotAfter(lab, cam, "4x_S1_Saturation_4x_Heavy_Front", C("sat", ImpactDirection.Front, 0, 0, false, 135f, 12f, 6.5f));
                sb.AppendLine($"  Saturation (anchored): 4 heavy wide front hits (3000 kg, 20 m/s) -> max disp {sat[0]:0.000} / {sat[1]:0.000} / {sat[2]:0.000} / {sat[3]:0.000} m, impacts {model.ImpactCount}, " +
                              $"flipped {satFlip} of {satTris:N0}, NaN {satNan} {(sat[3] - sat[2] < sat[1] - sat[0] + 1e-4f && sat[3] < 0.75f ? "PASS (diminishing, bounded)" : "CHECK")}");

                lab.SetAnchored(false);
                // ---------------------------------------------------------------- reset + repeatability
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                sb.AppendLine($"  Reset: max vertex error {ep:E2} m, normal error {en:E2} {(ep == 0f && en == 0f ? "PASS (exact)" : "FAIL")}");
                // Same launch three times from reset (physics + footprint + mesh must repeat).
                var meshes = new List<Vector3[]>[3];
                var reps = new CollisionDamageReceiver.Report[3];
                var events = new int[3];
                for (int k = 0; k < 3; k++)
                {
                    reps[k] = Launch(lab, receiver, C("rep", ImpactDirection.FrontCornerAngled, 14f, 1500f, false, 125f, 12f, 6.5f), false);
                    events[k] = receiver.EventCount * 100 + model.ImpactCount;
                    meshes[k] = CollisionLabValidation.Snapshot(filters);
                }
                for (int k = 0; k < 3; k++)
                    sb.AppendLine($"    run {k + 1}: events {events[k] / 100} impacts {events[k] % 100} closing {reps[k].closingSpeed:0.0000} strength {reps[k].strength:0.000000} " +
                                  $"crush {reps[k].footprint.travel:0.000000} area {reps[k].footprint.area:0.000000} centre {reps[k].footprint.center.ToString("F4")} dir {reps[k].localDirection.ToString("F4")}");
                float rep01 = CollisionLabValidation.MaxDiff(meshes[0], meshes[1]), rep12 = CollisionLabValidation.MaxDiff(meshes[1], meshes[2]);
                sb.AppendLine($"  Same corner launch 3x from reset: mesh difference run1-2 {rep01:E2} m, run2-3 {rep12:E2} m {(rep01 < 1e-3f && rep12 < 1e-3f ? "PASS (same within 1 mm)" : "CHECK")}");
            }
            finally
            {
                receiver.geometryAwareFootprint = true;
                lab.narrowImpactor = false;
                lab.ResetAll();
                receiver.logImpacts = true;
                Physics.simulationMode = prevMode;
                lab.SetAnchored(wasAnchored);
                model.Shutdown();
                cam.pivot = new Vector3(0f, 0.6f, 1.5f);
                cam.SetView(cam.defaultYaw, cam.defaultPitch, cam.defaultDistance);
            }
            return sb.ToString();
        }


        /// <summary>Where the flipped triangles are: per part, count and mean vehicle-space centroid.</summary>
        static string FlipReport(MeshFilter[] filters, List<Vector3[]> baseVerts, Transform root)
        {
            var parts = new List<(int n, string s)>();
            for (int i = 0; i < filters.Length; i++)
            {
                var v = filters[i].sharedMesh.vertices; var t = filters[i].sharedMesh.triangles; var v0 = baseVerts[i];
                int n = 0; Vector3 sum = Vector3.zero;
                for (int k = 0; k < t.Length; k += 3)
                {
                    Vector3 a = Vector3.Cross(v0[t[k + 1]] - v0[t[k]], v0[t[k + 2]] - v0[t[k]]);
                    Vector3 c = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                    if (a.sqrMagnitude < 1e-12f || Vector3.Dot(a, c) >= 0f) continue;
                    n++; sum += root.InverseTransformPoint(filters[i].transform.TransformPoint((v0[t[k]] + v0[t[k + 1]] + v0[t[k + 2]]) / 3f));
                }
                if (n > 0) parts.Add((n, $"{filters[i].name} {n} at {(sum / n).ToString("F2")}"));
            }
            parts.Sort((x, y) => y.n.CompareTo(x.n));
            var sb = new StringBuilder();
            for (int i = 0; i < Mathf.Min(4, parts.Count); i++) sb.Append(parts[i].s).Append("; ");
            return sb.ToString();
        }
        static float Ratio(Result m) => m.centre > 1e-4f ? m.outer / m.centre : 0f;

        static CollisionDamageReceiver.Report Launch(CollisionLabController lab, CollisionDamageReceiver receiver, Case c, bool keepDamage)
        {
            receiver.geometryAwareFootprint = !c.pointOnly;
            lab.direction = c.dir; lab.speed = c.speed; lab.mass = c.mass; lab.narrowImpactor = c.narrow;
            if (!keepDamage) lab.ResetAll();
            lab.Launch();
            if (c.glancing)
            {
                // Side swipe: sled travelling forward along the car's left side at 15°, overlapping 0.35 m (as in phase 1).
                Vector3 dir = Quaternion.Euler(0f, 15f, 0f) * Vector3.forward;
                Vector3 start = new Vector3(-1.03f - 0.95f + 0.35f, 0f, -0.2f) - dir * 7f;
                start.y = lab.car.position.y + lab.sledHeight;
                lab.sled.isKinematic = true;
                lab.sled.position = start; lab.sled.rotation = Quaternion.LookRotation(dir);
                lab.sled.transform.SetPositionAndRotation(start, lab.sled.rotation);
                Physics.SyncTransforms();
                lab.sled.isKinematic = false;
                lab.sled.linearVelocity = dir * c.speed;
            }
            for (int i = 0; i < 200; i++)        // 4 s of simulated time
            {
                Physics.Simulate(Time.fixedDeltaTime);
                receiver.Step(Time.fixedDeltaTime);
            }
            return receiver.Last;
        }

        /// <summary>Region of the mesh that moved more than 3 cm: its width across the impact (horizontal),
        /// height, and the mean crush near the footprint centre vs 0.45-0.75 m to either side of it.</summary>
        static Result Measure(MeshFilter[] filters, List<Vector3[]> baseVerts, Transform root, CollisionDamageReceiver.Report r)
        {
            var m = new Result { r = r };
            Vector3 d = r.localDirection.sqrMagnitude > 0f ? r.localDirection.normalized : Vector3.forward;
            Vector3 h = Vector3.Cross(Vector3.up, d);
            h = h.sqrMagnitude > 1e-6f ? h.normalized : Vector3.right;
            float centreLat = Vector3.Dot(r.localPoint, h);
            float minH = float.MaxValue, maxH = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            float cs = 0f, os = 0f; int cn = 0, on = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                var v = filters[i].sharedMesh.vertices;
                var t = filters[i].transform;
                for (int j = 0; j < v.Length; j++)
                {
                    Vector3 p0 = root.InverseTransformPoint(t.TransformPoint(baseVerts[i][j]));
                    Vector3 p1 = root.InverseTransformPoint(t.TransformPoint(v[j]));
                    float disp = (p1 - p0).magnitude;
                    m.maxDisp = Mathf.Max(m.maxDisp, disp);
                    if (disp > 0.01f)
                    {
                        float lat = Mathf.Abs(Vector3.Dot(p0, h) - centreLat);
                        if (lat < 0.2f) { cs += disp; cn++; }
                        else if (lat > 0.45f && lat < 0.75f) { os += disp; on++; }
                    }
                    if (disp <= 0.03f) continue;
                    m.moved++;
                    float l = Vector3.Dot(p0, h);
                    minH = Mathf.Min(minH, l); maxH = Mathf.Max(maxH, l);
                    minY = Mathf.Min(minY, p0.y); maxY = Mathf.Max(maxY, p0.y);
                }
            }
            if (m.moved > 0) { m.width = maxH - minH; m.height = maxY - minY; }
            m.centre = cn > 0 ? cs / cn : 0f;
            m.outer = on > 0 ? os / on : 0f;
            return m;
        }

        static void ShotAfter(CollisionLabController lab, DamageLab.Inspection.InspectionCameraController cam, string name, Case c)
        {
            lab.RefreshDiagnostics();
            Vector3 pivot = lab.car.position + Vector3.up * 0.6f;
            var sledRenderer = lab.sled.GetComponent<Renderer>();
            sledRenderer.enabled = false;   // keep the sled from hiding the damage in the inspection shot
            Aim(c.dir, lab, ref pivot);
            CollisionLabValidation.Shot(lab, cam, name, c.yaw + lab.car.rotation.eulerAngles.y, c.pitch, c.dist, pivot);
            sledRenderer.enabled = true;
        }

        // Look at the struck end of the car rather than its middle.
        static void Aim(ImpactDirection d, CollisionLabController lab, ref Vector3 pivot)
        {
            CollisionLabController.Aim(d, out Vector3 target, out _);
            Vector3 world = lab.car.transform.TransformPoint(new Vector3(target.x * 0.6f, 0f, target.z * 0.6f));
            pivot = new Vector3(world.x, lab.car.position.y + 0.6f, world.z);
        }
    }
}
