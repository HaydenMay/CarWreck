using System.Collections.Generic;

namespace CarWreck.Derby
{
    public enum LoadoutError { UnknownItem, WrongSlot, MissingAsset, IllegalCombo, OverBudget, NoFit }

    public readonly struct LoadoutIssue
    {
        public readonly LoadoutError error;
        /// <summary>Slot the problem is on, or -1 for loadout-wide problems (OverBudget, NoFit).</summary>
        public readonly int slot;
        public readonly string itemId;
        public readonly string message;
        public LoadoutIssue(LoadoutError error, int slot, string itemId, string message) { this.error = error; this.slot = slot; this.itemId = itemId; this.message = message; }
        public override string ToString() => $"{error}: {message}";
    }

    public sealed class LoadoutResult
    {
        public readonly List<LoadoutIssue> issues = new List<LoadoutIssue>();
        public int totalCost;
        public int capacity;
        public bool IsValid => issues.Count == 0;
        public bool HasIssue(LoadoutError e) { foreach (var i in issues) if (i.error == e) return true; return false; }
        public bool HasIssue(LoadoutError e, int slot) { foreach (var i in issues) if (i.error == e && i.slot == slot) return true; return false; }
    }

    /// <summary>
    /// Checks a loadout against a body's fit and the catalog. Pure: no scene access. Problems are reported per slot for the UI (invalid slots red, START disabled);
    /// nothing is repaired or dropped here. <see cref="ValidSubset"/> is the defensive match-start path: it keeps only the sound slots and the caller logs once.
    /// </summary>
    public static class LoadoutValidator
    {
        public static LoadoutResult Validate(Loadout loadout, VehicleAttachmentFit fit, AttachmentCatalog catalog)
        {
            var r = new LoadoutResult();
            if (loadout == null) return r;
            loadout.Normalize();
            if (fit == null)
            {
                if (!loadout.IsEmpty) r.issues.Add(new LoadoutIssue(LoadoutError.NoFit, -1, "", $"No attachment fit exists for vehicle '{loadout.vehicleKey}'."));
                return r;
            }
            r.capacity = fit.capacity;
            var equipped = new List<AttachmentDefinition>();
            for (int s = 0; s < VehicleAttachmentFit.SlotCount; s++)
            {
                string id = loadout.slotItemIds[s];
                if (string.IsNullOrEmpty(id)) continue;
                var def = catalog != null ? catalog.Find(id) : null;
                if (def == null) { r.issues.Add(new LoadoutIssue(LoadoutError.UnknownItem, s, id, $"Item '{id}' in slot {(AttachmentSlot)s} does not exist.")); continue; }
                if ((int)def.slot != s) { r.issues.Add(new LoadoutIssue(LoadoutError.WrongSlot, s, id, $"Item '{id}' belongs in slot {def.slot}, not {(AttachmentSlot)s}.")); continue; }
                if (def.prefab == null) { r.issues.Add(new LoadoutIssue(LoadoutError.MissingAsset, s, id, $"Item '{id}' has no prefab.")); continue; }
                equipped.Add(def);
                r.totalCost += def.capacityCost;
            }
            for (int i = 0; i < equipped.Count; i++)
                for (int j = i + 1; j < equipped.Count; j++)
                    if (Conflicts(equipped[i], equipped[j]))
                        r.issues.Add(new LoadoutIssue(LoadoutError.IllegalCombo, (int)equipped[j].slot, equipped[j].id, $"Items '{equipped[i].id}' and '{equipped[j].id}' cannot be equipped together."));
            if (r.totalCost > fit.capacity)
                r.issues.Add(new LoadoutIssue(LoadoutError.OverBudget, -1, "", $"Loadout costs {r.totalCost} but {fit.vehicleKey} can carry {fit.capacity}."));
            return r;
        }

        static bool Conflicts(AttachmentDefinition a, AttachmentDefinition b)
        {
            foreach (var x in a.incompatibleWith ?? new string[0]) if (x == b.id) return true;
            foreach (var x in b.incompatibleWith ?? new string[0]) if (x == a.id) return true;
            return false;
        }

        /// <summary>
        /// The sound slots of a loadout, as a new loadout. A slot with its own problem is emptied; for a combo or overspend, later slots are emptied until the rest is
        /// valid. Used only as a defensive fallback at match start; the UI never calls this.
        /// </summary>
        public static Loadout ValidSubset(Loadout loadout, VehicleAttachmentFit fit, AttachmentCatalog catalog)
        {
            if (loadout == null) return new Loadout();
            if (fit == null) return new Loadout(loadout.vehicleKey);
            var result = loadout.Clone();
            foreach (var i in Validate(result, fit, catalog).issues) if (i.slot >= 0) result.slotItemIds[i.slot] = "";
            for (int s = VehicleAttachmentFit.SlotCount - 1; s >= 0 && !Validate(result, fit, catalog).IsValid; s--) result.slotItemIds[s] = "";
            return result;
        }
    }
}
