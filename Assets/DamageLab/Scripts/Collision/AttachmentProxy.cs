using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Marks the collider(s) on this GameObject as an attachment's contact proxy: a collider on the car's OWN Rigidbody that stands for a mounted item.
    /// Opt-in and inert when absent. The collision receiver treats a proxy as a shape of its own:
    /// - it is not part of the car's shell: it never enters the shell bounds or the face fill, and it is never reported as the struck shell collider
    ///   (the report names the nearest shell region instead and carries the slot in <see cref="CollisionDamageReceiver.Report.ownSlot"/>);
    /// - as an impactor it shapes the dent footprint by itself (the other body's shell boxes are not merged into it);
    /// - the hit is still credited to the car (same Rigidbody, same cooldown, same one-event-per-pair behaviour), so attribution and scoring are untouched.
    /// It adds no damage factor: energy comes from masses and speeds exactly as before (architecture-v1.md section 7.3).
    /// Create the collider first, then add this component (or call <see cref="Register"/> after changing colliders).
    /// </summary>
    [DisallowMultipleComponent, ExecuteAlways]   // ExecuteAlways: the editor-mode validations (no play mode) register proxies the same way the game does
    public class AttachmentProxy : MonoBehaviour
    {
        [Tooltip("The attachment slot this proxy belongs to (the game's AttachmentSlot index: 0 front, 1 rear, 2 left, 3 right, 4 roof).")]
        public int slot = -1;
        [Tooltip("The attachment item id (for reports and diagnostics).")]
        public string itemId = "";

        static readonly Dictionary<Collider, AttachmentProxy> registry = new Dictionary<Collider, AttachmentProxy>();
        readonly List<Collider> own = new List<Collider>(2);

        /// <summary>True when the collider is a registered attachment proxy (O(1), no allocation).</summary>
        public static bool IsProxy(Collider c) => c != null && registry.ContainsKey(c);

        public static bool TryGet(Collider c, out AttachmentProxy proxy)
        {
            if (c != null && registry.TryGetValue(c, out proxy)) return true;
            proxy = null;
            return false;
        }

        void OnEnable() => Register();
        void OnDisable() => Unregister();

        /// <summary>(Re)registers the colliders on this GameObject.</summary>
        public void Register()
        {
            Unregister();
            GetComponents(own);
            foreach (var c in own) if (c != null) registry[c] = this;
        }

        void Unregister()
        {
            foreach (var c in own) if (c != null) registry.Remove(c);
            own.Clear();
        }
    }
}
