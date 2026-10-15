using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace DamageLab.EditorTools
{
    public static partial class CollisionPersistenceValidation
    {
        // Diagnostic entry point. Saves a short deterministic counterexample; all runtime flags restore in finally.
        public static void RunSavedChaosProbe()
        {
            var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-evidenceDir");
            if (i < 0) throw new ArgumentException("-evidenceDir required");
            string output = args[i + 1];
            var record = JsonUtility.FromJson<ChaosReplayEvidence>(File.ReadAllText(Path.Combine(output, "vehicle2_seed2026_recorded.json")));
            var impacts = new System.Collections.Generic.List<DamageLab.Deformation.LocalImpact>(record.impacts).GetRange(0, 8);
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script; Open(record.vehicle);
            var text = new StringBuilder("Saved seed2026 pickup counterexample: first eight impacts; no new PhysX sampling.\n");
            try
            {
                text.Append(RepeatabilityInvestigation(impacts));
                text.Append(ProbeImpact(impacts, new Vector3(0, 0, .0001f), 2));
                text.Append(ProbeImpact(impacts, new Vector3(0, 0, .0001f), 7));
                File.WriteAllText(Path.Combine(output, "saved_eight_impact_probe.txt"), text.ToString());
            }
            finally { Close(previous); }
        }
    }
}
