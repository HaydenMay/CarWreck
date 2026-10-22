using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>The five universal mounting slots. The index of a slot is its position in <see cref="Loadout.slotItemIds"/> and <see cref="VehicleAttachmentFit.sockets"/>.</summary>
    public enum AttachmentSlot { Front = 0, Rear = 1, Left = 2, Right = 3, Roof = 4 }

    /// <summary>Display metadata only: rarity never scales energy, points, durability or cost.</summary>
    public enum AttachmentRarity { Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>
    /// One collision box of an item, in the item prefab's frame (pivot on the mount plane, +Z outward, metres). The mount turns each one into a BoxCollider on the
    /// car's own Rigidbody, tagged with <see cref="DamageLab.CollisionTest.AttachmentProxy"/> so the damage engine knows the item led the hit.
    /// </summary>
    [System.Serializable]
    public struct ProxySpec
    {
        public Vector3 center;
        public Vector3 size;
        public ProxySpec(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
    }

    /// <summary>
    /// One attachment item. The prefab is mounted as plain renderers; any collision shape comes from <see cref="proxies"/> (Stage 2), never from the prefab.
    /// <see cref="designMassKg"/> is informational and is never written to a Rigidbody. The id is the stable key saved loadouts refer to: never rename it.
    /// </summary>
    [CreateAssetMenu(menuName = "CarWreck/Attachment Definition", fileName = "Attachment")]
    public class AttachmentDefinition : ScriptableObject
    {
        [Tooltip("Stable lower-case id (e.g. push_bar_junk). Saved loadouts reference it; never rename.")]
        public string id;
        public string displayName;
        public AttachmentSlot slot;
        [TextArea, Tooltip("Short text for the Garage card (a sentence or two). Describes the look; do not promise effects that do not exist.")] public string description;
        [Tooltip("Display only.")] public AttachmentRarity rarity;
        [Tooltip("Capacity points this item uses (>= 0). Independent of rarity and mass.")] public int capacityCost = 1;
        [Tooltip("Visual prefab, authored once facing +Z outward from the slot (see architecture-v1.md section 6).")] public GameObject prefab;
        [Tooltip("INFORMATIONAL in V1: never applied to the Rigidbody.")] public float designMassKg;
        [Tooltip("Ids of items that cannot be equipped together with this one (checked in both directions).")] public string[] incompatibleWith = new string[0];
        [Tooltip("Collision boxes in the prefab frame (pivot on the mount plane, +Z outward). Empty = visual only. They add no mass and do not change the inertia.")]
        public ProxySpec[] proxies = new ProxySpec[0];
    }
}
