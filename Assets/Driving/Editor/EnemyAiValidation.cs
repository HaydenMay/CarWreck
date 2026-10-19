using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Enemy AI V2 checks (brief tests A-O). Real PhysX in the DriveArena, stepped by script (Physics.Simulate at 50 Hz): the enemy pickup is driven by
    /// <see cref="EnemyDriver"/> through its own controller, the player by a small scripted driver. Each scenario runs once per driver profile
    /// (Brawler, Hunter, Cautious) and records what the enemy actually did (states, charges, back-offs, hits, closing speeds, detours).
    /// Editor timings; arena physics is not repeatable run to run in the editor, so checks use margins, not exact counts.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Driving.EditorTools.EnemyAiValidation.Run -quit -logFile ai.log
    /// </summary>
    public static class EnemyAiValidation
    {
        const float Dt = 0.02f;
        static readonly string[] ProfileNames = { "Brawler", "Hunter", "Cautious" };

        [MenuItem("CarWreck/Validate Enemy AI", priority = 14)]
        public static void Run() => Debug.Log(Execute());

        static DrivingGame game;
        static StringBuilder sb;
        static int fails;
        static Rigidbody P => game.Player.Body;
        static Rigidbody T => game.Target.Body;
        static EnemyDriver E => game.Target.enemy;
        static ArcadeCarController C => game.Player.controller;

        static void Check(string name, bool ok, string detail) { sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}"); if (!ok) fails++; }

        // ------------------------------------------------------------------ run metrics

        class Metrics
        {
            public string label;
            public float duration;
            public float firstChargeT = -1f, firstHitT = -1f;
            public int charges, repositions, hits, obstacleHits, stuck, detours, transitions, shortCommits, shortReps, flips, recoveries;
            public float minDist = 999f, sumDist, farTime, closeTime, maxClosingAtHit;
            public readonly List<float> chargeAngles = new List<float>(), chargeSpeeds = new List<float>(), chargeDists = new List<float>(), hitClosings = new List<float>();
            public readonly List<string> timeline = new List<string>();
            public float driveMsAvg, driveMsMax, allocBytes; public int driveCalls;
            public EnemyDriver.State end;
            public float MeanDist => duration > 0f ? sumDist / (duration / Dt) : 0f;
            public override string ToString() =>
                $"{label}: first charge {(firstChargeT < 0 ? "none" : firstChargeT.ToString("0.0") + " s")}, charges {charges}, back-offs {repositions}, hits on player {hits} (first {(firstHitT < 0 ? "none" : firstHitT.ToString("0.0") + " s")}, " +
                $"closing at hits {(hitClosings.Count == 0 ? "-" : string.Join("/", hitClosings.Select(c => (c * 3.6f).ToString("0"))))} km/h), wall/block hits {obstacleHits}, stuck recoveries {stuck}, detours {detours}, " +
                $"closest {minDist:0.0} m, mean distance {MeanDist:0} m, time > 45 m {farTime:0.0} s, state changes {transitions}";
        }

        public static int TraceEvery = 0; public static float TraceFrom = 0f, TraceTo = 999f;
        static EnemyDriver.State lastState;
        static float stateSince;

        /// <summary>Run <paramref name="seconds"/> of the scenario. <paramref name="playerDrive"/> sets the player's controller each step.</summary>
        static Metrics Simulate(string label, float seconds, Action<int, float> playerDrive, Action<Metrics, int> each = null)
        {
            var m = new Metrics { label = label, duration = seconds };
            int steps = Mathf.RoundToInt(seconds / Dt);
            lastState = E.Current; stateSince = 0f;
            int chargesSeen = 0, repsSeen = 0, hitsSeen = 0;
            var sw = new Stopwatch(); double driveMs = 0; double maxMs = 0; long alloc = 0;
            EnemyDriver.State prev = lastState; float lastRepEnd = -99f; bool wasRep = false;
            for (int i = 0; i < steps; i++)
            {
                float t = i * Dt;
                playerDrive?.Invoke(i, t);
                C.Tick(Dt);
                long a0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                sw.Restart(); E.Drive(Dt); sw.Stop();
                long a1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                if (a1 > a0) alloc += a1 - a0;
                driveMs += sw.Elapsed.TotalMilliseconds; maxMs = Math.Max(maxMs, sw.Elapsed.TotalMilliseconds); m.driveCalls++;
                game.Target.controller.Tick(Dt);
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt);
                game.AfterPhysicsStep(Dt);
                float d = E.DistanceToPlayer;
                m.minDist = Mathf.Min(m.minDist, d); m.sumDist += d; if (d > 45f) m.farTime += Dt; if (d < 9f) m.closeTime += Dt;
                var s = E.Current;
                if (s != prev)
                {
                    m.transitions++;
                    if (m.timeline.Count < 40) m.timeline.Add($"{t:0.0}s {s}");
                    if (prev == EnemyDriver.State.Commit && t - stateSince < E.minCommitTime - 0.05f && E.HitsLanded == hitsSeen && E.ObstacleHits == m.obstacleHits) m.shortCommits++;
                    if (prev == EnemyDriver.State.Reposition && t - stateSince < 0.4f) m.shortReps++;
                    if (prev == EnemyDriver.State.Reposition) { wasRep = true; lastRepEnd = t; }
                    if (s == EnemyDriver.State.Reposition && wasRep && t - lastRepEnd < 1.0f) m.flips++;
                    prev = s; stateSince = t;
                }
                if (E.AttacksCommitted != chargesSeen)
                {
                    chargesSeen = E.AttacksCommitted;
                    if (m.firstChargeT < 0f) m.firstChargeT = t;
                    Vector3 f = T.transform.forward; f.y = 0f; Vector3 to = P.position - T.position; to.y = 0f;
                    m.chargeAngles.Add(Mathf.Abs(Vector3.SignedAngle(f, to, Vector3.up))); m.chargeSpeeds.Add(E.LastChargeSpeed); m.chargeDists.Add(to.magnitude);
                }
                if (E.Repositions != repsSeen) repsSeen = E.Repositions;
                if (E.HitsLanded != hitsSeen)
                {
                    hitsSeen = E.HitsLanded;
                    if (m.firstHitT < 0f) m.firstHitT = t;
                    m.hitClosings.Add(E.LastHitClosingSpeed); m.maxClosingAtHit = Mathf.Max(m.maxClosingAtHit, E.LastHitClosingSpeed);
                }
                if (TraceEvery > 0 && i % TraceEvery == 0 && t >= TraceFrom && t < TraceTo) { Vector3 f0 = T.transform.forward; f0.y = 0f; Vector3 to0 = P.position - T.position; to0.y = 0f; sb.AppendLine($"        t {t:0.0} {E.Current,-10} dist {d:0.0} angle {Vector3.SignedAngle(f0, to0, Vector3.up):0} v {game.Target.controller.ForwardSpeed:0.0} pv {P.linearVelocity.magnitude:0.0} pos ({T.position.x:0},{T.position.z:0}) player ({P.position.x:0},{P.position.z:0}) detour {E.Detouring} up {T.transform.up.y:0.00} stuck {E.RecoveriesTriggered} steerIn {game.Target.controller.SteerInput:0.00} steerSt {game.Target.controller.Steering:0.00} thr {game.Target.controller.Throttle:0.00} yawRate {T.angularVelocity.y * Mathf.Rad2Deg:0}"); }
                each?.Invoke(m, i);
            }
            m.charges = E.AttacksCommitted; m.repositions = E.Repositions; m.hits = E.HitsLanded; m.obstacleHits = E.ObstacleHits; m.stuck = E.RecoveriesTriggered; m.detours = E.DetoursPlanned;
            m.end = E.Current;
            m.driveMsAvg = (float)(driveMs / Math.Max(1, m.driveCalls)); m.driveMsMax = (float)maxMs; m.allocBytes = alloc;
            return m;
        }

        // ------------------------------------------------------------------ scenario set-up

        static void Place(Rigidbody b, Vector3 pos, float yaw, float speed)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
            b.transform.SetPositionAndRotation(b.position, rot);
            b.linearVelocity = rot * Vector3.forward * speed; b.angularVelocity = Vector3.zero;
        }

        static void Begin(int profile, float startDelay = 1.5f)
        {
            game.enemyProfileIndex = profile;
            game.ResetAll();
            var v3 = Environment.GetEnvironmentVariable("EAI_V3");                       // Casual / Standard / Competitive: run every scenario with AI V3 at that difficulty
            E.v3Enabled = !string.IsNullOrEmpty(v3);
            if (E.v3Enabled) E.difficulty = v3 == "Casual" ? AiDifficulty.Casual : v3 == "Competitive" ? AiDifficulty.Competitive : AiDifficulty.Standard;
            if (int.TryParse(Environment.GetEnvironmentVariable("EAI_MASK"), out int eaiMask)) E.v3Mask = eaiMask;
            E.SetProfile(game.EnemyProfile);
            E.startDelay = startDelay;
            E.obstacleAwareness = true;
            E.Restart();
            game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
        }

        static void Settle(float yawP, Vector3 pPos, float pSpeed, Vector3 ePos, float yawE, float eSpeed)
        {
            Place(P, pPos, yawP, pSpeed); Place(T, ePos, yawE, eSpeed);
            Physics.SyncTransforms();
            C.ResetState(); game.Target.controller.ResetState();
            game.Player.Receiver.ClearCooldowns(); game.Target.Receiver.ClearCooldowns();
        }

        /// <summary>Player that holds a speed along its heading (open-loop steering 0).</summary>
        static Action<int, float> HoldSpeed(float speed) => (i, t) => { C.Throttle = C.ForwardSpeed < speed ? 1f : 0f; C.SteerInput = 0f; };

        static Action<int, float> Idle() => (i, t) => { C.Throttle = 0f; C.SteerInput = 0f; };

        /// <summary>Player that drives a loop of waypoints at a steady speed (pure pursuit).</summary>
        static Action<int, float> Loop(Vector3[] pts, float speed)
        {
            int k = 0;
            return (i, t) =>
            {
                Vector3 to = pts[k] - P.position; to.y = 0f;
                if (to.magnitude < 8f) { k = (k + 1) % pts.Length; to = pts[k] - P.position; to.y = 0f; }
                Vector3 f = P.transform.forward; f.y = 0f;
                float ang = Vector3.SignedAngle(f, to, Vector3.up);
                C.SteerInput = Mathf.Clamp(ang / 30f, -1f, 1f);
                C.Throttle = C.ForwardSpeed < speed ? 1f : 0f;
            };
        }

        static void SetTrace(string scenario, int profile)
        {
            TraceEvery = 0;
            var v = Environment.GetEnvironmentVariable("EAI_TRACE");   // "scenario,profile,from,to,everySteps"
            if (string.IsNullOrEmpty(v)) return;
            var a = v.Split(',');
            if (a[0] != scenario || int.Parse(a[1]) != profile) return;
            TraceFrom = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture); TraceTo = float.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture); TraceEvery = int.Parse(a[4]);
        }

        /// <summary>Several runs of the same profile as one set of numbers (counts add up, means are over all steps).</summary>
        static Metrics Merge(string label, List<Metrics> runs)
        {
            var m = new Metrics { label = label + " (" + runs.Count + " runs)", duration = runs.Sum(r => r.duration), minDist = runs.Min(r => r.minDist), sumDist = runs.Sum(r => r.sumDist), farTime = runs.Sum(r => r.farTime), closeTime = runs.Sum(r => r.closeTime) };
            m.charges = runs.Sum(r => r.charges); m.repositions = runs.Sum(r => r.repositions); m.hits = runs.Sum(r => r.hits); m.obstacleHits = runs.Sum(r => r.obstacleHits); m.stuck = runs.Sum(r => r.stuck);
            m.detours = runs.Sum(r => r.detours); m.transitions = runs.Sum(r => r.transitions); m.shortCommits = runs.Sum(r => r.shortCommits); m.shortReps = runs.Sum(r => r.shortReps); m.flips = runs.Sum(r => r.flips);
            var charged = runs.Where(r => r.firstChargeT >= 0f).ToList(); m.firstChargeT = charged.Count > 0 ? charged.Average(r => r.firstChargeT) : -1f;
            var hit = runs.Where(r => r.firstHitT >= 0f).ToList(); m.firstHitT = hit.Count > 0 ? hit.Average(r => r.firstHitT) : -1f;
            m.maxClosingAtHit = runs.Max(r => r.maxClosingAtHit);
            foreach (var r in runs) { m.chargeAngles.AddRange(r.chargeAngles); m.chargeSpeeds.AddRange(r.chargeSpeeds); m.chargeDists.AddRange(r.chargeDists); m.hitClosings.AddRange(r.hitClosings); }
            m.driveMsAvg = runs.Average(r => r.driveMsAvg); m.driveMsMax = runs.Max(r => r.driveMsMax); m.allocBytes = runs.Sum(r => r.allocBytes); m.driveCalls = runs.Sum(r => r.driveCalls); m.end = runs[runs.Count - 1].end;
            return m;
        }

        static string Fmt(IEnumerable<float> v, string f = "0") => v.Any() ? string.Join("/", v.Select(x => x.ToString(f))) : "-";

        // ------------------------------------------------------------------ the checks

        static string Execute()
        {
            sb = new StringBuilder("[EnemyAI] Validation V2 (real PhysX, Physics.Simulate at 50 Hz; editor timings)\n");
            fails = 0;
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                game.Setup();
                foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);   // open ground unless a test turns obstacles on
                game.SelectPlayer(0); game.SelectTarget(2);
                game.SetTargetMode((int)DrivingGame.TargetMode.Enemy);
                game.ResetAll();
                game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                var profiles = game.DriverProfiles;
                sb.AppendLine("  profiles: " + string.Join(", ", profiles.Select(p => $"{p.displayName} (aggression {p.aggression:0.00}, skill {p.skill:0.00})")));
                Check("Three driver profiles exist and are independent of the vehicle", profiles.Length >= 3 && profiles.Select(p => p.displayName).Distinct().Count() >= 3 && ProfileNames.All(n => profiles.Any(p => p.displayName == n)),
                      $"{profiles.Length} profiles; the same pickup is driven by each below");

                // Warm-up (not measured): the first calls of every method include Mono JIT time, which is not the cost of the AI.
                foreach (Transform o in game.obstacles) if (o.name.StartsWith("CrashWall")) o.gameObject.SetActive(true);
                Begin(0, 0.3f); Settle(0f, new Vector3(0f, 0f, 36f), 0f, new Vector3(0f, 0f, 2f), 0f, 0f); Simulate("warm-up", 8f, Idle());
                foreach (Transform o in game.obstacles) if (o.name.StartsWith("CrashWall")) o.gameObject.SetActive(false);
                var stationary = new Metrics[3]; var tailgate = new Metrics[3]; var moving = new Metrics[3];
                // ---- E/I/J/K: a stationary player in open ground, 30 s per profile, from three directions
                sb.AppendLine("  -- stationary player in open ground, 30 s, enemy starting from three directions (30,-45), (-35,40), (50,10) --");
                var statStarts = new[] { (new Vector3(30f, 0f, -45f), 326f), (new Vector3(-35f, 0f, 40f), 142f), (new Vector3(50f, 0f, 10f), 258f) };
                for (int p = 0; p < 3; p++)
                {
                    var runs = new List<Metrics>();
                    for (int k = 0; k < statStarts.Length; k++)
                    {
                        Begin(p);
                        Settle(0f, new Vector3(0f, 0f, 0f), 0f, statStarts[k].Item1, statStarts[k].Item2, 0f);
                        SetTrace("stationary" + k, p); runs.Add(Simulate(ProfileNames[p], 30f, Idle()));
                        sb.AppendLine($"    start {k}: {runs[k]}");
                    }
                    stationary[p] = Merge(ProfileNames[p], runs);
                    sb.AppendLine($"    TOTAL {stationary[p]}");
                    sb.AppendLine($"      charges: align {Fmt(stationary[p].chargeAngles)} deg, own speed {Fmt(stationary[p].chargeSpeeds.Select(v => v * 3.6f))} km/h, distance {Fmt(stationary[p].chargeDists)} m");
                }
                // ---- D: tailgating a driving player
                sb.AppendLine("  -- tailgating: the player drives straight at 43 km/h, the enemy starts 7 m behind it --");
                for (int p = 0; p < 3; p++)
                {
                    Begin(p, 0.1f);
                    Settle(0f, new Vector3(0f, 0f, -45f), 12f, new Vector3(0f, 0f, -52f), 0f, 12f);
                    SetTrace("tailgate", p); tailgate[p] = Simulate(ProfileNames[p], 8f, HoldSpeed(12f));
                    sb.AppendLine("    " + tailgate[p]);
                    sb.AppendLine($"      timeline {string.Join(", ", tailgate[p].timeline.Take(14))}");
                }
                // ---- A/G: a moving player on a loop (both directions), 30 s each
                sb.AppendLine("  -- moving player on a loop at 43 km/h, both directions, 30 s each --");
                var loopA = new[] { new Vector3(-40f, 0f, -30f), new Vector3(40f, 0f, -30f), new Vector3(40f, 0f, 40f), new Vector3(-40f, 0f, 40f) };
                var loopB = loopA.Reverse().ToArray();
                for (int p = 0; p < 3; p++)
                {
                    var runs = new List<Metrics>();
                    for (int k = 0; k < 2; k++)
                    {
                        Begin(p);
                        if (k == 0) Settle(90f, new Vector3(-40f, 0f, -30f), 12f, new Vector3(-18f, 0f, 8f), 162.8f, 0f);
                        else Settle(0f, new Vector3(-40f, 0f, -30f), 12f, new Vector3(40f, 0f, 20f), 250f, 0f);
                        SetTrace("moving" + k, p); runs.Add(Simulate(ProfileNames[p], 30f, Loop(k == 0 ? loopA : loopB, 12f)));
                        sb.AppendLine($"    loop {k}: {runs[k]}");
                    }
                    moving[p] = Merge(ProfileNames[p], runs);
                    sb.AppendLine($"    TOTAL {moving[p]}");
                    sb.AppendLine($"      charges: align {Fmt(moving[p].chargeAngles)} deg, distance {Fmt(moving[p].chargeDists)} m");
                }

                var all = new List<Metrics>(); all.AddRange(stationary); all.AddRange(tailgate); all.AddRange(moving);

                // ---- B: obstacle recovery (the V1 wall tests): the enemy reverses / turns away from the arena wall and gets going again
                sb.AppendLine("  -- wall recovery (enemy 5 m from the wall facing it, and pressed against it nose in), 18 s --");
                var wallAway = new float[3, 2]; var wallStuck = new int[3, 2];
                for (int p = 0; p < 3; p++)
                    for (int v = 0; v < 2; v++)
                    {
                        Begin(p, 0.1f);
                        Settle(0f, new Vector3(0f, 0f, -40f), 0f, new Vector3(0f, 0f, v == 0 ? 63f : 67.2f), 0f, 0f);
                        var m = Simulate(ProfileNames[p] + (v == 0 ? " facing the wall" : " nose in"), 18f, Idle());
                        all.Add(m);
                        wallAway[p, v] = 70f - T.position.z; wallStuck[p, v] = m.stuck;
                        sb.AppendLine($"    {m.label}: {wallAway[p, v]:0} m from the wall after 18 s, stuck recoveries {m.stuck}, end state {m.end}");
                    }

                // ---- C: obstructed player: the player waits behind the hazard wall (a U of concrete open towards the enemy), the enemy starts in front of it
                sb.AppendLine("  -- obstructed player: behind the 16 m crash wall with wings, 25 s (obstacle awareness on / off) --");
                foreach (Transform o in game.obstacles) if (o.name.StartsWith("CrashWall")) o.gameObject.SetActive(true);
                var obs = new Metrics[2, 3];
                for (int aware = 1; aware >= 0; aware--)
                    for (int p = 0; p < 3; p++)
                    {
                        Begin(p, 0.3f);
                        E.obstacleAwareness = aware == 1;
                        Settle(0f, new Vector3(0f, 0f, 36f), 0f, new Vector3(0f, 0f, 2f), 0f, 0f);
                        SetTrace("obstructed" + aware, p);
                        obs[aware, p] = Simulate(ProfileNames[p] + (aware == 1 ? " (aware)" : " (V1-style, no probing)"), 25f, Idle());
                        sb.AppendLine("    " + obs[aware, p]);
                        sb.AppendLine($"      timeline {string.Join(", ", obs[aware, p].timeline.Take(16))}");
                        if (aware == 1) all.Add(obs[aware, p]);
                    }
                foreach (Transform o in game.obstacles) if (o.name.StartsWith("CrashWall")) o.gameObject.SetActive(false);

                // ---- F: a missed attack: head-on along the south edge; the player swerves right when the enemy commits (0 s later and 0.2 s later), then drives on
                sb.AppendLine("  -- missed attack: the player sits still, then drives off to the right as the enemy commits (and 0.2 s later), 22 s --");
                var miss = new Metrics[3, 2]; var missAvoided = new bool[3, 2];
                for (int p = 0; p < 3; p++)
                    for (int dl = 0; dl < 2; dl++)
                    {
                        Begin(p, 0.3f);
                        Settle(0f, new Vector3(0f, 0f, 0f), 0f, new Vector3(30f, 0f, -45f), 326f, 0f);
                        int swerve = -1; int swerveDist = 0;
                        miss[p, dl] = Simulate(ProfileNames[p] + (dl == 0 ? " (swerve at commit)" : " (swerve 0.2 s after commit)"), 22f, (i, t) =>
                        {
                            if (swerve < 0 && E.Current == EnemyDriver.State.Commit) { swerve = i + dl * 10; swerveDist = Mathf.RoundToInt(E.DistanceToPlayer); }
                            C.Throttle = swerve >= 0 && i >= swerve ? 1f : 0f;                      // the player sits still, then drives off to the right as the enemy commits
                            C.SteerInput = swerve >= 0 && i >= swerve && i < swerve + 60 ? 1f : 0f;
                        });
                        var mm = miss[p, dl];
                        missAvoided[p, dl] = swerve >= 0 && (mm.firstHitT < 0f || mm.firstHitT > (swerve + 80) * Dt);
                        sb.AppendLine("    " + mm + $"; swerve began at {swerveDist} m, first attack avoided {missAvoided[p, dl]}");
                        all.Add(mm);
                    }

                // ---- L: reset
                sb.AppendLine("  -- reset --");
                Begin(1, 0.5f);
                Settle(0f, new Vector3(0f, 0f, 0f), 0f, new Vector3(30f, 0f, -45f), 326f, 0f);
                var runA = Simulate("run A", 7f, Idle());
                string seqA = string.Join(">", runA.timeline.Take(5).Select(s => s.Substring(s.IndexOf(' ') + 1)));
                var dirty = (E.AttacksCommitted, E.HitsLanded, E.StateChanges, E.Current);
                game.ResetAll();
                bool clean = E.Current == EnemyDriver.State.Waiting && E.AttacksCommitted == 0 && E.HitsLanded == 0 && E.ObstacleHits == 0 && E.Repositions == 0 && E.DetoursPlanned == 0 && E.RecoveriesTriggered == 0 && !E.Detouring
                             && game.Target.controller.Throttle == 0f && game.Target.controller.SteerInput == 0f && T.linearVelocity.magnitude < 0.1f && E.StateChanges == 0;
                E.startDelay = 0.5f; E.Restart();
                Settle(0f, new Vector3(0f, 0f, 0f), 0f, new Vector3(30f, 0f, -45f), 326f, 0f);
                var runB = Simulate("run B", 7f, Idle());
                string seqB = string.Join(">", runB.timeline.Take(5).Select(s => s.Substring(s.IndexOf(' ') + 1)));
                sb.AppendLine($"    before the reset: charges {dirty.Item1}, hits {dirty.Item2}, state {dirty.Item4}; after: clean {clean}; run A {seqA}; run B {seqB}");

                // ---- O: cost of the decisions
                float avgMs = all.Average(m => m.driveMsAvg), maxMs = all.Max(m => m.driveMsMax); float allocPerCall = (float)all.Sum(m => m.allocBytes) / Math.Max(1, all.Sum(m => m.driveCalls));
                sb.AppendLine($"  -- cost of E.Drive (editor, Mono, {all.Sum(m => m.driveCalls)} calls): average {avgMs * 1000f:0} us, worst call {maxMs:0.00} ms, managed allocation {allocPerCall:0.0} bytes per call; detour plans {obs[1, 0].detours + obs[1, 1].detours + obs[1, 2].detours} --");

                Report(stationary, tailgate, moving, wallAway, wallStuck, obs, miss, missAvoided, clean, seqA, seqB, all, avgMs, maxMs, allocPerCall);
            }
            catch (Exception e)
            {
                fails++;
                sb.AppendLine($"  FAIL  exception: {e}");
            }
            finally
            {
                game.ResetAll();
                Physics.simulationMode = prev;
                game.Player.Model.Shutdown();
                game.Target.Model.Shutdown();
                EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);   // discard edit-mode changes
            }
            sb.AppendLine(fails == 0 ? "[EnemyAI] ALL CHECKS PASSED" : $"[EnemyAI] {fails} CHECK(S) FAILED");
            return sb.ToString();
        }

        static void Report(Metrics[] st, Metrics[] tg, Metrics[] mv, float[,] wallAway, int[,] wallStuck, Metrics[,] obs, Metrics[,] miss, bool[,] avoided, bool resetClean, string seqA, string seqB, List<Metrics> all, float avgMs, float maxMs, float allocPerCall)
        {
            const int B = 0, H = 1, Ca = 2;
            // A. basic pursuit: the enemy stays with a moving player
            Check("A. Basic pursuit: the enemy stays with a player driving a loop at 43 km/h", mv.All(m => m.MeanDist < 35f && m.farTime / m.duration < 0.15f && m.minDist < 9f),
                  string.Join("; ", mv.Select(m => $"{m.label} mean distance {m.MeanDist:0} m, closest {m.minDist:0.0} m, time > 45 m {m.farTime:0.0} s of {m.duration:0}")));
            // B. obstacle recovery
            bool wallOk = true; for (int p = 0; p < 3; p++) for (int v = 0; v < 2; v++) wallOk &= wallAway[p, v] > 15f;
            Check("B. Obstacle recovery: from the wall (facing it / nose in) every profile reverses or turns away and drives off", wallOk,
                  string.Join("; ", Enumerable.Range(0, 3).Select(p => $"{ProfileNames[p]} {wallAway[p, 0]:0} / {wallAway[p, 1]:0} m away (stuck recoveries {wallStuck[p, 0]} / {wallStuck[p, 1]})")));
            // C. obstructed player
            bool cOk = true; var cd = new List<string>();
            for (int p = 0; p < 3; p++)
            {
                var a = obs[1, p]; var b = obs[0, p];
                bool ok = a.minDist < 9f && a.detours >= 1 && a.obstacleHits <= 1 && a.stuck <= 2;
                cOk &= ok;
                cd.Add($"{ProfileNames[p]}: aware reached {a.minDist:0.0} m of the player, detours {a.detours}, wall hits {a.obstacleHits}, stuck {a.stuck} | without probing: closest {b.minDist:0.0} m, wall hits {b.obstacleHits}, stuck {b.stuck}");
            }
            Check("C. Obstructed player: the enemy goes round the wall instead of ramming it repeatedly", cOk, string.Join("; ", cd));
            // D. close-range positioning
            bool dOk = tg.All(m => m.repositions >= 1 && m.closeTime < 5f);
            Check("D. Close-range positioning: a tailgating enemy backs off to make a run-up instead of following the bumper", dOk,
                  string.Join("; ", tg.Select(m => $"{m.label}: back-offs {m.repositions}, time within 9 m {m.closeTime:0.0} s of 8, hits {m.hits}" + (m.hits > 0 ? $" at {m.hitClosings.Max() * 3.6f:0} km/h" : ""))));
            // E. charge attack
            bool eOk = st.All(m => m.firstChargeT >= 0f && m.hits >= 1 && m.maxClosingAtHit >= 11f);
            Check("E. Charge attack: every profile lines up, commits and lands a meaningful ram (>= 40 km/h closing) on a stationary player", eOk,
                  string.Join("; ", st.Select(m => $"{m.label}: first charge {m.firstChargeT:0.0} s, best hit {m.maxClosingAtHit * 3.6f:0} km/h")));
            // F. missed attack
            int dodged = 0; for (int p = 0; p < 3; p++) for (int dl = 0; dl < 2; dl++) if (avoided[p, dl]) dodged++;
            bool fOk = dodged >= 2;
            for (int p = 0; p < 3; p++) for (int dl = 0; dl < 2; dl++) if (avoided[p, dl]) fOk &= miss[p, dl].charges >= 2 && miss[p, dl].end != EnemyDriver.State.Waiting;   // a miss is followed by another attack
            Check("F. Missed attack: a late swerve can make the charge miss, and the enemy then recovers and attacks again", fOk,
                  string.Join("; ", Enumerable.Range(0, 3).Select(p => $"{ProfileNames[p]}: avoided {(avoided[p, 0] ? "yes" : "no")} / {(avoided[p, 1] ? "yes" : "no")}, charges {miss[p, 0].charges} / {miss[p, 1].charges}")) + $" (drive-off at commit / 0.2 s later); {dodged} of 6 avoided");
            // G. moving target: some hits, some misses (no perfect prediction)
            int mc = mv.Sum(m => m.charges), mh = mv.Sum(m => m.hits);
            Check("G. Moving target: the enemy leads the player and hits it, but not every charge connects", mh >= 2 && mc - mh >= 2, $"{mc} charges, {mh} hits across the three profiles in 30 s each (hits per profile {string.Join("/", mv.Select(m => m.hits))})");
            // H. commitment
            int shortCommits = all.Sum(m => m.shortCommits), shortReps = all.Sum(m => m.shortReps), flips = all.Sum(m => m.flips);
            Check("H. Charge commitment: no charge is abandoned early and no back-off is dropped at once or repeated back to back", shortCommits == 0 && shortReps == 0 && flips == 0,
                  $"charges ended before 0.8 s without a hit: {shortCommits}; back-offs shorter than 0.4 s: {shortReps}; back-off -> back-off within 1 s: {flips}");
            // I. Brawler
            Check("I. Brawler: attacks more often and backs off less than Cautious", st[B].charges >= st[Ca].charges && st[B].repositions <= st[Ca].repositions && mv[B].charges >= mv[Ca].charges,
                  $"stationary charges {st[B].charges} vs {st[Ca].charges}, back-offs {st[B].repositions} vs {st[Ca].repositions}; moving charges {mv[B].charges} vs {mv[Ca].charges}; first charge moving {mv[B].firstChargeT:0.0} s vs {mv[Ca].firstChargeT:0.0} s");
            // J. Hunter
            float Mean(IEnumerable<float> v) => v.Any() ? v.Average() : 0f;
            float hAlign = Mean(st[H].chargeAngles.Concat(mv[H].chargeAngles)), bAlign = Mean(st[B].chargeAngles.Concat(mv[B].chargeAngles));
            float hHit = Mean(st[H].hitClosings.Concat(mv[H].hitClosings)), bHit = Mean(st[B].hitClosings.Concat(mv[B].hitClosings));
            float hRate = (float)(st[H].hits + mv[H].hits) / Math.Max(1, st[H].charges + mv[H].charges), bRate = (float)(st[B].hits + mv[B].hits) / Math.Max(1, st[B].charges + mv[B].charges);
            Check("J. Hunter: lines up more precisely and lands more of its charges than Brawler", hAlign <= bAlign + 0.5f && hRate >= bRate - 0.05f,
                  $"mean angle off the target at the start of a charge {hAlign:0.0} deg vs Brawler {bAlign:0.0}; hits per charge {hRate:0.00} vs {bRate:0.00}; mean closing speed at hits {hHit * 3.6f:0} vs {bHit * 3.6f:0} km/h");
            // K. Cautious
            Check("K. Cautious: attacks less often than Brawler, but still engages and does not flee", st[Ca].charges >= 1 && mv[Ca].charges >= 1 && st[Ca].hits >= 1 && st[Ca].MeanDist < 35f && mv[Ca].MeanDist < 35f && st[Ca].farTime / st[Ca].duration < 0.15f && mv[Ca].farTime / mv[Ca].duration < 0.15f,
                  $"stationary: charges {st[Ca].charges}, hits {st[Ca].hits}, back-offs {st[Ca].repositions}, mean distance {st[Ca].MeanDist:0} m; moving: charges {mv[Ca].charges}, hits {mv[Ca].hits}, mean distance {mv[Ca].MeanDist:0} m");
            // L. reset
            Check("L. Reset: combat state is cleared and the enemy starts over the same way", resetClean && seqA == seqB, $"clean {resetClean}; first states run A {seqA}, run B {seqB}");
            // O. cost
            Check("O. Decision cost: no significant time or allocation per call", avgMs < 0.3f && maxMs < 3f && allocPerCall < 64f, $"average {avgMs * 1000f:0} us, worst {maxMs:0.00} ms (editor; slowest runs: {string.Join(", ", all.OrderByDescending(m => m.driveMsMax).Take(3).Select(m => m.label + " " + m.driveMsMax.ToString("0.00")))}), {allocPerCall:0.0} bytes per call");
        }
    }
}
