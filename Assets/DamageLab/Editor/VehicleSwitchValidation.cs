using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Vehicle switching in the Collision Lab: damage one vehicle, switch, and check that nothing carries over (no old car,
    /// colliders, receivers or impacts; pristine mesh; original hitbox; the new vehicle's profile, geometry, aim and
    /// impact height), then that the new car takes hits normally, and the same on the way back. Real PhysX, anchored.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        static List<Vector3[]> SourceVerts(VehicleDamageModel m) => m.GetComponentsInChildren<MeshFilter>().Select(f =>
        {
            var part = m.Parts.FirstOrDefault(p => p.filter == f);
            return part != null ? part.sourceMesh.vertices : f.sharedMesh.vertices;
        }).ToList();

        static void Adopt()
        {
            receiver = lab.receiver;
            receiver.logImpacts = false;
            filters = lab.model.GetComponentsInChildren<MeshFilter>();
            lab.SetAnchored(true);
            lab.aimOffset = 0f;
        }

        /// <summary>One line of checks on the vehicle the lab has just switched to.</summary>
        static string CheckFresh(int index, GameObject oldCar, out bool ok)
        {
            var def = lab.vehicles[index];
            var model = lab.model; var hb = lab.hitbox; var g = lab.Geometry;
            bool oldGone = oldCar == null;
            int models = Object.FindObjectsByType<VehicleDamageModel>(FindObjectsSortMode.None).Length;
            int receivers = Object.FindObjectsByType<CollisionDamageReceiver>(FindObjectsSortMode.None).Length;
            int hitboxes = Object.FindObjectsByType<VehicleHitbox>(FindObjectsSortMode.None).Length;
            // Every car collider in the scene must belong to the current car.
            int strayColliders = Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None)
                .Count(c => c.GetComponentInParent<VehicleHitbox>() != null && c.GetComponentInParent<VehicleHitbox>() != hb);
            bool rightVehicle = model.definition == def && model.profile == def.profile && lab.car.mass == def.mass;
            float meshErr = CollisionLabValidation.MaxDiff(SourceVerts(model), CollisionLabValidation.Snapshot(filters));
            bool facesOriginal = hb.Faces.All(f => f.current == f.original);
            var inst = Object.Instantiate(def.model);
            VehicleGeometry measured;
            try { measured = VehicleRig.Measure(def, inst); } finally { Object.DestroyImmediate(inst); }
            bool geometryOk = (measured.body.size - g.body.size).magnitude < 1e-4f && Mathf.Abs(lab.sledHeight - VehicleRig.ImpactHeight(def, g)) < 1e-5f;
            lab.Aim(ImpactDirection.Front, out Vector3 aimFront, out _);
            bool aimOk = Mathf.Abs(aimFront.z - g.body.max.z) < 1e-5f;
            string info = lab.vehicleInfoText != null ? lab.vehicleInfoText.text : "";
            bool infoOk = info.Contains(def.displayName) && !receiver.HasReport && model.TotalImpactCount == 0;
            ok = oldGone && models == 1 && receivers == 1 && hitboxes == 1 && strayColliders == 0 && rightVehicle && meshErr == 0f && facesOriginal && geometryOk && aimOk && infoOk;
            return $"{def.displayName}: old car destroyed {oldGone}, damage models/receivers/hitboxes in scene {models}/{receivers}/{hitboxes}, stray car colliders {strayColliders}, " +
                   $"definition+profile+mass ({def.mass:0} kg) {rightVehicle}, impacts {model.TotalImpactCount}, mesh vs pristine source {meshErr:E1} m, hitbox faces original {facesOriginal}, " +
                   $"body {g.body.size.ToString("F3")} = measured {geometryOk}, impact height {lab.sledHeight:0.00} m, front aim z {aimFront.z:0.000} on body front {aimOk}, panel shows it / no stale report {infoOk} {P(ok)}";
        }

        public static string RunSwitching()
        {
            var sb = new StringBuilder("[CollisionLab] Vehicle switching (real PhysX, anchored)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(0);
            bool allOk = true;
            try
            {
                var pristine0 = CollisionLabValidation.Snapshot(filters);
                lab.hitbox.Setup();
                // Damage the muscle car from three sides.
                Hit(ImpactDirection.Front, 30f, 4000f, true, 0f, out _);
                Hit(ImpactDirection.LeftSide, 20f, 1500f, false, 0f, out _);
                Hit(ImpactDirection.RearCornerAngled, 20f, 1500f, false, 0f, out _);
                float retract = lab.hitbox.Faces.Sum(f => f.original - f.current);
                sb.AppendLine($"  muscle car damaged: {lab.model.TotalImpactCount} impacts, max disp {Disp():0.000} m, hitbox retracted {retract * 100f:0} cm in total");

                for (int round = 0; round < 2; round++)
                {
                    foreach (int target in new[] { 1, 0 })
                    {
                        var old = lab.car.gameObject;
                        lab.SelectVehicle(target);
                        Adopt();
                        sb.AppendLine($"  switch -> {CheckFresh(target, old, out bool ok)}");
                        allOk &= ok;
                        // The new car takes a hit as impact #1, at its own front.
                        var b0 = CollisionLabValidation.Snapshot(filters);
                        var r = Hit(ImpactDirection.Front, 20f, 1500f, true, 0f, out _);
                        float moved = CollisionLabValidation.MaxDiff(b0, CollisionLabValidation.Snapshot(filters));
                        float front = lab.Geometry.body.max.z;
                        bool hitOk = r.applied && lab.model.TotalImpactCount == 1 && moved > 0.1f && Mathf.Abs(r.footprint.center.z - front) < 0.35f;
                        allOk &= hitOk;
                        sb.AppendLine($"    first hit on it: applied {r.applied}, impact #{lab.model.TotalImpactCount}, moved {moved:0.000} m, footprint z {r.footprint.center.z:0.000} (body front {front:0.000}) {P(hitOk)}");
                        if (target == 0 && round == 0)
                        {
                            // Back on the muscle car: it must be the pristine car again, not the damaged one we left.
                            lab.RepairCar();
                            float err = CollisionLabValidation.MaxDiff(pristine0, CollisionLabValidation.Snapshot(filters));
                            sb.AppendLine($"    muscle car after switching back and repairing that hit: identical to the original undamaged car {err:E1} m {P(err == 0f)}");
                            allOk &= err == 0f;
                        }
                    }
                }
                sb.AppendLine($"  Vehicle switching carries no damage, colliders or reports between vehicles {P(allOk)}");
            }
            finally
            {
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
