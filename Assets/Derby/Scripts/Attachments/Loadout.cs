using System;

namespace CarWreck.Derby
{
    /// <summary>
    /// The persistent, selected configuration for one vehicle: which item id sits in each slot ("" = empty). No runtime state lives here.
    /// Unknown ids are kept (and flagged by <see cref="LoadoutValidator"/>), never silently dropped.
    /// </summary>
    [Serializable]
    public class Loadout
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string vehicleKey = "";
        public string[] slotItemIds = new string[VehicleAttachmentFit.SlotCount];

        public Loadout() { Normalize(); }
        public Loadout(string vehicleKey) { this.vehicleKey = vehicleKey ?? ""; Normalize(); }

        public string Get(AttachmentSlot s) { Normalize(); return slotItemIds[(int)s]; }
        public void Set(AttachmentSlot s, string id) { Normalize(); slotItemIds[(int)s] = id ?? ""; }

        public bool IsEmpty
        {
            get
            {
                if (slotItemIds == null) return true;
                foreach (var s in slotItemIds) if (!string.IsNullOrEmpty(s)) return false;
                return true;
            }
        }

        /// <summary>Makes the slot array exactly SlotCount long with no nulls (JsonUtility can produce short or null arrays).</summary>
        public void Normalize()
        {
            vehicleKey = vehicleKey ?? "";
            if (slotItemIds != null && slotItemIds.Length == VehicleAttachmentFit.SlotCount && Array.IndexOf(slotItemIds, null) < 0) return;
            var fixedIds = new string[VehicleAttachmentFit.SlotCount];
            for (int i = 0; i < fixedIds.Length; i++) fixedIds[i] = slotItemIds != null && i < slotItemIds.Length && slotItemIds[i] != null ? slotItemIds[i] : "";
            slotItemIds = fixedIds;
        }

        public Loadout Clone()
        {
            Normalize();
            var c = new Loadout(vehicleKey) { version = version };
            Array.Copy(slotItemIds, c.slotItemIds, slotItemIds.Length);
            return c;
        }

        public static bool SameItems(Loadout a, Loadout b)
        {
            if (a == null || b == null) return a == b;
            a.Normalize(); b.Normalize();
            for (int i = 0; i < VehicleAttachmentFit.SlotCount; i++) if (a.slotItemIds[i] != b.slotItemIds[i]) return false;
            return true;
        }
    }
}
