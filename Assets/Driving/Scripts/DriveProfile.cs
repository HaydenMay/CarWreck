using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Per-vehicle driving configuration for the shared <see cref="ArcadeCarController"/> (one controller, different numbers).
    /// Speeds are m/s (x 3.6 = km/h), accelerations m/s². These are playtest starting points, not final balance.
    /// </summary>
    [CreateAssetMenu(menuName = "CarWreck/Drive Profile", fileName = "DriveProfile")]
    public class DriveProfile : ScriptableObject
    {
        public string displayName = "Vehicle";
        [TextArea(2, 4)] public string description = "";

        [Header("Damage")]
        [Tooltip("How much of a vehicle-vs-vehicle collision's energy this vehicle absorbs, relative to the other car (see CollisionDamageReceiver.softness). 1 = baseline; below 1 = sturdier, above 1 = softer.")]
        public float damageSoftness = 1f;

        [Header("Forward")]
        public float acceleration = 13f;
        public float maxSpeed = 30f;
        [Header("Reverse")]
        public float reverseAcceleration = 11f;
        public float reverseMaxSpeed = 13f;
        [Header("Braking")]
        public float brakeDeceleration = 26f;
        public float coastDeceleration = 1.2f;
        [Tooltip("Extra push that cancels ground friction, so Max Speed / Reverse Max Speed are (nearly) the real top speeds.")]
        public float driveResistance = 3f;
        [Header("Steering")]
        public float steerRateLow = 100f;
        public float steerRateHigh = 34f;
        public float steerFullAuthoritySpeed = 3f;
        public float steerResponse = 28f;
        public float steerRise = 5f;
        public float steerReturn = 9f;
        [Header("Grip")]
        public float grip = 9f;
        public float maxGripAcceleration = 26f;
        [Range(0.2f, 1f)] public float brakingGripScale = 0.55f;
        [Header("Stability")]
        public float downforce = 0.22f;
        public float levelingStrength = 10f;
        public float levelingDamping = 3f;
    }
}
