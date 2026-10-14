using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Reproduces the hatchback's remaining mesh problems (components that fold far more than their neighbours under
    /// ordinary hits) and captures a close-up of each, with the folded-triangle count of the part. For the asset author.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        static int FoldedIn(string part)
        {
            int n = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].name != part) continue;
                var v = filters[i].sharedMesh.vertices; var t = filters[i].sharedMesh.triangles; var v0 = baseVerts[i];
                for (int k = 0; k < t.Length; k += 3)
                {
                    Vector3 a = Vector3.Cross(v0[t[k + 1]] - v0[t[k]], v0[t[k + 2]] - v0[t[k]]);
                    Vector3 c = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
                    if (a.sqrMagnitude > 1e-12f && Vector3.Dot(a, c) < 0f) n++;
                }
                return n;
            }
            return -1;
        }

        static int Tris(string part) => filters.Where(f => f.name == part).Select(f => f.sharedMesh.triangles.Length / 3).FirstOrDefault();

        static Vector3 PartCentre(string part)
        {
            var f = filters.FirstOrDefault(x => x.name == part);
            return f != null ? f.GetComponent<Renderer>().bounds.center : lab.car.position;
        }

        public static string RunHatchbackDefects()
        {
            var sb = new StringBuilder("[CollisionLab] Hatchback mesh defects: reproduction and close-ups (Screenshots/80_Sol_*)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Open(1);
            try
            {
                var model = lab.model; var g = lab.Geometry; Bounds bb = g.body, cab = g.cabin;
                void Shot(string name, string part, float yaw, float pitch, float dist, Vector3? pivot = null)
                {
                    HideSled(true);
                    CollisionLabValidation.Shot(lab, lab.cameraController, "80_Sol_" + name, yaw, pitch, dist, pivot ?? PartCentre(part));
                    HideSled(false);
                }
                void Report(string what, params string[] parts) =>
                    sb.AppendLine($"  {what}: " + string.Join(", ", parts.Select(p => $"{p} {FoldedIn(p)}/{Tris(p)} ({100f * FoldedIn(p) / Mathf.Max(1, Tris(p)):0.0}%)")));

                // Physical hits: the same wide sled, 15 m/s, 1500 kg, from a repaired car (as vehicle-hatch).
                lab.RepairCar(); Hit(ImpactDirection.Front, 15f, 1500f, false, 0f, out _);
                Report("front wide 15 m/s 1500 kg", "FrontValance", "GrilleSurround", "Bumper_Front", "Body");
                Shot("FrontValance_FrontHit", "FrontValance", 150f, 8f, 2.2f);
                lab.RepairCar(); Hit(ImpactDirection.Rear, 15f, 1500f, false, 0f, out _);
                Report("rear wide 15 m/s 1500 kg", "LicenseRecess", "BlankPlate", "Bumper_Rear", "Body");
                Shot("LicenseRecess_RearHit", "LicenseRecess", 340f, 8f, 1.8f);
                lab.RepairCar(); Hit(ImpactDirection.LeftSide, 15f, 1500f, false, 0f, out _);
                Report("left side wide 15 m/s 1500 kg", "MirrorGlass", "Mirror", "SideIndicator", "DoorSeam", "Body");
                Shot("MirrorGlass_LeftHit", "MirrorGlass", 110f, 12f, 1.6f);
                Shot("SideIndicator_LeftHit", "SideIndicator", 105f, 6f, 1.4f);

                // Click-style dents (strength 0.8) on an undamaged car: the muscle car folds 0 triangles in the same tests.
                foreach (var c in new (string n, Vector3 p, Vector3 d, float yaw, float pitch)[]
                {
                    ("Body_HoodDent", new Vector3(bb.center.x + 0.25f, cab.max.y + 2f, bb.max.z - 0.13f * bb.size.z), Vector3.down, 150f, 35f),
                    ("Body_DoorDent", new Vector3(bb.min.x - 2f, bb.center.y + 0.1f, bb.center.z - 0.1f), Vector3.right, 100f, 8f),
                    ("Body_RearDent", new Vector3(bb.center.x, cab.max.y + 1.2f, bb.min.z - 1.4f), new Vector3(0, -0.6f, 0.8f).normalized, 330f, 25f),
                })
                {
                    lab.RepairCar();
                    model.SurfacePointAlong(c.p, c.d, out Vector3 sp, 0.12f, 0f);
                    model.AddImpact(new LocalImpact { point = sp, direction = c.d, normal = -c.d, strength = 0.8f });
                    Report($"{c.n} at {sp.ToString("F2")} (vehicle space)", "Body", "LicenseRecess", "GrilleSurround", "DoorSeam");
                    Shot(c.n, "Body", c.yaw, c.pitch, 1.8f, lab.car.transform.TransformPoint(sp));
                }
                lab.RepairCar();
            }
            finally
            {
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
