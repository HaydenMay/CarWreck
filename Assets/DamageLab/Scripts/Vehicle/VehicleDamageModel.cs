using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DamageLab.Deformation;
using UnityEngine;

namespace DamageLab.Vehicle
{
    /// <summary>
    /// Vehicle-level damage controller. Discovers the imported parts, owns the four crush fields and
    /// applies the current Front / Rear / Left / Right amounts. Sliders set target values; the applied
    /// values ease toward them so dragging produces smooth, continuous deformation.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleDamageModel : MonoBehaviour
    {
        [Tooltip("Damage region definitions (impact footprints, depths, crumple behaviour).")]
        public VehicleDamageProfile profile;

        [Tooltip("How each imported child object takes damage, matched by name.")]
        public List<VehiclePartRule> partRules = new List<VehiclePartRule>(VehiclePartRule.Defaults());

        [Header("Damage (0-100)")]
        [Range(0f, 100f)] public float front;
        [Range(0f, 100f)] public float rear;
        [Range(0f, 100f)] public float left;
        [Range(0f, 100f)] public float right;

        [Tooltip("How quickly the applied damage catches up with the slider value (1/s). 0 = instant.")]
        public float responseSharpness = 16f;

        [Header("Click-to-damage")]
        [Tooltip("Maximum number of stored localized impacts.")]
        public int maxImpacts = 10;
        [Tooltip("Build concave MeshColliders from the original meshes for click picking. Turn off when the car has a dynamic Rigidbody (Collision Lab).")]
        public bool createPickingColliders = true;

        [Header("Debug")]
        public Material wireframeMaterial;
        [SerializeField] bool wireframe;

        /// <summary>Raised after the mesh has been rebuilt.</summary>
        public event Action Deformed;
        /// <summary>Raised when a localized impact is added or the impacts are cleared.</summary>
        public event Action ImpactsChanged;

        public bool IsInitialized { get; private set; }
        public int TotalTriangles { get; private set; }
        public int DeformableVertices { get; private set; }
        public int PartCount => parts.Count;
        public float LastDeformMilliseconds { get; private set; }
        public bool Wireframe => wireframe;

        readonly List<DeformablePart> parts = new List<DeformablePart>();
        readonly List<RigidPart> rigidParts = new List<RigidPart>();
        readonly List<GameObject> rigidWireframes = new List<GameObject>();
        readonly float[] applied = new float[DamageDirections.Count];
        readonly RegionState[] states = new RegionState[DamageDirections.Count];
        CrushField[] fields;
        readonly List<LocalImpact> impacts = new List<LocalImpact>();
        readonly List<MeshCollider> pickColliders = new List<MeshCollider>();
        ImpactField impactField;
        readonly Stopwatch stopwatch = new Stopwatch();
        bool dirty;

        void Start()
        {
            Initialize();
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void Update()
        {
            if (!IsInitialized) return;
            float t = responseSharpness <= 0f ? 1f : 1f - Mathf.Exp(-responseSharpness * Time.unscaledDeltaTime);
            for (int d = 0; d < DamageDirections.Count; d++)
            {
                float target = GetDamage((DamageDirection)d);
                float cur = applied[d];
                if (cur == target) continue;
                cur = Mathf.Lerp(cur, target, t);
                if (Mathf.Abs(cur - target) < 0.05f) cur = target; // snap so 0 restores exactly
                applied[d] = cur;
                dirty = true;
            }
            if (dirty) ApplyDeformation();
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Target damage (0-100) for a direction, as set by the UI.</summary>
        public float GetDamage(DamageDirection d)
        {
            switch (d)
            {
                case DamageDirection.Front: return front;
                case DamageDirection.Rear: return rear;
                case DamageDirection.Left: return left;
                default: return right;
            }
        }

        /// <summary>Damage (0-100) currently applied to the mesh (lags the target while easing).</summary>
        public float GetAppliedDamage(DamageDirection d) => applied[(int)d];

        public void SetDamage(DamageDirection d, float percent)
        {
            percent = Mathf.Clamp(percent, 0f, 100f);
            switch (d)
            {
                case DamageDirection.Front: front = percent; break;
                case DamageDirection.Rear: rear = percent; break;
                case DamageDirection.Left: left = percent; break;
                default: right = percent; break;
            }
        }

        /// <summary>Set all four targets and rebuild the mesh immediately (no easing). Used by captures/tests.</summary>
        public void SetDamageImmediate(float frontPct, float rearPct, float leftPct, float rightPct)
        {
            front = Mathf.Clamp(frontPct, 0f, 100f);
            rear = Mathf.Clamp(rearPct, 0f, 100f);
            left = Mathf.Clamp(leftPct, 0f, 100f);
            right = Mathf.Clamp(rightPct, 0f, 100f);
            if (!IsInitialized) Initialize();
            for (int d = 0; d < DamageDirections.Count; d++) applied[d] = GetDamage((DamageDirection)d);
            ApplyDeformation();
        }

        /// <summary>Directional sliders to 0 and all localized impacts removed.</summary>
        public void ResetAll()
        {
            front = rear = left = right = 0f;
            ClearImpacts();
        }

        // ------------------------------------------------------------------ localized impacts

        public int ImpactCount => impacts.Count;
        public bool ImpactLimitReached => impacts.Count >= maxImpacts;
        public IReadOnlyList<LocalImpact> Impacts => impacts;
        /// <summary>Vehicle-space bounds of the parts that define the impact faces (body + bumpers).</summary>
        public Bounds BodyBounds { get; private set; }

        /// <summary>
        /// Cast a world-space ray against the vehicle's picking colliders (the ORIGINAL, undamaged meshes)
        /// and, if it hits the car, add an impact at the hit point pushing along the ray.
        /// </summary>
        public bool TryApplyImpact(Ray worldRay, float strength01, out string message)
        {
            if (!IsInitialized) Initialize();
            if (ImpactLimitReached) { message = $"Impact limit reached ({maxImpacts})"; return false; }
            if (strength01 <= 0f) { message = "Impact strength is 0"; return false; }
            Physics.SyncTransforms();
            if (!Physics.Raycast(worldRay, out var hit, 500f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || !(hit.collider is MeshCollider mc) || !pickColliders.Contains(mc))
            {
                message = "Missed the vehicle";
                return false;
            }
            Vector3 dir = transform.InverseTransformDirection(worldRay.direction).normalized;
            Vector3 n = transform.InverseTransformDirection(hit.normal).normalized;
            if (Vector3.Dot(n, dir) > 0f) n = -n;
            AddImpact(new LocalImpact
            {
                point = transform.InverseTransformPoint(hit.point),
                direction = dir,
                normal = n,
                strength = Mathf.Clamp01(strength01),
            });
            message = $"Impact on {hit.collider.name}";
            return true;
        }

        /// <summary>
        /// First point of the ORIGINAL car surface met when travelling along <paramref name="localDir"/> through
        /// <paramref name="localPoint"/> (vehicle space): the original vertex nearest the start of a thin lane
        /// around that line. Used to move a physics contact (on a simplified collider) onto the real bodywork.
        /// Returns false if no vertex lies in the lane.
        /// </summary>
        public bool SurfacePointAlong(Vector3 localPoint, Vector3 localDir, out Vector3 surfacePoint, float lane = 0.12f, float backoff = 1.5f)
        {
            if (!IsInitialized) Initialize();
            Vector3 d = localDir.normalized;
            Vector3 origin = localPoint - d * backoff;
            float best = float.MaxValue;
            surfacePoint = localPoint;
            foreach (var p in parts) p.FirstAlongLane(origin, d, lane * lane, ref best, ref surfacePoint);
            return best < float.MaxValue;
        }

        /// <summary>Add an impact given in vehicle space. Ignored once <see cref="maxImpacts"/> is reached.</summary>
        public bool AddImpact(LocalImpact impact)
        {
            if (ImpactLimitReached || impact.strength <= 0f) return false;
            if (impact.impactor.valid && !impact.footprint.valid)
            {
                // Geometry-aware impact: find the contact patch on the original bodywork once, store it with the impact.
                if (!IsInitialized) Initialize();
                impactField.ResolveFootprint(ref impact, parts.Select(p => p.VehiclePositions));
            }
            impacts.Add(impact);
            if (IsInitialized) AccumulateImpact(impact);
            OnImpactsChanged();
            return true;
        }

        /// <summary>Remove only the localized impacts; the directional sliders are untouched.</summary>
        public void ClearImpacts()
        {
            if (impacts.Count == 0) return;
            impacts.Clear();
            impactField?.Clear();
            foreach (var p in parts) p.ClearImpacts();
            OnImpactsChanged();
        }

        void AccumulateImpact(LocalImpact impact)
        {
            int index = impactField.Add(impact);
            foreach (var p in parts) p.AccumulateImpact(impactField, index);
        }

        void OnImpactsChanged()
        {
            // Impacts appear at once (no easing), so rebuild now rather than next frame.
            if (IsInitialized) ApplyDeformation();
            ImpactsChanged?.Invoke();
        }

        public bool IsDamaged
        {
            get
            {
                if (impacts.Count > 0) return true;
                for (int d = 0; d < DamageDirections.Count; d++)
                    if (applied[d] > 0f || GetDamage((DamageDirection)d) > 0f) return true;
                return false;
            }
        }

        public void SetWireframe(bool on)
        {
            wireframe = on;
            if (!IsInitialized) return;
            foreach (var p in parts) p.SetWireframe(on, wireframeMaterial);
            if (on && rigidWireframes.Count == 0)
            {
                foreach (var rp in rigidParts)
                {
                    var mf = rp.transform.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var m = mf.sharedMesh;
                    var line = WireframeUtility.BuildLineMesh(m, m.triangles, m.vertices);
                    rigidWireframes.Add(WireframeUtility.CreateOverlay(rp.transform, line, wireframeMaterial));
                }
            }
            foreach (var go in rigidWireframes) if (go != null) go.SetActive(on);
        }

        // ------------------------------------------------------------------ setup

        public void Initialize()
        {
            if (IsInitialized) return;
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VehicleDamageProfile>();
                profile.ResetToDefaults();
            }

            TotalTriangles = 0;
            DeformableVertices = 0;
            var faceBounds = new Bounds();
            bool hasFaceBounds = false;
            var pendingRigid = new List<(Transform t, float follow)>();

            foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var rule = FindRule(mf.name);
                if (rule != null && rule.mode == PartMode.Ignore) continue;
                TotalTriangles += (int)(mf.sharedMesh.GetIndexCount(0) / 3);
                for (int s = 1; s < mf.sharedMesh.subMeshCount; s++) TotalTriangles += (int)(mf.sharedMesh.GetIndexCount(s) / 3);

                if (rule != null && rule.mode == PartMode.Rigid)
                {
                    pendingRigid.Add((mf.transform, rule.rigidFollow));
                    continue;
                }
                if (!mf.sharedMesh.isReadable)
                {
                    UnityEngine.Debug.LogWarning($"[DamageLab] Mesh '{mf.sharedMesh.name}' is not readable; enable Read/Write on the model importer.", this);
                    continue;
                }
                var part = new DeformablePart(mf, transform, rule != null ? rule.crumple : 1f);
                parts.Add(part);
                DeformableVertices += part.VertexCount;
                if (rule != null && rule.definesImpactFaces)
                {
                    var b = part.VehicleBounds();
                    if (!hasFaceBounds) { faceBounds = b; hasFaceBounds = true; }
                    else faceBounds.Encapsulate(b);
                }
            }
            if (!hasFaceBounds)
            {
                foreach (var p in parts)
                {
                    if (!hasFaceBounds) { faceBounds = p.VehicleBounds(); hasFaceBounds = true; }
                    else faceBounds.Encapsulate(p.VehicleBounds());
                }
            }

            BodyBounds = faceBounds;
            fields = new CrushField[DamageDirections.Count];
            for (int d = 0; d < DamageDirections.Count; d++)
            {
                var region = profile.Get((DamageDirection)d);
                region.direction = (DamageDirection)d;
                fields[d] = new CrushField(region, faceBounds);
            }
            foreach (var p in parts) p.Precompute(fields);
            foreach (var (t, follow) in pendingRigid)
            {
                var rp = new RigidPart(t, transform, follow);
                rp.Precompute(fields);
                rigidParts.Add(rp);
            }
            impactField = new ImpactField(profile.impact);
            var wheelCenters = new Vector3[rigidParts.Count];
            for (int i = 0; i < rigidParts.Count; i++) wheelCenters[i] = rigidParts[i].VehicleCenter;
            impactField.SetStructure(wheelCenters, profile.roofLineStart);
            foreach (var im in impacts) AccumulateImpact(im);

            // Static picking colliders built from the ORIGINAL meshes (never re-cooked as the car deforms).
            // Skipped when the car is a dynamic Rigidbody: concave MeshColliders cannot be part of it.
            if (createPickingColliders)
            {
                foreach (var p in parts) AddPickCollider(p.filter.gameObject, p.sourceMesh);
                foreach (var rp in rigidParts)
                {
                    var mf = rp.transform.GetComponent<MeshFilter>();
                    if (mf != null) AddPickCollider(mf.gameObject, mf.sharedMesh);
                }
            }

            IsInitialized = true;
            if (wireframe) SetWireframe(true);
            for (int d = 0; d < DamageDirections.Count; d++) applied[d] = 0f;
            dirty = IsDamaged;
        }

        /// <summary>Restore the imported meshes and transforms and release runtime data.</summary>
        public void Shutdown()
        {
            if (!IsInitialized) return;
            foreach (var p in parts) p.Dispose();
            foreach (var rp in rigidParts) rp.Restore();
            foreach (var go in rigidWireframes)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (Application.isPlaying) { Destroy(mf.sharedMesh); Destroy(go); }
                else { DestroyImmediate(mf.sharedMesh); DestroyImmediate(go); }
            }
            foreach (var c in pickColliders)
            {
                if (c == null) continue;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            pickColliders.Clear();
            parts.Clear();
            rigidParts.Clear();
            rigidWireframes.Clear();
            fields = null;
            impactField = null;
            IsInitialized = false;
        }

        /// <summary>Re-run setup (e.g. after the profile was edited in play mode).</summary>
        public void Rebuild()
        {
            Shutdown();
            Initialize();
            dirty = true;
        }

        void AddPickCollider(GameObject go, Mesh mesh)
        {
            if (mesh == null) return;
            var mc = go.AddComponent<MeshCollider>();
            mc.hideFlags = HideFlags.DontSave; // runtime-only; never written into the scene
            mc.sharedMesh = mesh;
            pickColliders.Add(mc);
        }

        VehiclePartRule FindRule(string objectName)
        {
            foreach (var r in partRules)
                if (!string.IsNullOrEmpty(r.nameContains) && objectName.IndexOf(r.nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return r;
            return null;
        }

        // ------------------------------------------------------------------ deformation

        void ApplyDeformation()
        {
            dirty = false;
            if (!IsInitialized) return;
            stopwatch.Restart();
            for (int d = 0; d < DamageDirections.Count; d++) states[d] = fields[d].Evaluate(applied[d] / 100f);
            float roofStart = profile.roofLineStart, roofEnd = profile.roofLineEnd;
            foreach (var p in parts) p.Apply(fields, states, roofStart, roofEnd, impactField);
            foreach (var rp in rigidParts) rp.Apply(fields, states, roofStart, roofEnd);
            stopwatch.Stop();
            LastDeformMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
            Deformed?.Invoke();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // Live tuning: when the profile or rules change in play mode, rebuild the fields.
            if (Application.isPlaying && IsInitialized) dirty = true;
        }
#endif
    }
}
