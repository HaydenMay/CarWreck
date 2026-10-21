using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Attachment V1 / milestone M1 checks: the loadout validator, serialization round trip, per-vehicle persistence and the placeholder catalog.
    /// Pure data tests (no scene, no physics). Uses throwaway in-memory assets and a throwaway PlayerPrefs key; touches no project assets.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentValidation.Run -quit -logFile attachments.log
    /// </summary>
    public static class AttachmentValidation
    {
        static StringBuilder sb;
        static int fails, passes;

        [MenuItem("CarWreck/Validate Attachments (M1 data)", priority = 41)]
        public static void Run()
        {
            Debug.Log(Execute());
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static void Check(string name, bool ok, string detail = "")
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? ": " + detail : "")}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[Attachments] M1 data validation\n");
            fails = passes = 0;
            var junk = new System.Collections.Generic.List<Object>();
            try
            {
                var prefab = new GameObject("TestPrefab"); junk.Add(prefab);
                var front = Item(junk, "front_a", AttachmentSlot.Front, 2, prefab);
                var front2 = Item(junk, "front_b", AttachmentSlot.Front, 4, prefab);
                var roof = Item(junk, "roof_a", AttachmentSlot.Roof, 1, prefab);
                var rear = Item(junk, "rear_a", AttachmentSlot.Rear, 2, prefab);
                var noPrefab = Item(junk, "broken_a", AttachmentSlot.Left, 1, null);
                var clash1 = Item(junk, "clash_a", AttachmentSlot.Left, 1, prefab); clash1.incompatibleWith = new[] { "clash_b" };
                var clash2 = Item(junk, "clash_b", AttachmentSlot.Right, 1, prefab);
                var cat = ScriptableObject.CreateInstance<AttachmentCatalog>(); junk.Add(cat);
                cat.items = new[] { front, front2, roof, rear, noPrefab, clash1, clash2 };
                var fit = Fit(junk, "TestCar", 6);
                cat.fits = new[] { fit };

                Basic(fit, cat);
                Budget(fit, cat);
                Persistence(cat, fit);
                Presets();
                CatalogAudit(junk, prefab);
                PlaceholderAssets();
            }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { foreach (var o in junk) if (o != null) Object.DestroyImmediate(o); LoadoutStore.Delete("TestCar"); }
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        static AttachmentDefinition Item(System.Collections.Generic.List<Object> junk, string id, AttachmentSlot slot, int cost, GameObject prefab)
        {
            var d = ScriptableObject.CreateInstance<AttachmentDefinition>(); junk.Add(d);
            d.id = id; d.displayName = id; d.slot = slot; d.capacityCost = cost; d.prefab = prefab;
            return d;
        }

        static VehicleAttachmentFit Fit(System.Collections.Generic.List<Object> junk, string key, int capacity)
        {
            var f = ScriptableObject.CreateInstance<VehicleAttachmentFit>(); junk.Add(f);
            f.vehicleKey = key; f.capacity = capacity;
            return f;
        }

        static Loadout L(params (AttachmentSlot slot, string id)[] entries)
        {
            var l = new Loadout("TestCar");
            foreach (var e in entries) l.Set(e.slot, e.id);
            return l;
        }

        static void Basic(VehicleAttachmentFit fit, AttachmentCatalog cat)
        {
            Check("empty loadout is valid", LoadoutValidator.Validate(new Loadout("TestCar"), fit, cat).IsValid);
            Check("null loadout is valid (nothing to check)", LoadoutValidator.Validate(null, fit, cat).IsValid);
            var ok = LoadoutValidator.Validate(L((AttachmentSlot.Front, "front_a"), (AttachmentSlot.Roof, "roof_a")), fit, cat);
            Check("valid two-item loadout", ok.IsValid && ok.totalCost == 3 && ok.capacity == 6, $"cost {ok.totalCost}/{ok.capacity}");

            var unknown = LoadoutValidator.Validate(L((AttachmentSlot.Front, "does_not_exist")), fit, cat);
            Check("unknown id is flagged on its slot", unknown.HasIssue(LoadoutError.UnknownItem, (int)AttachmentSlot.Front) && !unknown.IsValid);

            var wrong = LoadoutValidator.Validate(L((AttachmentSlot.Rear, "front_a")), fit, cat);
            Check("item in the wrong slot is flagged", wrong.HasIssue(LoadoutError.WrongSlot, (int)AttachmentSlot.Rear));

            var missing = LoadoutValidator.Validate(L((AttachmentSlot.Left, "broken_a")), fit, cat);
            Check("item with no prefab is flagged MissingAsset", missing.HasIssue(LoadoutError.MissingAsset, (int)AttachmentSlot.Left));

            var combo = LoadoutValidator.Validate(L((AttachmentSlot.Left, "clash_a"), (AttachmentSlot.Right, "clash_b")), fit, cat);
            Check("incompatible pair is flagged IllegalCombo", combo.HasIssue(LoadoutError.IllegalCombo));
            var comboAlone = LoadoutValidator.Validate(L((AttachmentSlot.Right, "clash_b")), fit, cat);
            Check("one half of an incompatible pair alone is fine", comboAlone.IsValid);

            var noFit = LoadoutValidator.Validate(L((AttachmentSlot.Front, "front_a")), null, cat);
            Check("non-empty loadout without a fit is flagged NoFit", noFit.HasIssue(LoadoutError.NoFit));
            Check("empty loadout without a fit is valid", LoadoutValidator.Validate(new Loadout("TestCar"), null, cat).IsValid);

            var shortArr = new Loadout("TestCar") { slotItemIds = new[] { "front_a" } };
            Check("short/odd slot array is normalised, not a crash", LoadoutValidator.Validate(shortArr, fit, cat).IsValid && shortArr.slotItemIds.Length == VehicleAttachmentFit.SlotCount);
            var nulls = new Loadout("TestCar") { slotItemIds = new string[VehicleAttachmentFit.SlotCount] };
            Check("null entries are treated as empty", LoadoutValidator.Validate(nulls, fit, cat).IsValid && nulls.IsEmpty);
        }

        static void Budget(VehicleAttachmentFit fit, AttachmentCatalog cat)
        {
            // capacity 6: front_a (2) + rear_a (2) + roof_a (1) = 5.
            var atLimit = LoadoutValidator.Validate(L((AttachmentSlot.Front, "front_b"), (AttachmentSlot.Rear, "rear_a")), fit, cat);
            Check("exactly at capacity is valid", atLimit.IsValid && atLimit.totalCost == 6, $"cost {atLimit.totalCost}/{atLimit.capacity}");
            var over = LoadoutValidator.Validate(L((AttachmentSlot.Front, "front_b"), (AttachmentSlot.Rear, "rear_a"), (AttachmentSlot.Roof, "roof_a")), fit, cat);
            Check("one point over capacity is OverBudget", over.HasIssue(LoadoutError.OverBudget) && over.totalCost == 7, $"cost {over.totalCost}/{over.capacity}");

            var subset = LoadoutValidator.ValidSubset(L((AttachmentSlot.Front, "front_b"), (AttachmentSlot.Rear, "rear_a"), (AttachmentSlot.Roof, "roof_a"), (AttachmentSlot.Left, "nope")), fit, cat);
            Check("ValidSubset result is valid", LoadoutValidator.Validate(subset, fit, cat).IsValid);
            Check("ValidSubset drops the unknown slot and trims the overspend", subset.Get(AttachmentSlot.Left) == "" && subset.Get(AttachmentSlot.Roof) == "" && subset.Get(AttachmentSlot.Front) == "front_b");
            var original = L((AttachmentSlot.Left, "nope"));
            LoadoutValidator.ValidSubset(original, fit, cat);
            Check("ValidSubset does not modify the saved loadout", original.Get(AttachmentSlot.Left) == "nope");
        }

        static void Persistence(AttachmentCatalog cat, VehicleAttachmentFit fit)
        {
            var l = L((AttachmentSlot.Front, "front_a"), (AttachmentSlot.Roof, "roof_a"), (AttachmentSlot.Left, "retired_item"));
            string json = LoadoutStore.ToJson(l);
            Check("round trip keeps every slot, including an unknown id",
                LoadoutStore.TryParse(json, "TestCar", out var back) == LoadoutStore.ParseResult.Ok && Loadout.SameItems(l, back) && back.vehicleKey == "TestCar");
            Check("missing json -> Missing + empty loadout", LoadoutStore.TryParse("", "TestCar", out var m) == LoadoutStore.ParseResult.Missing && m.IsEmpty);
            Check("corrupt json -> Corrupt + empty loadout", LoadoutStore.TryParse("{not json", "TestCar", out var c) == LoadoutStore.ParseResult.Corrupt && c.IsEmpty);
            string v2 = json.Replace("\"version\":1", "\"version\":2");
            Check("other version -> UnknownVersion + empty loadout", LoadoutStore.TryParse(v2, "TestCar", out var u) == LoadoutStore.ParseResult.UnknownVersion && u.IsEmpty);
            Check("short saved array is normalised", LoadoutStore.TryParse("{\"version\":1,\"vehicleKey\":\"TestCar\",\"slotItemIds\":[\"front_a\"]}", "TestCar", out var s) == LoadoutStore.ParseResult.Ok && s.slotItemIds.Length == VehicleAttachmentFit.SlotCount && s.Get(AttachmentSlot.Front) == "front_a");

            LoadoutStore.Save(l);
            Check("PlayerPrefs save then load", LoadoutStore.Load("TestCar", out var loaded) == LoadoutStore.ParseResult.Ok && Loadout.SameItems(l, loaded));
            Check("another vehicle has its own (empty) slot", LoadoutStore.Load("TestCar_Other", out var other) == LoadoutStore.ParseResult.Missing && other.IsEmpty);
            Check("saved unknown id survives the load and is still flagged", LoadoutValidator.Validate(loaded, fit, cat).HasIssue(LoadoutError.UnknownItem, (int)AttachmentSlot.Left));
            LoadoutStore.Delete("TestCar");
            Check("delete removes the save", LoadoutStore.Load("TestCar", out _) == LoadoutStore.ParseResult.Missing);
        }

        static void Presets()
        {
            const string key = "TestCar";
            for (int i = 0; i < LoadoutPresets.Count; i++) LoadoutPresets.Delete(key, i);
            Check("saved builds: three slots per vehicle, all empty at first", LoadoutPresets.Count == 3 && !LoadoutPresets.Exists(key, 0) && LoadoutPresets.Load(key, 1, out var none) == LoadoutStore.ParseResult.Missing && none.IsEmpty);
            var l = L((AttachmentSlot.Front, "front_a"), (AttachmentSlot.Left, "retired_item"));
            LoadoutPresets.Save(key, 1, l);
            Check("a saved build round-trips with every slot (an unknown id is kept, not dropped)", LoadoutPresets.Exists(key, 1) && LoadoutPresets.Load(key, 1, out var back) == LoadoutStore.ParseResult.Ok && Loadout.SameItems(l, back) && back.vehicleKey == key);
            Check("other slots and other vehicles are untouched", !LoadoutPresets.Exists(key, 0) && !LoadoutPresets.Exists("TestCar_Other", 1));
            l.Set(AttachmentSlot.Front, "front_b");
            LoadoutPresets.Load(key, 1, out var again);
            Check("a saved build is a copy: editing the current loadout later does not change it", again.Get(AttachmentSlot.Front) == "front_a");
            LoadoutPresets.Save(key, 7, l); LoadoutPresets.Save(key, -1, l);
            Check("out-of-range slots are ignored safely", !LoadoutPresets.Exists(key, 7) && LoadoutPresets.Load(key, 7, out _) == LoadoutStore.ParseResult.Missing);
            LoadoutPresets.Delete(key, 1);
            Check("delete empties the slot", !LoadoutPresets.Exists(key, 1));
        }

        static void CatalogAudit(System.Collections.Generic.List<Object> junk, GameObject prefab)
        {
            var cat = ScriptableObject.CreateInstance<AttachmentCatalog>(); junk.Add(cat);
            var a = Item(junk, "dup", AttachmentSlot.Front, 1, prefab);
            var b = Item(junk, "dup", AttachmentSlot.Rear, 1, prefab);
            var neg = Item(junk, "neg", AttachmentSlot.Roof, -1, prefab);
            var up = Item(junk, "Upper Case", AttachmentSlot.Left, 1, prefab);
            var ref0 = Item(junk, "refs_unknown", AttachmentSlot.Right, 1, prefab); ref0.incompatibleWith = new[] { "ghost" };
            var f1 = Fit(junk, "Same", 4); var f2 = Fit(junk, "Same", 4); var f3 = Fit(junk, "BadSockets", 4); f3.sockets = new AttachmentSocket[2];
            cat.items = new[] { a, b, neg, up, ref0 }; cat.fits = new[] { f1, f2, f3 };
            string all = string.Join("\n", cat.Audit());
            Check("audit finds duplicate item ids", all.Contains("Duplicate item id 'dup'"));
            Check("audit finds a negative cost", all.Contains("negative capacity cost"));
            Check("audit finds a non-lower-case id", all.Contains("lower-case"));
            Check("audit finds an unknown incompatible id", all.Contains("unknown incompatible id 'ghost'"));
            Check("audit finds a duplicate fit", all.Contains("Duplicate fit for vehicle 'Same'"));
            Check("audit finds a fit with the wrong socket count", all.Contains("exactly 5 sockets"));
        }

        static void PlaceholderAssets()
        {
            var cat = AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentAssetBuilder.CatalogPath);
            if (cat == null) { Check("placeholder catalog exists (run CarWreck > Build Attachment Placeholders)", false, AttachmentAssetBuilder.CatalogPath); return; }
            var problems = cat.Audit();
            Check("placeholder catalog audits clean", problems.Count == 0, problems.Count == 0 ? $"{cat.items.Length} items, {cat.fits.Length} fits" : string.Join("; ", problems));
            Check("placeholder catalog has 4 items and 3 fits", cat.items.Length == 4 && cat.fits.Length == 3);
            bool colliderFree = true;
            foreach (var it in cat.items) if (it != null && it.prefab != null && it.prefab.GetComponentInChildren<Collider>(true) != null) colliderFree = false;
            Check("placeholder prefabs carry no colliders (Stage 1 is visual only)", colliderFree);
            bool described = true; foreach (var it in cat.items) if (it == null || string.IsNullOrWhiteSpace(it.description)) described = false;
            Check("every placeholder item has a Garage description", described);
            var hatch = cat.FitFor("CompactHatchback"); var muscle = cat.FitFor("MuscleCar"); var pick = cat.FitFor("CompactPickup");
            Check("placeholder capacities are 8 / 10 / 12 (hatchback / muscle / pickup)", hatch != null && muscle != null && pick != null && hatch.capacity == 8 && muscle.capacity == 10 && pick.capacity == 12);
            bool anyFits = false;
            if (hatch != null)
            {
                var full = new Loadout("CompactHatchback");
                full.Set(AttachmentSlot.Front, "push_bar_junk"); full.Set(AttachmentSlot.Rear, "tow_hitch_bumper"); full.Set(AttachmentSlot.Roof, "roof_cage_rack");
                anyFits = LoadoutValidator.Validate(full, hatch, cat).IsValid;
                full.Set(AttachmentSlot.Front, "spiked_ram_plate");
                Check("hatchback: push bar + hitch + rack fits (cost 5/8); ram + hitch + rack fits (7/8)", anyFits && LoadoutValidator.Validate(full, hatch, cat).IsValid);
            }
        }
    }
}
