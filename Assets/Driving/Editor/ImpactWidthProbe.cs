using System.Collections.Generic;
using System.Text;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Diagnostic (not part of the game): how wide are the dent footprints in car-vs-car hits compared with a wall hit?
    /// Every scenario runs three times: first impactor box only (the old behaviour), combineImpactorBoxes, and combine plus vehicleGatherSteps 1;
    /// Width is the dent outline's extent across the struck face (local x for front/rear hits, local z for side hits);
    /// "expected" is the overlap of the two bodies' faces from their widths and the lateral offset.
    /// </summary>
    public static class ImpactWidthProbe
    {
        const float Dt = 0.02f;
        enum Kind { Wall, HeadOn, Offset, Side, Rear, Repeated }

        public static void Run()
        {
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<DrivingGame>();
            Physics.simulationMode = SimulationMode.Script;
            game.Setup();
            foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall") && !o.name.StartsWith("CrashWall")) o.gameObject.SetActive(false);
            var sb = new StringBuilder("[ImpactWidthProbe] dent footprint of the damaging hit, BEFORE = first box only; UNION = combineImpactorBoxes, no gather; AFTER = combine + vehicleGatherSteps 1\n");
            sb.AppendLine("  columns: car | width across the struck face (m) | % of that car's face | expected contact overlap (m) | width/expected | patch U x V (m) | crush travel (m) | area factor | energy (kJ) | strength | impacts applied | impactor boxes");
            string[] N = { "Muscle", "Hatch", "Pickup" };
            var callbacks = new List<string>();

            float Width(DrivingGame.Slot s) => s.rig.geometry.body.size.x;
            float Length(DrivingGame.Slot s) => s.rig.geometry.body.size.z;

            // Extent of the dent outline along a vehicle-local axis (0 = x, 2 = z).
            float Extent(CollisionDamageReceiver.Report r, int axis)
            {
                var f = r.footprint;
                if (!f.valid) return 0f;
                if (f.outline != null && f.outline.Length > 2)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    foreach (var p in f.outline) { lo = Mathf.Min(lo, p[axis]); hi = Mathf.Max(hi, p[axis]); }
                    return hi - lo;
                }
                return 2f * (f.halfU * Mathf.Abs(f.axisU[axis]) + f.halfV * Mathf.Abs(f.axisV[axis]));
            }

            void Line(string car, DrivingGame.Slot s, int axis, float expected)
            {
                var r = s.Receiver.Last;
                float face = axis == 0 ? Width(s) : Length(s);
                float w = Extent(r, axis);
                sb.AppendLine($"      {car,-6} width {w:0.00} m = {w / face * 100f,3:0}% of {face:0.00}  expected {expected:0.00}  ratio {(expected > 0f ? w / expected : 0f):0.00}  " +
                              $"patch {r.footprint.halfU * 2f:0.00} x {r.footprint.halfV * 2f:0.00}  travel {r.footprint.travel:0.000}  areaF {r.footprint.areaFactor:0.00}  " +
                              $"E {r.energy / 1000f:0.0}  str {r.strength * 100f:0.0}%  applied {s.Model.ImpactCount}/{s.Receiver.EventCount} events  [{r.impactorColliders}] struck '{r.carCollider}'");
            }

            void Place(Rigidbody b, Vector3 pos, float yaw, Vector3 vel)
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
                b.transform.SetPositionAndRotation(b.position, rot);
                b.linearVelocity = vel; b.angularVelocity = Vector3.zero;
            }

            void Scenario(Kind kind, int p, int t, int mode, bool logCallbacks)
            {
                game.SelectPlayer(p); game.SelectTarget(t);
                game.ResetAll();
                var P = game.Player; var T = game.Target;
                foreach (var s in new[] { P, T }) { s.Receiver.logImpacts = false; s.Receiver.combineImpactorBoxes = mode > 0; s.Receiver.vehicleGatherSteps = mode > 1 ? 1 : 0; }
                callbacks.Clear();
                int step = 0;
                System.Action<UnityEngine.Collision, bool> hook(string who) => (c, ok) =>
                {
                    if (callbacks.Count < 24) callbacks.Add($"{who} t{step * Dt:0.00}s {c.GetContact(0).thisCollider.name}<-{c.collider.name}{(ok ? "" : " (cooldown, ignored)")}");
                };
                var hp = hook("player"); var ht = hook("target");
                P.Receiver.ContactsReceived += hp; T.Receiver.ContactsReceived += ht;

                float wP = Width(P), wT = Width(T);
                Vector3 tp = T.Body.position;
                float offset = 0f;
                switch (kind)
                {
                    case Kind.Wall:
                        Place(P.Body, new Vector3(0, 0, -5f), 0f, Vector3.forward * 19f);
                        Place(T.Body, new Vector3(60, 0, -60), 0f, Vector3.zero);
                        break;
                    case Kind.HeadOn: case Kind.Repeated:
                        Place(T.Body, tp, 180f, Vector3.zero);
                        Place(P.Body, new Vector3(tp.x, 0, -30f), 0f, Vector3.forward * 20f);
                        break;
                    case Kind.Offset:
                        offset = 0.55f * Mathf.Min(wP, wT);   // about half the car's width: a classic offset frontal
                        Place(T.Body, tp, 180f, Vector3.zero);
                        Place(P.Body, new Vector3(tp.x + offset, 0, -30f), 0f, Vector3.forward * 20f);
                        break;
                    case Kind.Side:
                        Place(T.Body, tp, 90f, Vector3.zero);   // broadside across the player's path
                        Place(P.Body, new Vector3(tp.x, 0, -30f), 0f, Vector3.forward * 20f);
                        break;
                    case Kind.Rear:
                        Place(T.Body, tp, 0f, Vector3.zero);    // facing away: the player hits its rear
                        Place(P.Body, new Vector3(tp.x, 0, -30f), 0f, Vector3.forward * 20f);
                        break;
                }
                Physics.SyncTransforms();
                for (step = 0; step < 200; step++)
                {
                    P.controller.Throttle = 0; P.controller.Tick(Dt);
                    Physics.Simulate(Dt);
                    P.Receiver.Step(Dt); T.Receiver.Step(Dt);
                }

                string label = kind == Kind.Wall ? $"{N[p]} front into CrashWall @19 m/s" : $"{N[p]} -> {N[t]} {kind} @20 m/s{(kind == Kind.Offset ? $" (offset {offset:0.00} m)" : "")}";
                sb.AppendLine($"  {(mode == 2 ? "AFTER " : mode == 1 ? "UNION " : "BEFORE")} {label}");
                float overlap = Mathf.Max(0f, Mathf.Min(wP * 0.5f, wT * 0.5f + offset) - Mathf.Max(-wP * 0.5f, -wT * 0.5f + offset));
                if (kind == Kind.Wall) Line("player", P, 0, wP);
                else if (kind == Kind.Side) { Line("player", P, 0, wP); Line("target", T, 2, wP); }
                else { Line("player", P, 0, overlap); Line("target", T, 0, overlap); }

                if (kind == Kind.Repeated)
                {
                    // Second hit on the same, already dented cars (damage persists; poses and velocities are re-launched).
                    Place(T.Body, tp, 180f, Vector3.zero);
                    Place(P.Body, new Vector3(tp.x, 0, -30f), 0f, Vector3.forward * 20f);
                    Physics.SyncTransforms();
                    for (step = 0; step < 200; step++)
                    {
                        P.controller.Throttle = 0; P.controller.Tick(Dt);
                        Physics.Simulate(Dt);
                        P.Receiver.Step(Dt); T.Receiver.Step(Dt);
                    }
                    sb.AppendLine("      second hit:");
                    Line("player", P, 0, overlap); Line("target", T, 0, overlap);
                }
                if (logCallbacks) foreach (var c in callbacks) sb.AppendLine("        callback " + c);
                P.Receiver.ContactsReceived -= hp; T.Receiver.ContactsReceived -= ht;
            }

            foreach (int combine in new[] { 0, 1, 2 })
            {
                for (int p = 0; p < 3; p++) Scenario(Kind.Wall, p, 0, combine, false);
                for (int p = 0; p < 3; p++)
                    for (int t = 0; t < 3; t++) Scenario(Kind.HeadOn, p, t, combine, p == 0 && t == 0);
                int[,] pairs = { { 0, 1 }, { 1, 2 }, { 2, 0 } };
                foreach (var kind in new[] { Kind.Offset, Kind.Side, Kind.Rear })
                    for (int i = 0; i < 3; i++) Scenario(kind, pairs[i, 0], pairs[i, 1], combine, i == 0);
                Scenario(Kind.Repeated, 0, 1, combine, false);
            }
            Debug.Log(sb.ToString());
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }
    }
}
