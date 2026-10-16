using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Arcade driving for the destructible vehicle rigs: one Rigidbody, no wheel colliders. The body's box shell
    /// rests on the ground (low-friction material); this script supplies everything a tyre would:
    ///   * drive / brake / reverse force along the ground-projected heading (only while probes touch the ground),
    ///   * a target yaw rate (speed-sensitive) that torques the body towards the steering input,
    ///   * lateral grip: sideways velocity is bled off, capped, so hard corners and crashes slide a little,
    ///   * mild anti-roll and downforce so the car does not flip from ordinary driving (a big hit still can).
    /// Collisions are plain Unity physics, so the existing CollisionDamageReceiver sees them unchanged.
    /// All forces use ForceMode.Acceleration, so tuning is in m/s² and independent of the vehicle's mass.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ArcadeCarController : MonoBehaviour
    {
        [Header("Engine")]
        [Tooltip("Acceleration (m/s²) from standstill; falls off towards Max Speed.")]
        public float acceleration = 13f;
        [Tooltip("Top forward speed (m/s). 30 m/s = 108 km/h.")]
        public float maxSpeed = 30f;
        [Tooltip("Reverse acceleration (m/s²).")]
        public float reverseAcceleration = 11f;
        [Tooltip("Top reverse speed (m/s).")]
        public float reverseMaxSpeed = 13f;

        [Header("Braking")]
        [Tooltip("Deceleration (m/s²) while braking against the direction of travel.")]
        public float brakeDeceleration = 26f;
        [Tooltip("Deceleration (m/s²) with no input (engine drag / rolling resistance).")]
        public float coastDeceleration = 1.2f;
        [Tooltip("Extra push cancelling ground friction so the speed caps are (nearly) the real top speeds. Passive drag from the low-friction hull otherwise holds the car well below them.")]
        public float driveResistance = 3f;

        [Header("Steering")]
        [Tooltip("Turn rate (deg/s) at low speed with full steering.")]
        public float steerRateLow = 100f;
        [Tooltip("Turn rate (deg/s) at Max Speed with full steering.")]
        public float steerRateHigh = 34f;
        [Tooltip("Speed (m/s) below which steering fades out, so the car cannot spin on the spot.")]
        public float steerFullAuthoritySpeed = 3f;
        [Tooltip("How quickly the body follows the target turn rate (1/s). Needs to be high: ground friction resists yawing.")]
        public float steerResponse = 28f;
        [Tooltip("How quickly the steering input rises while a steering key is held (1/s).")]
        public float steerRise = 5f;
        [Tooltip("How quickly the steering input returns to centre when released (1/s).")]
        public float steerReturn = 9f;

        [Header("Grip")]
        [Tooltip("How strongly sideways velocity is removed (1/s). Higher = more planted.")]
        public float grip = 9f;
        [Tooltip("Largest sideways acceleration the tyres can supply (m/s²). Lower = slides sooner.")]
        public float maxGripAcceleration = 26f;
        [Tooltip("Grip multiplier while braking hard and steering (1 = unchanged, lower = rear slides out).")]
        [Range(0.2f, 1f)] public float brakingGripScale = 0.55f;

        [Header("Stability")]
        [Tooltip("Extra downward acceleration per m/s of speed.")]
        public float downforce = 0.22f;
        [Tooltip("Torque (rad/s²) that levels the body while the wheels are on the ground. 0 = off.")]
        public float levelingStrength = 10f;
        [Tooltip("Damping (1/s) of roll and pitch while on the ground.")]
        public float levelingDamping = 3f;

        [Header("Ground probes")]
        public LayerMask groundMask = ~0;
        [Tooltip("Distance below the probe origin that still counts as ground (m).")]
        public float probeLength = 0.6f;

        // Probe points in the car's local space (set by Configure; rays start inside the hull and point down).
        Vector3[] probes = { new Vector3(-0.7f, 0.35f, 1.3f), new Vector3(0.7f, 0.35f, 1.3f), new Vector3(-0.7f, 0.35f, -1.3f), new Vector3(0.7f, 0.35f, -1.3f) };
        Rigidbody rb;
        readonly RaycastHit[] hits = new RaycastHit[8];

        float steerState;
        bool grounded;
        float groundFactor;
        Vector3 groundNormal = Vector3.up;

        public float Throttle { get; set; }
        public float SteerInput { get; set; }

        /// <summary>Speed along the heading (m/s, negative = reversing).</summary>
        public float ForwardSpeed { get; private set; }
        public float Speed => rb != null ? rb.linearVelocity.magnitude : 0f;
        public bool IsGrounded => grounded;
        /// <summary>The car is on its side or roof.</summary>
        public bool IsFlipped => transform.up.y < 0.3f;
        public float Steering => steerState;

        void Awake() => rb = GetComponent<Rigidbody>();

        /// <summary>Copy a vehicle profile's handling values (the controller stays the same code for every vehicle).</summary>
        public void Apply(DriveProfile p)
        {
            if (p == null) return;
            acceleration = p.acceleration; maxSpeed = p.maxSpeed;
            reverseAcceleration = p.reverseAcceleration; reverseMaxSpeed = p.reverseMaxSpeed;
            brakeDeceleration = p.brakeDeceleration; coastDeceleration = p.coastDeceleration; driveResistance = p.driveResistance;
            steerRateLow = p.steerRateLow; steerRateHigh = p.steerRateHigh; steerFullAuthoritySpeed = p.steerFullAuthoritySpeed;
            steerResponse = p.steerResponse; steerRise = p.steerRise; steerReturn = p.steerReturn;
            grip = p.grip; maxGripAcceleration = p.maxGripAcceleration; brakingGripScale = p.brakingGripScale;
            downforce = p.downforce; levelingStrength = p.levelingStrength; levelingDamping = p.levelingDamping;
        }

        /// <summary>Place the four ground probes at the body corners (local space, <paramref name="bodyBottomY"/> = tyre contact height).</summary>
        public void Configure(Bounds body, float bodyBottomY)
        {
            float x = body.extents.x * 0.8f, z = body.extents.z * 0.75f, y = bodyBottomY + 0.3f;
            probes = new[]
            {
                new Vector3(body.center.x - x, y, body.center.z + z), new Vector3(body.center.x + x, y, body.center.z + z),
                new Vector3(body.center.x - x, y, body.center.z - z), new Vector3(body.center.x + x, y, body.center.z - z),
            };
            probeLength = 0.3f + 0.35f;
        }

        void FixedUpdate()
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            Tick(Time.fixedDeltaTime);
        }

        /// <summary>One driving step (called from FixedUpdate; the edit-mode validation steps it by hand).</summary>
        public void Tick(float dt)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();

            // Digital steering ramps in and out so a key tap is a gentle correction and a hold is full lock.
            float target = Mathf.Clamp(SteerInput, -1f, 1f);
            bool growing = Mathf.Abs(target) > 0.01f && (Mathf.Sign(target) != Mathf.Sign(steerState) || Mathf.Abs(target) > Mathf.Abs(steerState));
            float rate = growing ? steerRise : steerReturn;
            steerState = Mathf.MoveTowards(steerState, target, rate * dt);

            SampleGround();
            Vector3 up = groundNormal;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(transform.up, up);
            fwd.Normalize();
            Vector3 right = Vector3.Cross(up, fwd);

            Vector3 vel = rb.linearVelocity;
            float vF = Vector3.Dot(vel, fwd);
            float vR = Vector3.Dot(vel, right);
            ForwardSpeed = vF;

            float throttle = Mathf.Clamp(Throttle, -1f, 1f);
            if (groundFactor > 0f)
            {
                // ---------------------------------------------------------------- longitudinal
                float accel = 0f;
                bool braking = false;
                if (throttle > 0.01f)
                {
                    if (vF < -0.5f) { accel = Mathf.Min(brakeDeceleration, -vF / dt) * throttle; braking = true; }
                    else
                    {
                        float r = Mathf.Clamp01(vF / maxSpeed);
                        accel = (acceleration + driveResistance) * throttle * Mathf.Max(0f, 1f - r * r * r);
                    }
                }
                else if (throttle < -0.01f)
                {
                    if (vF > 0.5f) { accel = -Mathf.Min(brakeDeceleration, vF / dt) * -throttle; braking = true; }
                    else
                    {
                        float r = Mathf.Clamp01(-vF / reverseMaxSpeed);
                        accel = -(reverseAcceleration + driveResistance) * -throttle * Mathf.Max(0f, 1f - r * r * r);
                    }
                }
                else
                {
                    accel = -Mathf.Sign(vF) * Mathf.Min(coastDeceleration, Mathf.Abs(vF) / dt);
                }
                rb.AddForce(fwd * accel * groundFactor, ForceMode.Acceleration);

                // ---------------------------------------------------------------- steering (target yaw rate)
                float speedAbs = Mathf.Abs(vF);
                float t = Mathf.Clamp01(speedAbs / Mathf.Max(0.1f, maxSpeed));
                float yawRate = Mathf.Lerp(steerRateLow, steerRateHigh, t) * Mathf.Deg2Rad;
                float authority = Mathf.Clamp01(speedAbs / Mathf.Max(0.1f, steerFullAuthoritySpeed));
                float dirSign = vF >= -0.5f ? 1f : -1f;                 // steering is mirrored when reversing
                float targetYaw = steerState * yawRate * authority * dirSign;
                float currentYaw = Vector3.Dot(rb.angularVelocity, up);
                rb.AddTorque(up * ((targetYaw - currentYaw) * steerResponse * groundFactor), ForceMode.Acceleration);

                // ---------------------------------------------------------------- lateral grip
                float gripScale = braking && Mathf.Abs(steerState) > 0.2f ? brakingGripScale : 1f;
                float lat = -vR * grip * gripScale;
                float cap = maxGripAcceleration * gripScale;
                rb.AddForce(right * (Mathf.Clamp(lat, -cap, cap) * groundFactor), ForceMode.Acceleration);

                // ---------------------------------------------------------------- stability
                if (levelingStrength > 0f)
                {
                    Vector3 axis = Vector3.Cross(transform.up, up);
                    rb.AddTorque(axis * (levelingStrength * groundFactor), ForceMode.Acceleration);
                    Vector3 w = rb.angularVelocity;
                    Vector3 tilt = w - up * Vector3.Dot(w, up);
                    rb.AddTorque(-tilt * (levelingDamping * groundFactor), ForceMode.Acceleration);
                }
                rb.AddForce(-up * (downforce * Mathf.Abs(vF) * groundFactor), ForceMode.Acceleration);
            }
        }

        void SampleGround()
        {
            int count = 0;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < probes.Length; i++)
            {
                Vector3 origin = transform.TransformPoint(probes[i]);
                int n = Physics.RaycastNonAlloc(origin, Vector3.down, hits, probeLength, groundMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                Vector3 bestNormal = Vector3.up;
                for (int h = 0; h < n; h++)
                {
                    var hit = hits[h];
                    if (hit.collider.attachedRigidbody == rb) continue;      // own shell
                    if (hit.distance < best) { best = hit.distance; bestNormal = hit.normal; }
                }
                if (best < float.MaxValue) { count++; normal += bestNormal; }
            }
            grounded = count > 0;
            groundFactor = Mathf.Clamp01(count / 3f);                       // 3+ probes = full traction
            groundNormal = count > 0 ? normal.normalized : Vector3.up;
            if (groundNormal.y < 0.5f) groundNormal = Vector3.up;            // never "drive" up a wall
        }

        /// <summary>Back to rest: used by reset.</summary>
        public void ResetState()
        {
            steerState = 0f;
            ForwardSpeed = 0f;
        }
    }
}
