using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Random Launch / Chaos Run validation (real PhysX, Physics.Simulate, anchored car): deterministic selections,
    /// safe placement against the current (crushed) hitbox, a seeded multi-impact run processed one block at a time,
    /// coverage, saturation, unhit regions still deformable, record merging, sanity, physics re-run vs replay,
    /// STOP, repair / reset and switching vehicles in the middle of a run.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        [System.Serializable] public class ChaosReplayEvidence { public int vehicle, seed; public LocalImpact[] impacts; }
        public static string ChaosEvidenceDirectory;

        struct ChaosStats { public double wallMs; public int steps; public float minQuietAtLaunch; public int pendingAtLaunch; public List<float> moved; }

        /// <summary>Run the lab's Chaos Run to completion the way the running lab does (physics, receiver, hitbox safe
        /// point, chaos tick each step), checking at every launch that the previous shot was fully processed.</summary>
        static ChaosStats RunChaos(int count, int maxSteps = 200000)
        {
            var st = new ChaosStats { moved = new List<float>(), minQuietAtLaunch = float.MaxValue };
            var w = Stopwatch.StartNew();
            lab.StartChaos(count);
            int done = 0;
            var before = CollisionLabValidation.Snapshot(filters);
            for (int i = 0; i < maxSteps && lab.ChaosRunning; i++)
            {
                physicsWatch.Restart();
                Physics.Simulate(Time.fixedDeltaTime);
                physicsWatch.Stop(); physicsMs += physicsWatch.Elapsed.TotalMilliseconds; physicsSteps++;
                receiver.Step(Time.fixedDeltaTime);
                lab.AfterPhysicsStep();
                lab.ChaosTick();
                st.steps++;
                if (lab.ChaosAttempted != done)
                {
                    done = lab.ChaosAttempted;
                    // A new block was just launched (or the run ended): the previous impact must be fully processed.
                    if (lab.hitbox.HasPending) st.pendingAtLaunch++;
                    var rec = lab.ChaosRecords[lab.ChaosRecords.Count - 1];
                    if (rec.outcome != CollisionLabController.ShotOutcome.Miss) st.minQuietAtLaunch = Mathf.Min(st.minQuietAtLaunch, rec.steps);
                    var now = CollisionLabValidation.Snapshot(filters);
                    st.moved.Add(CollisionLabValidation.MaxDiff(before, now));
                    before = now;
                }
            }
            st.wallMs = w.Elapsed.TotalMilliseconds;
            return st;
        }

        static void SimSteps(int n)
        {
            for (int i = 0; i < n; i++)
            {
                Physics.Simulate(Time.fixedDeltaTime);
                receiver.Step(Time.fixedDeltaTime);
                lab.AfterPhysicsStep();
                lab.ChaosTick();
            }
        }

        static string Octants(IEnumerable<float> angles)
        {
            var n = new int[8];
            foreach (var a in angles) n[Mathf.Clamp((int)(((a + 22.5f) % 360f) / 45f), 0, 7)]++;
            string[] names = { "F", "FR", "R", "RR", "B", "RL", "L", "FL" };
            return string.Join(" ", Enumerable.Range(0, 8).Select(i => $"{names[i]} {n[i]}"));
        }

        public static string RunRandom(int vehicle, int seed = 2026, int count = 50)
        {
            var sb = new StringBuilder();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(vehicle);
            physicsMs = 0; physicsSteps = 0;
            var opts = new RandomImpactOptions { varyWidth = true };
            var savedOpts = lab.randomOptions;
            bool allOk = true;
            try
            {
                var hb = lab.hitbox; var model = lab.model;
                hb.Setup();
                lab.speed = 20f; lab.mass = 2500f; lab.randomOptions = opts; lab.SetSeed(seed);
                sb.AppendLine($"[CollisionLab] Random impact validation - {lab.CurrentVehicle?.displayName}: seed {seed}, {count} shots, 20 m/s, 2500 kg, random block width (real PhysX, anchored)");

                // ------------------------------------------------ 1. selections are a pure function of the seed
                var planA = Enumerable.Range(0, count).Select(k => RandomImpact.Plan(seed, k, opts, false)).ToList();
                var planB = Enumerable.Range(0, count).Select(k => RandomImpact.Plan(seed, k, opts, true)).ToList();
                bool same = planA.Zip(planB, (a, b) => a.approachDeg == b.approachDeg && a.lateral == b.lateral && a.narrow == b.narrow && a.speedScale == b.speedScale).All(x => x);
                var other = RandomImpact.Plan(seed + 1, 0, opts, false);
                bool differs = other.approachDeg != planA[0].approachDeg;
                sb.AppendLine($"  1. Selections: generating shots 0-{count - 1} twice gives identical parameters {P(same)}; seed {seed + 1} gives a different sequence {P(differs)}");
                sb.AppendLine($"     first shots: {string.Join(" | ", planA.Take(4).Select(s => $"{s.approachDeg:0.0}Â° {s.lateral:+0.00;-0.00} {(s.narrow ? "N" : "W")}"))}");
                sb.AppendLine($"     approach octants: {Octants(planA.Select(s => s.approachDeg))}; lateral range {planA.Min(s => s.lateral):+0.00;-0.00}..{planA.Max(s => s.lateral):+0.00;-0.00}; narrow {planA.Count(s => s.narrow)}/{count}");
                allOk &= same && differs;

                // ------------------------------------------------ 2. placement on the undamaged car
                var touch0 = new float[count]; int overlap0 = 0, predicted0 = 0;
                for (int k = 0; k < count; k++)
                {
                    var rec = lab.PlaceRandom(planA[k]);
                    if (lab.BlockOverlapsCar()) overlap0++;
                    if (rec.predictedContact) predicted0++;
                    touch0[k] = rec.predictedContact ? Vector3.Dot(rec.aimPoint, -planA[k].Direction) : float.NaN;
                }
                lab.ResetBlock();
                sb.AppendLine($"  2. Placement (undamaged): parked block overlaps the car {overlap0}/{count} {P(overlap0 == 0)}; contact predicted for {predicted0}/{count}");
                allOk &= overlap0 == 0;

                // ------------------------------------------------ 3. the seeded chaos run
                var recorded = new List<LocalImpact>();
                int lastTotal = 0;
                System.Action recImp = () => { if (model.TotalImpactCount > lastTotal && model.ImpactCount > 0) recorded.Add(model.Impacts[model.ImpactCount - 1]); lastTotal = model.TotalImpactCount; };
                model.ImpactsChanged += recImp;
                hitboxMs.Clear(); deformMs.Clear();
                int clamps0 = hb.OutwardClamps;
                Vector3 carPos = lab.car.position;
                var stats = RunChaos(count);
                model.ImpactsChanged -= recImp;
                var recs = lab.ChaosRecords.ToList();
                var finalA = CollisionLabValidation.Snapshot(filters);
                bool completed = !lab.ChaosRunning && lab.ChaosAttempted == count;
                bool counts = lab.ChaosImpacts + lab.ChaosContactOnly + lab.ChaosMisses == count && model.TotalImpactCount == lab.ChaosImpacts;
                sb.AppendLine($"  3. Chaos run: completed {completed}, launched {lab.ChaosAttempted}, impacts {lab.ChaosImpacts}, contact only {lab.ChaosContactOnly}, missed {lab.ChaosMisses}; " +
                              $"impacts on car {model.TotalImpactCount} ({model.ImpactCount} stored, {model.MergedImpactCount} merged) {P(completed && counts)}");
                sb.AppendLine($"     one block at a time: hitbox update still pending at the next launch {stats.pendingAtLaunch} times {P(stats.pendingAtLaunch == 0)}; " +
                              $"fewest steps for a shot with contact {stats.minQuietAtLaunch:0} (>= {lab.chaosQuietSteps} quiet steps); car never repaired (impacts only grew) {P(model.TotalImpactCount == lab.ChaosImpacts)}");
                allOk &= completed && counts && stats.pendingAtLaunch == 0;
                var hits = recs.Where(r => r.outcome == CollisionLabController.ShotOutcome.Impact).ToList();
                sb.AppendLine($"     impacts by approach octant: {Octants(hits.Select(r => r.shot.approachDeg))}");
                sb.AppendLine($"     regions struck: {string.Join(", ", lab.ChaosRegions.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}"))}");
                var boxes = hits.Where(r => !string.IsNullOrEmpty(r.carCollider)).GroupBy(r => r.carCollider).OrderByDescending(g => g.Count()).ToList();
                sb.AppendLine($"     hitbox boxes struck {boxes.Count}/14: {string.Join(", ", boxes.Select(g => $"{g.Key.Replace("Box", "")} {g.Count()}"))}");
                bool coverage = lab.ChaosRegions.Count >= 6 && boxes.Count >= 8 && hits.Select(r => Mathf.Clamp((int)(((r.shot.approachDeg + 22.5f) % 360f) / 45f), 0, 7)).Distinct().Count() >= 6;
                sb.AppendLine($"     spread over the car (>= 6 regions, >= 8 boxes, >= 6 approach octants) {P(coverage)}");
                allOk &= coverage;
                foreach (var m in recs.Where(r => r.outcome != CollisionLabController.ShotOutcome.Impact).Take(6))
                    sb.AppendLine($"     not an impact: shot {m.shot.shot} {m.shot.approachDeg:0.0}Â° lateral {m.shot.lateral:+0.00;-0.00} {(m.shot.narrow ? "narrow" : "wide")} -> {m.outcome}{(m.outcome == CollisionLabController.ShotOutcome.ContactOnly ? $" (strength {m.strength * 100f:0}, {m.region})" : "")}, contact predicted {m.predictedContact}");

                // Saturation: regions hit several times move less each time.
                var byBox = new Dictionary<string, List<float>>();
                for (int k = 0; k < recs.Count && k < stats.moved.Count; k++)
                    if (recs[k].outcome == CollisionLabController.ShotOutcome.Impact && !string.IsNullOrEmpty(recs[k].carCollider))
                    {
                        if (!byBox.TryGetValue(recs[k].carCollider, out var l)) byBox[recs[k].carCollider] = l = new List<float>();
                        l.Add(stats.moved[k]);
                    }
                var sat = byBox.Where(x => x.Value.Count >= 4).Select(x => $"{x.Key.Replace("Box", "")} {x.Value[0]:0.00}->{x.Value[x.Value.Count - 1]:0.00} m").ToList();
                float firstAvg = byBox.Where(x => x.Value.Count >= 4).Select(x => x.Value[0]).DefaultIfEmpty(0).Average();
                float lastAvg = byBox.Where(x => x.Value.Count >= 4).Select(x => x.Value[x.Value.Count - 1]).DefaultIfEmpty(0).Average();
                sb.AppendLine($"     local saturation (boxes hit >= 4 times, mesh moved first -> last hit): {string.Join(", ", sat)}; mean {firstAvg:0.000} -> {lastAvg:0.000} m {P(sat.Count == 0 || lastAvg < firstAvg)}");
                allOk &= sat.Count == 0 || lastAvg < firstAvg;

                // Sanity after the run.
                DamageLabValidation.CheckSanity(filters, baseVerts, out int nan, out int flipped, out int tris);
                bool boxesOk = true; float retract = 0f;
                foreach (var r in hb.regions)
                    boxesOk &= r.box.size.x > 0.05f && r.box.size.y > 0.05f && r.box.size.z > 0.05f && !float.IsNaN(r.box.center.x + r.box.center.y + r.box.center.z);
                foreach (var f in hb.Faces) { boxesOk &= f.current <= f.original + 1e-5f; retract += f.original - f.current; }
                float drift = (lab.car.position - carPos).magnitude;
                sb.AppendLine($"     sanity: NaN {nan}, folded {flipped} of {tris:N0} ({100f * flipped / tris:0.0}%), boxes finite and only retracted {boxesOk} (total {retract * 100f:0} cm, outward moves blocked {hb.OutwardClamps - clamps0}), car drift {drift * 1000f:0.00} mm {P(nan == 0 && boxesOk && drift < 0.002f)}");
                allOk &= nan == 0 && boxesOk && drift < 0.002f;

                // ------------------------------------------------ 4. placement against the crushed car
                int overlap1 = 0, deeper = 0, compared = 0;
                for (int k = 0; k < count; k++)
                {
                    var rec = lab.PlaceRandom(planA[k]);
                    if (lab.BlockOverlapsCar()) overlap1++;
                    if (rec.predictedContact && !float.IsNaN(touch0[k]))
                    {
                        compared++;
                        if (Vector3.Dot(rec.aimPoint, -planA[k].Direction) < touch0[k] - 0.02f) deeper++;
                    }
                }
                lab.ResetBlock();
                sb.AppendLine($"  4. Placement (after {count} shots): parked block overlaps the crushed car {overlap1}/{count} {P(overlap1 == 0)}; " +
                              $"first touch now deeper than on the undamaged car for {deeper}/{compared} lines (aims at the current hitbox)");
                allOk &= overlap1 == 0;

                // ------------------------------------------------ 5. unhit / least-hit regions still deform
                // "Least hit" by actual damage: the regions whose physical faces retracted least (a box struck once can
                // still be crushed by wide hits on its neighbours, and then it is rightly saturated).
                var least = Targets.Take(12).OrderBy(t => RegionRetract(hb, t.region)).Take(3).ToList();
                bool fresh = true;
                foreach (var t in least)
                {
                    int n0 = byBox.TryGetValue(t.region + "Box", out var l) ? l.Count : 0;
                    float ret = RegionRetract(hb, t.region);
                    float m = HitMoved(t, 20f, 2500f, true, out var r);
                    bool ok = r.applied && m > 0.08f;
                    fresh &= ok;
                    sb.AppendLine($"  5. Least-damaged region {t.region} (retracted {ret * 100f:0} cm, its box struck {n0}x): manual narrow hit moved the mesh {m:0.000} m {P(ok)}");
                }
                allOk &= fresh;

                // ------------------------------------------------ 6. reproducibility: selections vs physics
                lab.ResetAll();
                foreach (var im in recorded) model.AddImpact(im);
                float replay = CollisionLabValidation.MaxDiff(finalA, CollisionLabValidation.Snapshot(filters));
                lab.ResetAll();
                var stats2 = RunChaos(count);
                var recs2 = lab.ChaosRecords.ToList();
                bool sameSel = recs.Count == recs2.Count && recs.Zip(recs2, (a, b) => a.shot.approachDeg == b.shot.approachDeg && a.shot.lateral == b.shot.lateral && a.shot.narrow == b.shot.narrow).All(x => x);
                int sameOutcome = recs.Zip(recs2, (a, b) => a.outcome == b.outcome && a.region == b.region).Count(x => x);
                float physicsRerun = CollisionLabValidation.MaxDiff(finalA, CollisionLabValidation.Snapshot(filters));
                int firstDiverge = -1;
                for (int k = 0; k < Mathf.Min(stats.moved.Count, stats2.moved.Count); k++) if (Mathf.Abs(stats.moved[k] - stats2.moved[k]) > 1e-3f) { firstDiverge = k; break; }
                // What PhysX handed the deformation in the two runs: contact point and crush per shot.
                float worstIn = 0f; int worstK = -1; int firstInput = -1;
                for (int k = 0; k < Mathf.Min(recs.Count, recs2.Count); k++)
                {
                    float dIn = (recs[k].contactPoint - recs2[k].contactPoint).magnitude + Mathf.Abs(recs[k].crush - recs2[k].crush);
                    if (firstInput < 0 && dIn > 1e-3f) firstInput = k;
                    if (dIn > worstIn) { worstIn = dIn; worstK = k; }
                }
                string inputs = firstInput < 0 ? "every shot's contact point and crush matched within 1 mm"
                    : $"first input difference > 1 mm at shot {firstInput + 1}: footprint centre {recs[firstInput].contactPoint.ToString("F3")} vs {recs2[firstInput].contactPoint.ToString("F3")}, crush {recs[firstInput].crush:0.000} vs {recs2[firstInput].crush:0.000} m; largest at shot {worstK + 1} ({worstIn * 1000f:0.0} mm)";
                sb.AppendLine($"  6. Same seed again: identical random selections for all {recs2.Count} shots {P(sameSel)} (exact: they depend only on the seed)");
                sb.AppendLine($"     replay of the {recorded.Count} recorded impacts into a repaired car: mesh difference {replay:E1} m {P(replay < 1e-5f)} (deformation is exact for a given impact sequence)");
                sb.AppendLine($"     physics re-run of the whole run: same outcome and region {sameOutcome}/{count} shots, final mesh difference {physicsRerun * 1000f:0.0} mm, " +
                              $"first shot whose mesh change differs by > 1 mm: {(firstDiverge < 0 ? "none" : (firstDiverge + 1).ToString())} (PhysX is not bit-repeatable across runs; informational)");
                sb.AppendLine($"     physics inputs between the two runs: {inputs}");
                // The deformation-repeatability check proper: perturb every recorded impactor by 0.1 mm and replay
                // (footprints re-resolved on the replayed surface). The pre-fix crush function amplified this to 100s of mm.
                string evidence = ChaosEvidenceDirectory;
                if (!string.IsNullOrEmpty(evidence))
                {
                    System.IO.Directory.CreateDirectory(evidence);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, $"vehicle{vehicle}_seed{seed}_recorded.json"),
                        JsonUtility.ToJson(new ChaosReplayEvidence { vehicle = vehicle, seed = seed, impacts = recorded.ToArray() }, true));
                }
                var refRe = new List<List<Vector3[]>>();
                ReplayImpacts(recorded, true, Vector3.zero, null, refRe, null, out _);
                float worstShift = 0f;
                foreach (var shift in new[] { new Vector3(1e-4f, 0f, 0f), new Vector3(0f, 0f, 1e-4f) })
                {
                    var dB = ReplayImpacts(recorded, true, shift, refRe, null, null, out var worstParts);
                    if (!string.IsNullOrEmpty(evidence))
                        System.IO.File.WriteAllText(System.IO.Path.Combine(evidence, $"vehicle{vehicle}_shift{(shift.x > 0 ? "x" : "z")}.txt"),
                            Summ(dB, worstParts) + "\n" + string.Join("\n", dB.Select((d, k) => $"{k + 1}: {d * 1000f:F6} mm {(worstParts[k] >= 0 ? filters[worstParts[k]].name : "exact")}")));
                    worstShift = Mathf.Max(worstShift, dB.Max());
                }
                sb.AppendLine($"     0.1 mm perturbation of every one of the {recorded.Count} impactors (replayed): worst mesh difference {worstShift * 1000f:0.0} mm {P(worstShift < 0.02f)} (no amplification regression; pre-fix this was 100s of mm)");
                allOk &= sameSel && replay < 1e-5f && worstShift < 0.02f;
                lab.ResetAll();

                // ------------------------------------------------ 7. STOP keeps the damage and ends after the current impact
                lab.ResetAll();
                lab.StartChaos(10);
                SimSteps(40);                       // a block in flight
                int impactsBefore = model.TotalImpactCount;
                int inFlight = lab.ChaosAttempted + 1;
                lab.StopChaos();
                bool stillRunning = lab.ChaosRunning;
                SimSteps(3000);
                bool stopped = !lab.ChaosRunning && lab.ChaosAttempted == inFlight && model.TotalImpactCount >= impactsBefore && model.TotalImpactCount > 0;
                bool parked = lab.sled.isKinematic && !lab.BlockOverlapsCar();
                sb.AppendLine($"  7. STOP during shot {inFlight}: the block in flight finishes first ({stillRunning}), the run ends after {lab.ChaosAttempted} launch(es), no further launch; damage kept ({model.TotalImpactCount} impacts, not repaired); block parked clear of the car {parked} {P(stopped && parked)}");
                allOk &= stopped && parked;

                // ------------------------------------------------ 8. repair / reset after a run
                lab.RepairCar();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                bool faces0 = hb.Faces.All(f => f.current == f.original);
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep2, out float en2);
                sb.AppendLine($"  8. Repair Car: mesh {ep:E1}/{en:E1}, hitbox original {faces0}; Reset All: mesh {ep2:E1}/{en2:E1} {P(ep == 0f && en == 0f && faces0 && ep2 == 0f && en2 == 0f)}");
                allOk &= ep == 0f && en == 0f && faces0 && ep2 == 0f && en2 == 0f;

                // ------------------------------------------------ 9. switching vehicles in the middle of a run
                lab.StartChaos(10);
                SimSteps(25);
                var oldCar = lab.car.gameObject;
                int target = vehicle == 0 ? 1 : 0;
                lab.SelectVehicle(target);
                Adopt();
                bool switchOk = !lab.ChaosRunning && oldCar == null && lab.model.TotalImpactCount == 0 && lab.sled.isKinematic && !lab.BlockOverlapsCar();
                SimSteps(200);
                switchOk &= lab.model.TotalImpactCount == 0;    // nothing from the stopped run reaches the new car
                sb.AppendLine($"  9. Switching to {lab.CurrentVehicle.displayName} during a run: run stopped, old car gone, new car undamaged and stays undamaged, block parked {P(switchOk)}");
                allOk &= switchOk;
                lab.SelectVehicle(vehicle);
                Adopt();
                baseVerts = CollisionLabValidation.Snapshot(filters);

                // ------------------------------------------------ performance
                hitboxMs.Sort();
                var deform = hits.Select(r => r.deformMs).ToList();
                sb.AppendLine($"  PERFORMANCE (editor): {count}-shot run {stats.wallMs / 1000.0:0.0} s wall time for {stats.steps} physics steps ({stats.steps / (float)count:0} steps = {stats.steps * Time.fixedDeltaTime / count:0.00} s simulated per shot); " +
                              $"deformation {deform.DefaultIfEmpty(0).Average():0.0} ms avg / {deform.DefaultIfEmpty(0).Max():0.0} ms max per impact; " +
                              $"hitbox update {recs.Where(r => r.hitboxMs > 0).Select(r => r.hitboxMs).DefaultIfEmpty(0).Average():0.00} ms avg; physics step {physicsMs / Mathf.Max(1, physicsSteps):0.000} ms avg");
                sb.AppendLine($"  Random impact validation {P(allOk)}");
            }
            finally
            {
                lab.randomOptions = savedOpts;
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
