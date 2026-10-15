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
    /// Orientation continuity of the contact draft. For each recorded impact with a box face within the draft of its
    /// direction, the same history is replayed and that impact added four ways: as recorded (exact surface and draft),
    /// with the grazing axis turned exactly perpendicular to the direction about the leading face (the sliver closed),
    /// the limit the exact surface moves away from steeply and the draft should stay close to.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static void RunGrazingOrientationProbe()
        {
            var args = Environment.GetCommandLineArgs(); int ai = Array.IndexOf(args, "-evidenceDir");
            if (ai < 0) throw new ArgumentException("-evidenceDir required");
            string dir = args[ai + 1];
            var text = new StringBuilder("Grazing-contact orientation continuity: recorded seed-2026 impacts; no new PhysX sampling.\n" +
                "Distances are the worst vertex difference after the impact (mm) and, in brackets, vertices differing > 1 cm.\n");
            float savedDraft = BoxProjection.GrazingDraftDegrees;
            var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "vehicle*_seed2026_recorded.json").OrderBy(f => f))
                {
                    var record = JsonUtility.FromJson<ChaosReplayEvidence>(File.ReadAllText(file));
                    Open(record.vehicle);
                    try { text.Append(OrientationContinuity($"vehicle {record.vehicle}", new List<LocalImpact>(record.impacts), savedDraft)); }
                    finally { Close(previous); Physics.simulationMode = SimulationMode.Script; }
                }
            }
            finally
            {
                BoxProjection.GrazingDraftDegrees = savedDraft;
                Physics.simulationMode = previous;
            }
            File.WriteAllText(Path.Combine(dir, "grazing_orientation_probe.txt"), text.ToString());
        }

        static string OrientationContinuity(string title, List<LocalImpact> rec, float draft)
        {
            var sb = new StringBuilder($"\n== {title} (draft {draft:0.#} deg)\n");
            sb.AppendLine("  impact  part (exact vs draft)  tilt     exact vs draft    exact vs aligned    draft vs aligned    aligned: exact vs draft");
            for (int k = 0; k < rec.Count; k++)
            {
                var im = rec[k];
                if (!im.impactor.valid) continue;
                BoxProjection.GrazingDraftDegrees = draft;
                if (!new BoxProjection(im.impactor, im.direction).DraftLimited) continue;
                // The grazing axis closest to perpendicular (non-zero), and the rotation that makes it exactly perpendicular.
                Quaternion q = im.impactor.rotation; Vector3 d = im.direction.normalized;
                Vector3[] axes = { q * Vector3.right, q * Vector3.up, q * Vector3.forward };
                Vector3 axis = Vector3.zero; float best = float.MaxValue, front = -1f; int fi = 0;
                for (int i = 0; i < 3; i++)
                {
                    float dot = Mathf.Abs(Vector3.Dot(axes[i], d));
                    if (dot >= 1e-6f && dot < best) { best = dot; axis = axes[i]; }
                    if (dot > front) { front = dot; fi = i; }
                }
                // Pivot about the centre of the leading face, so the front stays where it is and only the grazing face's
                // far edge swings in: the aligned box is the recorded one with its sliver closed, not a box moved sideways.
                Vector3 pivot = im.impactor.center + axes[fi] * (Mathf.Abs(im.impactor.halfExtents[fi]) * Mathf.Sign(Vector3.Dot(axes[fi], d)));
                Quaternion fix = Quaternion.FromToRotation(axis, (axis - Vector3.Dot(axis, d) * d).normalized);
                var aligned = im; aligned.impactor.rotation = fix * q; aligned.impactor.center = pivot + fix * (im.impactor.center - pivot);
                var prior = rec.GetRange(0, k);

                float tilt = Mathf.Asin(best) * Mathf.Rad2Deg;
                var exP = AddOne(prior, im, 0f); var drP = AddOne(prior, im, draft);
                var exA = AddOne(prior, aligned, 0f); var drA = AddOne(prior, aligned, draft);
                float m0 = MaxDiffWhere(exP, drP, out int fw, out _, out int o0);
                float m1 = MaxDiffWhere(exP, exA, out _, out _, out int o1);
                float m2 = MaxDiffWhere(drP, exA, out _, out _, out int o2);
                float m3 = MaxDiffWhere(exA, drA, out _, out _, out int o3);
                string part = fw >= 0 ? filters[fw].name : "-";
                sb.AppendLine($"  {k + 1,6}  {part,-20} {tilt,6:0.000}°  {m0 * 1000f,8:0.0} ({o0,5})  {m1 * 1000f,8:0.0} ({o1,5})  {m2 * 1000f,8:0.0} ({o2,5})  {m3 * 1000f,8:0.0} ({o3,5})");
            }
            BoxProjection.GrazingDraftDegrees = draft;
            lab.ResetAll();
            return sb.ToString();
        }

        /// <summary>Replay <paramref name="prior"/> (exact surface) and add <paramref name="im"/> with the given draft; footprints re-resolved.</summary>
        static List<Vector3[]> AddOne(List<LocalImpact> prior, LocalImpact im, float draft)
        {
            BoxProjection.GrazingDraftDegrees = 0f;
            ReplayImpacts(prior, true, Vector3.zero, null, null, null, out _);
            BoxProjection.GrazingDraftDegrees = draft;
            im.footprint = default;
            lab.model.AddImpact(im);
            return CollisionLabValidation.Snapshot(filters);
        }
    }
}
