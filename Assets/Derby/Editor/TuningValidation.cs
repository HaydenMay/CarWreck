using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CarWreck.Driving;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Gameplay tuning V1 checks: pickup handling against the other vehicles, AI cluster detection / breakaway / re-engagement per driver profile,
    /// and before/after metrics (breakaway switched off = the previous AI). Real PhysX in the DerbyArena, stepped at 50 Hz.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.TuningValidation.Run -quit -logFile tuning.log
    /// Environment TUNING_ONLY = handling | ai | ffa | perf runs one part.
    /// </summary>
    public static class TuningValidation
    {
        const float Dt = 0.02f;
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Gameplay Tuning", priority = 22)]
        public static void Run() => Debug.Log(Execute());

        static void Check(string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[Tuning] Validation (DerbyArena, real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            string only = Environment.GetEnvironmentVariable("TUNING_ONLY") ?? "";
            var prev = Physics.simulationMode;
            try
            {
                if (only == "" || only == "handling") HandlingChecks();
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); game.ui = null;
                game.aiV3 = Environment.GetEnvironmentVariable("TUNING_V3") == "1";   // these are the tuning V1 checks: they run with the previous AI settings unless TUNING_V3=1 (AI V3 has its own validation)
                game.difficulty = AiDifficulty.Standard;
                if (only == "" || only == "pickup") PickupChecks();
                if (only == "" || only == "ai") AiChecks();
                if (only == "" || only == "ffa") FfaChecks();
                if (only == "" || only == "melee") MeleeChecks();
                if (only == "" || only == "perf") PerfChecks();
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); if (game != null) game.aiV3 = true; }
            sb.AppendLine($"[Tuning] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ==================================================================== handling (A-E)

        static void HandlingChecks()
        {
            sb.AppendLine("  -- vehicle handling (HandlingProbe: flat ground, full lock, speed held) --");
            var r = CarWreck.Driving.EditorTools.HandlingProbe.Measure();
            var mu = r.First(x => x.name.Contains("Muscle")); var ha = r.First(x => x.name.Contains("Hatchback")); var pi = r.First(x => x.name.Contains("Pickup"));
            foreach (var x in r) sb.AppendLine($"    {x.name,-17} radius at 6/10/15/20 m/s {string.Join(" / ", x.steady.OrderBy(k => k.Key).Select(k => k.Value.radius.ToString("0.0")))} m; U-turn at 15 m/s {x.uturnTime:0.00} s over {x.uturnDiameter:0.0} m; from rest 90/180 deg {x.restTime90:0.00}/{x.restTime180:0.00} s; onset {x.onset15:0.00} s; 0-20 m/s {x.t0to20:0.00} s");
            // ORIGINAL pickup measured before the change (same probe, commit bd2b1a9): radius 5.5 / 11.0 / 21.0 / 37.4 m, U-turn 4.82 s over 42.0 m, from rest 180 deg 4.58 s, onset 0.30 s, 0-20 m/s 3.00 s
            float R15 = pi.steady[15f].radius, R20 = pi.steady[20f].radius, R6 = pi.steady[6f].radius;
            Check("A. Pickup low-speed turning: it can turn around after a collision (180 degrees from rest in under 4.2 s, was 4.58 s; 6 m/s radius under 5 m, was 5.5 m)", pi.restTime180 < 4.2f && R6 < 5.0f, $"180 deg from rest {pi.restTime180:0.00} s, 90 deg {pi.restTime90:0.00} s, 6 m/s radius {R6:0.0} m");
            Check("B. Pickup high-speed control: radius at 15 and 20 m/s no more than 1.5x / 1.5x the muscle car's (was 1.5x / 1.66x the muscle car's, and 1.9x / 2.1x the hatchback's), onset under 0.25 s", R15 < 1.25f * mu.steady[15f].radius && R20 < 1.25f * mu.steady[20f].radius && pi.onset15 < 0.25f, $"radius 15 m/s {R15:0.0} m (muscle {mu.steady[15f].radius:0.0}, hatchback {ha.steady[15f].radius:0.0}), 20 m/s {R20:0.0} m (muscle {mu.steady[20f].radius:0.0}, hatchback {ha.steady[20f].radius:0.0}), onset {pi.onset15:0.00} s");
            bool order = true; foreach (float v in new[] { 6f, 10f, 15f, 20f }) order &= ha.steady[v].radius < mu.steady[v].radius && mu.steady[v].radius < pi.steady[v].radius;
            Check("C. Vehicle differentiation: at every speed the hatchback turns tightest, then the muscle car, then the pickup (which also accelerates slowest and takes the widest U-turn)", order && pi.uturnDiameter > mu.uturnDiameter * 1.1f && pi.t0to20 > mu.t0to20 * 1.2f && ha.uturnDiameter < mu.uturnDiameter, $"U-turn diameters hatchback {ha.uturnDiameter:0.0} / muscle {mu.uturnDiameter:0.0} / pickup {pi.uturnDiameter:0.0} m; 0-20 m/s {ha.t0to20:0.00} / {mu.t0to20:0.00} / {pi.t0to20:0.00} s");
            Check("E. The other vehicles keep their handling (same probe as before the change: muscle radius 13.8 / 22.5 m at 15 / 20 m/s, hatchback 11.0 / 18.2 m)", Math.Abs(mu.steady[15f].radius - 13.8f) < 0.5f && Math.Abs(mu.steady[20f].radius - 22.5f) < 0.8f && Math.Abs(ha.steady[15f].radius - 11.0f) < 0.5f && Math.Abs(ha.steady[20f].radius - 18.2f) < 0.8f,
                  $"muscle {mu.steady[15f].radius:0.0} / {mu.steady[20f].radius:0.0} m, hatchback {ha.steady[15f].radius:0.0} / {ha.steady[20f].radius:0.0} m");
        }

        // ==================================================================== helpers shared with the derby validation

        static void FreshMatch(int cars, float countdown = 0.2f, Action<MatchRules> tweak = null, GameMode mode = GameMode.PointsDerby)
        {
            game.GoToMenu();
            game.participantCount = cars;
            game.rules.mode = mode; game.rules.countdownSeconds = countdown;
            game.rules.pointsDuration = 180f; game.rules.showdownDuration = 60f; game.rules.respawnDelay = 3f; game.rules.durabilityEnergy = 650000f;
            tweak?.Invoke(game.rules);
            game.StartMatch();
            foreach (var p in game.Participants) p.car.Receiver.logImpacts = false;
            StepSeconds(countdown + 0.1f);
        }

        static void StepSeconds(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt); }

        static void AiOff() { foreach (var p in game.Participants) if (p.car.enemy != null) { p.car.enemy.enabled = false; p.car.controller.Throttle = 0f; p.car.controller.SteerInput = 0f; } }

        static void PlaceCar(Participant p, Vector3 pos, float yaw, float speed)
        {
            var rb = p.car.Body; var rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = new Vector3(pos.x, p.car.GroundLift, pos.z); rb.rotation = rot;
            rb.transform.SetPositionAndRotation(rb.position, rot);
            rb.linearVelocity = rot * Vector3.forward * speed; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            p.car.controller.ResetState(); p.car.Receiver.ClearCooldowns();
        }

        static void Park(params Participant[] except)
        {
            int k = 0;
            foreach (var p in game.Participants)
            {
                if (except.Contains(p)) continue;
                PlaceCar(p, new Vector3(-36f + 16f * (k++ % 5), 0f, 44f), 180f, 0f);
            }
        }

        /// <summary>The human car is driven by an EnemyDriver for these tests (it follows the same rules as any car).</summary>
        static EnemyDriver AddBot(Participant human, DriverProfile profile)
        {
            var hc = human.car;
            var bot = hc.rig.root.AddComponent<EnemyDriver>();
            bot.receiver = hc.Receiver; bot.arenaHalf = game.arenaHalf; bot.SetProfile(profile); bot.Begin(game.aiSpeedKmh / 3.6f); bot.startDelay = 0.2f; bot.Restart();
            hc.enemy = bot;
            game.WireDriver(bot);
            return bot;
        }

        static void RemoveBot(Participant human)
        {
            var bot = human.car.enemy; human.car.enemy = null;
            if (bot != null) UnityEngine.Object.DestroyImmediate(bot);
        }

        // ==================================================================== free-for-all metrics (before / after)

        class DriverStats
        {
            public string name;
            public float millTime, total;
            public int hits, weakHits, strongHits, repos, recov, charges, breakaways, breakawaysDone, breakawaysAborted, aiHits;
            public float breakawayTime, closingSum, peakPressure, overThreshold; public int rejCrowd, rejChance, rejCap, rejDest, abTime, abProg, abRec; public readonly Dictionary<string, float> millByState = new Dictionary<string, float>(); public float threshold;
            public readonly List<float> returnCharges = new List<float>();
        }

        /// <summary>A 5-driver free-for-all (the human car driven by an EnemyDriver as well): per-driver statistics over <paramref name="seconds"/>.</summary>
        static Dictionary<string, DriverStats> Ffa(float seconds, int seed, bool breakaway, GameMode mode = GameMode.PointsDerby, bool cluster = false)
        {
            FreshMatch(5, 0.1f, r => { }, mode);
            var human = game.Human;
            var ps = game.Participants;
            // the human bot takes the Hunter profile; the AI cars keep theirs
            var bot = AddBot(human, game.aiProfiles[1]);
            if (cluster)
            {
                var rr = new System.Random(seed * 13 + 1);
                for (int i = 0; i < ps.Count; i++)
                {
                    float a = (i * 72f + seed * 20f) * Mathf.Deg2Rad;
                    PlaceCar(ps[i], new Vector3(Mathf.Sin(a) * 6f, 0f, Mathf.Cos(a) * 6f - 24f), (float)(rr.NextDouble() * 360), 0f);
                    if (ps[i].car.enemy != null) { ps[i].car.enemy.startDelay = 0.2f; ps[i].car.enemy.Restart(); }
                }
            }
            var stats = ps.ToDictionary(p => p.name, p => new DriverStats { name = p.name });
            var applied = new Dictionary<Participant, Action<CollisionDamageReceiver.Report>>();
            foreach (var p in ps)
            {
                var st = stats[p.name]; var pp = p;
                Action<CollisionDamageReceiver.Report> h = r =>
                {
                    if (!r.applied) return;
                    st.hits++; st.closingSum += r.closingSpeed;
                    if (r.closingSpeed < 5f) st.weakHits++; else if (r.closingSpeed >= 8f) st.strongHits++;
                };
                applied[p] = h; p.car.Receiver.ImpactRegistered += h;
                if (p.car.enemy != null) p.car.enemy.breakawayEnabled = breakaway;
            }
            var history = ps.ToDictionary(p => p, p => new Queue<Vector3>());
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps && game.Match.State != MatchState.Results; i++)
            {
                game.ManualStep(Dt);
                if (i % 5 != 0) continue;
                foreach (var p in ps)
                {
                    var st = stats[p.name]; st.total += 0.1f;
                    var q = history[p]; q.Enqueue(p.car.Body.position); if (q.Count > 80) q.Dequeue();
                    if (!p.IsActive) continue;
                    if (p.car.enemy != null && p.car.enemy.Current == EnemyDriver.State.Breakaway) st.breakawayTime += 0.1f;
                    if (p.car.enemy != null) { st.peakPressure = Mathf.Max(st.peakPressure, p.car.enemy.Pressure); if (p.car.enemy.Pressure >= p.car.enemy.breakawayThreshold) st.overThreshold += 0.1f; }
                    if (q.Count == 80)
                    {
                        float disp = (p.car.Body.position - q.Peek()).magnitude;
                        float nearest = float.MaxValue;
                        foreach (var o in ps) if (o != p && o.IsActive) nearest = Mathf.Min(nearest, (o.car.Body.position - p.car.Body.position).magnitude);
                        if (disp < 10f && nearest < 10f) { st.millTime += 0.1f; string sn = p.car.enemy != null ? p.car.enemy.Current.ToString() : "-"; st.millByState.TryGetValue(sn, out float mv); st.millByState[sn] = mv + 0.1f; }
                    }
                }
            }
            foreach (var p in ps)
            {
                var st = stats[p.name]; var e = p.car.enemy;
                p.car.Receiver.ImpactRegistered -= applied[p];
                if (e != null) { st.repos = e.Repositions; st.recov = e.RecoveriesTriggered; st.charges = e.AttacksCommitted; st.threshold = e.breakawayThreshold; st.abTime = e.AbortTimeout; st.abProg = e.AbortProgress; st.abRec = e.AbortRecover; st.rejCrowd = e.RejectedCrowd; st.rejChance = e.RejectedChance; st.rejCap = e.RejectedCap; st.rejDest = e.RejectedNoDestination; st.breakaways = e.Breakaways; st.breakawaysDone = e.BreakawaysCompleted; st.breakawaysAborted = e.BreakawaysAborted; st.aiHits = e.HitsLanded; }
            }
            RemoveBot(human);
            return stats;
        }

        static void FfaChecks() => FfaRun(false);
        static void MeleeChecks() => FfaRun(true);

        static void FfaRun(bool cluster)
        {
            int runs = cluster ? 10 : 3; float secs = cluster ? 40f : 60f;
            sb.AppendLine($"  -- 5-driver free-for-all {(cluster ? "starting in a clump" : "from the spawn ring")}, {runs} x {secs:0} s, previous AI (breakaway off) vs new AI --");
            var before = new List<Dictionary<string, DriverStats>>(); var after = new List<Dictionary<string, DriverStats>>();
            var control = new List<Dictionary<string, DriverStats>>();
            for (int seed = 1; seed <= runs; seed++) { before.Add(Ffa(secs, seed, false, GameMode.PointsDerby, cluster)); after.Add(Ffa(secs, seed, true, GameMode.PointsDerby, cluster)); control.Add(Ffa(secs, seed, false, GameMode.PointsDerby, cluster)); }
            sb.AppendLine($"    control (previous AI again, new random seeds, shows the run-to-run noise): hits {control.Sum(d => d.Values.Sum(s => s.hits))}, strong {control.Sum(d => d.Values.Sum(s => s.strongHits))}, charges {control.Sum(d => d.Values.Sum(s => s.charges))}; previous AI: hits {before.Sum(d => d.Values.Sum(s => s.hits))}, strong {before.Sum(d => d.Values.Sum(s => s.strongHits))}, charges {before.Sum(d => d.Values.Sum(s => s.charges))}; new AI: hits {after.Sum(d => d.Values.Sum(s => s.hits))}, strong {after.Sum(d => d.Values.Sum(s => s.strongHits))}, charges {after.Sum(d => d.Values.Sum(s => s.charges))}");
            Summ("previous AI", before); Summ("new AI     ", after);
            foreach (var (lab, l) in new[] { ("previous AI", before), ("new AI", after) })
            {
                var tot = new Dictionary<string, float>(); foreach (var run in l) foreach (var s in run.Values) foreach (var kv in s.millByState) { tot.TryGetValue(kv.Key, out float v); tot[kv.Key] = v + kv.Value; }
                sb.AppendLine($"    milling seconds by state, {lab}: " + string.Join(", ", tot.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value:0}")));
            }
            float Mill(List<Dictionary<string, DriverStats>> l) => l.Sum(d => d.Values.Sum(s => s.millTime)) / Mathf.Max(1f, l.Sum(d => d.Values.Sum(s => s.total)));
            float Hits(List<Dictionary<string, DriverStats>> l) => l.Sum(d => d.Values.Sum(s => s.hits)) / (float)l.Count;
            float Strong(List<Dictionary<string, DriverStats>> l) => l.Sum(d => d.Values.Sum(s => s.strongHits)) / (float)l.Count;
            float BreakT(List<Dictionary<string, DriverStats>> l) => l.Sum(d => d.Values.Sum(s => s.breakawayTime)) / Mathf.Max(1f, l.Sum(d => d.Values.Sum(s => s.total)));
            Check("F/I. Fewer drivers stuck milling in a cluster (displacement under 10 m in 8 s with a car within 10 m), without long retreats", Mill(after) < Mill(before), $"milling share of driving time {Mill(before) * 100f:0.0}% -> {Mill(after) * 100f:0.0}%; share of time in breakaway {BreakT(after) * 100f:0.0}%");
            Check("N. Collision frequency and strength are preserved (hits per match, strong hits at 8 m/s closing or more)", Hits(after) >= 0.85f * Hits(before) && Strong(after) >= 0.85f * Strong(before), $"hits per match {Hits(before):0} -> {Hits(after):0} (run to run spread is about 15 %), strong hits {Strong(before):0} -> {Strong(after):0}");
            Check("I. No endless retreat: under 25 % of driving time is spent in breakaway", BreakT(after) < 0.25f, $"{BreakT(after) * 100f:0.0}%");
            int Att(string n) => after.Sum(r => r[n].breakaways);
            Check("F. Cluster detection: in the real melee the unproductive-fighting measure passes every driver's threshold at some point with the previous AI (the back-off and charge cycles are seen)", before[0].Keys.All(n => before.Max(r => r[n].peakPressure) >= before[0][n].threshold), string.Join("; ", before[0].Keys.Select(n => $"{n} peak {before.Max(r => r[n].peakPressure):0.0} vs threshold {before[0][n].threshold:0.00}")));
            Check("J/L. In the same melee Cautious breaks away at least as often as Brawler, Brawler (the most aggressive) least of all, and Cautious still charges as often as in the previous AI (within noise)", Att("CAUTIOUS") >= Att("BRAWLER") && Att("CAUTIOUS") >= 2 && Att("BRAWLER") <= Att("HUNTER") + 1 && after.Sum(r => r["CAUTIOUS"].charges) >= 0.5f * before.Sum(r => r["CAUTIOUS"].charges), $"breakaways started over {runs} runs: Brawler {Att("BRAWLER")}, Hunter {Att("HUNTER")}, Cautious {Att("CAUTIOUS")}, Rookie {Att("ROOKIE")}; Cautious charges {before.Sum(r => r["CAUTIOUS"].charges)} -> {after.Sum(r => r["CAUTIOUS"].charges)}");
            Check("O. Targeting is unchanged: the AI cars keep fighting one another (hits landed on other cars by every AI driver) and the human-controlled car gets no special share", after[0].Keys.Where(n => n != "YOU").All(n => after.Sum(r => r[n].aiHits) >= 1), string.Join("; ", after[0].Keys.Select(n => $"{n} landed {after.Sum(r => r[n].aiHits)} hits")));
        }

        static void Summ(string label, List<Dictionary<string, DriverStats>> runs)
        {
            sb.AppendLine($"    {label}: per driver, summed over {runs.Count} runs (hits / strong / weak, mean closing km/h, charges, back-offs, recoveries, milling s, breakaways done/aborted, breakaway s)");
            foreach (string n in runs[0].Keys)
            {
                var l = runs.Select(r => r[n]).ToList();
                int hits = l.Sum(s => s.hits);
                sb.AppendLine($"      {n,-9} {hits} / {l.Sum(s => s.strongHits)} / {l.Sum(s => s.weakHits)}, {(hits > 0 ? l.Sum(s => s.closingSum) / hits * 3.6f : 0f):0} km/h, {l.Sum(s => s.charges)}, {l.Sum(s => s.repos)}, {l.Sum(s => s.recov)}, {l.Sum(s => s.millTime):0}, {l.Sum(s => s.breakawaysDone)}/{l.Sum(s => s.breakawaysAborted)}, {l.Sum(s => s.breakawayTime):0}, peak pressure {l.Max(s => s.peakPressure):0.0}, s over threshold {l.Sum(s => s.overThreshold):0.0}, aborts (time/progress/stuck) {l.Sum(s => s.abTime)}/{l.Sum(s => s.abProg)}/{l.Sum(s => s.abRec)}, rejected (crowd/chance/cap/no place) {l.Sum(s => s.rejCrowd)}/{l.Sum(s => s.rejChance)}/{l.Sum(s => s.rejCap)}/{l.Sum(s => s.rejDest)}");
            }
        }

        // ==================================================================== cluster scenarios (F-M)

        class ClusterResult
        {
            public string name; public bool breakawayOn;
            public float peakPressure, firstBreakT = -1f, maxDist, breakawayTime, firstChargeAfter = -1f, destQuality;
            public int breakaways, done, aborted, charges, repos, recov, weak, strong, chargesAfterFirstEnd, strongAfter;
            public readonly List<float> flank = new List<float>();
            public float firstEndT = -1f;
        }

        /// <summary>
        /// One driver of the given profile against three parked cars in a clump (target, plus one on each side). "trapped": it starts 7 m from the target with the wall 3 m behind it, so it can
        /// never build a run-up (the repeated low-speed bumper contact of scenario 4); otherwise it starts 22 m out in the open (a productive fight).
        /// </summary>
        static ClusterResult Cluster(string profile, bool breakaway, bool trapped, float seconds, bool inject)
        {
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants;
            var x = ps.First(p => p.profile != null && p.profile.displayName == profile);
            var others = ps.Where(p => p != x).ToList();
            var t = others[0]; var b = others[1]; var c = others[2]; var spare = others[3];
            AiOff();
            PlaceCar(spare, new Vector3(36f, 0f, 44f), 180f, 0f);
            Vector3 centre = new Vector3(0f, 0f, -38f);
            PlaceCar(t, centre, 0f, 0f);
            if (trapped) { PlaceCar(b, centre + new Vector3(-5f, 0f, 0f), 0f, 0f); PlaceCar(c, centre + new Vector3(5f, 0f, 0f), 0f, 0f); }
            else { PlaceCar(b, new Vector3(-36f, 0f, 44f), 180f, 0f); PlaceCar(c, new Vector3(-20f, 0f, 44f), 180f, 0f); }
            if (trapped) PlaceCar(x, new Vector3(0f, 0f, -45.5f), 0f, 0f); else PlaceCar(x, new Vector3(22f, 0f, -38f), 270f, 0f);
            var e = x.car.enemy; e.breakawayEnabled = breakaway; e.enabled = true; e.startDelay = 0.3f; e.Restart(); e.SetTarget(t.car.Body);
            var res = new ClusterResult { name = profile, breakawayOn = breakaway };
            float elapsed = 0f;
            Action<CollisionDamageReceiver.Report> h = r =>
            {
                if (!r.applied) return;
                if (r.closingSpeed < 5f) res.weak++;
                else if (r.closingSpeed >= 8f) { res.strong++; if (res.firstEndT >= 0f) res.strongAfter++; }
            };
            x.car.Receiver.ImpactRegistered += h;
            int seenBreak = 0, seenEnd = 0, seenCharges = 0; Vector3 bearingAtBreak = Vector3.zero;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (inject && e.Breakaways == 0 && i >= 25 && i % 25 == 0) e.AddPressure(10f);
                game.ManualStep(Dt); elapsed += Dt;
                res.peakPressure = Mathf.Max(res.peakPressure, e.Pressure);
                Vector3 off = x.car.Body.position - centre; off.y = 0f;
                res.maxDist = Mathf.Max(res.maxDist, off.magnitude);
                if (e.Current == EnemyDriver.State.Breakaway) res.breakawayTime += Dt;
                if (e.Breakaways != seenBreak)
                {
                    seenBreak = e.Breakaways; if (res.firstBreakT < 0f) res.firstBreakT = elapsed; bearingAtBreak = off.normalized;
                    float clear = 99f; foreach (var o in ps) if (o != x) clear = Mathf.Min(clear, Vector3.Distance(o.car.Body.position, e.BreakawayDestination));
                    if (res.destQuality == 0f) res.destQuality = clear;
                }
                if (e.BreakawaysCompleted + e.BreakawaysAborted != seenEnd) { seenEnd = e.BreakawaysCompleted + e.BreakawaysAborted; if (res.firstEndT < 0f) res.firstEndT = elapsed; }
                if (e.AttacksCommitted != seenCharges)
                {
                    seenCharges = e.AttacksCommitted;
                    if (res.firstEndT >= 0f)
                    {
                        res.chargesAfterFirstEnd++;
                        if (res.firstChargeAfter < 0f) res.firstChargeAfter = elapsed - res.firstEndT;
                        res.flank.Add(Vector3.Angle(bearingAtBreak, off.normalized));
                    }
                }
            }
            x.car.Receiver.ImpactRegistered -= h;
            res.breakaways = e.Breakaways; res.done = e.BreakawaysCompleted; res.aborted = e.BreakawaysAborted; res.charges = e.AttacksCommitted; res.repos = e.Repositions; res.recov = e.RecoveriesTriggered;
            return res;
        }

        static string Fmt(ClusterResult r) => $"{r.name,-9} {(r.breakawayOn ? "new " : "prev")}: breakaways {r.breakaways} (done {r.done}, aborted {r.aborted}), first at {(r.firstBreakT < 0 ? "-" : r.firstBreakT.ToString("0.0") + " s")}, peak pressure {r.peakPressure:0.0}, max distance {r.maxDist:0} m, charges {r.charges}, back-offs {r.repos}, recoveries {r.recov}, hits weak/strong {r.weak}/{r.strong}, in breakaway {r.breakawayTime:0.0} s, charges after the first breakaway {r.chargesAfterFirstEnd} (first {(r.firstChargeAfter < 0 ? "-" : r.firstChargeAfter.ToString("0.0") + " s")} after it ended), strong hits after {r.strongAfter}, destination clearance {r.destQuality:0} m, new approach angle {(r.flank.Count > 0 ? r.flank.Average().ToString("0") : "-")} deg";

        static void AiChecks()
        {
            string[] names = { "Brawler", "Hunter", "Cautious", "Rookie" };
            // Mechanism, forced: the driver is pinned in a clump (parked cars either side of its target, a wall 3 m behind it) and the unproductive-fighting measure is pushed over any threshold
            // once a second until it acts, so the destination choice, the manoeuvre, the return and the profile differences can be checked without waiting for a rare natural trigger.
            sb.AppendLine("  -- forced breakaway: pinned in a clump of three parked cars with the pressure measure pushed up, 3 runs x 30 s per driver --");
            var forced = names.ToDictionary(n => n, n => Enumerable.Range(0, 3).Select(k => Cluster(n, true, true, 30f, true)).ToList());
            foreach (var n in names) foreach (var r in forced[n]) sb.AppendLine("    " + Fmt(r));
            var d = EnemyDriverNumbers();
            sb.AppendLine("    profile numbers (threshold / chance / cooldown / max time / distance / candidates / noise): " + string.Join("; ", d));
            int Tot(string n, Func<ClusterResult, int> f) => forced[n].Sum(f);
            float Avg(string n, Func<ClusterResult, float> f) => forced[n].Average(f);

            Check("G. Breakaway: every profile can leave the clump when the pressure is there (it moves more than 20 m from it and the manoeuvre completes)", names.All(n => Tot(n, r => r.done) >= 1 && forced[n].Any(r => r.maxDist > 20f)),
                  string.Join("; ", names.Select(n => $"{n}: {Tot(n, r => r.breakaways)} started, {Tot(n, r => r.done)} completed, {Tot(n, r => r.aborted)} aborted")));
            Check("H. Re-engagement: after a breakaway the driver is back in the combat loop and charges again, within 12 s of it ending, in most runs, and lands real hits afterwards", names.All(n => forced[n].Count(r => r.firstChargeAfter >= 0f && r.firstChargeAfter < 12f) >= 2) && names.Sum(n => Tot(n, r => r.strongAfter)) >= 4,
                  string.Join("; ", names.Select(n => $"{n}: charged again within 12 s in {forced[n].Count(r => r.firstChargeAfter >= 0f && r.firstChargeAfter < 12f)} of 3 runs (after {string.Join("/", forced[n].Select(r => r.firstChargeAfter < 0 ? "-" : r.firstChargeAfter.ToString("0.0")))} s), strong hits afterwards {Tot(n, r => r.strongAfter)}")));
            Check("I. No endless retreat or circling: per 30 s at most 3 breakaways, under 40 % of the time spent in breakaway, and every driver keeps charging (2+ per run)", names.All(n => forced[n].All(r => r.breakaways <= 3 && r.breakawayTime < 0.4f * 30f && r.charges >= 2)),
                  string.Join("; ", names.Select(n => $"{n}: breakaways {string.Join("/", forced[n].Select(r => r.breakaways))}, time in breakaway {string.Join("/", forced[n].Select(r => r.breakawayTime.ToString("0.0")))} s, charges {string.Join("/", forced[n].Select(r => r.charges))}")));
            Check("K. Hunter takes a new angle: the bearing it returns from differs from the contact bearing by 40 degrees or more (mean over its return charges)", forced["Hunter"].SelectMany(r => r.flank).Any() && forced["Hunter"].SelectMany(r => r.flank).Average() >= 40f,
                  $"Hunter angle change {(forced["Hunter"].SelectMany(r => r.flank).Any() ? forced["Hunter"].SelectMany(r => r.flank).Average().ToString("0") : "-")} deg over {forced["Hunter"].SelectMany(r => r.flank).Count()} returns (Rookie {(forced["Rookie"].SelectMany(r => r.flank).Any() ? forced["Rookie"].SelectMany(r => r.flank).Average().ToString("0") : "-")} deg)");
            Check("M. Rookie is functional and distinct: it breaks away and returns (completed 1+, charges 3+), with its own thresholds and a noisier destination choice", Tot("Rookie", r => r.done) >= 1 && forced["Rookie"].All(r => r.charges >= 2) && d[3] != d[1] && d[3] != d[2] && d[3] != d[0],
                  $"Rookie {Tot("Rookie", r => r.done)} completed, charges {string.Join("/", forced["Rookie"].Select(r => r.charges))}, destination clearance {Avg("Rookie", r => r.destQuality):0.0} m");

            sb.AppendLine("  -- productive fight in the open (22 m run-up against the centre car of the clump, the others parked far away), 25 s, no forcing --");
            var prod = names.ToDictionary(n => n, n => Cluster(n, true, false, 25f, false));
            foreach (var n in names) sb.AppendLine("    " + Fmt(prod[n]));
            Check("J. Brawler stays in a productive fight: every driver lands real hits there and none breaks away more than once in 25 s (Brawler never); its threshold and chance are the least eager of the four", names.All(n => prod[n].strong >= 1 && prod[n].breakaways <= 1) && prod["Brawler"].breakaways == 0 && EnemyThresholds().OrderByDescending(x => x.Value).First().Key == "Brawler", $"strong hits {string.Join("; ", names.Select(n => n + " " + prod[n].strong))}; breakaways {string.Join("; ", names.Select(n => n + " " + prod[n].breakaways))}; thresholds {string.Join("; ", EnemyThresholds().Select(kv => kv.Key + " " + kv.Value.ToString("0.00")))}");

            string src = System.IO.File.ReadAllText("Assets/Driving/Scripts/EnemyDriver.cs") + System.IO.File.ReadAllText("Assets/Derby/Scripts/AiTargeting.cs") + System.IO.File.ReadAllText("Assets/Derby/Scripts/DerbyGame.cs");
            bool noForce = !src.Contains("AddForce") && !src.Contains("AddExplosionForce") && !src.Contains("AddTorque");
            var pile = Pileup();
            Check("Scenario 3. A five-car pileup can occur and separates through ordinary driving (no forces applied anywhere in the AI or game code)", noForce && pile.together >= 1.5f && pile.separatedAt > 0f && pile.separatedAt < 30f, $"all five within 15 m of their centre for {pile.together:0.0} s, spread beyond 25 m after {pile.separatedAt:0.0} s; forces in AI/game code: {(noForce ? "none" : "FOUND")}");
        }

        static Dictionary<string, float> EnemyThresholds()
        {
            var d = new Dictionary<string, float>();
            foreach (var p in new[] { "Brawler", "Hunter", "Cautious", "Rookie" })
            {
                var go = new GameObject("tmp"); go.AddComponent<Rigidbody>(); go.AddComponent<ArcadeCarController>();
                var e = go.AddComponent<EnemyDriver>(); e.SetProfile(game.aiProfiles.First(q => q.displayName == p));
                d[p] = e.breakawayThreshold; UnityEngine.Object.DestroyImmediate(go);
            }
            return d;
        }

        static (float together, float separatedAt) Pileup()
        {
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants;
            AddBot(game.Human, game.aiProfiles[1]);
            var rnd = new System.Random(9);
            for (int i = 0; i < ps.Count; i++)
            {
                float a = i * 72f * Mathf.Deg2Rad;
                PlaceCar(ps[i], new Vector3(Mathf.Sin(a) * 5f, 0f, Mathf.Cos(a) * 5f - 20f), (float)(rnd.NextDouble() * 360), 0f);
                if (ps[i].car.enemy != null) { ps[i].car.enemy.startDelay = 0.2f; ps[i].car.enemy.Restart(); ps[i].car.enemy.enabled = true; }
            }
            float together = 0f, sep = -1f;
            for (int i = 0; i < 1500 && sep < 0f; i++)
            {
                game.ManualStep(Dt);
                Vector3 c = Vector3.zero; foreach (var p in ps) c += p.car.Body.position; c /= ps.Count;
                float max = ps.Max(p => Vector3.Distance(p.car.Body.position, c));
                if (max < 15f) together += Dt;
                if (i > 50 && max > 25f) sep = i * Dt;
            }
            RemoveBot(game.Human);
            return (together, sep);
        }

        static List<string> EnemyDriverNumbers()
        {
            var l = new List<string>();
            foreach (var p in new[] { "Brawler", "Hunter", "Cautious", "Rookie" })
            {
                var prof = game.aiProfiles.First(q => q.displayName == p);
                var go = new GameObject("tmp"); go.AddComponent<Rigidbody>(); go.AddComponent<ArcadeCarController>();
                var e = go.AddComponent<EnemyDriver>(); e.SetProfile(prof);
                l.Add($"{p} {e.breakawayThreshold:0.00} / {e.breakawayChance:0.00} / {e.breakawayCooldown:0.0} s / {e.breakawayMaxTime:0.0} s / {e.breakawayDistance:0} m / {e.breakawayCandidates:0} / {e.breakawayNoise:0.00}");
                UnityEngine.Object.DestroyImmediate(go);
            }
            return l;
        }

        // ==================================================================== pickup attack viability (D)

        class AttackResult { public int charges, hits, strong, recoveries, repos; public float meanClosing; public readonly List<float> misses = new List<float>(); }

        /// <summary>
        /// The pickup, driven by the AI (Hunter profile), chases a hatchback that circles the open arena at 12 m/s for 40 s. <paramref name="oldHandling"/> swaps in the
        /// handling values the pickup had before the tuning (same car, same AI, same target path), for the before/after comparison.
        /// </summary>
        static AttackResult PickupChase(bool oldHandling, bool hunterAi)
        {
            game.Targeting.evalInterval = 1e9f;                       // the driver keeps the target it is given
            game.SelectVehicle(2);                                    // the player's car is the pickup
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants;
            var hatch = ps.First(p => p.vehicleIndex == 1);
            var x = game.Human;
            AiOff(); Park(x, hatch);
            var profile = game.driveProfiles[2];
            DriveProfile old = null;
            if (oldHandling)
            {
                old = UnityEngine.Object.Instantiate(profile);
                old.acceleration = 9.5f; old.reverseAcceleration = 8.5f; old.steerRateLow = 78f; old.steerRateHigh = 26f; old.steerResponse = 22f; old.steerRise = 3.2f;
                x.car.controller.Apply(old);
            }
            var bot = AddBot(x, game.aiProfiles[1]);
            bot.breakawayEnabled = hunterAi;
            PlaceCar(x, new Vector3(-30f, 0f, -30f), 0f, 0f);
            PlaceCar(hatch, new Vector3(20f, 0f, -30f), 0f, 10f);
            bot.Restart(); bot.SetTarget(hatch.car.Body); bot.startDelay = 0.3f;
            { var rec = game.Targeting.RecordOf(x.id); rec.targetId = hatch.id; rec.nextEval = 1e9f; rec.holdUntil = 1e9f; }       // the driver keeps the target it is given (V3 would otherwise prefer a parked car)
            var path = new[] { new Vector3(30f, 0f, -30f), new Vector3(30f, 0f, 30f), new Vector3(-30f, 0f, 30f), new Vector3(-30f, 0f, -30f) };
            int k = 0; int hits = 0, strong = 0; float closingSum = 0f; int seenCharges = 0; float windowEnd = -1f, minD = 999f; var misses = new List<float>();
            Action<CollisionDamageReceiver.Report> h = r => { if (r.applied && r.other == hatch.car.rig.root.name) { hits++; closingSum += r.closingSpeed; if (r.closingSpeed >= 8f) strong++; } };
            x.car.Receiver.ImpactRegistered += h;
            var hc = hatch.car;
            for (int i = 0; i < 2000; i++)
            {
                Vector3 to = path[k] - hc.Body.position; to.y = 0f;
                if (to.magnitude < 10f) { k = (k + 1) % path.Length; to = path[k] - hc.Body.position; to.y = 0f; }
                Vector3 f = hc.Body.transform.forward; f.y = 0f;
                hc.controller.SteerInput = Mathf.Clamp(Vector3.SignedAngle(f, to, Vector3.up) / 30f, -1f, 1f);
                hc.controller.Throttle = hc.controller.ForwardSpeed < 10f ? 1f : 0f;
                game.ManualStep(Dt);
                if (bot.AttacksCommitted != seenCharges) { if (windowEnd >= 0f) misses.Add(minD); seenCharges = bot.AttacksCommitted; windowEnd = i * Dt + 3f; minD = 999f; }
                if (windowEnd >= 0f) { minD = Mathf.Min(minD, Vector3.Distance(x.car.Body.position, hatch.car.Body.position)); if (i * Dt >= windowEnd) { misses.Add(minD); windowEnd = -1f; } }
            }
            x.car.Receiver.ImpactRegistered -= h;
            var res = new AttackResult { charges = bot.AttacksCommitted, hits = hits, strong = strong, recoveries = bot.RecoveriesTriggered, repos = bot.Repositions, meanClosing = hits > 0 ? closingSum / hits : 0f };
            res.misses.AddRange(misses);
            RemoveBot(x);
            x.car.controller.Apply(profile);
            if (old != null) UnityEngine.Object.DestroyImmediate(old);
            game.Targeting.evalInterval = 0.35f;
            game.SelectVehicle(0);
            return res;
        }

        static void PickupChecks()
        {
            sb.AppendLine("  -- pickup (AI Hunter driver) chasing a hatchback that circles the arena at 10 m/s, 6 x 40 s each --");
            var oldR = new List<AttackResult>(); var newR = new List<AttackResult>();
            for (int i = 0; i < 6; i++) { oldR.Add(PickupChase(true, false)); newR.Add(PickupChase(false, false)); }
            string F(List<AttackResult> l) => $"charges {l.Sum(r => r.charges)}, hits on the hatchback {l.Sum(r => r.hits)} (strong, 8 m/s closing or more: {l.Sum(r => r.strong)}), mean closing {(l.Sum(r => r.hits) > 0 ? l.Sum(r => r.meanClosing * r.hits) / l.Sum(r => r.hits) * 3.6f : 0f):0} km/h, recoveries {l.Sum(r => r.recoveries)}, back-offs {l.Sum(r => r.repos)}, closest approach within 3 s of a charge: mean {(l.SelectMany(r => r.misses).Any() ? l.SelectMany(r => r.misses).Average() : 0f):0.0} m, within contact range (6 m) {l.SelectMany(r => r.misses).Count(m => m < 6f)} of {l.SelectMany(r => r.misses).Count()}";
            sb.AppendLine("    old pickup handling: " + F(oldR));
            sb.AppendLine("    new pickup handling: " + F(newR));
            Check("D. Pickup attack viability: the pickup lines up charges that reach a moving hatchback (more charges end within 6 m of it than with the old handling) and lands real hits (3+ at 8 m/s closing or more in 6 x 40 s against a car that is running away)", newR.SelectMany(r => r.misses).Count(m => m < 6f) > oldR.SelectMany(r => r.misses).Count(m => m < 6f) && newR.Sum(r => r.strong) >= 3, "old: " + F(oldR) + " | new: " + F(newR));
        }

        // ==================================================================== performance (U)

        static (float mean, float worst, float pick, float allocPerCall, int calls) DriveCost(bool breakaway)
        {
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 650000f);
            var human = game.Human; AddBot(human, game.aiProfiles[1]);
            var drivers = game.Participants.Select(p => p.car.enemy).Where(e => e != null).ToList();
            foreach (var e in drivers) { e.breakawayEnabled = breakaway; e.measureCost = true; }
            StepSeconds(5f);
            for (int i = 0; i < 2500; i++) game.ManualStep(Dt);
            float mean = drivers.Average(e => e.MeanDriveMilliseconds), worst = drivers.Max(e => e.WorstDriveMilliseconds), pick = drivers.Max(e => e.WorstBreakawayMilliseconds);
            int calls = drivers.Sum(e => e.DriveCalls);
            var one = drivers[0]; one.measureCost = false; long m0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 2000; i++) one.Drive(Dt);
            long m1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            RemoveBot(human);
            return (mean, worst, pick, Mathf.Max(0, m1 - m0) / 2000f, calls);
        }

        static void PerfChecks()
        {
            sb.AppendLine("  -- AI decision cost (editor, Mono; 5 drivers, 55 s each) --");
            var before = DriveCost(false); var after = DriveCost(true);
            Check("U. Decision cost is not significantly higher (mean under 2x and under 0.1 ms, worst call under 3 ms, no allocation per call)", after.mean < Mathf.Max(2f * before.mean, 0.02f) && after.mean < 0.1f && after.worst < 3f && after.allocPerCall < 64f,
                  $"previous AI mean {before.mean * 1000f:0.0} us, worst {before.worst:0.00} ms; new AI mean {after.mean * 1000f:0.0} us, worst {after.worst:0.00} ms, worst destination choice {after.pick:0.00} ms, allocation {after.allocPerCall:0.0} bytes/call ({after.calls} calls)");
        }
    }
}
