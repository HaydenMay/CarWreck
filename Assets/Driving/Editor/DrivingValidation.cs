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
        // "Damage persists" floor: a dent still this deep 1 s after the hits (reverted damage reads ~0). The striker takes a 15%
        // share spread over its full face, so its dent is shallow: 0.047 m for Compact Pickup -> Compact Hatchback (head-on face fill).
        const float PersistFloor = 0.03f;

        [MenuItem("CarWreck/Validate Driving", priority = 12)]
        public static void Run() => Debug.Log(Execute());

        static DrivingGame game;
        static ArcadeCarController C => game.Player.controller;
        static Rigidbody P => game.Player.Body;
        static Rigidbody T => game.Target.Body;

        static void Step(int steps, float throttle, float steer, Action<int> each = null) => Step(steps, i => throttle, steer, each);

        static void Step(int steps, Func<int, float> throttle, float steer, Action<int> each = null) => Step(steps, throttle, i => steer, each);

        static void Step(int steps, Func<int, float> throttle, Func<int, float> steer, Action<int> each = null)
        {
            for (int i = 0; i < steps; i++)
            {
                C.Throttle = throttle(i);
                C.SteerInput = steer(i);
                C.Tick(Dt);
                if (game.Target.controller != null) { game.Target.driver?.Drive(); game.Target.enemy?.Drive(Dt); game.Target.controller.Tick(Dt); }   // moving target: its driver feeds its own controller
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt);
                game.Target.Receiver.Step(Dt);
                game.AfterPhysicsStep(Dt);                                  // the arena's collider-refit safe point (FixedUpdate at runtime)
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
                                      $"{tEv,2} {tDamage,3} {tr.strength * 100f,3:0} {tr.deformMilliseconds,5:0.0} | {moved,5:0.0} m {yawD,4:0}° | +{pExtra}/+{tExtra} | +{pSecond}/+{tSecond} | {(pPersist > PersistFloor && tPersist > PersistFloor ? "yes" : "NO")} | {(resetOk ? "ok" : "NO")}");
                        string key = $"{Names[p]} -> {Names[t]}";
                        float eRatio = pr.energy > 0f ? tr.energy / pr.energy : 0f;
                        Check($"{key}: the parked target takes the bigger share (15/85)", tr.strength > pr.strength && eRatio > 4.5f && eRatio < 7f,
                              $"energy applied: player {pr.energy / 1000f:0} kJ, target {tr.energy / 1000f:0} kJ (ratio {eRatio:0.0}, expected 5.7); strength {pr.strength * 100f:0}% vs {tr.strength * 100f:0}%");
                        Check($"{key}: both vehicles damaged and target pushed", both && (moved > 0.3f || yawD > 3f), $"player impacts {pDamage}, target impacts {tDamage}, target moved {moved:0.0} m / yawed {yawD:0}°");
                        Check($"{key}: damage persists and reset restores both", pPersist > PersistFloor && tPersist > PersistFloor && resetOk, $"max displacement after 1 s: player {pPersist:0.000} m, target {tPersist:0.000} m; reset ok {resetOk}");
                    }

                // ================================================================ momentum split: moving cars
                sb.AppendLine("  -- momentum split (player share of the dissipated energy; wall hits are unaffected) --");
                {
                    // hatchback (player, 15 m/s) head-on with a pickup (target, 15 m/s towards it): mass counts, the lighter car takes slightly more
                    game.SelectPlayer(1); game.SelectTarget(2);
                    game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                    game.ResetAll();
                    Place(0f, -30f, 0f, 15f);
                    T.linearVelocity = new Vector3(0f, 0f, -15f);
                    Step(2, 0f, 0f);
                    Step(150, 0f, 0f);
                    var pr = game.Player.Receiver.Last; var tr = game.Target.Receiver.Last;
                    float share = pr.energy / Mathf.Max(1f, pr.energy + tr.energy);
                    float pm = 950f * 15f, tm = 1350f * 15f, want = 0.5f + 0.35f * (1f - 2f * pm / (pm + tm));
                    Check("Hatchback head-on with a moving pickup: both attack, so the split stays near 50/50", pr.applied && tr.applied && share > 0.3f && share < 0.7f, $"(both attacking, so no lopsided split; speeds decay before contact so exact mass effect is not asserted) hatchback took {share * 100f:0}% of the energy (expected {want * 100f:0}%), strength {pr.strength * 100f:0}% vs {tr.strength * 100f:0}%; closing {pr.closingSpeed:0.0}/{tr.closingSpeed:0.0} m/s, energy {pr.energy / 1000f:0.0}/{tr.energy / 1000f:0.0} kJ, target speed now {T.linearVelocity.magnitude:0.0}, events {game.Player.Receiver.EventCount}/{game.Target.Receiver.EventCount}");
                    // muscle (player, 25 m/s) rear-ends a muscle car driving away at 15 m/s: the attacker takes the small share
                    game.SelectPlayer(0); game.SelectTarget(0);
                    game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                    game.ResetAll();
                    Place(0f, -30f, 0f, 25f);
                    T.linearVelocity = new Vector3(0f, 0f, 15f);
                    Step(2, 0f, 0f);
                    Step(200, 0f, 0f);
                    pr = game.Player.Receiver.Last; tr = game.Target.Receiver.Last;
                    share = pr.energy / Mathf.Max(1f, pr.energy + tr.energy);
                    Check("Rear-ending a retreating car: the attacker takes the small share", pr.applied && tr.applied && Mathf.Abs(share - 0.15f) < 0.04f, $"attacker took {share * 100f:0}% of the energy, strength {pr.strength * 100f:0}% vs {tr.strength * 100f:0}%");
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
                // ================================================================ moving target scenarios
                sb.AppendLine("  -- moving target scenarios (muscle car player, pickup target; target speed in km/h) --");
                foreach (var mode in new[] { DrivingGame.TargetMode.SameDirection, DrivingGame.TargetMode.HeadOn })
                {
                    string label = mode == DrivingGame.TargetMode.SameDirection ? "SAME DIRECTION" : "HEAD-ON";
                    game.SelectPlayer(0);
                    game.SetTargetMode((int)mode);
                    game.SelectTarget(2);
                    game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                    game.SetTargetSpeedKmh(40f);
                    game.ResetAll();
                    var ps = mode == DrivingGame.TargetMode.SameDirection ? game.sameDirPlayerSpawn : game.headOnPlayerSpawn;
                    var ts = mode == DrivingGame.TargetMode.SameDirection ? game.sameDirTargetSpawn : game.headOnTargetSpawn;
                    Check($"{label}: scenario selection places both cars", Vector3.Distance(new Vector3(P.position.x, 0, P.position.z), ps.position) < 0.1f && Vector3.Distance(new Vector3(T.position.x, 0, T.position.z), ts.position) < 0.1f && game.Target.driver != null,
                          $"player at {P.position.ToString("F1")}, target at {T.position.ToString("F1")}, target driven {game.Target.driver != null}");

                    // 1) the target drives straight at its configured speed, by physics (player idle, nothing in its way)
                    foreach (float kmh in new[] { 30f, 60f })
                    {
                        game.SetTargetSpeedKmh(kmh);
                        game.ResetAll();
                        var t0 = T.position; var yaw0 = T.rotation.eulerAngles.y;
                        float maxX = 0f;
                        Step(150, 0f, 0f, i => maxX = Mathf.Max(maxX, Mathf.Abs(T.position.x - t0.x)));
                        float sp = T.linearVelocity.magnitude * 3.6f;
                        float yawDrift = Mathf.Abs(Mathf.DeltaAngle(yaw0, T.rotation.eulerAngles.y));
                        float travelled = Vector3.Distance(new Vector3(T.position.x, 0, T.position.z), new Vector3(t0.x, 0, t0.z));
                        Check($"{label}: target holds {kmh:0} km/h on a straight line", Mathf.Abs(sp - kmh) < 3f && maxX < 0.3f && yawDrift < 1f && Mathf.Abs(travelled - kmh / 3.6f * 3f) < 3f,
                              $"speed {sp:0.0} km/h after 3 s, lateral drift {maxX:0.00} m, heading drift {yawDrift:0.0} deg, travelled {travelled:0.0} m (expected {kmh / 3.6f * 3f:0.0}), state {game.Target.driver.Current}");
                    }
                    // 2) route end: nothing hits it, it brakes to a stop inside the arena
                    game.SetTargetSpeedKmh(40f);
                    game.ResetAll();
                    var tStart = T.position;
                    Step(1000, 0f, 0f);       // 20 s
                    float runLen = Vector3.Distance(new Vector3(T.position.x, 0, T.position.z), new Vector3(tStart.x, 0, tStart.z));
                    Check($"{label}: unstruck target stops at the end of its route", game.Target.driver.Current == TargetDriver.State.Stopping && T.linearVelocity.magnitude < 0.6f && runLen > game.routeLength - 2f && runLen < game.routeLength + 15f && Mathf.Abs(T.position.z) < 69f,
                          $"state {game.Target.driver.Current}, speed {T.linearVelocity.magnitude * 3.6f:0.0} km/h, ran {runLen:0} m (route {game.routeLength:0})");

                    // 3) collision, repeated 5 times with a reset between runs
                    var strengthsP = new System.Collections.Generic.List<float>(); var strengthsT = new System.Collections.Generic.List<float>();
                    bool stable = true;
                    for (int run = 0; run < 5; run++)
                    {
                        var events = new System.Text.StringBuilder();
                        CollisionDamageReceiver.Report? firstP = null, firstT = null;          // the FIRST damaging hit of each car (later ones: wall pins, re-hits)
                        System.Action<CollisionDamageReceiver.Report> logP = r => { if (r.applied && firstP == null) firstP = r; events.Append($"[P vs {r.other} {r.closingSpeed * 3.6f:0}km/h {r.strength * 100f:0}% {(r.applied ? "applied" : "not")} {r.region}] "); };
                        System.Action<CollisionDamageReceiver.Report> logT = r => { if (r.applied && firstT == null) firstT = r; events.Append($"[T vs {r.other} {r.closingSpeed * 3.6f:0}km/h {r.strength * 100f:0}% {(r.applied ? "applied" : "not")} {r.region}] "); };
                        game.Player.Receiver.ImpactRegistered += logP; game.Target.Receiver.ImpactRegistered += logT;
                        game.ResetAll();
                        Step(10, 0f, 0f);
                        bool contact = false; int contactStep = -1; float preSpeedT = 0f, preSpeedP = 0f, postSpeedT = 0f, maxXT = 0f; var tp0 = T.position;
                        Vector3 tFwd = game.Target.startRot * Vector3.forward;
                        // First physical car-car contact (the damage event itself is gathered for a step, so EventCount lags it).
                        bool touched = false;
                        System.Action<UnityEngine.Collision, bool> onTouch = (c, ok) => { if (c.rigidbody == P || c.rigidbody == T) touched = true; };
                        game.Player.Receiver.ContactsReceived += onTouch;
                        Step(700, 1f, 0f, i =>
                        {
                            if (!contact)
                            {
                                if (touched || game.Player.Receiver.EventCount > 0 || game.Target.Receiver.EventCount > 0) { contact = true; contactStep = i; }
                                else { preSpeedT = Vector3.Dot(T.linearVelocity, tFwd) * 3.6f; preSpeedP = P.linearVelocity.magnitude * 3.6f; maxXT = Mathf.Max(maxXT, Mathf.Abs(T.position.x - tp0.x)); }
                            }
                            else if (i == contactStep + 12 && postSpeedT == 0f) postSpeedT = Vector3.Dot(T.linearVelocity, tFwd) * 3.6f;    // 0.25 s after first contact
                        });
                        game.Player.Receiver.ContactsReceived -= onTouch;
                        game.Player.Receiver.ImpactRegistered -= logP; game.Target.Receiver.ImpactRegistered -= logT;
                        if (run == 0) sb.AppendLine($"    {label} run 1 events: {events}");
                        var pr = firstP ?? game.Player.Receiver.Last; var tr = firstT ?? game.Target.Receiver.Last;
                        float share = pr.energy / Mathf.Max(1f, pr.energy + tr.energy);
                        bool finite = !float.IsNaN(P.position.x) && !float.IsNaN(T.position.x) && P.position.magnitude < 500f && T.position.magnitude < 500f;
                        stable &= finite;
                        strengthsP.Add(pr.strength * 100f); strengthsT.Add(tr.strength * 100f);
                        if (run == 0)
                        {
                            float expectedClosing = mode == DrivingGame.TargetMode.SameDirection ? preSpeedP - preSpeedT : preSpeedP + preSpeedT;
                            Check($"{label}: collision registers on both cars through the existing engine", pr.applied && tr.applied && game.Player.Model.ImpactCount > 0 && game.Target.Model.ImpactCount > 0 && Mathf.Abs(pr.closingSpeed * 3.6f - expectedClosing) < 8f,
                                  $"closing {pr.closingSpeed * 3.6f:0} km/h (player {preSpeedP:0} / target {preSpeedT:0} km/h before impact, expected about {expectedClosing:0}), strength {pr.strength * 100f:0}% / {tr.strength * 100f:0}%, regions '{pr.region}' / '{tr.region}', deform {pr.deformMilliseconds:0}/{tr.deformMilliseconds:0} ms");
                            Check($"{label}: target stayed on its straight path until impact", maxXT < 0.5f, $"max lateral deviation {maxXT:0.00} m (the player steers; the target never does)");
                            bool reacted = mode == DrivingGame.TargetMode.SameDirection ? postSpeedT > preSpeedT + 1f : postSpeedT < preSpeedT - 8f;
                            Check($"{label}: target reacts physically (velocity changed by the collision) and its driver lifts off", reacted && game.Target.driver.Current == TargetDriver.State.Struck,
                                  $"target speed along its heading {preSpeedT:0} -> {postSpeedT:0} km/h, driver {game.Target.driver.Current}");
                            if (mode == DrivingGame.TargetMode.SameDirection)
                                Check($"{label}: attacker/retreating-car split follows momentum", Mathf.Abs(share - 0.15f) < 0.05f, $"player took {share * 100f:0}% of the energy (expected 15%)");
                            else
                                Check($"{label}: both attacking so the split is not lopsided", share > 0.25f && share < 0.6f, $"player took {share * 100f:0}% of the energy (muscle car vs pickup, both driving into each other)");
                        }
                    }
                    float spread = Mathf.Max(strengthsP.Max() - strengthsP.Min(), strengthsT.Max() - strengthsT.Min());
                    Check($"{label}: 5 repeated runs after reset are stable and consistent", stable && spread < 12f, $"player strengths {string.Join("/", strengthsP.Select(x => x.ToString("0")))}%, target {string.Join("/", strengthsT.Select(x => x.ToString("0")))}% (spread {spread:0} points)");

                    // 4) reset restores everything, including the scenario movement
                    game.ResetAll();
                    Step(150, 0f, 0f);
                    float spd = T.linearVelocity.magnitude * 3.6f;
                    Check($"{label}: reset restores both cars, clears damage and restarts the target", MaxDisplacement(game.Player) < 1e-4f && MaxDisplacement(game.Target) < 1e-4f && game.Player.Model.ImpactCount == 0 && game.Target.Model.ImpactCount == 0 && Mathf.Abs(spd - 40f) < 3f && game.Target.driver.Current == TargetDriver.State.Driving,
                          $"meshes restored, impacts {game.Player.Model.ImpactCount}/{game.Target.Model.ImpactCount}, target speed {spd:0.0} km/h 3 s after reset, driver {game.Target.driver.Current}");
                }
                // back to the original stationary scenario
                game.SetTargetMode(0);
                game.ResetAll();
                Step(100, 0f, 0f);
                Check("Stationary scenario restored after switching back", game.Target.driver == null && Vector3.Distance(T.position, game.Target.startPos) < 0.1f && T.linearVelocity.magnitude < 0.1f && Vector3.Distance(new Vector3(P.position.x, 0, P.position.z), game.playerSpawn.position) < 0.1f,
                      $"target at {T.position.ToString("F1")} speed {T.linearVelocity.magnitude:0.00} m/s, player at {P.position.ToString("F1")}");

                // ================================================================ enemy encounter
                sb.AppendLine("  -- enemy encounter (muscle car player, pickup enemy) --");
                game.SelectPlayer(0);
                game.SetTargetMode((int)DrivingGame.TargetMode.Enemy);
                game.SelectTarget(2);
                game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                var E = game.Target.enemy;
                Check("ENEMY mode spawns exactly one enemy, driven by the same controller, away from the player", E != null && game.Target.controller != null && UnityEngine.Object.FindObjectsByType<EnemyDriver>(FindObjectsSortMode.None).Length == 1
                      && Vector3.Distance(game.Target.startPos, game.Player.startPos) > 40f && game.Target.def.displayName == "Compact Pickup",
                      $"enemy {game.Target.def.displayName} at {game.Target.startPos.ToString("F1")}, player at {game.Player.startPos.ToString("F1")}, distance {Vector3.Distance(game.Target.startPos, game.Player.startPos):0} m");

                // --- Test A: pursuit (player drives away east at 12 m/s)
                {
                    game.ResetAll();
                    E = game.Target.enemy;
                    Place(0f, -50f, 90f);
                    float startDist = Vector3.Distance(T.position, P.position), minAngle = 180f, dist6 = 0f;
                    Step(400, i => P.linearVelocity.magnitude < 12f ? 1f : 0f, 0f, i =>
                    {
                        Vector3 toP = P.position - T.position; toP.y = 0f; Vector3 f = T.transform.forward; f.y = 0f;
                        if (i > 100) minAngle = Mathf.Min(minAngle, Mathf.Abs(Vector3.SignedAngle(f, toP, Vector3.up)));
                        if (i == 299) dist6 = toP.magnitude;
                    });
                    Check("A. Enemy pursues a player who drives away", E.Current != EnemyDriver.State.Waiting && dist6 < startDist - 15f && minAngle < 40f,
                          $"distance {startDist:0} m at the start, {dist6:0} m after 6 s (player moving away at 43 km/h); best heading error to the player {minAngle:0} deg; state {E.Current}");
                }

                // --- Test B + E: attack a stationary player; both cars damaged through the existing engine
                {
                    game.ResetAll();
                    E = game.Target.enemy;
                    int tFrames = -1; float contactKmh = 0f; string first = "";
                    var seen = new System.Collections.Generic.HashSet<EnemyDriver.State>();
                    CollisionDamageReceiver.Report? firstP = null, firstT = null;
                    System.Action<CollisionDamageReceiver.Report> lp = r => { if (r.applied && firstP == null) firstP = r; };
                    System.Action<CollisionDamageReceiver.Report> lt = r => { if (r.applied && firstT == null) firstT = r; };
                    game.Player.Receiver.ImpactRegistered += lp; game.Target.Receiver.ImpactRegistered += lt;
                    Step(1000, 0f, 0f, i => { seen.Add(E.Current); if (tFrames < 0 && firstP != null && firstT != null) tFrames = i; });
                    game.Player.Receiver.ImpactRegistered -= lp; game.Target.Receiver.ImpactRegistered -= lt;
                    if (firstT != null) { contactKmh = firstT.Value.closingSpeed * 3.6f; first = $"enemy {firstT.Value.strength * 100f:0}% / player {firstP?.strength * 100f:0}%, closing {contactKmh:0} km/h, regions '{firstT.Value.region}' / '{firstP?.region}'"; }
                    Check("B. Enemy attacks a stationary player (commits to an attack line, does not circle forever)", tFrames >= 0 && tFrames * Dt < 14f && E.AttacksCommitted >= 1 && seen.Contains(EnemyDriver.State.Commit) && contactKmh > 30f,
                          $"first damaging hit after {tFrames * Dt:0.0} s, attacks committed {E.AttacksCommitted}, states seen {string.Join("/", seen)}; {first}");
                    Check("E. Enemy hit damages both vehicles through the existing receivers", game.Player.Model.ImpactCount > 0 && game.Target.Model.ImpactCount > 0 && firstP != null && firstT != null && MaxDisplacement(game.Player) > 0.05f && MaxDisplacement(game.Target) > 0.05f,
                          $"impact records player {game.Player.Model.ImpactCount} / enemy {game.Target.Model.ImpactCount}, deformation player {MaxDisplacement(game.Player):0.00} m / enemy {MaxDisplacement(game.Target):0.00} m, deform ms {firstP?.deformMilliseconds:0}/{firstT?.deformMilliseconds:0}");
                }

                // --- Test C: evasion. Player and enemy drive at each other along the south edge; the player swerves right when the enemy is N metres away.
                {
                    int dodgesMissed = 0, reengaged = 0, runs = 0; var rows = new System.Text.StringBuilder(); bool sawOvershoot = false;
                    foreach (float swerveAt in new[] { 32f, 26f, 21f })
                    {
                        game.ResetAll();
                        E = game.Target.enemy;
                        // enemy starts at the west end facing east, player at the east end facing west, 70 m apart (open ground, no obstacles on this line)
                        T.position = new Vector3(-40f, T.position.y, -55f); T.rotation = Quaternion.Euler(0f, 90f, 0f); T.transform.SetPositionAndRotation(T.position, T.rotation);
                        T.linearVelocity = Vector3.zero; T.angularVelocity = Vector3.zero;
                        Place(30f, -55f, 270f);
                        E.Restart();
                        bool dodged = false; int dodgeStep = -1, hitsAtDodge = 0, hitsAfterWindow = 0; float closestLater = 999f;
                        var seq = new System.Collections.Generic.List<EnemyDriver.State>(); EnemyDriver.State lastS = E.Current;
                        Step(1100, i => P.linearVelocity.magnitude < 14f ? 1f : 0f, i => (dodgeStep >= 0 && i < dodgeStep + 50) ? 1f : 0f, i =>
                        {
                            float d = E.DistanceToPlayer;
                            if (E.Current != lastS) { seq.Add(E.Current); lastS = E.Current; if (E.Current == EnemyDriver.State.Overshoot) sawOvershoot = true; }
                            if (!dodged && d < swerveAt && E.Current != EnemyDriver.State.Waiting) { dodged = true; dodgeStep = i + 1; hitsAtDodge = E.HitsLanded; }
                            if (dodged && i == dodgeStep + 100) hitsAfterWindow = E.HitsLanded;          // 2 s after the swerve began
                            if (dodged && i > dodgeStep + 100) closestLater = Mathf.Min(closestLater, d);
                        });
                        bool missed = dodged && hitsAfterWindow == hitsAtDodge;
                        bool back = dodged && (closestLater < 14f || E.HitsLanded > hitsAfterWindow);
                        runs++; if (missed) dodgesMissed++; if (back) reengaged++;
                        rows.Append($"\n      swerve at {swerveAt:0} m: {(missed ? "MISSED" : "hit")}, then closest approach {closestLater:0} m / further hits {E.HitsLanded - hitsAfterWindow}, attacks {E.AttacksCommitted}, states {string.Join(">", seq.Take(8))}");
                    }
                    sb.AppendLine("    C runs:" + rows);
                    Check("C. Evasion: a well-timed swerve makes the enemy miss, and it overshoots, recovers and comes back", dodgesMissed >= 1 && dodgesMissed < runs + 1 && reengaged >= runs - 1 && sawOvershoot,
                          $"{dodgesMissed} of {runs} swerves (at 32 / 26 / 21 m) avoided the first hit; the enemy came back for another attack in {reengaged} of {runs} runs");
                }

                // --- Test D: wall recovery
                {
                    foreach (var variant in new[] { "facing the wall 5 m away", "pressed against the wall, nose in" })
                    {
                        game.ResetAll();
                        E = game.Target.enemy;
                        var rb = T;
                        float z = variant.StartsWith("facing") ? 63f : 67.2f;
                        rb.position = new Vector3(0f, rb.position.y, z); rb.rotation = Quaternion.identity;
                        rb.transform.SetPositionAndRotation(rb.position, rb.rotation); rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                        Physics.SyncTransforms();
                        float zMax = z; int recoveries0 = E.RecoveriesTriggered;
                        Step(900, 0f, 0f, i => zMax = Mathf.Max(zMax, T.position.z));
                        float away = 70f - T.position.z;
                        Check($"D. Wall recovery ({variant})", away > 15f && Mathf.Abs(T.position.x) < 69f && !float.IsNaN(T.position.x),
                              $"after 18 s the enemy is {away:0} m from the wall (state {E.Current}, stuck recoveries {E.RecoveriesTriggered - recoveries0}, closest approach z {zMax:0.0})");
                    }
                }

                // --- Test F: player rams the enemy, which keeps working afterwards
                {
                    game.ResetAll();
                    E = game.Target.enemy;
                    E.startDelay = 100f; E.Restart();                              // hold the enemy still at its start while the player lines up the hit
                    Vector3 toE = T.position - P.position; toE.y = 0f;
                    Place(T.position.x - toE.normalized.x * 30f, T.position.z - toE.normalized.z * 30f, Quaternion.LookRotation(toE).eulerAngles.y);
                    Step(300, i => P.linearVelocity.magnitude < 18f ? 1f : 0f, 0f);
                    bool hit = game.Player.Model.ImpactCount > 0 && game.Target.Model.ImpactCount > 0;
                    E.startDelay = 1.5f; E.Restart();                              // release it: it must now drive off and fight on
                    float maxSpeed = 0f; var seen = new System.Collections.Generic.HashSet<EnemyDriver.State>();
                    Step(600, 0f, 0f, i => { maxSpeed = Mathf.Max(maxSpeed, T.linearVelocity.magnitude * 3.6f); seen.Add(E.Current); });
                    Check("F. Player rams the enemy; both damaged and the enemy still functions afterwards", hit && maxSpeed > 15f && seen.Contains(EnemyDriver.State.Pursue),
                          $"impact records player {game.Player.Model.ImpactCount} / enemy {game.Target.Model.ImpactCount}; afterwards the enemy reached {maxSpeed:0} km/h, states {string.Join("/", seen)}, enemy hits landed {E.HitsLanded}");
                }

                // --- Test G: reset repeatedly
                {
                    bool ok = true; string why = "";
                    for (int k = 0; k < 3; k++)
                    {
                        game.ResetAll();
                        Step(5, 0f, 0f);
                        int count = UnityEngine.Object.FindObjectsByType<EnemyDriver>(FindObjectsSortMode.None).Length;
                        bool clean = MaxDisplacement(game.Player) < 1e-4f && MaxDisplacement(game.Target) < 1e-4f && game.Player.Model.ImpactCount == 0 && game.Target.Model.ImpactCount == 0
                                     && Vector3.Distance(T.position, game.Target.startPos) < 0.2f && Vector3.Distance(P.position, game.Player.startPos) < 0.2f && T.linearVelocity.magnitude < 0.5f && game.Target.enemy.Current == EnemyDriver.State.Waiting
                                     && game.Target.enemy.HitsLanded == 0;
                        if (count != 1 || !clean) { ok = false; why += $" reset {k + 1}: enemies {count}, clean {clean};"; }
                        Step(400, 0f, 0f);                                          // let the enemy attack again between resets
                    }
                    Check("G. Reset x3: both vehicles restored, damage cleared, one enemy only, behaviour restarted", ok, ok ? "3 of 3 resets restored meshes, impact records, poses, velocities and the enemy's state; exactly one EnemyDriver in the scene each time" : why);
                }

                // --- Test H (selection / stationary / moving scenarios still work)
                game.SelectTarget(1);
                Check("H. Changing the enemy's vehicle works (hatchback enemy)", game.Target.enemy != null && game.Target.def.displayName == "Compact Hatchback" && UnityEngine.Object.FindObjectsByType<EnemyDriver>(FindObjectsSortMode.None).Length == 1, $"enemy is now {game.Target.def.displayName}");
                game.SelectTarget(2);
                game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                game.ResetAll();
                Step(100, 0f, 0f);
                Check("H. Switching back to the parked target restores the original stationary scenario", game.Target.enemy == null && game.Target.driver == null && UnityEngine.Object.FindObjectsByType<EnemyDriver>(FindObjectsSortMode.None).Length == 0
                      && Vector3.Distance(T.position, game.Target.startPos) < 0.1f && T.linearVelocity.magnitude < 0.1f && Vector3.Distance(new Vector3(P.position.x, 0, P.position.z), game.playerSpawn.position) < 0.1f,
                      $"target at {T.position.ToString("F1")} speed {T.linearVelocity.magnitude:0.00} m/s; no EnemyDriver left in the scene");

                // ================================================================ damage-aware colliders (the Collision Lab's VehicleHitbox, refitted by the arena)
                sb.AppendLine("  -- damage-aware colliders (muscle car player, pickup target, head-on in open ground) --");
                {
                    game.SelectPlayer(0); game.SelectTarget(2);
                    game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                    game.refitColliders = true;
                    game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                    foreach (Transform clo in game.obstacles) if (!clo.name.StartsWith("Wall")) clo.gameObject.SetActive(false);

                    float ClRetract(DrivingGame.Slot clS, string clRegion) { var f = clS.rig.hitbox.FaceOf(clRegion, 2, +1f); return f.original - f.current; }
                    float ClMax(DrivingGame.Slot clS) => clS.rig.hitbox.Faces.Max(f => f.original - f.current);
                    bool ClPristine(DrivingGame.Slot clS) => clS.rig.hitbox.Faces.All(f => f.current == f.original) && !clS.rig.hitbox.HasPending;
                    // collider front face minus front-most visible metal along the centre lane at mid height (car space, m)
                    float ClGap(DrivingGame.Slot clS)
                    {
                        var clRoot = clS.rig.root.transform; var clFc = clS.rig.hitbox.Find("FrontCentre").Outer;
                        float clY = clFc.min.y + 0.65f * clFc.size.y, clBest = float.PositiveInfinity, clCol = float.NaN, clVis = float.NegativeInfinity;
                        var clRay = new Ray(clRoot.TransformPoint(new Vector3(0f, clY, clFc.max.z + 4f)), clRoot.TransformDirection(Vector3.back));
                        foreach (var clB in clS.rig.root.GetComponentsInChildren<BoxCollider>())
                            if (clB.enabled && !clB.isTrigger && clB.Raycast(clRay, out var clH, 12f) && clH.distance < clBest) { clBest = clH.distance; clCol = clRoot.InverseTransformPoint(clH.point).z; }
                        foreach (var clPart in clS.Model.Parts)
                            for (int clI = 0; clI < clPart.VehiclePositions.Length; clI++)
                            {
                                Vector3 clV = clPart.DeformedVehiclePosition(clI);
                                if (Mathf.Abs(clV.x) < 0.08f && Mathf.Abs(clV.y - clY) < 0.1f) clVis = Mathf.Max(clVis, clV.z);
                            }
                        return clCol - clVis;
                    }

                    CollisionDamageReceiver.Report? clRP = null, clRT = null;
                    Action<CollisionDamageReceiver.Report> clHP = r => { if (r.applied && clRP == null) clRP = r; };
                    Action<CollisionDamageReceiver.Report> clHT = r => { if (r.applied && clRT == null) clRT = r; };
                    game.Player.Receiver.ImpactRegistered += clHP; game.Target.Receiver.ImpactRegistered += clHT;
                    void ClHit(float clKmh, float clOff = 0f, Action<int> clEach = null)
                    {
                        float clV = clKmh / 3.6f * 0.5f;
                        T.position = new Vector3(0f, T.position.y, 4.5f); T.rotation = Quaternion.Euler(0f, 180f, 0f); T.transform.SetPositionAndRotation(T.position, T.rotation);
                        T.linearVelocity = Vector3.back * clV; T.angularVelocity = Vector3.zero;
                        Place(clOff, -4.5f, 0f, clV);
                        game.Player.Receiver.ClearCooldowns(); game.Target.Receiver.ClearCooldowns();
                        clRP = clRT = null;
                        Step(70, 0f, 0f, clEach);
                    }

                    // --- A: undamaged cars, and the first impact is unaffected by the refit
                    game.refitColliders = false; game.ResetAll(); ClHit(100f);
                    float clZoff = clRT.Value.contactPoint.z, clSoff = clRT.Value.strength;
                    game.ResetAll(); game.refitColliders = true;
                    Step(5, 0f, 0f);
                    bool clPrist = ClPristine(game.Player) && ClPristine(game.Target);
                    float clG0P = ClGap(game.Player), clG0T = ClGap(game.Target);
                    ClHit(100f);
                    Check("Colliders A. Undamaged cars keep their original shell, and the first impact is identical with the refit on or off",
                          clPrist && Mathf.Abs(clRT.Value.contactPoint.z - clZoff) < 0.01f && Mathf.Abs(clRT.Value.strength - clSoff) < 0.01f,
                          $"undamaged shells pristine {clPrist}; first-hit contact z {clZoff:0.000} (off) vs {clRT.Value.contactPoint.z:0.000} (on), strength {clSoff * 100f:0.0}% vs {clRT.Value.strength * 100f:0.0}%");

                    // --- B: moderate frontal
                    game.ResetAll(); int clRP0 = game.Player.refits, clRT0 = game.Target.refits;
                    ClHit(60f);
                    float clBt = ClRetract(game.Target, "FrontCentre"), clBp = ClRetract(game.Player, "FrontCentre");
                    float clGapT = ClGap(game.Target) - clG0T, clGapP = ClGap(game.Player) - clG0P;
                    Check("Colliders B. Moderate frontal hit: both shells retract about as far as the dent is deep, one refit each",
                          clBt > 0.02f && clBt < 0.12f && clBp > 0.01f && clBp < 0.12f && game.Target.refits - clRT0 == 1 && game.Player.refits - clRP0 == 1 && Mathf.Abs(clGapT) < 0.04f && Mathf.Abs(clGapP) < 0.04f && !game.Target.rig.hitbox.HasPending,
                          $"front-centre box face retracted pickup {clBt * 100f:0.0} cm / muscle {clBp * 100f:0.0} cm; centre-lane gap to the metal pickup {clGapT:0.000} / muscle {clGapP:0.000} m (refit off: about the dent depth, 0.06); refits pickup {game.Target.refits - clRT0} / muscle {game.Player.refits - clRP0}");

                    // --- C: severe frontal
                    game.ResetAll();
                    ClHit(170f);
                    clBt = ClRetract(game.Target, "FrontCentre"); clBp = ClRetract(game.Player, "FrontCentre");
                    clGapT = ClGap(game.Target) - clG0T; clGapP = ClGap(game.Player) - clG0P;
                    Check("Colliders C. Severe frontal hit (170 km/h closing): the shells follow the deep dent",
                          clBt > 0.30f && clBp > 0.30f && clGapT < 0.10f && clGapP < 0.10f,
                          $"front-centre box face retracted pickup {clBt * 100f:0.0} cm / muscle {clBp * 100f:0.0} cm; centre-lane excess gap pickup {clGapT:0.000} / muscle {clGapP:0.000} m (refit off: about 0.5)");
                    float clSevereRetract = clBt;

                    // --- D: repeated hits meet the retracted shell; one refit per hit, no duplicates; cost
                    game.ResetAll(); clRP0 = game.Player.refits; clRT0 = game.Target.refits;
                    var clZ = new System.Collections.Generic.List<float>(); var clMs = new System.Collections.Generic.List<float>();
                    for (int clK = 0; clK < 3; clK++)
                    {
                        int clUpd = game.Target.rig.hitbox.UpdateCount;
                        ClHit(100f);
                        clZ.Add(clRT.Value.contactPoint.z);
                        if (game.Target.rig.hitbox.UpdateCount != clUpd) clMs.Add(game.Target.rig.hitbox.LastUpdateMilliseconds);
                    }
                    clGapT = ClGap(game.Target) - clG0T;
                    Check("Colliders D. Repeated hits: each hit meets the shell left by the last one; one refit per damaging hit",
                          clZ[0] - clZ[2] > 0.15f && clZ[1] < clZ[0] - 0.05f && game.Target.refits - clRT0 == 3 && game.Player.refits - clRP0 == 3 && clGapT < 0.10f,
                          $"struck car's contact point z {clZ[0]:0.000} -> {clZ[1]:0.000} -> {clZ[2]:0.000} (refit off: unchanged); refits pickup {game.Target.refits - clRT0} / muscle {game.Player.refits - clRP0} for 3 hits; centre-lane excess gap {clGapT:0.000} m; refit {(clMs.Count > 0 ? clMs.Average() : 0f):0.00} ms avg / {(clMs.Count > 0 ? clMs.Max() : 0f):0.00} ms max (editor)");
                    Check("Colliders J. Refit cost", clMs.Count == 3 && clMs.Max() < 10f, $"{clMs.Count} refits, {(clMs.Count > 0 ? clMs.Max() : 0f):0.00} ms max in the editor (budget 10 ms; WebGL not measured here)");

                    // --- I: two damaged cars pressed together stay stable
                    float clThP = game.Player.Receiver.energyThreshold, clThT = game.Target.Receiver.energyThreshold;
                    game.Player.Receiver.energyThreshold = game.Target.Receiver.energyThreshold = 1e12f;
                    T.position = new Vector3(0f, T.position.y, 3.5f); T.rotation = Quaternion.Euler(0f, 180f, 0f); T.transform.SetPositionAndRotation(T.position, T.rotation); T.linearVelocity = Vector3.zero; T.angularVelocity = Vector3.zero;
                    Place(0f, -3.5f, 0f);
                    float clAng = 0f, clUp = 0f; bool clNan = false;
                    Step(150, 1f, 0f, i => { clAng = Mathf.Max(clAng, P.angularVelocity.magnitude, T.angularVelocity.magnitude); clUp = Mathf.Max(clUp, Mathf.Abs(P.linearVelocity.y), Mathf.Abs(T.linearVelocity.y)); clNan |= float.IsNaN(P.position.x) || float.IsNaN(T.position.x); });
                    game.Player.Receiver.energyThreshold = clThP; game.Target.Receiver.energyThreshold = clThT;
                    Check("Colliders I. Two damaged cars pressed nose to nose (3 s, driven) stay stable", !clNan && clAng < 1f && clUp < 0.5f && T.linearVelocity.magnitude > 2f,
                          $"max angular speed {clAng:0.00} rad/s, max vertical speed {clUp:0.00} m/s, NaN {clNan}; the pickup was pushed to {T.linearVelocity.magnitude:0.0} m/s");

                    // --- F: offset impact retracts only what was hit
                    game.ResetAll(); clRT0 = game.Target.refits;
                    ClHit(100f, 0.9f);
                    float clFl = ClRetract(game.Target, "FrontLeft"), clFc = ClRetract(game.Target, "FrontCentre"), clFr = ClRetract(game.Target, "FrontRight");
                    float clRear = game.Target.rig.hitbox.Faces.Where(f => f.region.name.StartsWith("Rear")).Select(f => f.original - f.current).DefaultIfEmpty(0f).Max();
                    float clSpread = Mathf.Max(clFl, clFc, clFr) - Mathf.Min(clFl, clFc, clFr);
                    Check("Colliders F. Offset impact: only the struck side retracts, the rest of the shell is unchanged", clSpread > 0.10f && clRear < 0.005f && game.Target.refits - clRT0 == 1,
                          $"pickup front faces retracted left {clFl * 100f:0} / centre {clFc * 100f:0} / right {clFr * 100f:0} cm, rear faces {clRear * 100f:0.0} cm, {game.Target.refits - clRT0} refit");

                    // --- scheduling: not during the hit, but soon after it; and the switch works
                    game.ResetAll();
                    int clImpactStep = -1, clRefitStep = -1; int clU0 = game.Target.rig.hitbox.UpdateCount;
                    ClHit(100f, 0f, i =>
                    {
                        if (clImpactStep < 0 && clRT != null) clImpactStep = i;
                        if (clImpactStep >= 0 && clRefitStep < 0 && game.Target.rig.hitbox.UpdateCount != clU0) clRefitStep = i;
                    });
                    float clDelayS = (clRefitStep - clImpactStep) * Dt;
                    Check("Colliders scheduling: the shell is refitted only after the settle delay, never within the hit's own steps", clImpactStep >= 0 && clRefitStep > clImpactStep && clDelayS >= game.colliderSettleDelay - 0.021f && clDelayS <= game.colliderMaxDelay + 0.05f,
                          $"hit registered at step {clImpactStep}, shell refitted {clDelayS:0.00} s later (settle delay {game.colliderSettleDelay:0.00} s, cap {game.colliderMaxDelay:0.00} s)");
                    game.ResetAll(); game.refitColliders = false;
                    ClHit(170f);
                    bool clOffNo = ClMax(game.Target) == 0f && game.Target.rig.hitbox.HasPending;
                    game.refitColliders = true; Step(30, 0f, 0f);
                    Check("Colliders: with the switch off the shell stays undamaged (pending); turning it on applies the pending refit", clOffNo && !game.Target.rig.hitbox.HasPending && ClRetract(game.Target, "FrontCentre") > 0.30f,
                          $"switch off: no face moved, refit pending {clOffNo}; switched on: retraction {ClRetract(game.Target, "FrontCentre") * 100f:0} cm");

                    // --- G: reset x3 restores everything
                    bool clResetOk = true; string clWhy = "";
                    var clRetracts = new System.Collections.Generic.List<float>();
                    for (int clK = 0; clK < 3; clK++)
                    {
                        game.ResetAll(); ClHit(170f);
                        clRetracts.Add(ClRetract(game.Target, "FrontCentre"));
                        game.ResetAll(); Step(5, 0f, 0f);
                        bool clClean = ClPristine(game.Player) && ClPristine(game.Target) && MaxDisplacement(game.Player) < 1e-4f && MaxDisplacement(game.Target) < 1e-4f
                                       && Mathf.Abs(ClGap(game.Target) - clG0T) < 0.005f && Vector3.Distance(T.position, game.Target.startPos) < 0.2f && Vector3.Distance(P.position, game.Player.startPos) < 0.2f
                                       && T.linearVelocity.magnitude < 0.5f && P.linearVelocity.magnitude < 0.5f;
                        if (!clClean) { clResetOk = false; clWhy += $" reset {clK + 1} not clean;"; }
                    }
                    Check("Colliders G. Reset x3 after a severe hit: shells, meshes, poses and velocities restored; the same hit retracts the same again",
                          clResetOk && clRetracts.Max() - clRetracts.Min() < 0.03f, clResetOk ? $"3 of 3 clean; retraction of each repeat of the same hit {string.Join(" / ", clRetracts.Select(r => (r * 100f).ToString("0.0")))} cm" : clWhy);

                    // --- E: AI vehicle (the enemy) gets the same refit
                    game.SetTargetMode((int)DrivingGame.TargetMode.Enemy);
                    game.SelectTarget(2);
                    game.ResetAll();
                    Step(400, 0f, 0f, i => { });
                    var clHold = game.Target.enemy; clHold.startDelay = 1000f; clHold.Restart();      // the enemy keeps attacking; hold it so the last hit has time to be refitted
                    Step(60, 0f, 0f);
                    bool clBoth = game.Player.Model.ImpactCount > 0 && game.Target.Model.ImpactCount > 0;
                    Check("Colliders E. AI vehicle: the enemy's shell is refitted after its own attack (one refit per hit, nothing left pending)", game.Target.enemy != null && clBoth && game.Player.refits > 0 && game.Target.refits > 0 && !game.Player.rig.hitbox.HasPending && !game.Target.rig.hitbox.HasPending,
                          $"impacts player {game.Player.Model.ImpactCount} / enemy {game.Target.Model.ImpactCount}; refits since the cars were built player {game.Player.refits} / enemy {game.Target.refits}; face retraction player {ClMax(game.Player) * 100f:0} cm / enemy {ClMax(game.Target) * 100f:0} cm (the enemy only took a {game.Target.Receiver.Last.strength * 100f:0}% hit, {MaxDisplacement(game.Target):0.000} m deep and {game.Target.Receiver.Last.footprint.halfU * 2f:0.00} m wide: a dent that small and narrow leaves the median-fitted face where it was)");
                    // The enemy driver is a car like any other: when it is rammed hard (test F's set-up) its shell retracts too.
                    game.ResetAll();
                    var clEn = game.Target.enemy; clEn.startDelay = 100f; clEn.Restart();
                    Vector3 clToE = T.position - P.position; clToE.y = 0f;
                    Place(T.position.x - clToE.normalized.x * 30f, T.position.z - clToE.normalized.z * 30f, Quaternion.LookRotation(clToE).eulerAngles.y);
                    Step(300, i => P.linearVelocity.magnitude < 18f ? 1f : 0f, 0f);
                    Step(60, 0f, 0f);
                    Check("Colliders E. AI vehicle rammed by the player: the enemy's shell retracts like any other car's", game.Target.Model.ImpactCount > 0 && ClMax(game.Target) > 0.02f && !game.Target.rig.hitbox.HasPending && !game.Player.rig.hitbox.HasPending,
                          $"enemy hit strength {game.Target.Receiver.Last.strength * 100f:0}%, deepest dent {MaxDisplacement(game.Target):0.000} m, face retraction enemy {ClMax(game.Target) * 100f:0} cm / player {ClMax(game.Player) * 100f:0} cm");
                    clEn.startDelay = 1.5f; clEn.Restart();
                    game.Player.Receiver.ImpactRegistered -= clHP;
                    game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                    game.ResetAll();
                    Step(5, 0f, 0f);
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
