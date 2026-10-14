using System.Collections.Generic;
using System.Text;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Repeatability investigation for physical impacts: how far the deformation of a recorded impact sequence moves when
    /// (A) the exact optimisations are switched off, (B) the impactor is shifted by 0.1 mm, and (C) each optimisation is
    /// switched on alone; (D) per-impact divergence shows where a difference first appears and which part carries it.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        static void SetOptimizations(bool cull, bool skip, bool sweep, bool prefilter, bool range)
        {
            VehicleDamageModel.CullImpactsByBounds = cull;
            VehicleDamageModel.SkipUnchangedParts = skip;
            VehicleDamageModel.SweepPartFilter = sweep;
            ImpactField.SilhouettePrefilter = prefilter;
            DeformablePart.AccumulateByRange = range;
        }

        /// <summary>Replay <paramref name="rec"/> into a repaired car. reresolve: footprints are found again against the
        /// replayed surface (as a physical hit does), with the impactor shifted by <paramref name="shift"/>. Returns the mesh
        /// difference to <paramref name="reference"/> after every impact (or stores the snapshots when it is null).</summary>
        static float[] ReplayImpacts(List<LocalImpact> rec, bool reresolve, Vector3 shift, List<List<Vector3[]>> reference,
                                     List<List<Vector3[]>> store, List<ImpactFootprint> fps, out int[] worstFilter)
        {
            lab.ResetAll();
            var model = lab.model;
            var diffs = new float[rec.Count];
            worstFilter = new int[rec.Count];
            for (int k = 0; k < rec.Count; k++)
            {
                var im = rec[k];
                if (reresolve && im.impactor.valid) { im.footprint = default; im.impactor.center += shift; }
                model.AddImpact(im);
                fps?.Add(model.Impacts[model.ImpactCount - 1].footprint);
                var snap = CollisionLabValidation.Snapshot(filters);
                if (reference != null) diffs[k] = MaxDiffWhere(reference[k], snap, out worstFilter[k]);
                store?.Add(snap);
            }
            return diffs;
        }

        static float MaxDiffWhere(List<Vector3[]> a, List<Vector3[]> b, out int filter) => MaxDiffWhere(a, b, out filter, out _, out _);

        static float MaxDiffWhere(List<Vector3[]> a, List<Vector3[]> b, out int filter, out int vertex, out int over1cm)
        {
            float m = 0f; filter = -1; vertex = -1; over1cm = 0;
            for (int f = 0; f < a.Count; f++)
                for (int i = 0; i < a[f].Length; i++)
                {
                    float d = (a[f][i] - b[f][i]).sqrMagnitude;
                    if (d > 1e-4f) over1cm++;
                    if (d > m) { m = d; filter = f; vertex = i; }
                }
            return Mathf.Sqrt(m);
        }

        static string Summ(float[] d, int[] wf)
        {
            int first1mm = -1, first1cm = -1, worst = 0;
            for (int k = 0; k < d.Length; k++)
            {
                if (first1mm < 0 && d[k] > 1e-3f) first1mm = k;
                if (first1cm < 0 && d[k] > 1e-2f) first1cm = k;
                if (d[k] > d[worst]) worst = k;
            }
            string Part(int k) => wf[k] >= 0 ? filters[wf[k]].name : "-";
            return $"final {d[d.Length - 1] * 1000f:0.000} mm, worst {d[worst] * 1000f:0.000} mm at impact {worst + 1} ({Part(worst)}), " +
                   $"first >1 mm: {(first1mm < 0 ? "none" : $"{first1mm + 1} ({Part(first1mm)})")}, first >1 cm: {(first1cm < 0 ? "none" : $"{first1cm + 1} ({Part(first1cm)})")}";
        }

        static string Fp(ImpactFootprint f) =>
            $"{f.kind} gap {f.firstContact:0.0000} travel {f.travel:0.0000} area {f.area:0.0000} half {f.halfU:0.000}x{f.halfV:0.000} centre {f.center.ToString("F4")}";

        static string RepeatabilityInvestigation(List<LocalImpact> recorded)
        {
            var sb = new StringBuilder("  Repeatability investigation (replays of the recorded impacts; differences are max vertex distance):\n");
            try
            {
                // Reference: every optimisation off (the deformation code path of main), footprints re-resolved.
                SetOptimizations(false, false, false, false, false);
                var refOff = new List<List<Vector3[]>>(); var fpOff = new List<ImpactFootprint>();
                ReplayImpacts(recorded, true, Vector3.zero, null, refOff, fpOff, out _);

                // Test A: stored footprints, culling on vs off.
                SetOptimizations(true, true, true, true, true);
                var storedOn = new List<List<Vector3[]>>();
                ReplayImpacts(recorded, false, Vector3.zero, null, storedOn, null, out _);
                SetOptimizations(false, false, false, false, false);
                var dA = ReplayImpacts(recorded, false, Vector3.zero, storedOn, null, null, out var wA);
                sb.AppendLine($"    A  stored footprints, all optimisations on vs off: {Summ(dA, wA)}");
                SetOptimizations(true, true, true, true, true);
                var fpOn = new List<ImpactFootprint>();
                var dA2 = ReplayImpacts(recorded, true, Vector3.zero, refOff, null, fpOn, out var wA2);
                int fpMismatch = 0; for (int k = 0; k < fpOn.Count; k++) if (Fp(fpOn[k]) != Fp(fpOff[k])) fpMismatch++;
                sb.AppendLine($"    A' re-resolved footprints, all on vs all off: {Summ(dA2, wA2)}; footprints differing: {fpMismatch}/{fpOn.Count}");
                var dStored = new float[recorded.Count];
                for (int k = 0; k < recorded.Count; k++) dStored[k] = MaxDiffWhere(storedOn[k], refOff[k], out _);
                sb.AppendLine($"       (re-resolving vs the footprints recorded in the physics run: final {dStored[dStored.Length - 1] * 1000f:0.000} mm)");

                // Test C: each optimisation alone against all off.
                string[] names = { "affected-part culling", "selective rebuild", "sweep part filter", "silhouette prefilter", "AccumulateRange" };
                for (int o = 0; o < 5; o++)
                {
                    SetOptimizations(o == 0, o == 1, o == 2, o == 3, o == 4);
                    var dC = ReplayImpacts(recorded, true, Vector3.zero, refOff, null, null, out var wC);
                    sb.AppendLine($"    C  only {names[o],-22} vs all off: {Summ(dC, wC)}");
                }

                // Test B: 0.1 mm impactor shifts (re-resolved), with every optimisation off and on.
                Vector3[] shifts = { new Vector3(1e-4f, 0f, 0f), new Vector3(0f, 1e-4f, 0f), new Vector3(0f, 0f, 1e-4f), new Vector3(-1e-4f, 0f, -1e-4f) };
                foreach (bool on in new[] { false, true })
                {
                    SetOptimizations(on, on, on, on, on);
                    foreach (var s in shifts)
                    {
                        var fpS = new List<ImpactFootprint>();
                        var dB = ReplayImpacts(recorded, true, s, refOff, null, fpS, out var wB);
                        sb.AppendLine($"    B  shift {s * 1000f} mm, optimisations {(on ? "on " : "off")}: {Summ(dB, wB)}");
                        // Test D: the first impact that diverges by more than 1 mm, and what its footprint did.
                        int k1 = System.Array.FindIndex(dB, x => x > 1e-3f);
                        if (k1 >= 0 && !on)
                        {
                            sb.AppendLine($"       D  impact {k1 + 1} first diverges ({dB[k1] * 1000f:0.000} mm, part {filters[wB[k1]].name}); dir {recorded[k1].direction.ToString("F3")}");
                            sb.AppendLine($"          footprint ref : {Fp(fpOff[k1])}");
                            sb.AppendLine($"          footprint +0.1: {Fp(fpS[k1])}");
                            if (k1 > 0) sb.AppendLine($"          previous impact {k1} diff {dB[k1 - 1] * 1000f:0.000} mm");
                        }
                    }
                }
            }
            finally
            {
                SetOptimizations(true, true, true, true, true);
                lab.ResetAll();
            }
            return sb.ToString();
        }

        /// <summary>Where a 0.1 mm impactor shift changes impact <paramref name="k"/> (0-based) most: that vertex's terms, both ways.</summary>
        static string ProbeImpact(List<LocalImpact> recorded, Vector3 shift, int k)
        {
            var sb = new StringBuilder();
            var upTo = recorded.GetRange(0, k + 1);
            var r0 = new List<List<Vector3[]>>();
            ReplayImpacts(upTo, true, Vector3.zero, null, r0, null, out _);
            ReplayImpacts(upTo, true, shift, null, null, null, out _);
            float worst = MaxDiffWhere(r0[k], CollisionLabValidation.Snapshot(filters), out int f, out int v, out int over);
            sb.AppendLine($"    probe: impact {k + 1} with every impactor shifted {shift * 1000f} mm: worst vertex {filters[f].name}[{v}] differs {worst * 1000f:0.000} mm; vertices differing > 1 cm: {over}");
            foreach (var s in new[] { Vector3.zero, shift })
            {
                ReplayImpacts(recorded.GetRange(0, k), true, s, null, null, null, out _);
                Vector3 prior = lab.model.ImpactSumAt(filters[f], v);
                var im = recorded[k]; im.footprint = default; im.impactor.center += s;
                lab.model.AddImpact(im);
                sb.AppendLine($"      {(s == Vector3.zero ? "original" : "shifted "),-8}: {lab.model.ExplainImpactVertex(filters[f], v, lab.model.ImpactCount - 1, prior)}");
            }
            lab.ResetAll();
            return sb.ToString();
        }


        public static string RunRepeatProbe(int vehicle)
        {
            var sb = new StringBuilder($"[CollisionLab] Repeatability probe (33 overflow launches, then replays)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(vehicle);
            try
            {
                lab.hitbox.Setup();
                var model = lab.model;
                var recorded = new List<LocalImpact>();
                int lastTotal = 0;
                System.Action rec = () => { if (model.TotalImpactCount > lastTotal && model.ImpactCount > 0) recorded.Add(model.Impacts[model.ImpactCount - 1]); lastTotal = model.TotalImpactCount; };
                model.ImpactsChanged += rec;
                foreach (var s in Sequence()) { receiver.logImpacts = false; Hit(s.d, 30f, 4000f, s.narrow, s.offset, out _); }
                model.ImpactsChanged -= rec;
                sb.AppendLine($"  {lab.CurrentVehicle?.displayName}: {recorded.Count} impacts recorded");
                SetOptimizations(false, false, false, false, false);
                var refOff = new List<List<Vector3[]>>();
                ReplayImpacts(recorded, true, Vector3.zero, null, refOff, null, out _);
                foreach (var s in new[] { new Vector3(1e-4f, 0f, 0f), new Vector3(0f, 1e-4f, 0f) })
                {
                    var dB = ReplayImpacts(recorded, true, s, refOff, null, null, out _);
                    int k1 = System.Array.FindIndex(dB, x => x > 1e-3f);
                    if (k1 >= 0) sb.Append(ProbeImpact(recorded, s, k1));
                    int kw = 0; for (int k = 1; k < dB.Length; k++) if (dB[k] - dB[k - 1] > dB[kw] - (kw > 0 ? dB[kw - 1] : 0f)) kw = k;
                    if (kw != k1) sb.Append(ProbeImpact(recorded, s, kw));
                }
                SetOptimizations(true, true, true, true, true);
                sb.Append(RepeatabilityInvestigation(recorded));
            }
            finally
            {
                SetOptimizations(true, true, true, true, true);
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
