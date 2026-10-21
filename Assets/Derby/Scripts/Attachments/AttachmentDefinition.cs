using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>The five universal mounting slots. The index of a slot is its position in <see cref="Loadout.slotItemIds"/> and <see cref="VehicleAttachmentFit.sockets"/>.</summary>
    public enum AttachmentSlot { Front = 0, Rear = 1, Left = 2, Right = 3, Roof = 4 }

    /// <summary>Display metadata only: rarity never scales energy, points, durability or cost.</summary>
    public enum AttachmentRarity { Common, Uncommon, Rare, Epic, Legendary }

    /// <summary>
    /// One attachment item. Stage 1 (visual only): the prefab is mounted as plain renderers, so <see cref="designMassKg"/> is informational and is never
    /// written to a Rigidbody. The id is the stable key saved loadouts refer to: never rename it.
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
    }
}
