using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Header("Debug")]
        public Material wireframeMaterial;
        [SerializeField] bool wireframe;

        /// <summary>Raised after the mesh has been rebuilt.</summary>
        public event Action Deformed;

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

        public void ResetAll()
        {
            front = rear = left = right = 0f;
        }

        public bool IsDamaged
        {
            get
            {
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
            parts.Clear();
            rigidParts.Clear();
            rigidWireframes.Clear();
            fields = null;
            IsInitialized = false;
        }

        /// <summary>Re-run setup (e.g. after the profile was edited in play mode).</summary>
        public void Rebuild()
        {
            Shutdown();
            Initialize();
            dirty = true;
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
            foreach (var p in parts) p.Apply(fields, states, roofStart, roofEnd);
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
