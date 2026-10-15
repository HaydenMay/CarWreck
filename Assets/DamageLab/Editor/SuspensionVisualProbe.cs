using System;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Visual check for coherent tube sections: the same 15 m/s wide front hit on the pickup with the opt-in turned off
    /// (in memory only) and on, then the front suspension rendered alone so the wheels and body do not hide it. Every
    /// rule flag is restored in finally; nothing is saved.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static void RunSuspensionVisualProbe()
        {
            var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-evidenceDir");
            if (ai < 0) throw new ArgumentException("-evidenceDir required");
            string dir = args[ai + 1]; Directory.CreateDirectory(dir);
            var text = new StringBuilder("Front suspension visual probe: pickup, wide sled 15 m/s 1500 kg front; tube sections off (in memory) vs on.\n");
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            Open(2);
            var opted = lab.model.Rules.Where(r => r.coherentTubeSections).ToList();
            try
            {
                foreach (bool coherent in new[] { false, true })
                {
                    foreach (var r in opted) r.coherentTubeSections = coherent;
                    lab.model.Shutdown(); lab.model.Initialize(); lab.Setup(); lab.RepairCar();
                    Hit(ImpactDirection.Front, 15f, 1500f, false, 0f, out _);
                    var part = lab.model.Parts.First(p => p.filter.name == "Suspension_Front");
                    int n = part.VertexCount; var x0 = part.VehiclePositions; var x1 = new Vector3[n];
                    for (int i = 0; i < n; i++) x1[i] = part.DeformedVehiclePosition(i);
                    int folds = Folds(part.sourceMesh.triangles, x0, x1);
                    float disp = 0f; for (int i = 0; i < n; i++) disp = Mathf.Max(disp, (x1[i] - x0[i]).magnitude);
                    string label = coherent ? "sections_on" : "sections_off";
                    text.AppendLine($"  {label}: Suspension_Front folded {folds}/{part.TriangleCount} ({100f * folds / part.TriangleCount:0.00}%), max displacement {disp * 1000f:0} mm");

                    var keep = part.filter.GetComponent<Renderer>();
                    var hidden = UnityEngine.Object.FindObjectsByType<Renderer>().Where(r => r != keep && r.enabled).ToList();
                    foreach (var r in hidden) r.enabled = false;
                    try
                    {
                        Vector3 pivot = part.filter.GetComponent<Renderer>().bounds.center;
                        foreach (var view in new[] { ("front", 160f, 12f, 1.9f), ("oblique", 120f, 28f, 1.7f) })
                        {
                            string key = $"95_suspension_{label}_{view.Item1}";
                            CollisionLabValidation.Shot(lab, lab.cameraController, key, view.Item2, view.Item3, view.Item4, pivot);
                            File.Copy(Path.Combine(DamageLabCapture.OutputFolder, key + ".png"), Path.Combine(dir, $"suspension_front_{label}_{view.Item1}.png"), true);
                        }
                    }
                    finally { foreach (var r in hidden) r.enabled = true; }
                }
            }
            finally
            {
                foreach (var r in opted) r.coherentTubeSections = true;
                Close(previous);
            }
            File.WriteAllText(Path.Combine(dir, "suspension_visual_probe.txt"), text.ToString());
        }
    }
}
