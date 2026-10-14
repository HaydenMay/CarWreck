using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Collision Lab phase 3 validation with real PhysX (Physics.Simulate in edit mode).
    /// Checkpoint A: anchored car, damage persisting across launches, Reset Block / Repair Car / Reset All.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        static CollisionLabController lab;
        static CollisionDamageReceiver receiver;
        static MeshFilter[] filters;
        static List<Vector3[]> baseVerts;
        static List<Vector3[]> baseNormals;

        static void Open()
        {
            if (EditorSceneManager.GetActiveScene().path != CollisionLabSceneBuilder.ScenePath)
                EditorSceneManager.OpenScene(CollisionLabSceneBuilder.ScenePath, OpenSceneMode.Single);
            lab = Object.FindAnyObjectByType<CollisionLabController>();
            receiver = lab.receiver;
            lab.model.Initialize();
            lab.Setup();
            receiver.logImpacts = false;
            filters = lab.model.GetComponentsInChildren<MeshFilter>();
            lab.SetAnchored(true);
            lab.aimOffset = 0f;
            lab.ResetAll();
            baseVerts = CollisionLabValidation.Snapshot(filters);
            baseNormals = new List<Vector3[]>();
            foreach (var f in filters) baseNormals.Add(f.sharedMesh.normals);
        }

        static void Close(SimulationMode prev)
        {
            lab.aimOffset = 0f;
            lab.narrowImpactor = false;
            lab.direction = ImpactDirection.Front;
            lab.SetAnchored(true);
            lab.ResetAll();
            receiver.logImpacts = true;
            Physics.simulationMode = prev;
            lab.model.Shutdown();
            var cam = lab.cameraController;
            cam.pivot = new Vector3(0f, 0.6f, 1.5f);
            cam.SetView(cam.defaultYaw, cam.defaultPitch, cam.defaultDistance);
        }

        /// <summary>Simulate 4 s at 50 Hz, the way the running lab does: physics step, receiver, then the hitbox safe point.</summary>
        static void Simulate(int steps = 200)
        {
            for (int i = 0; i < steps; i++)
            {
                Physics.Simulate(Time.fixedDeltaTime);
                receiver.Step(Time.fixedDeltaTime);
                lab.AfterPhysicsStep();
            }
        }

        static CollisionDamageReceiver.Report Fire(ImpactDirection d, float speed, float mass, bool narrow, float offset = 0f)
        {
            lab.speed = speed; lab.mass = mass; lab.narrowImpactor = narrow; lab.aimOffset = offset;
            lab.SetDirection(d);
            lab.Launch();
            Simulate();
            return receiver.Last;
        }

        static float Disp() => CollisionLabValidation.MaxDiff(baseVerts, CollisionLabValidation.Snapshot(filters));

        static string P(bool ok) => ok ? "PASS" : "FAIL";

        public static string RunA(bool screenshots = true)
        {
            var sb = new StringBuilder("[CollisionLab] Phase 3 checkpoint A: anchored car + persistent damage (real PhysX, Physics.Simulate 50 Hz)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open();
            try
            {
                var car = lab.car;
                Vector3 p0 = car.position; Quaternion r0 = car.rotation;
                float PoseErr() => (car.position - p0).magnitude + Quaternion.Angle(r0, car.rotation) * Mathf.Deg2Rad;

                // 1-5: the sequence from the brief, never repairing in between.
                var seq = new (string name, ImpactDirection d, bool narrow)[]
                {
                    ("front", ImpactDirection.Front, false),
                    ("front again", ImpactDirection.Front, false),
                    ("left side", ImpactDirection.LeftSide, false),
                    ("rear", ImpactDirection.Rear, true),
                    ("front-left corner", ImpactDirection.FrontCornerAngled, false),
                };
                float last = 0f; bool allAdded = true, allStill = true, allOne = true;
                List<Vector3[]> before = CollisionLabValidation.Snapshot(filters);
                for (int i = 0; i < seq.Length; i++)
                {
                    // Changing direction / impactor must not repair anything.
                    lab.narrowImpactor = seq[i].narrow;
                    lab.SetDirection(seq[i].d);
                    float dirChange = CollisionLabValidation.MaxDiff(before, CollisionLabValidation.Snapshot(filters));
                    int ev0 = receiver.EventCount, im0 = lab.model.ImpactCount;
                    var r = Fire(seq[i].d, 12f, 1500f, seq[i].narrow);
                    int events = lab.model.ImpactCount - im0;   // damaging collisions (harmless re-touches of a retracted face do not count)
                    var now = CollisionLabValidation.Snapshot(filters);
                    float added = CollisionLabValidation.MaxDiff(before, now);
                    float disp = Disp();
                    bool still = PoseErr() < 1e-3f;   // frozen axes: only a sub-millimetre solver residual
                    allStill &= still; allAdded &= added > 0.01f && dirChange == 0f; allOne &= events == 1;
                    sb.AppendLine($"  {i + 1}. {seq[i].name,-18} {(seq[i].narrow ? "narrow" : "wide  ")}  damaging events {events} (all contacts {receiver.EventCount - ev0})  strength {r.strength * 100f,5:0.0}  " +
                                  $"impacts stored {lab.model.ImpactCount}  this hit moved mesh {added:0.000} m  total max disp {disp:0.000} m  " +
                                  $"settings change before launch moved mesh {dirChange:0.000} m  car pose drift {PoseErr() * 1000f:0.00} mm  {P(still && events == 1 && added > 0.01f && dirChange == 0f)}");
                    before = now; last = disp;
                }
                sb.AppendLine($"  Anchored car never moved (drift < 1 mm) {P(allStill)}; every launch = 1 damaging collision {P(allOne)}; every launch added damage, settings changes repaired nothing {P(allAdded)}; " +
                              $"{lab.model.ImpactCount} impacts accumulated");
                if (screenshots)
                {
                    HideSled(true);
                    CollisionLabValidation.Shot(lab, lab.cameraController, "50_Persistent_5_Hits_FrontLeft", 130f, 14f, 7.5f, p0 + Vector3.up * 0.6f);
                    CollisionLabValidation.Shot(lab, lab.cameraController, "51_Persistent_5_Hits_RearRight", 300f, 14f, 7.5f, p0 + Vector3.up * 0.6f);
                    HideSled(false);
                }

                // Reset Block: block home, damage untouched.
                var dmg = CollisionLabValidation.Snapshot(filters);
                int impacts = lab.model.ImpactCount;
                lab.ResetBlock();
                Vector3 sledA = lab.sled.position;
                lab.ResetBlock();
                bool blockHome = (lab.sled.position - sledA).sqrMagnitude < 1e-10f && lab.sled.isKinematic && lab.sled.linearVelocity == Vector3.zero;
                float afterBlock = CollisionLabValidation.MaxDiff(dmg, CollisionLabValidation.Snapshot(filters));
                sb.AppendLine($"  Reset Block: mesh change {afterBlock:E1} m, impacts {impacts} -> {lab.model.ImpactCount}, block parked at rest at the same pose {P(afterBlock == 0f && impacts == lab.model.ImpactCount && blockHome)}");

                // Repair Car: original mesh, settings kept.
                var dirBefore = lab.direction; float speedBefore = lab.speed; bool narrowBefore = lab.narrowImpactor;
                lab.RepairCar();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out float ep, out float en);
                bool kept = lab.direction == dirBefore && lab.speed == speedBefore && lab.narrowImpactor == narrowBefore && lab.anchored;
                sb.AppendLine($"  Repair Car: vertex error {ep:E1} m, normal error {en:E1}, impacts {lab.model.ImpactCount}, settings kept {kept} {P(ep == 0f && en == 0f && lab.model.ImpactCount == 0 && kept)}");

                // Reset All after a free-mode hit that moved the car.
                lab.SetAnchored(false);
                var fr = Fire(ImpactDirection.Front, 12f, 1500f, false);
                float moved = (car.position - p0).magnitude;
                float freeDisp = Disp();
                // Free mode, second hit: Reset Block parks the block relative to where the car now is.
                lab.ResetBlock();
                var fr2 = Fire(ImpactDirection.Front, 12f, 1500f, false);
                int freeImpacts = lab.model.ImpactCount;
                sb.AppendLine($"  Free physics: hit 1 strength {fr.strength * 100f:0.0}, car pushed {moved:0.00} m, max disp {freeDisp:0.000} m; " +
                              $"Reset Block + hit 2 (block parked relative to the moved car) strength {fr2.strength * 100f:0.0}, impacts {freeImpacts}, car now {(car.position - p0).magnitude:0.00} m from start " +
                              $"{P(moved > 0.5f && freeDisp > 0.01f && freeImpacts == 2)}");
                lab.ResetAll();
                CollisionLabValidation.Compare(filters, baseVerts, baseNormals, out ep, out en);
                sb.AppendLine($"  Reset All: vertex error {ep:E1} m, normal error {en:E1}, car pose error {PoseErr():E1}, car velocity {car.linearVelocity.magnitude + car.angularVelocity.magnitude:E1}, impacts {lab.model.ImpactCount} " +
                              $"{P(ep == 0f && en == 0f && PoseErr() < 1e-5f && lab.model.ImpactCount == 0)}");
                lab.SetAnchored(true);

                // Anchored strength vs free strength for the same launch (documented, not calibrated).
                lab.ResetAll();
                var an = Fire(ImpactDirection.Front, 12f, 1500f, false);
                sb.AppendLine($"  Same 12 m/s 1500 kg front launch: anchored strength {an.strength * 100f:0.0} (μ {an.reducedMass:0} kg) vs free {fr.strength * 100f:0.0} (μ {fr.reducedMass:0} kg)");
            }
            finally { Close(prev); }
            return sb.ToString();
        }

        static void HideSled(bool hide)
        {
            var r = lab.sled.GetComponent<Renderer>();
            if (r != null) r.enabled = !hide;
        }
    }
}

