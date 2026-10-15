using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Importer round trip on a real vehicle: import the hatchback's reference manifest (write mode, under a temporary
    /// name), then replay the same recorded physical impacts into the hand-written hatchback and into the manifest-built
    /// one. Identical meshes = the importer reproduces the verified destruction behaviour. Temporary assets are deleted.
    /// </summary>
    public static partial class CollisionPersistenceValidation
    {
        public static string RunManifestRoundtrip()
        {
            var sb = new StringBuilder("[CollisionLab] Vehicle manifest round trip (hatchback reference manifest, write mode)\n");
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            const string tempManifest = "Temp/ManifestRoundtrip.vehicle.json";
            string defPath = VehicleDefinitionSetup.Folder + "/ManifestRoundtrip.asset";
            string profilePath = VehicleDefinitionSetup.Folder + "/ManifestRoundtripDamageProfile.asset";
            var savedVehicles = (VehicleDefinition[])null;
            Open(1);
            try
            {
                savedVehicles = lab.vehicles;
                // Recorded impacts from real launches on the hand-written hatchback.
                var recorded = new List<LocalImpact>();
                int last = 0;
                System.Action rec = () => { if (lab.model.TotalImpactCount > last && lab.model.ImpactCount > 0) recorded.Add(lab.model.Impacts[lab.model.ImpactCount - 1]); last = lab.model.TotalImpactCount; };
                lab.model.ImpactsChanged += rec;
                foreach (var d in new[] { CollisionTest.ImpactDirection.Front, CollisionTest.ImpactDirection.LeftSide, CollisionTest.ImpactDirection.RearCornerAngled, CollisionTest.ImpactDirection.FrontRightCornerAngled, CollisionTest.ImpactDirection.Rear, CollisionTest.ImpactDirection.Front })
                    Hit(d, 20f, 2000f, d == CollisionTest.ImpactDirection.Front, 0f, out _);
                lab.model.ImpactsChanged -= rec;
                lab.ResetAll();
                foreach (var im in recorded) lab.model.AddImpact(im);
                var handWritten = CollisionLabValidation.Snapshot(filters);

                // Import the same manifest for real under a temporary name.
                var json = File.ReadAllText("Docs/VehicleProduction/examples/compact_hatchback.vehicle.json");
                var m = JsonUtility.FromJson<VehicleManifestImporter.Manifest>(json);
                m.displayName = "Manifest Roundtrip"; m.id = "carwreck.manifest_roundtrip";
                Directory.CreateDirectory("Temp");
                File.WriteAllText(tempManifest, JsonUtility.ToJson(m, true));
                sb.Append(VehicleManifestImporter.Import(tempManifest, false, out var def, out var errors));
                def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(defPath);
                bool imported = errors.Count == 0 && def != null;
                if (!imported) { sb.AppendLine("  import FAILED"); return sb.ToString(); }

                // Build it through the normal vehicle path and replay the same impacts.
                lab.vehicles = savedVehicles.Concat(new[] { def }).ToArray();
                lab.SelectVehicle(lab.vehicles.Length - 1);
                Adopt();
                lab.ResetAll();
                foreach (var im in recorded) lab.model.AddImpact(im);
                var fromManifest = CollisionLabValidation.Snapshot(filters);
                bool sameTopology = fromManifest.Count == handWritten.Count && fromManifest.Zip(handWritten, (x, y) => x.Length == y.Length).All(x => x);
                float diff = sameTopology ? CollisionLabValidation.MaxDiff(handWritten, fromManifest) : float.NaN;
                sb.AppendLine($"  {recorded.Count} recorded impacts replayed into the hand-written and the manifest-built hatchback: mesh difference {diff:E1} m, " +
                              $"warnings {lab.VehicleWarnings.Count}, colliders {lab.car.GetComponentsInChildren<Collider>().Length} {P(sameTopology && diff == 0f && lab.VehicleWarnings.Count == 0)}");
            }
            finally
            {
                if (savedVehicles != null && lab.vehicles != savedVehicles) { lab.vehicles = savedVehicles; lab.SelectVehicle(1); Adopt(); }
                AssetDatabase.DeleteAsset(defPath);
                AssetDatabase.DeleteAsset(profilePath);
                if (File.Exists(tempManifest)) File.Delete(tempManifest);
                Close(prev);
            }
            return sb.ToString();
        }
    }
}
