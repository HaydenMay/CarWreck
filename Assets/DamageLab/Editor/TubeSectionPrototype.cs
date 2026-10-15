using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Standalone prototype for coherent tube sections: real 15 m/s wide hits from the six outlier-check directions, then
    /// post-process the rendered displacement of thin tube parts by averaging it over each mesh-derived cross-section,
    /// and compare folded triangles and cross-section distortion with and without. Touches no runtime state.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static string RunTubePrototype(int vehicle, string partFilter)
        {
            var sb = new StringBuilder();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(vehicle);
            try
            {
                var model = lab.model;
                var parts = model.Parts.Where(p => p.filter.name.Contains(partFilter)).ToList();
                var sections = new Dictionary<DeformablePart, (int[] s, int count)>();
                foreach (var p in parts)
                {
                    var nrm = new Vector3[p.VertexCount];
                    for (int i = 0; i < nrm.Length; i++) nrm[i] = p.VehicleNormal(i);
                    var s = TubeSections.Build(p.VehiclePositions, nrm, p.sourceMesh.triangles, out int c);
                    sections[p] = (s, c);
                    int bound = s.Count(x => x >= 0);
                    sb.AppendLine($"{p.filter.name}: {p.VertexCount} vertices, {c} sections, {bound} bound ({100f * bound / p.VertexCount:0}%), {p.TriangleCount} triangles");
                }
                foreach (var d in new[] { ImpactDirection.Front, ImpactDirection.Rear, ImpactDirection.LeftSide, ImpactDirection.RightSide, ImpactDirection.FrontCornerAngled, ImpactDirection.RearCornerAngled })
                {
                    lab.RepairCar();
                    Hit(d, 15f, 1500f, false, 0f, out _);
                    sb.AppendLine($"-- {CollisionLabController.Label(d)}");
                    foreach (var p in parts)
                    {
                        var (s, c) = sections[p];
                        int n = p.VertexCount;
                        var x0 = p.VehiclePositions;
                        var x1 = new Vector3[n];
                        for (int i = 0; i < n; i++) x1[i] = p.DeformedVehiclePosition(i);
                        // Section mean displacement.
                        var sum = new Vector3[c]; var cnt = new int[c];
                        for (int i = 0; i < n; i++) if (s[i] >= 0) { sum[s[i]] += x1[i] - x0[i]; cnt[s[i]]++; }
                        var avg = (Vector3[])x1.Clone();
                        for (int i = 0; i < n; i++) if (s[i] >= 0) avg[i] = x0[i] + sum[s[i]] / cnt[s[i]];
                        int f1 = Folds(p.sourceMesh.triangles, x0, x1), f2 = Folds(p.sourceMesh.triangles, x0, avg);
                        // Cross-section distortion: worst change of a member's distance to its section centroid (original vs deformed).
                        var c0 = new Vector3[c]; var c1 = new Vector3[c];
                        for (int i = 0; i < n; i++) if (s[i] >= 0) { c0[s[i]] += x0[i]; c1[s[i]] += x1[i]; }
                        for (int k = 0; k < c; k++) if (cnt[k] > 0) { c0[k] /= cnt[k]; c1[k] /= cnt[k]; }
                        float worstRing = 0f, maxDisp = 0f;
                        for (int i = 0; i < n; i++)
                        {
                            maxDisp = Mathf.Max(maxDisp, (x1[i] - x0[i]).magnitude);
                            if (s[i] < 0) continue;
                            float r0 = (x0[i] - c0[s[i]]).magnitude;
                            if (r0 > 1e-5f) worstRing = Mathf.Max(worstRing, Mathf.Abs((x1[i] - c1[s[i]]).magnitude - r0) / r0);
                        }
                        int tris = p.TriangleCount;
                        sb.AppendLine($"   {p.filter.name,-18} max disp {maxDisp * 1000f:0} mm; folds per-vertex {f1}/{tris} ({100f * f1 / tris:0.00}%) -> sections {f2}/{tris} ({100f * f2 / tris:0.00}%); worst section radius change {worstRing * 100f:0}%");
                    }
                }
                lab.RepairCar();
            }
            finally
            {
                Close(prev);
            }
            var text = sb.ToString();
            File.WriteAllText("Temp/tube-prototype.txt", text);
            return text;
        }

        static int Folds(int[] t, Vector3[] v0, Vector3[] v)
        {
            int f = 0;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 a = Vector3.Cross(v0[t[k + 1]] - v0[t[k]], v0[t[k + 2]] - v0[t[k]]);
                Vector3 c = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                if (a.sqrMagnitude > 1e-12f && Vector3.Dot(a, c) < 0f) f++;
            }
            return f;
        }
    }
}
