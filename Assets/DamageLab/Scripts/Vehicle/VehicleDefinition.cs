using System.Collections.Generic;
using DamageLab.Deformation;
using UnityEngine;

namespace DamageLab.Vehicle
{
    /// <summary>
    /// Everything CarWreck needs to make a vehicle destructible: model, mass, damage profile, explicit component
    /// classification and the physics-shell proportions. Geometric values left at 0 are computed from the model's
    /// actual geometry (see <c>VehicleGeometry</c>); nothing in the destruction code is specific to one vehicle.
    /// </summary>
    [CreateAssetMenu(menuName = "Damage Lab/Vehicle Definition", fileName = "VehicleDefinition")]
    public class VehicleDefinition : ScriptableObject
    {
        public string displayName = "Vehicle";
        [Tooltip("The imported model (FBX prefab). Must face +Z with metre units; meshes must be readable.")]
        public GameObject model;
        [Tooltip("Vehicle mass (kg) for free-physics collisions.")]
        public float mass = 1400f;
        [Tooltip("Directional crush regions, cabin line and localized-impact tuning for this vehicle.")]
        public VehicleDamageProfile profile;

        [Header("Components")]
        [Tooltip("Explicit classification of the model's components (first match wins). Every mesh should match exactly one rule.")]
        public List<VehiclePartRule> components = new List<VehiclePartRule>();
        [Tooltip("Crumple used for components no rule matches (they still deform with the body, and are reported as warnings).")]
        [Range(0f, 2f)] public float unknownComponentCrumple = 1f;

        [Header("Physics shell (0 = automatic from the body size)")]
        [Tooltip("Depth (m) of the adaptive nose sections. Auto = 14 % of the body length.")]
        public float frontSectionDepth;
        [Tooltip("Depth (m) of the adaptive tail sections. Auto = 14 % of the body length.")]
        public float rearSectionDepth;
        [Tooltip("Depth (m) of the adaptive side shell. Auto = 22 % of the body width.")]
        public float sideSectionDepth;
        [Tooltip("Cabin box size relative to the cabin mesh bounds (width, length).")]
        public Vector2 cabinBoxScale = new Vector2(0.92f, 0.85f);

        [Header("Collision Lab")]
        [Tooltip("Height (m above the car's base) of the test impactor's centre line. 0 = automatic (body centre).")]
        public float impactHeight;
        [Tooltip("Impact records kept individually; older ones merge into the accumulated damage.")]
        public int maxImpactRecords = 24;

        public const float FrontDepthFraction = 0.14f, RearDepthFraction = 0.14f, SideDepthFraction = 0.22f;

        public float FrontDepth(Bounds body) => frontSectionDepth > 0f ? frontSectionDepth : body.size.z * FrontDepthFraction;
        public float RearDepth(Bounds body) => rearSectionDepth > 0f ? rearSectionDepth : body.size.z * RearDepthFraction;
        public float SideDepth(Bounds body) => sideSectionDepth > 0f ? sideSectionDepth : body.size.x * SideDepthFraction;
    }
}
