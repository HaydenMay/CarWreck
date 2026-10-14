using System.Collections.Generic;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Creates / refreshes the vehicle definition assets. All vehicle-specific knowledge lives here as data: component
    /// classification, mass and the few explicit overrides. Everything geometric that can be measured is measured
    /// (by <see cref="VehicleRig.Measure"/>), including the hatchback profile's cabin line.
    /// </summary>
    public static class VehicleDefinitionSetup
    {
        public const string Folder = DamageLabAssets.SettingsFolder + "/Vehicles";
        public const string MusclePath = Folder + "/MuscleCar.asset";
        public const string HatchPath = Folder + "/CompactHatchback.asset";
        public const string HatchProfilePath = Folder + "/CompactHatchbackDamageProfile.asset";
        const string HatchModel = "Assets/Vehicles/CompactHatchback/Models/CompactHatchback.fbx";

        [MenuItem("Damage Lab/Create or Refresh Vehicle Definitions", priority = 2)]
        public static void BuildMenu() => Build();

        public static VehicleDefinition[] Build()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder(DamageLabAssets.SettingsFolder, "Vehicles");
            var muscleProfile = AssetDatabase.LoadAssetAtPath<VehicleDamageProfile>(DamageLabAssets.ProfilePath) ?? DamageLabAssets.DamageProfile(false);

            // ---------------------------------------------------------------- muscle car: the regression baseline.
            // Explicit values reproduce the phase 1-4 setup exactly (0.7 m nose/tail, 0.45 m sides, 0.7 m impact height).
            var muscle = LoadOrCreate(MusclePath);
            muscle.displayName = "Muscle Car";
            muscle.model = AssetDatabase.LoadAssetAtPath<GameObject>(DamageLabAssets.ModelPath);
            muscle.mass = 1400f;
            muscle.profile = muscleProfile;
            muscle.components = new List<VehiclePartRule>(VehiclePartRule.Defaults());
            muscle.unknownComponentCrumple = 1f;
            muscle.frontSectionDepth = 0.7f;
            muscle.rearSectionDepth = 0.7f;
            muscle.sideSectionDepth = 0.45f;
            muscle.impactHeight = 0.7f;
            muscle.maxImpactRecords = 24;
            EditorUtility.SetDirty(muscle);

            // ---------------------------------------------------------------- compact hatchback: configuration only.
            var hatch = LoadOrCreate(HatchPath);
            hatch.displayName = "Compact Hatchback";
            hatch.model = AssetDatabase.LoadAssetAtPath<GameObject>(HatchModel);
            hatch.mass = 950f;
            hatch.components = HatchbackComponents();
            hatch.unknownComponentCrumple = 1f;
            hatch.frontSectionDepth = hatch.rearSectionDepth = hatch.sideSectionDepth = 0f;   // automatic
            hatch.impactHeight = 0f;                                                          // automatic
            hatch.maxImpactRecords = 24;
            // Same damage tuning as the muscle car (no weakening); only the cabin line comes from the hatchback's geometry.
            var profile = AssetDatabase.LoadAssetAtPath<VehicleDamageProfile>(HatchProfilePath);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VehicleDamageProfile>(); AssetDatabase.CreateAsset(profile, HatchProfilePath); }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(muscleProfile), profile);
            profile.name = "CompactHatchbackDamageProfile";
            if (hatch.model != null)
            {
                var inst = Object.Instantiate(hatch.model);
                try
                {
                    var g = VehicleRig.Measure(hatch, inst);
                    profile.roofLineStart = Mathf.Round(g.Beltline * 100f) / 100f;          // cabin protection starts at the beltline
                    profile.roofLineEnd = Mathf.Round((g.cabin.max.y - 0.07f) * 100f) / 100f; // as the muscle car: 7 cm below the roof
                }
                finally { Object.DestroyImmediate(inst); }
            }
            EditorUtility.SetDirty(profile);
            hatch.profile = profile;
            EditorUtility.SetDirty(hatch);
            AssetDatabase.SaveAssets();
            Debug.Log($"[DamageLab] Vehicle definitions: {muscle.displayName}, {hatch.displayName} (hatchback cabin line {profile.roofLineStart:0.00}-{profile.roofLineEnd:0.00} m)");
            return new[] { muscle, hatch };
        }

        static VehicleDefinition LoadOrCreate(string path)
        {
            var d = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (d == null) { d = ScriptableObject.CreateInstance<VehicleDefinition>(); AssetDatabase.CreateAsset(d, path); }
            return d;
        }

        /// <summary>
        /// Every hatchback component, classified with the same resistance as its muscle-car counterpart: body and
        /// painted panels 1.0 (the muscle car's lower front, spoiler and scoop are 1.0), bumpers 0.25, cabin 0.85, grille-type
        /// fascia 0.55, lamps and housings 0.45, exhaust 0.4 (side pipes), trim 1.0 (muscle "Trim" and "Mirrors" are 1.0).
        /// </summary>
        static List<VehiclePartRule> HatchbackComponents()
        {
            var l = new List<VehiclePartRule>();
            void R(string name, VehicleComponentRole role, float crumple, PartMode mode = PartMode.Deform) =>
                l.Add(new VehiclePartRule(name, mode, crumple, false, 0.75f, role));
            R("Wheel", VehicleComponentRole.Wheel, 0f, PartMode.Rigid);
            R("Body", VehicleComponentRole.Body, 1f);
            R("Bumper", VehicleComponentRole.Bumper, 0.25f);
            R("Cabin", VehicleComponentRole.Cabin, 0.85f);
            R("Underbody", VehicleComponentRole.Underbody, 1f);
            foreach (var n in new[] { "FrontValance", "RearValance", "Sill", "Spoiler" }) R(n, VehicleComponentRole.Panel, 1f);
            foreach (var n in new[] { "Grille", "GrilleSlat", "GrilleSurround", "LowerIntake", "LowerSlat", "FrontSplitter" }) R(n, VehicleComponentRole.Fascia, 0.55f);
            foreach (var n in new[] { "Headlight", "HeadlightHousing", "FogLight", "FogHousing", "Taillight", "TaillightHousing", "ReverseLight", "RearIndicator", "FrontIndicator", "SideIndicator" })
                R(n, VehicleComponentRole.Light, 0.45f);
            R("Exhaust", VehicleComponentRole.Trim, 0.4f);
            foreach (var n in new[] { "SpoilerMount", "LicenseRecess", "BlankPlate", "HatchLock", "HatchSeam", "DoorSeam", "DoorHandle", "SideMoulding", "Mirror", "MirrorGlass", "MirrorStem", "FrontWiper", "RearWiper", "RoofGutter" })
                R(n, VehicleComponentRole.Trim, 1f);
            return l;
        }
    }
}
