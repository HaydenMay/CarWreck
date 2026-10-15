using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Collision Lab phase 4: damage-aware hitbox over the whole car. Coverage of every region, locality of repeated hits,
    /// local saturation, an 84-hit mixed stress run, repair/reset exactness and costs. Real PhysX, anchored car.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        struct Target
        {
            public string name, region; public ImpactDirection d; public float offset; public int axis; public float sign;
        }

        static Target T(string n, ImpactDirection d, float off, string region, int axis, float sign) =>
            new Target { name = n, d = d, offset = off, region = region, axis = axis, sign = sign };

        // The listed offsets are only nominal; the run aims every straight hit at its region's centre (Off), computed from the
        // vehicle's own hitbox layout, so the same targets work on any vehicle.
        static readonly Target[] Targets =
        {
            T("front centre", ImpactDirection.Front, 0f, "FrontCentre", 2, +1),
            T("front left", ImpactDirection.Front, -0.65f, "FrontLeft", 2, +1),
            T("front right", ImpactDirection.Front, 0.65f, "FrontRight", 2, +1),
            T("rear centre", ImpactDirection.Rear, 0f, "RearCentre", 2, -1),
            T("rear left", ImpactDirection.Rear, -0.65f, "RearLeft", 2, -1),
            T("rear right", ImpactDirection.Rear, 0.65f, "RearRight", 2, -1),
            T("left side front", ImpactDirection.LeftSide, 1.35f, "LeftFront", 0, -1),
            T("left side mid", ImpactDirection.LeftSide, 0.15f, "LeftMid", 0, -1),
            T("left side rear", ImpactDirection.LeftSide, -1.05f, "LeftRear", 0, -1),
            T("right side front", ImpactDirection.RightSide, 1.35f, "RightFront", 0, +1),
            T("right side mid", ImpactDirection.RightSide, 0.15f, "RightMid", 0, +1),
            T("right side rear", ImpactDirection.RightSide, -1.05f, "RightRear", 0, +1),
            T("front-left corner angled", ImpactDirection.FrontCornerAngled, 0f, "FrontLeft", -1, 0),
            T("rear-right corner angled", ImpactDirection.RearCornerAngled, 0f, "RearRight", -1, 0),
            T("front-right corner angled", ImpactDirection.FrontRightCornerAngled, 0f, "FrontRight", -1, 0),
            T("rear-left corner angled", ImpactDirection.RearLeftCornerAngled, 0f, "RearLeft", -1, 0),
        };



        /// <summary>Screenshot prefix: none for the muscle car (keeps the phase 4 file names), the vehicle name otherwise.</summary>
        static string Tag() => lab.vehicleIndex == 0 ? "" : (lab.CurrentVehicle != null ? lab.CurrentVehicle.name : "Vehicle") + "_";
        /// <summary>Aim offset that puts the impactor on the target region's centre (from the vehicle's own hitbox layout).</summary>
        static float Off(Target t)
        {
            if (t.axis < 0) return 0f;   // corners aim at the body corner itself
            var r = lab.hitbox.Find(t.region);
            if (r == null) return t.offset;
            lab.Aim(t.d, out Vector3 target, out _);
            Vector3 c = r.Outer.center;
            return t.d == ImpactDirection.LeftSide || t.d == ImpactDirection.RightSide ? c.z - target.z : c.x - target.x;
        }
        /// <summary>Contact position measured outward along the approach (bigger = further out from the car).</summary>
        static float Outward(CollisionDamageReceiver.Report r, ImpactDirection d)
        {
            Vector3 dir = CollisionLabController.Direction(d);
            return -Vector3.Dot(r.contactPoint, dir.normalized);
        }

        /// <summary>
        /// Where the impactor's leading point was when it first touched the car, measured outward along the approach: the
        /// latest impact's box at its first-contact pose (backed out by the penetration the solver reported), projected
        /// onto the approach direction. Unlike the averaged contact point this does not slide along a corner when PhysX
        /// reports contacts on two faces, so it is the reliable "how far in did the block get before it hit" measure.
        /// </summary>
        static float ImpactorFront(ImpactDirection d)
        {
            var m = lab.model;
            if (m.ImpactCount == 0 || !m.Impacts[m.ImpactCount - 1].impactor.valid) return float.NaN;
            var b = m.Impacts[m.ImpactCount - 1].impactor;
            Vector3 dir = CollisionLabController.Direction(d).normalized;
            float lead = Vector3.Dot(b.center, dir)
                       + Mathf.Abs(Vector3.Dot(b.rotation * Vector3.right, dir)) * b.halfExtents.x
                       + Mathf.Abs(Vector3.Dot(b.rotation * Vector3.up, dir)) * b.halfExtents.y
                       + Mathf.Abs(Vector3.Dot(b.rotation * Vector3.forward, dir)) * b.halfExtents.z;
            return -lead;
        }

        /// <summary>
        /// Where the block will first touch the car's physical hitbox on its next launch, measured outward along the
        /// approach: the parked block swept (Physics.BoxCast) along its launch path against the car's current colliders.
        /// Exact and independent of the physics step: at 15 m/s a 4 m block moves 30 cm per 20 ms step and PhysX does not
        /// sweep it (it is large relative to that motion), so poses and contact points reported after the step land 0-15 cm
        /// past the true first touch depending on the step phase.
        /// </summary>
        struct SweptTouch
        {
            public float outward;
            public Collider collider;
            public Vector3 parkedCenter, half;
        }

        static SweptTouch SweptFirstTouch(Target t, bool narrow = true)
        {
            lab.narrowImpactor = narrow; lab.aimOffset = Off(t); lab.direction = t.d;
            lab.ResetBlock();
            var bc = lab.sled.GetComponent<BoxCollider>(); var tr = bc.transform;
            Vector3 c = tr.TransformPoint(bc.center), half = Vector3.Scale(bc.size, tr.lossyScale) * 0.5f, dir = tr.forward;
            Physics.SyncTransforms();
            float best = float.MaxValue; Collider first = null;
            foreach (var h in Physics.BoxCastAll(c, half, dir, tr.rotation, 20f, ~0, QueryTriggerInteraction.Ignore))
                if (h.distance > 0f && h.collider.GetComponentInParent<VehicleHitbox>() == lab.hitbox && h.distance < best)
                { best = h.distance; first = h.collider; }
            var result = new SweptTouch { outward = float.NaN, collider = first,
                parkedCenter = lab.car.transform.InverseTransformPoint(c),
                half = AdaptiveContactValidation.ProjectedHalfExtents(Quaternion.Inverse(lab.car.rotation) * tr.rotation, half) };
            if (best == float.MaxValue) return result;
            Vector3 lead = lab.car.transform.InverseTransformPoint(c + dir * (half.z + best));
            result.outward = -Vector3.Dot(lead, CollisionLabController.Direction(t.d).normalized);
            return result;
        }

        static float FaceValue(VehicleHitbox hb, Target t) { var f = hb.FaceOf(t.region, t.axis, t.sign); return f != null ? f.current : float.NaN; }
        static float FaceOriginal(VehicleHitbox hb, Target t) { var f = hb.FaceOf(t.region, t.axis, t.sign); return f != null ? f.original : float.NaN; }

        /// <summary>Sum of how far every face of a region has retracted (m).</summary>
        static float RegionRetract(VehicleHitbox hb, string region)
        {
            float s = 0f;
            foreach (var f in hb.Faces) if (f.region.name == region) s += f.original - f.current;
            return s;
        }

        static bool DiagOk()
        {
            lab.RefreshDiagnostics();
            string diag = lab.diagnosticsText != null ? lab.diagnosticsText.text : "";
            var shown = receiver.Last;
            return shown.applied && diag.Contains($"{shown.strength * 100f:0} / 100") && !diag.Contains("not applied") && !diag.Contains("No collision");
        }

        static float HitMoved(Target t, float speed, float mass, bool narrow, out CollisionDamageReceiver.Report r)
        {
            var before = CollisionLabValidation.Snapshot(filters);
            r = Hit(t.d, speed, mass, narrow, Off(t), out _);
            return CollisionLabValidation.MaxDiff(before, CollisionLabValidation.Snapshot(filters));
        }

        public static string RunWholeVehicle(bool screenshots = true) => RunWholeVehicle(0, screenshots);

        public static string RunWholeVehicle(int vehicle, bool screenshots = true)
        {
            var sb = new StringBuilder();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(vehicle);
            sb.AppendLine($"[CollisionLab] Phase 4: whole-vehicle damage-aware hitbox - {lab.CurrentVehicle?.displayName} (real PhysX, Physics.Simulate 50 Hz, anchored car)");
            physicsMs = 0; physicsSteps = 0; hitboxMs.Clear(); deformMs.Clear(); refitCounts.Clear();
            var colliderDebug = Object.FindAnyObjectByType<PhysicsHitboxDebug>();
            try
            {
                var hb = lab.hitbox;
                var model = lab.model;
                hb.Setup();
                int colliders = 0;
                foreach (var c in lab.car.GetComponentsInChildren<Collider>()) if (c.enabled) colliders++;
                var originalBoxes = new List<(Vector3 c, Vector3 s)>();
                foreach (var r in hb.regions) originalBoxes.Add((r.box.center, r.box.size));
                sb.AppendLine($"  Car colliders: {colliders} boxes = core + cabin + {hb.regions.Length} adaptive regions ({hb.FaceCount} adaptive faces, {hb.SampleCount:N0} vertex samples)");
                var undamaged = new StringBuilder();
                foreach (var f in hb.Faces) undamaged.Append($"{f.Label} {f.sign * f.original:0.000}; ");
                sb.AppendLine($"  Undamaged faces (car space): {undamaged}");
                if (screenshots && colliderDebug != null)
                {
                    colliderDebug.SetVisible(true);
                    Shots(Tag() + "60_P4_Hitbox_Undamaged_FrontLeft", 135f);
                    HideSled(true);
                    CollisionLabValidation.Shot(lab, lab.cameraController, Tag() + "61_P4_Hitbox_Undamaged_RearRight", 315f, 16f, 7f, lab.car.position + Vector3.up * 0.6f);
                    HideSled(false);
                }

                // ---------------------------------------------------------------- A. coverage: every region, two identical hits
                sb.AppendLine("  A. COVERAGE (narrow block, 15 m/s, 1500 kg; Repair Car before each target). Depth = where the block first touches the car's physical");
                sb.AppendLine("     hitbox (parked block swept along its launch path), outward along the approach. For reference: the recorded impactor pose and the");
                sb.AppendLine("     averaged PhysX contact point, both reported after a 30 cm physics step, so 0-15 cm past the true first touch depending on step phase.");
                sb.AppendLine("     Acceptance unchanged: >3 cm deeper, <6 cm from expected first physical surface, >5 cm regional retraction; cabin matches measured protection within 1 mm.");
                sb.AppendLine("     Solver contact/recorded poses are diagnostics. A swept footprint overlapping the intact protected cabin must stop there.");
                sb.AppendLine("     target                      region       hit1 touch  face before->after hit1   hit2 touch (deeper by)   pose 1->2       contact 1->2    region retract  result");
                bool coverageOk = true;
                foreach (var t in Targets)
                {
                    lab.RepairCar();
                    var touch1 = SweptFirstTouch(t); float s1 = touch1.outward;
                    var r1 = Hit(t.d, 15f, 1500f, true, Off(t), out _);
                    float i1 = ImpactorFront(t.d);
                    float fBefore = FaceOriginal(hb, t), fAfter = FaceValue(hb, t);
                    string cornerFaces = "";
                    if (t.axis < 0)
                        foreach (var f in hb.Faces) if (f.region.name == t.region) cornerFaces += $"{f.Label} {f.sign * f.original:0.000}->{f.sign * f.current:0.000} ";
                    var touch2 = SweptFirstTouch(t); float s2 = touch2.outward;
                    // Independently measured protection, not the potentially misplaced actual collider's bounds.
                    Bounds protectedCabin = AdaptiveContactValidation.ProtectedCabin(lab.Geometry, lab.CurrentVehicle.cabinBoxScale);
                    var cabin = hb.cabin;
                    bool cabinIntact = AdaptiveContactValidation.CabinMatches(cabin, lab.car.transform, protectedCabin);
                    bool cabinFirst = false;
                    float expected = t.axis < 0 ? s2 : AdaptiveContactValidation.ExpectedFace(fAfter, protectedCabin,
                        touch2.parkedCenter, touch2.half, t.axis, t.sign, out cabinFirst);
                    bool surfaceIdentity = t.axis < 0 || touch2.collider == (cabinFirst ? cabin : hb.Find(t.region).box);
                    var r2 = Hit(t.d, 15f, 1500f, true, Off(t), out _);
                    float i2 = ImpactorFront(t.d);
                    float c1 = Outward(r1, t.d), c2 = Outward(r2, t.d);
                    bool ok = AdaptiveContactValidation.Accept(s1, s2, expected, RegionRetract(hb, t.region),
                        r1.applied && r2.applied, surfaceIdentity, cabinIntact);
                    coverageOk &= ok;
                    sb.AppendLine($"     {t.name,-27} {t.region,-12} {s1,8:0.000}   " +
                                  $"{(t.axis < 0 ? "   (two faces)        " : $"{fBefore,6:0.000} -> {fAfter,6:0.000}      ")}" +
                                  $"{s2,8:0.000} ({(s1 - s2) * 100f,5:0.0} cm)    {i1,6:0.000}->{i2,6:0.000}   {c1,6:0.000}->{c2,6:0.000}   {RegionRetract(hb, t.region) * 100f,6:0.0} cm      {P(ok)}  [{r1.carCollider} -> {r2.carCollider}]");
                    if (t.axis >= 0) sb.AppendLine($"        swept surface {touch2.collider?.name}, expected {(cabinFirst ? "protected cabin" : "adaptive face")} {expected:0.000}; sweep error {(s2 - expected) * 1000f:0.0} mm; cabin geometry intact {cabinIntact}");
                    if (cornerFaces.Length > 0) sb.AppendLine($"        corner faces after hit 1: {cornerFaces}");
                }
                sb.AppendLine($"  Coverage: every region's second identical hit lands deeper, at the retracted face or independently expected protected cabin {P(coverageOk)}");

                // ---------------------------------------------------------------- B. locality + C. saturation
                sb.AppendLine("  B/C. LOCALITY + SATURATION (30 m/s, 4000 kg, narrow unless noted):");
                lab.RepairCar();
                var fc = Targets[0];
                var lastMoves = new List<float>(); var faceTrail = new List<float>();
                bool allApplied = true, diagAll = true;
                for (int i = 0; i < 25; i++)
                {
                    float m = HitMoved(fc, 30f, 4000f, true, out var r);
                    allApplied &= r.applied; diagAll &= DiagOk();
                    lastMoves.Add(m); faceTrail.Add(FaceValue(hb, fc));
                }
                float faceSettle = faceTrail[19] - faceTrail[24];
                sb.AppendLine($"     front centre x25: moved per hit 1/2/3 {lastMoves[0]:0.000}/{lastMoves[1]:0.000}/{lastMoves[2]:0.000} m ... 23/24/25 {lastMoves[22]:0.000}/{lastMoves[23]:0.000}/{lastMoves[24]:0.000} m; " +
                              $"face {FaceOriginal(hb, fc):0.000} -> {faceTrail[0]:0.000} -> {faceTrail[24]:0.000} (moved {faceSettle * 100f:0.0} cm over hits 21-25); all applied {allApplied}, diagnostics {diagAll} " +
                              $"{P(allApplied && diagAll && lastMoves[24] < 0.05f && faceSettle < 0.02f)} (local saturation)");
                if (screenshots) Shots(Tag() + "62_P4_FrontCentre_x25_Hitbox", 140f);
                bool locality1 = true;
                foreach (int k in new[] { 1, 2, 7, 10, 3 })   // front left, front right, left mid, right mid, rear centre
                {
                    float m = HitMoved(Targets[k], 30f, 4000f, true, out var r);
                    bool ok = r.applied && m > 0.1f && RegionRetract(hb, Targets[k].region) > 0.05f && DiagOk();
                    locality1 &= ok;
                    sb.AppendLine($"       then {Targets[k].name,-18}: mesh moved {m:0.000} m, {Targets[k].region} retracted {RegionRetract(hb, Targets[k].region) * 100f:0.0} cm {P(ok)}");
                }
                if (screenshots) Shots(Tag() + "63_P4_FrontCentre_x25_then_Others", 150f);

                lab.RepairCar();
                for (int i = 0; i < 10; i++) Hit(Targets[7].d, 30f, 4000f, false, Off(Targets[7]), out _);   // left side mid, wide
                bool locality2 = true;
                foreach (int k in new[] { 4, 10 })   // rear left, right side mid
                {
                    float m = HitMoved(Targets[k], 30f, 4000f, true, out var r);
                    locality2 &= r.applied && m > 0.1f;
                    sb.AppendLine($"     left side mid x10 (wide), then {Targets[k].name,-16}: mesh moved {m:0.000} m {P(r.applied && m > 0.1f)}");
                }
                lab.RepairCar();
                for (int i = 0; i < 10; i++) Hit(Targets[3].d, 30f, 4000f, true, Off(Targets[3]), out _);   // rear centre
                bool locality3 = true;
                foreach (int k in new[] { 0, 6 })   // front centre, left side front
                {
                    float m = HitMoved(Targets[k], 30f, 4000f, true, out var r);
                    locality3 &= r.applied && m > 0.1f;
                    sb.AppendLine($"     rear centre x10, then {Targets[k].name,-24}: mesh moved {m:0.000} m {P(r.applied && m > 0.1f)}");
                }
                sb.AppendLine($"  Locality: a hammered region does not stop other regions deforming {P(locality1 && locality2 && locality3)}");

                // ---------------------------------------------------------------- D. long run: 84 mixed hits
                lab.RepairCar();
                int clampsBefore = hb.OutwardClamps;
                int applied = 0, diag = 0, n = 0; float maxDrift = 0f;
                var blockDeform = new List<float>[5]; for (int k = 0; k < 5; k++) blockDeform[k] = new List<float>();
                var hbStart = hitboxMs.Count;
                Vector3 carPos = lab.car.position;
                for (int round = 0; round < 6; round++)
                    for (int k = 0; k < 14; k++)   // the original 14 targets, so the long run stays comparable with earlier runs
                    {
                        bool narrow = (round + k) % 2 == 0;
                        float speed = round % 3 == 2 ? 15f : 30f;
                        var r = Hit(Targets[k].d, speed, 4000f, narrow, Off(Targets[k]), out _);
                        if (r.applied) applied++;
                        if (DiagOk()) diag++;
                        blockDeform[Mathf.Min(4, n / 20)].Add(r.deformMilliseconds);
                        n++;
                        maxDrift = Mathf.Max(maxDrift, (lab.car.position - carPos).magnitude);
                    }
                DamageLabValidation.CheckSanity(filters, baseVerts, out int nan, out int flipped, out int tris);
                var hbLong = hitboxMs.GetRange(hbStart, hitboxMs.Count - hbStart);
                sb.AppendLine($"  D. LONG RUN: {n} mixed hits (14 targets x 6 rounds, narrow/wide alternating, 30 and 15 m/s, 4000 kg): applied {applied}/{n}, diagnostics correct {diag}/{n}, " +
                              $"impacts {model.TotalImpactCount} ({model.ImpactCount} stored, {model.MergedImpactCount} merged), max disp {Disp():0.000} m, NaN {nan}, flipped {flipped} of {tris:N0} ({100f * flipped / tris:0.0}%), " +
                              $"car drift {maxDrift * 1000f:0.00} mm, outward face moves blocked {hb.OutwardClamps - clampsBefore} {P(applied == n && diag == n && nan == 0 && model.ImpactCount <= model.maxImpacts)}");
                sb.AppendLine($"     deformation per hit by block of 20: {Avg(blockDeform[0]):0.0} / {Avg(blockDeform[1]):0.0} / {Avg(blockDeform[2]):0.0} / {Avg(blockDeform[3]):0.0} / {Avg(blockDeform[4]):0.0} ms; " +
                              $"hitbox update {Avg(hbLong):0.00} ms avg over {hbLong.Count} updates");
                var faces = new StringBuilder();
                foreach (var r in hb.regions) faces.Append($"{r.name} -{RegionRetract(hb, r.name) * 100f:0}cm ");
                sb.AppendLine($"     region retraction after the long run: {faces}");
                if (screenshots && colliderDebug != null)
                {
                    Shots(Tag() + "64_P4_LongRun_84_FrontLeft", 135f);
                    HideSled(true);
                    CollisionLabValidation.Shot(lab, lab.cameraController, Tag() + "65_P4_LongRun_84_RearRight", 315f, 16f, 7f, lab.car.position + Vector3.up * 0.6f);
                    CollisionLabValidation.Shot(lab, lab.cameraController, Tag() + "66_P4_LongRun_84_Top", 90f, 70f, 8f, lab.car.position + Vector3.up * 0.6f);
                    HideSled(false);
                }

                // ---------------------------------------------------------------- E. reset / repair
                var dmg = CollisionLabValidation.Snapshot(filters);
                lab.ResetBlock();
                float blockErr = CollisionLabValidation.MaxDiff(dmg, CollisionLabValidation.Snapshot(filters));
                lab.RepairCar();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                bool boxesExact = true;
                for (int i = 0; i < hb.regions.Length; i++)
                    boxesExact &= hb.regions[i].box.center == originalBoxes[i].c && hb.regions[i].box.size == originalBoxes[i].s;
                var first = Hit(Targets[0].d, 15f, 1500f, true, 0f, out _);
                int firstCount = model.TotalImpactCount;
                bool firstIsOne = firstCount == 1;
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep2, out float en2);
                bool boxesExact2 = true;
                for (int i = 0; i < hb.regions.Length; i++)
                    boxesExact2 &= hb.regions[i].box.center == originalBoxes[i].c && hb.regions[i].box.size == originalBoxes[i].s;
                sb.AppendLine($"  E. Reset Block after 84 hits: mesh change {blockErr:E1} {P(blockErr == 0f)}; Repair Car: mesh {ep:E1}/{en:E1}, all {hb.regions.Length} region boxes identical to the original {boxesExact}, " +
                              $"next hit is impact #{firstCount} {P(ep == 0f && en == 0f && boxesExact && firstIsOne && first.applied)}; Reset All: mesh {ep2:E1}/{en2:E1}, boxes exact {boxesExact2} {P(ep2 == 0f && en2 == 0f && boxesExact2)}");

                // ---------------------------------------------------------------- costs
                hitboxMs.Sort(); deformMs.Sort();
                sb.AppendLine($"  COSTS (editor, this machine): {colliders} car colliders; hitbox update {Avg(hitboxMs):0.00} ms avg / {(hitboxMs.Count > 0 ? hitboxMs[hitboxMs.Count - 1] : 0f):0.00} ms max over {hitboxMs.Count} updates " +
                              $"(only after impacts; {Avg(refitCounts):0.0} of {hb.FaceCount} faces re-fitted per update on average); visual deformation {Avg(deformMs):0.0} ms avg / {(deformMs.Count > 0 ? deformMs[deformMs.Count - 1] : 0f):0.0} ms max per hit; physics step {physicsMs / Mathf.Max(1, physicsSteps):0.000} ms avg over {physicsSteps} steps");
            }
            finally
            {
                if (colliderDebug != null) colliderDebug.SetVisible(false);
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
