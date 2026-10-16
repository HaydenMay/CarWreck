using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Diagnostic (not part of the game): is the FINAL damage of a progressive run the same as an instant run of the same arena scenario?
    /// Each scenario runs twice with real PhysX from fresh cars: progressive crushing OFF (the arena before it) and ON (one rendered frame per
    /// physics step advances the crush). Compares the impact records (count, strength, point) and the final meshes of both cars.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Driving.EditorTools.ProgressiveEquivalenceProbe.Run -quit -logFile probe.log
    /// </summary>
    public static class ProgressiveEquivalenceProbe
    {
        const float Dt = 0.02f;
        static DrivingGame game;

        struct Result { public List<LocalImpact> pickupImpacts, muscleImpacts; public Vector3[][] pickup, muscle; public string text; }

        [MenuItem("CarWreck/Diagnostics/Progressive Equivalence Probe", priority = 41)]
        public static void Run()
        {
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            Physics.simulationMode = SimulationMode.Script;
            VehicleDamageModel.ProgressiveInEditMode = true;
            var sb = new StringBuilder("[ProgressiveEquivalenceProbe] progressive ON vs OFF, same arena scenario, final state after the crush ends; metres\n");
            try
            {
                game.Setup();
                foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);
                game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                foreach (var (name, kmh, off) in new[] { ("moderate 80", 80f, 0f), ("severe 170", 170f, 0f), ("offset 100 / 0.9 m", 100f, 0.9f) })
                {
                    var a = Scenario(false, kmh, off);           // instant, refit on (the arena before progressive crushing)
                    var a2 = Scenario(false, kmh, off);          // the same again: PhysX run-to-run baseline
                    var b = Scenario(true, kmh, off);            // progressive, refit on (waits for the crush)
                    var c = Scenario(false, kmh, off, false);    // instant, no refit
                    var d = Scenario(true, kmh, off, false);     // progressive, no refit: isolates the display change from collider timing
                    string Row(string label, Result x, Result y) => $"      {label}: pickup {Diff(x.pickup, y.pickup):0.0e0} m, muscle {Diff(x.muscle, y.muscle):0.0e0} m; records pickup {Same(x.pickupImpacts, y.pickupImpacts)}, muscle {Same(x.muscleImpacts, y.muscleImpacts)}";
                    sb.AppendLine($"{name}: instant {a.text}");
                    sb.AppendLine($"{new string(' ', name.Length)}  progressive {b.text}");
                    sb.AppendLine($"{new string(' ', name.Length)}  instant (2nd run) {a2.text}");
                    sb.AppendLine(Row("instant vs instant again (run-to-run baseline)", a, a2));
                    sb.AppendLine(Row("progressive vs instant, refit on", a, b));
                    sb.AppendLine(Row("progressive vs instant, refit OFF in both", c, d));
                }

            }
            finally
            {
                VehicleDamageModel.ProgressiveInEditMode = false;
                Physics.simulationMode = SimulationMode.FixedUpdate;
            }
            Debug.Log(sb.ToString());
        }

        static Result Scenario(bool progressive, float kmh, float offsetX, bool refit = true)
        {
            game.progressiveCrush = progressive;
            game.refitColliders = refit;
            // fresh cars pick up the setting
            game.SelectPlayer(1); game.SelectPlayer(0); game.SelectTarget(0); game.SelectTarget(2);
            game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
            game.ResetAll();
            float v = kmh / 3.6f * 0.5f;
            Place(game.Target.Body, new Vector3(0f, 0f, 4.5f), 180f, Vector3.back * v);
            Place(game.Player.Body, new Vector3(offsetX, 0f, -4.5f), 0f, Vector3.forward * v);
            Physics.SyncTransforms();
            game.Player.Receiver.ClearCooldowns(); game.Target.Receiver.ClearCooldowns(); game.Player.controller.ResetState();
            for (int i = 0; i < 100; i++)
            {
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt);
                game.Player.Model.AdvanceCrush(Dt); game.Target.Model.AdvanceCrush(Dt);
                game.AfterPhysicsStep(Dt);
            }
            game.Player.Model.CompleteCrush(); game.Target.Model.CompleteCrush();
            var T = game.Target.Model; var P = game.Player.Model;
            var r = new Result
            {
                pickupImpacts = T.Impacts.ToList(), muscleImpacts = P.Impacts.ToList(),
                pickup = T.Parts.Select(p => p.RuntimeMesh.vertices).ToArray(), muscle = P.Parts.Select(p => p.RuntimeMesh.vertices).ToArray(),
            };
            string One(VehicleDamageModel m) => m.ImpactCount == 0 ? "no hit" : string.Join(" + ", m.Impacts.Select(i => $"{i.strength * 100f:0.0}%"));
            r.text = $"pickup impacts [{One(T)}], muscle impacts [{One(P)}]";
            var rp = ReplayDiff(game.Target, 0); var rm = ReplayDiff(game.Player, 1);
            r.text += $"; vs instant replay of its own records: pickup {rp.pos:0.0e0} m (normals {rp.nrm:0.0e0}), muscle {rm.pos:0.0e0} m (normals {rm.nrm:0.0e0})";
            return r;
        }

        /// <summary>Final displayed mesh of the car vs an instant replay of its impact records on a fresh standalone car (what ProgressiveCrushValidation compares).</summary>
        static (float pos, float nrm) ReplayDiff(DrivingGame.Slot s, int index)
        {
            var rig = VehicleRig.Build(s.def, null, new Vector3(400f + 12f * index, -200f, 400f), game.playerRig);
            rig.body.isKinematic = true;
            rig.model.impactCapacityScale = game.crushCapacityScale;
            rig.model.progressiveCrush = new ProgressiveCrushSettings { enabled = false };
            rig.model.Initialize();
            rig.receiver.logImpacts = false;
            foreach (var im in s.Model.Impacts.ToList()) rig.model.AddImpact(im);
            float dp = 0f, dn = 0f;
            for (int p = 0; p < s.Model.Parts.Count; p++)
            {
                var a = s.Model.Parts[p].RuntimeMesh.vertices; var b = rig.model.Parts[p].RuntimeMesh.vertices;
                var na = s.Model.Parts[p].RuntimeMesh.normals; var nb = rig.model.Parts[p].RuntimeMesh.normals;
                for (int i = 0; i < a.Length; i++) { dp = Mathf.Max(dp, (a[i] - b[i]).magnitude); dn = Mathf.Max(dn, (na[i] - nb[i]).magnitude); }
            }
            rig.model.Shutdown(); UnityEngine.Object.DestroyImmediate(rig.root);
            return (dp, dn);
        }

        static float Diff(Vector3[][] a, Vector3[][] b)
        {
            float d = 0f;
            for (int p = 0; p < a.Length; p++) for (int i = 0; i < a[p].Length; i++) d = Mathf.Max(d, (a[p][i] - b[p][i]).magnitude);
            return d;
        }

        static string Same(List<LocalImpact> a, List<LocalImpact> b)
        {
            if (a.Count != b.Count) return $"NO ({a.Count} vs {b.Count})";
            float d = 0f;
            for (int i = 0; i < a.Count; i++) d = Mathf.Max(d, (a[i].point - b[i].point).magnitude, Mathf.Abs(a[i].strength - b[i].strength), a[i].footprint.travel - b[i].footprint.travel);
            return d == 0f ? "identical" : $"max record difference {d:0.0e0}";
        }

        static void Place(Rigidbody b, Vector3 pos, float yaw, Vector3 vel)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
            b.transform.SetPositionAndRotation(b.position, rot);
            b.linearVelocity = vel; b.angularVelocity = Vector3.zero;
        }
    }
}
