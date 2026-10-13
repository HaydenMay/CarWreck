using System;
using UnityEngine;

namespace DamageLab.Vehicle
{
    public enum PartMode
    {
        Deform,  // mesh vertices are deformed
        Rigid,   // transform follows the crush (wheels)
        Ignore,
    }

    /// <summary>Maps imported child objects (by name) to how they take damage.</summary>
    [Serializable]
    public class VehiclePartRule
    {
        [Tooltip("Case-insensitive substring of the child object's name.")]
        public string nameContains = "";
        public PartMode mode = PartMode.Deform;
        [Tooltip("Scales folds and creases. Sheet metal = 1, stiff chrome bumpers bend more than they wrinkle.")]
        [Range(0f, 2f)] public float crumple = 1f;
        [Tooltip("Rigid parts: how much of the surrounding crush they follow.")]
        [Range(0f, 1f)] public float rigidFollow = 0.75f;
        [Tooltip("Whether this part defines the outer faces the impacts are measured from.")]
        public bool definesImpactFaces = false;

        public VehiclePartRule() { }

        public VehiclePartRule(string name, PartMode mode, float crumple = 1f, bool faces = false, float follow = 0.75f)
        {
            nameContains = name;
            this.mode = mode;
            this.crumple = crumple;
            definesImpactFaces = faces;
            rigidFollow = follow;
        }

        public static VehiclePartRule[] Defaults() => new[]
        {
            new VehiclePartRule("Wheel", PartMode.Rigid, 0f, false, 0.75f),
            new VehiclePartRule("Body", PartMode.Deform, 1f, true),
            new VehiclePartRule("Bumper", PartMode.Deform, 0.25f, true),
            new VehiclePartRule("Cabin", PartMode.Deform, 0.85f),
            new VehiclePartRule("Grille", PartMode.Deform, 0.55f),
            new VehiclePartRule("Headlights", PartMode.Deform, 0.45f),
            new VehiclePartRule("Taillights", PartMode.Deform, 0.45f),
            new VehiclePartRule("SidePipes", PartMode.Deform, 0.4f),
            new VehiclePartRule("Wireframe", PartMode.Ignore),
        };
    }
}
