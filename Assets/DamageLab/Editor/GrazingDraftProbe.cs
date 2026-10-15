using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.Deformation;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Grazing-contact sensitivity: replays recorded Chaos Run impacts (no new PhysX sampling) with the exact leading
    /// surface and with the contact draft, perturbing every impactor by 0.1 mm. Diagnostics freeze the footprint (the
    /// shifted box with the unshifted footprint) or drop the fold term, one at a time, to show which term carries the
    /// difference. All flags restore in finally.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static void RunGrazingDraftProbe()
        {
            var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-evidenceDir");
            if (ai < 0) throw new ArgumentException("-evidenceDir required");
            string dir = args[ai + 1];
            int di = Array.IndexOf(args, "-diagnoseImpacts"); int diagnose = di < 0 ? 0 : int.Parse(args[di + 1]);
            var text = new StringBuilder("Grazing-contact draft probe: recorded seed-2026 Chaos Run impacts replayed; no new PhysX sampling.\n");
            float savedDraft = BoxProjection.GrazingDraftDegrees;
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "vehicle*_seed2026_recorded.json").OrderBy(f => f))
                {
                    var record = JsonUtility.FromJson<ChaosReplayEvidence>(File.ReadAllText(file));
                    Open(record.vehicle);
                    try
                    {
                        var all = new List<LocalImpact>(record.impacts);
                        if (record.vehicle == 2) text.Append(DraftSequence("pickup saved 8-impact counterexample (Sol: 26.582 mm z shift)", all.GetRange(0, 8), true));
                        else if (diagnose > 0) text.Append(DraftSequence($"vehicle {record.vehicle} first {diagnose} impacts (diagnostics on the last)", all.GetRange(0, diagnose), true));
                        text.Append(DraftSequence($"vehicle {record.vehicle} full recorded run ({all.Count} impacts)", all, false));
                    }
                    finally { Close(previous); Physics.simulationMode = SimulationMode.Script; }
                }
            }
            finally
            {
                BoxProjection.GrazingDraftDegrees = savedDraft;
                ImpactField.FoldsOff = false;
                Physics.simulationMode = previous;
            }
            File.WriteAllText(Path.Combine(dir, "grazing_draft_probe.txt"), text.ToString());
        }

        static string DraftSequence(string title, List<LocalImpact> rec, bool diagnostics)
        {
            var sb = new StringBuilder($"\n== {title}\n");
            Vector3[] shifts = { new Vector3(1e-4f, 0f, 0f), new Vector3(0f, 0f, 1e-4f), new Vector3(0f, 1e-4f, 0f), new Vector3(-1e-4f, 0f, -1e-4f) };
            List<List<Vector3[]>> exactRef = null;
            foreach (float draft in diagnostics ? new[] { 0f, 1f, 2f, 3f } : new[] { 0f, 2f })
            {
                BoxProjection.GrazingDraftDegrees = draft;
                var refS = new List<List<Vector3[]>>(); var fps = new List<ImpactFootprint>();
                ReplayImpacts(rec, true, Vector3.zero, null, refS, fps, out _);
                int grazing = 0;
                foreach (var im in rec) if (im.impactor.valid && new BoxProjection(im.impactor, im.direction).DraftLimited) grazing++;
                string label = draft == 0f ? "exact surface" : $"draft {draft:0}°";
                sb.AppendLine($"  {label}: {grazing}/{rec.Count} impacts have a face within the draft of their direction");
                if (exactRef == null) exactRef = refS;
                else
                {
                    var d = new float[rec.Count]; var w = new int[rec.Count];
                    for (int k = 0; k < rec.Count; k++) d[k] = MaxDiffWhere(exactRef[k], refS[k], out w[k]);
                    sb.AppendLine($"    change vs exact surface (unshifted): {Summ(d, w)}");
                }
                float worst = 0f;
                foreach (var s in shifts)
                {
                    var dB = ReplayImpacts(rec, true, s, refS, null, null, out var wB);
                    worst = Mathf.Max(worst, dB.Max());
                    sb.AppendLine($"    shift {s * 1000f} mm: {Summ(dB, wB)}");
                }
                sb.AppendLine($"    worst 0.1 mm perturbation: {worst * 1000f:0.000} mm {(worst < 0.02f ? "PASS" : "FAIL")} (limit 20 mm)");
                if (!diagnostics || (draft != 0f && draft != 2f)) continue;

                // Freeze the footprint: the shifted box crushes with the unshifted footprint (first contact, travel, centre).
                var z = new Vector3(0f, 0f, 1e-4f);
                lab.ResetAll();
                var frozen = new float[rec.Count]; var wf = new int[rec.Count];
                for (int k = 0; k < rec.Count; k++)
                {
                    var im = rec[k];
                    if (im.impactor.valid) { im.footprint = fps[k]; im.impactor.center += z; }
                    lab.model.AddImpact(im);
                    frozen[k] = MaxDiffWhere(refS[k], CollisionLabValidation.Snapshot(filters), out wf[k]);
                }
                sb.AppendLine($"    diagnostic, z shift with footprints frozen: {Summ(frozen, wf)}");
                // Drop the fold term (both runs), to see how much the fold phase amplifies the gap change.
                ImpactField.FoldsOff = true;
                var refNF = new List<List<Vector3[]>>();
                ReplayImpacts(rec, true, Vector3.zero, null, refNF, null, out _);
                var dNF = ReplayImpacts(rec, true, z, refNF, null, null, out var wNF);
                ImpactField.FoldsOff = false;
                sb.AppendLine($"    diagnostic, z shift with the fold term off: {Summ(dNF, wNF)}");
                sb.Append(ProbeImpact(rec, z, rec.Count - 1));
            }
            BoxProjection.GrazingDraftDegrees = 2f;
            lab.ResetAll();
            return sb.ToString();
        }
    }
}
