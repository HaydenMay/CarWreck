using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Per-vehicle integration check (any VehicleDefinition): classification warnings, collision smoke tests from every
    /// side, repeated hits, saturation, Damage Lab paths (directional sliders, click-style dents), repair/reset,
    /// folded triangles per component, layered-part drift, and the rebuild optimisation (exactness + before/after cost
    /// on an identical recorded impact sequence). Real PhysX in the Collision Lab scene, anchored car.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static string RunVehicle(int vehicle, bool legacyConfiguration = false, bool screenshots = true)
        {
            var sb = new StringBuilder();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            VehicleDefinition def = null; List<VehiclePartRule> savedRules = null; VehicleDamageProfile savedProfile = null;
            float[] savedDims = null;
            Open(vehicle);
            try
            {
                def = lab.vehicles[vehicle];
                if (legacyConfiguration)
                {
                    // The assessment's starting point: muscle-car rules (substring-era names), muscle profile, muscle hitbox sizes.
                    savedRules = def.components; savedProfile = def.profile;
                    savedDims = new[] { def.frontSectionDepth, def.rearSectionDepth, def.sideSectionDepth, def.impactHeight };
                    def.components = VehiclePartRule.Defaults().Select(r => { r.match = NameMatch.Contains; return r; }).ToList();   // old substring matching (Underbody matched Body)
                    def.profile = lab.vehicles[0].profile;
                    def.frontSectionDepth = def.rearSectionDepth = 0.7f; def.sideSectionDepth = 0.45f; def.impactHeight = 0.7f;
                    lab.SelectVehicle(vehicle);
                    receiver = lab.receiver;
                    filters = lab.model.GetComponentsInChildren<MeshFilter>();
                    baseVerts = CollisionLabValidation.Snapshot(filters);
                    baseNormals = new List<Vector3[]>(); foreach (var f in filters) baseNormals.Add(f.sharedMesh.normals);
                    receiver.logImpacts = false;
                }
                var model = lab.model; var hb = lab.hitbox; var g = lab.Geometry;
                string tag = (legacyConfiguration ? "legacy_" : "") + def.name;
                sb.AppendLine($"[CollisionLab] Vehicle integration: {def.displayName}{(legacyConfiguration ? " WITH LEGACY (muscle-car) CONFIGURATION" : "")} (real PhysX, anchored)");
                sb.AppendLine($"  model: {model.PartCount} deforming parts, {model.DeformableVertices:N0} deforming vertices, {model.TotalTriangles:N0} triangles, {g.wheelCenters?.Length ?? 0} wheels at {string.Join(" ", (g.wheelCenters ?? new Vector3[0]).Select(w => w.ToString("F2")))}");
                sb.AppendLine($"  geometry: body {g.body.size.ToString("F3")} (min {g.body.min.ToString("F2")} max {g.body.max.ToString("F2")}), beltline {g.Beltline:0.000}, cabin top {g.cabin.max.y:0.000}, lift {g.groundLift:0.000}; " +
                              $"profile cabin line {model.profile.roofLineStart:0.00}-{model.profile.roofLineEnd:0.00}; crush-field bounds {model.BodyBounds.size.ToString("F3")} centre y {model.BodyBounds.center.y:0.000}");
                sb.AppendLine($"  physics: {lab.car.GetComponentsInChildren<Collider>().Length} boxes; nose/tail depth {def.FrontDepth(g.body):0.00}/{def.RearDepth(g.body):0.00} m, side {def.SideDepth(g.body):0.00} m, impact height {lab.sledHeight:0.00} m; " +
                              $"faces {string.Join("; ", hb.Faces.Select(f => $"{f.Label} {f.sign * f.original:0.000}"))}");
                sb.AppendLine($"  validation warnings: {lab.VehicleWarnings.Count}{(lab.VehicleWarnings.Count > 0 ? "\n    - " + string.Join("\n    - ", lab.VehicleWarnings.Take(12)) + (lab.VehicleWarnings.Count > 12 ? $"\n    - ... {lab.VehicleWarnings.Count - 12} more" : "") : "")}");
                var layers = LayerPairs(model);
                if (screenshots && !legacyConfiguration) VShots(tag + "_00_undamaged");

                var deform = new List<float>();
                string HitLine(string name, CollisionDamageReceiver.Report r, List<Vector3[]> before)
                {
                    float moved = CollisionLabValidation.MaxDiff(before, CollisionLabValidation.Snapshot(filters));
                    DamageLabValidation.CheckSanity(filters, baseVerts, out int nan, out int flip, out int tris);
                    var (sepMax, sepP95, open) = LayerSeparation(model, layers);
                    if (r.applied) deform.Add(r.deformMilliseconds);
                    return $"    {name,-30} applied {(r.applied ? "yes" : "NO "),-3} str {r.strength * 100f,5:0.0} contact {r.contactPoint.ToString("F2")} moved {moved:0.000} m " +
                           $"deform {r.deformMilliseconds,5:0.0} ms (resolve {model.LastResolveMilliseconds:0.0}, accumulate {model.LastAccumulateMilliseconds:0.0} [{model.LastImpactPartsEvaluated}/{model.PartCount}], rebuild {model.LastDeformMilliseconds:0.0} [{model.LastRebuiltParts}/{model.PartCount}]) " +
                           $"NaN {nan} folded {flip} ({100f * flip / Mathf.Max(1, tris):0.00}%) layer drift p95 {sepP95 * 100:0.0} max {sepMax * 100:0.0} cm | hitbox: {hb.LastChangeSummary}";
                }

                // ---------------------------------------------------------------- single impacts from every side
                sb.AppendLine("  SINGLE IMPACTS (wide sled, 15 m/s, 1500 kg, from a repaired car):");
                bool allApplied = true;
                foreach (var d in new[] { ImpactDirection.Front, ImpactDirection.Rear, ImpactDirection.LeftSide, ImpactDirection.RightSide, ImpactDirection.FrontCornerAngled, ImpactDirection.RearCornerAngled })
                {
                    lab.RepairCar();
                    var before = CollisionLabValidation.Snapshot(filters);
                    var r = Hit(d, 15f, 1500f, false, 0f, out _);
                    allApplied &= r.applied;
                    sb.AppendLine(HitLine(CollisionLabController.Label(d), r, before));
                    sb.AppendLine($"      folded by part: {PartFlips(filters, baseVerts)}");
                    if (screenshots && !legacyConfiguration && (d == ImpactDirection.Front || d == ImpactDirection.LeftSide || d == ImpactDirection.RearCornerAngled))
                        VShots($"{tag}_01_{d}");
                }

                // ---------------------------------------------------------------- repeated + cumulative + saturation
                lab.RepairCar();
                sb.AppendLine("  REPEATED narrow front hits (12 m/s, same start pose) - contact must move into the dent:");
                float c0 = float.NaN, cPrev = float.MaxValue; bool deeper = true;
                for (int k = 0; k < 3; k++)
                {
                    var before = CollisionLabValidation.Snapshot(filters);
                    var r = Hit(ImpactDirection.Front, 12f, 1500f, true, 0f, out _);
                    if (k > 0) deeper &= r.contactPoint.z < cPrev - 0.02f;
                    cPrev = r.contactPoint.z; if (k == 0) c0 = r.contactPoint.z;
                    sb.AppendLine("  " + HitLine($"hit {k + 1} (FrontCentre face {hb.FaceOf("FrontCentre", 2, 1).current:0.000})", r, before));
                }
                sb.AppendLine($"    contact deepens each hit {P(deeper)} ({c0:0.000} -> {cPrev:0.000})");
                var recorded = new List<LocalImpact>();
                Action rec = () => { if (model.ImpactCount > 0) recorded.Add(model.Impacts[model.ImpactCount - 1]); };
                foreach (var im in model.Impacts) recorded.Add(im);
                model.ImpactsChanged += rec;
                sb.AppendLine("  CUMULATIVE (no repair, 20 m/s):");
                foreach (var t in new (ImpactDirection d, bool n, float o)[] { (ImpactDirection.LeftSide, false, 0f), (ImpactDirection.RightSide, true, 0.15f), (ImpactDirection.Rear, true, -0.6f), (ImpactDirection.RearCornerAngled, false, 0f), (ImpactDirection.FrontCornerAngled, false, 0f), (ImpactDirection.Front, true, 0.6f) })
                {
                    var before = CollisionLabValidation.Snapshot(filters);
                    var r = Hit(t.d, 20f, 1500f, t.n, t.o, out _);
                    allApplied &= r.applied;
                    sb.AppendLine(HitLine($"{t.d} {(t.n ? "narrow" : "wide")} {t.o:+0.0;-0.0}", r, before));
                }
                model.ImpactsChanged -= rec;
                sb.AppendLine($"    after 9 hits, folded by part: {PartFlips(filters, baseVerts)}; worst layer drift: {DriftPairs(model, layers)}");
                var damaged = CollisionLabValidation.Snapshot(filters);
                if (screenshots) VShots($"{tag}_02_cumulative_9");
                bool uvSame = model.Parts.All(p => p.RuntimeMesh == null || (p.RuntimeMesh.uv.SequenceEqual(p.sourceMesh.uv) && p.RuntimeMesh.uv2.SequenceEqual(p.sourceMesh.uv2)));
                sb.AppendLine($"  stripe/paint UVs unchanged by deformation (runtime meshes vs source) {P(uvSame)}");
                lab.RepairCar();
                var sat = new List<float>();
                for (int k = 0; k < 12; k++)
                {
                    var before = CollisionLabValidation.Snapshot(filters);
                    var r = Hit(ImpactDirection.Front, 30f, 4000f, true, 0f, out _);
                    allApplied &= r.applied;
                    sat.Add(CollisionLabValidation.MaxDiff(before, CollisionLabValidation.Snapshot(filters)));
                }
                DamageLabValidation.CheckSanity(filters, baseVerts, out int sNan, out int sFlip, out int sTris);
                sb.AppendLine($"  SATURATION front centre x12 at 30 m/s 4000 kg: moved per hit {sat[0]:0.000} / {sat[1]:0.000} / {sat[2]:0.000} ... {sat[9]:0.000} / {sat[10]:0.000} / {sat[11]:0.000} m, " +
                              $"max disp {Disp():0.000} m, FrontCentre retracted {(hb.FaceOf("FrontCentre", 2, 1).original - hb.FaceOf("FrontCentre", 2, 1).current) * 100f:0.0} cm, NaN {sNan}, folded {100f * sFlip / sTris:0.0}% " +
                              $"{P(sat[11] < 0.06f && sNan == 0)}");
                sb.AppendLine($"  every hit applied {P(allApplied)}; deformation per hit {(deform.Count > 0 ? deform.Average() : 0):0.0} ms avg / {(deform.Count > 0 ? deform.Max() : 0):0.0} ms max over {deform.Count}");

                // ---------------------------------------------------------------- Damage Lab paths
                lab.RepairCar();
                sb.AppendLine("  DAMAGE LAB PATHS:");
                foreach (var s in new (string n, float f, float r, float l, float rt)[] { ("slider front 100", 100, 0, 0, 0), ("slider rear 100", 0, 100, 0, 0), ("slider left 100", 0, 0, 100, 0), ("all sliders 100", 100, 100, 100, 100) })
                {
                    model.SetDamageImmediate(s.f, s.r, s.l, s.rt);
                    DamageLabValidation.CheckSanity(filters, baseVerts, out int nan, out int flip, out int tris);
                    var (sepMax, sepP95, open) = LayerSeparation(model, layers);
                    sb.AppendLine($"    {s.n,-18} max disp {Disp():0.000} m, rebuild {model.LastDeformMilliseconds:0.0} ms, NaN {nan}, folded {flip} ({100f * flip / Mathf.Max(1, tris):0.00}%), layer drift p95 {sepP95 * 100:0.0} max {sepMax * 100:0.0} cm");
                    if (s.n == "all sliders 100") { sb.AppendLine($"      folded by part: {PartFlips(filters, baseVerts)}"); if (screenshots && !legacyConfiguration) VShots($"{tag}_03_all_sliders_100"); }
                }
                model.SetDamageImmediate(0, 0, 0, 0);
                Bounds bb = g.body, cab = g.cabin;
                foreach (var c in new (string n, Vector3 p, Vector3 d)[]
                {
                    ("hood from above", new Vector3(bb.center.x + 0.25f, cab.max.y + 2f, bb.max.z - 0.13f * bb.size.z), Vector3.down),
                    ("roof from above", new Vector3(cab.center.x, cab.max.y + 2f, cab.center.z), Vector3.down),
                    ("door from the side", new Vector3(bb.min.x - 2f, bb.center.y + 0.1f, bb.center.z - 0.1f), Vector3.right),
                    ("rear from behind/above", new Vector3(bb.center.x, cab.max.y + 1.2f, bb.min.z - 1.4f), new Vector3(0, -0.6f, 0.8f).normalized),
                })
                {
                    model.ClearImpacts();
                    bool hit = model.SurfacePointAlong(c.p, c.d, out Vector3 sp, 0.12f, 0f);
                    bool ok = hit && model.AddImpact(new LocalImpact { point = sp, direction = c.d, normal = -c.d, strength = 0.8f });
                    DamageLabValidation.CheckSanity(filters, baseVerts, out int nan, out int flip, out int tris);
                    sb.AppendLine($"    dent {c.n,-24} at {(hit ? sp.ToString("F2") : "MISSED")} applied {ok} moved {Disp():0.000} m, NaN {nan}, folded {flip} [{PartFlips(filters, baseVerts)}]");
                }
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                sb.AppendLine($"  Reset All: vertex error {ep:E1}, normal error {en:E1} {P(ep == 0f && en == 0f)}");

                // ---------------------------------------------------------------- rebuild optimisation: exactness and cost on the same recorded impacts
                if (!legacyConfiguration)
                {
                    float Replay(bool optimised, out List<Vector3[]> mesh, out float evaluated, out float rebuilt)
                    {
                        VehicleDamageModel.CullImpactsByBounds = optimised;
                        VehicleDamageModel.SkipUnchangedParts = optimised;
                        lab.ResetAll();
                        var w = Stopwatch.StartNew(); float ev = 0, rb = 0;
                        foreach (var im in recorded) { model.AddImpact(im); ev += model.LastImpactPartsEvaluated; rb += model.LastRebuiltParts; }
                        w.Stop();
                        mesh = CollisionLabValidation.Snapshot(filters);
                        evaluated = ev / recorded.Count; rebuilt = rb / recorded.Count;
                        return (float)w.Elapsed.TotalMilliseconds / recorded.Count;
                    }
                    Replay(true, out _, out _, out _);   // warm-up
                    float tFull = Replay(false, out var meshFull, out float evFull, out float rbFull);
                    float tOpt = Replay(true, out var meshOpt, out float evOpt, out float rbOpt);
                    VehicleDamageModel.CullImpactsByBounds = true; VehicleDamageModel.SkipUnchangedParts = true;
                    float diff = CollisionLabValidation.MaxDiff(meshFull, meshOpt);
                    sb.AppendLine($"  REBUILD OPTIMISATION ({recorded.Count} recorded impacts replayed): before {tFull:0.0} ms/impact (parts evaluated {evFull:0.0}, rebuilt {rbFull:0.0} of {model.PartCount}); " +
                                  $"after {tOpt:0.0} ms/impact (evaluated {evOpt:0.0}, rebuilt {rbOpt:0.0}); identical mesh: max difference {diff:E1} m {P(diff == 0f)}; matches the live run {CollisionLabValidation.MaxDiff(damaged, meshOpt):E1} m");
                    lab.ResetAll();
                }
            }
            finally
            {
                VehicleDamageModel.CullImpactsByBounds = true; VehicleDamageModel.SkipUnchangedParts = true;
                if (savedRules != null)
                {
                    def.components = savedRules; def.profile = savedProfile;
                    def.frontSectionDepth = savedDims[0]; def.rearSectionDepth = savedDims[1]; def.sideSectionDepth = savedDims[2]; def.impactHeight = savedDims[3];
                }
                Close(prev);
            }
            return sb.ToString();
        }

        static void VShots(string name)
        {
            HideSled(true);
            Vector3 pivot = lab.car.position + Vector3.up * 0.6f;
            CollisionLabValidation.Shot(lab, lab.cameraController, "70_" + name + "_FL", 135f, 16f, 7.2f, pivot);
            CollisionLabValidation.Shot(lab, lab.cameraController, "70_" + name + "_RR", 315f, 16f, 7.2f, pivot);
            HideSled(false);
        }

        // ------------------------------------------------------------------ folded triangles and layered parts
        static string PartFlips(MeshFilter[] fs, List<Vector3[]> baseV)
        {
            var l = new List<(int n, string s)>();
            for (int i = 0; i < fs.Length; i++)
            {
                var v = fs[i].sharedMesh.vertices; var t = fs[i].sharedMesh.triangles; var v0 = baseV[i]; int n = 0;
                for (int k = 0; k < t.Length; k += 3)
                {
                    Vector3 a = Vector3.Cross(v0[t[k + 1]] - v0[t[k]], v0[t[k + 2]] - v0[t[k]]);
                    Vector3 c = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                    if (a.sqrMagnitude > 1e-12f && Vector3.Dot(a, c) < 0f) n++;
                }
                if (n > 0) l.Add((n, $"{fs[i].name} {n}/{t.Length / 3}"));
            }
            return l.Count == 0 ? "none" : string.Join(", ", l.OrderByDescending(x => x.n).Take(7).Select(x => x.s));
        }

        struct LayerPair { public int pa, ia, pb, ib; public float d0; }

        static List<LayerPair> LayerPairs(VehicleDamageModel model)
        {
            var hash = new Dictionary<Vector3Int, List<(int p, int i)>>();
            Vector3Int K(Vector3 v) => new Vector3Int(Mathf.FloorToInt(v.x * 50f), Mathf.FloorToInt(v.y * 50f), Mathf.FloorToInt(v.z * 50f));
            for (int p = 0; p < model.Parts.Count; p++)
            {
                var pos = model.Parts[p].VehiclePositions;
                for (int i = 0; i < pos.Length; i++) { var k = K(pos[i]); if (!hash.TryGetValue(k, out var l)) hash[k] = l = new List<(int, int)>(); l.Add((p, i)); }
            }
            var pairs = new List<LayerPair>();
            for (int p = 0; p < model.Parts.Count; p++)
            {
                var pos = model.Parts[p].VehiclePositions;
                for (int i = 0; i < pos.Length; i += 2)
                {
                    var k = K(pos[i]); float best = 0.01f; (int p, int i) bi = (-1, -1);
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                        if (hash.TryGetValue(k + new Vector3Int(dx, dy, dz), out var l))
                            foreach (var q in l) { if (q.p == p) continue; float d = (model.Parts[q.p].VehiclePositions[q.i] - pos[i]).magnitude; if (d < best && d > 1e-5f) { best = d; bi = q; } }
                    if (bi.p >= 0) pairs.Add(new LayerPair { pa = p, ia = i, pb = bi.p, ib = bi.i, d0 = best });
                }
            }
            return pairs;
        }

        static (float max, float p95, int open) LayerSeparation(VehicleDamageModel model, List<LayerPair> pairs)
        {
            if (pairs.Count == 0) return (0, 0, 0);
            var d = new float[pairs.Count]; int open = 0;
            for (int k = 0; k < pairs.Count; k++)
            {
                var x = pairs[k];
                d[k] = Mathf.Abs((model.Parts[x.pa].DeformedVehiclePosition(x.ia) - model.Parts[x.pb].DeformedVehiclePosition(x.ib)).magnitude - x.d0);
                if (d[k] > 0.01f) open++;
            }
            Array.Sort(d);
            return (d[d.Length - 1], d[(int)(d.Length * 0.95f)], open);
        }

        static string DriftPairs(VehicleDamageModel model, List<LayerPair> pairs)
        {
            var g = new Dictionary<string, float>();
            foreach (var x in pairs)
            {
                float d = Mathf.Abs((model.Parts[x.pa].DeformedVehiclePosition(x.ia) - model.Parts[x.pb].DeformedVehiclePosition(x.ib)).magnitude - x.d0);
                string k = model.Parts[x.pa].filter.name + "~" + model.Parts[x.pb].filter.name;
                if (!g.TryGetValue(k, out float m) || d > m) g[k] = d;
            }
            return string.Join(", ", g.OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value * 100f:0.0}cm"));
        }
    }
}
