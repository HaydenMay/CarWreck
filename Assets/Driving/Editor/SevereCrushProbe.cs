using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Diagnostic (not part of the game): how deep does a dead-on head-on crush each car as the closing speed rises?
    /// Every pairing runs with severe crush OFF (the old mapping: depth stops growing once strength saturates) and ON
    /// (the driving lab's severe-crush settings). Crush = largest vertex displacement of the car's meshes (m).
    /// </summary>
    public static class SevereCrushProbe
    {
        const float Dt = 0.02f;

        public static void Run()
        {
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            Physics.simulationMode = SimulationMode.Script;
            game.Setup();
            foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall") && !o.name.StartsWith("CrashWall")) o.gameObject.SetActive(false);
            var sb = new StringBuilder("[SevereCrushProbe] dead-on head-ons, both cars at half the closing speed. Per car: energy share (kJ) | strength | depth scale | travel (m) | crush (m)\n");
            float onEnergy = game.severeCrushEnergy, onCap = game.crushCapacityScale;
            string[] names = game.vehicles.Select(v => v.displayName).ToArray();

            void Place(Rigidbody b, Vector3 pos, float yaw, Vector3 vel)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
                b.transform.SetPositionAndRotation(b.position, rot);
                b.linearVelocity = vel; b.angularVelocity = Vector3.zero;
            }

            string Car(DrivingGame.Slot s)
            {
                if (!s.Receiver.HasReport) return "no hit";
                var r = s.Receiver.Last;
                return $"{r.energy / 1000f,6:0} kJ  str {r.strength * 100f,3:0}%  x{s.Receiver.SevereDepthScale(r.energy):0.00}  travel {r.footprint.travel:0.000}  crush {MaxDisplacement(s):0.000}";
            }

            void Respawn(int p, int t)
            {
                // Spawn applies the capacity scale: force a fresh spawn of both cars.
                game.SelectPlayer((p + 1) % names.Length); game.SelectPlayer(p);
                game.SelectTarget((t + 1) % names.Length); game.SelectTarget(t);
            }

            void Pair(int p, int t)
            {
                foreach (bool on in new[] { false, true })
                {
                    game.severeCrushEnergy = on ? onEnergy : 0f;
                    game.crushCapacityScale = on ? onCap : 1f;
                    Respawn(p, t);
                    sb.AppendLine($"  {names[p]} (player) vs {names[t]} (target), severe crush {(on ? "ON" : "OFF")}");
                    foreach (float kmh in new[] { 30f, 60f, 100f, 140f, 170f })
                    {
                        game.ResetAll();
                        var P = game.Player; var T = game.Target;
                        foreach (var s in new[] { P, T }) s.Receiver.logImpacts = false;
                        float v = kmh / 3.6f;
                        Vector3 tp = T.Body.position;
                        Place(T.Body, tp, 180f, Vector3.back * v * 0.5f);
                        Place(P.Body, new Vector3(tp.x, 0f, tp.z) - Vector3.forward * 9f, 0f, Vector3.forward * v * 0.5f);
                        Physics.SyncTransforms();
                        for (int step = 0; step < 150; step++)
                        {
                            Physics.Simulate(Dt);
                            P.Receiver.Step(Dt); T.Receiver.Step(Dt);
                        }
                        sb.AppendLine($"    {kmh,3:0} km/h  player: {Car(P)}   target: {Car(T)}   impacts {P.Model.ImpactCount}/{T.Model.ImpactCount}");
                    }
                }
            }

            int hatch = Array.FindIndex(names, n => n.Contains("Hatch")), pickup = Array.FindIndex(names, n => n.Contains("Pickup"));
            Pair(0, 0);
            if (hatch >= 0 && pickup >= 0) Pair(hatch, pickup);
            game.severeCrushEnergy = onEnergy; game.crushCapacityScale = onCap;
            Debug.Log(sb.ToString());
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        static float MaxDisplacement(DrivingGame.Slot s)
        {
            float d = 0f;
            var source = s.def.model.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).GroupBy(f => f.name).ToDictionary(g => g.Key, g => g.First().sharedMesh.vertices, StringComparer.Ordinal);
            foreach (var f in s.rig.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || !source.TryGetValue(f.name, out var src)) continue;
                var now = f.sharedMesh.vertices;
                if (now.Length != src.Length) continue;
                for (int i = 0; i < now.Length; i++) d = Mathf.Max(d, (now[i] - src[i]).magnitude);
            }
            return d;
        }
    }
}
