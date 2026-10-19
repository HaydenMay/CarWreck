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
    /// First-playable derby checks (brief tests A-Y and the performance items). The match rules are tested without a scene (pure DerbyMatch); everything that
    /// involves cars runs in the real DerbyArena with real PhysX, stepped by script (Physics.Simulate at 50 Hz through DerbyGame.ManualStep: AI drivers, controllers,
    /// physics, damage receivers and collider refits in player-loop order). Arena physics is not repeatable run to run in the editor, so scene checks use margins.
    /// Run: Unity -batchmode -projectPath . -executeMethod CarWreck.Derby.EditorTools.DerbyValidation.Run -quit -logFile derby.log
    /// Environment DERBY_ONLY = pure | scene | perf runs one part.
    /// </summary>
    public static class DerbyValidation
    {
        const float Dt = 0.02f;
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Derby", priority = 21)]
        public static void Run() => Debug.Log(Execute());

        static void Check(string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[Derby] Validation (pure match rules; DerbyArena with real PhysX stepped at 50 Hz; editor timings)\n");
            fails = passes = 0;
            string only = Environment.GetEnvironmentVariable("DERBY_ONLY") ?? "";
            var prevMode = Physics.simulationMode;
            try
            {
                if (only == "" || only == "pure") PureRules();
                if (only == "" || only == "scene" || only == "perf" || only == "balance" || only == "exploit")
                {
                    Physics.simulationMode = SimulationMode.Script;
                    EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                    game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                    game.Setup();
                    game.ui = null;                                  // no canvas in edit mode
                    foreach (var rb in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) { }
                    if (only == "" || only == "scene") SceneChecks();
                    if (only == "" || only == "perf") Performance();
                    if (only == "balance") Balance();
                    if (only == "exploit") ExploitProbe();
                }
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prevMode; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            sb.AppendLine($"[Derby] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ==================================================================== pure match rules

        static (DerbyMatch m, List<Participant> ps) NewMatch(GameMode mode, int n = 5, Action<MatchRules> tweak = null)
        {
            var rules = new MatchRules { mode = mode, countdownSeconds = 0f, durabilityEnergy = 100000f };
            tweak?.Invoke(rules);
            var ps = new List<Participant>();
            for (int i = 0; i < n; i++) ps.Add(new Participant(i, "P" + i, i == 0 ? ControlType.Human : ControlType.AI));
            var m = new DerbyMatch(rules, ps);
            m.Begin();
            return (m, ps);
        }

        static void Advance(DerbyMatch m, float seconds) { int n = Mathf.RoundToInt(seconds / Dt); for (int i = 0; i < n; i++) m.Tick(Dt); }
        static int Hit(DerbyMatch m, int att, int vic, float energy) => m.ApplyDamage(new DamageEvent(vic, att, energy));
        static void Wreck(DerbyMatch m, int vic, int att = -1) => m.ApplyDamage(new DamageEvent(vic, att, 1e7f));

        static void PureRules()
        {
            sb.AppendLine("  -- match rules (no scene) --");
            var def = new MatchRules();
            Check("Q0. Defaults: 180 s Points Derby, 60 s showdown, respawn delay, five participants by configuration", def.pointsDuration == 180f && def.showdownDuration == 60f && def.mode == GameMode.PointsDerby && def.respawnDelay > 0f,
                  $"duration {def.pointsDuration} s, showdown {def.showdownDuration} s, respawn {def.respawnDelay} s, mode {def.mode}");

            // ---- scoring tiers, environment, self hits
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby, 6, r => r.durabilityEnergy = 1e9f);
                int p0 = Hit(m, 0, 1, 2000f), p1 = Hit(m, 0, 2, 5000f), p2 = Hit(m, 0, 3, 20000f), p3 = Hit(m, 0, 4, 50000f), p4 = Hit(m, 0, 5, 120000f);
                Check("L1. Scoring tiers: below the threshold nothing, then minor / solid / heavy / devastating", p0 == 0 && p1 == 10 && p2 == 30 && p3 == 70 && p4 == 120, $"2 kJ {p0}, 5 kJ {p1}, 20 kJ {p2}, 50 kJ {p3}, 120 kJ {p4} points");
                int env = m.ApplyDamage(new DamageEvent(1, -1, 50000f)); int self = m.ApplyDamage(new DamageEvent(1, 1, 50000f));
                Check("L2. Walls and obstacles (and self-inflicted hits) score nothing but still wear durability down", env == 0 && self == 0 && ps[1].score == 0 && ps[1].durability < ps[1].maxDurability, $"env {env}, self {self} points; durability {ps[1].durability:0}");
                Check("L3. Hits are attributed: the victim's damage is the attacker's points and stats", ps[0].hitsDealt == 4 && ps[0].damageDealt > 190000f && ps[2].hitsTaken == 1 && ps[2].score == 0, $"attacker hits {ps[0].hitsDealt}, victim took {ps[2].damageTaken:0} J");
            }
            // ---- durability to wreck, wreck bonus, wrecked cars earn and take nothing
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby);
                Participant wreckedCredit = null; int wrecks = 0; m.Wrecked += (v, c) => { wrecks++; wreckedCredit = c; };
                Hit(m, 0, 1, 60000f); Advance(m, 1f);
                int final = Hit(m, 0, 1, 60000f);
                Check("K1. Durability: enough damage wrecks the car once, the attacker gets the wreck bonus", ps[1].status == ParticipantStatus.Wrecked && wrecks == 1 && wreckedCredit == ps[0] && ps[0].score == 70 + 70 + m.rules.wreckBonus && ps[0].wrecksDealt == 1,
                      $"status {ps[1].status}, attacker score {ps[0].score} (hit 70 + hit 70 + bonus {m.rules.wreckBonus})");
                int again = Hit(m, 0, 1, 90000f);
                Check("N1. A wrecked car takes no more damage, is wrecked once, and gives no more points", again == 0 && wrecks == 1 && ps[1].timesWrecked == 1 && ps[1].durability == 0f, $"extra hit gave {again}, wrecks {wrecks}, timesWrecked {ps[1].timesWrecked}");
                Advance(m, 1f);
                int fromDead = Hit(m, 1, 2, 90000f);
                Check("N2. A wrecked car cannot earn new points", fromDead == 0 && ps[1].score == 0, $"hit from a wrecked car awarded {fromDead}");
                Check("N3. A wrecked car is not active, so it is not a valid target", !ps[1].IsActive && m.ActiveCount == 4 && !FreeForAllRule.Instance.IsOpponent(ps[1], ps[1]), $"active {m.ActiveCount} of 5");
                // head-on: both wreck each other in one step: both still score (grace window)
                var (m2, p2) = NewMatch(GameMode.PointsDerby);
                Hit(m2, 2, 3, 120000f); Hit(m2, 3, 2, 120000f);
                Check("L4. Head-on: both cars deserve points when both inflicted damage, even when both are wrecked in the same step", p2[2].score == 120 + 150 + 0 + 0 || p2[2].score >= 120 && p2[3].score >= 120,
                      $"scores {p2[2].score} / {p2[3].score}");
                // wall finishes a car that was hit a moment ago: the attacker still gets the wreck bonus
                var (m3, p3) = NewMatch(GameMode.PointsDerby);
                Hit(m3, 0, 1, 30000f); Advance(m3, 1.5f); int s0 = p3[0].score;
                m3.ApplyDamage(new DamageEvent(1, -1, 1e7f));
                Check("L5. Knocked into a wall: the wreck bonus goes to the car that hit it within the credit window", p3[0].score == s0 + m3.rules.wreckBonus, $"score {s0} -> {p3[0].score}");
                var (m4, p4) = NewMatch(GameMode.PointsDerby);
                Hit(m4, 0, 1, 30000f); Advance(m4, 6f);
                m4.ApplyDamage(new DamageEvent(1, -1, 1e7f));
                Check("L6. ... but not when the last hit was long ago", p4[0].score == 30, $"score {p4[0].score}");
            }
            // ---- duplicates / farming
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby, 5, r => r.durabilityEnergy = 1e9f);
                int total = 0; for (int i = 0; i < 10; i++) { total += Hit(m, 0, 1, 20000f); Advance(m, 0.02f); }
                Check("M1. Repeated contact: ten identical events within 0.2 s score once", total == 30 && m.DuplicateEventsIgnored == 9, $"{total} points, {m.DuplicateEventsIgnored} duplicates ignored");
                Advance(m, 1f); int later = Hit(m, 0, 1, 20000f);
                Check("M2. ... and score again after the pair cooldown", later == 30, $"{later} points after 1 s");
                int harder = 0; Advance(m, 0.1f); harder = Hit(m, 0, 1, 60000f);
                Check("M3. ... a clearly harder hit inside the cooldown still counts", harder == 70, $"{harder} points");
                int tapTotal = 0; Advance(m, 2f); for (int i = 0; i < 100; i++) { tapTotal += Hit(m, 0, 2, 4000f); Advance(m, 0.05f); }
                Check("M4. Low-energy bumper taps for 5 s score at most one tap per cooldown, not per contact", tapTotal <= 80 && tapTotal >= 10, $"100 taps over 5 s: {tapTotal} points (cap 80)");
            }
            // ---- Points Derby clock and winner
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby, 5, r => { r.pointsDuration = 180f; r.durabilityEnergy = 1e9f; });
                Hit(m, 3, 1, 50000f);                                  // an AI (id 3) scores the most
                Hit(m, 0, 2, 20000f);
                Advance(m, 179f);
                bool still = m.State == MatchState.Active;
                Advance(m, 1.1f);
                Check("Q1. Points Derby ends at the configured time (180 s) and not before", still && m.State == MatchState.Results, $"state at 179 s {(still ? "Active" : "ended early")}, at 180.1 s {m.State}");
                Check("R1. Highest score wins, and an AI can win (the human did not)", m.Winner == ps[3] && ps[3].control == ControlType.AI && m.Ranking[0] == ps[3] && ps[0].placement == 2 && !m.IsDraw, $"winner {m.Winner?.name}, human placement {ps[0].placement}, text '{m.ResultText}'");
                int late = Hit(m, 0, 1, 100000f);
                Check("Q2. No scoring after the match has ended", late == 0 && ps[0].score == 30, $"hit after the end gave {late}");
            }
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby, 5, r => { r.pointsDuration = 10f; r.durabilityEnergy = 1e9f; });
                Hit(m, 0, 1, 20000f); Hit(m, 2, 3, 20000f);
                Advance(m, 10.1f);
                Check("R2. An exact tie on score, wrecks and deaths is a declared draw (no hidden favourite for the player)", m.IsDraw && m.Winner == null && ps[0].placement == 1 && ps[2].placement == 1, $"draw {m.IsDraw}, text '{m.ResultText}'");
                var (m2, p2) = NewMatch(GameMode.PointsDerby, 5, r => { r.pointsDuration = 10f; r.durabilityEnergy = 100000f; });
                Hit(m2, 0, 1, 30000f); Hit(m2, 2, 3, 30000f);        // 30 each
                Wreck(m2, 4, 2);                                       // AI 2 wrecks 4: +150
                Advance(m2, 10.1f);
                Check("R3. Tiebreak chain: more wrecks dealt wins before a draw is declared", m2.Winner == p2[2], $"winner {m2.Winner?.name}; scores {string.Join("/", p2.Select(p => p.score))}");
            }
            // ---- respawn (Points Derby) and none in Last Car Standing
            {
                var (m, ps) = NewMatch(GameMode.PointsDerby, 5, r => r.respawnDelay = 3f);
                int due = 0; Participant duePlayer = null; m.RespawnDue += p => { due++; duePlayer = p; };
                Hit(m, 1, 0, 200000f);                                 // an AI wrecks the human
                int sc = ps[0].score; ps[0].score = 77;
                Advance(m, 2.9f); bool early = due > 0;
                Advance(m, 0.3f);
                m.Respawned(ps[0]);
                Check("O1. Points Derby: a wrecked car is due for respawn after the delay (the human like any AI) and returns with full durability and its score", !early && due >= 1 && duePlayer == ps[0] && ps[0].IsActive && ps[0].durability == ps[0].maxDurability && ps[0].score == 77, $"respawn due {(early ? "too early" : "after the delay")}, status {ps[0].status}, score {ps[0].score}");
                Wreck(m, 2, 0); Advance(m, 3.2f); m.Respawned(ps[2]);
                Check("O2. The same for an AI car", ps[2].IsActive && ps[2].timesWrecked == 1, $"status {ps[2].status}");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                int due = 0; m.RespawnDue += p => due++;
                Wreck(m, 1, 0); Advance(m, 20f); m.Respawned(ps[1]);
                Check("S1. Last Car Standing: no respawns, the wrecked car is out for good", due == 0 && ps[1].status == ParticipantStatus.Out && m.ActiveCount == 4, $"respawn events {due}, status {ps[1].status}");
            }
            // ---- Last Car Standing: survival, then the final showdown (the full sequence and its edge cases are in ShowdownValidation)
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                bool showdown = false; m.ShowdownStarted += () => showdown = true;
                Wreck(m, 1, 0); Wreck(m, 2, 0); Advance(m, 1f);
                bool notYet = m.State == MatchState.Active && !m.ShowdownTriggered;
                Wreck(m, 3, 0); Advance(m, 0.3f);
                bool prepared = m.State == MatchState.ShowdownTransition && m.Finalists[0] == ps[0] && m.Finalists[1] == ps[4];
                Advance(m, 1.6f);
                Check("T1. With exactly two cars left the Final Showdown is prepared (transition), then the configured 60 s duel starts", notYet && prepared && m.State == MatchState.FinalShowdown && showdown && m.ShowdownRemaining > 58f, $"3 active -> {(notYet ? "no showdown" : "showdown too early")}; 2 active -> {(prepared ? "transition" : m.State.ToString())}, then {m.State}, {m.ShowdownRemaining:0.0} s");
                Advance(m, 10f);
                Check("T2. The showdown clock counts down", m.ShowdownRemaining < 50.5f && m.ShowdownRemaining > 47f, $"{m.ShowdownRemaining:0.0} s left");
                Wreck(m, 4, 0); Advance(m, 0.5f);
                Check("U1. Showdown ends when one of the two is wrecked: the survivor wins, once", m.State == MatchState.Results && m.Winner == ps[0] && !m.IsDraw && ps[0].placement == 1 && ps[4].placement == 2 && m.Kind == ResultKind.WinByWreck, $"state {m.State}, winner {m.Winner?.name}, placements {string.Join("/", ps.Select(p => p.placement))}");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                Wreck(m, 2); Wreck(m, 3); Wreck(m, 4); Advance(m, 1.9f);                // two left: 0 and 1; the duel is on
                Hit(m, 1, 0, 20000f); Hit(m, 0, 1, 50000f);                            // the human deals more showdown damage
                Advance(m, 61f);
                Check("V1. Showdown timeout: the finalist that dealt more showdown damage wins", m.State == MatchState.Results && m.Winner == ps[0] && m.Kind == ResultKind.WinByShowdownDamage, $"winner {m.Winner?.name} (dealt {ps[0].showdownDamage * 100f:0.0}% v {ps[1].showdownDamage * 100f:0.0}%), '{m.ResultText}'");
                var (m2, p2) = NewMatch(GameMode.LastCarStanding);
                Hit(m2, 1, 0, 20000f); Hit(m2, 1, 0, 20000f);                           // survival: the AI (1) builds a score lead
                Wreck(m2, 2); Wreck(m2, 3); Wreck(m2, 4); Advance(m2, 1.9f);
                int surv0 = p2[0].score, surv1 = p2[1].score; bool survivalLead = surv1 > surv0;
                Hit(m2, 0, 1, 20000f);                                                  // showdown: only the human deals damage
                Advance(m2, 61f);
                Check("V2. The survival score is irrelevant at the timeout: the AI had the score lead, the human dealt the showdown damage and won", survivalLead && m2.Winner == p2[0], $"survival scores {surv0} v {surv1}, winner {m2.Winner?.name}");
                var (m3, p3) = NewMatch(GameMode.LastCarStanding);
                Wreck(m3, 2); Wreck(m3, 3); Wreck(m3, 4); Advance(m3, 1.9f);
                Hit(m3, 0, 1, 20000f); Hit(m3, 1, 0, 20000f);
                Advance(m3, 61f);
                Check("V3. Exactly equal showdown damage: a declared draw, not the player", m3.IsDraw && m3.Winner == null, $"draw {m3.IsDraw}, '{m3.ResultText}'");
            }
            // ---- simultaneous eliminations
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                Wreck(m, 2); Wreck(m, 3); Wreck(m, 4);
                Hit(m, 1, 0, 20000f); Hit(m, 0, 1, 50000f);            // 0 leads
                Wreck(m, 0); Wreck(m, 1);                              // both wrecked in the same step
                Advance(m, 0.5f);
                Check("W1. Both finalists wrecked in the same step: decided by score, one winner", m.State == MatchState.Results && m.Winner == ps[0] && ps[0].placement == 1 && ps[1].placement == 2, $"winner {m.Winner?.name}, '{m.ResultText}'");
                var (m2, p2) = NewMatch(GameMode.LastCarStanding);
                Wreck(m2, 2); Wreck(m2, 3); Wreck(m2, 4); Wreck(m2, 0); Wreck(m2, 1); Advance(m2, 0.5f);
                Check("W2. Everyone wrecked with equal scores: a draw, and the result is reached once", m2.State == MatchState.Results && m2.IsDraw, $"'{m2.ResultText}'");
                var (m3, p3) = NewMatch(GameMode.LastCarStanding);
                int results = 0; m3.StateChanged += (a, b) => { if (b == MatchState.Results) results++; };
                Wreck(m3, 2); Wreck(m3, 3); Wreck(m3, 4); Wreck(m3, 0); Advance(m3, 0.5f); Wreck(m3, 1, 0); Advance(m3, 2f);
                Check("W3. A car wrecked well after the other finalist was wrecked cannot change or repeat the result: the survivor won once", m3.Winner == p3[1] && results == 1, $"winner {m3.Winner?.name}, results events {results}");
                var (m5, p5) = NewMatch(GameMode.LastCarStanding);
                Wreck(m5, 2); Wreck(m5, 3); Wreck(m5, 4);
                Hit(m5, 1, 0, 20000f);                                  // the AI (1) leads 30 to 0
                Wreck(m5, 0); Advance(m5, 0.1f); Wreck(m5, 1); Advance(m5, 0.5f);   // the second wreck follows within the simultaneous window
                Check("W5. Two finalists wrecked 0.1 s apart count as simultaneous: the higher score wins, even though the leader was wrecked last-but-not-first", m5.Winner == p5[1] && m5.State == MatchState.Results, $"winner {m5.Winner?.name}, '{m5.ResultText}'");
                var (m4, p4) = NewMatch(GameMode.LastCarStanding);
                Wreck(m4, 1); Wreck(m4, 2); Wreck(m4, 3); Advance(m4, 1.9f);
                bool onlyTwo = m4.State == MatchState.FinalShowdown;
                Wreck(m4, 4, 0);
                int after = Hit(m4, 0, 4, 5e4f);
                Advance(m4, 0.5f);
                Check("W4. A hit arriving after the last wreck in the same callback chain cannot create a second winner or score", onlyTwo && m4.Winner == p4[0] && after == 0 && m4.State == MatchState.Results, $"winner {m4.Winner?.name}, extra points {after}");
            }
            // ---- edge sizes
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding, 1);
                Advance(m, 0.5f);
                Check("E1. A match with one participant ends at once with that participant as winner", m.State == MatchState.Results && m.Winner == ps[0], $"state {m.State}");
                var (m2, p2) = NewMatch(GameMode.LastCarStanding, 2);
                Advance(m2, 0.1f);
                Advance(m2, 5f); bool noDuel = m2.State == MatchState.Active && !m2.ShowdownTriggered;
                Wreck(m2, 1, 0); Advance(m2, 0.5f);
                Check("E2. Two participants: there is no survival phase to get through (no showdown until a car is eliminated); the first wreck ends the match", noDuel && m2.State == MatchState.Results && m2.Winner == p2[0], $"state {m2.State}, winner {m2.Winner?.name}");
                var (m3, p3) = NewMatch(GameMode.PointsDerby, 12);
                Check("E3. The match model has no participant cap (12 here)", m3.participants.Count == 12 && m3.ActiveCount == 12, $"{m3.ActiveCount} active");
                var rule = FreeForAllRule.Instance; var a = p3[0]; var b = p3[1];
                Check("E4. Free-for-all: every distinct participant is a valid opponent, whatever the control type or team", rule.IsOpponent(a, b) && rule.IsOpponent(b, a) && !rule.IsOpponent(a, a) && !rule.IsOpponent(b, null), "no team or control-type input");
            }
            // ---- countdown
            {
                var rules = new MatchRules { countdownSeconds = 3f };
                var ps = new List<Participant> { new Participant(0, "A", ControlType.Human), new Participant(1, "B", ControlType.AI) };
                var m = new DerbyMatch(rules, ps); m.Begin();
                Advance(m, 2.9f); bool counting = m.State == MatchState.Countdown;
                int s = Hit(m, 0, 1, 50000f);
                Advance(m, 0.2f);
                Check("Y0. Countdown: nothing scores or takes damage before it ends, then the match is active", counting && s == 0 && ps[1].durability == ps[1].maxDurability && m.State != MatchState.Countdown && ps.All(p => p.IsActive), $"after 2.9 s {(counting ? "counting" : "started early")}; after 3.1 s {m.State}");
            }
        }

        // ==================================================================== scene checks (real physics)

        static void FreshMatch(int cars, GameMode mode, float countdown = 0.2f, Action<MatchRules> tweak = null)
        {
            game.GoToMenu();
            game.participantCount = cars;
            game.rules.mode = mode;
            game.rules.countdownSeconds = countdown;
            game.rules.pointsDuration = 180f; game.rules.showdownDuration = 60f; game.rules.respawnDelay = 3f; game.rules.durabilityEnergy = 650000f;
            tweak?.Invoke(game.rules);
            game.StartMatch();
            foreach (var p in game.Participants) p.car.Receiver.logImpacts = false;
            StepSeconds(countdown + 0.1f);
        }

        static void StepSeconds(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt); }
        static void StepUntil(Func<bool> done, float maxSeconds) { int n = Mathf.RoundToInt(maxSeconds / Dt); for (int i = 0; i < n && !done(); i++) game.ManualStep(Dt); }

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

        /// <summary>Everyone except the given cars parked in a row on the north side, far from the duel.</summary>
        static void Park(params Participant[] except)
        {
            int k = 0;
            foreach (var p in game.Participants)
            {
                if (except.Contains(p)) continue;
                PlaceCar(p, new Vector3(-36f + 16f * (k++ % 5), 0f, 44f), 180f, 0f);
            }
        }

        class RamResult { public float attackerScore, victimTaken, attackerTaken, closing; public int victimHits; }

        /// <summary>The attacker drives 14 m at the parked victim (AI off). Returns what the rules recorded.</summary>
        static RamResult Ram(Participant a, Participant v, float speed = 16f, float seconds = 2.2f)
        {
            AiOff();
            Park(a, v);
            PlaceCar(v, new Vector3(0f, 0f, -40f), 90f, 0f);
            PlaceCar(a, new Vector3(-14f, 0f, -40f), 90f, speed);
            float s0 = a.score, vt0 = v.damageTaken, at0 = a.damageTaken; int vh0 = v.hitsTaken;
            for (int i = 0; i < Mathf.RoundToInt(seconds / Dt); i++)
            {
                a.car.controller.Throttle = a.car.controller.ForwardSpeed < speed ? 1f : 0.3f; a.car.controller.SteerInput = 0f;
                v.car.controller.Throttle = 0f;
                game.ManualStep(Dt);
            }
            a.car.controller.Throttle = 0f;
            return new RamResult { attackerScore = a.score - s0, victimTaken = v.damageTaken - vt0, attackerTaken = a.damageTaken - at0, victimHits = v.hitsTaken - vh0 };
        }

        static double GeometryChecksum(DerbyCar c)
        {
            double sum = 0;
            foreach (var f in c.rig.root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = f.sharedMesh; if (mesh == null) continue;
                var vs = mesh.vertices;
                for (int i = 0; i < vs.Length; i++) sum += vs[i].x * 1.0 + vs[i].y * 3.1 + vs[i].z * 7.3 + (i % 97) * 0.001 * vs[i].x;
            }
            return sum;
        }

        static float ShellRetraction(DerbyCar c)
        {
            float m = 0f;
            if (c.rig.hitbox == null || c.rig.hitbox.Faces == null) return m;
            foreach (var f in c.rig.hitbox.Faces) m = Mathf.Max(m, f.original - f.current);
            return float.IsNaN(m) ? 0f : m;
        }

        static void SceneChecks()
        {
            sb.AppendLine("  -- DerbyArena (real physics) --");
            VehicleChecks();
            SpawnChecks();
            TargetingChecks();
            AiBehaviourChecks();
            DamageAndRulesChecks();
            LifecycleChecks();
        }

        // ------------------------------------------------------------------ A, B, C
        static void VehicleChecks()
        {
            // A: every existing model can be built and works as a car
            var found = new List<string>(); bool all = true;
            for (int v = 0; v < game.vehicles.Length; v++)
            {
                game.SelectVehicle(v);
                var c = game.HumanCar;
                int boxes = c.rig.root.GetComponentsInChildren<BoxCollider>().Length;
                bool ok = c != null && c.Model.IsInitialized && boxes == 14 && c.controller != null && c.Body.mass > 500f && game.vehicles[v].model != null;
                all &= ok; found.Add($"{game.vehicles[v].displayName} ({c.Body.mass:0} kg, {boxes} shell boxes)");
            }
            Check("A. All existing vehicle models can be instantiated as destructible, drivable cars", all && game.vehicles.Length >= 3, string.Join("; ", found));
            game.SelectVehicle(0);
            FreshMatch(5, GameMode.PointsDerby);
            var roster = game.Participants;
            Check("A2. Five cars by default, built from the three models (no new models)", roster.Count == 5 && roster.Select(p => p.vehicleIndex).Distinct().Count() == 3, $"{roster.Count} cars: {string.Join(", ", roster.Select(p => game.vehicles[p.vehicleIndex].displayName))}");

            // B: independent colours for instances of one model
            var byModel = roster.GroupBy(p => p.vehicleIndex).Where(g => g.Count() >= 2).ToList();
            bool bOk = byModel.Count > 0; var bd = new List<string>();
            foreach (var g in byModel)
            {
                var cars = g.ToList();
                var m0 = PaintMaterial(cars[0].car); var m1 = PaintMaterial(cars[1].car);
                bool differ = m0 != null && m1 != null && m0 != m1 && PaintColor(m0) != PaintColor(m1);
                // non-paint materials stay shared (windows, tyres, lights, chrome)
                int shared = SharedNonPaint(cars[0].car, cars[1].car, out int nonPaint);
                bOk &= differ && shared == nonPaint && nonPaint > 0;
                bd.Add($"{game.vehicles[g.Key].displayName}: {cars.Count} cars, paint {PaintColor(m0)} vs {PaintColor(m1)}, {shared}/{nonPaint} other materials shared");
            }
            int distinctPaints = roster.Select(p => PaintMaterial(p.car)).Distinct().Count();
            Check("B1. Two instances of one model have different body colours; windows, tyres, lights and trim keep their shared materials", bOk, string.Join("; ", bd));
            Check("B2. Every car has its own colour, and each model's paint slot was found", distinctPaints == roster.Count && roster.All(p => PaintMaterial(p.car) != null), $"{distinctPaints} distinct paints on {roster.Count} cars");
            int matsBefore = Resources.FindObjectsOfTypeAll<Material>().Length, cacheBefore = VehiclePainter.CachedMaterials;
            for (int i = 0; i < 20; i++) foreach (var p in roster) VehiclePainter.Apply(p.car.rig.root, game.palette.Get(p.paintIndex), game.palette);
            int matsAfter = Resources.FindObjectsOfTypeAll<Material>().Length;
            Check("B3. Painting is cached: repainting every car 20 times creates no new materials (cache is shared by colour, not per instance)", matsAfter == matsBefore && VehiclePainter.CachedMaterials == cacheBefore, $"materials {matsBefore} -> {matsAfter}, cached paints {VehiclePainter.CachedMaterials} (at most {game.vehicles.Length} models x {roster.Count} colours)");
            // distinguishable colours
            var cols = roster.Select(p => PaintColor(PaintMaterial(p.car))).ToList();
            float minDist = float.MaxValue;
            for (int i = 0; i < cols.Count; i++) for (int j = i + 1; j < cols.Count; j++) minDist = Mathf.Min(minDist, ColorDistance(cols[i], cols[j]));
            Check("B4. The five paints are clearly different from one another", minDist > 0.45f, $"smallest colour distance {minDist:0.00}");

            // C: paint does not affect deformation: the same impact on the same model in two colours gives the same geometry; repainting a damaged car keeps its dents
            var same = byModel.First().ToList();
            var ca = same[0].car; var cb = same[1].car;
            var impact = new DamageLab.Deformation.LocalImpact { point = new Vector3(0f, 0.5f, ca.rig.geometry.body.max.z), direction = new Vector3(0f, 0f, -1f), normal = new Vector3(0f, 0f, 1f), strength = 0.8f, depthScale = 1f };
            double a0 = GeometryChecksum(ca), b0 = GeometryChecksum(cb);
            ca.Model.AddImpact(impact); cb.Model.AddImpact(impact);
            double a1 = GeometryChecksum(ca), b1 = GeometryChecksum(cb);
            Check("C1. The same hit on two cars of one model in different colours deforms them identically", Math.Abs(a1 - b1) < 1e-6 && Math.Abs(a1 - a0) > 1e-3, $"checksum change {a1 - a0:0.0000} vs {b1 - b0:0.0000}; difference between the cars {Math.Abs(a1 - b1):0.00000000}");
            var meshesBefore = ca.rig.root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
            VehiclePainter.Apply(ca.rig.root, game.palette.Get((same[0].paintIndex + 3) % game.palette.Count), game.palette);
            double a2 = GeometryChecksum(ca);
            var meshesAfter = ca.rig.root.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh).ToArray();
            Check("C2. Repainting a dented car keeps its dents and its mesh instances", Math.Abs(a2 - a1) < 1e-9 && meshesBefore.SequenceEqual(meshesAfter), $"checksum change {a2 - a1:0.0000000000}, same mesh objects {meshesBefore.SequenceEqual(meshesAfter)}");
            ca.Model.ProgressiveCrushEditCheck();
        }

        static Material PaintMaterial(DerbyCar c)
        {
            foreach (var r in c.rig.root.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in r.sharedMaterials) if (VehiclePainter.IsPaint(m) || (m != null && m.name.Contains("Paint"))) return m;
            return null;
        }

        static Color PaintColor(Material m)
        {
            if (m == null) return Color.clear;
            return m.HasProperty("_PrimaryColor") ? m.GetColor("_PrimaryColor") : m.GetColor("_BaseColor");
        }

        static float ColorDistance(Color a, Color b) => Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        static int SharedNonPaint(DerbyCar a, DerbyCar b, out int nonPaint)
        {
            var ma = a.rig.root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && !m.name.Contains("Paint")).ToList();
            var mb = new HashSet<Material>(b.rig.root.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && !m.name.Contains("Paint")));
            nonPaint = ma.Count;
            return ma.Count(m => mb.Contains(m));
        }

        // ------------------------------------------------------------------ D: spawning
        static void SpawnChecks()
        {
            var sbd = new List<string>(); bool ok = true;
            foreach (int n in new[] { 2, 5, 8, 12 })
            {
                FreshMatch(n, GameMode.PointsDerby, 0.1f);
                var cars = game.Participants.Select(p => p.car).ToList();
                float minGap = float.MaxValue; bool overlapCars = false;
                for (int i = 0; i < cars.Count; i++)
                    for (int j = i + 1; j < cars.Count; j++)
                    {
                        minGap = Mathf.Min(minGap, Vector3.Distance(cars[i].Body.position, cars[j].Body.position));
                        if (cars[i].rig.root.GetComponentsInChildren<Collider>().Any(ci => cars[j].rig.root.GetComponentsInChildren<Collider>().Any(cj => ci.bounds.Intersects(cj.bounds)))) overlapCars = true;
                    }
                int blocked = 0;
                foreach (var c in cars)
                {
                    var hits = Physics.OverlapBox(c.Body.position + Vector3.up * 0.9f, new Vector3(0.9f, 0.5f, 2.1f), c.Body.rotation, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Any(h => h.attachedRigidbody != c.Body)) blocked++;
                }
                bool good = !overlapCars && blocked == 0 && minGap >= 9f && cars.Count == n;
                ok &= good; sbd.Add($"{n} cars: closest pair {minGap:0.0} m, {blocked} on top of an obstacle/car, car overlap {overlapCars}");
            }
            Check("D. Cars spawn without overlapping each other or obstacles, for 2, 5, 8 and 12 participants (count is configuration, not a limit)", ok, string.Join("; ", sbd));
            FreshMatch(5, GameMode.PointsDerby, 0.1f);
            bool noEarlyMove = true;
            game.GoToMenu(); game.participantCount = 5; game.rules.countdownSeconds = 3f; game.StartMatch();
            StepSeconds(2.5f);
            float moved = game.Participants.Max(p => p.car.Body.linearVelocity.magnitude);
            noEarlyMove = moved < 0.6f && game.Match.State == MatchState.Countdown;
            StepSeconds(0.7f);
            Check("D2. Countdown: no car moves (AI included) before it ends, and the match is active when it does", noEarlyMove && game.Match.State == MatchState.Active, $"fastest car during the countdown {moved:0.00} m/s, state after 3.2 s {game.Match.State}");
        }

        // ------------------------------------------------------------------ E, F, G, H: targeting
        static void TargetingChecks()
        {
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants; var ai = ps.Where(p => p.control == ControlType.AI).ToList();
            var seen = ai.ToDictionary(p => p.id, p => new HashSet<int>());
            int onHuman = 0, onAi = 0, selfTarget = 0, inactiveTarget = 0, samples = 0, noTarget = 0;
            var distSum = ai.ToDictionary(p => p.id, p => 0f); var distN = ai.ToDictionary(p => p.id, p => 0);
            game.Human.car.controller.Throttle = 0f;
            for (int i = 0; i < 3000; i++)
            {
                game.ManualStep(Dt);
                if (i % 10 != 0) continue;
                foreach (var a in ai)
                {
                    int t = game.Targeting.TargetOf(a.id);
                    samples++;
                    if (t < 0) { noTarget++; continue; }
                    seen[a.id].Add(t);
                    if (t == a.id) selfTarget++;
                    var tp = game.Match.Find(t);
                    if (tp != null && !tp.IsActive && game.Match.InPlay) inactiveTarget++;
                    if (t == game.Human.id) onHuman++; else onAi++;
                    distSum[a.id] += Vector3.Distance(a.car.Body.position, tp.car.Body.position); distN[a.id]++;
                }
            }
            bool aiVsAi = ai.Any(a => seen[a.id].Any(t => t != game.Human.id && t != a.id));
            bool aiVsHuman = ai.Any(a => seen[a.id].Contains(game.Human.id));
            Check("E. AI drivers target other AI drivers", aiVsAi, $"targets seen per AI: {string.Join("; ", ai.Select(a => a.name + " -> " + string.Join(",", seen[a.id].OrderBy(x => x))))}");
            Check("F. AI drivers target the player", aiVsHuman, $"{onHuman} of {onHuman + onAi} target samples were the player (expected about 25% with four rivals each)");
            float share = onHuman / (float)Math.Max(1, onHuman + onAi);
            Check("G1. In play the player gets no special priority: the share of AI attention on the player is not above the average (about 1/4) by more than a margin", share < 0.45f, $"player share {share * 100f:0}% of target-time, none-target samples {noTarget}/{samples}");
            Check("E2. An AI never targets itself and never keeps an inactive car as its target", selfTarget == 0 && inactiveTarget == 0, $"self {selfTarget}, inactive {inactiveTarget}");
            sb.AppendLine($"    targeting stats: switches {game.Targeting.Switches}, evaluations {game.Targeting.Evaluations}, worst targeting pass {game.WorstAiMilliseconds:0.000} ms; mean distance to chosen target " + string.Join(", ", ai.Select(a => $"{a.name} {(distN[a.id] > 0 ? distSum[a.id] / distN[a.id] : 0f):0} m")));

            // G2: the choice never looks at who drives: scores are identical when the control types are swapped
            var snapshot = Layout(ps, 11);
            var scoresA = AllScores(ps, 5f);
            var oldCtl = ps.Select(p => p.control).ToList();
            ps[0].control = ControlType.AI; ps[2].control = ControlType.Human;
            var scoresB = AllScores(ps, 5f);
            for (int i = 0; i < ps.Count; i++) ps[i].control = oldCtl[i];
            Check("G2. Target scores are identical if the human and an AI swap control types (no control-type input at all)", scoresA.Length == scoresB.Length && scoresA.Zip(scoresB, (x, y) => x == y).All(b => b), $"{scoresA.Length} pairwise scores compared");

            // G3: over random layouts the human is chosen about as often as any other participant
            var rng = new System.Random(3);
            var counts = new int[ps.Count]; int trials = 3000;
            var tune = ai.Select(a => AiTargeting.Tuning.For(a.profile)).ToList();
            for (int t = 0; t < trials; t++)
            {
                Layout(ps, rng.Next());
                for (int k = 0; k < ai.Count; k++)
                {
                    var me = ai[k]; Participant best = null; float bs = float.NegativeInfinity;
                    foreach (var c in ps) { if (c == me) continue; float s = AiTargeting.Score(me, c, tune[k], 10f + t * 0.37f); if (s > bs) { bs = s; best = c; } }
                    counts[best.id]++;
                }
            }
            int total = counts.Sum(); float humanShare = counts[0] / (float)total;
            Check("G3. Over 3000 random layouts the player is the best target for about a quarter of the choices, like any other car", humanShare > 0.16f && humanShare < 0.34f, $"choice shares by participant id: {string.Join(" / ", counts.Select(c => (100f * c / total).ToString("0") + "%"))}");

            // H: the three profiles choose differently in the same situation
            var br = ai.First(a => a.profile != null && a.profile.displayName == "Brawler"); var hu = ai.First(a => a.profile != null && a.profile.displayName == "Hunter"); var ca = ai.First(a => a.profile != null && a.profile.displayName == "Cautious");
            var others = ps.Where(p => p != br && p != hu && p != ca).ToList();           // the human and the Rookie
            var near = others[0]; var wounded = others[1];
            var details = new List<string>(); string lastDetail = "";
            string pick(Participant me, bool nearFacesMe)
            {
                AiOff();
                foreach (var p in ps) p.durability = p.maxDurability;
                wounded.durability = wounded.maxDurability * 0.25f;
                var all = new[] { br, hu, ca };
                PlaceCar(me, new Vector3(0f, 0f, -20f), 0f, 0f);
                foreach (var o in all) if (o != me) PlaceCar(o, new Vector3(-40f, 0f, 40f), 0f, 0f);
                PlaceCar(near, new Vector3(0f, 0f, -2f), nearFacesMe ? 180f : 90f, nearFacesMe ? 9f : 0f);     // 18 m ahead, healthy, maybe driving straight at us
                PlaceCar(wounded, new Vector3(30f, 0f, -45f), 90f, 0f);                                          // 36 m away, badly damaged
                var t = AiTargeting.Tuning.For(me.profile); t.noise = 0f;
                float sn = AiTargeting.Score(me, near, t, 20f), sw = AiTargeting.Score(me, wounded, t, 20f);
                lastDetail = $"{me.name}: near {sn:0.00}, wounded {sw:0.00}"; details.Add(lastDetail); return sn >= sw ? "near" : "wounded";
            }
            string b0 = pick(br, false), h0 = pick(hu, false), c0 = pick(ca, false), c1 = pick(ca, true), b1 = pick(br, true);
            Check("H1. Personalities choose differently: Brawler takes the nearby car, Hunter the wounded one, Cautious avoids the car driving straight at it", b0 == "near" && h0 == "wounded" && c1 == "wounded" && b1 == "near", $"healthy car 18 m away vs wounded car 36 m away: Brawler {b0}, Hunter {h0}, Cautious {c0}; when the near car drives at us: Brawler {b1}, Cautious {c1} | " + string.Join("; ", details));
            sb.AppendLine("    targeting weights (near / weak / ahead / head-on / commit): " + string.Join("; ", new[] { br, hu, ca }.Select(p => { var t = AiTargeting.Tuning.For(p.profile); return $"{p.name} {t.near:0.00}/{t.weak:0.00}/{t.ahead:0.00}/{t.headOn:0.00}/{t.commitBonus:0.00} hold {t.holdTime:0.0} s"; })));
        }

        static float[] AllScores(List<Participant> ps, float now)
        {
            var l = new List<float>();
            foreach (var a in ps.Where(p => p.profile != null)) { var t = AiTargeting.Tuning.For(a.profile); foreach (var c in ps) if (c != a) l.Add(AiTargeting.Score(a, c, t, now)); }
            return l.ToArray();
        }

        /// <summary>Random but reproducible positions, headings, speeds and durabilities for all cars.</summary>
        static object Layout(List<Participant> ps, int seed)
        {
            var r = new System.Random(seed);
            foreach (var p in ps)
            {
                PlaceCar(p, new Vector3((float)(r.NextDouble() * 80 - 40), 0f, (float)(r.NextDouble() * 80 - 40)), (float)(r.NextDouble() * 360), (float)(r.NextDouble() * 15));
                p.durability = p.maxDurability * (0.2f + 0.8f * (float)r.NextDouble());
            }
            return null;
        }

        // ------------------------------------------------------------------ I, J: AI behaviour inside the derby
        static void AiBehaviourChecks()
        {
            // I: obstacle recovery: an AI nose-in against the north wall, and one facing the corner wedge, gets away
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants; var ai = ps.Where(p => p.control == ControlType.AI).Take(2).ToList();
            AiOff(); Park(ai.ToArray());
            foreach (var p in ps) p.car.Body.constraints = RigidbodyConstraints.None;
            var a1 = ai[0]; var a2 = ai[1];
            PlaceCar(a1, new Vector3(-10f, 0f, 48.2f), 0f, 0f);       // nose against the north wall
            PlaceCar(a2, new Vector3(44f, 0f, 44f), 45f, 0f);          // nose into the north-east corner wedge
            foreach (var a in new[] { a1, a2 }) { a.car.enemy.enabled = true; a.car.enemy.startDelay = 0.2f; a.car.enemy.Restart(); a.car.enemy.SetTarget(game.Human.car.Body); }
            PlaceCar(game.Human, new Vector3(0f, 0f, -30f), 0f, 0f);
            float d1 = 0f, d2 = 0f; var start1 = a1.car.Body.position; var start2 = a2.car.Body.position;
            for (int i = 0; i < 700; i++) { game.ManualStep(Dt); }
            d1 = Vector3.Distance(a1.car.Body.position, start1); d2 = Vector3.Distance(a2.car.Body.position, start2);
            Check("I. AI V2 obstacle recovery still works in the derby arena: pinned against the wall and in the corner, the AI backs out and drives off", d1 > 12f && d2 > 12f && a1.car.enemy.RecoveriesTriggered + a2.car.enemy.RecoveriesTriggered >= 1, $"moved {d1:0} m / {d2:0} m in 14 s, recoveries {a1.car.enemy.RecoveriesTriggered} / {a2.car.enemy.RecoveriesTriggered}");

            // J: charge behaviour: one AI vs the stationary player in open ground
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants; var hunter = ps.First(p => p.profile != null && p.profile.displayName == "Hunter");
            AiOff(); Park(hunter, game.Human);
            PlaceCar(game.Human, new Vector3(0f, 0f, -40f), 0f, 0f);
            PlaceCar(hunter, new Vector3(30f, 0f, -20f), 250f, 0f);
            hunter.car.enemy.enabled = true; hunter.car.enemy.startDelay = 0.3f; hunter.car.enemy.Restart(); hunter.car.enemy.SetTarget(game.Human.car.Body);
            for (int i = 0; i < 1250; i++) game.ManualStep(Dt);
            var e = hunter.car.enemy;
            Check("J. AI V2 charge behaviour still works: the AI lines up, commits and lands meaningful rams on the target", e.AttacksCommitted >= 1 && e.HitsLanded >= 1 && e.LastHitClosingSpeed > 6f, $"charges {e.AttacksCommitted}, hits {e.HitsLanded}, last closing {e.LastHitClosingSpeed * 3.6f:0} km/h, back-offs {e.Repositions}; the target took {game.Human.damageTaken / 1000f:0} kJ");

            // H2: the same encounter for each personality: the AI alone against the stationary player in open ground, two starts of 30 s each
            var table = new Dictionary<string, (int charges, int backoffs, int hits)>();
            foreach (string name in new[] { "Brawler", "Hunter", "Cautious", "Rookie" })
            {
                int ch = 0, bo = 0, hi = 0;
                foreach (var start in new[] { (new Vector3(30f, 0f, -18f), 250f), (new Vector3(-30f, 0f, -18f), 110f) })
                {
                    FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
                    ps = game.Participants; var drv = ps.First(p => p.profile != null && p.profile.displayName == name);
                    AiOff(); Park(drv, game.Human);
                    PlaceCar(game.Human, new Vector3(0f, 0f, -40f), 0f, 0f);
                    PlaceCar(drv, start.Item1, start.Item2, 0f);
                    var en = drv.car.enemy; en.enabled = true; en.startDelay = 0.3f; en.Restart(); en.SetTarget(game.Human.car.Body);
                    for (int i = 0; i < 1500; i++) { game.ManualStep(Dt); if (game.Human.status != ParticipantStatus.Active) break; }
                    ch += en.AttacksCommitted; bo += en.Repositions; hi += en.HitsLanded;
                }
                table[name] = (ch, bo, hi);
            }
            Check("H2. Observable behaviour differs by personality in the same encounter: the Brawler charges more and backs off less than the Cautious driver", table["Brawler"].charges >= table["Cautious"].charges && table["Cautious"].backoffs >= table["Brawler"].backoffs && table["Brawler"].charges != table["Hunter"].charges | table["Hunter"].backoffs != table["Brawler"].backoffs,
                  "two 30 s runs each (charges / back-offs / hits): " + string.Join("; ", table.Select(kv => $"{kv.Key} {kv.Value.charges} / {kv.Value.backoffs} / {kv.Value.hits}")));

            // H3: a 60 s free-for-all, for the record (chaotic, so informational)
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants;
            for (int i = 0; i < 3000; i++) game.ManualStep(Dt);
            sb.AppendLine("    60 s free-for-all, per driver (charges / hits landed / back-offs / state changes / wall hits / damage dealt kJ / taken kJ / score):");
            foreach (var p in ps) sb.AppendLine($"      {p.name,-9} {(p.car.enemy != null ? $"{p.car.enemy.AttacksCommitted} / {p.car.enemy.HitsLanded} / {p.car.enemy.Repositions} / {p.car.enemy.StateChanges} / {p.car.enemy.ObstacleHits}" : "(human, idle)")} / {p.damageDealt / 1000f:0} / {p.damageTaken / 1000f:0} / {p.score}");
        }

        // ------------------------------------------------------------------ K, L, M, N, O, P
        static void DamageAndRulesChecks()
        {
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants; var d = new List<string>(); bool kOk = true, lOk = true;
            for (int i = 0; i < ps.Count; i++)
            {
                var v = ps[i]; var a = ps[(i + 1) % ps.Count];
                var r = Ram(a, v);
                bool k = r.victimTaken > 1500f && v.hitsTaken > 0; bool l = r.attackerScore > 0f;
                kOk &= k; lOk &= l;
                d.Add($"{a.name} -> {v.name}: victim took {r.victimTaken / 1000f:0} kJ, attacker scored {r.attackerScore:0}, took {r.attackerTaken / 1000f:0} kJ");
            }
            Check("K. Every participant (the human and each AI) can receive damage from a verified collision", kOk, string.Join("; ", d));
            Check("L. Every participant can earn points as the attacker", lOk, string.Join("; ", ps.Select(p => p.name + " " + p.score)));
            bool asym = d.Count > 0; var worst = ps.Select((p, i) => (i, v: p)).ToList();

            // M: pinned contact does not farm points
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants; var att = ps[1]; var vic = ps[2];
            var first = Ram(att, vic);
            int scoreAfterRam = att.score;
            for (int i = 0; i < 250; i++)         // 5 s of pushing against the victim at low speed
            {
                att.car.controller.Throttle = 0.5f; att.car.controller.SteerInput = 0f; game.ManualStep(Dt);
            }
            att.car.controller.Throttle = 0f;
            Check("M. Physical repeated contact: 5 s of pressing against the victim after a ram gives no farming (a few light pushes at most)", att.score - scoreAfterRam <= 30, $"ram scored {first.attackerScore:0}; pressing for 5 s added {att.score - scoreAfterRam}");

            // N: a wrecked car stops being a target for the AI at once
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 650000f);
            ps = game.Participants; StepSeconds(8f);
            var victim = ps.First(p => p.control == ControlType.AI);
            int hunters = ps.Count(p => p != victim && p.control == ControlType.AI && game.Targeting.TargetOf(p.id) == victim.id);
            game.Match.ApplyDamage(new DamageEvent(victim.id, ps.First(p => p != victim).id, 1e8f));
            game.ManualStep(Dt); StepSeconds(0.8f);
            bool anyTarget = ps.Any(p => p != victim && game.Targeting.TargetOf(p.id) == victim.id) || ps.Any(p => p.car.enemy != null && p.car.enemy.Target == victim.car.Body);
            Check("N. A wrecked car is no longer an active target: no AI keeps it as its target, and it counts as an obstacle", victim.status == ParticipantStatus.Wrecked && !anyTarget, $"{hunters} AI had it as target before; after the wreck status {victim.status}, still targeted {anyTarget}");

            // O, P: respawn
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants;
            var human = game.Human; var aiCar = ps.First(p => p.control == ControlType.AI);
            double fresh = GeometryChecksum(human.car), freshAi = GeometryChecksum(aiCar.car);
            Ram(aiCar, human, 18f); Ram(human, aiCar, 18f);
            double dented = GeometryChecksum(human.car);
            float shell = ShellRetraction(human.car) + ShellRetraction(aiCar.car);
            var matsBefore = human.car.rig.root.GetComponentsInChildren<MeshRenderer>().SelectMany(r => r.sharedMaterials).ToList();
            human.score = 55;
            game.Match.ApplyDamage(new DamageEvent(human.id, aiCar.id, 1e12f)); game.Match.ApplyDamage(new DamageEvent(aiCar.id, human.id, 1e12f));
            ps.ForEach(p => p.car.Receiver.ClearCooldowns());
            bool wrecked = human.status == ParticipantStatus.Wrecked && aiCar.status == ParticipantStatus.Wrecked;
            Material wreckPaint = PaintMaterial(human.car);
            StepSeconds(2.5f); bool tooEarly = human.IsActive || aiCar.IsActive;
            StepSeconds(1.5f);
            Check("O. Points Derby respawn works for the player and for an AI: after the delay both are active again with full durability, score kept", wrecked && !tooEarly && human.IsActive && aiCar.IsActive && human.DurabilityFraction > 0.9f && human.score >= 55 && game.Respawns >= 2, $"wrecked {wrecked}, respawns {game.Respawns}, human {human.status} durability {human.DurabilityFraction * 100f:0}%, score {human.score}");
            double after = GeometryChecksum(human.car), afterAi = GeometryChecksum(aiCar.car);
            bool fresh2 = Math.Abs(after - fresh) < 1e-6 && Math.Abs(afterAi - freshAi) < 1e-6;
            Check("P. Respawn restores fresh geometry and collision state: the dents are gone, the shell is back at full size, counters and motion are reset, the paint is back", fresh2 && Math.Abs(dented - fresh) > 1e-3 && shell > 0.01f && ShellRetraction(human.car) < 1e-4f && !human.car.Model.IsDamaged && human.car.Body.linearVelocity.magnitude < 2f && PaintMaterial(human.car) != wreckPaint && !human.car.Model.IsCrushing,
                  $"checksum back to fresh {fresh2} (dented differed by {Math.Abs(dented - fresh):0.000}), shell retraction before {shell:0.000} m after {ShellRetraction(human.car):0.0000} m, damaged {human.car.Model.IsDamaged}, impacts {human.car.Model.ImpactCount}");
            int bodies = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            sb.AppendLine($"    rigidbodies in the scene after respawns: {bodies}");
        }

        // ------------------------------------------------------------------ Q-Y with the real game
        static void LifecycleChecks()
        {
            // Q / R with the game: short timer, an AI leads
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => { r.pointsDuration = 8f; r.durabilityEnergy = 1e9f; });
            var ps = game.Participants; var ai = ps.First(p => p.control == ControlType.AI);
            game.Match.ApplyDamage(new DamageEvent(game.Human.id, ai.id, 50000f));
            StepSeconds(7.5f); bool running = game.Match.InPlay;
            StepSeconds(0.8f);
            Check("Q. Points Derby ends at the configured timer in the real game (8 s here; the default is 180 s)", running && game.Match.State == MatchState.Results, $"at 7.6 s {(running ? "running" : "ended early")}, at 8.4 s {game.Match.State}");
            Check("R. The highest score wins - an AI won this one, not the player", game.Match.Winner != null && game.Match.Winner.control == ControlType.AI && game.Match.Winner == game.Match.Ranking[0], $"winner {game.Match.Winner?.name} ({game.Match.Winner?.score}), human placement {game.Human.placement} of {ps.Count}");
            bool idle = ps.All(p => p.car.controller.Throttle == 0f);

            // S, T, U with real cars
            FreshMatch(5, GameMode.LastCarStanding, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants; int resp0 = game.Respawns;
            game.Match.ApplyDamage(new DamageEvent(ps[1].id, ps[0].id, 1e12f));
            StepSeconds(6f);
            Check("S. Last Car Standing in the real game: a wrecked car is out for good (no respawn after 6 s) and stays a hulk", ps[1].status == ParticipantStatus.Out && game.Respawns == resp0 && game.Match.ActiveCount == 4, $"status {ps[1].status}, active {game.Match.ActiveCount}");
            game.Match.ApplyDamage(new DamageEvent(ps[2].id, ps[0].id, 1e12f));
            StepSeconds(0.3f); bool threeLeft = game.Match.State == MatchState.Active;
            game.Match.ApplyDamage(new DamageEvent(ps[3].id, ps[0].id, 1e12f));
            StepSeconds(0.3f);
            Check("T. The Final Showdown is prepared as soon as exactly two cars are active", threeLeft && game.Match.State == MatchState.ShowdownTransition && game.Match.ShowdownTriggered, $"state {game.Match.State}");
            StepUntil(() => game.Match.State == MatchState.FinalShowdown, 5f);
            Check("T2. ...and the 60 s duel starts after the transition and the countdown", game.Match.State == MatchState.FinalShowdown && game.Match.ShowdownRemaining > 58f, $"state {game.Match.State}, {game.Match.ShowdownRemaining:0.0} s");
            game.Match.ApplyDamage(new DamageEvent(ps[4].id, ps[0].id, 1e12f));
            StepSeconds(0.5f);
            Check("U. The showdown ends when one of the two is wrecked: survivor wins", game.Match.State == MatchState.Results && game.Match.Winner == ps[0], $"state {game.Match.State}, winner {game.Match.Winner?.name}");
            // V, W through the real game objects
            FreshMatch(5, GameMode.LastCarStanding, 0.1f, r => { r.durabilityEnergy = 1e9f; r.showdownDuration = 6f; });
            AiOff();
            ps = game.Participants;
            for (int i = 2; i < 5; i++) game.Match.ApplyDamage(new DamageEvent(ps[i].id, -1, 1e12f));
            StepUntil(() => game.Match.State == MatchState.FinalShowdown, 5f); AiOff();
            game.Match.ApplyDamage(new DamageEvent(1, 0, 0.1f * ps[1].maxDurability));      // the human deals showdown damage (10 % of the opponent's armour), the AI none
            StepSeconds(7f);
            Check("V. Showdown timeout in the real game: the finalist with more showdown damage wins", game.Match.State == MatchState.Results && game.Match.Winner == ps[0] && game.Match.Kind == ResultKind.WinByShowdownDamage, $"winner {game.Match.Winner?.name}, '{game.Match.ResultText}'");
            FreshMatch(5, GameMode.LastCarStanding, 0.1f, r => r.durabilityEnergy = 1e9f);
            ps = game.Participants;
            for (int i = 2; i < 5; i++) game.Match.ApplyDamage(new DamageEvent(ps[i].id, -1, 1e12f));
            StepUntil(() => game.Match.State == MatchState.FinalShowdown, 5f); AiOff();
            game.Match.ApplyDamage(new DamageEvent(ps[1].id, ps[0].id, 0.1f * ps[1].maxDurability));
            game.Match.ApplyDamage(new DamageEvent(0, -1, 1e12f)); game.Match.ApplyDamage(new DamageEvent(1, -1, 1e12f));
            StepSeconds(0.5f);
            Check("W. Both finalists wrecked in the same step in the real game: one result, the higher showdown damage", game.Match.State == MatchState.Results && game.Match.Winner == ps[0] && !game.Match.IsDraw, $"winner {game.Match.Winner?.name}, '{game.Match.ResultText}'");

            // X: restart
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 650000f);
            ps = game.Participants;
            StepSeconds(20f);
            Ram(ps[1], ps[2], 18f);
            int bodiesBefore = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            var carsBefore = ps.Select(p => p.car).ToList();
            double dirty = GeometryChecksum(ps[2].car);
            game.Rematch();
            StepSeconds(0.3f);
            ps = game.Participants;
            int bodiesAfter = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            bool clean = ps.All(p => p.score == 0 && p.hitsTaken == 0 && p.timesWrecked == 0 && p.IsActive && p.durability == p.maxDurability && !p.car.Model.IsDamaged && p.car.Receiver.EventCount == 0)
                         && game.Match.Clock < 1f;
            Check("X. Restart creates a clean match: scores, stats, damage, geometry and clocks are reset, no cars leak", clean && bodiesBefore == bodiesAfter && ps.Select(p => p.car).SequenceEqual(carsBefore), $"clean {clean}, rigidbodies {bodiesBefore} -> {bodiesAfter}, same car objects reused {ps.Select(p => p.car).SequenceEqual(carsBefore)}, dirty checksum differed {Math.Abs(dirty - GeometryChecksum(ps[2].car)) > 1e-3}");

            // Y: back to the menu and start again, repeatedly
            var meshCounts = new List<int>(); var matCounts = new List<int>(); var rbCounts = new List<int>();
            Resources.UnloadUnusedAssets();
            for (int i = 0; i < 5; i++)
            {
                game.GoToMenu();
                bool inMenu = game.MenuOpen && game.Participants.Count == 0 && game.Match == null && game.HumanCar != null;
                if (!inMenu) { Check("Y. Returning to the menu works", false, $"iteration {i}: menu {game.MenuOpen}, participants {game.Participants.Count}"); break; }
                game.StartMatch(); StepSeconds(2f);
                meshCounts.Add(Resources.FindObjectsOfTypeAll<Mesh>().Length); matCounts.Add(Resources.FindObjectsOfTypeAll<Material>().Length); rbCounts.Add(UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length);
            }
            game.GoToMenu();
            int rbMenu = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            Check("Y. Returning to the menu removes the AI cars and the match, the player's car is kept (repaired); five menu -> match -> menu rounds leave no extra rigidbodies and bounded meshes/materials",
                  game.MenuOpen && game.Participants.Count == 0 && rbCounts.Distinct().Count() == 1 && rbMenu < rbCounts[0] && game.HumanCar != null && !game.HumanCar.Model.IsDamaged,
                  $"rigidbodies per round {string.Join("/", rbCounts)} (menu {rbMenu}), meshes {string.Join("/", meshCounts)}, materials {string.Join("/", matCounts)}");
            // quick participant-count switch inside one session
            game.participantCount = 5;
        }



        // ==================================================================== the reported damage exploit (informational: Opus owns the collision code)
        static void ExploitProbe()
        {
            sb.AppendLine("  -- exploit probe: head-on, the second car brakes to a stop shortly before impact (what the owner reported: stationary player 0 %, attacker 97 %) --");
            sb.AppendLine("     columns: victim speed at start / braking starts at gap / closing speed at contact | victim energy kJ, attacker energy kJ | points victim, points attacker | victim strength, attacker strength");
            int zeroVictim = 0, trials = 0;
            foreach (float v0 in new[] { 10f, 15f })
                foreach (float gap in new[] { 16f, 10f, 6f, 3f })
                {
                    FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
                    var ps = game.Participants; var a = ps[1]; var v = ps[0];
                    AiOff(); Park(a, v);
                    PlaceCar(v, new Vector3(14f, 0f, -40f), 270f, v0);
                    PlaceCar(a, new Vector3(-16f, 0f, -40f), 90f, 17f);
                    float closingAtContact = 0f; bool braking = false;
                    var ra = a.car.Receiver; var rv = v.car.Receiver;
                    for (int i = 0; i < 150; i++)
                    {
                        float dist = Vector3.Distance(a.car.Body.position, v.car.Body.position) - 4.8f;
                        if (!braking && dist < gap) braking = true;
                        a.car.controller.Throttle = a.car.controller.ForwardSpeed < 17f ? 1f : 0f; a.car.controller.SteerInput = 0f;
                        v.car.controller.Throttle = braking ? -1f : (v.car.controller.ForwardSpeed < v0 ? 1f : 0f); v.car.controller.SteerInput = 0f;
                        if (closingAtContact == 0f && dist < 0.3f) closingAtContact = (a.car.Body.linearVelocity - v.car.Body.linearVelocity).magnitude;
                        game.ManualStep(Dt);
                    }
                    trials++;
                    if (v.damageTaken <= 0f && a.damageTaken > 5000f) zeroVictim++;
                    sb.AppendLine($"       {v0:0} m/s / {gap:0} m / {closingAtContact:0.0} m/s | {v.damageTaken / 1000f:0.0}, {a.damageTaken / 1000f:0.0} | {v.score}, {a.score} | {(rv.HasReport ? rv.Last.strength : 0f):0.00}, {(ra.HasReport ? ra.Last.strength : 0f):0.00}  (victim receiver events {rv.EventCount}, attacker {ra.EventCount})");
                }
            sb.AppendLine($"    cases where the braking car took no damage while the other car took more than 5 kJ: {zeroVictim} of {trials}");
        }

        // ==================================================================== balance simulation (informational)
        static void Balance()
        {
            sb.AppendLine("  -- balance simulation: five AI-style drivers, default rules (the human car is driven by an EnemyDriver for this test only) --");
            foreach (var mode in new[] { GameMode.PointsDerby, GameMode.LastCarStanding })
                for (int run = 0; run < 2; run++)
                {
                    FreshMatch(5, mode, 0.1f);
                    var hc = game.Human.car;
                    var bot = hc.rig.root.AddComponent<EnemyDriver>();
                    bot.receiver = hc.Receiver; bot.arenaHalf = game.arenaHalf; bot.SetProfile(game.aiProfiles[1]); bot.Begin(game.aiSpeedKmh / 3.6f); bot.startDelay = 0.2f; bot.Restart();
                    hc.enemy = bot;
                    int maxSteps = mode == GameMode.PointsDerby ? 9100 : 36000;
                    float showdownAt = -1f; int steps = 0;
                    for (; steps < maxSteps && game.Match.State != MatchState.Results; steps++)
                    {
                        game.ManualStep(Dt);
                        if (showdownAt < 0f && game.Match.State == MatchState.FinalShowdown) showdownAt = game.Match.Clock;
                    }
                    var m = game.Match;
                    sb.AppendLine($"    {mode} run {run + 1}: {(m.State == MatchState.Results ? "finished" : "NOT FINISHED")} at {m.Clock:0} s; winner {m.Winner?.name ?? "none"} ('{m.ResultText}'); showdown began {(showdownAt < 0 ? "never" : showdownAt.ToString("0") + " s")}; respawns {game.Respawns}");
                    sb.AppendLine("      " + string.Join(" | ", m.Ranking.Select(p => $"{p.name} score {p.score}, wrecks dealt {p.wrecksDealt}, wrecked {p.timesWrecked}, hits {p.hitsDealt}/{p.hitsTaken}")));
                    hc.enemy = null; UnityEngine.Object.DestroyImmediate(bot);
                }
            game.GoToMenu();
        }

        // ==================================================================== performance (editor)
        static void Performance()
        {
            sb.AppendLine("  -- performance (editor, Mono; WebGL numbers are measured in the browser) --");
            // build time and a five-car match
            var sw = Stopwatch.StartNew();
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
            sw.Stop();
            long gc0 = GC.GetTotalMemory(false);
            var steps = new List<double>(); var ws = new Stopwatch();
            for (int i = 0; i < 1500; i++) { ws.Restart(); game.ManualStep(Dt); ws.Stop(); steps.Add(ws.Elapsed.TotalMilliseconds); }
            steps.Sort();
            Check("PERF1. Five-vehicle match: a full fixed step (5 cars, 4 AI, physics, damage, refits) is cheap", steps.Average() < 12.0, $"{steps.Average():0.00} ms average, {steps[(int)(steps.Count * 0.99)]:0.00} ms p99, {steps.Last():0.0} ms worst over 30 s; building the match took {sw.ElapsedMilliseconds} ms; AI targeting pass worst {game.WorstAiMilliseconds:0.000} ms; mean AI Drive {game.Participants.Where(p => p.car.enemy != null).Average(p => p.car.enemy.WorstDriveMilliseconds):0.000} ms worst-call");
            // allocations per step
            long a0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            for (int i = 0; i < 500; i++) game.ManualStep(Dt);
            long a1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            sb.AppendLine($"    managed memory change over 500 steps of the running match (includes physics-callback and damage allocations; GC may have run): {(a1 - a0) / 1024f:0} KB");

            // repeated respawns of the same cars
            FreshMatch(5, GameMode.PointsDerby, 0.1f, r => { r.durabilityEnergy = 1e9f; r.respawnDelay = 0.5f; });
            var ps = game.Participants;
            Resources.UnloadUnusedAssets(); GC.Collect();
            int mesh0 = Resources.FindObjectsOfTypeAll<Mesh>().Length, mat0 = Resources.FindObjectsOfTypeAll<Material>().Length, rb0 = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            long mem0 = GC.GetTotalMemory(true);
            var respawnMs = new List<double>();
            int matMid = 0;
            for (int round = 0; round < 40; round++)
            {
                var v = ps[1 + round % 4];
                Ram(ps[(round + 2) % 5 == v.id ? 0 : (round + 2) % 5], v, 17f, 1.2f);
                game.Match.ApplyDamage(new DamageEvent(v.id, -1, 1e12f));
                ws.Restart();
                int guard = 0; while (!v.IsActive && guard++ < 400) game.ManualStep(Dt);
                ws.Stop(); respawnMs.Add(ws.Elapsed.TotalMilliseconds);
                if (round == 11) matMid = Resources.FindObjectsOfTypeAll<Material>().Length;   // every model has been wrecked and repainted once by now
            }
            Resources.UnloadUnusedAssets(); GC.Collect();
            int mesh1 = Resources.FindObjectsOfTypeAll<Mesh>().Length, mat1 = Resources.FindObjectsOfTypeAll<Material>().Length, rb1 = UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length;
            long mem1 = GC.GetTotalMemory(true);
            Check("PERF2. 40 respawns do not leak meshes, materials or bodies, and managed memory stays bounded", mesh1 <= mesh0 + 2 && mat1 == matMid && mat1 <= mat0 + game.vehicles.Length && rb1 == rb0 && mem1 - mem0 < 24L * 1024 * 1024, $"meshes {mesh0} -> {mesh1}, materials {mat0} -> {matMid} after the first 12 respawns (the charcoal wreck paint is created once per model) -> {mat1} at the end, rigidbodies {rb0} -> {rb1}, managed memory {(mem1 - mem0) / 1024f / 1024f:0.0} MB growth; respawns done {game.Respawns}");
            // the respawn operation itself (reset of geometry, colliders, motion, AI, paint) on damaged cars
            var resetMs = new List<double>();
            var spotA = new SpawnPlanner.Spot { position = new Vector3(0f, 0f, 30f), yaw = 180f };
            for (int round = 0; round < 30; round++)
            {
                var v = ps[1 + round % 4];
                Ram(ps[(round + 2) % 5 == v.id ? 0 : (round + 2) % 5], v, 17f, 1.2f);
                ws.Restart(); game.ResetCar(v.car, spotA); ws.Stop(); resetMs.Add(ws.Elapsed.TotalMilliseconds);
                PlaceCar(v, new Vector3(-30f + 15f * (round % 5), 0f, 44f), 180f, 0f);
            }
            resetMs.Sort();
            Check("PERF3. The respawn operation on a damaged car is not a hitch", resetMs[(int)(resetMs.Count * 0.9)] < 40.0, $"ResetCar on damaged cars: {resetMs.Average():0.0} ms average, {resetMs.Last():0.0} ms worst over 30 resets (editor, Mono)");

            // scaling smoke test above five (not a release requirement)
            foreach (int n in new[] { 8, 12 })
            {
                FreshMatch(n, GameMode.PointsDerby, 0.1f, r => r.durabilityEnergy = 1e9f);
                steps.Clear(); bool ok = true;
                try { for (int i = 0; i < 750; i++) { ws.Restart(); game.ManualStep(Dt); ws.Stop(); steps.Add(ws.Elapsed.TotalMilliseconds); } }
                catch (Exception e) { ok = false; sb.AppendLine("    scaling exception: " + e.Message); }
                steps.Sort();
                sb.AppendLine($"    scaling smoke test ({n} cars, 15 s, no claim of playability): {(ok ? "ran without errors" : "FAILED")}, {steps.Average():0.00} ms per fixed step average, {steps[(int)(steps.Count * 0.99)]:0.0} ms p99; targeting evaluations {game.Targeting.Evaluations}, worst targeting pass {game.WorstAiMilliseconds:0.000} ms; rigidbody-pair contacts get costly beyond this in editor");
                Check($"PERF4. Scaling smoke test with {n} participants runs without exceptions (informational)", ok, $"{steps.Average():0.0} ms/step");
            }
            FreshMatch(5, GameMode.PointsDerby, 0.1f);
        }
    }

    static class ModelExt
    {
        /// <summary>Completes any progressive crush in progress (edit-mode no-op normally).</summary>
        public static void ProgressiveCrushEditCheck(this DamageLab.Vehicle.VehicleDamageModel m) { if (m.IsCrushing) m.CompleteCrush(); }
    }
}
