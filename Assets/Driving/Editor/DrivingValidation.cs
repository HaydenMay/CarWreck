using System;
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
    /// Automated driving + vehicle-to-vehicle checks with real PhysX, stepped by script (Physics.Simulate at 50 Hz) in the
    /// editor: per-vehicle handling (accelerate, brake, reverse, steer, stability), vehicle switching (no state carry-over),
    /// and every player/target combination driven into each other (both receive damage, both move, persistence, reset),
    /// plus rear-first hits. Validates the simulation only; browser input, touch, the menu and the camera are checked separately.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Driving.EditorTools.DrivingValidation.Run -quit
    /// </summary>
    public static class DrivingValidation
    {
        const float Dt = 0.02f;
        static readonly string[] Names = { "Muscle Car", "Compact Hatchback", "Compact Pickup" };

        [MenuItem("CarWreck/Validate Driving", priority = 12)]
        public static void Run() => Debug.Log(Execute());

        static DrivingGame game;
        static ArcadeCarController C => game.Player.controller;
        static Rigidbody P => game.Player.Body;
        static Rigidbody T => game.Target.Body;

        static void Step(int steps, float throttle, float steer, Action<int> each = null) => Step(steps, i => throttle, steer, each);

        static void Step(int steps, Func<int, float> throttle, float steer, Action<int> each = null)
        {
            for (int i = 0; i < steps; i++)
            {
                C.Throttle = throttle(i);
                C.SteerInput = steer;
                C.Tick(Dt);
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt);
                game.Target.Receiver.Step(Dt);
                each?.Invoke(i);
            }
        }

        /// <summary>Teleport the player to (x, z) facing <paramref name="yaw"/>, optionally already moving along its heading (m/s).</summary>
        static void Place(float x, float z, float yaw, float speedAlongHeading = 0f)
        {
            var rb = P;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = new Vector3(x, game.Player.startPos.y, z);
            rb.rotation = rot;
            rb.transform.SetPositionAndRotation(rb.position, rb.rotation);
            rb.linearVelocity = rot * Vector3.forward * speedAlongHeading;
            rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            C.ResetState();
        }

        static float MaxDisplacement(DrivingGame.Slot s)
        {
            // Largest vertex distance between the live (deformed) meshes and the imported source meshes, by part name.
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

        static string Execute()
        {
            var sb = new StringBuilder("[Driving] Validation M2 (real PhysX, Physics.Simulate at 50 Hz, editor timings)\n");
            int fails = 0;
            void Check(string name, bool ok, string detail) { sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}"); if (!ok) fails++; }

            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                game.Setup();
                foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);   // open ground for the tests
                game.Player.Receiver.logImpacts = false;

                // ================================================================ handling per vehicle
                sb.AppendLine("  -- handling (open ground; km/h) --");
                sb.AppendLine("  vehicle              0-60  0-80   top fwd  brake 72->0   rev 0-30  rev@6s  rev top  yaw/s@15m/s  minUp");
                var results = new (float t60, float t100, float top, float brake, float rev30, float rev6, float revTop, float yaw, float up)[3];
                for (int v = 0; v < 3; v++)
                {
                    game.SelectPlayer(v);
                    game.Player.Receiver.logImpacts = false;
                    float t60 = -1, t100 = -1, top = 0;
                    Place(60f, -66f, 0f);
                    Step(30, 0f, 0f);
                    Step(400, 1f, 0f, i => { float k = C.ForwardSpeed * 3.6f; if (t60 < 0 && k >= 60f) t60 = (i + 1) * Dt; if (t100 < 0 && k >= 80f) t100 = (i + 1) * Dt; top = Mathf.Max(top, k); });
                    var prof = game.profiles[v];
                    // true top speed: start near it and keep the throttle down
                    Place(60f, -66f, 0f, prof.maxSpeed * 0.9f);
                    Step(150, 1f, 0f, i => top = Mathf.Max(top, C.ForwardSpeed * 3.6f));
                    // braking from 20 m/s
                    Place(60f, -66f, 0f, 20f);
                    Step(1, 0f, 0f);
                    float stopT = 0f;
                    Step(100, -1f, 0f, i => { if (C.ForwardSpeed > 0.6f) stopT = (i + 1) * Dt; });
                    // reverse from rest
                    float r30 = -1, rev6 = 0, revTop = 0;
                    Place(60f, 60f, 0f);
                    Step(30, 0f, 0f);
                    Step(300, -1f, 0f, i => { float k = -C.ForwardSpeed * 3.6f; if (r30 < 0 && k >= 30f) r30 = (i + 1) * Dt; if (i == 299) rev6 = k; revTop = Mathf.Max(revTop, k); });
                    // true reverse top speed
                    Place(60f, 60f, 0f, 0f);
                    P.linearVelocity = -(P.rotation * Vector3.forward) * prof.reverseMaxSpeed * 0.9f;
                    Step(150, -1f, 0f, i => revTop = Mathf.Max(revTop, -C.ForwardSpeed * 3.6f));
                    // steering rate at 15 m/s, full lock, coasting, 1 s
                    Place(60f, -66f, 0f, 15f);
                    Step(10, 0f, 0f);
                    float yaw0 = P.rotation.eulerAngles.y;
                    Step(50, 0f, 1f);
                    float yawRate = Mathf.DeltaAngle(yaw0, P.rotation.eulerAngles.y);
                    // reverse steering: right lock while reversing turns the nose to the left (the tail swings right)
                    Place(60f, 60f, 0f, 0f);
                    P.linearVelocity = -(P.rotation * Vector3.forward) * 8f;
                    Step(5, -1f, 0f);
                    float ry0 = P.rotation.eulerAngles.y;
                    Step(50, -1f, 1f);
                    float revYaw = Mathf.DeltaAngle(ry0, P.rotation.eulerAngles.y);
                    // stability: 3 s full lock near top speed
                    float minUp = 1f;
                    Place(60f, -64f, 0f, prof.maxSpeed * 0.9f);
                    Step(150, 1f, -1f, i => minUp = Mathf.Min(minUp, P.transform.up.y));
                    results[v] = (t60, t100, top, stopT, r30, rev6, revTop, yawRate, minUp);
                    sb.AppendLine($"  {Names[v],-20}{t60,5:0.0}s {(t100 < 0 ? "  --  " : t100.ToString("0.0").PadLeft(5) + "s")}{top,7:0}   {stopT,5:0.00}s       {r30,5:0.0}s  {rev6,6:0}  {revTop,7:0}  {yawRate,8:0}      {minUp:0.00}");
                    Check($"{Names[v]}: steering works forward and reversing", yawRate > 25f && revYaw < -5f, $"{yawRate:0}° in 1 s at 15 m/s forward; reversing with right lock yaws {revYaw:0}° (negative = tail swings right)");
                }
                Check("Muscle reverse reaches about 45 km/h", results[0].revTop > 41f && results[0].revTop < 50f, $"{results[0].revTop:0} km/h (Milestone 1: ~29 km/h terminal); 30 km/h after {results[0].rev30:0.0} s");
                Check("Hatchback accelerates faster than the muscle car, pickup slower", results[1].t60 > 0 && results[1].t60 < results[0].t60 && results[2].t60 > results[0].t60, $"0-60: hatch {results[1].t60:0.0} s, muscle {results[0].t60:0.0} s, pickup {results[2].t60:0.0} s");
                Check("Pickup brakes later than the others", results[2].brake > results[0].brake && results[0].brake >= results[1].brake, $"72 km/h to stop: hatch {results[1].brake:0.00} s, muscle {results[0].brake:0.00} s, pickup {results[2].brake:0.00} s");
                Check("Steering rate: hatchback > muscle > pickup", results[1].yaw > results[0].yaw && results[0].yaw > results[2].yaw, $"{results[1].yaw:0}°, {results[0].yaw:0}°, {results[2].yaw:0}° in 1 s at 54 km/h");
                Check("No vehicle tips over in a sustained full-lock turn", results.All(r => r.up > 0.85f), string.Join(", ", results.Select(r => r.up.ToString("0.00"))));

                // ================================================================ vehicle switching
                sb.AppendLine("  -- switching --");
                game.SelectPlayer(2);
                var inject = new LocalImpact { point = new Vector3(0f, 0.66f, game.Player.Model.BodyBounds.max.z), direction = Vector3.back, normal = Vector3.forward, strength = 1f };
                game.Player.Model.AddImpact(inject);
                float dmg = MaxDisplacement(game.Player);
                game.SelectPlayer(0);
                var fresh = game.Player;
                float freshDisp = MaxDisplacement(fresh);
                bool colorsOk = true;
                foreach (var r in fresh.rig.root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var src = fresh.def.model.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x => x.name == r.name);
                    if (src == null || src.sharedMaterial == null || r.sharedMaterial == null) continue;
                    if (src.sharedMaterial != r.sharedMaterial) { colorsOk = false; break; }
                }
                Check("Switching vehicle starts undamaged", dmg > 0.1f && freshDisp < 1e-4f && fresh.Model.ImpactCount == 0 && !fresh.Model.IsDamaged, $"damaged pickup displaced {dmg:0.00} m; new muscle car displacement {freshDisp:0.0000} m, impacts {fresh.Model.ImpactCount}");
                Check("Paint/materials preserved after switching", colorsOk && fresh.rig.root.GetComponentsInChildren<MeshRenderer>(true).Length > 5, "renderer materials identical to the vehicle model's");
                // two instances of the same model are independent
                game.SelectPlayer(2); game.SelectTarget(2);
                game.Target.Model.AddImpact(new LocalImpact { point = new Vector3(0f, 0.66f, game.Target.Model.BodyBounds.max.z), direction = Vector3.back, normal = Vector3.forward, strength = 1f });
                float tDisp = MaxDisplacement(game.Target), pDisp = MaxDisplacement(game.Player);
                Check("Same model twice: damage stays on the damaged instance", tDisp > 0.1f && pDisp < 1e-4f, $"target pickup displaced {tDisp:0.00} m, player pickup {pDisp:0.0000} m");
                game.ResetAll();
                Check("Reset restores a damaged target", MaxDisplacement(game.Target) < 1e-4f && game.Target.Model.ImpactCount == 0, "mesh and impact records restored");

                // ================================================================ vehicle-vs-vehicle
                sb.AppendLine("  -- player drives into the parked target (approach cruise 20 m/s, broadside; kmh = closing speed) --");
                sb.AppendLine("  player -> target             kmh | player: ev dmg str  ms | target: ev dmg str  ms | target moved  yaw | sustained +dmg P/T | 2nd hit P/T | persist | reset");
                for (int p = 0; p < 3; p++)
                    for (int t = 0; t < 3; t++)
                    {
                        game.SelectPlayer(p); game.SelectTarget(t);
                        game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                        game.ResetAll();
                        Step(50, 0f, 0f);                                    // settle
                        var tStart = T.position; var tYaw0 = T.rotation.eulerAngles.y;
                        // cruise at 20 m/s towards the target (it is 42 m ahead; contact happens a few seconds in)
                        Step(250, i => P.linearVelocity.magnitude < 20f ? 1f : 0f, 0f);
                        var pr = game.Player.Receiver.Last; var tr = game.Target.Receiver.Last;
                        int pDamage = game.Player.Model.ImpactCount, tDamage = game.Target.Model.ImpactCount;
                        float moved = Vector3.Distance(T.position, tStart), yawD = Mathf.Abs(Mathf.DeltaAngle(tYaw0, T.rotation.eulerAngles.y));
                        // sustained push: hold the throttle against the target
                        Step(100, 1f, 0f);
                        int pExtra = game.Player.Model.ImpactCount - pDamage, tExtra = game.Target.Model.ImpactCount - tDamage;
                        // second distinct hit: back off, then drive in again
                        Step(60, -1f, 0f);
                        int p2 = game.Player.Model.ImpactCount, t2 = game.Target.Model.ImpactCount;
                        Step(150, 1f, 0f);
                        int pSecond = game.Player.Model.ImpactCount - p2, tSecond = game.Target.Model.ImpactCount - t2;
                        Step(50, 0f, 0f);
                        float pPersist = MaxDisplacement(game.Player), tPersist = MaxDisplacement(game.Target);
                        int pEv = game.Player.Receiver.EventCount, tEv = game.Target.Receiver.EventCount;
                        game.ResetAll();
                        Step(5, 0f, 0f);
                        bool resetOk = MaxDisplacement(game.Player) < 1e-4f && MaxDisplacement(game.Target) < 1e-4f && Vector3.Distance(T.position, game.Target.startPos) < 0.05f
                                       && game.Player.Model.ImpactCount == 0 && game.Target.Model.ImpactCount == 0 && P.linearVelocity.magnitude < 0.1f && T.linearVelocity.magnitude < 0.1f;
                        bool both = pDamage > 0 && tDamage > 0 && pr.applied && tr.applied;
                        sb.AppendLine($"  {Names[p],-18}-> {Names[t],-18}{pr.closingSpeed * 3.6f,4:0} | {pEv,2} {pDamage,3} {pr.strength * 100f,3:0} {pr.deformMilliseconds,5:0.0} | " +
                                      $"{tEv,2} {tDamage,3} {tr.strength * 100f,3:0} {tr.deformMilliseconds,5:0.0} | {moved,5:0.0} m {yawD,4:0}° | +{pExtra}/+{tExtra} | +{pSecond}/+{tSecond} | {(pPersist > 0.05f && tPersist > 0.05f ? "yes" : "NO")} | {(resetOk ? "ok" : "NO")}");
                        string key = $"{Names[p]} -> {Names[t]}";
                        Check($"{key}: both vehicles damaged and target pushed", both && (moved > 0.3f || yawD > 3f), $"player impacts {pDamage}, target impacts {tDamage}, target moved {moved:0.0} m / yawed {yawD:0}°");
                        Check($"{key}: damage persists and reset restores both", pPersist > 0.05f && tPersist > 0.05f && resetOk, $"max displacement after 1 s: player {pPersist:0.00} m, target {tPersist:0.00} m; reset ok {resetOk}");
                    }

                // ================================================================ rear-first hits
                sb.AppendLine("  -- player reverses into a parked pickup (full reverse throttle from 40 m away) --");
                for (int p = 0; p < 3; p++)
                {
                    game.SelectPlayer(p); game.SelectTarget(2);
                    game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                    game.ResetAll();
                    Step(30, 0f, 0f);
                    // facing -z, 40 m south of the target's side: reversing runs backwards (+z) into it
                    Place(0f, -48f, 180f);
                    Step(30, 0f, 0f);
                    float peak = 0f;
                    Step(500, -1f, 0f, i => { if (game.Player.Receiver.EventCount == 0) peak = Mathf.Max(peak, -C.ForwardSpeed * 3.6f); });
                    var pr = game.Player.Receiver.Last; var tr = game.Target.Receiver.Last;
                    bool rear = pr.region != null && pr.region.StartsWith("rear");
                    Check($"{Names[p]} reversing into pickup: rear impact with real damage", pr.applied && rear && game.Target.Model.ImpactCount > 0,
                          $"closing {pr.closingSpeed * 3.6f:0} km/h (peak {peak:0}), region '{pr.region}', player strength {pr.strength * 100f:0}% ({pr.energy / 1000f:0} kJ), target strength {tr.strength * 100f:0}%, deform {pr.deformMilliseconds:0.0}/{tr.deformMilliseconds:0.0} ms");
                }
            }
            finally
            {
                game.ResetAll();
                Physics.simulationMode = prev;
                game.Player.Model.Shutdown();
                game.Target.Model.Shutdown();
                EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);   // discard edit-mode changes
            }
            sb.AppendLine(fails == 0 ? "[Driving] ALL CHECKS PASSED" : $"[Driving] {fails} CHECK(S) FAILED");
            return sb.ToString();
        }
    }
}
