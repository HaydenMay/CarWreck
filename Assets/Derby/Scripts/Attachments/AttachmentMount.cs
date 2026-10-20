using System.Collections.Generic;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>
    /// Mounts a loadout's items on one car as plain visuals. Stage 1 contract (architecture-v1.md section 7.1):
    /// - items live under an "Attachments" child of the car root, one socket node per slot, so they follow the Rigidbody with no extra script;
    /// - they carry no Collider, Rigidbody or Joint (any found in a prefab is stripped and counted) and change no mass;
    /// - the mount MUST be added after <c>VehicleDamageModel.Initialize()</c>. Initialize returns early when already initialized and the derby never calls Rebuild,
    ///   so the model never treats an attachment mesh as a deformable part. That ordering is load-bearing: do not move the mount before Initialize.
    /// Applying a loadout is a per-slot diff: a slot whose item did not change keeps its instance, so repeated Apply / ResetVisuals calls allocate nothing and never
    /// duplicate objects. Only <see cref="Apply"/> with a changed slot creates or destroys instances (menu choices, vehicle switch).
    /// </summary>
    public class AttachmentMount : MonoBehaviour
    {
        public const string RootName = "Attachments";

        struct Mounted
        {
            public Renderer renderer;
            public Material[] original;
            public Material[] wreck;
        }

        AttachmentCatalog catalog;
        VehicleAttachmentFit fit;
        string vehicleKey = "";
        Transform attachRoot;
        readonly Transform[] slotRoots = new Transform[VehicleAttachmentFit.SlotCount];
        readonly GameObject[] instances = new GameObject[VehicleAttachmentFit.SlotCount];
        readonly string[] mountedIds = new string[VehicleAttachmentFit.SlotCount];
        readonly List<Mounted> renderers = new List<Mounted>();
        bool wrecked;

        /// <summary>The loadout last passed to <see cref="Apply"/> and what the validator said about it.</summary>
        public LoadoutResult LastResult { get; private set; }
        /// <summary>Colliders / Rigidbodies / Joints removed from item instances (should stay 0: the catalog audit and the editor tests check the prefabs).</summary>
        public int StrippedPhysicsComponents { get; private set; }
        public bool Wrecked => wrecked;
        public string VehicleKey => vehicleKey;
        public Transform Root => attachRoot;
        public int InstanceCount { get { int n = 0; foreach (var i in instances) if (i != null) n++; return n; } }
        public int RendererCount => renderers.Count;

        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o);
        }

        public void Configure(AttachmentCatalog catalog, string vehicleKey)
        {
            this.catalog = catalog;
            this.vehicleKey = vehicleKey ?? "";
            fit = catalog != null ? catalog.FitFor(this.vehicleKey) : null;
            for (int i = 0; i < mountedIds.Length; i++) if (mountedIds[i] == null) mountedIds[i] = "";
        }

        public string MountedId(AttachmentSlot s) => mountedIds[(int)s] ?? "";
        public GameObject Instance(AttachmentSlot s) => instances[(int)s];

        void EnsureRoot()
        {
            if (attachRoot != null) return;
            var go = new GameObject(RootName);
            attachRoot = go.transform;
            attachRoot.SetParent(transform, false);
        }

        /// <summary>
        /// Mounts the sound slots of <paramref name="desired"/> (the validator's <see cref="LoadoutValidator.ValidSubset"/>: an unsound slot or an overspend is left empty,
        /// never half-applied). The result of validating the request itself is kept in <see cref="LastResult"/> for the UI and the log.
        /// </summary>
        public void Apply(Loadout desired)
        {
            LastResult = LoadoutValidator.Validate(desired, fit, catalog);
            var use = desired == null || fit == null ? new Loadout(vehicleKey) : LoadoutValidator.ValidSubset(desired, fit, catalog);
            bool changed = false;
            for (int s = 0; s < VehicleAttachmentFit.SlotCount; s++)
            {
                string want = use.slotItemIds[s] ?? "";
                if (want == (mountedIds[s] ?? "") && (want == "" || instances[s] != null)) continue;
                ClearSlot(s);
                if (want != "") MountSlot(s, catalog.Find(want));
                changed = true;
            }
            if (changed) RebuildRendererList();
            if (wrecked) ApplyWreckMaterials(true);
        }

        void ClearSlot(int s)
        {
            if (instances[s] != null) Destroy(instances[s]);
            instances[s] = null;
            mountedIds[s] = "";
        }

        void MountSlot(int s, AttachmentDefinition def)
        {
            EnsureRoot();
            if (slotRoots[s] == null)
            {
                var n = new GameObject("Slot_" + (AttachmentSlot)s).transform;
                n.SetParent(attachRoot, false);
                slotRoots[s] = n;
            }
            var socket = fit.sockets[s];
            slotRoots[s].localPosition = socket.localPosition;
            slotRoots[s].localRotation = Quaternion.Euler(socket.localEulerAngles);
            slotRoots[s].localScale = Vector3.one;

            GameObject prefab = def.prefab;
            Vector3 offset = Vector3.zero;
            float scale = 1f;
            foreach (var o in fit.overrides ?? new BracketOverride[0])
                if (o.itemId == def.id && o.bracketPrefab != null)
                {
                    prefab = o.bracketPrefab; offset = o.offset; scale = o.uniformScale > 0f ? Mathf.Clamp(o.uniformScale, 0.9f, 1.1f) : 1f;
                    break;
                }
            var inst = Instantiate(prefab, slotRoots[s]);
            inst.name = def.id;
            inst.transform.localPosition = offset;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one * scale;   // uniform only: functional shapes are never stretched
            StripPhysics(inst);
            instances[s] = inst;
            mountedIds[s] = def.id;
        }

        void StripPhysics(GameObject inst)
        {
            int found = 0;
            foreach (var j in inst.GetComponentsInChildren<Joint>(true)) { found++; Destroy(j); }          // joints first: they require a Rigidbody
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) { found++; Destroy(c); }
            foreach (var r in inst.GetComponentsInChildren<Rigidbody>(true)) { found++; Destroy(r); }
            StrippedPhysicsComponents += found;
            if (found > 0) Debug.LogWarning($"[Attachments] Item '{inst.name}' carried physics components; they were removed (Stage 1 items are visual only).", inst);
        }

        void RebuildRendererList()
        {
            renderers.Clear();
            foreach (var inst in instances)
            {
                if (inst == null) continue;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var original = r.sharedMaterials;
                    renderers.Add(new Mounted { renderer = r, original = original, wreck = null });
                }
            }
        }

        /// <summary>Back to the as-built look and pose (called from DerbyGame.ResetCar: match start, respawn, Showdown repair, rematch, menu).</summary>
        public void ResetVisuals()
        {
            for (int s = 0; s < slotRoots.Length; s++)
            {
                if (slotRoots[s] == null || fit == null) continue;
                slotRoots[s].localPosition = fit.sockets[s].localPosition;
                slotRoots[s].localRotation = Quaternion.Euler(fit.sockets[s].localEulerAngles);
            }
            SetWrecked(false);
        }

        /// <summary>Burnt-out look for a hulk: every item renderer takes the catalog's wreck material. Items stay mounted (they are part of the hulk).</summary>
        public void SetWrecked(bool on)
        {
            if (on == wrecked) return;
            wrecked = on;
            ApplyWreckMaterials(on);
        }

        void ApplyWreckMaterials(bool on)
        {
            var wreckMat = catalog != null ? catalog.wreckMaterial : null;
            for (int i = 0; i < renderers.Count; i++)
            {
                var m = renderers[i];
                if (m.renderer == null) continue;
                if (on)
                {
                    if (wreckMat == null) continue;
                    if (m.wreck == null || m.wreck.Length != m.original.Length)
                    {
                        m.wreck = new Material[m.original.Length];
                        for (int k = 0; k < m.wreck.Length; k++) m.wreck[k] = wreckMat;
                        renderers[i] = m;
                    }
                    m.renderer.sharedMaterials = m.wreck;
                }
                else m.renderer.sharedMaterials = m.original;
            }
        }

        /// <summary>Removes every item (the node and its sockets stay for reuse).</summary>
        public void Clear()
        {
            for (int s = 0; s < instances.Length; s++) ClearSlot(s);
            renderers.Clear();
        }
    }
}
