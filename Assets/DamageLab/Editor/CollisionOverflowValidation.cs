using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Regression for the "no damage after 24 impacts" bug: 30+ heavy hits (30 m/s, 4000 kg, anchored) with the impact
    /// history bounded at maxImpacts records and older records merged into the per-vertex damage sum.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        struct Shot30 { public string name; public ImpactDirection d; public bool narrow; public float offset; }

        static Shot30 S(string n, ImpactDirection d, bool narrow, float offset = 0f) => new Shot30 { name = n, d = d, narrow = narrow, offset = offset };

        // 1-25 only touch the front centre and the left side (both saturate); 26+ move to undamaged regions,
        // back to the saturated centre, and to the untouched front-left / front-right hitbox sections.
        static List<Shot30> Sequence()
        {
            var seq = new List<Shot30>();
            for (int i = 0; i < 25; i++)
                seq.Add(i % 2 == 0 ? S("front centre narrow", ImpactDirection.Front, true) : S("left side wide", ImpactDirection.LeftSide, false));
            seq.Add(S("front-left narrow (unhit section)", ImpactDirection.Front, true, -0.6f));   // 26
            seq.Add(S("right side wide (undamaged)", ImpactDirection.RightSide, false));          // 27
            seq.Add(S("rear wide (undamaged)", ImpactDirection.Rear, false));                       // 28
            seq.Add(S("front centre narrow (saturated)", ImpactDirection.Front, true));             // 29
            seq.Add(S("front-right narrow (unhit section)", ImpactDirection.Front, true, 0.6f));   // 30
            for (int i = 0; i < 3; i++) seq.Add(S("front centre narrow (saturated)", ImpactDirection.Front, true)); // 31-33
            return seq;
        }

        public static string RunOverflow(bool screenshots = true)
        {
            var sb = new StringBuilder("[CollisionLab] Impact-history overflow regression: 33 hits at 30 m/s, 4000 kg, anchored (real PhysX)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open();
            var model = lab.model;
            var hb = lab.hitbox;
            var colliderDebug = Object.FindAnyObjectByType<PhysicsHitboxDebug>();
            try
            {
                hb.Setup();
                sb.AppendLine($"  model: maxImpacts {model.maxImpacts} records, overflow {model.impactOverflow}");
                sb.AppendLine("   #  target                               applied  strength  this hit moved  total max disp  stored/merged/total  hitbox faces L/C/R       deform ms  diagnostics");
                var seq = Sequence();
                var recorded = new List<DamageLab.Deformation.LocalImpact>();
                var launchImpactsA = new List<int>(); var contactA = new List<float>();
                var fpA = new List<ImpactFootprintSnap>();
                int lastTotal = 0;
                System.Action rec = () => { if (model.TotalImpactCount > lastTotal && model.ImpactCount > 0) recorded.Add(model.Impacts[model.ImpactCount - 1]); lastTotal = model.TotalImpactCount; };
                model.ImpactsChanged += rec;
                int suppressedCount = 0;
                receiver.ImpactRegistered += r0 => { if (r0.suppressed) suppressedCount++; };
                var deformBefore = new List<float>(); var deformAfter = new List<float>();
                List<Vector3[]> finalA = null;
                bool allApplied = true, diagOk = true, hitboxOk = true, undamagedOk = true;
                float[] satMoves = new float[4]; int satIdx = 0;
                for (int i = 0; i < seq.Count; i++)
                {
                    var s = seq[i];
                    var before = CollisionLabValidation.Snapshot(filters);
                    float[] facesBefore = { hb.CurrentFront[0], hb.CurrentFront[1], hb.CurrentFront[2] };
                    receiver.logImpacts = false;
                    int t0 = model.TotalImpactCount;
                    var r = Hit(s.d, 30f, 4000f, s.narrow, s.offset, out _);
                    launchImpactsA.Add(model.TotalImpactCount - t0); contactA.Add(r.contactPoint.z); fpA.Add(new ImpactFootprintSnap(r));
                    receiver.logImpacts = false;
                    float moved = CollisionLabValidation.MaxDiff(before, CollisionLabValidation.Snapshot(filters));
                    lab.RefreshDiagnostics();
                    string diag = lab.diagnosticsText != null ? lab.diagnosticsText.text : "";
                    // The panel shows the latest damaging collision (normally this launch's hit; occasionally a second
                    // damaging contact in the same launch): it must show that hit, applied, never "not applied" / empty.
                    var shown = receiver.Last;
                    bool diagGood = r.applied && shown.applied && diag.Contains($"{shown.strength * 100f:0} / 100") && !diag.Contains("not applied") && !diag.Contains("No collision");
                    allApplied &= r.applied; diagOk &= diagGood;
                    (i < 24 ? deformBefore : deformAfter).Add(r.deformMilliseconds);
                    int n = i + 1;
                    if (n == 26) hitboxOk &= facesBefore[0] - hb.CurrentFront[0] > 0.05f;   // front-left retracts after the 25th
                    if (n == 30) hitboxOk &= facesBefore[2] - hb.CurrentFront[2] > 0.05f;   // front-right too
                    if (n == 27 || n == 28) undamagedOk &= moved > 0.15f;
                    if (n == 29 || n >= 31) satMoves[satIdx++] = moved;
                    if (n <= 3 || (n >= 23 && n <= 30) || n >= 31)
                        sb.AppendLine($"  {n,2}  {s.name,-36} {(r.applied ? "yes" : "NO "),5}  {r.strength * 100f,7:0.0}  {moved,10:0.000} m  {Disp(),10:0.000} m  " +
                                      $"{model.ImpactCount,6}/{model.MergedImpactCount,2}/{model.TotalImpactCount,2}       {hb.CurrentFront[0]:0.000}/{hb.CurrentFront[1]:0.000}/{hb.CurrentFront[2]:0.000}   " +
                                      $"{r.deformMilliseconds,6:0.0}   {(diagGood ? "ok" : "BAD")}");
                    if (screenshots && n == 30)
                    {
                        if (colliderDebug != null) colliderDebug.SetVisible(true);
                        Shots("58_Overflow_30_Hits_Front", 140f);
                        HideSled(true);
                        CollisionLabValidation.Shot(lab, lab.cameraController, "59_Overflow_30_Hits_Rear", 320f, 16f, 7.5f, lab.car.position + Vector3.up * 0.6f);
                        HideSled(false);
                        if (colliderDebug != null) colliderDebug.SetVisible(false);
                    }
                }
                finalA = CollisionLabValidation.Snapshot(filters);
                model.ImpactsChanged -= rec;
                sb.AppendLine($"  Every hit 1-{seq.Count} applied (incl. 24, 25, 26, 30) {P(allApplied)}; diagnostics show each hit {P(diagOk)}; " +
                              $"records stay bounded at {model.ImpactCount} (+{model.MergedImpactCount} merged) {P(model.ImpactCount <= model.maxImpacts)}");
                sb.AppendLine($"  Undamaged regions after 25 impacts (right side, rear) deform normally {P(undamagedOk)}; front-left/right hitbox sections still retract after hit 25 {P(hitboxOk)}");
                sb.AppendLine($"  Saturated front centre (hits 29, 31, 32, 33): mesh moved {satMoves[0]:0.000} / {satMoves[1]:0.000} / {satMoves[2]:0.000} / {satMoves[3]:0.000} m (saturation caps it; the hit still registers)");
                sb.AppendLine($"  Deformation cost per hit: {Avg(deformBefore):0.0} ms avg for hits 1-24, {Avg(deformAfter):0.0} ms avg for hits 25+ (merging is O(1); per-hit cost does not grow)");

                // Re-initialisation keeps the merged damage (saved sums), so a Shutdown/Initialize cycle is lossless.
                model.Shutdown();
                model.Initialize();
                model.SetDamageImmediate(0f, 0f, 0f, 0f);
                float reinit = CollisionLabValidation.MaxDiff(finalA, CollisionLabValidation.Snapshot(filters));
                sb.AppendLine($"  Shutdown + Initialize with {model.MergedImpactCount} merged impacts: mesh difference {reinit:E1} m {P(reinit < 1e-5f)}");

                // Determinism of the damage itself: replay the exact impacts (as recorded when added) into a repaired car.
                lab.ResetAll();
                foreach (var im in recorded) model.AddImpact(im);
                float replay = CollisionLabValidation.MaxDiff(finalA, CollisionLabValidation.Snapshot(filters));
                sb.AppendLine($"  Replaying the {recorded.Count} recorded impacts (incl. {model.MergedImpactCount} merges) into a repaired car: mesh difference {replay:E1} m {P(replay < 1e-5f)}");

                // Physics repeat: the same 33 launches again. PhysX contacts are not bit-identical across different solver
                // histories, so this measures how far physics variation carries, and where the launches first differ.
                lab.ResetAll();
                int firstDiff = -1, extraB = 0; string diffNote = ""; float worstDev = 0f; string worstNote = "";
                for (int i = 0; i < seq.Count; i++)
                {
                    int t0 = model.TotalImpactCount;
                    receiver.logImpacts = false;
                    var r = Hit(seq[i].d, 30f, 4000f, seq[i].narrow, seq[i].offset, out _);
                    receiver.logImpacts = false;
                    int dImp = model.TotalImpactCount - t0;
                    if (dImp > 1) extraB++;
                    var snap = new ImpactFootprintSnap(r);
                    float dev = snap.Deviation(fpA[i]);
                    if (dev > worstDev) { worstDev = dev; worstNote = $"launch {i + 1} ({seq[i].name}): centre {fpA[i].centre.ToString("F4")} vs {snap.centre.ToString("F4")}, crush {fpA[i].travel:0.0000} vs {snap.travel:0.0000}, area {fpA[i].area:0.0000} vs {snap.area:0.0000}, contact {fpA[i].contact.ToString("F4")} vs {snap.contact.ToString("F4")}, strength {fpA[i].strength:0.0000} vs {snap.strength:0.0000}"; }
                    if (firstDiff < 0 && (dImp != launchImpactsA[i] || Mathf.Abs(r.contactPoint.z - contactA[i]) > 1e-3f))
                    {
                        firstDiff = i + 1;
                        diffNote = $"launch {i + 1}: impacts {launchImpactsA[i]} vs {dImp}, contact z {contactA[i]:0.0000} vs {r.contactPoint.z:0.0000}";
                    }
                }
                float rep = CollisionLabValidation.MaxDiff(finalA, CollisionLabValidation.Snapshot(filters));
                sb.AppendLine($"  Same 33 launches again (physics re-run): mesh difference {rep:E1} m {(rep < 1e-3f ? "PASS" : "CHECK")}; " +
                              $"{(firstDiff < 0 ? "all launches matched" : "first difference at " + diffNote)}; launches with a 2nd damaging contact: run 1 {launchImpactsA.FindAll(x => x > 1).Count}, run 2 {extraB} (of {seq.Count} each); second contacts with impact strength suppressed by one-impact-per-launch over both runs: {suppressedCount}");
                sb.AppendLine($"    largest per-launch input difference between the two runs: {(worstNote.Length > 0 ? worstNote : "none")}");

                // Repair Car / Reset All after overflow.
                lab.RepairCar();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                bool faces = hb.CurrentFront[0] == hb.OriginalFront[0] && hb.CurrentFront[1] == hb.OriginalFront[1] && hb.CurrentFront[2] == hb.OriginalFront[2];
                sb.AppendLine($"  Repair Car after 33 hits: mesh error {ep:E1}/{en:E1}, impacts {model.TotalImpactCount}, hitbox original {faces} {P(ep == 0f && en == 0f && model.TotalImpactCount == 0 && faces)}");
                for (int i = 0; i < 26; i++) Hit(ImpactDirection.Front, 30f, 4000f, i % 2 == 0, (i % 3 - 1) * 0.5f, out _);
                int beforeReset = model.TotalImpactCount;
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out ep, out en);
                var after = Hit(ImpactDirection.Front, 30f, 4000f, true, 0f, out _);
                sb.AppendLine($"  Reset All after {beforeReset} hits: mesh error {ep:E1}/{en:E1} {P(ep == 0f && en == 0f)}; next hit applied {after.applied} (impacts {model.TotalImpactCount}) {P(after.applied && model.TotalImpactCount == 1)}");
            }
            finally
            {
                if (colliderDebug != null) colliderDebug.SetVisible(false);
                Close(prev);
            }
            return sb.ToString();
        }
    }
}

namespace DamageLab.EditorTools
{
    /// <summary>The inputs a collision hands to the deformation, for comparing two physics runs.</summary>
    struct ImpactFootprintSnap
    {
        public Vector3 centre, contact; public float travel, area, strength;
        public ImpactFootprintSnap(DamageLab.CollisionTest.CollisionDamageReceiver.Report r)
        { centre = r.footprint.center; contact = r.contactPoint; travel = r.footprint.travel; area = r.footprint.area; strength = r.strength; }
        public float Deviation(ImpactFootprintSnap o) =>
            (centre - o.centre).magnitude + Mathf.Abs(travel - o.travel) + Mathf.Abs(area - o.area) + (contact - o.contact).magnitude + Mathf.Abs(strength - o.strength);
    }
}
