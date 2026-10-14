using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Turns a vehicle manifest (vehicle.json, written by a vehicle generator next to its FBX) into a destructible
    /// vehicle: model import settings, materials (two-colour paint with a stripe mask, URP/Lit for the rest), the
    /// VehicleDefinition with its component rules and a damage profile whose cabin line is measured from the model.
    /// No vehicle-specific code. Format and rules: Docs/VehicleProduction/vehicle-asset-spec.md.
    /// Bridge: "vehicle-import &lt;manifest&gt;" (writes assets) or "vehicle-import &lt;manifest&gt; dry" (checks only).
    /// </summary>
    public static class VehicleManifestImporter
    {
        public const string Format = "carwreck.vehicle.v1";
        const string PaintShader = "CarWreck/CompactHatchback/Two Color Paint";   // the shared two-colour paint (UV1 stripe mask)

        [Serializable] public class Component { public string pattern, match = "Token", role, mode = "Deform"; public float crumple = 1f; }
        [Serializable] public class CabinLine { public float start, end; }
        [Serializable] public class Shell { public float frontDepth, rearDepth, sideDepth, impactHeight; }
        [Serializable] public class Paint { public string material, stripeMask; }
        [Serializable]
        public class Manifest
        {
            public string format, id, displayName, model, palette;
            public int revision;
            public float massKg;
            public float unknownComponentCrumple = 1f;
            public int maxImpactRecords = 24;
            public CabinLine cabinLine = new CabinLine();
            public Shell shell = new Shell();
            public Paint paint = new Paint();
            public Component[] components = new Component[0];
        }
        [Serializable] class PaletteEntry { public string name; public float[] color_linear, primary_srgb, secondary_srgb, emission_linear; public float metallic, smoothness, emission_strength; }
        [Serializable] class Palette { public PaletteEntry[] materials; }

        public static Manifest Load(string path) => JsonUtility.FromJson<Manifest>(File.ReadAllText(path));

        /// <summary>Build the definition (and, unless <paramref name="dryRun"/>, the import settings, materials and assets).
        /// Returns a report; <paramref name="definition"/> is the in-memory or saved definition.</summary>
        public static string Import(string manifestPath, bool dryRun, out VehicleDefinition definition, out List<string> errors)
        {
            var sb = new StringBuilder($"[DamageLab] Vehicle manifest {manifestPath}{(dryRun ? " (dry run: nothing written)" : "")}\n");
            errors = new List<string>();
            definition = null;
            var m = Load(manifestPath);
            if (m.format != Format) errors.Add($"format is '{m.format}', expected '{Format}'");
            if (string.IsNullOrEmpty(m.id) || !m.id.Contains(".")) errors.Add("id must be a stable dotted ID such as 'carwreck.compact_hatchback'");
            if (string.IsNullOrEmpty(m.displayName)) errors.Add("displayName is missing");
            if (m.massKg < 300f || m.massKg > 6000f) errors.Add($"massKg {m.massKg} is outside 300-6000 kg");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m.model ?? "");
            if (prefab == null) errors.Add($"model '{m.model}' not found (path relative to the project, e.g. Assets/Vehicles/<Name>/Models/<Name>.fbx)");
            var rules = new List<VehiclePartRule>();
            foreach (var c in m.components ?? new Component[0])
            {
                if (!Enum.TryParse(c.role, out VehicleComponentRole role)) { errors.Add($"component '{c.pattern}': unknown role '{c.role}'"); continue; }
                if (!Enum.TryParse(c.mode, out PartMode mode)) { errors.Add($"component '{c.pattern}': unknown mode '{c.mode}'"); continue; }
                if (!Enum.TryParse(c.match, out NameMatch match)) { errors.Add($"component '{c.pattern}': unknown match '{c.match}'"); continue; }
                if (match == NameMatch.Contains) errors.Add($"component '{c.pattern}': 'Contains' matching is legacy-only; use Token or Exact");
                rules.Add(new VehiclePartRule(c.pattern, mode, c.crumple, false, 0.75f, role, match));
            }
            if (!rules.Any(r => r.role == VehicleComponentRole.Body)) errors.Add("no component has role Body");
            if (!rules.Any(r => r.role == VehicleComponentRole.Wheel)) errors.Add("no component has role Wheel");
            if (errors.Count > 0) { sb.AppendLine("  ERRORS:\n    - " + string.Join("\n    - ", errors)); return sb.ToString(); }

            string folder = Path.GetDirectoryName(m.model).Replace('\\', '/');
            string vehicleFolder = folder.EndsWith("/Models") ? folder.Substring(0, folder.Length - 7) : folder;
            string assetName = new string(m.displayName.Where(char.IsLetterOrDigit).ToArray());
            if (!dryRun) ApplyImportSettings(m, vehicleFolder, sb);

            // Definition (in memory first).
            var def = ScriptableObject.CreateInstance<VehicleDefinition>();
            def.name = assetName;
            def.displayName = m.displayName;
            def.model = prefab;
            def.mass = m.massKg;
            def.components = rules;
            def.unknownComponentCrumple = m.unknownComponentCrumple;
            def.frontSectionDepth = m.shell.frontDepth; def.rearSectionDepth = m.shell.rearDepth; def.sideSectionDepth = m.shell.sideDepth;
            def.impactHeight = m.shell.impactHeight;
            def.maxImpactRecords = Mathf.Max(1, m.maxImpactRecords);
            // Damage profile: the shared tuning (a copy of the baseline muscle profile, never weakened), cabin line from the model.
            var baseline = AssetDatabase.LoadAssetAtPath<VehicleDamageProfile>(DamageLabAssets.ProfilePath);
            var profile = ScriptableObject.CreateInstance<VehicleDamageProfile>();
            if (baseline != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(baseline), profile);
            profile.name = assetName + "DamageProfile";
            def.profile = profile;

            // Measure and classify against the actual imported model.
            var warnings = new List<string>();
            var inst = UnityEngine.Object.Instantiate(prefab);
            VehicleGeometry g;
            int unclassified = 0, ambiguous = 0, parts = 0, deformVerts = 0;
            try
            {
                g = VehicleRig.Measure(def, inst);
                foreach (var f in inst.GetComponentsInChildren<MeshFilter>(true))
                {
                    var rule = VehiclePartRule.Find(rules, f.name, out int n);
                    if (rule != null && rule.mode == PartMode.Ignore) continue;
                    parts++;
                    if (rule == null) { unclassified++; warnings.Add($"unclassified component '{f.name}'"); }
                    else if (n > 1) { ambiguous++; warnings.Add($"ambiguous component '{f.name}' ({n} rules)"); }
                    if (rule == null || rule.mode == PartMode.Deform) deformVerts += f.sharedMesh != null ? f.sharedMesh.vertexCount : 0;
                    if (f.sharedMesh != null && !f.sharedMesh.isReadable) warnings.Add($"mesh '{f.name}' is not readable");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(inst); }
            profile.roofLineStart = m.cabinLine.start > 0f ? m.cabinLine.start : Mathf.Round(g.Beltline * 100f) / 100f;
            profile.roofLineEnd = m.cabinLine.end > 0f ? m.cabinLine.end : Mathf.Round((g.cabin.max.y - 0.07f) * 100f) / 100f;
            var inst2 = UnityEngine.Object.Instantiate(prefab);
            try { VehicleRig.Measure(def, inst2, warnings); } finally { UnityEngine.Object.DestroyImmediate(inst2); }
            sb.AppendLine($"  {m.displayName} ({m.id} r{m.revision}): {parts} components, {deformVerts:N0} deformable vertices, {rules.Count} rules, mass {m.massKg:0} kg");
            sb.AppendLine($"  measured body {g.body.size.ToString("F3")}, beltline {g.Beltline:0.00} m, cabin top {g.cabin.max.y:0.00} m -> cabin line {profile.roofLineStart:0.00}-{profile.roofLineEnd:0.00} m; " +
                          $"shell {def.FrontDepth(g.body):0.00}/{def.RearDepth(g.body):0.00}/{def.SideDepth(g.body):0.00} m, impact height {VehicleRig.ImpactHeight(def, g):0.00} m");
            sb.AppendLine($"  classification: {unclassified} unclassified, {ambiguous} ambiguous; warnings {warnings.Count}{(warnings.Count > 0 ? "\n    - " + string.Join("\n    - ", warnings.Take(20)) : "")}");
            if (unclassified > 0) errors.Add($"{unclassified} unclassified components (mandatory: every deformable mesh matches a rule)");
            if (ambiguous > 0) errors.Add($"{ambiguous} ambiguous components");

            if (!dryRun && errors.Count == 0)
            {
                string defPath = VehicleDefinitionSetup.Folder + "/" + assetName + ".asset";
                string profilePath = VehicleDefinitionSetup.Folder + "/" + assetName + "DamageProfile.asset";
                var existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
                var existingProfile = AssetDatabase.LoadAssetAtPath<VehicleDamageProfile>(profilePath);
                if (existingProfile == null) AssetDatabase.CreateAsset(profile, profilePath);
                else { EditorUtility.CopySerialized(profile, existingProfile); profile = existingProfile; }
                def.profile = profile;
                if (existing == null) AssetDatabase.CreateAsset(def, defPath);
                else { EditorUtility.CopySerialized(def, existing); def = existing; def.profile = profile; }
                EditorUtility.SetDirty(def); EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                sb.AppendLine($"  wrote {defPath} and {profilePath}; rebuild the Collision Lab scene (collision-build) to add it to the vehicle selector");
            }
            definition = def;
            sb.AppendLine(errors.Count == 0 ? "  RESULT: PASS (configuration)" : "  RESULT: FAIL\n    - " + string.Join("\n    - ", errors));
            return sb.ToString();
        }

        /// <summary>The import settings both production vehicles use (readable, uncompressed, file order kept, imported
        /// normals, metre scale, axis conversion baked) and URP materials from the generator's palette.</summary>
        static void ApplyImportSettings(Manifest m, string vehicleFolder, StringBuilder sb)
        {
            var materials = new Dictionary<string, Material>();
            if (!string.IsNullOrEmpty(m.palette) && File.Exists(m.palette))
            {
                var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(m.palette));
                Texture2D mask = null;
                if (!string.IsNullOrEmpty(m.paint.stripeMask))
                {
                    var ti = AssetImporter.GetAtPath(m.paint.stripeMask) as TextureImporter;
                    if (ti != null)
                    {
                        ti.sRGBTexture = false; ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp;
                        ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
                    }
                    mask = AssetDatabase.LoadAssetAtPath<Texture2D>(m.paint.stripeMask);
                }
                string matFolder = vehicleFolder + "/Materials";
                if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(vehicleFolder, "Materials");
                foreach (var e in palette.materials)
                {
                    string path = matFolder + "/" + e.name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)   // existing materials keep their (possibly user-edited) values
                    {
                        bool paint = e.name == m.paint.material;
                        mat = new Material(Shader.Find(paint ? PaintShader : "Universal Render Pipeline/Lit")) { name = e.name };
                        if (paint)
                        {
                            mat.SetColor("_PrimaryColor", new Color(e.primary_srgb[0], e.primary_srgb[1], e.primary_srgb[2]).linear);
                            mat.SetColor("_StripeColor", new Color(e.secondary_srgb[0], e.secondary_srgb[1], e.secondary_srgb[2]).linear);
                            mat.SetTexture("_StripeMask", mask);
                        }
                        else mat.SetColor("_BaseColor", new Color(e.color_linear[0], e.color_linear[1], e.color_linear[2], 1));
                        mat.SetFloat("_Metallic", e.metallic);
                        mat.SetFloat("_Smoothness", e.smoothness);
                        if (e.emission_strength > 0)
                        {
                            mat.SetColor("_EmissionColor", new Color(e.emission_linear[0], e.emission_linear[1], e.emission_linear[2]) * e.emission_strength);
                            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                            mat.EnableKeyword("_EMISSION");
                        }
                        AssetDatabase.CreateAsset(mat, path);
                    }
                    materials[e.name] = mat;
                }
                sb.AppendLine($"  materials: {materials.Count} ({m.paint.material} uses the two-colour paint shader)");
            }
            var importer = (ModelImporter)AssetImporter.GetAtPath(m.model);
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false;
            importer.optimizeMeshPolygons = false;
            importer.weldVertices = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.CalculateMikk;
            importer.importCameras = false; importer.importLights = false; importer.importAnimation = false;
            importer.globalScale = 1f; importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
            sb.AppendLine("  model import settings applied (readable, uncompressed, vertex order kept, imported normals, metre scale, axis conversion baked)");
        }

        /// <summary>Dry-run the reference manifest and compare it with the hand-written hatchback configuration.</summary>
        public static string CheckAgainstExisting(string manifestPath, string existingDefinitionPath)
        {
            var report = Import(manifestPath, true, out var def, out var errors);
            var existing = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(existingDefinitionPath);
            if (def == null || existing == null) return report + "  comparison skipped (missing definition)\n";
            var a = def.components; var b = existing.components;
            int same = 0;
            for (int i = 0; i < Mathf.Min(a.Count, b.Count); i++)
                if (a[i].nameContains == b[i].nameContains && a[i].role == b[i].role && a[i].mode == b[i].mode && a[i].match == b[i].match && Mathf.Approximately(a[i].crumple, b[i].crumple)) same++;
            bool rulesSame = a.Count == b.Count && same == a.Count;
            bool rest = Mathf.Approximately(def.mass, existing.mass) && Mathf.Approximately(def.profile.roofLineStart, existing.profile.roofLineStart)
                        && Mathf.Approximately(def.profile.roofLineEnd, existing.profile.roofLineEnd) && def.model == existing.model;
            return report + $"  manifest vs {existingDefinitionPath}: rules identical {rulesSame} ({same}/{b.Count}), mass/model/cabin line identical {rest} {(rulesSame && rest && errors.Count == 0 ? "PASS" : "FAIL")}\n";
        }
    }
}
