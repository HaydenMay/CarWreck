using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// One deformable mesh of the vehicle. Owns a runtime copy of the imported mesh (the shared asset is
    /// never written), caches the original vertex data and re-derives the deformed state from it on
    /// every update, so values never accumulate error.
    /// </summary>
    public sealed class DeformablePart
    {
        public readonly MeshFilter filter;
        public readonly Mesh sourceMesh;
        public readonly float crumple;
        public Mesh RuntimeMesh { get; private set; }
        public int VertexCount => baseVerts.Length;
        /// <summary>Original vertex positions in vehicle space (read only).</summary>
        public Vector3[] VehiclePositions => vehiclePos;
        public Vector3 VehicleNormal(int i) => vehicleNormal[i];
        public int TriangleCount => triangles.Length / 3;

        readonly Vector3[] baseVerts;     // mesh space, original
        readonly Vector3[] baseNormals;   // mesh space, original (as imported)
        readonly Vector3[] baseCalcNormals; // normals recomputed from the original positions
        readonly Vector3[] vehiclePos;    // vehicle space, original
        readonly Vector3[] vehicleNormal; // vehicle space, smoothed across coincident vertices
        readonly Vector3[] work;
        readonly Vector3[] workNormals;
        readonly Vector3[] accum;
        readonly int[] triangles;
        readonly int[] normalRep;         // vertex -> representative of its smoothing group
        readonly Matrix4x4 vehicleToMesh;
        readonly Matrix4x4 meshToVehicle;
        float windingSign = 1f;
        CrushSample[][] samples;          // [direction][vertex]
        bool deformed;

        Mesh wireMesh;
        GameObject wireObject;

        public DeformablePart(MeshFilter filter, Transform vehicleRoot, float crumple)
        {
            this.filter = filter;
            this.crumple = crumple;
            sourceMesh = filter.sharedMesh;

            baseVerts = sourceMesh.vertices;
            baseNormals = sourceMesh.normals;
            if (baseNormals == null || baseNormals.Length != baseVerts.Length)
            {
                var tmp = Object.Instantiate(sourceMesh);
                tmp.RecalculateNormals();
                baseNormals = tmp.normals;
                Object.DestroyImmediate(tmp);
            }
            triangles = sourceMesh.triangles;
            int n = baseVerts.Length;

            meshToVehicle = vehicleRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            vehicleToMesh = meshToVehicle.inverse;

            vehiclePos = new Vector3[n];
            for (int i = 0; i < n; i++) vehiclePos[i] = meshToVehicle.MultiplyPoint3x4(baseVerts[i]);

            // Group coincident vertices (split by UV seams / hard edges in the import).
            // Displacement uses the group-averaged normal so split vertices move together (no tears);
            // normal recomputation only merges vertices whose original normals agree (keeps hard edges).
            vehicleNormal = new Vector3[n];
            normalRep = new int[n];
            var groups = new Dictionary<Vector3Int, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = baseVerts[i];
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>(2);
                list.Add(i);
            }
            const float hardEdgeCos = 0.5f; // 60 degrees
            foreach (var list in groups.Values)
            {
                Vector3 sum = Vector3.zero;
                foreach (int i in list) sum += baseNormals[i];
                Vector3 avg = meshToVehicle.MultiplyVector(sum).normalized;
                foreach (int i in list)
                {
                    vehicleNormal[i] = avg.sqrMagnitude > 0f ? avg : meshToVehicle.MultiplyVector(baseNormals[i]).normalized;
                    normalRep[i] = i;
                    foreach (int j in list)
                    {
                        if (j >= i) break;
                        if (normalRep[j] == j && Vector3.Dot(baseNormals[i], baseNormals[j]) > hardEdgeCos)
                        {
                            normalRep[i] = j;
                            break;
                        }
                    }
                }
            }

            work = (Vector3[])baseVerts.Clone();   // valid before the first rebuild (wireframe overlays read it)
            workNormals = new Vector3[n];
            accum = new Vector3[n];
            baseCalcNormals = new Vector3[n];
            System.Array.Copy(baseVerts, work, n);
            ComputeNormals(baseVerts, baseCalcNormals);
            // Detect the winding convention once so recomputed normals agree with the imported ones.
            float agree = 0f;
            for (int i = 0; i < n; i++) agree += Vector3.Dot(baseCalcNormals[i], baseNormals[i]);
            if (agree < 0f)
            {
                windingSign = -1f;
                for (int i = 0; i < n; i++) baseCalcNormals[i] = -baseCalcNormals[i];
            }

            RuntimeMesh = Object.Instantiate(sourceMesh);
            RuntimeMesh.name = sourceMesh.name + " (Damage Instance)";
            RuntimeMesh.hideFlags = HideFlags.DontSave;
            RuntimeMesh.MarkDynamic();
            filter.sharedMesh = RuntimeMesh;
        }

        /// <summary>Vehicle-space bounds of the original mesh.</summary>
        public Bounds VehicleBounds()
        {
            if (boundsCached) return cachedBounds;
            var b = new Bounds(vehiclePos[0], Vector3.zero);
            for (int i = 1; i < vehiclePos.Length; i++) b.Encapsulate(vehiclePos[i]);
            cachedBounds = b; boundsCached = true;
            return b;
        }
        Bounds cachedBounds; bool boundsCached;

        /// <summary>True when the cached impact sum changed since the mesh was last rebuilt.</summary>
        public bool ImpactDirty { get; private set; }
        bool appliedOnce;

        /// <summary>Precompute all direction-dependent per-vertex constants (runs once; parallelised).</summary>
        public void Precompute(CrushField[] fields)
        {
            samples = new CrushSample[fields.Length][];
            for (int d = 0; d < fields.Length; d++)
            {
                var field = fields[d];
                var arr = samples[d] = new CrushSample[vehiclePos.Length];
                if (MultiThreaded)
                {
                    Parallel.For(0, (vehiclePos.Length + 1023) / 1024, chunk =>
                    {
                        int end = Mathf.Min(vehiclePos.Length, (chunk + 1) * 1024);
                        for (int i = chunk * 1024; i < end; i++) arr[i] = field.Sample(vehiclePos[i]);
                    });
                }
                else
                {
                    for (int i = 0; i < arr.Length; i++) arr[i] = field.Sample(vehiclePos[i]);
                }
            }
        }

        /// <summary>Rebuild the deformed mesh. With <paramref name="onlyIfImpactsChanged"/> a part whose impact sum did not
        /// change since its last rebuild is skipped (the directional slider state must be unchanged too); returns whether it rebuilt.</summary>
        public bool Apply(CrushField[] fields, RegionState[] states, float roofStart, float roofEnd, ImpactField impacts = null, bool onlyIfImpactsChanged = false)
        {
            if (onlyIfImpactsChanged && appliedOnce && !ImpactDirty) return false;
            appliedOnce = true;
            ImpactDirty = false;
            bool any = impacts != null && hasImpacts;
            for (int d = 0; d < states.Length; d++) any |= states[d].active;
            int n = baseVerts.Length;

            if (!any)
            {
                if (!deformed) return false;
                // Exact restore: copy the original arrays back.
                System.Array.Copy(baseVerts, work, n);
                RuntimeMesh.SetVertices(baseVerts);
                RuntimeMesh.SetNormals(baseNormals);
                RuntimeMesh.bounds = sourceMesh.bounds;
                if (wireMesh != null) { wireMesh.SetVertices(baseVerts); wireMesh.bounds = sourceMesh.bounds; }
                deformed = false;
                return true;
            }

            // Displacement is a pure function of per-vertex constants, so vertex chunks run in parallel.
            applyFields = fields; applyStates = states; applyRoofStart = roofStart; applyRoofEnd = roofEnd;
            applyImpacts = any && hasImpacts ? impacts : null;
            if (MultiThreaded && n >= 4096) Parallel.For(0, (n + ChunkSize - 1) / ChunkSize, applyChunk ??= DisplaceChunk);
            else DisplaceChunk(0, n);

            ComputeNormals(work, workNormals);
            for (int i = 0; i < n; i++)
            {
                // Apply only the *change* in shading normal on top of the imported normal, so the
                // first slider step does not pop from imported to recomputed normals.
                Vector3 nn = baseNormals[i] + windingSign * workNormals[i] - baseCalcNormals[i];
                float m = nn.sqrMagnitude;
                workNormals[i] = m > 1e-12f ? nn / Mathf.Sqrt(m) : baseNormals[i];
            }

            RuntimeMesh.SetVertices(work);
            RuntimeMesh.SetNormals(workNormals);
            RuntimeMesh.RecalculateBounds();
            if (wireMesh != null) { wireMesh.SetVertices(work); wireMesh.RecalculateBounds(); }
            deformed = true;
            return true;
        }

        const int ChunkSize = 2048;
#if UNITY_WEBGL && !UNITY_EDITOR
        const bool MultiThreaded = false; // no thread pool in the browser
#else
        const bool MultiThreaded = true;
#endif
        System.Action<int> applyChunk;
        CrushField[] applyFields;
        RegionState[] applyStates;
        float applyRoofStart, applyRoofEnd;
        ImpactField applyImpacts;

        void DisplaceChunk(int chunk) => DisplaceChunk(chunk * ChunkSize, Mathf.Min(baseVerts.Length, (chunk + 1) * ChunkSize));

        void DisplaceChunk(int start, int end)
        {
            var fields = applyFields;
            var states = applyStates;
            for (int i = start; i < end; i++)
            {
                Vector3 disp = Vector3.zero;
                for (int d = 0; d < states.Length; d++)
                {
                    if (!states[d].active) continue;
                    disp += fields[d].Displacement(in samples[d][i], vehicleNormal[i], in states[d], crumple, applyRoofStart, applyRoofEnd);
                }
                // Localized click impacts (cached sum, computed from the original positions).
                if (applyImpacts != null) disp += applyImpacts.Limit(impactSum[i], impactCap[i]);
                work[i] = baseVerts[i] + vehicleToMesh.MultiplyVector(disp);
            }
        }

        Vector3[] impactSum;              // per vertex: summed raw displacement of all localized impacts
        float[] impactCap;                // per vertex: structural crush capacity (depends only on the original mesh)
        bool hasImpacts;

        /// <summary>Add impact <paramref name="index"/> of the field to this part's cached impact sum.
        /// Only the new impact is evaluated, so a click costs one impact and slider moves cost none.</summary>
        public void AccumulateImpact(ImpactField field, int index)
        {
            int n = vehiclePos.Length;
            impactSum ??= new Vector3[n];
            if (impactCap == null)
            {
                impactCap = new float[n];
                for (int i = 0; i < n; i++) impactCap[i] = field.Capacity(vehiclePos[i], crumple);
            }
            hasImpacts = true;
            ImpactDirty = true;
            if (MultiThreaded && n >= 4096)
            {
                Parallel.For(0, (n + ChunkSize - 1) / ChunkSize, chunk =>
                {
                    int end = Mathf.Min(n, (chunk + 1) * ChunkSize);
                    Accumulate(field, index, chunk * ChunkSize, end);
                });
            }
            else
            {
                Accumulate(field, index, 0, n);
            }
        }

        /// <summary>Evaluate impacts with <see cref="ImpactField.AccumulateRange"/> (exact). Off = one Contribution call per vertex (for validation).</summary>
        public static bool AccumulateByRange = true;

        void Accumulate(ImpactField field, int index, int start, int end)
        {
            if (AccumulateByRange) { field.AccumulateRange(index, vehiclePos, vehicleNormal, crumple, impactSum, impactCap, start, end); return; }
            for (int i = start; i < end; i++) impactSum[i] += field.Contribution(index, vehiclePos[i], vehicleNormal[i], crumple, impactSum[i], impactCap[i]);
        }


        /// <summary>Vertex i as damaged by localized impacts so far (original + saturated impact sum), vehicle space.</summary>
        public Vector3 ImpactedVehiclePosition(int i, ImpactField field) =>
            impactSum == null || !hasImpacts ? vehiclePos[i] : vehiclePos[i] + field.Limit(impactSum[i], impactCap[i]);

        /// <summary>Current rendered position of vertex i in vehicle space (original when undeformed).</summary>
        public Vector3 DeformedVehiclePosition(int i) => deformed ? meshToVehicle.MultiplyPoint3x4(work[i]) : vehiclePos[i];
        /// <summary>Original vertex with the smallest distance t ≥ 0 along d from o, within √r2 of the line.</summary>
        public void FirstAlongLane(Vector3 o, Vector3 d, float r2, ref float bestT, ref Vector3 bestPoint)
        {
            for (int i = 0; i < vehiclePos.Length; i++)
            {
                Vector3 v = vehiclePos[i] - o;
                float t = Vector3.Dot(v, d);
                if (t < 0f || t >= bestT) continue;
                if (v.sqrMagnitude - t * t > r2) continue;
                bestT = t;
                bestPoint = vehiclePos[i];
            }
        }


        /// <summary>Copy of the per-vertex impact sum (null when this part has no impact damage).</summary>
        public Vector3[] ImpactSumCopy() => impactSum != null && hasImpacts ? (Vector3[])impactSum.Clone() : null;

        /// <summary>Restore a sum saved by <see cref="ImpactSumCopy"/> (same mesh) instead of replaying impacts.</summary>
        public void RestoreImpactSum(Vector3[] sum, ImpactField field)
        {
            if (sum == null || sum.Length != vehiclePos.Length) return;
            impactSum = (Vector3[])sum.Clone();
            if (impactCap == null)
            {
                impactCap = new float[sum.Length];
                for (int i = 0; i < sum.Length; i++) impactCap[i] = field.Capacity(vehiclePos[i], crumple);
            }
            hasImpacts = true;
            ImpactDirty = true;
        }
        public void ClearImpacts()
        {
            if (hasImpacts) ImpactDirty = true;
            if (impactSum != null) System.Array.Clear(impactSum, 0, impactSum.Length);
            hasImpacts = false;
        }

        void ComputeNormals(Vector3[] pos, Vector3[] result)
        {
            int n = pos.Length;
            System.Array.Clear(accum, 0, n);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                Vector3 pa = pos[a];
                Vector3 fn = Vector3.Cross(pos[b] - pa, pos[c] - pa); // area weighted
                accum[normalRep[a]] += fn;
                accum[normalRep[b]] += fn;
                accum[normalRep[c]] += fn;
            }
            for (int i = 0; i < n; i++) result[i] = accum[normalRep[i]].normalized;
        }

        public void SetWireframe(bool on, Material material)
        {
            if (on && wireObject == null)
            {
                wireMesh = WireframeUtility.BuildLineMesh(RuntimeMesh, triangles, work);
                wireObject = WireframeUtility.CreateOverlay(filter.transform, wireMesh, material);
            }
            if (wireObject != null) wireObject.SetActive(on);
        }

        /// <summary>Put the imported mesh back and free runtime objects.</summary>
        public void Dispose()
        {
            if (filter != null) filter.sharedMesh = sourceMesh;
            DestroySafe(RuntimeMesh);
            DestroySafe(wireMesh);
            DestroySafe(wireObject);
            RuntimeMesh = null;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }
    }

    /// <summary>
    /// A part that is moved rather than deformed (wheels). It follows the crush field evaluated at its
    /// centre, so wheels are shoved inward / backward with the bodywork around them.
    /// </summary>
    public sealed class RigidPart
    {
        public readonly Transform transform;
        readonly Vector3 baseLocalPosition;
        readonly Quaternion baseLocalRotation;
        readonly Vector3 vehicleCenter;
        /// <summary>Centre of the part in vehicle space (used as a wheel-mount position).</summary>
        public Vector3 VehicleCenter => vehicleCenter;
        readonly Vector3 vehicleExtents;
        readonly float follow;
        readonly Transform vehicleRoot;
        CrushSample[] samples;

        public RigidPart(Transform t, Transform vehicleRoot, float follow)
        {
            transform = t;
            this.vehicleRoot = vehicleRoot;
            this.follow = follow;
            baseLocalPosition = t.localPosition;
            baseLocalRotation = t.localRotation;
            var r = t.GetComponent<Renderer>();
            Vector3 world = r != null ? r.bounds.center : t.position;
            vehicleCenter = vehicleRoot.InverseTransformPoint(world);
            vehicleExtents = r != null ? vehicleRoot.InverseTransformVector(r.bounds.extents) : Vector3.zero;
            vehicleExtents = new Vector3(Mathf.Abs(vehicleExtents.x), Mathf.Abs(vehicleExtents.y), Mathf.Abs(vehicleExtents.z));
        }

        public void Precompute(CrushField[] fields)
        {
            samples = new CrushSample[fields.Length];
            // Sample each field at the point of the part nearest that impact face (e.g. the front of
            // the tyre for a frontal hit), so the wheel is shoved as soon as the crush reaches it.
            for (int d = 0; d < fields.Length; d++)
            {
                Vector3 o = fields[d].frame.outward;
                Vector3 lead = vehicleCenter + Vector3.Scale(o, vehicleExtents);
                samples[d] = fields[d].Sample(lead);
            }
        }

        public void Apply(CrushField[] fields, RegionState[] states, float roofStart, float roofEnd)
        {
            Vector3 disp = Vector3.zero;
            for (int d = 0; d < states.Length; d++)
            {
                if (!states[d].active) continue;
                // Rigid parts only take the bulk push (no folds/creases): crumple = 0, normal irrelevant.
                disp += fields[d].Displacement(in samples[d], Vector3.zero, in states[d], 0f, roofStart, roofEnd);
            }
            disp *= follow;
            disp.y = 0f; // keep wheels on the ground
            Transform parent = transform.parent;
            Vector3 worldDisp = vehicleRoot.TransformVector(disp);
            Vector3 localDisp = parent != null ? parent.InverseTransformVector(worldDisp) : worldDisp;
            transform.localPosition = baseLocalPosition + localDisp;
            // Camber: a wheel shoved inward tilts its top inward.
            float camber = Mathf.Clamp(-disp.x * 40f, -14f, 14f);
            float toe = Mathf.Clamp(disp.z * 25f * Mathf.Sign(vehicleCenter.x), -10f, 10f);
            transform.localRotation = Quaternion.Euler(0f, toe, camber) * baseLocalRotation;
        }

        public void Restore()
        {
            if (transform == null) return;
            transform.localPosition = baseLocalPosition;
            transform.localRotation = baseLocalRotation;
        }
    }
}
