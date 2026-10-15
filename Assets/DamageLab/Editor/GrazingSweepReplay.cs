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
    /// Replays every recorded run of a perturbation sweep (Run*/vehicle*_seed2026_recorded.json under -evidenceDir) with
    /// the current runtime settings and the four 0.1 mm impactor shifts of the Chaos perturbation test. No new PhysX
    /// sampling, so the recorded runs (which differ between Editor launches) are re-judged exactly. Writes
    /// sweep_replay.txt next to the runs.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static void RunGrazingSweepReplay()
        {
            var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-evidenceDir");
            if (ai < 0) throw new ArgumentException("-evidenceDir required");
            string dir = args[ai + 1];
            Vector3[] shifts = { new Vector3(1e-4f, 0f, 0f), new Vector3(0f, 0f, 1e-4f), new Vector3(0f, 1e-4f, 0f), new Vector3(-1e-4f, 0f, -1e-4f) };
            var text = new StringBuilder($"Perturbation sweep replay: recorded seed-2026 Chaos Runs, draft {BoxProjection.GrazingDraftDegrees:0}°, four 0.1 mm shifts; no new PhysX sampling.\n");
            text.AppendLine("run     vehicle  impacts  worst mm  at impact (part)          result");
            var worstByVehicle = new Dictionary<int, float>(); int fails = 0, total = 0;
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            try
            {
                var runs = Directory.GetDirectories(dir, "Run*").OrderBy(d => int.TryParse(Path.GetFileName(d).Substring(3), out int n) ? n : int.MaxValue);
                foreach (var run in runs)
                    foreach (var file in Directory.GetFiles(run, "vehicle*_seed2026_recorded.json").OrderBy(f => f))
                    {
                        var record = JsonUtility.FromJson<ChaosReplayEvidence>(File.ReadAllText(file));
                        Open(record.vehicle);
                        try
                        {
                            var rec = new List<LocalImpact>(record.impacts);
                            var refS = new List<List<Vector3[]>>();
                            ReplayImpacts(rec, true, Vector3.zero, null, refS, null, out _);
                            float worst = 0f; int at = -1; string part = "-";
                            foreach (var s in shifts)
                            {
                                var dB = ReplayImpacts(rec, true, s, refS, null, null, out var wB);
                                for (int k = 0; k < dB.Length; k++)
                                    if (dB[k] > worst) { worst = dB[k]; at = k; part = wB[k] >= 0 ? filters[wB[k]].name : "-"; }
                            }
                            bool pass = worst < 0.02f; total++; if (!pass) fails++;
                            worstByVehicle[record.vehicle] = Mathf.Max(worstByVehicle.TryGetValue(record.vehicle, out float w) ? w : 0f, worst);
                            text.AppendLine($"{Path.GetFileName(run),-7} {record.vehicle,7}  {rec.Count,7}  {worst * 1000f,8:0.000}  {at + 1,9} ({part,-14})  {(pass ? "PASS" : "FAIL")}");
                        }
                        finally { Close(previous); Physics.simulationMode = SimulationMode.Script; }
                    }
            }
            finally { Physics.simulationMode = previous; }
            foreach (var kv in worstByVehicle.OrderBy(kv => kv.Key)) text.AppendLine($"vehicle {kv.Key}: worst {kv.Value * 1000f:0.000} mm over all runs");
            text.AppendLine($"{total - fails}/{total} vehicle runs pass the 20 mm limit");
            File.WriteAllText(Path.Combine(dir, "sweep_replay.txt"), text.ToString());
        }
    }
}
