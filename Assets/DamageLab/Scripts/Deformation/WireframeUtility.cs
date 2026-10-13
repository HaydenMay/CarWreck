using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Builds line-topology overlays that share vertex positions with the deformed mesh, so the wireframe
    /// shows the live deformed topology (URP has no built-in wireframe mode).
    /// </summary>
    public static class WireframeUtility
    {
        public static Mesh BuildLineMesh(Mesh source, int[] triangles, Vector3[] vertices)
        {
            var edges = new HashSet<long>();
            var indices = new List<int>(triangles.Length * 2);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                AddEdge(triangles[t], triangles[t + 1], edges, indices);
                AddEdge(triangles[t + 1], triangles[t + 2], edges, indices);
                AddEdge(triangles[t + 2], triangles[t], edges, indices);
            }
            var m = new Mesh
            {
                name = source.name + " (Wireframe)",
                hideFlags = HideFlags.DontSave,
                indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            m.MarkDynamic();
            m.SetVertices(vertices);
            m.SetIndices(indices, MeshTopology.Lines, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddEdge(int a, int b, HashSet<long> edges, List<int> indices)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (!edges.Add(key)) return;
            indices.Add(a);
            indices.Add(b);
        }

        public static GameObject CreateOverlay(Transform parent, Mesh lineMesh, Material material)
        {
            var go = new GameObject("Wireframe") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }
    }
}
