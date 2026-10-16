using System.Text;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>Diagnostic (not part of the game): how wide are the dent footprints in car-vs-car hits compared with a wall hit?</summary>
    public static class ImpactWidthProbe
    {
        const float Dt = 0.02f;
        public static void Run()
        {
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<DrivingGame>();
            Physics.simulationMode = SimulationMode.Script;
            game.Setup();
            foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall") && !o.name.StartsWith("CrashWall")) o.gameObject.SetActive(false);
            var sb = new StringBuilder("[ImpactWidthProbe] footprint of the damaging hit (width x height of the dent patch, metres)\n");
            game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
            string[] N = { "Muscle", "Hatch", "Pickup" };
            void Line(string name, CollisionDamageReceiver.Report r, string car, float bodyWidth)
                => sb.AppendLine($"  {name,-34} {car,-7} kind {r.footprint.kind,-6} patch {r.footprint.halfU * 2f:0.00} x {r.footprint.halfV * 2f:0.00} m (car body {bodyWidth:0.00} m wide) strength {r.strength * 100f:0}%  contacts {r.contacts}  struck collider '{r.carCollider}'");
            void Run(string name, int p, int t, bool wall)
            {
                game.SelectPlayer(p); game.SelectTarget(t);
                game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                game.ResetAll();
                var P = game.Player.Body; var T = game.Target.Body;
                if (wall) { P.position = new Vector3(0, P.position.y, -5f); P.transform.position = P.position; P.linearVelocity = Vector3.forward * 19f; T.position = new Vector3(60, T.position.y, -60); T.transform.position = T.position; }
                else { T.rotation = Quaternion.Euler(0, 180, 0); T.transform.rotation = T.rotation; P.position = new Vector3(0, P.position.y, -30f); P.transform.position = P.position; P.linearVelocity = Vector3.forward * 20f; }
                Physics.SyncTransforms();
                for (int i = 0; i < 200; i++) { game.Player.controller.Throttle = 0; game.Player.controller.Tick(Dt); Physics.Simulate(Dt); game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt); }
                var g = game.Player.rig.geometry.body; var gt = game.Target.rig.geometry.body;
                Line(name, game.Player.Receiver.Last, "player", g.size.x);
                if (!wall) Line(name, game.Target.Receiver.Last, "target", gt.size.x);
            }
            Run("muscle front into CrashWall", 0, 0, true);
            Run("hatch front into CrashWall", 1, 0, true);
            Run("pickup front into CrashWall", 2, 0, true);
            Run("muscle vs muscle, head-on", 0, 0, false);
            Run("hatch vs pickup, head-on", 1, 2, false);
            Run("pickup vs hatch, head-on", 2, 1, false);
            Debug.Log(sb.ToString());
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
    }
}
