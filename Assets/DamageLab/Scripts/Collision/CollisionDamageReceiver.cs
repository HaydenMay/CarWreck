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
        [Tooltip("Severe crush (0 = off: the Collision Lab is unaffected). Strength saturates long before highway energies, so above this " +
                 "energy (J) the dent keeps deepening: depth x (1 + severeDepthGain x (1 - exp(-(E - severeEnergy) / severeEnergyScale))).")]
        public float severeEnergy;
        [Tooltip("Energy (J) above severeEnergy for 63% of the extra severe-crush depth.")]
        public float severeEnergyScale = 250000f;
        [Tooltip("Largest extra depth from severe crush, as a fraction of the full-strength depth (2 = up to 3x as deep).")]
        public float severeDepthGain = 2f;
        [Tooltip("How much the sideways (scraping) velocity tilts the push direction (0 = pure contact normal).")]
        [Range(0f, 1f)] public float tangentialDirectionWeight = 0.5f;

        [Header("Two deformable vehicles (off by default: the Collision Lab is unaffected)")]
        [Tooltip("The energy computed below is what the whole collision dissipates. When the other body is also a damageable vehicle (it has its own receiver) the two cars share it, so each applies only a fraction instead of both applying all of it. Against walls and other non-vehicles the car still takes all of it.")]
        public bool shareEnergyWithVehicles;
        [Tooltip("How lopsided the split can get. Each car's share of the energy depends on how much it was driving INTO the other (mass x speed towards it): 0.35 = an all-attacking car takes 15% and a stationary or retreating one takes 85%; both attacking equally = 50/50; 0 = always 50/50.")]
        [Range(0f, 0.5f)] public float momentumShareRange = 0.35f;

        [Header("Event filtering")]
        [Tooltip("Seconds after an impact during which the same body cannot create another one.")]
        public float cooldown = 0.5f;
        [Tooltip("Half-width (m) of the lane used to find the real body surface behind the contact point.")]
        public float surfaceLane = 0.12f;
        public bool logImpacts = true;
        [Tooltip("Use the impacting BoxCollider's shape for the dent footprint (off = Phase 1 point impacts).")]
        public bool geometryAwareFootprint = true;
        [Tooltip("When the other body touches the car with several aligned box colliders in the same event (a vehicle's segmented physics shell: " +
                 "FrontLeft / FrontCentre / FrontRight ...), the dent footprint is their combined box instead of only the first one reported. " +
                 "Only boxes that actually made contact are combined, so an offset hit stays local. A single-box impactor (wall, lab block) is unaffected.")]
        public bool combineImpactorBoxes = true;
        [Tooltip("Vehicle-vs-vehicle only (the other body has its own receiver, combineImpactorBoxes on): extra physics steps an event keeps gathering " +
                 "contacts before it is applied. Shell segments of a sloped or rounded face can first touch one step apart; without this the later " +
                 "ones fall into the cooldown and the dent covers only the first segment. Walls and lab blocks are processed immediately as before.")]
        [Range(0, 3)] public int vehicleGatherSteps = 1;
        [Tooltip("Vehicle-vs-vehicle only: the other vehicle's shell boxes that are aligned with the first one, whose leading face is within this " +
                 "distance (m, along the impact direction) of the first box's, and that lie in front of this car also join the footprint. In a fast " +
                 "hit continuous collision detection stops the cars at the first touching corner box, so the rest of a face that would have hit " +
                 "never reports a contact. The crush depth still decides which vertices dent, so a box beside this car adds nothing. 0 = off.")]
        [Range(0f, 0.5f)] public float vehicleFaceReach = 0.25f;
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
            public string carCollider;        // which car collider was struck (e.g. "FrontLeftBox")
            public string impactorColliders;  // the other body's box colliders that touched in this event ("FrontLeftBox + FrontCentreBox")
            public int impactorBoxes;         // how many of them shaped the footprint (1 = the first box only)
        }

        /// <summary>Diagnostics: every collision callback from a rigidbody, and whether it joined an event (false = ignored by the cooldown).</summary>
        public event Action<UnityEngine.Collision, bool> ContactsReceived;

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
            public BoxImpactor box;   // in the other body's frame until Process places it in vehicle (model) space
            public Vector3 otherPosition; public Quaternion otherRotation; // the other body's pose in vehicle space at the first box contact
            public Vector3 boxMin, boxMax; // union of the aligned impactor boxes, in the first box's frame (about its centre)
            public bool vehicle;      // the other body is a damageable vehicle: the event may gather contacts for vehicleGatherSteps more steps
            public int age;           // steps this event has already waited
            public Vector3 velocityBefore; // this car's velocity before the step that produced the first contact (momentum split)
            public readonly List<BoxCollider> boxes = new List<BoxCollider>(4);
            public string impactors;
            public string carCollider; // the car collider of the deepest contact
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
            bool cooling = lastHit.TryGetValue(other, out float t) && clock - t < cooldown;
            ContactsReceived?.Invoke(c, !cooling);
            if (cooling) return;

            c.GetContacts(contactBuffer);
            if (contactBuffer.Count == 0) return;
            if (!pending.TryGetValue(other, out var p))
            {
                pending[other] = p = new Pending { velocityBefore = velocityBeforeStep };
                p.vehicle = combineImpactorBoxes && vehicleGatherSteps > 0 && other.TryGetComponent<CollisionDamageReceiver>(out _);
            }
            // A gathered vehicle event takes its strength inputs (point, normal, velocity, impulse, penetration) from its first
            // step only, exactly as without gathering; contacts from the extra step only add impactor boxes (the footprint).
            bool first = p.age == 0;
            Vector3 com = body.worldCenterOfMass;
            if (first) foreach (var cp in contactBuffer)
            {
                Vector3 n = cp.normal;
                if (Vector3.Dot(n, com - cp.point) < 0f) n = -n;      // orient into the car
                // Contact points may be reported on either shape. A point lying inside the car's box (on the block's
                // penetrated face) is moved out by its penetration so it sits where the surfaces first met.
                Vector3 pt = cp.point;
                float pen = -cp.separation;
                if (pen > 0f && cp.thisCollider is BoxCollider own && InsideDepth(own, pt) > 0.5f * pen) pt -= n * pen;
                p.pointSum += model.transform.InverseTransformPoint(pt);       // vehicle space: an event may span steps
                p.normalSum += model.transform.InverseTransformDirection(n);
                p.contacts++;
                if (cp.separation <= p.minSeparation || p.carCollider == null) p.carCollider = cp.thisCollider != null ? cp.thisCollider.name : null;
                p.minSeparation = Mathf.Min(p.minSeparation, cp.separation);
            }
            if (first) p.impulse += c.impulse.magnitude;
            if (first && c.relativeVelocity.sqrMagnitude > p.relativeVelocity.sqrMagnitude) p.relativeVelocity = c.relativeVelocity;
            if (c.collider is BoxCollider bc && geometryAwareFootprint && !p.boxes.Contains(bc))
            {
                // The impactor's real shape: its box collider, kept in the other body's own frame (where its shell boxes are
                // fixed and aligned), and placed on this car at the pose of the event's first contact.
                var bt = bc.transform;
                var ot = other.transform;
                Vector3 center = ot.InverseTransformPoint(bt.TransformPoint(bc.center)), half = Vector3.Scale(bc.size, bt.lossyScale) * 0.5f;
                half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
                Quaternion rotation = Quaternion.Inverse(ot.rotation) * bt.rotation;
                if (!p.box.valid)
                {
                    p.box = new BoxImpactor { valid = true, center = center, rotation = rotation, halfExtents = half };
                    p.boxMin = -half; p.boxMax = half;
                    p.boxes.Add(bc); p.impactors = bc.name;
                    var root = model.transform;
                    p.otherPosition = root.InverseTransformPoint(ot.position);
                    p.otherRotation = Quaternion.Inverse(root.rotation) * ot.rotation;
                }
                else if (combineImpactorBoxes && Quaternion.Angle(rotation, p.box.rotation) < 0.5f)
                {
                    // Another segment of the same body's shell touched in this event (a vehicle's physics shell is tiled into
                    // boxes): grow the footprint box to cover it. Only touching boxes join, so an offset hit stays local.
                    Vector3 l = Quaternion.Inverse(p.box.rotation) * (center - p.box.center);
                    p.boxMin = Vector3.Min(p.boxMin, l - half); p.boxMax = Vector3.Max(p.boxMax, l + half);
                    p.boxes.Add(bc); p.impactors += " + " + bc.name;
                }
            }
        }

        /// <summary>The event's impactor box: the first box reported, or the combined box of every aligned box that touched.</summary>
        static BoxImpactor EventBox(Pending p)
        {
            var b = p.box;
            if (p.boxes.Count <= 1) return b;
            b.center += b.rotation * ((p.boxMin + p.boxMax) * 0.5f);
            b.halfExtents = (p.boxMax - p.boxMin) * 0.5f;
            return b;
        }

        readonly List<BoxCollider> faceBoxes = new List<BoxCollider>(32);

        /// <summary>
        /// Adds the other vehicle's untouched shell boxes of the same face to the event: aligned with the first box, leading face within
        /// vehicleFaceReach of the first box's along the impact direction, and overlapping this car's shell (so a box beside the car is skipped).
        /// Filled boxes are marked "~" in the impactor list.
        /// </summary>
        void FillFace(Rigidbody other, Pending p, Vector3 inwardVehicle)
        {
            Vector3 d = Quaternion.Inverse(p.otherRotation) * inwardVehicle;   // impact direction in the other body's frame
            float firstLead = Lead(p.box.center, p.box.rotation, p.box.halfExtents, d);
            Bounds mine = ShellBounds();
            mine.Expand(2f * vehicleFaceReach);
            var ot = other.transform;
            other.GetComponentsInChildren(faceBoxes);
            foreach (var bc in faceBoxes)
            {
                if (bc.isTrigger || !bc.enabled || bc.attachedRigidbody != other || p.boxes.Contains(bc)) continue;
                var bt = bc.transform;
                Quaternion rotation = Quaternion.Inverse(ot.rotation) * bt.rotation;
                if (Quaternion.Angle(rotation, p.box.rotation) >= 0.5f) continue;
                Vector3 center = ot.InverseTransformPoint(bt.TransformPoint(bc.center)), half = Vector3.Scale(bc.size, bt.lossyScale) * 0.5f;
                half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
                if (firstLead - Lead(center, rotation, half, d) > vehicleFaceReach) continue;     // further back: not this face
                // In front of this car: its box (at the first contact's pose, vehicle space) overlaps this car's shell bounds.
                Vector3 c = p.otherPosition + p.otherRotation * center;
                Quaternion r = p.otherRotation * rotation;
                Vector3 ext = new Vector3(
                    Mathf.Abs((r * Vector3.right).x) * half.x + Mathf.Abs((r * Vector3.up).x) * half.y + Mathf.Abs((r * Vector3.forward).x) * half.z,
                    Mathf.Abs((r * Vector3.right).y) * half.x + Mathf.Abs((r * Vector3.up).y) * half.y + Mathf.Abs((r * Vector3.forward).y) * half.z,
                    Mathf.Abs((r * Vector3.right).z) * half.x + Mathf.Abs((r * Vector3.up).z) * half.y + Mathf.Abs((r * Vector3.forward).z) * half.z);
                if (!mine.Intersects(new Bounds(c, 2f * ext))) continue;
                Vector3 l = Quaternion.Inverse(p.box.rotation) * (center - p.box.center);
                p.boxMin = Vector3.Min(p.boxMin, l - half); p.boxMax = Vector3.Max(p.boxMax, l + half);
                p.boxes.Add(bc); p.impactors += " + ~" + bc.name;
            }
        }

        /// <summary>How far a box reaches along d (its leading face).</summary>
        static float Lead(Vector3 center, Quaternion rotation, Vector3 half, Vector3 d)
        {
            return Vector3.Dot(center, d) + Mathf.Abs(Vector3.Dot(rotation * Vector3.right, d)) * half.x
                 + Mathf.Abs(Vector3.Dot(rotation * Vector3.up, d)) * half.y + Mathf.Abs(Vector3.Dot(rotation * Vector3.forward, d)) * half.z;
        }

        readonly List<BoxCollider> ownBoxes = new List<BoxCollider>(32);

        /// <summary>This car's physics shell bounds in vehicle (model) space.</summary>
        Bounds ShellBounds()
        {
            var root = model.transform;
            body.GetComponentsInChildren(ownBoxes);
            bool any = false;
            var b = new Bounds();
            foreach (var bc in ownBoxes)
            {
                if (bc.isTrigger || !bc.enabled || bc.attachedRigidbody != body) continue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = bc.center + Vector3.Scale(bc.size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 v = root.InverseTransformPoint(bc.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(v, Vector3.zero); any = true; } else b.Encapsulate(v);
                }
            }
            return b;
        }

        /// <summary>The event box (other body's frame) placed in vehicle space at the first contact's pose.</summary>
        static BoxImpactor InVehicleSpace(BoxImpactor b, Pending p)
        {
            b.center = p.otherPosition + p.otherRotation * b.center;
            b.rotation = p.otherRotation * b.rotation;
            return b;
        }

        /// <summary>Advance the event clock and turn the contacts gathered since the last call into impacts.</summary>
        public void Step(float dt)
        {
            clock += dt;
            if (pending.Count > 0)
            {
                ready.Clear();
                foreach (var kv in pending)
                {
                    if (kv.Value.vehicle && kv.Value.age < vehicleGatherSteps) kv.Value.age++;   // still gathering this vehicle's contacts
                    else ready.Add(kv.Key);
                }
                foreach (var other in ready) { Process(other, pending[other]); pending.Remove(other); }
            }
            if (body != null) velocityBeforeStep = body.linearVelocity;   // read by the next Step: the velocity from before the physics step that produced its contacts
        }

        Vector3 velocityBeforeStep;
        readonly List<Rigidbody> ready = new List<Rigidbody>(4);

        /// <summary>This car's share (0.5 - range .. 0.5 + range) of the dissipated energy in a vehicle-vs-vehicle hit. Momentum each car was driving into the other (mass x speed along the contact normal, from before the impact): the more it attacked, the smaller its share; a stationary or retreating car takes the larger one.</summary>
        float MomentumShare(Rigidbody other, Vector3 inwardNormal, Vector3 otherRelativeVelocity, Vector3 vMe)
        {
            Vector3 vOther = vMe + otherRelativeVelocity;                  // pre-impact: relative velocity is the other body's velocity relative to this one
            float pMe = body.mass * Mathf.Max(0f, -Vector3.Dot(vMe, inwardNormal));
            float pOther = other.mass * Mathf.Max(0f, Vector3.Dot(vOther, inwardNormal));
            float total = pMe + pOther;
            float attack = total > 1e-3f ? pMe / total : 0.5f;             // 1 = I did all the attacking, 0 = none
            return Mathf.Clamp(0.5f + momentumShareRange * (1f - 2f * attack), 0.5f - momentumShareRange, 0.5f + momentumShareRange);
        }

        /// <summary>Crush multiplier for energies past strength saturation (1 = none; see <see cref="severeEnergy"/>).</summary>
        public float SevereDepthScale(float energy)
        {
            if (severeEnergy <= 0f || energy <= severeEnergy) return 1f;
            return 1f + Mathf.Max(0f, severeDepthGain) * (1f - Mathf.Exp(-(energy - severeEnergy) / Mathf.Max(1f, severeEnergyScale)));
        }

        void Process(Rigidbody other, Pending p)
        {
            lastHit[other] = clock;
            EventCount++;
            var root = model.transform;
            Vector3 point = root.TransformPoint(p.pointSum / p.contacts);
            Vector3 n = p.normalSum.sqrMagnitude > 1e-8f ? root.TransformDirection(p.normalSum.normalized) : (body.worldCenterOfMass - point).normalized;
            // A fast block can be reported only after it has moved part of a step into the collider. Back the impactor
            // pose out along the normal by that penetration (contact points were corrected per contact above).
            float penetration = Mathf.Max(0f, -p.minSeparation);
            if (p.box.valid && p.vehicle && vehicleFaceReach > 0f) FillFace(other, p, root.InverseTransformDirection(n));
            if (p.box.valid) p.box = InVehicleSpace(EventBox(p), p);
            if (p.box.valid) p.box.center -= root.InverseTransformDirection(n) * penetration;
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
            if (shareEnergyWithVehicles && other.TryGetComponent<CollisionDamageReceiver>(out _)) energy *= MomentumShare(other, n, v, p.velocityBefore);
            float strength = energy <= energyThreshold ? 0f : 1f - Mathf.Exp(-(energy - energyThreshold) / Mathf.Max(1f, energyScale));

            Vector3 dir = separating ? n : (n * vn + vt * tangentialDirectionWeight).normalized;
            Vector3 localDir = root.InverseTransformDirection(dir);
            Vector3 localContact = root.InverseTransformPoint(point);
            model.SurfacePointAlong(localContact, localDir, out Vector3 localPoint, surfaceLane);

            var impact = new LocalImpact
            {
                point = localPoint,
                direction = localDir,
                normal = -root.InverseTransformDirection(n),
                strength = strength,
                depthScale = SevereDepthScale(energy),
            };
            if (p.box.valid) impact.impactor = p.box;   // already in vehicle space

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
                carCollider = p.carCollider,
                impactorColliders = p.impactors,
                impactorBoxes = p.boxes.Count,
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
            bool keepLast = !report.applied && HasReport && Last.applied && (report.suppressed || spent.Contains(other) || (report.strength <= 0f && clock - lastAppliedClock < 3f));
            if (report.applied) lastAppliedClock = clock;
            if (!keepLast) Last = report;
            HasReport = true;
            if (logImpacts)
                UnityEngine.Debug.Log($"[CollisionLab] {report.other} -> car: |v| {report.relativeSpeed:0.00} m/s, closing {report.closingSpeed:0.00} m/s, " +
                                      $"impulse {report.impulse:0} N·s, μ {report.reducedMass:0} kg, E {report.energy / 1000f:0.0} kJ, strength {report.strength * 100f:0.0}, " +
                                      $"{report.contacts} contacts, at {report.localPoint.ToString("F2")} ({report.region}) dir {report.localDirection.ToString("F2")}, " +
                                      $"{(report.footprint.valid ? $"footprint {report.footprint.kind} {report.footprint.halfU * 2f:0.00} x {report.footprint.halfV * 2f:0.00} m ({report.footprint.area:0.00} m², crush {report.footprint.travel:0.000} m), " : "")}" +
                                      $"{(report.impactorBoxes > 1 ? $"impactor {report.impactorColliders}, " : "")}" +
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
