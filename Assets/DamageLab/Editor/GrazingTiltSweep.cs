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
    /// Tilt sweep for the contact draft. Selected recorded grazing impacts are re-added with their grazing tilt scaled
    /// down towards zero (rotation about the leading face's centre, as in the orientation probe), with the exact surface
    /// and with the draft. A continuous contact model approaches the aligned (zero-tilt) result as the tilt shrinks; a
    /// plateau at small tilts is a jump at zero.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static void RunGrazingTiltSweep()
        {
            var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-evidenceDir");
            if (ai < 0) throw new ArgumentException("-evidenceDir required");
            string dir = args[ai + 1];
            var picks = new Dictionary<int, int[]> { { 1, new[] { 6, 26, 28 } }, { 2, new[] { 3, 8, 39, 49 } } };
            var text = new StringBuilder("Grazing-contact tilt sweep: recorded seed-2026 impacts; no new PhysX sampling.\n" +
                "Worst vertex difference from the aligned (zero-tilt) result in mm, vertices differing > 1 cm in brackets.\n");
            float savedDraft = BoxProjection.GrazingDraftDegrees;
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "vehicle*_seed2026_recorded.json").OrderBy(f => f))
                {
                    var record = JsonUtility.FromJson<ChaosReplayEvidence>(File.ReadAllText(file));
                    if (!picks.TryGetValue(record.vehicle, out var ids)) continue;
                    Open(record.vehicle);
                    try { text.Append(TiltSweep($"vehicle {record.vehicle}", new List<LocalImpact>(record.impacts), ids, savedDraft)); }
                    finally { Close(previous); Physics.simulationMode = SimulationMode.Script; }
                }
            }
            finally
            {
                BoxProjection.GrazingDraftDegrees = savedDraft;
                Physics.simulationMode = previous;
            }
            File.WriteAllText(Path.Combine(dir, "grazing_tilt_sweep.txt"), text.ToString());
        }

        static string TiltSweep(string title, List<LocalImpact> rec, int[] ids, float draft)
        {
            float[] fractions = { 1f, 0.5f, 0.25f, 0.1f, 0.03f, 0.01f, 0.001f };
            var sb = new StringBuilder($"\n== {title} (draft {draft:0.#} deg)\n");
            foreach (int id in ids)
            {
                int k = id - 1;
                var im = rec[k];
                Quaternion q = im.impactor.rotation; Vector3 d = im.direction.normalized;
                Vector3[] axes = { q * Vector3.right, q * Vector3.up, q * Vector3.forward };
                Vector3 axis = Vector3.zero; float best = float.MaxValue, front = -1f; int fi = 0;
                for (int i = 0; i < 3; i++)
                {
                    float dot = Mathf.Abs(Vector3.Dot(axes[i], d));
                    if (dot >= 1e-6f && dot < best) { best = dot; axis = axes[i]; }
                    if (dot > front) { front = dot; fi = i; }
                }
                Vector3 pivot = im.impactor.center + axes[fi] * (Mathf.Abs(im.impactor.halfExtents[fi]) * Mathf.Sign(Vector3.Dot(axes[fi], d)));
                Quaternion fix = Quaternion.FromToRotation(axis, (axis - Vector3.Dot(axis, d) * d).normalized);
                LocalImpact Tilted(float t)
                {
                    var r = Quaternion.Slerp(fix, Quaternion.identity, t);   // t = 1 as recorded, t = 0 aligned
                    var v = im; v.impactor.rotation = r * q; v.impactor.center = pivot + r * (im.impactor.center - pivot);
                    return v;
                }
                var prior = rec.GetRange(0, k);
                var aligned = AddOne(prior, Tilted(0f), 0f);
                float tilt = Mathf.Asin(best) * Mathf.Rad2Deg;
                sb.AppendLine($"  impact {id}: recorded tilt {tilt:0.000}°, box {Vector3.Scale(im.impactor.halfExtents, Vector3.one * 2000f)} mm");
                sb.AppendLine("      tilt      exact surface         draft");
                foreach (float t in fractions)
                {
                    float me = MaxDiffWhere(AddOne(prior, Tilted(t), 0f), aligned, out _, out _, out int oe);
                    float md = MaxDiffWhere(AddOne(prior, Tilted(t), draft), aligned, out _, out _, out int od);
                    sb.AppendLine($"    {tilt * t,7:0.0000}°  {me * 1000f,8:0.0} ({oe,5})  {md * 1000f,8:0.0} ({od,5})");
                }
            }
            BoxProjection.GrazingDraftDegrees = draft;
            lab.ResetAll();
            return sb.ToString();
        }
    }
}
