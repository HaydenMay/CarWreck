using System.Text;
using DamageLab.CollisionTest;
using UnityEngine;

namespace DamageLab.EditorTools
{
    public static partial class CollisionPersistenceValidation
    {
        /// <summary>Diagnostic: repeated angled rear-corner hits on one vehicle, with the struck collider and the nearby faces.</summary>
        public static string RunCornerProbe(int vehicle)
        {
            var sb = new StringBuilder();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(vehicle);
            try
            {
                var hb = lab.hitbox;
                string Faces() => $"RearRight -Z {-hb.FaceOf("RearRight", 2, -1).current:0.000} +X {hb.FaceOf("RearRight", 0, 1).current:0.000} | RightRear +X {hb.FaceOf("RightRear", 0, 1).current:0.000} | RearCentre -Z {-hb.FaceOf("RearCentre", 2, -1).current:0.000}";
                foreach (bool narrow in new[] { true, false })
                {
                    lab.RepairCar();
                    lab.Aim(ImpactDirection.RearCornerAngled, out Vector3 target, out Vector3 dir);
                    sb.AppendLine($"[CornerProbe] {lab.CurrentVehicle.displayName} rear-right angled, {(narrow ? "narrow" : "wide")}: aim {target.ToString("F3")} dir {dir.ToString("F3")}; faces {Faces()}");
                    for (int k = 0; k < 3; k++)
                    {
                        var r = Hit(ImpactDirection.RearCornerAngled, 15f, 1500f, narrow, 0f, out Vector3 start);
                        sb.AppendLine($"  hit {k + 1}: start {start.ToString("F2")} contact {r.contactPoint.ToString("F3")} collider {r.carCollider} pen {r.penetration * 100f:0.0} cm strength {r.strength * 100f:0.0}; faces now {Faces()}; summary {hb.LastChangeSummary}");
                    }
                }
            }
            finally { Close(prev); }
            return sb.ToString();
        }
    }
}
