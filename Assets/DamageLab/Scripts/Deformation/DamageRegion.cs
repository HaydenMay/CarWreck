using System;
using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Tunable description of one impact direction: where the impactor hits, how deep it can crush,
    /// and how the metal buckles. All distances are in vehicle space (metres, x = right, y = up, z = forward).
    /// </summary>
    [Serializable]
    public class DamageRegion
    {
        public DamageDirection direction;

        [Header("Crush depth")]
        [Tooltip("How far (m) the most-impacted point of the face is pushed in at 100% damage.")]
        public float maxCrushDepth = 0.8f;
        [Tooltip("Response exponent applied to the 0..1 damage amount. >1 keeps low damage subtle.")]
        public float damageExponent = 1.1f;
        [Tooltip("Crumple zone length = depth * scale + min. Keep scale >= 1.5 so the crush mapping stays monotonic (no self-intersection along the impact axis).")]
        public float zoneLengthScale = 1.7f;
        public float zoneLengthMin = 0.15f;

        [Header("Impact footprint")]
        [Tooltip("Impact centre on the face. x = lateral position (front/rear) or longitudinal position (left/right); y = height.")]
        public Vector2 impactCenter;
        [Tooltip("Footprint radii (along face, vertical) at low damage.")]
        public Vector2 radiusAtLowDamage = new Vector2(0.6f, 0.35f);
        [Tooltip("Footprint radii at 100% damage. The dent spreads as damage grows.")]
        public Vector2 radiusAtFullDamage = new Vector2(1.2f, 0.6f);
        [Tooltip("Vertical radius multiplier above the impact centre. < 1 protects the roofline.")]
        public float upperRadiusScale = 1f;
        [Range(0f, 1f)]
        [Tooltip("Fraction of the crush applied across the whole face (barrier-like), on top of the focused dent.")]
        public float baseCoverage = 0.2f;
        [Tooltip("Super-gaussian exponent of the dent footprint along the face. Higher = flatter dent floor with sharper edges.")]
        public float profileSharpness = 1.5f;
        [Tooltip("Vertical shape exponent. 2 = rounded; ~1 = pointed, which folds the panel into a crisp horizontal crease at the impact height.")]
        public float verticalShape = 2f;
        [Range(0f, 0.6f)]
        [Tooltip("Low-frequency deterministic noise that makes the dent outline irregular.")]
        public float profileIrregularity = 0.25f;

        [Header("Crumpling")]
        [Tooltip("Wavelength (m) of the accordion folds that form where material is compressed.")]
        public float foldWavelength = 0.2f;
        [Tooltip("Fold amplitude multiplier. 1 = amplitude derived from conserving panel length.")]
        public float foldStrength = 1f;
        [Tooltip("Compression the metal absorbs before it starts to buckle (keeps distant panels smooth).")]
        public float elasticStrain = 0.06f;
        [Range(0f, 1f)]
        [Tooltip("How much fold height varies along each ridge (0 = perfectly uniform ridges).")]
        public float foldVariation = 0.6f;
        [Tooltip("Bias that makes buckled panels bulge outward (hood tenting, fender splay).")]
        public float outwardBuckle = 0.35f;
        [Tooltip("Sharp creases along the edge of the dent (where the panel bends most).")]
        public float creaseStrength = 0.12f;
        [Tooltip("Smaller wrinkles across the dent floor.")]
        public float wrinkleStrength = 0.05f;
        [Tooltip("Spatial frequency of crease / wrinkle noise (1/m).")]
        public float creaseFrequency = 4f;
        [Tooltip("How strongly the surrounding panel is pulled toward the impact centre.")]
        public float drawIn = 0.12f;

        [Header("Structure")]
        [Tooltip("How much the roof drops near a heavy impact (fraction of crush depth).")]
        public float roofSag = 0.05f;
        [Tooltip("Seed for this region's deterministic noise.")]
        public int seed = 1;
    }
}
