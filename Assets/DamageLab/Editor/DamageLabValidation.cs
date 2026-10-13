using System.Collections.Generic;
using System.Text;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Checks the reversibility guarantees of the damage system on the real lab vehicle:
    ///  1. every slider back to 0 restores the original vertices and normals bit-for-bit;
    ///  2. a value reached by different paths (20 → 60 → 20 vs. straight to 20) gives the same mesh;
    ///  3. many random slider moves do not accumulate error.
    /// </summary>
    public static class DamageLabValidation
    {
        [MenuItem("Damage Lab/Validate Reversibility", priority = 40)]
        public static void ValidateMenu() => Debug.Log(Run());

        public static string Run()
        {
            if (EditorSceneManager.GetActiveScene().path != DamageLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(DamageLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            var model = Object.FindAnyObjectByType<VehicleDamageModel>();
            var sb = new StringBuilder("[DamageLab] Validation\n");
            model.Initialize();
            try
            {
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = new List<Vector3[]>();
                var baseNormals = new List<Vector3[]>();
                foreach (var f in filters) { baseVerts.Add(f.sharedMesh.vertices); baseNormals.Add(f.sharedMesh.normals); }

                // 1 + 3: random walk then reset
                var rng = new System.Random(1234);
                for (int i = 0; i < 200; i++)
                    model.SetDamageImmediate(rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101), rng.Next(0, 101));
                model.SetDamageImmediate(0, 0, 0, 0);
                Compare(filters, baseVerts, baseNormals, out float maxPos, out float maxNrm);
                sb.AppendLine($"  After 200 random combined states -> reset to 0: max vertex error {maxPos:E2} m, max normal error {maxNrm:E2} {(maxPos == 0f && maxNrm == 0f ? "PASS (exact)" : "FAIL")}");

                // 2: path independence
                model.SetDamageImmediate(0, 0, 20, 0);
                var direct = Snapshot(filters);
                model.SetDamageImmediate(0, 0, 60, 0);
                model.SetDamageImmediate(0, 0, 20, 0);
                float pathErr = MaxDiff(direct, Snapshot(filters));
                sb.AppendLine($"  Left 20 directly vs. 20 -> 60 -> 20: max difference {pathErr:E2} m {(pathErr == 0f ? "PASS (identical)" : "FAIL")}");

                model.SetDamageImmediate(80, 0, 60, 0);
                var combo = Snapshot(filters);
                model.SetDamageImmediate(0, 100, 0, 100);
                model.SetDamageImmediate(80, 0, 60, 0);
                float comboErr = MaxDiff(combo, Snapshot(filters));
                sb.AppendLine($"  Front 80 + Left 60 reached by two different paths: max difference {comboErr:E2} m {(comboErr == 0f ? "PASS (identical)" : "FAIL")}");

                // displacement stats at a few levels (how much the body actually moves)
                foreach (var level in new[] { 25, 50, 75, 100 })
                {
                    model.SetDamageImmediate(0, 0, level, 0);
                    float maxDisp = MaxDiff(baseVerts, Snapshot(filters));
                    model.SetDamageImmediate(level, 0, 0, 0);
                    float maxDispF = MaxDiff(baseVerts, Snapshot(filters));
                    sb.AppendLine($"  Damage {level,3}: max vertex displacement  Left {maxDisp:0.000} m   Front {maxDispF:0.000} m");
                }
                float total = 0f;
                for (int i = 0; i < 20; i++)
                {
                    model.SetDamageImmediate(70 + (i % 2), 50, 60, 40);
                    total += model.LastDeformMilliseconds;
                }
                model.SetDamageImmediate(0, 0, 0, 0);
                sb.AppendLine($"  Deformable vertices: {model.DeformableVertices:N0}; full rebuild with all four directions active: {total / 20f:0.0} ms average (editor)");
            }
            finally
            {
                model.Shutdown();
            }
            return sb.ToString();
        }

        static List<Vector3[]> Snapshot(MeshFilter[] filters)
        {
            var l = new List<Vector3[]>();
            foreach (var f in filters) l.Add(f.sharedMesh.vertices);
            return l;
        }

        static float MaxDiff(List<Vector3[]> a, List<Vector3[]> b)
        {
            float m = 0f;
            for (int i = 0; i < a.Count; i++)
                for (int j = 0; j < a[i].Length; j++)
                    m = Mathf.Max(m, (a[i][j] - b[i][j]).magnitude);
            return m;
        }

        static void Compare(MeshFilter[] filters, List<Vector3[]> verts, List<Vector3[]> normals, out float maxPos, out float maxNrm)
        {
            maxPos = 0f; maxNrm = 0f;
            for (int i = 0; i < filters.Length; i++)
            {
                var v = filters[i].sharedMesh.vertices;
                var n = filters[i].sharedMesh.normals;
                for (int j = 0; j < v.Length; j++)
                {
                    maxPos = Mathf.Max(maxPos, (v[j] - verts[i][j]).magnitude);
                    maxNrm = Mathf.Max(maxNrm, (n[j] - normals[i][j]).magnitude);
                }
            }
        }
    }
}
