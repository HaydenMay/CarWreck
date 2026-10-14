using System;
using System.Collections.Generic;
using System.Diagnostics;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Turns real Unity collisions on the car's Rigidbody into localized damage.
    ///
    /// Per collision EVENT (all contacts reported for one other Rigidbody during one physics step are merged):
    ///   n      contact normal, averaged over the contact points, oriented into the car
    ///   v      relative velocity of the other body with respect to the car at the moment of contact
    ///   vn     closing speed along n (only this part crushes; sideways scraping does not)
    ///   μ      reduced mass  m_car·m_other / (m_car + m_other)
    ///   E      = ½ μ vn²   kinetic energy of the normal approach that the collision must dissipate
    ///          (doubling speed = 4× energy; a heavier impactor adds energy but saturates at the car's mass)
    ///   s      = 1 - exp(-(E - E0) / Es) for E > E0, else 0      (impact strength 0..1)
    /// The impact pushes along n·vn + w·vt (mostly the normal, tilted by the scraping direction), at the point
    /// where that line first meets the ORIGINAL car surface (the colliders are simplified boxes).
    /// Contacts with static geometry (the ground) are ignored, and a body that just hit the car is ignored for
    /// a short cooldown, so resting contact, bounces and multiple contact points never add extra impacts.
    /// </summary>
    [DisallowMultipleComponent, ExecuteAlways] // ExecuteAlways: also receives contacts during edit-mode validation (Physics.Simulate)
    public class CollisionDamageReceiver : MonoBehaviour
    {
        public VehicleDamageModel model;
        public Rigidbody body;

        [Header("Severity mapping (energy of the normal approach, J)")]
        [Tooltip("Below this energy a collision leaves no damage (light bumps, settling, vibrations).")]
        public float energyThreshold = 1500f;
        [Tooltip("Energy above the threshold for strength ≈ 63%. Strength = 1 - exp(-(E - threshold) / scale).")]
        public float energyScale = 60000f;
        [Tooltip("How much the sideways (scraping) velocity tilts the push direction (0 = pure contact normal).")]
        [Range(0f, 1f)] public float tangentialDirectionWeight = 0.5f;

        [Header("Event filtering")]
        [Tooltip("Seconds after an impact during which the same body cannot create another one.")]
        public float cooldown = 0.5f;
        [Tooltip("Half-width (m) of the lane used to find the real body surface behind the contact point.")]
        public float surfaceLane = 0.12f;
        public bool logImpacts = true;

        public struct Report
        {
            public string other;
            public float relativeSpeed;   // |v| (m/s)
            public float closingSpeed;    // vn (m/s)
            public float impulse;         // collision impulse magnitude (N·s), summed over contacts
            public float reducedMass;     // kg
            public float energy;          // J
            public float strength;        // 0..1
            public bool applied;
            public int contacts;
            public Vector3 localPoint;    // vehicle space, on the original surface
            public Vector3 localDirection;
            public string region;
            public float deformMilliseconds;
        }

        public bool HasReport { get; private set; }
        public Report Last { get; private set; }
        public int EventCount { get; private set; }
        public event Action<Report> ImpactRegistered;

        class Pending
        {
            public Vector3 pointSum, normalSum, relativeVelocity;
            public float impulse;
            public int contacts;
        }

        readonly Dictionary<Rigidbody, Pending> pending = new Dictionary<Rigidbody, Pending>();
        readonly Dictionary<Rigidbody, float> lastHit = new Dictionary<Rigidbody, float>();
        readonly List<ContactPoint> contactBuffer = new List<ContactPoint>(32);
        readonly Stopwatch stopwatch = new Stopwatch();
        float clock;

        void Reset()
        {
            model = GetComponent<VehicleDamageModel>();
            body = GetComponent<Rigidbody>();
        }

        /// <summary>Forget pending contacts, cooldowns and the last report (used by the lab's RESET).</summary>
        public void ResetState()
        {
            pending.Clear();
            lastHit.Clear();
            HasReport = false;
            EventCount = 0;
        }

        void OnCollisionEnter(UnityEngine.Collision c) => Accumulate(c);

        // FixedUpdate runs before each physics step, so it processes the contacts gathered in the previous step.
        void FixedUpdate() => Step(Time.fixedDeltaTime);

        void Accumulate(UnityEngine.Collision c)
        {
            var other = c.rigidbody;
            if (other == null || other == body) return;              // ground and other static geometry
            if (lastHit.TryGetValue(other, out float t) && clock - t < cooldown) return;

            c.GetContacts(contactBuffer);
            if (contactBuffer.Count == 0) return;
            if (!pending.TryGetValue(other, out var p)) pending[other] = p = new Pending();
            Vector3 com = body.worldCenterOfMass;
            foreach (var cp in contactBuffer)
            {
                Vector3 n = cp.normal;
                if (Vector3.Dot(n, com - cp.point) < 0f) n = -n;      // orient into the car
                p.pointSum += cp.point;
                p.normalSum += n;
                p.contacts++;
            }
            p.impulse += c.impulse.magnitude;
            if (c.relativeVelocity.sqrMagnitude > p.relativeVelocity.sqrMagnitude) p.relativeVelocity = c.relativeVelocity;
        }

        /// <summary>Advance the event clock and turn the contacts gathered since the last call into impacts.</summary>
        public void Step(float dt)
        {
            clock += dt;
            if (pending.Count == 0) return;
            foreach (var kv in pending) Process(kv.Key, kv.Value);
            pending.Clear();
        }

        void Process(Rigidbody other, Pending p)
        {
            lastHit[other] = clock;
            EventCount++;
            Vector3 n = p.normalSum.sqrMagnitude > 1e-8f ? p.normalSum.normalized : (body.worldCenterOfMass - p.pointSum / p.contacts).normalized;
            Vector3 point = p.pointSum / p.contacts;
            Vector3 v = p.relativeVelocity;
            if (Vector3.Dot(v, n) < 0f) v = -v;                       // the other body moves INTO the car
            float vn = Vector3.Dot(v, n);
            Vector3 vt = v - n * vn;

            float mOther = other.isKinematic ? float.PositiveInfinity : other.mass;
            float mu = float.IsInfinity(mOther) ? body.mass : body.mass * mOther / (body.mass + mOther);
            float energy = 0.5f * mu * vn * vn;
            float strength = energy <= energyThreshold ? 0f : 1f - Mathf.Exp(-(energy - energyThreshold) / Mathf.Max(1f, energyScale));

            Vector3 dir = (n * vn + vt * tangentialDirectionWeight).normalized;
            var root = model.transform;
            Vector3 localDir = root.InverseTransformDirection(dir);
            Vector3 localContact = root.InverseTransformPoint(point);
            model.SurfacePointAlong(localContact, localDir, out Vector3 localPoint, surfaceLane);

            var report = new Report
            {
                other = other.name,
                relativeSpeed = v.magnitude,
                closingSpeed = vn,
                impulse = p.impulse,
                reducedMass = mu,
                energy = energy,
                strength = strength,
                contacts = p.contacts,
                localPoint = localPoint,
                localDirection = localDir,
                region = Region(localPoint),
            };
            if (strength > 0f)
            {
                stopwatch.Restart();
                report.applied = model.AddImpact(new LocalImpact
                {
                    point = localPoint,
                    direction = localDir,
                    normal = -root.InverseTransformDirection(n),
                    strength = strength,
                });
                stopwatch.Stop();
                report.deformMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
            }
            Last = report;
            HasReport = true;
            if (logImpacts)
                UnityEngine.Debug.Log($"[CollisionLab] {report.other} -> car: |v| {report.relativeSpeed:0.00} m/s, closing {report.closingSpeed:0.00} m/s, " +
                                      $"impulse {report.impulse:0} N·s, μ {report.reducedMass:0} kg, E {report.energy / 1000f:0.0} kJ, strength {report.strength * 100f:0.0}, " +
                                      $"{report.contacts} contacts, at {report.localPoint.ToString("F2")} ({report.region}) dir {report.localDirection.ToString("F2")}, " +
                                      $"{(report.applied ? $"deform {report.deformMilliseconds:0.0} ms" : "no damage")}");
            ImpactRegistered?.Invoke(report);
        }

        string Region(Vector3 p)
        {
            Bounds b = model.BodyBounds;
            Vector3 f = p - b.center;
            float fx = f.x / Mathf.Max(0.01f, b.extents.x), fz = f.z / Mathf.Max(0.01f, b.extents.z);
            string lon = fz > 0.55f ? "front" : fz < -0.55f ? "rear" : "middle";
            string lat = fx < -0.5f ? "left" : fx > 0.5f ? "right" : "centre";
            string height = p.y > b.max.y + 0.05f ? ", roof/cabin" : "";
            return $"{lon} {lat}{height}";
        }
    }
}
