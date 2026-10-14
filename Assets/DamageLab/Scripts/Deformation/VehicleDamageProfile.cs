using UnityEngine;

namespace DamageLab.Deformation
{
    /// <summary>
    /// Damage region definitions for one vehicle: one <see cref="DamageRegion"/> per direction plus
    /// a few vehicle-wide structural values. Lives as an asset so it can be tuned without code changes.
    /// </summary>
    [CreateAssetMenu(menuName = "Damage Lab/Vehicle Damage Profile", fileName = "VehicleDamageProfile")]
    public class VehicleDamageProfile : ScriptableObject
    {
        public DamageRegion front = new DamageRegion();
        public DamageRegion rear = new DamageRegion();
        public DamageRegion left = new DamageRegion();
        public DamageRegion right = new DamageRegion();

        [Header("Vehicle structure")]
        [Tooltip("Height (m) where the roof sag starts to apply.")]
        public float roofLineStart = 1.05f;
        [Tooltip("Height (m) where the roof sag is at full strength.")]
        public float roofLineEnd = 1.40f;

        [Header("Click-to-damage")]
        public ImpactSettings impact = new ImpactSettings();

        public DamageRegion Get(DamageDirection d)
        {
            switch (d)
            {
                case DamageDirection.Front: return front;
                case DamageDirection.Rear: return rear;
                case DamageDirection.Left: return left;
                default: return right;
            }
        }

        /// <summary>Defaults tuned for the stylized muscle car (≈4.7 m long, 2 m wide).</summary>
        public void ResetToDefaults()
        {
            front = Longitudinal(DamageDirection.Front, 0.85f, new Vector2(-0.18f, 0.55f), 0.32f, 11);
            rear = Longitudinal(DamageDirection.Rear, 0.75f, new Vector2(0.2f, 0.6f), 0.30f, 23);
            left = Lateral(DamageDirection.Left, new Vector2(-0.05f, 0.62f), 37);
            right = Lateral(DamageDirection.Right, new Vector2(0.15f, 0.6f), 53);
            roofLineStart = 1.05f;
            roofLineEnd = 1.40f;
            impact = new ImpactSettings();
        }

        // Front / rear: a wide barrier-like hit that shortens the car and folds hood / trunk / fenders.
        static DamageRegion Longitudinal(DamageDirection d, float depth, Vector2 center, float wavelength, int seed) => new DamageRegion
        {
            direction = d, maxCrushDepth = depth, damageExponent = 1.1f,
            zoneLengthScale = 1.7f, zoneLengthMin = 0.15f,
            impactCenter = center,
            radiusAtLowDamage = new Vector2(0.45f, 0.35f), radiusAtFullDamage = new Vector2(0.85f, 0.7f),
            upperRadiusScale = 1.2f, baseCoverage = 0.22f, profileSharpness = 1.0f, verticalShape = 1.4f, profileIrregularity = 0.3f,
            foldWavelength = wavelength, foldStrength = 0.9f, elasticStrain = 0.08f, foldVariation = 0.6f, outwardBuckle = 0.3f,
            creaseStrength = 0.16f, wrinkleStrength = 0.04f, creaseFrequency = 2.6f, drawIn = 0.12f,
            roofSag = 0.06f, seed = seed,
        };

        // Left / right: a narrower intrusion centred on the doors with a crisp horizontal crease.
        static DamageRegion Lateral(DamageDirection d, Vector2 center, int seed) => new DamageRegion
        {
            direction = d, maxCrushDepth = 0.55f, damageExponent = 0.95f,
            zoneLengthScale = 1.7f, zoneLengthMin = 0.12f,
            impactCenter = center,
            radiusAtLowDamage = new Vector2(0.55f, 0.3f), radiusAtFullDamage = new Vector2(1.25f, 0.5f),
            upperRadiusScale = 0.8f, baseCoverage = 0.06f, profileSharpness = 1.6f, verticalShape = 1.15f, profileIrregularity = 0.25f,
            foldWavelength = 0.24f, foldStrength = 0.8f, elasticStrain = 0.1f, foldVariation = 0.6f, outwardBuckle = 0.2f,
            creaseStrength = 0.24f, wrinkleStrength = 0.05f, creaseFrequency = 2.6f, drawIn = 0.15f,
            roofSag = 0.05f, seed = seed,
        };

        void Reset() => ResetToDefaults();
    }
}
