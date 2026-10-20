using System.Collections.Generic;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>Single lookup root for every attachment item and per-vehicle fit.</summary>
    [CreateAssetMenu(menuName = "CarWreck/Attachment Catalog", fileName = "AttachmentCatalog")]
    public class AttachmentCatalog : ScriptableObject
    {
        public AttachmentDefinition[] items = new AttachmentDefinition[0];
        public VehicleAttachmentFit[] fits = new VehicleAttachmentFit[0];

        public AttachmentDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id) || items == null) return null;
            foreach (var it in items) if (it != null && it.id == id) return it;
            return null;
        }

        public VehicleAttachmentFit FitFor(string vehicleKey)
        {
            if (string.IsNullOrEmpty(vehicleKey) || fits == null) return null;
            foreach (var f in fits) if (f != null && f.vehicleKey == vehicleKey) return f;
            return null;
        }

        /// <summary>Data problems in the catalog itself (not in a loadout). Empty when the catalog is sound.</summary>
        public List<string> Audit()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            var itemList = items ?? new AttachmentDefinition[0];
            foreach (var it in itemList)
            {
                if (it == null) { problems.Add("Catalog contains an empty item entry."); continue; }
                if (string.IsNullOrEmpty(it.id)) problems.Add($"Item '{it.name}' has no id.");
                else if (!ids.Add(it.id)) problems.Add($"Duplicate item id '{it.id}'.");
                else if (it.id != it.id.ToLowerInvariant() || it.id.Contains(" ")) problems.Add($"Item id '{it.id}' must be lower-case without spaces.");
                if (it.capacityCost < 0) problems.Add($"Item '{it.id}' has a negative capacity cost.");
                if (it.prefab == null) problems.Add($"Item '{it.id}' has no prefab.");
                foreach (var other in it.incompatibleWith ?? new string[0])
                    if (other == it.id) problems.Add($"Item '{it.id}' lists itself as incompatible.");
            }
            foreach (var it in itemList)
                if (it != null)
                    foreach (var other in it.incompatibleWith ?? new string[0])
                        if (!string.IsNullOrEmpty(other) && other != it.id && !ids.Contains(other)) problems.Add($"Item '{it.id}' lists unknown incompatible id '{other}'.");
            var keys = new HashSet<string>();
            foreach (var f in fits ?? new VehicleAttachmentFit[0])
            {
                if (f == null) { problems.Add("Catalog contains an empty fit entry."); continue; }
                if (string.IsNullOrEmpty(f.vehicleKey)) problems.Add($"Fit '{f.name}' has no vehicleKey.");
                else if (!keys.Add(f.vehicleKey)) problems.Add($"Duplicate fit for vehicle '{f.vehicleKey}'.");
                if (f.capacity < 0) problems.Add($"Fit '{f.vehicleKey}' has a negative capacity.");
                if (f.sockets == null || f.sockets.Length != VehicleAttachmentFit.SlotCount) problems.Add($"Fit '{f.vehicleKey}' must have exactly {VehicleAttachmentFit.SlotCount} sockets.");
            }
            return problems;
        }
    }
}
