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
    /// AI V3 checks (combat intelligence, difficulty, score awareness) on real PhysX in the DerbyArena, stepped at 50 Hz: repeatable scenarios plus simulated Points Derby
    /// matches in which every arm (previous AI, Casual, Standard, Competitive) plays the same kind of match.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AiV3Validation.Run -quit -logFile ai.log
    /// Environment AI_ONLY = melee | scen | perf | probe runs one part; AI_RUNS = matches per arm; AI_SECONDS = seconds per match.
    /// </summary>
    public static class AiV3Validation
    {
        const float Dt = 0.02f;
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate AI V3", priority = 23)]
        public static void Run() => Debug.Log(Execute());

        static void Check(string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[AI V3] Validation (DerbyArena, real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            string only = Environment.GetEnvironmentVariable("AI_ONLY") ?? "";
            var prev = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); game.ui = null;
                if (only == "" || only == "scen") Scenarios();
                if (only == "" || only == "mix") MixedChecks();
                if (only == "" || only == "duels") DuelChecks();
                if (only == "" || only == "vehicles") VehicleChecks();
                if (only == "" || only == "idle") IdleChecks();
                if (only == "" || only == "perf") Perf();
                if (only == "melee") Melee();
                if (only == "debug") DebugChase();
                if (only == "duel") Duels();
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); game.aiV3 = true; game.difficulty = AiDifficulty.Standard; }
            sb.AppendLine($"[AI V3] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ==================================================================== helpers

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

        static DriverProfile averageProfile;
        static DriverProfile Average()
        {
            if (averageProfile == null) { averageProfile = ScriptableObject.CreateInstance<DriverProfile>(); averageProfile.displayName = "Average"; averageProfile.aggression = 0.5f; averageProfile.skill = 0.5f; averageProfile.combatIntelligence = 0.5f; }
            return averageProfile;
        }

        /// <summary>The human car is driven by an EnemyDriver for these tests (it follows the same rules as any car).</summary>
        static EnemyDriver AddBot(Participant human, DriverProfile profile)
        {
            var hc = human.car;
            var bot = hc.rig.root.AddComponent<EnemyDriver>();
            bot.receiver = hc.Receiver; bot.arenaHalf = game.arenaHalf;
            game.WireDriver(bot);
            bot.SetProfile(profile); bot.Begin(game.aiSpeedKmh / 3.6f); bot.startDelay = 0.2f; bot.Restart();
            hc.enemy = bot;
            return bot;
        }

        static void RemoveBot(Participant human)
        {
            var bot = human.car.enemy; human.car.enemy = null;
            if (bot != null) UnityEngine.Object.DestroyImmediate(bot);
        }

        static DriverProfile Profile(string n) => game.aiProfiles.First(q => q.displayName == n);

        // ==================================================================== simulated matches

        class Stats
        {
            public int runs, score, hits, scoredHits, strong, weak, wrecks, wrecked, charges, successes, taps, rash, finishing, setups, recoveries, repositions, breakaways, chargeHits;
            public float closingSum, damageJ, chargeSpeedSum, chargeClosingSum, takenJ; public int hitsTaken;
            public readonly Dictionary<string, float> state = new Dictionary<string, float>();
            public float total;
            public void Add(string s, float t) { state.TryGetValue(s, out float v); state[s] = v + t; }
            public float Share(params string[] names) => total > 0f ? names.Sum(n => state.TryGetValue(n, out float v) ? v : 0f) / total : 0f;
            public float PointsPerHit => scoredHits > 0 ? (float)score / scoredHits : 0f;
            public float DamageKJPerHit => scoredHits > 0 ? damageJ / scoredHits / 1000f : 0f;
            public float MeanClosingKmh => hits > 0 ? closingSum / hits * 3.6f : 0f;
            public float ChargeClosingKmh => chargeHits > 0 ? chargeClosingSum / chargeHits * 3.6f : 0f;
            public float ChargeStartKmh => charges > 0 ? chargeSpeedSum / charges * 3.6f : 0f;
            public void Merge(Stats s)
            {
                runs += s.runs; score += s.score; hits += s.hits; scoredHits += s.scoredHits; strong += s.strong; weak += s.weak; wrecks += s.wrecks; wrecked += s.wrecked; charges += s.charges; successes += s.successes;
                taps += s.taps; rash += s.rash; finishing += s.finishing; setups += s.setups; recoveries += s.recoveries; repositions += s.repositions; breakaways += s.breakaways; chargeHits += s.chargeHits;
                closingSum += s.closingSum; damageJ += s.damageJ; chargeSpeedSum += s.chargeSpeedSum; chargeClosingSum += s.chargeClosingSum; total += s.total; takenJ += s.takenJ; hitsTaken += s.hitsTaken;
                foreach (var kv in s.state) Add(kv.Key, kv.Value);
            }
        }

        class Arm
        {
            public string name; public bool v3; public AiDifficulty diff; public int mask = EnemyDriver.FAll;
            public readonly Dictionary<string, Stats> drivers = new Dictionary<string, Stats>();
            public int matches;
            public readonly List<float> scoreByMatch = new List<float>();
            public readonly Stats winner = new Stats();
            public int switches; public float driverMinutes;
            public Stats Total { get { var t = new Stats(); foreach (var s in drivers.Values) t.Merge(s); return t; } }
        }

        /// <summary>Driver counters that restart with every respawn (the driver is restarted): accumulate their increments instead.</summary>
        class Delta
        {
            int prev;
            public int Total;
            public void Feed(int cur) { Total += cur >= prev ? cur - prev : cur; prev = cur; }
        }

        class Track { public readonly Delta taps = new Delta(), rash = new Delta(), finishing = new Delta(), setups = new Delta(), recov = new Delta(), repos = new Delta(), breaks = new Delta(); public EnemyDriver.State prev = EnemyDriver.State.Waiting; public int hitsAtCommit; }

        /// <summary>One Points Derby match of five drivers (four personalities plus an average driver in the player's car), played at 50 Hz for <paramref name="seconds"/>.</summary>
        static void PlayMatch(Arm arm, float seconds, Func<int, AiDifficulty> diffOf = null, Dictionary<AiDifficulty, Arm> byDiff = null, Action<MatchRules> tweak = null)
        {
            game.aiV3 = arm.v3; game.difficulty = arm.diff;
            FreshMatch(5, 0.1f, tweak, GameMode.PointsDerby);
            var human = game.Human;
            var ps = game.Participants;
            AddBot(human, Average());
            foreach (var p in ps) if (p.car.enemy != null) p.car.enemy.v3Mask = arm.mask;
            if (diffOf != null) for (int di = 0; di < ps.Count; di++) { var de = ps[di].car.enemy; de.difficulty = diffOf(di); de.SetProfile(de.profile); de.Restart(); de.startDelay = 0.3f + 0.12f * di; }
            var st = ps.ToDictionary(p => p.name, p => new Stats { runs = 1 });
            var tr = ps.ToDictionary(p => p, p => new Track());
            var handlers = new Dictionary<Participant, Action<CollisionDamageReceiver.Report>>();
            foreach (var p in ps)
            {
                Action<CollisionDamageReceiver.Report> h = r =>
                {
                    if (!r.applied || !game.Match.InPlay || r.other == null || !r.other.StartsWith("Car")) return;
                    // this receiver belongs to the victim; the hit counts for the attacker, found by name
                    var att = ps.FirstOrDefault(q => q.car != null && q.car.rig.root.name == r.other);
                    if (att == null) return;
                    var sa = st[att.name];
                    sa.hits++; sa.closingSum += r.closingSpeed;
                    if (r.closingSpeed < 5f) sa.weak++; else if (r.closingSpeed >= 8f) sa.strong++;
                    var ae = att.car.enemy;
                    if (ae != null && (ae.Current == EnemyDriver.State.Commit || ae.Current == EnemyDriver.State.Overshoot)) { sa.chargeHits++; sa.chargeClosingSum += r.closingSpeed; }
                };
                handlers[p] = h; p.car.Receiver.ImpactRegistered += h;
            }
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps && game.Match.State != MatchState.Results; i++)
            {
                game.ManualStep(Dt);
                foreach (var p in ps)
                {
                    var e = p.car.enemy; if (e == null) continue;
                    var s = st[p.name]; var t = tr[p];
                    t.taps.Feed(e.TapsRejected); t.rash.Feed(e.RashCharges); t.finishing.Feed(e.FinishingCharges); t.setups.Feed(e.SetupRuns); t.recov.Feed(e.RecoveriesTriggered); t.repos.Feed(e.Repositions); t.breaks.Feed(e.Breakaways);
                    if (!p.IsActive) { t.prev = EnemyDriver.State.Waiting; continue; }
                    var cur = e.Current;
                    if (i % 5 == 0) { s.Add(cur.ToString(), 0.1f); s.total += 0.1f; }
                    if (t.prev != EnemyDriver.State.Commit && cur == EnemyDriver.State.Commit) { t.hitsAtCommit = e.HitsLanded; s.charges++; s.chargeSpeedSum += p.car.controller.ForwardSpeed; }
                    if (t.prev == EnemyDriver.State.Commit && cur != EnemyDriver.State.Commit && e.HitsLanded > t.hitsAtCommit) s.successes++;
                    t.prev = cur;
                }
            }
            foreach (var p in ps)
            {
                var s = st[p.name]; var t = tr[p];
                p.car.Receiver.ImpactRegistered -= handlers[p];
                s.score = p.score; s.scoredHits = p.hitsDealt; s.wrecks = p.wrecksDealt; s.wrecked = p.timesWrecked; s.damageJ = p.damageDealt; s.takenJ = p.damageTaken; s.hitsTaken = p.hitsTaken;
                s.taps = t.taps.Total; s.rash = t.rash.Total; s.finishing = t.finishing.Total; s.setups = t.setups.Total; s.recoveries = t.recov.Total; s.repositions = t.repos.Total; s.breakaways = t.breaks.Total;
                string key = p == human ? "AVERAGE" : p.name;
                var dest = byDiff != null ? byDiff[diffOf(ps.IndexOf(p))] : arm;
                if (!dest.drivers.TryGetValue(key, out var acc)) dest.drivers[key] = acc = new Stats();
                acc.Merge(s);
            }
            arm.scoreByMatch.Add(ps.Sum(p => p.score) / (float)ps.Count);
            arm.switches += game.Targeting.Switches; arm.driverMinutes += ps.Count * Mathf.Min(seconds, game.Match.Clock) / 60f;
            { var top = ps.OrderByDescending(q => q.score).First(); arm.winner.Merge(st[top.name]); }
            foreach (var p in ps) { if (!vehicleScores.TryGetValue(p.vehicleIndex, out var vl)) vehicleScores[p.vehicleIndex] = vl = new List<float>(); vl.Add(p.score); }
            if (byDiff != null) foreach (var p in ps) { var ba = byDiff[diffOf(ps.IndexOf(p))]; ba.scoreByMatch.Add(p.score); ba.matches++; }
            RemoveBot(human);
            arm.matches++;
        }

        static string Line(string label, Stats t)
        {
            float n = Math.Max(1, t.runs);
            return $"{label} score {t.score / n:0}, hits {t.hits / n:0.0} (strong {t.strong / n:0.0}), closing {t.MeanClosingKmh:0} km/h, charge hits {t.chargeHits / n:0.0} at {t.ChargeClosingKmh:0} km/h, points/hit {t.PointsPerHit:0.0}, damage/hit {t.DamageKJPerHit:0.0} kJ, wrecks {t.wrecks / n:0.00}, wrecked {t.wrecked / n:0.00}, hits taken {t.hitsTaken / n:0.0}, damage dealt/taken {(t.takenJ > 0f ? t.damageJ / t.takenJ : 0f):0.00}, charges {t.charges / n:0.0} (from {t.ChargeStartKmh:0} km/h, {(t.charges > 0 ? 100f * t.successes / t.charges : 0f):0}% hit), taps given up {t.taps / n:0.0}, set-ups {t.setups / n:0.0}, rash {t.rash / n:0.0}, finishing {t.finishing / n:0.0}, pursue {t.Share("Pursue", "LineUp") * 100f:0}%, commit {t.Share("Commit") * 100f:0}%, after-hit {t.Share("Overshoot") * 100f:0}%, repos/breakaway {t.Share("Reposition", "Breakaway") * 100f:0}%, stuck {t.Share("Recover") * 100f:0}%";
        }

        static void Report(Arm a)
        {
            float mean = a.scoreByMatch.Count > 0 ? a.scoreByMatch.Average() : 0f;
            float sd = a.scoreByMatch.Count > 1 ? Mathf.Sqrt(a.scoreByMatch.Sum(x => (x - mean) * (x - mean)) / (a.scoreByMatch.Count - 1)) : 0f;
            sb.AppendLine($"    {a.name,-15} {a.matches} matches (mean score per driver {mean:0}, spread between matches {sd:0}): " + Line("", a.Total));
        }

        static void ReportDrivers(Arm a)
        {
            foreach (var kv in a.drivers.OrderBy(k => k.Key)) sb.AppendLine($"      {a.name,-15} {kv.Key,-9} " + Line("", kv.Value));
        }

        /// <summary>"v2", "cas", "std", "comp", optionally followed by -TSPRUX to switch V3 parts off (T targeting, S speed, P pickiness, R run-up, U score awareness, X sloppiness, A approach angle).</summary>
        static Arm ParseArm(string spec)
        {
            string[] parts = spec.Split('-');
            var a = new Arm { name = spec };
            switch (parts[0])
            {
                case "v2": a.v3 = false; a.diff = AiDifficulty.Standard; break;
                case "cas": a.v3 = true; a.diff = AiDifficulty.Casual; break;
                case "comp": a.v3 = true; a.diff = AiDifficulty.Competitive; break;
                default: a.v3 = true; a.diff = AiDifficulty.Standard; break;
            }
            if (parts.Length > 1)
                foreach (char c in parts[1])
                {
                    int bit = c == 'T' ? EnemyDriver.FTarget : c == 'S' ? EnemyDriver.FSpeed : c == 'P' ? EnemyDriver.FPick : c == 'R' ? EnemyDriver.FRoll : c == 'U' ? EnemyDriver.FUrgency : c == 'X' ? EnemyDriver.FSloppy : c == 'A' ? EnemyDriver.FAngle : c == 'M' ? EnemyDriver.FMomentum : 0;
                    a.mask &= ~bit;
                }
            return a;
        }

        static Dictionary<AiDifficulty, Arm> lastMixed;
        static readonly Dictionary<int, List<float>> vehicleScores = new Dictionary<int, List<float>>();

        static void Melee()
        {
            int runs = int.TryParse(Environment.GetEnvironmentVariable("AI_RUNS"), out int r0) ? r0 : 4;
            float secs = float.TryParse(Environment.GetEnvironmentVariable("AI_SECONDS"), out float s0) ? s0 : 180f;
            if (Environment.GetEnvironmentVariable("AI_MIX") == "1")
            {
                // every match holds all three difficulties at once (rotating over the drivers), so the comparison is made inside the same matches against the same opponents
                sb.AppendLine($"  -- mixed difficulties in the same matches (driver i plays difficulty (i + match) mod 3), {runs} matches x {secs:0} s --");
                var byDiff = new Dictionary<AiDifficulty, Arm> { { AiDifficulty.Casual, new Arm { name = "Casual", v3 = true, diff = AiDifficulty.Casual } }, { AiDifficulty.Standard, new Arm { name = "Standard", v3 = true, diff = AiDifficulty.Standard } }, { AiDifficulty.Competitive, new Arm { name = "Competitive", v3 = true, diff = AiDifficulty.Competitive } } };
                var host = new Arm { name = "mixed", v3 = true, diff = AiDifficulty.Standard };
                var sw2 = Stopwatch.StartNew();
                for (int k = 0; k < runs; k++) { int kk = k; PlayMatch(host, secs, i => (AiDifficulty)((i + kk) % 3), byDiff); }
                sb.AppendLine($"    ({sw2.Elapsed.TotalSeconds:0} s wall clock for {runs} matches)");
                foreach (var a in byDiff.Values) { a.matches = a.scoreByMatch.Count; Report(a); }
                sb.AppendLine("    per personality:");
                foreach (var a in byDiff.Values) ReportDrivers(a);
                lastMixed = byDiff;
                return;
            }
            string spec = Environment.GetEnvironmentVariable("AI_ARMS") ?? "v2,cas,std,comp";
            bool perDriver = Environment.GetEnvironmentVariable("AI_DRIVERS") == "1";
            sb.AppendLine($"  -- simulated Points Derby: 4 personalities + an average driver, {runs} matches x {secs:0} s per arm ({spec}) --");
            var arms = spec.Split(',').Select(ParseArm).ToArray();
            var sw = Stopwatch.StartNew();
            for (int k = 0; k < runs; k++) foreach (var a in arms) PlayMatch(a, secs);
            sb.AppendLine($"    ({sw.Elapsed.TotalSeconds:0} s wall clock for {runs * arms.Length} matches)");
            foreach (var a in arms) Report(a);
            if (perDriver)
            {
                sb.AppendLine("    per personality:");
                foreach (var a in arms) ReportDrivers(a);
            }
        }

        // ==================================================================== 1 v 1 duels between difficulties

        class DuelResult { public int scoreA, scoreB, hitsA, hitsB, wrecksA, wrecksB; public float dmgA, dmgB, takenA, takenB, speedA, speedB, slowA, slowB; public int chargesA, chargesB, deferA; }

        /// <summary>
        /// Two cars, both driven by the same personality, one with the arm A settings and one with the arm B settings (sides alternate between repetitions, because the player's car is a
        /// muscle car and the AI car a hatchback). Points Derby rules, <paramref name="seconds"/> long. The only difference between the two drivers is the arm.
        /// </summary>
        static DuelResult Duel(string profile, Arm a, Arm b, bool swap, float seconds)
        {
            game.aiV3 = true; game.difficulty = AiDifficulty.Standard;
            FreshMatch(2, 0.1f, null, GameMode.PointsDerby);
            var ps = game.Participants; var botCar = ps[0]; var aiCar = ps[1];
            var bot = AddBot(botCar, Profile(profile));
            var ai = aiCar.car.enemy; ai.SetProfile(Profile(profile));
            var armBot = swap ? b : a; var armAi = swap ? a : b;
            void Apply(EnemyDriver e, Arm arm, float delay) { e.v3Enabled = arm.v3; e.difficulty = arm.diff; e.v3Mask = arm.mask; e.SetProfile(e.profile); e.Restart(); e.startDelay = delay; }
            Apply(bot, armBot, 0.3f); Apply(ai, armAi, 0.4f);
            float sumA = 0, sumB = 0, slowA = 0, slowB = 0; int samples = 0;
            var pa = swap ? aiCar : botCar; var pb = swap ? botCar : aiCar;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps && game.Match.State != MatchState.Results; i++)
            {
                game.ManualStep(Dt);
                if (i % 5 != 0) continue;
                float va = pa.car.Body.linearVelocity.magnitude, vb = pb.car.Body.linearVelocity.magnitude;
                sumA += va; sumB += vb; if (va < 6f) slowA++; if (vb < 6f) slowB++; samples++;
            }
            var ea = pa.car.enemy; var eb = pb.car.enemy;
            var r = new DuelResult { scoreA = pa.score, scoreB = pb.score, hitsA = pa.hitsDealt, hitsB = pb.hitsDealt, wrecksA = pa.wrecksDealt, wrecksB = pb.wrecksDealt, dmgA = pa.damageDealt, dmgB = pb.damageDealt, takenA = pa.damageTaken, takenB = pb.damageTaken,
                speedA = sumA / Mathf.Max(1, samples), speedB = sumB / Mathf.Max(1, samples), slowA = slowA / Mathf.Max(1, samples), slowB = slowB / Mathf.Max(1, samples), chargesA = ea != null ? ea.AttacksCommitted : 0, chargesB = eb != null ? eb.AttacksCommitted : 0, deferA = ea != null ? ea.EdgeDeferrals : 0 };
            RemoveBot(botCar);
            return r;
        }

        static void Duels()
        {
            int reps = int.TryParse(Environment.GetEnvironmentVariable("AI_RUNS"), out int r0) ? r0 : 6;
            float secs = float.TryParse(Environment.GetEnvironmentVariable("AI_SECONDS"), out float s0) ? s0 : 90f;
            string specA = Environment.GetEnvironmentVariable("AI_DUEL_A") ?? "comp", specB = Environment.GetEnvironmentVariable("AI_DUEL_B") ?? "cas";
            bool each = Environment.GetEnvironmentVariable("AI_DRIVERS") == "1";
            sb.AppendLine($"  -- 1 v 1 duels, same personality on both sides, only the arm differs: {reps} per personality, {secs:0} s each, sides alternate; B = {specB} --");
            var armB = ParseArm(specB);
            foreach (string sa in specA.Split(','))
            {
                var armA = ParseArm(sa);
                var all = new List<DuelResult>();
                foreach (var prof in new[] { "Brawler", "Hunter", "Cautious", "Rookie" })
                {
                    var l = Enumerable.Range(0, reps).Select(k => Duel(prof, armA, armB, k % 2 == 1, secs)).ToList();
                    all.AddRange(l);
                    if (each) sb.AppendLine($"      {sa,-11} vs {specB,-5} {prof,-9}: score {l.Average(x => x.scoreA):0} v {l.Average(x => x.scoreB):0}, hits {l.Average(x => x.hitsA):0.0} v {l.Average(x => x.hitsB):0.0}, damage dealt {l.Average(x => x.dmgA) / 1000f:0} v {l.Average(x => x.dmgB) / 1000f:0} kJ, ahead in {l.Count(x => x.scoreA > x.scoreB)}, behind in {l.Count(x => x.scoreA < x.scoreB)}");
                }
                float n = all.Count;
                sb.AppendLine($"    {sa,-14} vs {specB,-5}: score {all.Average(x => x.scoreA):0} v {all.Average(x => x.scoreB):0}, ahead in {all.Count(x => x.scoreA > x.scoreB)} of {all.Count}, behind in {all.Count(x => x.scoreA < x.scoreB)}; damage dealt {all.Sum(x => x.dmgA) / 1000f / n:0} v {all.Sum(x => x.dmgB) / 1000f / n:0} kJ, taken {all.Sum(x => x.takenA) / 1000f / n:0} v {all.Sum(x => x.takenB) / 1000f / n:0} kJ (ratio {all.Sum(x => x.dmgA) / Math.Max(1f, all.Sum(x => x.takenA)):0.00} v {all.Sum(x => x.dmgB) / Math.Max(1f, all.Sum(x => x.takenB)):0.00}); hits {all.Average(x => x.hitsA):0.0} v {all.Average(x => x.hitsB):0.0}; charges {all.Average(x => x.chargesA):0.0} v {all.Average(x => x.chargesB):0.0}; mean speed {all.Average(x => x.speedA):0.0} v {all.Average(x => x.speedB):0.0} m/s, time under 6 m/s {all.Average(x => x.slowA) * 100f:0}% v {all.Average(x => x.slowB) * 100f:0}%; edge deferrals {all.Average(x => x.deferA):0.0}");
            }
        }

        // ==================================================================== debugging aid

        static void DebugChase()
        {
            foreach (bool v3 in new[] { false, true })
            {
                game.aiV3 = v3; game.difficulty = AiDifficulty.Standard;
                game.Targeting.evalInterval = 1e9f;
                game.SelectVehicle(2);
                FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
                var ps = game.Participants; var hatch = ps.First(p => p.vehicleIndex == 1); var x = game.Human;
                AiOff(); Park(x, hatch);
                var bot = AddBot(x, game.aiProfiles[1]);
                PlaceCar(x, new Vector3(-30f, 0f, -30f), 0f, 0f); PlaceCar(hatch, new Vector3(20f, 0f, -30f), 0f, 10f);
                bot.Restart(); bot.SetTarget(hatch.car.Body); bot.startDelay = 0.3f;
                var path = new[] { new Vector3(30f, 0f, -30f), new Vector3(30f, 0f, 30f), new Vector3(-30f, 0f, 30f), new Vector3(-30f, 0f, -30f) };
                int k = 0; var hc = hatch.car; var prev = EnemyDriver.State.Waiting; int prevRec = 0;
                sb.AppendLine($"  -- debug chase, V3 {(v3 ? "on" : "off")} --");
                for (int i = 0; i < 1250; i++)
                {
                    Vector3 to = path[k] - hc.Body.position; to.y = 0f;
                    if (to.magnitude < 10f) { k = (k + 1) % path.Length; to = path[k] - hc.Body.position; to.y = 0f; }
                    Vector3 f = hc.Body.transform.forward; f.y = 0f;
                    hc.controller.SteerInput = Mathf.Clamp(Vector3.SignedAngle(f, to, Vector3.up) / 30f, -1f, 1f);
                    hc.controller.Throttle = hc.controller.ForwardSpeed < 10f ? 1f : 0f;
                    game.ManualStep(Dt);
                    if (bot.Current != prev || bot.RecoveriesTriggered != prevRec)
                    {
                        sb.AppendLine($"    t={i * Dt:0.0} {prev} -> {bot.Current}  speed {x.car.controller.ForwardSpeed:0.0} m/s, dist {Vector3.Distance(x.car.Body.position, hc.Body.position):0} m, pos ({x.car.Body.position.x:0},{x.car.Body.position.z:0}), recoveries {bot.RecoveriesTriggered}, throttle {x.car.controller.Throttle:0.0}");
                        prev = bot.Current; prevRec = bot.RecoveriesTriggered;
                    }
                }
                RemoveBot(x); game.Targeting.evalInterval = 0.35f; game.SelectVehicle(0);
            }
        }

        // ==================================================================== scripted scenarios

        static readonly Arm[] ScenarioArms =
        {
            new Arm { name = "previous AI", v3 = false, diff = AiDifficulty.Standard },
            new Arm { name = "Casual", v3 = true, diff = AiDifficulty.Casual },
            new Arm { name = "Standard", v3 = true, diff = AiDifficulty.Standard },
            new Arm { name = "Competitive", v3 = true, diff = AiDifficulty.Competitive },
        };

        static void Pin(Participant attacker, Participant target)
        {
            var rec = game.Targeting.RecordOf(attacker.id);
            rec.targetId = target.id; rec.nextEval = 1e9f; rec.holdUntil = 1e9f;
            attacker.car.enemy.SetTarget(target.car.Body);
        }

        class ChargeTrial { public bool hit; public float closing, speed, time; }

        /// <summary>
        /// SCENARIO 1, high-value charge: the driver (in the player's car) starts from rest 50 m from a stationary car in open ground and has the room to build speed.
        /// How hard is the first hit? (closing speed in m/s, and the driver's own speed at the moment.)
        /// </summary>
        static List<ChargeTrial> ChargeTrials(Arm arm, string profile, int n, int vehicle = 0)
        {
            game.aiV3 = arm.v3; game.difficulty = arm.diff;
            game.SelectVehicle(vehicle);
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var x = game.Human; var target = game.Participants.First(p => p != x);
            AiOff(); Park(x, target);
            var bot = AddBot(x, Profile(profile));
            var res = new List<ChargeTrial>(); ChargeTrial cur = null; float clock = 0f;
            Action<CollisionDamageReceiver.Report> h = r => { if (cur != null && !cur.hit && r.applied && r.other == x.car.rig.root.name) { cur.hit = true; cur.closing = r.closingSpeed; cur.speed = x.car.controller.Speed; cur.time = clock; } };
            target.car.Receiver.ImpactRegistered += h;
            for (int t = 0; t < n; t++)
            {
                cur = new ChargeTrial(); clock = 0f;
                float lat = (t % 3 - 1) * 2.5f;
                PlaceCar(target, new Vector3(10f, 0f, -30f + lat), 90f + (t % 2 == 0 ? 0f : 40f), 0f);
                PlaceCar(x, new Vector3(-40f, 0f, -30f), 90f, 0f);
                bot.Restart(); bot.startDelay = 0.2f; Pin(x, target);
                for (int i = 0; i < 450 && !cur.hit; i++) { game.ManualStep(Dt); clock += Dt; }
                res.Add(cur);
            }
            target.car.Receiver.ImpactRegistered -= h;
            RemoveBot(x);
            game.SelectVehicle(0);
            return res;
        }

        class TailResult { public int hits, weak, strong; public float closingSum; public int repos, charges; public float chargeDistSum; public int chargeStarts; }

        /// <summary>
        /// The driver starts 8 m behind a car that circles the open arena at 12 m/s. Rather than tap its bumper over and over, a driver that prepares its attacks should give up
        /// the weak taps and come back harder.
        /// </summary>
        static TailResult TailTrial(Arm arm, string profile, float seconds)
        {
            game.aiV3 = arm.v3; game.difficulty = arm.diff;
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var x = game.Human; var target = game.Participants.First(p => p != x);
            AiOff(); Park(x, target);
            var bot = AddBot(x, Profile(profile));
            PlaceCar(x, new Vector3(-38f, 0f, -30f), 90f, 12f);
            PlaceCar(target, new Vector3(-30f, 0f, -30f), 90f, 12f);
            bot.Restart(); bot.startDelay = 0.1f; Pin(x, target);
            var r2 = new TailResult();
            Action<CollisionDamageReceiver.Report> h = r => { if (r.applied && r.other == x.car.rig.root.name) { r2.hits++; r2.closingSum += r.closingSpeed; if (r.closingSpeed < 5f) r2.weak++; else if (r.closingSpeed >= 8f) r2.strong++; } };
            target.car.Receiver.ImpactRegistered += h;
            var path = new[] { new Vector3(30f, 0f, -30f), new Vector3(30f, 0f, 30f), new Vector3(-30f, 0f, 30f), new Vector3(-30f, 0f, -30f) };
            int k = 0; var hc = target.car; var prevState = EnemyDriver.State.Waiting;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                Vector3 to = path[k] - hc.Body.position; to.y = 0f;
                if (to.magnitude < 10f) { k = (k + 1) % path.Length; to = path[k] - hc.Body.position; to.y = 0f; }
                Vector3 fw = hc.Body.transform.forward; fw.y = 0f;
                hc.controller.SteerInput = Mathf.Clamp(Vector3.SignedAngle(fw, to, Vector3.up) / 30f, -1f, 1f);
                hc.controller.Throttle = hc.controller.ForwardSpeed < 12f ? 1f : 0f;
                game.ManualStep(Dt);
                hc.controller.Throttle = hc.controller.ForwardSpeed < 12f ? 1f : 0f;
                var cur = bot.Current;
                if (prevState != EnemyDriver.State.Commit && cur == EnemyDriver.State.Commit) { r2.chargeDistSum += Vector3.Distance(x.car.Body.position, hc.Body.position); r2.chargeStarts++; }
                prevState = cur;
            }
            target.car.Receiver.ImpactRegistered -= h;
            r2.repos = bot.Repositions; r2.charges = bot.AttacksCommitted;
            RemoveBot(x);
            return r2;
        }

        static void Scenarios()
        {
            int n = int.TryParse(Environment.GetEnvironmentVariable("AI_TRIALS"), out int t0) ? t0 : 6;
            string[] profs = { "Hunter", "Brawler", "Cautious", "Rookie" };
            sb.AppendLine($"  -- scenario 1, high-value charge: from rest, 50 m of open ground to a parked car, {n} trials per driver and arm --");
            var charge = new Dictionary<string, List<ChargeTrial>>();
            foreach (var prof in profs)
                foreach (var arm in ScenarioArms)
                {
                    var l = ChargeTrials(arm, prof, n);
                    charge[prof + "/" + arm.name] = l;
                    var h = l.Where(x => x.hit).ToList();
                    sb.AppendLine($"    {prof,-9} {arm.name,-12} hit {h.Count}/{l.Count}, closing {(h.Count > 0 ? h.Average(x => x.closing) : 0f):0.0} m/s ({(h.Count > 0 ? h.Average(x => x.closing) * 3.6f : 0f):0} km/h), own speed {(h.Count > 0 ? h.Average(x => x.speed) : 0f):0.0} m/s, after {(h.Count > 0 ? h.Average(x => x.time) : 0f):0.0} s");
                }
            float Closing(string arm) => profs.SelectMany(p => charge[p + "/" + arm]).Where(x => x.hit).Average(x => x.closing);
            bool allHit = charge.Values.All(l => l.All(x => x.hit));
            float cV2 = Closing("previous AI"), cCas = Closing("Casual"), cStd = Closing("Standard"), cComp = Closing("Competitive");
            Check("Scenario 1/A3. High-value charge in open ground: every driver finds the target; the closing speed of the first hit rises with the difficulty (Competitive at least 6 % above the previous AI, Standard not below it, Casual not above it)",
                  allHit && cComp >= 1.06f * cV2 && cStd >= 0.98f * cV2 && cCas <= 1.03f * cV2 && cComp > cStd - 0.3f,
                  $"mean first-hit closing speed over the four personalities: previous AI {cV2:0.0}, Casual {cCas:0.0}, Standard {cStd:0.0}, Competitive {cComp:0.0} m/s ({cV2 * 3.6f:0} / {cCas * 3.6f:0} / {cStd * 3.6f:0} / {cComp * 3.6f:0} km/h); Hunter {charge["Hunter/previous AI"].Average(x => x.closing):0.0} -> {charge["Hunter/Competitive"].Average(x => x.closing):0.0} m/s");

            sb.AppendLine("  -- tailgating: 8 m behind a car circling the open arena at 12 m/s, 3 x 40 s per driver and arm --");
            var tail = new Dictionary<string, List<TailResult>>();
            foreach (var prof in profs)
                foreach (var arm in ScenarioArms)
                {
                    var rs = Enumerable.Range(0, 3).Select(_ => TailTrial(arm, prof, 40f)).ToList();
                    tail[prof + "/" + arm.name] = rs;
                    int hits = rs.Sum(r => r.hits);
                    sb.AppendLine($"    {prof,-9} {arm.name,-12} hits {hits} (weak under 5 m/s: {rs.Sum(r => r.weak)}, strong 8+ m/s: {rs.Sum(r => r.strong)}), mean closing {(hits > 0 ? rs.Sum(r => r.closingSum) / hits : 0f):0.0} m/s, charges {rs.Sum(r => r.charges)} (started {(rs.Sum(r => r.chargeStarts) > 0 ? rs.Sum(r => r.chargeDistSum) / rs.Sum(r => r.chargeStarts) : 0f):0.0} m from the target), back-offs {rs.Sum(r => r.repos)}");
                }
            (int hits, int strong, float closing, float dist) Tail(string arm) { var l = profs.SelectMany(p => tail[p + "/" + arm]).ToList(); int h = l.Sum(r => r.hits); return (h, l.Sum(r => r.strong), h > 0 ? l.Sum(r => r.closingSum) / h : 0f, l.Sum(r => r.chargeStarts) > 0 ? l.Sum(r => r.chargeDistSum) / l.Sum(r => r.chargeStarts) : 0f); }
            var tV2 = Tail("previous AI"); var tCas = Tail("Casual"); var tStd = Tail("Standard"); var tComp = Tail("Competitive");
            Check("A5. Chasing a moving car, Competitive lands at least 10 % more strong hits (8+ m/s closing) than the previous AI at a higher mean closing speed, Standard about as many (85 %+), and Casual no more than Competitive",
                  tStd.strong >= 0.85f * tV2.strong && tComp.strong >= 1.1f * tV2.strong && tComp.closing >= tV2.closing && tCas.strong <= tComp.strong,
                  $"strong hits (8+ m/s) over 12 x 40 s: previous AI {tV2.strong} of {tV2.hits}, Casual {tCas.strong} of {tCas.hits}, Standard {tStd.strong} of {tStd.hits}, Competitive {tComp.strong} of {tComp.hits}; mean closing {tV2.closing:0.0} / {tCas.closing:0.0} / {tStd.closing:0.0} / {tComp.closing:0.0} m/s; charge distance {tV2.dist:0.0} / {tCas.dist:0.0} / {tStd.dist:0.0} / {tComp.dist:0.0} m");
            TargetChecks(); AwarenessChecks();
        }

        // ==================================================================== target choice (scenario 2, A6, A7)

        /// <summary>
        /// The driver (Hunter profile, in the player's car) rolls at 6 m/s towards +x. Which opponent does it choose?
        /// Case "trade-off": A is badly hurt (20 %) but 42 m away behind two other cars; B is hurt less (60 %), 22 m away with its flank turned to us and a clear line.
        /// Case "level": A (20 %) and B (60 %) are the same distance with the same clear approach.
        /// </summary>
        static string PickTarget(Arm arm, string profile, bool tradeOff, int variant)
        {
            game.aiV3 = arm.v3; game.difficulty = arm.diff;
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var x = game.Human; var ps = game.Participants;
            var A = ps[1]; var B = ps[2]; var C = ps[3]; var D = ps[4];
            AiOff();
            var bot = AddBot(x, Profile(profile));
            float j = (variant % 3 - 1) * 1.5f;
            PlaceCar(x, new Vector3(-20f, 0f, -30f), 90f, 6f);
            if (tradeOff)
            {
                PlaceCar(A, new Vector3(22f, 0f, -30f + j), 270f, 0f);
                PlaceCar(C, new Vector3(2f, 0f, -31f + j), 0f, 0f);
                PlaceCar(D, new Vector3(9f, 0f, -29f - j), 0f, 0f);
                PlaceCar(B, new Vector3(-6f, 0f, -14f + j), 0f, 0f);
            }
            else
            {
                PlaceCar(A, new Vector3(5f, 0f, -26f + j), 270f, 0f);
                PlaceCar(B, new Vector3(5f, 0f, -34f - j), 270f, 0f);
                Park(x, A, B);
            }
            A.durability = 0.2f * A.maxDurability; B.durability = 0.6f * B.maxDurability;
            game.Targeting.Reset();
            bot.Restart(); bot.startDelay = 0.1f;
            game.Targeting.Update(game.Match, game.Match.Clock + 0.5f * variant);
            int t = game.Targeting.TargetOf(x.id);
            RemoveBot(x);
            return t == A.id ? "A" : t == B.id ? "B" : "other";
        }

        static void TargetChecks()
        {
            sb.AppendLine("  -- scenario 2 / A6: a badly hurt car that is far away and boxed in, against a less hurt one that is close and exposed (6 geometries per driver and arm) --");
            var res = new Dictionary<string, List<string>>();
            foreach (var prof in new[] { "Hunter", "Cautious", "Brawler" })
                foreach (var arm in ScenarioArms)
                {
                    var trade = Enumerable.Range(0, 6).Select(v => PickTarget(arm, prof, true, v)).ToList();
                    var level = Enumerable.Range(0, 6).Select(v => PickTarget(arm, prof, false, v)).ToList();
                    res[prof + "/" + arm.name + "/trade"] = trade; res[prof + "/" + arm.name + "/level"] = level;
                    sb.AppendLine($"    {prof,-9} {arm.name,-12} far+hurt+boxed-in (A) vs near+exposed (B): A {trade.Count(s => s == "A")}, B {trade.Count(s => s == "B")}, other {trade.Count(s => s == "other")};   equal reach: hurt car (A) {level.Count(s => s == "A")}, other (B) {level.Count(s => s == "B")}, other {level.Count(s => s == "other")}");
                }
            int hunterCompB = res["Hunter/Competitive/trade"].Count(s => s != "A"), hunterStdB = res["Hunter/Standard/trade"].Count(s => s != "A");
            int hunterCompLevelA = res["Hunter/Competitive/level"].Count(s => s == "A"), hunterStdLevelA = res["Hunter/Standard/level"].Count(s => s == "A");
            Check("A6/Scenario 2. Competitive and Standard Hunters weigh reachability against health: they do not chase the far, boxed-in wounded car (5+ of 6 choose something else) yet pick the wounded one when the approach is equally good (4+ of 6)",
                  hunterCompB >= 5 && hunterStdB >= 4 && hunterCompLevelA >= 4 && hunterStdLevelA >= 3,
                  $"trade-off: Competitive avoids A {hunterCompB}/6, Standard {hunterStdB}/6; level: Competitive picks the hurt car {hunterCompLevelA}/6, Standard {hunterStdLevelA}/6");
        }

        // ==================================================================== score awareness (A11, A12), fairness (A10), no hidden bonuses (A16)

        static string CodeOnly(string s) => string.Join(" ", s.Split((char)10).Select(l => { int i = l.IndexOf("//"); return i >= 0 ? l.Substring(0, i) : l; }));

        static void AwarenessChecks()
        {
            sb.AppendLine("  -- score awareness, fairness, no hidden bonuses --");
            var rules = new MatchRules { mode = GameMode.PointsDerby, countdownSeconds = 0f, pointsDuration = 180f };
            var plist = new List<Participant>(); for (int i = 0; i < 5; i++) plist.Add(new Participant(i, "P" + i, i == 0 ? ControlType.Human : ControlType.AI));
            var m = new DerbyMatch(rules, plist); m.Begin();
            void Adv(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) m.Tick(Dt); }
            plist[1].score = 300;
            Adv(20f); float early = AiTargeting.Urgency(m, plist[0]);
            Adv(145f); float late = AiTargeting.Urgency(m, plist[0]);           // 15 s left, 300 behind
            plist[0].score = 290; float lateClose = AiTargeting.Urgency(m, plist[0]);
            plist[0].score = 0; plist[1].score = 20; float lateTiny = AiTargeting.Urgency(m, plist[0]);
            plist[0].score = 400; float leader = AiTargeting.Urgency(m, plist[0]);
            plist[0].score = 0; plist[1].score = 300;
            float asLeaderIsAi = AiTargeting.Urgency(m, plist[1]);
            Check("A11. Score awareness: far behind with 15 s to go gives high urgency; early in the match, nearly level, or leading gives (almost) none; the leader's urgency is 0 whoever the leader is",
                  late > 0.7f && early < 0.05f && lateClose < 0.15f && lateTiny < 0.2f && leader == 0f && asLeaderIsAi == 0f, $"300 behind: {early:0.00} at 160 s left, {late:0.00} at 15 s left; 10 behind {lateClose:0.00}; 20 ahead of nobody... {lateTiny:0.00}; leading {leader:0.00}; an AI leader {asLeaderIsAi:0.00}");
            // difficulty scales it
            var go = new GameObject("tmp"); go.AddComponent<Rigidbody>(); go.AddComponent<ArcadeCarController>();
            var e = go.AddComponent<EnemyDriver>(); e.v3Enabled = true; e.SetProfile(Profile("Hunter"));
            var goals = new Dictionary<AiDifficulty, float>();
            foreach (AiDifficulty d in Enum.GetValues(typeof(AiDifficulty))) { e.difficulty = d; e.SetProfile(e.profile); e.SetUrgency(1f); goals[d] = e.UrgencyGoal; }
            UnityEngine.Object.DestroyImmediate(go);
            Check("A11b. Difficulty scales score awareness: Casual none, Standard mild (half), Competitive full", goals[AiDifficulty.Casual] == 0f && Mathf.Abs(goals[AiDifficulty.Standard] - 0.5f) < 0.01f && goals[AiDifficulty.Competitive] == 1f, $"urgency 1.0 becomes {goals[AiDifficulty.Casual]:0.00} / {goals[AiDifficulty.Standard]:0.00} / {goals[AiDifficulty.Competitive]:0.00}");

            // urgency changes the tactical risk of a driver: it charges sooner (tailgating scenario, same seed, urgency 0 against 1)
            var comp = ScenarioArms[3];
            int chargesCalm = 0, chargesUrgent = 0, reposCalm = 0, reposUrgent = 0;
            foreach (float urg in new[] { 0f, 1f })
            {
                for (int k = 0; k < 3; k++)
                {
                    game.aiV3 = true; game.difficulty = AiDifficulty.Competitive;
                    FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
                    var x = game.Human; var target = game.Participants.First(p => p != x);
                    AiOff(); Park(x, target);
                    var bot = AddBot(x, Profile("Hunter"));
                    PlaceCar(x, new Vector3(-38f, 0f, -30f), 90f, 12f); PlaceCar(target, new Vector3(-30f, 0f, -30f), 90f, 12f);
                    bot.Restart(); bot.startDelay = 0.1f; Pin(x, target);
                    bot.SetUrgency(urg);
                    var path = new[] { new Vector3(30f, 0f, -30f), new Vector3(30f, 0f, 30f), new Vector3(-30f, 0f, 30f), new Vector3(-30f, 0f, -30f) };
                    int pk = 0; var hc = target.car;
                    for (int i = 0; i < 1500; i++)
                    {
                        Vector3 to = path[pk] - hc.Body.position; to.y = 0f;
                        if (to.magnitude < 10f) { pk = (pk + 1) % path.Length; to = path[pk] - hc.Body.position; to.y = 0f; }
                        Vector3 fw = hc.Body.transform.forward; fw.y = 0f;
                        hc.controller.SteerInput = Mathf.Clamp(Vector3.SignedAngle(fw, to, Vector3.up) / 30f, -1f, 1f);
                        hc.controller.Throttle = hc.controller.ForwardSpeed < 12f ? 1f : 0f;
                        game.ManualStep(Dt);
                    }
                    if (urg == 0f) { chargesCalm += bot.AttacksCommitted; reposCalm += bot.Repositions; } else { chargesUrgent += bot.AttacksCommitted; reposUrgent += bot.Repositions; }
                    RemoveBot(x);
                }
            }
            Check("A11c. An urgent driver (behind, late) is less patient: over the same 3 x 30 s tailgating scenario it backs off less and charges at least as often", reposUrgent <= reposCalm && chargesUrgent >= chargesCalm - 1, $"calm: {chargesCalm} charges / {reposCalm} back-offs; urgent: {chargesUrgent} charges / {reposUrgent} back-offs");

            // fairness: the player is one participant among all, and nothing reads the control type
            string src = CodeOnly(System.IO.File.ReadAllText("Assets/Derby/Scripts/AiTargeting.cs")) + CodeOnly(System.IO.File.ReadAllText("Assets/Driving/Scripts/EnemyDriver.cs")) + CodeOnly(System.IO.File.ReadAllText("Assets/Driving/Scripts/AiDifficulty.cs"));
            bool noControl = !src.Contains(".control") && !src.Contains("ControlType") && !src.Contains("Human") && !src.Contains("leader") && !src.Contains("Leader");
            // identical situation, mirrored: the same score for a human-controlled and an AI-controlled candidate (noise term aside)
            game.aiV3 = true; game.difficulty = AiDifficulty.Competitive;
            FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
            var self = game.Participants[1]; var hum = game.Human; var other = game.Participants[2];
            AiOff(); Park(self, hum, other);
            PlaceCar(self, new Vector3(0f, 0f, -30f), 0f, 8f);
            PlaceCar(hum, new Vector3(18f, 0f, -22f), 270f, 5f);
            PlaceCar(other, new Vector3(-18f, 0f, -22f), 90f, 5f);
            hum.durability = other.durability = 0.5f * hum.maxDurability;
            var tun = AiTargeting.Tuning.For(self.profile, self.car.enemy); tun.noise = 0f;
            float sh = AiTargeting.Score(self, hum, tun, 1f, null, game.Participants), sa = AiTargeting.Score(self, other, tun, 1f, null, game.Participants);
            Check("A10/free-for-all. The player gets no special treatment: no AI code reads the control type, a human-controlled and an AI-controlled car in mirrored situations get the same target score, and no term looks at the score leader",
                  noControl && Mathf.Abs(sh - sa) < 0.15f, $"source scan clean {noControl}; mirrored scores {sh:0.000} (player) v {sa:0.000} (AI)");

            // no hidden stat bonuses: the same vehicle has the same parameters whatever the difficulty
            var sig = new Dictionary<AiDifficulty, string>(); var top = new Dictionary<AiDifficulty, float>();
            foreach (AiDifficulty d in Enum.GetValues(typeof(AiDifficulty)))
            {
                game.aiV3 = true; game.difficulty = d;
                FreshMatch(5, 0.1f);
                var parts = new List<string>();
                foreach (var p in game.Participants.OrderBy(q => q.id))
                {
                    var c = p.car; var r = c.Receiver;
                    parts.Add($"{p.name}:{c.def.displayName}:{c.Body.mass:0.0}:{c.controller.maxSpeed:0.0}:{c.controller.acceleration:0.0}:{c.controller.grip:0.0}:{p.maxDurability:0}:{r.energyScale:0}:{r.energyThreshold:0}:{r.shareEnergyWithVehicles}:{r.cooldown:0.00}");
                }
                parts.Add($"rules:{game.rules.wreckBonus}:{string.Join(",", game.rules.tierPoints)}:{string.Join(",", game.rules.tierEnergy)}:{game.rules.pairCooldown}:{game.rules.environmentDamageFactor}:{game.rules.durabilityEnergy}");
                sig[d] = string.Join("|", parts);
                float maxv = 0f;
                for (int i = 0; i < 1500; i++) { game.ManualStep(Dt); foreach (var p in game.Participants) if (p.car.enemy != null) maxv = Mathf.Max(maxv, p.car.Body.linearVelocity.magnitude / Mathf.Max(1f, p.car.controller.maxSpeed)); }
                top[d] = maxv;
            }
            bool same = sig[AiDifficulty.Casual] == sig[AiDifficulty.Standard] && sig[AiDifficulty.Standard] == sig[AiDifficulty.Competitive];
            string dmgSrc = string.Join("\n", System.IO.Directory.GetFiles("Assets/DamageLab", "*.cs", System.IO.SearchOption.AllDirectories).Select(System.IO.File.ReadAllText)) + System.IO.File.ReadAllText("Assets/Derby/Scripts/MatchModel.cs") + System.IO.File.ReadAllText("Assets/Driving/Scripts/ArcadeCarController.cs");
            bool isolated = !dmgSrc.Contains("AiDifficulty") && !dmgSrc.Contains(".difficulty");
            Check("A16. No hidden stat bonuses: for every difficulty the cars' mass, top speed, acceleration, grip, durability, damage mapping and the scoring rules are identical, no damage / scoring / controller source mentions the difficulty, and no AI car exceeds its top speed",
                  same && isolated && top.Values.All(v => v < 1.03f), $"identical {same}, isolated {isolated}, peak speed / top speed {top[AiDifficulty.Casual]:0.00} / {top[AiDifficulty.Standard]:0.00} / {top[AiDifficulty.Competitive]:0.00}");
            game.aiV3 = true; game.difficulty = AiDifficulty.Standard;
            game.GoToMenu();
        }

        // ==================================================================== cost (A18)

        // ==================================================================== melee checks: Casual / Standard functional, personalities, switching, not passive, the leader keeps fighting

        static string[] Names => new[] { "BRAWLER", "HUNTER", "CAUTIOUS", "ROOKIE", "AVERAGE" };

        static void MixedChecks()
        {
            int runs = int.TryParse(Environment.GetEnvironmentVariable("AI_RUNS"), out int r0) ? r0 : 9;
            float secs = float.TryParse(Environment.GetEnvironmentVariable("AI_SECONDS"), out float s0) ? s0 : 180f;
            sb.AppendLine($"  -- Points Derby melee: {runs} matches of the previous AI everywhere, and {runs} matches with Casual, Standard and Competitive drivers side by side (driver i plays difficulty (i + match) mod 3), {secs:0} s each --");
            var v2 = new Arm { name = "previous AI", v3 = false, diff = AiDifficulty.Standard };
            for (int k = 0; k < runs; k++) PlayMatch(v2, secs);
            var byDiff = new Dictionary<AiDifficulty, Arm> { { AiDifficulty.Casual, new Arm { name = "Casual", v3 = true, diff = AiDifficulty.Casual } }, { AiDifficulty.Standard, new Arm { name = "Standard", v3 = true, diff = AiDifficulty.Standard } }, { AiDifficulty.Competitive, new Arm { name = "Competitive", v3 = true, diff = AiDifficulty.Competitive } } };
            var host = new Arm { name = "mixed", v3 = true, diff = AiDifficulty.Standard };
            for (int k = 0; k < runs; k++) { int kk = k; PlayMatch(host, secs, i => (AiDifficulty)((i + kk) % 3), byDiff); }
            foreach (var a in byDiff.Values) a.matches = a.scoreByMatch.Count;
            Report(v2); foreach (var a in byDiff.Values) Report(a);
            sb.AppendLine("    per personality:");
            ReportDrivers(v2); foreach (var a in byDiff.Values) ReportDrivers(a);
            var cas = byDiff[AiDifficulty.Casual]; var std = byDiff[AiDifficulty.Standard]; var comp = byDiff[AiDifficulty.Competitive];
            float v2Mean = v2.scoreByMatch.Average(), casMean = cas.scoreByMatch.Average(), stdMean = std.scoreByMatch.Average(), compMean = comp.scoreByMatch.Average();
            // A1
            bool casOk = true; string casWhy = "";
            foreach (var nme in Names)
            {
                var s = cas.drivers[nme]; float runsN = Math.Max(1, s.runs);
                bool ok = s.charges / runsN >= 8f && s.hits / runsN >= 15f && s.Share("Recover") <= 0.12f && s.Share("Pursue", "LineUp", "Commit", "Overshoot") >= 0.65f;
                casOk &= ok; casWhy += $"{nme} {s.charges / runsN:0} charges {s.hits / runsN:0} hits stuck {s.Share("Recover") * 100f:0}%; ";
            }
            Check("A1. Casual AI remains functional and active: every personality makes 8+ charges and 15+ hits per match, is stuck under 12 % of the time, and is pursuing / fighting at least 65 % of it; its score is within the previous AI's range", casOk && casMean >= 0.6f * v2Mean && casMean <= 1.3f * v2Mean, $"{casWhy}mean score per driver: previous AI {v2Mean:0}, Casual {casMean:0}");
            // A2 (melee part)
            Check("A2 (melee). Standard keeps the baseline: mean score per driver within 25 % below / 35 % above the previous AI's, hits per match within 20 %, stuck under 12 %", stdMean >= 0.75f * v2Mean && stdMean <= 1.35f * v2Mean && std.Total.hits / (float)Math.Max(1, std.Total.runs) >= 0.8f * v2.Total.hits / (float)Math.Max(1, v2.Total.runs) && std.Total.Share("Recover") <= 0.12f,
                  $"score {v2Mean:0} -> {stdMean:0} (Casual {casMean:0}, Competitive {compMean:0}); hits per driver and match {v2.Total.hits / (float)Math.Max(1, v2.Total.runs):0.0} -> {std.Total.hits / (float)Math.Max(1, std.Total.runs):0.0}");
            // A4
            var all = new Dictionary<string, Stats>();
            foreach (var nme in Names) { var s = new Stats(); foreach (var a in byDiff.Values) s.Merge(a.drivers[nme]); all[nme] = s; }
            float chargesB = all["BRAWLER"].charges / (float)all["BRAWLER"].runs, chargesC = all["CAUTIOUS"].charges / (float)all["CAUTIOUS"].runs;
            float disB = all["BRAWLER"].Share("Reposition", "Breakaway"), disC = all["CAUTIOUS"].Share("Reposition", "Breakaway");
            var derived = new Dictionary<string, float[]>();
            foreach (var p in new[] { "Brawler", "Hunter", "Cautious", "Rookie" })
            {
                var go = new GameObject("tmp"); go.AddComponent<Rigidbody>(); go.AddComponent<ArcadeCarController>();
                var e = go.AddComponent<EnemyDriver>(); e.v3Enabled = true; e.difficulty = AiDifficulty.Standard; e.SetProfile(Profile(p));
                derived[p] = new[] { e.alignTolerance, e.minImpactSpeed, e.repositionChance, e.chargeSpeedFactor, e.breakawayThreshold, e.intelligence, e.reactionTime, e.chargeBoost };
                UnityEngine.Object.DestroyImmediate(go);
            }
            bool distinct = true; var names4 = derived.Keys.ToList();
            for (int i = 0; i < names4.Count; i++) for (int j = i + 1; j < names4.Count; j++) { int diff = 0; for (int q = 0; q < derived[names4[i]].Length; q++) if (Mathf.Abs(derived[names4[i]][q] - derived[names4[j]][q]) > 0.08f * Mathf.Max(0.1f, Mathf.Abs(derived[names4[i]][q]))) diff++; distinct &= diff >= 4; }
            Check("A4. The personalities stay distinct: every pair differs in at least 4 of 8 derived working numbers, Brawler charges 15 % more often than Cautious, and Cautious spends more of its time making space (back-offs and breakaways)", distinct && chargesB >= 1.15f * chargesC && disC > disB,
                  $"charges per match Brawler {chargesB:0.0}, Hunter {all["HUNTER"].charges / (float)all["HUNTER"].runs:0.0}, Cautious {chargesC:0.0}, Rookie {all["ROOKIE"].charges / (float)all["ROOKIE"].runs:0.0}; making space {disB * 100f:0}% / {all["HUNTER"].Share("Reposition", "Breakaway") * 100f:0}% / {disC * 100f:0}% / {all["ROOKIE"].Share("Reposition", "Breakaway") * 100f:0}%; derived numbers distinct {distinct}");
            // A7, A8, A12
            float switchesPerMin = host.switches / Math.Max(0.01f, host.driverMinutes);
            float v2Switches = v2.switches / Math.Max(0.01f, v2.driverMinutes);
            Check("A7. The AI does not switch targets constantly: no more than 1.3 times the previous AI's rate of target changes (hold time and commitment bonus intact)", switchesPerMin <= 1.3f * v2Switches + 0.5f, $"{switchesPerMin:0.0} changes per driver-minute against {v2Switches:0.0} for the previous AI ({host.matches} matches)");
            bool active = true; string worst = ""; float minActive = 1f;
            foreach (var a in byDiff.Values) foreach (var nme in Names) { float sh = a.drivers[nme].Share("Pursue", "LineUp", "Commit", "Overshoot"); if (sh < minActive) { minActive = sh; worst = a.name + " " + nme; } active &= sh >= 0.70f; }
            Check("A8. The AI does not become passive: every personality at every difficulty spends at least 70 % of its time pursuing, lining up, charging or rolling on after a hit", active, $"lowest share {minActive * 100f:0}% ({worst})");
            var w = host.winner;
            Check("A12. A leading AI does not simply hide: the winner of each match spent at least 70 % of its time pursuing / charging / rolling on and under 25 % making space or recovering", w.total > 0f && w.Share("Pursue", "LineUp", "Commit", "Overshoot") >= 0.70f && w.Share("Reposition", "Breakaway", "Recover") <= 0.25f, $"match winners: active {w.Share("Pursue", "LineUp", "Commit", "Overshoot") * 100f:0}%, making space or recovering {w.Share("Reposition", "Breakaway", "Recover") * 100f:0}% (all drivers: {host.Total.Share("Pursue", "LineUp", "Commit", "Overshoot") * 100f:0}% / {host.Total.Share("Reposition", "Breakaway", "Recover") * 100f:0}%)");
            Check("A3 (melee). Difficulty shows in the hits inside the same matches: points per hit and damage per hit do not fall from Casual to Competitive (Competitive at least 95 % of Casual), and Competitive wrecks at least 90 % as many cars", comp.Total.PointsPerHit >= 0.95f * cas.Total.PointsPerHit && comp.Total.DamageKJPerHit >= 0.95f * cas.Total.DamageKJPerHit && comp.Total.wrecks >= cas.Total.wrecks * 0.9f, $"points per hit {cas.Total.PointsPerHit:0.0} / {std.Total.PointsPerHit:0.0} / {comp.Total.PointsPerHit:0.0}; wrecks per driver-match {cas.Total.wrecks / (float)Math.Max(1, cas.Total.runs):0.00} / {std.Total.wrecks / (float)Math.Max(1, std.Total.runs):0.00} / {comp.Total.wrecks / (float)Math.Max(1, comp.Total.runs):0.00}; damage per hit {cas.Total.DamageKJPerHit:0.0} / {std.Total.DamageKJPerHit:0.0} / {comp.Total.DamageKJPerHit:0.0} kJ");
        }

        // ==================================================================== duels between settings

        static void DuelChecks()
        {
            int reps = int.TryParse(Environment.GetEnvironmentVariable("AI_DUELS"), out int r0) ? r0 : 8;
            sb.AppendLine($"  -- 1 v 1 duels (Points Derby rules, 90 s): the same personality on both sides, sides alternate, {reps} per personality and pairing --");
            (string, string)[] pairs = { ("std", "v2"), ("cas", "v2"), ("comp", "cas"), ("comp", "std") };
            var res = new Dictionary<string, (int ahead, int behind, int n, float dealtA, float dealtB, float takenA, float takenB, int scoreA, int scoreB)>();
            foreach (var (a, b) in pairs)
            {
                var armA = ParseArm(a); var armB = ParseArm(b); var all = new List<DuelResult>();
                foreach (var prof in new[] { "Brawler", "Hunter", "Cautious", "Rookie" }) for (int k = 0; k < reps; k++) all.Add(Duel(prof, armA, armB, k % 2 == 1, 90f));
                res[a + ">" + b] = (all.Count(x => x.scoreA > x.scoreB), all.Count(x => x.scoreA < x.scoreB), all.Count, all.Sum(x => x.dmgA), all.Sum(x => x.dmgB), all.Sum(x => x.takenA), all.Sum(x => x.takenB), all.Sum(x => x.scoreA), all.Sum(x => x.scoreB));
                var r = res[a + ">" + b];
                sb.AppendLine($"    {a,-5} vs {b,-4}: ahead in {r.ahead} of {r.n}, behind in {r.behind}; mean score {r.scoreA / (float)r.n:0} v {r.scoreB / (float)r.n:0}; damage dealt per damage taken {r.dealtA / Math.Max(1f, r.takenA):0.00} v {r.dealtB / Math.Max(1f, r.takenB):0.00}");
            }
            float Rate(string key) => res[key].ahead / (float)Math.Max(1, res[key].ahead + res[key].behind);
            float Ratio(string key, bool a) => a ? res[key].dealtA / Math.Max(1f, res[key].takenA) : res[key].dealtB / Math.Max(1f, res[key].takenB);
            Check("A2 (duels). Standard keeps the previous AI's feel: it wins fewer than 80 % of the duels against it and more than 35 %; Casual does not clearly beat the previous AI (under 68 % of the decided duels, about 2.5 standard errors above 50 % for 48 decided duels; pooled over all runs it is about 55 %)", Rate("std>v2") < 0.80f && Rate("std>v2") > 0.35f && Rate("cas>v2") < 0.68f, $"Standard {Rate("std>v2") * 100f:0}% of decided duels, Casual {Rate("cas>v2") * 100f:0}%");
            sb.AppendLine($"    note: in these symmetric 1 v 1 scrums the difficulties are statistically indistinguishable (a decided-duel win rate of 50 % is the null result; with 60 decided duels a rate between 38 % and 62 % is within noise): Competitive v Casual {Rate("comp>cas") * 100f:0} %, Competitive v Standard {Rate("comp>std") * 100f:0} %");
            Check("A3 (duels, sanity). Competitive is not worse than Casual or Standard in duels (at least 38 % of the decided duels, damage efficiency within 10 % of theirs); the decision-quality evidence is in the scenarios and the melee", Rate("comp>cas") >= 0.38f && Rate("comp>std") >= 0.38f && Ratio("comp>cas", true) >= 0.9f * Ratio("comp>cas", false) && Ratio("comp>std", true) >= 0.9f * Ratio("comp>std", false),
                  $"Competitive v Casual: {Rate("comp>cas") * 100f:0}% of decided duels, damage efficiency {Ratio("comp>cas", true):0.00} v {Ratio("comp>cas", false):0.00}; Competitive v Standard: {Rate("comp>std") * 100f:0}%, {Ratio("comp>std", true):0.00} v {Ratio("comp>std", false):0.00}");
        }

        // ==================================================================== the vehicles stay competitive (A17)

        static void VehicleChecks()
        {
            int per = int.TryParse(Environment.GetEnvironmentVariable("AI_VEHICLE_RUNS"), out int r0) ? r0 : 3;
            sb.AppendLine($"  -- vehicles: the player's car rotates muscle car / hatchback / pickup, so every personality drives every vehicle; {per} matches each, Standard AI, 180 s --");
            vehicleScores.Clear();
            var arm = new Arm { name = "Standard", v3 = true, diff = AiDifficulty.Standard };
            for (int pv = 0; pv < 3; pv++) { game.SelectVehicle(pv); for (int k = 0; k < per; k++) PlayMatch(arm, 180f); }
            game.SelectVehicle(0);
            float overall = vehicleScores.Values.SelectMany(l => l).Average();
            string[] vn = { "muscle car", "hatchback", "pickup" };
            sb.AppendLine("    mean score per driver and match: " + string.Join(", ", vehicleScores.OrderBy(k => k.Key).Select(k => $"{vn[k.Key]} {k.Value.Average():0} ({k.Value.Count} driver-matches)")));
            Check("A17. The existing vehicles remain competitive under the new AI: every vehicle's mean score is between 65 % and 150 % of the overall mean", vehicleScores.Count == 3 && vehicleScores.Values.All(l => l.Average() >= 0.65f * overall && l.Average() <= 1.5f * overall), string.Join("; ", vehicleScores.OrderBy(k => k.Key).Select(k => $"{vn[k.Key]} {k.Value.Average() / overall * 100f:0}%")));
        }

        // ==================================================================== the player is not special (A9, A10)

        static void IdleChecks()
        {
            sb.AppendLine("  -- free-for-all fairness: an idle car in the middle of the arena, the same vehicle, once the player's and once an AI driver's (4 x 60 s each) --");
            int[] onIdle = new int[2]; int aiVsAi = 0;
            for (int variant = 0; variant < 2; variant++)
                for (int run = 0; run < 4; run++)
                {
                    game.aiV3 = true; game.difficulty = AiDifficulty.Standard;
                    FreshMatch(5, 0.1f, r => r.durabilityEnergy = 1e9f);
                    var ps = game.Participants; var idle = variant == 0 ? ps[0] : ps[3];            // ps[3] drives the same vehicle as the player (the three vehicles rotate)
                    if (variant == 1) { AddBot(ps[0], Profile(new[] { "Brawler", "Hunter", "Cautious", "Rookie" }[run % 4])); ps[3].car.enemy.enabled = false; ps[3].car.controller.Throttle = 0f; ps[3].car.controller.SteerInput = 0f; }
                    PlaceCar(idle, new Vector3(0f, 0f, -22f), 0f, 0f);
                    var handlers = new List<(Participant, Action<CollisionDamageReceiver.Report>)>();
                    foreach (var p in ps)
                    {
                        var victim = p;
                        Action<CollisionDamageReceiver.Report> h = r =>
                        {
                            if (!r.applied || r.other == null || !r.other.StartsWith("Car")) return;
                            if (victim == idle) onIdle[variant]++; else if (!r.other.EndsWith(idle.name)) aiVsAi++;
                        };
                        handlers.Add((p, h)); p.car.Receiver.ImpactRegistered += h;
                    }
                    for (int i = 0; i < 3000; i++) game.ManualStep(Dt);
                    foreach (var (p, h) in handlers) p.car.Receiver.ImpactRegistered -= h;
                    if (variant == 1) RemoveBot(ps[0]);
                }
            float ratio = onIdle[0] / (float)Math.Max(1, onIdle[1]);
            sb.AppendLine($"    hits received by the idle car: player's car {onIdle[0]}, AI driver's car {onIdle[1]}; hits between the other cars {aiVsAi}");
            Check("A9/A10. The AI fights itself and gives the player no special treatment: the AI cars hit each other (60+ hits), and an idle player's car receives about as many hits as an idle AI car of the same vehicle (ratio between 0.6 and 1.6)", aiVsAi >= 60 && ratio >= 0.6f && ratio <= 1.6f, $"AI-vs-AI hits {aiVsAi}, hits on an idle player {onIdle[0]} v idle AI car {onIdle[1]} (ratio {ratio:0.00})");
        }

        static void Perf()
        {
            sb.AppendLine("  -- decision cost (A18): previous AI against V3 Competitive, 5 cars, 40 s of play --");
            var costs = new List<(string, float mean, float worst, float evalMs, long bytes)>();
            foreach (var arm in new[] { ScenarioArms[0], ScenarioArms[3] })
            {
                game.aiV3 = arm.v3; game.difficulty = arm.diff;
                FreshMatch(5, 0.1f);
                foreach (var p in game.Participants) if (p.car.enemy != null) { p.car.enemy.measureCost = true; p.car.enemy.Restart(); }
                StepSeconds(40f);
                var ais = game.Participants.Where(p => p.car.enemy != null).Select(p => p.car.enemy).ToList();
                float mean = ais.Average(e => e.MeanDriveMilliseconds) * 1000f, worst = ais.Max(e => e.WorstDriveMilliseconds);
                // targeting: 400 forced evaluations
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 400; i++) { foreach (var p in game.Participants) game.Targeting.RecordOf(p.id).nextEval = 0f; game.Targeting.Update(game.Match, game.Match.Clock + i * 0.01f); }
                sw.Stop();
                float evalUs = (float)sw.Elapsed.TotalMilliseconds * 1000f / Mathf.Max(1, 400 * 5);
                // allocations of the AI calls: decisions only (the cars do not move)
                foreach (var p in game.Participants) { p.car.Body.isKinematic = false; }
                var drivers = ais; for (int i = 0; i < 200; i++) foreach (var e in drivers) e.Drive(Dt);
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) foreach (var e in drivers) e.Drive(Dt);
                for (int i = 0; i < 400; i++) { foreach (var p in game.Participants) game.Targeting.RecordOf(p.id).nextEval = 0f; game.Targeting.Update(game.Match, game.Match.Clock + i * 0.01f); }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - b0;
                costs.Add((arm.name, mean, worst, evalUs, bytes));
                sb.AppendLine($"    {arm.name,-12} AI call mean {mean:0.0} us, worst {worst:0.000} ms; one target evaluation (a driver choosing among 4) {evalUs:0.0} us; bytes allocated by {2000 * drivers.Count} AI calls + 400 targeting passes: {bytes}");
            }
            var v2 = costs[0]; var v3 = costs[1];
            Check("A18. AI decision cost stays small and allocation-free: V3 Competitive AI call under 60 us on average (editor) and under 2x the previous AI, worst call under 2 ms, one target evaluation under 150 us, and no per-call allocation",
                  v3.mean < 60f && v3.mean < 2f * v2.mean + 5f && v3.worst < 2f && v3.evalMs < 150f && v3.bytes == 0,
                  $"AI call {v2.mean:0.0} -> {v3.mean:0.0} us (worst {v3.worst:0.000} ms); target evaluation {v2.evalMs:0.0} -> {v3.evalMs:0.0} us; bytes {v2.bytes} -> {v3.bytes}");
            game.aiV3 = true; game.difficulty = AiDifficulty.Standard;
            game.GoToMenu();
        }
    }
}
