using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>Checkpoint B: damage-aware front hitbox.</summary>
    public static partial class CollisionPersistenceValidation
    {
        // Tracking for stability and performance (filled by SimulateTracked).
        static readonly System.Diagnostics.Stopwatch physicsWatch = new System.Diagnostics.Stopwatch();
        static double physicsMs; static int physicsSteps;
        static float maxSledSpeedAfterUpdate, sledSpeedBeforeUpdate, maxCarSpeedAfterUpdate;
        static readonly List<float> hitboxMs = new List<float>();
        static readonly List<float> deformMs = new List<float>();
        static readonly List<float> refitCounts = new List<float>();

        static void SimulateTracked(int steps = 200)
        {
            int updates = lab.hitbox.UpdateCount;
            int afterSteps = -1;
            for (int i = 0; i < steps; i++)
            {
                physicsWatch.Restart();
                Physics.Simulate(Time.fixedDeltaTime);
                physicsWatch.Stop();
                physicsMs += physicsWatch.Elapsed.TotalMilliseconds; physicsSteps++;
                int ev = receiver.EventCount;
                receiver.Step(Time.fixedDeltaTime);
                if (receiver.EventCount != ev && receiver.Last.applied) deformMs.Add(receiver.Last.deformMilliseconds);
                float before = (lab.sled.linearVelocity - lab.car.linearVelocity).magnitude;
                lab.AfterPhysicsStep();
                if (lab.hitbox.UpdateCount != updates)
                {
                    updates = lab.hitbox.UpdateCount;
                    hitboxMs.Add(lab.hitbox.LastUpdateMilliseconds);
                    refitCounts.Add(lab.hitbox.LastRefitFaces);
                    sledSpeedBeforeUpdate = Mathf.Max(sledSpeedBeforeUpdate, before);
                    afterSteps = 0;
                }
                if (afterSteps >= 0 && afterSteps < 60)
                {
                    afterSteps++;
                    maxSledSpeedAfterUpdate = Mathf.Max(maxSledSpeedAfterUpdate, (lab.sled.linearVelocity - lab.car.linearVelocity).magnitude);
                    maxCarSpeedAfterUpdate = Mathf.Max(maxCarSpeedAfterUpdate, lab.car.linearVelocity.magnitude + lab.car.angularVelocity.magnitude);
                }
            }
        }

        static CollisionDamageReceiver.Report Hit(ImpactDirection d, float speed, float mass, bool narrow, float offset, out Vector3 sledStart)
        {
            lab.speed = speed; lab.mass = mass; lab.narrowImpactor = narrow; lab.aimOffset = offset;
            lab.SetDirection(d);
            // The launch's impact is its FIRST collision event. After the hitbox retracts, a block still creeping at a few
            // cm/s may touch the new face again later; those touches are below the damage threshold and not the impact.
            CollisionDamageReceiver.Report first = default;
            bool got = false;
            void OnImpact(CollisionDamageReceiver.Report r) { if (!got) { first = r; got = true; } }
            receiver.ImpactRegistered += OnImpact;
            int ev0 = receiver.EventCount;
            lab.Launch();
            sledStart = lab.sled.position;
            SimulateTracked();
            lab.hitbox.ApplyPending();
            receiver.ImpactRegistered -= OnImpact;
            lateTouches += Mathf.Max(0, receiver.EventCount - ev0 - 1);
            return first;
        }

        static int lateTouches;

        static string Fronts() =>
            $"L {lab.hitbox.CurrentFront[0]:0.000}  C {lab.hitbox.CurrentFront[1]:0.000}  R {lab.hitbox.CurrentFront[2]:0.000}";

        static float Retract(int s) => lab.hitbox.OriginalFront[s] - lab.hitbox.CurrentFront[s];

        static void Shots(string name, float yaw = 140f)
        {
            HideSled(true);
            lab.RefreshDiagnostics();
            if (lab.collidersToggle != null) lab.collidersToggle.SetIsOnWithoutNotify(true);
            Vector3 pivot = lab.car.transform.TransformPoint(new Vector3(0f, 0.6f, 1.6f));
            CollisionLabValidation.Shot(lab, lab.cameraController, name, yaw, 16f, 5.5f, pivot);
            HideSled(false);
        }

        /// <summary>Max Z (car space) over every car collider's box: where an object coming from the front first meets the car.</summary>
        static float ColliderFrontZ()
        {
            float m = float.MinValue;
            foreach (var bc in lab.car.GetComponentsInChildren<BoxCollider>())
                if (bc.enabled && !bc.isTrigger) m = Mathf.Max(m, bc.center.z + bc.size.z * 0.5f);
            return m;
        }

        static float Avg(List<float> l)
        {
            float s = 0f;
            foreach (var x in l) s += x;
            return l.Count > 0 ? s / l.Count : 0f;
        }

        public static string RunB(bool screenshots = true)
        {
            var sb = new StringBuilder("[CollisionLab] Phase 3 checkpoint B: damage-aware front hitbox (real PhysX, Physics.Simulate 50 Hz, anchored car)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open();
            physicsMs = 0; physicsSteps = 0; hitboxMs.Clear(); deformMs.Clear();
            maxSledSpeedAfterUpdate = sledSpeedBeforeUpdate = maxCarSpeedAfterUpdate = 0f;
            lateTouches = 0;
            var colliderDebug = Object.FindAnyObjectByType<PhysicsHitboxDebug>();
            try
            {
                var hb = lab.hitbox;
                hb.Setup();
                if (colliderDebug != null) colliderDebug.SetVisible(true);
                int colliders = 0;
                foreach (var c in lab.car.GetComponentsInChildren<Collider>()) if (c.enabled) colliders++;
                sb.AppendLine($"  Car colliders: {colliders} boxes (core, cabin, {hb.regions.Length} adaptive regions with {hb.FaceCount} faces); core front face z {hb.CoreFrontZ:0.000}; " +
                              $"{hb.SampleCount:N0} mesh vertices sampled in {hb.columns}x{hb.rows} cells per section");

                // ---------------------------------------------------------------- undamaged contour
                var orig = new float[3];
                for (int s = 0; s < 3; s++) orig[s] = hb.CurrentFront[s];
                var contour = new StringBuilder();
                for (int s = 0; s < 3; s++)
                {
                    var b = hb.Find(new[] { "FrontLeft", "FrontCentre", "FrontRight" }[s]).Outer;
                    float maxZ = float.MinValue;
                    foreach (var p in lab.model.Parts)
                        foreach (var v in p.VehiclePositions)
                            if (v.x >= b.min.x && v.x < b.max.x && v.y >= b.min.y && v.y <= b.max.y) maxZ = Mathf.Max(maxZ, v.z);
                    contour.Append($"{(s == 0 ? "left" : s == 1 ? "centre" : "right")} face {orig[s]:0.000} vs bodywork tip {maxZ:0.000} ({(maxZ - orig[s]) * 100f:0.0} cm proud); ");
                }
                sb.AppendLine($"  Undamaged front: {contour}");
                float maxFace = Mathf.Max(orig[0], Mathf.Max(orig[1], orig[2]));
                sb.AppendLine($"  Front-most collider face {ColliderFrontZ():0.000} = the front sections (core ends at {hb.CoreFrontZ:0.000}) {P(Mathf.Abs(ColliderFrontZ() - maxFace) < 1e-4f)}");
                if (screenshots) Shots("52_Hitbox_Undamaged");

                // ---------------------------------------------------------------- 1. three identical narrow hits, centre
                sb.AppendLine("  TEST 1: narrow block, front centre, 12 m/s, 1500 kg, same start pose, no repair:");
                Vector3 start0 = Vector3.zero;
                float firstContact = 0f, prevContact = 0f, prevDisp = 0f;
                bool deeper = true, sameStart = true;
                for (int k = 0; k < 3; k++)
                {
                    var r = Hit(ImpactDirection.Front, 12f, 1500f, true, 0f, out Vector3 start);
                    if (k == 0) { start0 = start; firstContact = r.contactPoint.z; }
                    sameStart &= (start - start0).magnitude < 1e-4f;
                    float disp = Disp();
                    sb.AppendLine($"    hit {k + 1}: block start {start.ToString("F4")}  physical contact z {r.contactPoint.z:0.000} ({(firstContact - r.contactPoint.z) * 100f:0.0} cm deeper than hit 1, block had penetrated {r.penetration * 100f:0.0} cm when reported)  " +
                                  $"strength {r.strength * 100f:0.0}  crush {r.footprint.travel:0.000} m  total max disp {disp:0.000} m  -> faces {Fronts()}");
                    if (k > 0) deeper &= r.contactPoint.z < prevContact - 0.02f;
                    prevContact = r.contactPoint.z; prevDisp = disp;
                    if (screenshots && (k == 0 || k == 2)) Shots(k == 0 ? "53_Hitbox_After_1_Narrow_Hit" : "54_Hitbox_After_3_Narrow_Hits");
                }
                sb.AppendLine($"    same start pose every launch {P(sameStart)}; each contact deeper into the dent than the last {P(deeper)}; retracted centre {Retract(1) * 100f:0.0} cm, " +
                              $"left {Retract(0) * 100f:0.0} cm, right {Retract(2) * 100f:0.0} cm {P(Retract(1) > 0.05f && Retract(0) < 0.5f * Retract(1) && Retract(2) < 0.5f * Retract(1))}");
                float c3 = hb.CurrentFront[1];
                var r4 = Hit(ImpactDirection.Front, 12f, 1500f, true, 0f, out _);
                sb.AppendLine($"    hit 4 (saturation): contact z {r4.contactPoint.z:0.000}, centre face {c3:0.000} -> {hb.CurrentFront[1]:0.000} (moved {(c3 - hb.CurrentFront[1]) * 100f:0.0} cm), " +
                              $"total max disp {prevDisp:0.000} -> {Disp():0.000} m");

                // ---------------------------------------------------------------- repair restores the physics shape exactly, no drift
                lab.RepairCar();
                bool exact = true;
                for (int s = 0; s < 3; s++) exact &= hb.CurrentFront[s] == orig[s];
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    Hit(ImpactDirection.Front, 15f, 2000f, cycle == 1, (cycle - 1) * 0.5f, out _);
                    lab.RepairCar();
                    for (int s = 0; s < 3; s++) exact &= hb.CurrentFront[s] == orig[s];
                }
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                sb.AppendLine($"  Repair Car after damage, then 3 more hit/repair cycles: section faces identical to the undamaged fit {P(exact)}, mesh error {ep:E1}/{en:E1} {P(ep == 0f && en == 0f)}");

                // ---------------------------------------------------------------- 2. front-left vs front-right
                sb.AppendLine("  TEST 2: narrow block, front-left (offset -0.65 m) twice at 15 m/s, then front-right (+0.65 m):");
                var l1 = Hit(ImpactDirection.Front, 15f, 1500f, true, -0.65f, out _);
                var l2 = Hit(ImpactDirection.Front, 15f, 1500f, true, -0.65f, out _);
                sb.AppendLine($"    left hits: contact z {l1.contactPoint.z:0.000} -> {l2.contactPoint.z:0.000} (penetration {l1.penetration * 100f:0.0} / {l2.penetration * 100f:0.0} cm); faces now {Fronts()} " +
                              $"(left -{Retract(0) * 100f:0.0} cm, centre -{Retract(1) * 100f:0.0} cm, right -{Retract(2) * 100f:0.0} cm)");
                if (screenshots) Shots("55_Hitbox_FrontLeft_Damaged", 150f);
                float rightBefore = lab.hitbox.CurrentFront[2], leftRetract = Retract(0);
                var rr = Hit(ImpactDirection.Front, 15f, 1500f, true, 0.65f, out _);
                sb.AppendLine($"    right hit: contact z {rr.contactPoint.z:0.000} (penetration {rr.penetration * 100f:0.0} cm) vs undamaged right face {orig[2]:0.000} ({(orig[2] - rr.contactPoint.z) * 100f:0.0} cm) and left hit 1 {l1.contactPoint.z:0.000} " +
                              $"(averaged contact point, informational); right face was still undamaged ({rightBefore:0.000}) while left was retracted {leftRetract * 100f:0.0} cm, and the right hit then retracted it to {lab.hitbox.CurrentFront[2]:0.000} " +
                              $"{P(rightBefore == orig[2] && leftRetract > 0.08f && lab.hitbox.CurrentFront[2] < orig[2] - 0.05f && rr.applied)}; faces after {Fronts()}");
                if (screenshots) Shots("56_Hitbox_FrontLeft_and_Right", 150f);

                // ---------------------------------------------------------------- 3. wide frontal
                lab.RepairCar();
                var w = Hit(ImpactDirection.Front, 15f, 1500f, false, 0f, out _);
                sb.AppendLine($"  TEST 3: wide sled front 15 m/s: contact z {w.contactPoint.z:0.000}, footprint {w.footprint.halfU * 2f:0.00} m wide; retracted left {Retract(0) * 100f:0.0} / " +
                              $"centre {Retract(1) * 100f:0.0} / right {Retract(2) * 100f:0.0} cm {P(Retract(0) > 0.05f && Retract(1) > 0.05f && Retract(2) > 0.05f)}");
                var w2 = Hit(ImpactDirection.Front, 15f, 1500f, false, 0f, out _);
                sb.AppendLine($"    second wide hit: contact z {w.contactPoint.z:0.000} -> {w2.contactPoint.z:0.000} ({(w.contactPoint.z - w2.contactPoint.z) * 100f:0.0} cm deeper); faces {Fronts()}");
                if (screenshots) Shots("57_Hitbox_Wide_2_Hits", 140f);

                // ---------------------------------------------------------------- stability
                sb.AppendLine($"  Stability (all hits above): relative block speed when a hitbox update was applied <= {sledSpeedBeforeUpdate:0.000} m/s; " +
                              $"max block speed in the 60 steps after an update {maxSledSpeedAfterUpdate:0.000} m/s; max car speed {maxCarSpeedAfterUpdate:0.000}; later low-speed re-touches of the retracted face {lateTouches} (no damage) " +
                              $"{P(maxSledSpeedAfterUpdate < 0.5f && maxCarSpeedAfterUpdate < 0.01f)}");
                // Block resting in the dent, then Repair Car grows the hitbox back around it.
                lab.RepairCar();
                Hit(ImpactDirection.Front, 15f, 1500f, true, 0f, out _);
                Hit(ImpactDirection.Front, 15f, 1500f, true, 0f, out _);
                Vector3 restPos = lab.sled.position;
                lab.RepairCar();
                bool reparked = (lab.sled.position - restPos).sqrMagnitude > 1e-6f;
                float maxV = 0f;
                for (int i = 0; i < 60; i++)
                {
                    Physics.Simulate(Time.fixedDeltaTime);
                    receiver.Step(Time.fixedDeltaTime);
                    lab.AfterPhysicsStep();
                    maxV = Mathf.Max(maxV, lab.sled.linearVelocity.magnitude);
                }
                sb.AppendLine($"  Repair Car with the block resting in the dent: block re-parked {reparked}, max block speed afterwards {maxV:0.000} m/s, impacts {lab.model.ImpactCount} " +
                              $"{P(maxV < 0.01f && lab.model.ImpactCount == 0)}");
                // Free physics with the split hitbox.
                lab.SetAnchored(false);
                lab.ResetAll();
                Vector3 carStart = lab.car.position;
                var fr = Hit(ImpactDirection.Front, 15f, 1500f, false, 0f, out _);
                sb.AppendLine($"  Free physics, wide front 15 m/s: strength {fr.strength * 100f:0.0}, car pushed {(lab.car.position - carStart).magnitude:0.00} m, " +
                              $"spin {lab.car.angularVelocity.magnitude:0.00} rad/s, faces {Fronts()} {P(fr.strength > 0f && lab.car.angularVelocity.magnitude < 2f)}");
                lab.SetAnchored(true);

                // ---------------------------------------------------------------- performance
                hitboxMs.Sort(); deformMs.Sort();
                sb.AppendLine($"  Performance (editor, this machine): {colliders} colliders on the car; hitbox update {Avg(hitboxMs):0.00} ms avg / " +
                              $"{(hitboxMs.Count > 0 ? hitboxMs[hitboxMs.Count - 1] : 0f):0.00} ms max over {hitboxMs.Count} updates; visual deformation per impact {Avg(deformMs):0.0} ms avg / " +
                              $"{(deformMs.Count > 0 ? deformMs[deformMs.Count - 1] : 0f):0.0} ms max; physics step {physicsMs / Mathf.Max(1, physicsSteps):0.000} ms avg over {physicsSteps} steps");
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
