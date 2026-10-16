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

        static void Step(int steps, Func<int, float> throttle, float steer, Action<int> each = null)
        {
            for (int i = 0; i < steps; i++)
            {
                C.Throttle = throttle(i);
                C.SteerInput = steer;
                C.Tick(Dt);
                if (game.Target.controller != null) { game.Target.driver?.Drive(); game.Target.controller.Tick(Dt); }   // moving target: its driver feeds its own controller
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
