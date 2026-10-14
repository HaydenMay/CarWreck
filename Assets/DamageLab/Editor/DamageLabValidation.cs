using System.Collections.Generic;
using System.Text;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Checks the reversibility guarantees of the damage system on the real lab vehicle:
    ///  1. every slider back to 0 restores the original vertices and normals bit-for-bit;
    ///  2. a value reached by different paths (20 → 60 → 20 vs. straight to 20) gives the same mesh;
    ///  3. many random slider moves do not accumulate error.
    /// </summary>
    public static class DamageLabValidation
    {
        [MenuItem("Damage Lab/Validate Reversibility", priority = 40)]
        public static void ValidateMenu() => Debug.Log(Run());

        public static string Run()
        {
            if (EditorSceneManager.GetActiveScene().path != DamageLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(DamageLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var model = Object.FindAnyObjectByType<VehicleDamageModel>();
            var sb = new StringBuilder("[DamageLab] Validation\n");
            model.Initialize();
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = new List<Vector3[]>();
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) { baseVerts.Add(f.sharedMesh.vertices); baseNormals.Add(f.sharedMesh.normals); }

                // 1 + 3: random walk then reset
                var rng = new System.Random(1234);
                for (int i = 0; i < 200; i++)
                    model.SetDamageImmediate(rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101));
                model.SetDamageImmediate(0, 0, 0, 0);
                Compare(filters, baseVerts, baseNormals, out float maxPos, out float maxNrm);
                sb.AppendLine($"  After 200 random combined states -> reset to 0: max vertex error {maxPos:E2} m, max normal error {maxNrm:E2} {(maxPos == 0f && maxNrm == 0f ? "PASS (exact)" : "FAIL")}");

                // 2: path independence
                model.SetDamageImmediate(0, 0, 20, 0);
                var direct = Snapshot(filters);
                model.SetDamageImmediate(0, 0, 60, 0);
                model.SetDamageImmediate(0, 0, 20, 0);
                float pathErr = MaxDiff(direct, Snapshot(filters));
                sb.AppendLine($"  Left 20 directly vs. 20 -> 60 -> 20: max difference {pathErr:E2} m {(pathErr == 0f ? "PASS (identical)" : "FAIL")}");

                model.SetDamageImmediate(80, 0, 60, 0);
                var combo = Snapshot(filters);
                model.SetDamageImmediate(0, 100, 0, 100);
                model.SetDamageImmediate(80, 0, 60, 0);
                float comboErr = MaxDiff(combo, Snapshot(filters));
                sb.AppendLine($"  Front 80 + Left 60 reached by two different paths: max difference {comboErr:E2} m {(comboErr == 0f ? "PASS (identical)" : "FAIL")}");

                // displacement stats at a few levels (how much the body actually moves)
                foreach (var level in new[] { 25, 50, 75, 100 })
                {
                    model.SetDamageImmediate(0, 0, level, 0);
                    float maxDisp = MaxDiff(baseVerts, Snapshot(filters));
                    model.SetDamageImmediate(level, 0, 0, 0);
                    float maxDispF = MaxDiff(baseVerts, Snapshot(filters));
                    sb.AppendLine($"  Damage {level,3}: max vertex displacement  Left {maxDisp:0.000} m   Front {maxDispF:0.000} m");
                }
                float total = 0f;
                for (int i = 0; i < 20; i++)
                {
                    model.SetDamageImmediate(70 + (i % 2), 50, 60, 40);
                    total += model.LastDeformMilliseconds;
                }
                model.SetDamageImmediate(0, 0, 0, 0);
                sb.AppendLine($"  Deformable vertices: {model.DeformableVertices:N0}; full rebuild with all four directions active: {total / 20f:0.0} ms average (editor)");
            }
            finally
            {
                model.Shutdown();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Click-to-damage checks, all through the real picking path (Physics.Raycast against the
        /// static picking colliders): direction, locality, misses, limit, clear/reset, determinism, sanity.
        /// </summary>
        public static string RunImpacts()
        {
            if (EditorSceneManager.GetActiveScene().path != DamageLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(DamageLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var model = Object.FindAnyObjectByType<VehicleDamageModel>();
            var sb = new StringBuilder("[DamageLab] Impact validation\n");
            model.Initialize();
            var root = model.transform;
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = new List<Vector3[]>();
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) { baseVerts.Add(f.sharedMesh.vertices); baseNormals.Add(f.sharedMesh.normals); }
                Bounds b = model.BodyBounds;
                sb.AppendLine($"  Body bounds (vehicle space): center {b.center.ToString("F2")}, size {b.size.ToString("F2")}");

                // Rays defined in vehicle space: start outside the car, aim at a point on/inside the body.
                Ray VRay(Vector3 targetFrac, Vector3 dir)
                {
                    Vector3 target = b.center + Vector3.Scale(b.extents, targetFrac);
                    Vector3 d = dir.normalized;
                    return new Ray(root.TransformPoint(target - d * 6f), root.TransformDirection(d));
                }
                var cases = new (string name, Ray ray, Vector3 expected)[]
                {
                    ("Door from the side", VRay(new Vector3(-1f, 0.05f, -0.08f), Vector3.right), Vector3.right),
                    ("Hood from above", VRay(new Vector3(0.38f, 0.3f, 0.5f), Vector3.down), Vector3.down),
                    ("Grille from the front", VRay(new Vector3(0f, -0.15f, 1f), Vector3.back), Vector3.back),
                    ("Front-left corner, angled (back + down + in)", VRay(new Vector3(-0.85f, 0.1f, 0.92f), new Vector3(0.45f, -0.55f, -0.7f)), new Vector3(0.45f, -0.55f, -0.7f).normalized),
                    ("Roof from above", VRay(new Vector3(0f, 0.6f, -0.12f), Vector3.down), Vector3.down),
                };
                foreach (var c in cases)
                {
                    model.ClearImpacts();
                    bool ok = model.TryApplyImpact(c.ray, 0.7f, out string msg);
                    if (!ok) { sb.AppendLine($"  {c.name}: MISS ({msg}) FAIL"); continue; }
                    var im = model.Impacts[0];
                    Vector3 sum = Vector3.zero; int moved = 0; float maxDisp = 0f, farthest = 0f;
                    var now = Snapshot(filters);
                    for (int i = 0; i < filters.Length; i++)
                        for (int j = 0; j < now[i].Length; j++)
                        {
                            Vector3 dv = root.InverseTransformVector(filters[i].transform.TransformVector(now[i][j] - baseVerts[i][j]));
                            float m = dv.magnitude;
                            if (m < 1e-3f) continue;
                            moved++;
                            sum += dv;
                            maxDisp = Mathf.Max(maxDisp, m);
                            if (m > 5e-3f)
                            {
                                Vector3 p = root.InverseTransformPoint(filters[i].transform.TransformPoint(baseVerts[i][j]));
                                farthest = Mathf.Max(farthest, (p - im.point).magnitude);
                            }
                        }
                    float align = sum.sqrMagnitude > 0f ? Vector3.Dot(sum.normalized, c.expected) : 0f;
                    sb.AppendLine($"  {c.name}: {msg} at {im.point.ToString("F2")}; mean push dir {sum.normalized.ToString("F2")} " +
                                  $"(alignment with ray {align:0.00}) {(align > 0.8f ? "PASS" : "FAIL")}; max disp {maxDisp:0.000} m; " +
                                  $"{moved:N0} verts moved >1 mm, none >5 mm beyond {farthest:0.00} m of the hit {(farthest < 1.2f ? "PASS (local)" : "FAIL")}");
                }

                // Misses and limits
                model.ClearImpacts();
                bool sky = model.TryApplyImpact(new Ray(root.TransformPoint(b.center + Vector3.up * 3f), Vector3.up), 1f, out string skyMsg);
                bool beside = model.TryApplyImpact(new Ray(root.TransformPoint(b.center + new Vector3(-6f, 0f, 0f)), root.TransformDirection(Vector3.forward)), 1f, out string besideMsg);
                bool zero = model.TryApplyImpact(cases[0].ray, 0f, out string zeroMsg);
                sb.AppendLine($"  Ray into empty sky: {(sky ? "HIT" : skyMsg)}; ray beside the car: {(beside ? "HIT" : besideMsg)}; strength 0 on the door: {(zero ? "APPLIED" : zeroMsg)} " +
                              $"{(!sky && !beside && !zero && model.ImpactCount == 0 ? "PASS" : "FAIL")}");
                for (int i = 0; i < 12; i++) model.TryApplyImpact(cases[i % cases.Length].ray, 0.5f, out _);
                bool eleventh = model.TryApplyImpact(cases[0].ray, 0.5f, out string limitMsg);
                sb.AppendLine($"  12 clicks + 1 more: {model.ImpactCount} stored, extra click -> \"{limitMsg}\" {(model.ImpactCount == model.maxImpacts && !eleventh ? "PASS" : "FAIL")}");

                // Stacking sanity: 10 x strength 100 on one spot, plus every slider at 100.
                model.ClearImpacts();
                model.TryApplyImpact(cases[0].ray, 1f, out _);
                float singleMax = MaxDiff(baseVerts, Snapshot(filters));
                CheckSanity(filters, baseVerts, out int nan1, out int flip1, out int tris);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 1; i < 10; i++) model.TryApplyImpact(cases[0].ray, 1f, out _);
                sw.Stop();
                float stackMax = MaxDiff(baseVerts, Snapshot(filters));
                CheckSanity(filters, baseVerts, out int nan10, out int flip10, out _);
                sb.AppendLine($"  Strength 100 on the door: max displacement {singleMax:0.000} m, flipped triangles {flip1}; 10 x on the same spot: {stackMax:0.000} m, " +
                              $"flipped {flip10} of {tris:N0}, NaN {nan1 + nan10}");
                sb.AppendLine($"  Cost per click (pick + one impact + rebuild): {sw.Elapsed.TotalMilliseconds / 9:0.0} ms (editor, multithreaded)");
                model.SetDamageImmediate(100, 100, 100, 100);
                CheckSanity(filters, baseVerts, out int nan, out int flipped, out _);
                float sliderMs = model.LastDeformMilliseconds;
                model.ClearImpacts();
                model.SetDamageImmediate(100, 100, 100, 100);
                CheckSanity(filters, baseVerts, out _, out int flippedSliders, out _);
                sb.AppendLine($"  All sliders 100 + those 10 impacts: NaN {nan}, flipped triangles {flipped} ({100f * flipped / tris:0.00}%) vs {flippedSliders} with the sliders alone; " +
                              $"slider rebuild with 10 cached impacts {sliderMs:0.0} ms");
                model.SetDamageImmediate(0, 0, 0, 0);

                // Clear Impacts keeps the sliders: F30 L50 + impacts -> clear == F30 L50 alone
                model.ClearImpacts();
                model.SetDamageImmediate(30, 0, 50, 0);
                var slidersOnly = Snapshot(filters);
                model.TryApplyImpact(cases[4].ray, 0.7f, out _);
                model.TryApplyImpact(cases[1].ray, 0.5f, out _);
                float withImpacts = MaxDiff(slidersOnly, Snapshot(filters));
                model.ClearImpacts();
                float clearErr = MaxDiff(slidersOnly, Snapshot(filters));
                sb.AppendLine($"  F30 L50 + roof/hood impacts (adds up to {withImpacts:0.000} m) -> Clear Impacts: diff to F30 L50 alone {clearErr:E2} m, sliders now F{model.front} L{model.left} " +
                              $"{(clearErr == 0f && model.front == 30f && model.left == 50f ? "PASS (identical)" : "FAIL")}");

                // Determinism: same sequence twice; same impacts in a different order
                model.SetDamageImmediate(0, 0, 0, 0);
                model.ClearImpacts();
                model.TryApplyImpact(cases[0].ray, 0.4f, out _); model.TryApplyImpact(cases[3].ray, 0.75f, out _); model.TryApplyImpact(cases[1].ray, 0.6f, out _);
                var seqA = Snapshot(filters);
                model.ClearImpacts();
                model.TryApplyImpact(cases[0].ray, 0.4f, out _); model.TryApplyImpact(cases[3].ray, 0.75f, out _); model.TryApplyImpact(cases[1].ray, 0.6f, out _);
                float repeatErr = MaxDiff(seqA, Snapshot(filters));
                model.ClearImpacts();
                model.TryApplyImpact(cases[1].ray, 0.6f, out _); model.TryApplyImpact(cases[0].ray, 0.4f, out _); model.TryApplyImpact(cases[3].ray, 0.75f, out _);
                float orderErr = MaxDiff(seqA, Snapshot(filters));
                sb.AppendLine($"  Same 3 clicks repeated: diff {repeatErr:E2} m {(repeatErr == 0f ? "PASS (identical)" : "FAIL")}; same clicks in another order: diff {orderErr:E3} m (expected: later hits on damaged metal spread wider, so order now matters)");

                // Reset All restores the original exactly, repeatedly
                float worst = 0f, worstN = 0f;
                var rng = new System.Random(99);
                for (int k = 0; k < 20; k++)
                {
                    model.SetDamageImmediate(rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101));
                    int n = rng.Next(1, 11);
                    for (int i = 0; i < n; i++) model.TryApplyImpact(cases[rng.Next(cases.Length)].ray, (float)rng.NextDouble(), out _);
                    model.ResetAll();
                    model.SetDamageImmediate(0, 0, 0, 0); // skip the slider easing
                    Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                    worst = Mathf.Max(worst, ep); worstN = Mathf.Max(worstN, en);
                }
                sb.AppendLine($"  20 x (random sliders + 1-10 random clicks -> Reset All): max vertex error {worst:E2} m, max normal error {worstN:E2}, impacts left {model.ImpactCount} " +
                              $"{(worst == 0f && worstN == 0f && model.ImpactCount == 0 ? "PASS (exact)" : "FAIL")}");
            }
            finally
            {
                model.ClearImpacts();
                model.SetDamageImmediate(0, 0, 0, 0);
                model.Shutdown();
            }
            return sb.ToString();
        }

        /// <summary>
        /// Saturation stress test: 1, 3 and 5 strength-100 clicks on the same spot (same ray) for several
        /// locations. Reports the deepest displacement, how far the damage spread, how much the metal
        /// around the wheel mounts moved, and folded triangles.
        /// </summary>
        public static string RunStress()
        {
            if (EditorSceneManager.GetActiveScene().path != DamageLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(DamageLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var model = Object.FindAnyObjectByType<VehicleDamageModel>();
            var sb = new StringBuilder("[DamageLab] Saturation stress test (strength 100, same ray)\n");
            model.Initialize();
            var root = model.transform;
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = new List<Vector3[]>();
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) { baseVerts.Add(f.sharedMesh.vertices); baseNormals.Add(f.sharedMesh.normals); }
                var wheels = new List<Vector3>();
                foreach (var f in filters)
                    if (f.name.Contains("Wheel")) wheels.Add(root.InverseTransformPoint(f.GetComponent<Renderer>().bounds.center));
                Bounds b = model.BodyBounds;
                Ray VRay(Vector3 frac, Vector3 dir)
                {
                    Vector3 target = b.center + Vector3.Scale(b.extents, frac);
                    Vector3 d = dir.normalized;
                    return new Ray(root.TransformPoint(target - d * 6f), root.TransformDirection(d));
                }
                var cases = new (string name, Ray ray)[]
                {
                    ("Door (side)", VRay(new Vector3(-1f, 0.05f, -0.08f), Vector3.right)),
                    ("Hood (above)", VRay(new Vector3(0.38f, 0.3f, 0.5f), Vector3.down)),
                    ("Front-left wheel arch (side, above)", VRay(new Vector3(-1f, 0.4f, 0.6f), new Vector3(0.8f, -0.6f, 0f))),
                    ("Roof (above)", VRay(new Vector3(0f, 0.6f, -0.12f), Vector3.down)),
                    ("Windshield base (front, above)", VRay(new Vector3(0f, 1.4f, 0.25f), new Vector3(0f, -0.6f, -0.8f))),
                    ("Front-left corner (angled)", VRay(new Vector3(-0.85f, 0.1f, 0.92f), new Vector3(0.45f, -0.55f, -0.7f))),
                };
                sb.AppendLine("  location                              hits  max disp  area>2cm  wheel-zone max  flipped tris");
                foreach (var c in cases)
                {
                    int done = 0;
                    foreach (int hits in new[] { 1, 3, 5 })
                    {
                        while (done < hits) { model.TryApplyImpact(c.ray, 1f, out _); done++; }
                        float maxDisp = 0f, wheelZone = 0f; int area = 0;
                        var now = Snapshot(filters);
                        for (int i = 0; i < filters.Length; i++)
                            for (int j = 0; j < now[i].Length; j++)
                            {
                                Vector3 dv = root.InverseTransformVector(filters[i].transform.TransformVector(now[i][j] - baseVerts[i][j]));
                                float m = dv.magnitude;
                                if (m < 1e-4f) continue;
                                maxDisp = Mathf.Max(maxDisp, m);
                                if (m > 0.02f) area++;
                                Vector3 p = root.InverseTransformPoint(filters[i].transform.TransformPoint(baseVerts[i][j]));
                                foreach (var w in wheels)
                                    if ((p - w).magnitude < 0.6f) wheelZone = Mathf.Max(wheelZone, m);
                            }
                        CheckSanity(filters, baseVerts, out int nan, out int flipped, out _);
                        sb.AppendLine($"  {c.name,-38}{hits,4}  {maxDisp,7:0.000} m {area,8:N0}  {wheelZone,10:0.000} m  {flipped,8}{(nan > 0 ? "  NaN!" : "")}");
                    }
                    model.ClearImpacts();
                }

                // Moderate single hits should still be generous.
                foreach (float s in new[] { 0.4f, 0.7f })
                {
                    model.ClearImpacts();
                    model.TryApplyImpact(cases[0].ray, s, out _);
                    sb.AppendLine($"  Single door hit at strength {s * 100:0}: max displacement {MaxDiff(baseVerts, Snapshot(filters)):0.000} m");
                }
                // Two locations, then combined with sliders, then reset.
                model.ClearImpacts();
                model.TryApplyImpact(cases[0].ray, 1f, out _);
                model.TryApplyImpact(cases[1].ray, 1f, out _);
                float two = MaxDiff(baseVerts, Snapshot(filters));
                model.SetDamageImmediate(60, 0, 50, 0);
                CheckSanity(filters, baseVerts, out int nanC, out int flipC, out _);
                model.ClearImpacts();
                CheckSanity(filters, baseVerts, out _, out int flipS, out _);
                sb.AppendLine($"  Door + hood (100 each): max {two:0.000} m; with Front 60 + Left 50: flipped {flipC} (sliders alone {flipS}), NaN {nanC}");
                model.TryApplyImpact(cases[3].ray, 1f, out _);
                model.ResetAll();
                model.SetDamageImmediate(0, 0, 0, 0);
                Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                sb.AppendLine($"  Reset All: max vertex error {ep:E2}, normal error {en:E2} {(ep == 0f && en == 0f ? "PASS (exact)" : "FAIL")}");
            }
            finally
            {
                model.ClearImpacts();
                model.SetDamageImmediate(0, 0, 0, 0);
                model.Shutdown();
            }
            return sb.ToString();
        }

        internal static void CheckSanity(MeshFilter[] filters, List<Vector3[]> baseVerts, out int nan, out int flipped, out int tris)
        {
            nan = flipped = tris = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                var mesh = filters[i].sharedMesh;
                var v = mesh.vertices;
                foreach (var p in v) if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) nan++;
                var t = mesh.triangles;
                var v0 = baseVerts[i];
                for (int k = 0; k < t.Length; k += 3)
                {
                    Vector3 a = Vector3.Cross(v0[t[k + 1]] - v0[t[k]], v0[t[k + 2]] - v0[t[k]]);
                    Vector3 c = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                    if (a.sqrMagnitude < 1e-12f) continue;
                    tris++;
                    if (Vector3.Dot(a, c) < 0f) flipped++;
                }
            }
        }

        static List<Vector3[]> Snapshot(MeshFilter[] filters)
        {
            var l = new List<Vector3[]>();
            foreach (var f in filters) l.Add(f.sharedMesh.vertices);
            return l;
        }

        static float MaxDiff(List<Vector3[]> a, List<Vector3[]> b)
        {
            float m = 0f;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < a[i].Length; j++)
                    m = Mathf.Max(m, (a[i][j] - b[i][j]).magnitude);
            return m;
        }

        static void Compare(MeshFilter[] filters, List<Vector3[]> verts, List<Vector3[]> normals, out float maxPos, out float maxNrm)
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
