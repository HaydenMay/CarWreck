using System.IO;
using UnityEditor;
using UnityEngine;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Creates (or refreshes) the PLACEHOLDER attachment data: a catalog, three per-vehicle fits and four grey-box items. Capacities are placeholders until playtests;
    /// sockets are left unplaced for milestone M2; the prefabs are plain grey cubes with no colliders. Safe to re-run: existing assets are updated in place.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentAssetBuilder.Build -quit
    /// </summary>
    public static class AttachmentAssetBuilder
    {
        public const string DataFolder = "Assets/Derby/Settings/Attachments";
        public const string ArtFolder = "Assets/Derby/Art/Attachments/GreyBox";
        public const string CatalogPath = DataFolder + "/AttachmentCatalog.asset";

        struct ItemSpec
        {
            public string id, name; public AttachmentSlot slot; public int cost; public float mass; public Vector3 size;
            public ItemSpec(string id, string name, AttachmentSlot slot, int cost, float mass, Vector3 size) { this.id = id; this.name = name; this.slot = slot; this.cost = cost; this.mass = mass; this.size = size; }
        }

        static readonly ItemSpec[] Items =
        {
            new ItemSpec("push_bar_junk", "Junk Push Bar", AttachmentSlot.Front, 2, 40f, new Vector3(1.6f, 0.35f, 0.25f)),
            new ItemSpec("spiked_ram_plate", "Spiked Ram Plate", AttachmentSlot.Front, 4, 70f, new Vector3(1.2f, 0.6f, 0.4f)),
            new ItemSpec("roof_cage_rack", "Roof Cage Rack", AttachmentSlot.Roof, 1, 25f, new Vector3(1.2f, 1.4f, 0.25f)),
            new ItemSpec("tow_hitch_bumper", "Tow Hitch Bumper", AttachmentSlot.Rear, 2, 35f, new Vector3(1.5f, 0.3f, 0.3f)),
        };

        // Placeholder capacities (owner-approved as placeholders; retune after playtests).
        static readonly (string key, int capacity)[] Fits = { ("CompactHatchback", 8), ("MuscleCar", 10), ("CompactPickup", 12) };

        [MenuItem("CarWreck/Build Attachment Placeholders", priority = 40)]
        public static void Build()
        {
            EnsureFolder(DataFolder); EnsureFolder(ArtFolder);
            var mat = LoadOrCreate<Material>(ArtFolder + "/Attach_GreyBox.mat", () => new Material(Shader.Find("Universal Render Pipeline/Lit")));
            mat.color = new Color(0.45f, 0.45f, 0.47f);
            EditorUtility.SetDirty(mat);
            var wreck = LoadOrCreate<Material>(ArtFolder + "/Attach_Wreck.mat", () => new Material(Shader.Find("Universal Render Pipeline/Lit")));
            wreck.color = new Color(0.1f, 0.095f, 0.09f);
            EditorUtility.SetDirty(wreck);

            var defs = new AttachmentDefinition[Items.Length];
            for (int i = 0; i < Items.Length; i++)
            {
                var spec = Items[i];
                var prefab = BuildPrefab(spec, mat);
                var def = LoadOrCreate<AttachmentDefinition>($"{DataFolder}/{spec.id}.asset", () => ScriptableObject.CreateInstance<AttachmentDefinition>());
                def.id = spec.id; def.displayName = spec.name; def.slot = spec.slot; def.rarity = AttachmentRarity.Common;
                def.capacityCost = spec.cost; def.designMassKg = spec.mass; def.prefab = prefab;
                if (def.incompatibleWith == null) def.incompatibleWith = new string[0];
                EditorUtility.SetDirty(def);
                defs[i] = def;
            }

            var fits = new VehicleAttachmentFit[Fits.Length];
            for (int i = 0; i < Fits.Length; i++)
            {
                var fit = LoadOrCreate<VehicleAttachmentFit>($"{DataFolder}/Fit_{Fits[i].key}.asset", () => ScriptableObject.CreateInstance<VehicleAttachmentFit>());
                fit.vehicleKey = Fits[i].key; fit.capacity = Fits[i].capacity;
                if (fit.sockets == null || fit.sockets.Length != VehicleAttachmentFit.SlotCount) fit.sockets = new AttachmentSocket[VehicleAttachmentFit.SlotCount];
                fit.overrides = new BracketOverride[0];
                if (Fits[i].key == "CompactPickup")
                {
                    // The pickup's cab roof is only 1.25 m long, so the roof rack gets a shorter authored variant (same item, same cost): no stretching or squashing.
                    var shortRack = BuildPrefab(new ItemSpec("roof_cage_rack__bracket_CompactPickup", "", AttachmentSlot.Roof, 0, 0f, new Vector3(1.2f, 1.1f, 0.25f)), mat);
                    fit.overrides = new[] { new BracketOverride { itemId = "roof_cage_rack", bracketPrefab = shortRack, offset = Vector3.zero, uniformScale = 1f } };
                }
                EditorUtility.SetDirty(fit);
                fits[i] = fit;
            }

            var catalog = LoadOrCreate<AttachmentCatalog>(CatalogPath, () => ScriptableObject.CreateInstance<AttachmentCatalog>());
            catalog.items = defs; catalog.fits = fits; catalog.wreckMaterial = wreck;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var problems = catalog.Audit();
            Debug.Log($"[Attachments] Placeholder data built: {defs.Length} items, {fits.Length} fits, {problems.Count} catalog problems.");
            foreach (var p in problems) Debug.LogWarning("[Attachments] " + p);
        }

        static GameObject BuildPrefab(ItemSpec spec, Material mat)
        {
            var root = new GameObject("Attach_" + spec.id);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Attach_" + spec.id + "_Body";
            Object.DestroyImmediate(cube.GetComponent<Collider>());   // visual only: Stage 1 items never carry colliders
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = spec.size;
            cube.transform.localPosition = new Vector3(0f, 0f, spec.size.z * 0.5f);   // mount plane at the pivot, item extends along +Z (outward); size = (width, height, depth outward)
            cube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            string path = $"{ArtFolder}/Attach_{spec.id}.prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved;
        }

        static T LoadOrCreate<T>(string path, System.Func<T> create) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var made = create();
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
