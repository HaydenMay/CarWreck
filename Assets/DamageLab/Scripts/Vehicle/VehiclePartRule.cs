using System;
using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.Vehicle
{
    public enum PartMode
    {
        Deform,  // mesh vertices are deformed
        Rigid,   // transform follows the crush (wheels)
        Ignore,
    }

    /// <summary>What a vehicle component is. Roles drive the generic vehicle setup: Body and Bumper parts define the
    /// outer impact faces and the physics shell, Cabin parts the cabin box, Wheel parts the wheel reference points.</summary>
    public enum VehicleComponentRole
    {
        Unspecified,
        Body,       // main body shell (hood, doors, fenders, hatch...)
        Bumper,     // bumpers: part of the outer impact faces
        Cabin,      // greenhouse: roof, pillars, glass
        Wheel,      // wheel assemblies (rigid)
        Panel,      // other painted sheet-metal panels (valances, sills, spoilers)
        Fascia,     // grilles, intakes, splitters
        Light,      // lamps and their housings
        Trim,       // mouldings, handles, mirrors, wipers, plates, exhausts
        Underbody,  // floor pan / underside
        Ignore,
    }

    public enum NameMatch
    {
        Token,     // name == pattern, or name starts with pattern followed by a non-letter ("Wheel" matches "Wheel_FL", "Body.001"; "Body" does NOT match "Underbody")
        Exact,
        Contains,  // legacy substring matching
    }

    [Serializable]
    public class VehiclePartRule
    {
        [Tooltip("Pattern matched against the component's object name (case-insensitive).")]
        public string nameContains = "";
        public NameMatch match = NameMatch.Token;
        public VehicleComponentRole role = VehicleComponentRole.Unspecified;
        public PartMode mode = PartMode.Deform;
        [Tooltip("Scales folds and creases. Sheet metal = 1, stiff chrome bumpers bend more than they wrinkle.")]
        [Range(0f, 2f)] public float crumple = 1f;
        [Tooltip("Rigid parts: how much of the surrounding crush they follow.")]
        [Range(0f, 1f)] public float rigidFollow = 0.75f;
        [Tooltip("Whether this part defines the outer faces the impacts are measured from (Body and Bumper roles always do).")]
        public bool definesImpactFaces = false;
        [Tooltip("Thin tubes (coil springs, rods, pipes): each mesh-detected cross-section ring moves as one unit, so the tube bends and crushes along its length without its round section turning inside out. Off by default.")]
        public bool coherentTubeSections = false;

        public bool DefinesImpactFaces => definesImpactFaces || role == VehicleComponentRole.Body || role == VehicleComponentRole.Bumper;

        public VehiclePartRule() { }

        public VehiclePartRule(string name, PartMode mode, float crumple = 1f, bool faces = false, float follow = 0.75f,
                               VehicleComponentRole role = VehicleComponentRole.Unspecified, NameMatch match = NameMatch.Token)
        {
            nameContains = name;
            this.mode = mode;
            this.crumple = crumple;
            definesImpactFaces = faces;
            rigidFollow = follow;
            this.role = role;
            this.match = match;
        }

        public bool Matches(string objectName)
        {
            if (string.IsNullOrEmpty(nameContains) || string.IsNullOrEmpty(objectName)) return false;
            switch (match)
            {
                case NameMatch.Exact: return string.Equals(objectName, nameContains, StringComparison.OrdinalIgnoreCase);
                case NameMatch.Contains: return objectName.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0;
                default:
                    if (!objectName.StartsWith(nameContains, StringComparison.OrdinalIgnoreCase)) return false;
                    return objectName.Length == nameContains.Length || !char.IsLetter(objectName[nameContains.Length]);
            }
        }

        /// <summary>First matching rule (null if none) and how many rules matched in total (more than one = ambiguous).</summary>
        public static VehiclePartRule Find(IList<VehiclePartRule> rules, string objectName, out int matchCount)
        {
            VehiclePartRule first = null;
            matchCount = 0;
            if (rules == null) return null;
            foreach (var r in rules)
            {
                if (r == null || !r.Matches(objectName)) continue;
                matchCount++;
                if (first == null) first = r;
            }
            return first;
        }

        /// <summary>The muscle car's classification (also the fallback for models without a vehicle definition).</summary>
        public static VehiclePartRule[] Defaults() => new[]
        {
            new VehiclePartRule("Wheel", PartMode.Rigid, 0f, false, 0.75f, VehicleComponentRole.Wheel),
            new VehiclePartRule("Body", PartMode.Deform, 1f, true, role: VehicleComponentRole.Body),
            new VehiclePartRule("Bumper", PartMode.Deform, 0.25f, true, role: VehicleComponentRole.Bumper),
            new VehiclePartRule("Cabin", PartMode.Deform, 0.85f, role: VehicleComponentRole.Cabin),
            new VehiclePartRule("Grille", PartMode.Deform, 0.55f, role: VehicleComponentRole.Fascia),
            new VehiclePartRule("Headlights", PartMode.Deform, 0.45f, role: VehicleComponentRole.Light),
            new VehiclePartRule("Taillights", PartMode.Deform, 0.45f, role: VehicleComponentRole.Light),
            new VehiclePartRule("SidePipes", PartMode.Deform, 0.4f, role: VehicleComponentRole.Trim),
            // Previously unclassified muscle-car parts: same behaviour as before (deform, crumple 1), now explicit.
            new VehiclePartRule("HoodScoop", PartMode.Deform, 1f, role: VehicleComponentRole.Panel),
            new VehiclePartRule("Spoiler", PartMode.Deform, 1f, role: VehicleComponentRole.Panel),
            new VehiclePartRule("Mirrors", PartMode.Deform, 1f, role: VehicleComponentRole.Trim),
            new VehiclePartRule("Trim", PartMode.Deform, 1f, role: VehicleComponentRole.Trim),
            new VehiclePartRule("Wireframe", PartMode.Ignore, role: VehicleComponentRole.Ignore, match: NameMatch.Contains),
        };
    }
}
