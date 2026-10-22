using System.Collections.Generic;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>
    /// Mounts a loadout's items on one car. Contract (architecture-v1.md sections 7.1 and 7.3):
    /// - items live under an "Attachments" child of the car root, one socket node per slot, so they follow the Rigidbody with no extra script;
    /// - item prefabs carry no Collider, Rigidbody or Joint (any found is stripped and counted in <see cref="StrippedPhysicsComponents"/>);
    /// - Stage 2: each <see cref="AttachmentDefinition.proxies"/> box becomes a BoxCollider child of the item, on the car's own Rigidbody, tagged with
    ///   <see cref="DamageLab.CollisionTest.AttachmentProxy"/> (slot, item id). It copies the shell's physics material and layer. Proxies change neither mass nor
    ///   centre of mass (both are set by hand on the rig), and the inertia tensor is held at its no-attachment value while any proxy is mounted (see
    ///   <see cref="LockInertia"/>). A cleared slot's proxies leave the simulation at once (deactivated before the deferred destroy);
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
        readonly List<BoxCollider> proxies = new List<BoxCollider>();
        bool wrecked;
        Rigidbody body;
        bool inertiaLocked;
        Vector3 bareInertia;
        Quaternion bareInertiaRotation;

        /// <summary>The loadout last passed to <see cref="Apply"/> and what the validator said about it.</summary>
        public LoadoutResult LastResult { get; private set; }
        /// <summary>Colliders / Rigidbodies / Joints removed from item instances (should stay 0: the catalog audit and the editor tests check the prefabs).</summary>
        public int StrippedPhysicsComponents { get; private set; }
        public bool Wrecked => wrecked;
        public string VehicleKey => vehicleKey;
        public Transform Root => attachRoot;
        public int InstanceCount { get { int n = 0; foreach (var i in instances) if (i != null) n++; return n; } }
        public int RendererCount => renderers.Count;
        /// <summary>Proxy colliders currently mounted (all slots).</summary>
        public int ProxyCount => proxies.Count;
        public IReadOnlyList<BoxCollider> Proxies => proxies;
        /// <summary>True while the Rigidbody's inertia is held at its no-attachment value (some proxy is mounted).</summary>
        public bool InertiaLocked => inertiaLocked;

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
            go.AddComponent<DamageLab.Vehicle.AttachmentRoot>();   // the damage model never treats anything below this node as a body part, in any mounting order
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
            if (changed) { RebuildRendererList(); RebuildProxyList(); }
            if (wrecked) ApplyWreckMaterials(true);
        }

        void ClearSlot(int s)
        {
            if (instances[s] != null)
            {
                instances[s].SetActive(false);   // Object.Destroy is deferred to the end of the frame: deactivating takes its proxies out of the simulation now
                Destroy(instances[s]);
            }
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
            AddProxies(inst, s, def);
            instances[s] = inst;
            mountedIds[s] = def.id;
        }

        void AddProxies(GameObject inst, int s, AttachmentDefinition def)
        {
            if (def.proxies == null || def.proxies.Length == 0) return;
            if (body == null) body = GetComponent<Rigidbody>();
            if (body == null) return;   // a car without a body (a preview) has nothing to collide with
            LockInertia();
            var shell = ShellBox();
            for (int i = 0; i < def.proxies.Length; i++)
            {
                var go = new GameObject("Proxy_" + i);
                go.layer = shell != null ? shell.gameObject.layer : gameObject.layer;
                go.transform.SetParent(inst.transform, false);
                var bc = go.AddComponent<BoxCollider>();        // the collider first: the marker registers the colliders it finds when it is enabled
                bc.center = def.proxies[i].center;
                bc.size = def.proxies[i].size;
                bc.sharedMaterial = shell != null ? shell.sharedMaterial : null;
                var marker = go.AddComponent<DamageLab.CollisionTest.AttachmentProxy>();
                marker.slot = s;
                marker.itemId = def.id;
                marker.Register();   // explicit: also covers a marker that was enabled before its fields were set
            }
        }

        /// <summary>The first shell box of the car (the rig's "PhysicsShape" children): the proxies copy its physics material and layer.</summary>
        BoxCollider ShellBox()
        {
            var shapes = transform.Find("PhysicsShape");
            if (shapes != null)
                foreach (var bc in shapes.GetComponentsInChildren<BoxCollider>(true))
                    if (!DamageLab.CollisionTest.AttachmentProxy.IsProxy(bc)) return bc;
            return null;
        }

        /// <summary>
        /// Holds the inertia tensor at its value without attachments while any proxy is mounted, so a proxy never changes how the car turns or rolls. The value
        /// is read before the first proxy is added; once no proxy remains the tensor is handed back to PhysX (automatic again). Trade-off: while locked, the
        /// small inertia change PhysX would compute when crush refits resize the shell boxes is not applied (the locked value is the as-built one).
        /// </summary>
        void LockInertia()
        {
            if (inertiaLocked) return;
            bareInertia = body.inertiaTensor;
            bareInertiaRotation = body.inertiaTensorRotation;
            body.inertiaTensor = bareInertia;
            body.inertiaTensorRotation = bareInertiaRotation;
            inertiaLocked = true;
        }

        void RebuildProxyList()
        {
            proxies.Clear();
            foreach (var inst in instances)
            {
                if (inst == null) continue;
                foreach (var bc in inst.GetComponentsInChildren<BoxCollider>(true))
                    if (DamageLab.CollisionTest.AttachmentProxy.IsProxy(bc)) proxies.Add(bc);
            }
            if (proxies.Count == 0 && inertiaLocked && body != null)
            {
                body.ResetInertiaTensor();
                inertiaLocked = false;
            }
        }

        void StripPhysics(GameObject inst)
        {
            int found = 0;
            foreach (var j in inst.GetComponentsInChildren<Joint>(true)) { found++; Destroy(j); }          // joints first: they require a Rigidbody
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) { found++; Destroy(c); }
            foreach (var r in inst.GetComponentsInChildren<Rigidbody>(true)) { found++; Destroy(r); }
            StrippedPhysicsComponents += found;
            if (found > 0) Debug.LogWarning($"[Attachments] Item '{inst.name}' carried physics components; they were removed (collision comes from the definition's proxies only).", inst);
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
            RebuildProxyList();
        }
    }
}
