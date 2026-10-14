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
        [Tooltip("Use the impacting BoxCollider's shape for the dent footprint (off = Phase 1 point impacts).")]
        public bool geometryAwareFootprint = true;
        [Tooltip("After a body has made its damaging impact, its further contacts (bounces, depenetration pushes, re-touches) are logged " +
                 "but cause no damage until it is re-armed (ClearCooldowns / ResetState: the lab does this on every launch and block reset).")]
        public bool oneImpactPerLaunch = true;

        public struct Report
        {
            public string other;
            public float relativeSpeed;   // |v| (m/s)
            public float closingSpeed;    // vn (m/s), 0 when separating
            public float rawClosing;      // relative velocity along the inward normal before clamping (< 0 = separating)
            public bool separating;
            public float impulse;         // collision impulse magnitude (N·s), summed over contacts
            public float reducedMass;     // kg
            public float energy;          // J
            public float strength;        // 0..1
            public bool applied;
            public bool suppressed;       // had strength, but this body already made its damaging impact this launch
            public int contacts;
            public Vector3 localPoint;    // vehicle space, on the original surface
            public Vector3 contactPoint;  // vehicle space, averaged physics contact point backed out by the penetration (where the colliders met)
            public float penetration;     // how far the block had moved into the car collider when the contact was reported (m)
            public Vector3 localDirection;
            public string region;
            public float deformMilliseconds;
            public ImpactFootprint footprint; // contact patch (geometry-aware impacts)
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
            public float minSeparation;   // most negative ContactPoint.separation (= penetration when the contact was reported)
            public BoxImpactor box;   // world space, pose at the first reported contact
        }

        readonly Dictionary<Rigidbody, Pending> pending = new Dictionary<Rigidbody, Pending>();
        readonly Dictionary<Rigidbody, float> lastHit = new Dictionary<Rigidbody, float>();
        readonly HashSet<Rigidbody> spent = new HashSet<Rigidbody>();   // bodies that already made their damaging impact
        readonly List<ContactPoint> contactBuffer = new List<ContactPoint>(32);
        readonly Stopwatch stopwatch = new Stopwatch();
        float lastAppliedClock = float.NegativeInfinity;
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
            spent.Clear();
            HasReport = false;
            EventCount = 0;
        }

        /// <summary>Forget pending contacts and cooldowns only (the block was re-parked for an intentional new launch).</summary>
        public void ClearCooldowns()
        {
            pending.Clear();
            lastHit.Clear();
            spent.Clear();
            lastAppliedClock = float.NegativeInfinity;
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
                // Contact points may be reported on either shape. A point lying inside the car's box (on the block's
                // penetrated face) is moved out by its penetration so it sits where the surfaces first met.
                Vector3 pt = cp.point;
                float pen = -cp.separation;
                if (pen > 0f && cp.thisCollider is BoxCollider own && InsideDepth(own, pt) > 0.5f * pen) pt -= n * pen;
                p.pointSum += pt;
                p.normalSum += n;
                p.contacts++;
                p.minSeparation = Mathf.Min(p.minSeparation, cp.separation);
            }
            p.impulse += c.impulse.magnitude;
            if (c.relativeVelocity.sqrMagnitude > p.relativeVelocity.sqrMagnitude) p.relativeVelocity = c.relativeVelocity;
            if (!p.box.valid && c.collider is BoxCollider bc && geometryAwareFootprint)
            {
                // The impactor's real shape: its box collider at the contact pose (world space).
                var bt = bc.transform;
                p.box = new BoxImpactor
                {
                    valid = true,
                    center = bt.TransformPoint(bc.center),
                    rotation = bt.rotation,
                    halfExtents = Vector3.Scale(bc.size, bt.lossyScale) * 0.5f,
                };
            }
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
            // A fast block can be reported only after it has moved part of a step into the collider. Back the impactor
            // pose out along the normal by that penetration (contact points were corrected per contact above).
            float penetration = Mathf.Max(0f, -p.minSeparation);
            Vector3 point = p.pointSum / p.contacts;
            if (p.box.valid) p.box.center -= n * penetration;
            // Collision.relativeVelocity is the other body's velocity relative to this one (verified by the
            // validation: every approaching hit has vn > 0). A contact whose bodies are already separating
            // along the normal is not an impact, so it is never flipped into one.
            Vector3 v = p.relativeVelocity;
            float vn = ClosingSpeed(v, n, out bool separating);
            Vector3 vt = v - n * Vector3.Dot(v, n);

            float mOther = other.isKinematic ? float.PositiveInfinity : other.mass;
            // An anchored car (all axes frozen) cannot move: it acts as infinite mass, so the impactor's own mass is what counts.
            bool carFixed = body.isKinematic || body.constraints == RigidbodyConstraints.FreezeAll;
            float mCar = carFixed ? float.PositiveInfinity : body.mass;
            float mu = float.IsInfinity(mOther) && float.IsInfinity(mCar) ? body.mass
                     : float.IsInfinity(mOther) ? mCar : float.IsInfinity(mCar) ? mOther : mCar * mOther / (mCar + mOther);
            float energy = 0.5f * mu * vn * vn;
            float strength = energy <= energyThreshold ? 0f : 1f - Mathf.Exp(-(energy - energyThreshold) / Mathf.Max(1f, energyScale));

            Vector3 dir = separating ? n : (n * vn + vt * tangentialDirectionWeight).normalized;
            var root = model.transform;
            Vector3 localDir = root.InverseTransformDirection(dir);
            Vector3 localContact = root.InverseTransformPoint(point);
            model.SurfacePointAlong(localContact, localDir, out Vector3 localPoint, surfaceLane);

            var impact = new LocalImpact
            {
                point = localPoint,
                direction = localDir,
                normal = -root.InverseTransformDirection(n),
                strength = strength,
            };
            if (p.box.valid)
                impact.impactor = new BoxImpactor
                {
                    valid = true,
                    center = root.InverseTransformPoint(p.box.center),
                    rotation = Quaternion.Inverse(root.rotation) * p.box.rotation,
                    halfExtents = p.box.halfExtents,
                };

            var report = new Report
            {
                other = other.name,
                relativeSpeed = v.magnitude,
                closingSpeed = vn,
                rawClosing = Vector3.Dot(v, n),
                separating = separating,
                impulse = p.impulse,
                reducedMass = mu,
                energy = energy,
                strength = strength,
                contacts = p.contacts,
                localPoint = localPoint,
                localDirection = localDir,
                contactPoint = localContact,
                penetration = penetration,
                region = Region(localPoint),
            };
            report.suppressed = strength > 0f && oneImpactPerLaunch && spent.Contains(other);
            if (strength > 0f && !report.suppressed)
            {
                stopwatch.Restart();
                report.applied = model.AddImpact(impact);
                stopwatch.Stop();
                report.deformMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
                if (report.applied)
                {
                    spent.Add(other);
                    report.footprint = model.Impacts[model.ImpactCount - 1].footprint;
                    if (report.footprint.valid) { report.localPoint = report.footprint.center; report.region = Region(report.footprint.center); }
                }
            }
            // A harmless (below-threshold) re-touch shortly after a damaging hit, or a suppressed second contact of the same launch,
            // must not replace the hit in the diagnostics; it is still counted and logged.
            bool keepLast = !report.applied && HasReport && Last.applied && (report.suppressed || (report.strength <= 0f && clock - lastAppliedClock < 3f));
            if (report.applied) lastAppliedClock = clock;
            if (!keepLast) Last = report;
            HasReport = true;
            if (logImpacts)
                UnityEngine.Debug.Log($"[CollisionLab] {report.other} -> car: |v| {report.relativeSpeed:0.00} m/s, closing {report.closingSpeed:0.00} m/s, " +
                                      $"impulse {report.impulse:0} N·s, μ {report.reducedMass:0} kg, E {report.energy / 1000f:0.0} kJ, strength {report.strength * 100f:0.0}, " +
                                      $"{report.contacts} contacts, at {report.localPoint.ToString("F2")} ({report.region}) dir {report.localDirection.ToString("F2")}, " +
                                      $"{(report.footprint.valid ? $"footprint {report.footprint.kind} {report.footprint.halfU * 2f:0.00} x {report.footprint.halfV * 2f:0.00} m ({report.footprint.area:0.00} m², crush {report.footprint.travel:0.000} m), " : "")}" +
                                      $"{(report.separating ? "separating, " : "")}{(report.applied ? $"deform {report.deformMilliseconds:0.0} ms" : report.suppressed ? "no damage (block already made its impact this launch)" : "no damage")}");
            ImpactRegistered?.Invoke(report);
        }


        /// <summary>How far point p (world) lies inside the box collider (≤ 0 = on or outside its surface).</summary>
        static float InsideDepth(BoxCollider bc, Vector3 p)
        {
            Vector3 l = bc.transform.InverseTransformPoint(p) - bc.center;
            Vector3 h = Vector3.Scale(bc.size * 0.5f, bc.transform.lossyScale);
            l = Vector3.Scale(l, bc.transform.lossyScale);
            return Mathf.Min(h.x - Mathf.Abs(l.x), Mathf.Min(h.y - Mathf.Abs(l.y), h.z - Mathf.Abs(l.z)));
        }
        /// <summary>Approach speed along the inward normal n for the other body's velocity relative to the car;
        /// 0 (and <paramref name="separating"/>) when the bodies are moving apart.</summary>
        public static float ClosingSpeed(Vector3 relativeVelocity, Vector3 inwardNormal, out bool separating)
        {
            float vn = Vector3.Dot(relativeVelocity, inwardNormal);
            separating = vn <= 0f;
            return separating ? 0f : vn;
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
