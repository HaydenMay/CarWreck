using System;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>Where one slot sits on a car, in car-root space. +Z of the socket points outward from the slot. Euler angles (not a Quaternion) so a default socket is a valid identity.</summary>
    [Serializable]
    public struct AttachmentSocket
    {
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        [Tooltip("Largest box an item in this slot may occupy (metres).")] public Vector3 envelopeSize;
    }

    /// <summary>A per-vehicle replacement for an item's default prefab (mounting plate, offsets). The functional shape stays identical; non-uniform scaling is never used to fit.</summary>
    [Serializable]
    public struct BracketOverride
    {
        public string itemId;
        public GameObject bracketPrefab;
        public Vector3 offset;
        [Range(0.9f, 1.1f)] public float uniformScale;
    }

    /// <summary>
    /// Attachment fit data for one body: capacity and the five sockets. A separate asset so the Opus-owned VehicleDefinition assets are not touched.
    /// <see cref="vehicleKey"/> is the VehicleDefinition asset name and the key under which that body's loadout is saved.
    /// </summary>
    [CreateAssetMenu(menuName = "CarWreck/Vehicle Attachment Fit", fileName = "AttachmentFit")]
    public class VehicleAttachmentFit : ScriptableObject
    {
        public const int SlotCount = 5;

        [Tooltip("VehicleDefinition asset name (e.g. CompactHatchback).")] public string vehicleKey;
        [Tooltip("Total capacity points this body can carry.")] public int capacity = 8;
        [Tooltip("Indexed by AttachmentSlot. Positions are placed in milestone M2.")] public AttachmentSocket[] sockets = new AttachmentSocket[SlotCount];
        public BracketOverride[] overrides = new BracketOverride[0];
    }
}
