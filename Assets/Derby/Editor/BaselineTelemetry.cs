using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    /// Attachment V1 / milestone M0: READ-ONLY baseline telemetry. Plays AI-only matches in the real DerbyArena (real PhysX at 50 Hz through DerbyGame.ManualStep) and
    /// records, per car and match, what the current game does for each stock chassis: hits by tier (scored and reported), energy dealt and taken, points, wrecks,
    /// survival, Showdown outcomes and who finishes wrecks. It only subscribes to existing events and reads existing fields: no rule, value, scene or asset is changed.
    ///
    /// Roster: the human seat is driven by an EnemyDriver (as DerbyValidation.Balance does) and the player's vehicle rotates through the three bodies, so every match
    /// contains all three chassis (5 cars: 2+2+1) and each chassis meets every AI personality over the rotations.
    /// Environment: BASELINE_MODE = points | last (default points), BASELINE_MATCHES (default 30, rounded up to a multiple of 3), BASELINE_DIFFICULTY = casual | standard | competitive
    /// (default standard), BASELINE_OUT (default Docs/Attachments/baseline), BASELINE_LOADOUT = none | full (default none; full = ram plate, tow hitch and roof rack on
    /// all five cars, 7 capacity points, which fits every chassis: the M4 with/without comparison).
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.BaselineTelemetry.Run -quit -logFile baseline.log
    /// </summary>
    public static class BaselineTelemetry
    {
        const float Dt = 0.02f;
        const int Tiers = 5;   // index 0 = below the scoring threshold, 1..4 = the four scoring tiers

        class CarRec
        {
            public int seat; public string name, chassis, profile;
            public int score, hitsDealt, hitsTaken, wrecksDealt, timesWrecked;
            public double damageDealt, damageTaken;                       // MatchModel accounting (J)
            public int[] scoredTier = new int[Tiers];                     // scored hits by tier (1..4)
            public int[] reportedDealt = new int[Tiers], reportedTaken = new int[Tiers];   // applied receiver reports by victim-side tier
            public double reportedEnergyDealt, reportedEnergyTaken;
            public int envHits; public double envEnergy;                  // applied reports against something that is not a car
            public float survival; public bool survived, finalist, showdownWin;
            public int soloWrecks, assistedWrecks;                        // wrecks this car dealt: finisher did >= 50 % / < 50 % of the victim's damage in that life
        }

        class MatchRec
        {
            public string mode, difficulty; public int index, rotation; public float duration; public bool finished;
            public string result; public int winnerSeat = -1; public string winnerChassis = "";
            public bool showdown; public int showdownWinnerSeat = -1;
            public List<CarRec> cars = new List<CarRec>();
        }

        static DerbyGame game;
        static bool fullLoadout;
        static readonly string[] FullKit = { "spiked_ram_plate", "tow_hitch_bumper", "roof_cage_rack" };
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        [MenuItem("CarWreck/Attachments/Baseline Telemetry (M0)", priority = 47)]
        public static void Run()
        {
            string log = "";
            int code = 0;
            var prev = Physics.simulationMode;
            try { log = Execute(); }
            catch (Exception e) { log = "EXCEPTION: " + e; code = 1; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            Debug.Log(log);
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static string Execute()
        {
            string modeText = (Environment.GetEnvironmentVariable("BASELINE_MODE") ?? "points").ToLowerInvariant();
            var mode = modeText.StartsWith("last") ? GameMode.LastCarStanding : GameMode.PointsDerby;
            string diffText = (Environment.GetEnvironmentVariable("BASELINE_DIFFICULTY") ?? "standard").ToLowerInvariant();
            var difficulty = diffText.StartsWith("cas") ? AiDifficulty.Casual : diffText.StartsWith("comp") ? AiDifficulty.Competitive : AiDifficulty.Standard;
            int matches = int.TryParse(Environment.GetEnvironmentVariable("BASELINE_MATCHES"), out int n) ? Mathf.Max(3, n) : 30;
            matches = (matches + 2) / 3 * 3;
            string outDir = Environment.GetEnvironmentVariable("BASELINE_OUT") ?? "Docs/Attachments/baseline";
            fullLoadout = (Environment.GetEnvironmentVariable("BASELINE_LOADOUT") ?? "none").ToLowerInvariant().StartsWith("full");
            Directory.CreateDirectory(outDir);

            Physics.simulationMode = SimulationMode.Script;
            EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
            game.Setup();
            game.ui = null;
            game.difficulty = difficulty;
            game.participantCount = 5;
            game.rules.mode = mode;       // everything else stays at the scene's real rules

            var records = new List<MatchRec>();
            var wall = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < matches; i++)
            {
                int rotation = i % 3;
                records.Add(PlayMatch(i, rotation, mode, difficulty));
                if ((i + 1) % 5 == 0) Debug.Log($"[Baseline] {i + 1}/{matches} matches, {wall.Elapsed.TotalSeconds:0} s");
            }
            game.GoToMenu();

            string tag = (mode == GameMode.PointsDerby ? "points" : "last") + "-" + difficulty.ToString().ToLowerInvariant();
            File.WriteAllText(Path.Combine(outDir, $"matches-{tag}.csv"), Csv(records));
            string summary = Summary(records, mode, difficulty, wall.Elapsed.TotalSeconds);
            File.WriteAllText(Path.Combine(outDir, $"baseline-{tag}.md"), summary);
            return summary;
        }

        // ------------------------------------------------------------------ one match

        static MatchRec PlayMatch(int index, int rotation, GameMode mode, AiDifficulty difficulty)
        {
            game.GoToMenu();
            game.SelectVehicle(rotation);
            game.StartMatch();
            var match = game.Match;
            var parts = game.Participants.ToList();
            var human = game.Human;
            var hc = human.car;
            if (fullLoadout)
                foreach (var p in parts)
                {
                    var l = new Loadout(p.car.mount.VehicleKey);
                    foreach (var id in FullKit) l.Set(game.attachmentCatalog.items.First(d => d.id == id).slot, id);
                    p.loadout = l; p.car.mount.Apply(l);
                }
            var bot = hc.rig.root.AddComponent<EnemyDriver>();
            bot.receiver = hc.Receiver; bot.arenaHalf = game.arenaHalf;
            game.WireDriver(bot);
            bot.SetProfile(game.aiProfiles[1]); bot.Begin(game.aiSpeedKmh / 3.6f); bot.startDelay = 0.2f; bot.Restart();
            hc.enemy = bot;

            var rec = new MatchRec { mode = mode == GameMode.PointsDerby ? "points" : "last", difficulty = difficulty.ToString(), index = index, rotation = rotation };
            var bySeat = new Dictionary<int, CarRec>();
            var byRoot = new Dictionary<string, CarRec>();
            foreach (var p in parts)
            {
                var c = new CarRec { seat = p.id, name = p.name, chassis = game.vehicles[p.vehicleIndex].name, profile = p == human ? "bot-" + game.aiProfiles[1].displayName : (p.profile != null ? p.profile.displayName : "none") };
                rec.cars.Add(c); bySeat[p.id] = c; byRoot[p.car.rig.root.name] = c;
            }
            var pairDamage = new Dictionary<(int victim, int attacker), double>();
            var pendingWreck = new Dictionary<int, int>();   // victim seat -> finisher seat, resolved when the victim's own report handler runs

            match.Scored += (a, v, pts, tier) =>
            {
                int t = Array.IndexOf(game.rules.tierNames, tier);
                if (t >= 0 && bySeat.TryGetValue(a.id, out var ar)) ar.scoredTier[t + 1]++;
            };
            match.Wrecked += (v, credit) => { if (credit != null) pendingWreck[v.id] = credit.id; };   // (victim, credited finisher; null = nobody)

            var unsub = new List<Action>();
            foreach (var p in parts)
            {
                var victim = p; var vr = bySeat[p.id];
                Action<CollisionDamageReceiver.Report> handler = r =>
                {
                    if (!r.applied) return;
                    int tier = game.rules.TierFor(r.energy) + 1;
                    if (byRoot.TryGetValue(r.other ?? "", out var ar))
                    {
                        ar.reportedDealt[tier]++; ar.reportedEnergyDealt += r.energy;
                        vr.reportedTaken[tier]++; vr.reportedEnergyTaken += r.energy;
                        var key = (victim.id, ar.seat);
                        pairDamage.TryGetValue(key, out double d); pairDamage[key] = d + r.energy;
                    }
                    else { vr.envHits++; vr.envEnergy += r.energy; }
                    // our handler runs after the game's, so a wreck caused by this very report has already been announced
                    if (pendingWreck.TryGetValue(victim.id, out int finisher))
                    {
                        double total = 0, mine = 0;
                        foreach (var kv in pairDamage) if (kv.Key.victim == victim.id) { total += kv.Value; if (kv.Key.attacker == finisher) mine = kv.Value; }
                        if (bySeat.TryGetValue(finisher, out var fr)) { if (total <= 0 || mine / total >= 0.5) fr.soloWrecks++; else fr.assistedWrecks++; }
                        foreach (var key in pairDamage.Keys.Where(k => k.victim == victim.id).ToList()) pairDamage.Remove(key);
                        pendingWreck.Remove(victim.id);
                    }
                };
                p.car.Receiver.ImpactRegistered += handler;
                var receiver = p.car.Receiver;
                unsub.Add(() => receiver.ImpactRegistered -= handler);
            }

            int maxSteps = mode == GameMode.PointsDerby ? 9600 : 36000;
            for (int s = 0; s < maxSteps && match.State != MatchState.Results; s++) game.ManualStep(Dt);

            rec.finished = match.State == MatchState.Results;
            rec.duration = match.Clock;
            rec.result = match.ResultText;
            rec.showdown = match.ShowdownTriggered;
            if (match.Winner != null) { rec.winnerSeat = match.Winner.id; rec.winnerChassis = bySeat[match.Winner.id].chassis; }
            foreach (var p in parts)
            {
                var c = bySeat[p.id];
                c.score = p.score; c.hitsDealt = p.hitsDealt; c.hitsTaken = p.hitsTaken; c.wrecksDealt = p.wrecksDealt; c.timesWrecked = p.timesWrecked;
                c.damageDealt = p.damageDealt; c.damageTaken = p.damageTaken;
                c.survived = p.status != ParticipantStatus.Out;
                c.survival = p.status == ParticipantStatus.Out ? p.outAt : match.Clock;
                c.finalist = match.ShowdownTriggered && (match.Finalists[0] == p || match.Finalists[1] == p);
                c.showdownWin = c.finalist && match.Winner == p;
                if (c.showdownWin) rec.showdownWinnerSeat = p.id;
            }
            foreach (var u in unsub) u();
            hc.enemy = null; UnityEngine.Object.DestroyImmediate(bot);
            return rec;
        }

        // ------------------------------------------------------------------ output

        static string Csv(List<MatchRec> records)
        {
            var sb = new StringBuilder("mode,difficulty,match,rotation,finished,duration_s,seat,chassis,profile,score,hits_dealt,hits_taken,damage_dealt_J,damage_taken_J,wrecks_dealt,times_wrecked,solo_wrecks,assisted_wrecks,"
                + "scored_t1,scored_t2,scored_t3,scored_t4,rep_dealt_t0,rep_dealt_t1,rep_dealt_t2,rep_dealt_t3,rep_dealt_t4,rep_taken_t0,rep_taken_t1,rep_taken_t2,rep_taken_t3,rep_taken_t4,"
                + "rep_energy_dealt_J,rep_energy_taken_J,env_hits,env_energy_J,survival_s,survived,finalist,showdown_win,match_winner_seat\n");
            foreach (var m in records)
                foreach (var c in m.cars)
                    sb.Append(string.Join(",", new[]
                    {
                        m.mode, m.difficulty, m.index.ToString(), m.rotation.ToString(), m.finished ? "1" : "0", F(m.duration), c.seat.ToString(), c.chassis, c.profile,
                        c.score.ToString(), c.hitsDealt.ToString(), c.hitsTaken.ToString(), F(c.damageDealt), F(c.damageTaken), c.wrecksDealt.ToString(), c.timesWrecked.ToString(), c.soloWrecks.ToString(), c.assistedWrecks.ToString(),
                        c.scoredTier[1].ToString(), c.scoredTier[2].ToString(), c.scoredTier[3].ToString(), c.scoredTier[4].ToString(),
                        c.reportedDealt[0].ToString(), c.reportedDealt[1].ToString(), c.reportedDealt[2].ToString(), c.reportedDealt[3].ToString(), c.reportedDealt[4].ToString(),
                        c.reportedTaken[0].ToString(), c.reportedTaken[1].ToString(), c.reportedTaken[2].ToString(), c.reportedTaken[3].ToString(), c.reportedTaken[4].ToString(),
                        F(c.reportedEnergyDealt), F(c.reportedEnergyTaken), c.envHits.ToString(), F(c.envEnergy), F(c.survival), c.survived ? "1" : "0", c.finalist ? "1" : "0", c.showdownWin ? "1" : "0", m.winnerSeat.ToString(),
                    })).Append('\n');
            return sb.ToString();
        }

        static string F(double v) => v.ToString("0.###", Ci);

        static (double mean, double sd, double se) Stats(IEnumerable<double> xs)
        {
            var a = xs.ToArray();
            if (a.Length == 0) return (0, 0, 0);
            double mean = a.Average();
            double sd = a.Length > 1 ? Math.Sqrt(a.Sum(x => (x - mean) * (x - mean)) / (a.Length - 1)) : 0;
            return (mean, sd, a.Length > 1 ? sd / Math.Sqrt(a.Length) : 0);
        }

        static string Ms((double mean, double sd, double se) s, string fmt = "0.0") => $"{s.mean.ToString(fmt, Ci)} ± {s.se.ToString(fmt, Ci)}";

        static string Summary(List<MatchRec> records, GameMode mode, AiDifficulty difficulty, double wallSeconds)
        {
            bool points = mode == GameMode.PointsDerby;
            var rows = records.SelectMany(m => m.cars.Select(c => (m, c))).ToList();
            var chassis = rows.Select(r => r.c.chassis).Distinct().OrderBy(s => s).ToList();
            var sb = new StringBuilder();
            sb.AppendLine($"# Baseline telemetry: {(points ? "Points Derby" : "Last Car Standing")}, {difficulty} AI (milestone M0, generated)");
            sb.AppendLine();
            if (fullLoadout) { sb.AppendLine($"**Loadout: FULL on all five cars ({string.Join(", ", FullKit)}).** Compare against the stock-car run of the same protocol."); sb.AppendLine(); }
            sb.AppendLine($"{records.Count} AI-only matches ({records.Count(m => m.finished)} finished), 5 cars each, real DerbyArena rules and physics, stepped at 50 Hz in the editor ({wallSeconds:0} s wall time). " +
                "Seat 0 (the player's seat) is driven by a bot; the player's vehicle rotates through the three bodies, so every match has all three chassis. Cells are mean ± standard error over car-matches. " +
                "Nothing in the game was changed to produce this. Raw rows: the matching `matches-*.csv`.");
            sb.AppendLine();
            sb.AppendLine("## Per chassis (per car-match)");
            sb.AppendLine();
            sb.AppendLine("| Chassis | n | Score | Scored hits dealt | Hits taken | Energy dealt (kJ) | Energy taken (kJ) | Wrecks dealt | Times wrecked | " + (points ? "Match wins" : "Survival (s) | Reached Showdown | Showdown wins") + " |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|" + (points ? "---|" : "---|---|---|"));
            foreach (var ch in chassis)
            {
                var rs = rows.Where(r => r.c.chassis == ch).ToList();
                int wins = rs.Count(r => r.m.winnerSeat == r.c.seat);
                string tail = points ? $"{wins}/{rs.Count} ({100.0 * wins / rs.Count:0}%)"
                    : $"{Ms(Stats(rs.Select(r => (double)r.c.survival)))} | {rs.Count(r => r.c.finalist)}/{rs.Count} | {rs.Count(r => r.c.showdownWin)}/{Math.Max(1, rs.Count(r => r.c.finalist))}";
                sb.AppendLine($"| {ch} | {rs.Count} | {Ms(Stats(rs.Select(r => (double)r.c.score)))} | {Ms(Stats(rs.Select(r => (double)r.c.hitsDealt)))} | {Ms(Stats(rs.Select(r => (double)r.c.hitsTaken)))} | " +
                    $"{Ms(Stats(rs.Select(r => r.c.reportedEnergyDealt / 1000.0)))} | {Ms(Stats(rs.Select(r => r.c.reportedEnergyTaken / 1000.0)))} | {Ms(Stats(rs.Select(r => (double)r.c.wrecksDealt)))} | {Ms(Stats(rs.Select(r => (double)r.c.timesWrecked)))} | {tail} |");
            }
            sb.AppendLine();
            sb.AppendLine("A chance win rate for one car in a five-car match is 20 %.");
            sb.AppendLine();
            sb.AppendLine("## Scored hits by tier (share of each chassis' scored hits)");
            sb.AppendLine();
            sb.AppendLine("| Chassis | Glancing | Solid | Heavy | Devastating | Scored hits per minute |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var ch in chassis)
            {
                var rs = rows.Where(r => r.c.chassis == ch).ToList();
                var t = new double[Tiers]; foreach (var r in rs) for (int i = 1; i < Tiers; i++) t[i] += r.c.scoredTier[i];
                double tot = Math.Max(1, t.Sum());
                double minutes = rs.Sum(r => r.m.duration) / 60.0;
                sb.AppendLine($"| {ch} | {100 * t[1] / tot:0}% | {100 * t[2] / tot:0}% | {100 * t[3] / tot:0}% | {100 * t[4] / tot:0}% | {tot / Math.Max(0.01, minutes):0.00} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Finishing blows (wreck stealing)");
            sb.AppendLine();
            sb.AppendLine("A wreck is 'assisted' when the car that wrecked the victim had done less than half of the damage that victim took in that life. The wreck bonus (150 points) goes to the finisher either way.");
            sb.AppendLine();
            sb.AppendLine("| Chassis | Wrecks dealt | Solo | Assisted | Assisted share |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var ch in chassis)
            {
                var rs = rows.Where(r => r.c.chassis == ch).ToList();
                int solo = rs.Sum(r => r.c.soloWrecks), ass = rs.Sum(r => r.c.assistedWrecks);
                sb.AppendLine($"| {ch} | {rs.Sum(r => r.c.wrecksDealt)} | {solo} | {ass} | {(solo + ass > 0 ? (100.0 * ass / (solo + ass)).ToString("0") + "%" : "n/a")} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Per AI personality (to show how much of the spread is the driver, not the chassis)");
            sb.AppendLine();
            sb.AppendLine("| Profile | n | Score | Scored hits dealt | Wrecks dealt | Times wrecked |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var pr in rows.Select(r => r.c.profile).Distinct().OrderBy(s => s))
            {
                var rs = rows.Where(r => r.c.profile == pr).ToList();
                sb.AppendLine($"| {pr} | {rs.Count} | {Ms(Stats(rs.Select(r => (double)r.c.score)))} | {Ms(Stats(rs.Select(r => (double)r.c.hitsDealt)))} | {Ms(Stats(rs.Select(r => (double)r.c.wrecksDealt)))} | {Ms(Stats(rs.Select(r => (double)r.c.timesWrecked)))} |");
            }
            sb.AppendLine();
            sb.AppendLine("## Match level");
            sb.AppendLine();
            var dur = Stats(records.Select(m => (double)m.duration));
            sb.AppendLine($"- Mean match length {dur.mean:0} s (sd {dur.sd:0}); unfinished matches: {records.Count(m => !m.finished)}.");
            if (!points)
            {
                int sd = records.Count(m => m.showdown);
                sb.AppendLine($"- Final Showdown reached in {sd} of {records.Count} matches; Showdown decided by a win (not a draw) in {records.Count(m => m.showdown && m.showdownWinnerSeat >= 0)} of them.");
            }
            sb.AppendLine($"- Draws: {records.Count(m => m.winnerSeat < 0 && m.finished)}.");
            sb.AppendLine($"- Applied hits against non-car objects (environment): {rows.Sum(r => r.c.envHits)} in total across all cars.");
            sb.AppendLine();
            sb.AppendLine("## How to read this, and its limits");
            sb.AppendLine();
            sb.AppendLine(fullLoadout ? "- These are the current game's numbers with the full kit above on every car. Compare them only against a stock run of the same protocol (same mode, difficulty, match count and roster rotation)."
                : "- These are the current game's numbers for the stock cars with no attachments. A later attachment build must be compared against the same protocol (same mode, difficulty, match count and roster rotation), never against a human or a different setup.");
            sb.AppendLine("- Standard errors assume car-matches are independent; cars in one match are not (they hit each other), so treat the intervals as optimistic and compare chassis only when differences are several standard errors.");
            sb.AppendLine("- The seat-0 bot is built like `DerbyValidation.Balance` and does not get the game's hulk-avoidance hook, so it is a slightly different driver from the AI cars. The per-personality table shows how much that matters.");
            sb.AppendLine("- Editor physics at a fixed 50 Hz step is not the WebGL build on a phone; this is a baseline of rules and balance, not of performance.");
            sb.AppendLine("- Human play is not represented.");
            return sb.ToString();
        }
    }
}
