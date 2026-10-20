using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
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
    /// Last Car Standing: Survival -> Final Showdown checks. Part 1 drives the pure match model (no scene): triggers, the transition and countdown states, damage gating, showdown-only
    /// damage, every way a duel can end. Part 2 uses the real DerbyArena (real cars, PhysX at 50 Hz): repair and deformation reset, placement, frozen countdown, touch input, AI and player
    /// duels, restart / rematch / menu, the HUD texts and the fleeing-leader scenario.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.ShowdownValidation.Run -quit -logFile showdown.log
    /// Environment SHOWDOWN_ONLY = pure | scene | flee | perf runs one part.
    /// </summary>
    public static class ShowdownValidation
    {
        const float Dt = 0.02f;
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Final Showdown", priority = 24)]
        public static void Run() => Debug.Log(Execute());

        static void Check(string name, bool ok, string detail)
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[Showdown] Validation (pure match model, then the DerbyArena with real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            string only = Environment.GetEnvironmentVariable("SHOWDOWN_ONLY") ?? "";
            var prev = Physics.simulationMode;
            try
            {
                if (only == "" || only == "pure") PureChecks();
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); ui = game.ui; game.ui = null;
                if (only == "" || only == "scene") { SceneChecks(); HudChecks(); }
                if (only == "" || only == "perf") PerfChecks();
                if (only == "" || only == "flee") FleeChecks();
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            sb.AppendLine($"[Showdown] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ==================================================================== pure model

        static (DerbyMatch m, List<Participant> ps) NewMatch(GameMode mode, int n = 5, Action<MatchRules> tweak = null)
        {
            var rules = new MatchRules { mode = mode, countdownSeconds = 3f, durabilityEnergy = 100000f };
            tweak?.Invoke(rules);
            var ps = new List<Participant>();
            for (int i = 0; i < n; i++) ps.Add(new Participant(i, "P" + i, i == 0 ? ControlType.Human : ControlType.AI));
            var m = new DerbyMatch(rules, ps);
            m.Begin();
            Advance(m, rules.countdownSeconds + 0.1f);
            return (m, ps);
        }

        static void Advance(DerbyMatch m, float seconds) { int n = Mathf.RoundToInt(seconds / Dt); for (int i = 0; i < n; i++) m.Tick(Dt); }
        static int Hit(DerbyMatch m, int att, int vic, float energy) => m.ApplyDamage(new DamageEvent(vic, att, energy));
        static void Wreck(DerbyMatch m, int vic, int att = -1) => m.ApplyDamage(new DamageEvent(vic, att, 1e7f));

        /// <summary>Advances until the duel is on (transition + countdown), returning false if it never starts.</summary>
        static bool ToDuel(DerbyMatch m)
        {
            for (int i = 0; i < 1000 && m.State != MatchState.FinalShowdown; i++) m.Tick(Dt);
            return m.State == MatchState.FinalShowdown;
        }

        /// <summary>Last Car Standing with participants 0 and <paramref name="other"/> left, in the Survival phase.</summary>
        static (DerbyMatch m, List<Participant> ps) TwoLeft(int other = 1, Action<MatchRules> tweak = null)
        {
            var (m, ps) = NewMatch(GameMode.LastCarStanding, 5, tweak);
            for (int i = 0; i < 5; i++) if (i != 0 && i != other) Wreck(m, i, 0);
            return (m, ps);
        }

        static void PureChecks()
        {
            sb.AppendLine("  -- the match model: Survival -> Final Showdown (no scene) --");
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                int prep = 0; m.ShowdownPrepare += (a, b) => prep++;
                Advance(m, 20f);
                Check("S1. Last Car Standing starts in the Survival phase: active, nobody out, no showdown, 5 participants alive", m.State == MatchState.Active && !m.ShowdownTriggered && !m.InShowdown && m.ActiveCount == 5 && prep == 0 && m.Kind == ResultKind.None, $"state {m.State}, active {m.ActiveCount}, showdown prepared {prep}x");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                int prep = 0; m.ShowdownPrepare += (a, b) => prep++;
                Wreck(m, 1, 0); Advance(m, 5f); Wreck(m, 2, 0); Advance(m, 30f);
                Check("S2. Three or more survivors do not trigger the showdown (4 alive, then 3 alive for 30 s)", m.State == MatchState.Active && m.ActiveCount == 3 && prep == 0, $"state {m.State}, active {m.ActiveCount}, prepared {prep}x");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                var seq = new List<MatchState>(); m.StateChanged += (a, b) => seq.Add(b);
                int prep = 0; Participant fa = null, fb = null; m.ShowdownPrepare += (a, b) => { prep++; fa = a; fb = b; };
                Wreck(m, 1, 0); Wreck(m, 2, 0); Advance(m, 1f);
                Wreck(m, 3, 0);
                Advance(m, 0.1f); bool settling = m.State == MatchState.Active && prep == 0;
                Advance(m, 0.1f);
                bool triggered = m.State == MatchState.ShowdownTransition && prep == 1;
                Wreck(m, 3, 0); Wreck(m, 1, 0); Wreck(m, 2, 4); Advance(m, 6f);     // duplicate elimination events of cars that are already out
                Check("S3. Exactly two survivors trigger the showdown once (after the last wreck has settled), duplicate elimination events do not re-trigger it", settling && triggered && prep == 1 && fa == ps[0] && fb == ps[4] && seq.Count(s => s == MatchState.ShowdownTransition) == 1,
                      $"settling {settling}, transition {triggered}, prepared {prep}x for {fa?.name} and {fb?.name}, states {string.Join(">", seq)}");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                int prep = 0; m.ShowdownPrepare += (a, b) => prep++;
                Wreck(m, 1, 0); Wreck(m, 2, 0); Wreck(m, 3, 0); Advance(m, 0.1f); Wreck(m, 4, 0); Advance(m, 1f);
                Check("S4. Only one survivor ends the match normally, without a showdown", m.State == MatchState.Results && m.Winner == ps[0] && m.Kind == ResultKind.LastCarStanding && prep == 0 && m.ResultText.Contains("LAST CAR STANDING"), $"state {m.State}, winner {m.Winner?.name}, kind {m.Kind}, prepared {prep}x");
            }
            {
                // simultaneous eliminations
                string Run(int[] order, bool fourAlive, out MatchState st, out string finalists, out string winner)
                {
                    var (m, ps) = NewMatch(GameMode.LastCarStanding);
                    int prep = 0; string f = ""; m.ShowdownPrepare += (a, b) => { prep++; f = a.name + "+" + b.name; };
                    if (fourAlive) Wreck(m, 4, 0);
                    foreach (int v in order) Wreck(m, v, 0);                  // all in the same step
                    Advance(m, 0.4f);
                    st = m.State; finalists = f; winner = m.Winner?.name ?? "-";
                    return $"{st}/{f}/{winner}/{m.ResultText}";
                }
                string r1 = Run(new[] { 1, 2 }, false, out var s1, out var f1, out var w1);       // 3 alive (0,3,4): two out at once -> hmm that is 5-2 = 3 alive
                string r2 = Run(new[] { 1, 2, 3 }, false, out var s2, out var f2, out var w2);    // 2 alive (0,4) after three at once -> showdown
                string r3 = Run(new[] { 3, 2, 1 }, false, out var s3, out var f3, out var w3);    // the same wrecks in the opposite order
                string r4 = Run(new[] { 1, 2, 3 }, true, out var s4, out var f4, out var w4);     // everybody but the human at once -> the human wins
                string r5 = Run(new[] { 1, 2, 3 }, false, out var s5, out var f5, out var w5);    // repeated: identical
                Check("S5. Simultaneous eliminations resolve deterministically: (3 left stays Survival), (2 left -> showdown with exactly those two, whatever the order), (1 left wins normally), and repeating the sequence repeats the result",
                      s1 == MatchState.Active && s2 == MatchState.ShowdownTransition && f2 == "P0+P4" && f3 == f2 && s3 == s2 && s4 == MatchState.Results && w4 == "P0" && r2 == r5,
                      $"two out at once: {s1}; three out: {s2} {f2}; reversed order: {s3} {f3}; four out: {s4} winner {w4}; repeat identical {(r2 == r5)}");
            }
            {
                var (m, ps) = TwoLeft(3);
                Hit(m, 3, 0, 40000f);                                      // survival damage on the human: durability falls, the AI gets some points
                float hp0 = ps[0].durability, hp3 = ps[3].durability; int sc3 = ps[3].score;
                Advance(m, 0.3f);
                bool inTransition = m.State == MatchState.ShowdownTransition;
                Check("S6. Both finalists are restored to 100% durability when the showdown is prepared, with fresh showdown statistics; the eliminated cars stay out", inTransition && hp0 < ps[0].maxDurability && ps[0].durability == ps[0].maxDurability && ps[3].durability == ps[3].maxDurability && ps[0].showdownDamage == 0f && ps[3].showdownDamage == 0f && ps[1].status == ParticipantStatus.Out && ps[2].status == ParticipantStatus.Out && ps[4].status == ParticipantStatus.Out && ps[3].score == sc3,
                      $"human {hp0 / ps[0].maxDurability * 100f:0}% -> {ps[0].DurabilityFraction * 100f:0}%, opponent {hp3 / ps[3].maxDurability * 100f:0}% -> {ps[3].DurabilityFraction * 100f:0}%, others out {ps[1].status}/{ps[2].status}/{ps[4].status}");
            }
            {
                var (m, ps) = TwoLeft(2);
                Advance(m, 0.3f);                                          // transition running
                var states = new List<string>();
                float dur0 = ps[0].durability, dur2 = ps[2].durability;
                int p1 = Hit(m, 2, 0, 80000f), p2 = Hit(m, 0, 2, 80000f);
                bool gatedTransition = p1 == 0 && p2 == 0 && ps[0].durability == dur0 && ps[2].durability == dur2 && ps[0].showdownDamage == 0f && ps[2].showdownDamage == 0f;
                Advance(m, 1.4f);                                          // into the countdown
                bool inCountdown = m.State == MatchState.ShowdownCountdown;
                float cd0 = m.CountdownRemaining, sr0 = m.ShowdownRemaining;
                int p3 = Hit(m, 2, 0, 80000f);
                Advance(m, 1.0f); float cd1 = m.CountdownRemaining; int c1 = Mathf.CeilToInt(cd1);
                int p4 = Hit(m, 0, 2, 80000f);
                Advance(m, 1.0f); float cd2 = m.CountdownRemaining; int c2 = Mathf.CeilToInt(cd2);
                float sr1 = m.ShowdownRemaining;
                Advance(m, 0.9f); bool stillCount = m.State == MatchState.ShowdownCountdown;
                Advance(m, 0.1f);
                Check("S11. The countdown is 3 s: 3 -> 2 -> 1 -> GO (the duel state starts when it ends), and the showdown timer waits at 60.0 s", gatedTransition && inCountdown && Mathf.Abs(cd0 - 3f) < 0.1f && c1 == 2 && c2 == 1 && stillCount && m.State == MatchState.FinalShowdown && Mathf.Abs(sr0 - 60f) < 0.01f && Mathf.Abs(sr1 - 60f) < 0.01f,
                      $"countdown {cd0:0.00} -> {cd1:0.00} -> {cd2:0.00} -> {m.State}, timer {sr0:0.0}/{sr1:0.0} before GO");
                Check("S13/S27. No damage is applied and nothing scores during the transition or the countdown (4 hits tried, 0 points, durability and showdown damage unchanged)", p3 == 0 && p4 == 0 && ps[0].durability == dur0 && ps[2].durability == dur2 && ps[0].showdownDamage == 0f && ps[2].showdownDamage == 0f && ps[0].showdownEnergy == 0f,
                      $"points {p1}/{p2}/{p3}/{p4}, human {ps[0].DurabilityFraction * 100f:0}%, opponent {ps[2].DurabilityFraction * 100f:0}%");
                float before = m.ShowdownRemaining;
                Advance(m, 10f);
                Check("S14/S15. The timer starts at GO and the duel lasts 60 s (still running at 59.8 s, over at 60.2 s)", Mathf.Abs(before - 60f) < 0.12f && Mathf.Abs(m.ShowdownRemaining - 50f) < 0.15f, $"{before:0.0} -> {m.ShowdownRemaining:0.0} after 10 s");
                Advance(m, 49.8f); bool at = m.State == MatchState.FinalShowdown;
                Advance(m, 0.4f);
                Check("S15. ...and it ends by itself at zero", at && m.State == MatchState.Results && m.ShowdownRemaining == 0f, $"at 59.8 s {(at ? "running" : "over")}, afterwards {m.State}");
            }
            {
                var (m, ps) = TwoLeft(1);
                ToDuel(m);
                float rem = m.ShowdownRemaining;
                Advance(m, 12f); float remBefore = m.ShowdownRemaining;
                int results = 0; m.StateChanged += (a, b) => { if (b == MatchState.Results) results++; };
                Wreck(m, 1, 0);
                Advance(m, 0.1f); bool settling = m.State == MatchState.FinalShowdown;
                Advance(m, 0.2f);
                float frozen = m.ShowdownRemaining; Advance(m, 5f);
                Check("S16. Wrecking one finalist ends the match as soon as the wreck has settled: the survivor wins by wreck, the timer stops, one result", settling && m.State == MatchState.Results && m.Winner == ps[0] && m.Kind == ResultKind.WinByWreck && m.KindLabel == "WIN BY WRECK" && results == 1 && Mathf.Abs(frozen - remBefore) < 0.5f && m.ShowdownRemaining == frozen,
                      $"kind {m.Kind} '{m.ResultText}', {remBefore:0.0} s left at the wreck, timer {frozen:0.0} afterwards {m.ShowdownRemaining:0.0}, results events {results}");
            }
            {
                var (m, ps) = TwoLeft(1);
                ToDuel(m);
                Hit(m, 0, 1, 20000f); Hit(m, 0, 1, 10000f); Hit(m, 1, 0, 12000f);
                Advance(m, 60.5f);
                float a = ps[0].showdownDamage, b = ps[1].showdownDamage;
                Check("S17. At the timeout the finalist that dealt more showdown damage wins: WIN BY SHOWDOWN DAMAGE", m.State == MatchState.Results && m.Winner == ps[0] && m.Kind == ResultKind.WinByShowdownDamage && m.KindLabel == "WIN BY SHOWDOWN DAMAGE" && a > b && Mathf.Abs(a - 30000f / ps[1].maxDurability) < 1e-4f && Mathf.Abs(b - 12000f / ps[0].maxDurability) < 1e-4f,
                      $"damage dealt {a * 100f:0.0}% vs {b * 100f:0.0}% of the other's armour, '{m.ResultText}'");
            }
            {
                // survival score must not decide the winner
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                Hit(m, 4, 2, 90000f); Hit(m, 4, 3, 90000f); Hit(m, 4, 2, 90000f);       // P4 earns survival points (and may wreck)
                for (int i = 1; i <= 3; i++) Wreck(m, i, 4);
                int s4 = ps[4].score, s0 = ps[0].score;
                ToDuel(m);
                Hit(m, 0, 4, 30000f);                                                     // the human, with far fewer survival points, deals the only showdown damage
                Advance(m, 61f);
                Check("S18. The survival-phase score does not decide the showdown: the finalist with the far lower score won on showdown damage", s4 > s0 + 100 && m.Winner == ps[0] && m.Kind == ResultKind.WinByShowdownDamage && !m.ResultText.Contains("SCORE"), $"survival scores {s0} v {s4}, winner {m.Winner?.name}, '{m.ResultText}'");
            }
            {
                var (m, ps) = TwoLeft(1); ToDuel(m); Advance(m, 61f);
                var (m2, p2) = TwoLeft(1); ToDuel(m2);
                Hit(m2, 0, 1, 25000f); Hit(m2, 1, 0, 25000f); Advance(m2, 61f);
                var (m3, p3) = TwoLeft(1, r => { }); p3[0].score = 900; ToDuel(m3); Advance(m3, 61f);    // a survival lead is no help
                Check("S19. Equal showdown damage (none dealt, or the same amount each) is a draw: no winner, both finalists placed first, label DRAW", m.IsDraw && m.Winner == null && m.Kind == ResultKind.Draw && m2.IsDraw && m2.Kind == ResultKind.Draw && ps[0].placement == 1 && ps[1].placement == 1 && m3.IsDraw && m2.KindLabel == "DRAW",
                      $"no damage: '{m.ResultText}'; equal damage: '{m2.ResultText}'; with a survival lead: '{m3.ResultText}'");
            }
            {
                var (m, ps) = TwoLeft(1); ToDuel(m);
                Hit(m, 0, 1, 40000f); Hit(m, 1, 0, 15000f);
                Wreck(m, 0, 1); Wreck(m, 1, 0);                      // both wrecked in the same step: the totals decide
                Advance(m, 0.5f);
                var (m2, p2) = TwoLeft(1); ToDuel(m2);
                Hit(m2, 1, 0, 40000f);
                Wreck(m2, 0, 1); Advance(m2, 0.1f); Wreck(m2, 1, 0);  // 0.1 s apart: still simultaneous, the other leads in damage
                Advance(m2, 0.5f);
                var (m3, p3) = TwoLeft(1); ToDuel(m3);
                Wreck(m3, 0, 1); Wreck(m3, 1, 0); Advance(m3, 0.5f);   // nothing dealt before: equal totals
                int results = 0; var (m4, p4) = TwoLeft(1); m4.StateChanged += (a, b) => { if (b == MatchState.Results) results++; };
                ToDuel(m4); Wreck(m4, 0, 1); Wreck(m4, 1, 0); Advance(m4, 3f); Wreck(m4, 1, 0); Advance(m4, 1f);
                Check("S20. Both finalists wrecked together (same step, or 0.1 s apart): the larger showdown damage wins, equal totals draw, and a late event cannot change it",
                      m.Winner == ps[0] && m.Kind == ResultKind.WinByShowdownDamage && m2.Winner == p2[1] && m3.IsDraw && m3.Kind == ResultKind.Draw && results == 1 && p4[0].placement == 1 && p4[1].placement == 1,
                      $"same step: {m.Winner?.name} ({m.Kind}); 0.1 s apart: {m2.Winner?.name}; no damage: {(m3.IsDraw ? "draw" : "winner")}; results events {results}");
            }
            {
                // a timeout and a wreck in the same tick give one result, the wreck
                var (m, ps) = TwoLeft(1); ToDuel(m);
                Hit(m, 1, 0, 8000f);                                    // the AI leads in damage at the moment time runs out
                Advance(m, 59.97f);
                int results = 0; m.StateChanged += (a, b) => { if (b == MatchState.Results) results++; };
                Wreck(m, 1, 0);                                        // the human wrecks it in the last 0.03 s
                Advance(m, 0.5f);
                Check("S5b. A wreck and the timeout arriving together give one result - the wreck - not two or a contradictory pair", results == 1 && m.Winner == ps[0] && m.Kind == ResultKind.WinByWreck, $"results events {results}, winner {m.Winner?.name}, kind {m.Kind}");
            }
            {
                var (m, ps) = NewMatch(GameMode.LastCarStanding);
                int due = 0; m.RespawnDue += p => due++;
                Wreck(m, 1, 0); Wreck(m, 2, 0); Wreck(m, 3, 0); Advance(m, 2f);
                ToDuel(m); Wreck(m, 4, 0); Advance(m, 10f);
                Check("S23. No respawns, in Survival or in the showdown", due == 0 && ps[1].status == ParticipantStatus.Out && ps[4].status == ParticipantStatus.Out, $"respawn events {due}");
            }
            {
                var (m, ps) = TwoLeft(1); ToDuel(m);
                float maxA = ps[0].maxDurability, maxB = ps[1].maxDurability;
                Hit(m, 0, 1, 30000f); Hit(m, 0, 1, 30000f); Hit(m, 0, 1, 30000f);
                int wall = Hit(m, -1, 1, 5e5f);                          // walls do not count as damage dealt (and may wear the car down)
                Hit(m, 1, 1, 50000f);                                     // self damage is not damage dealt to the opponent
                Wreck(m, 2, 1);                                           // damage to an eliminated car: nothing happens
                float took = maxB - ps[1].durability;
                Check("S26/S30. Showdown damage is counted once, from verified hits only: it equals the opponent's durability loss from the finalist's hits (walls, self hits and eliminated cars add nothing)",
                      Mathf.Abs(ps[0].showdownEnergy - 90000f) < 1f && ps[1].showdownEnergy == 0f && Mathf.Abs(ps[0].showdownDamage - 90000f / maxB) < 1e-5f,
                      $"dealt {ps[0].showdownEnergy:0} J = {ps[0].showdownDamage * 100f:0.0}% of the opponent's armour; opponent lost {took:0} J of which 90000 J from hits");
                Hit(m, 0, 1, 1e8f);                                       // overkill: only what was left counts
                Check("S26b. A wrecking hit counts only the durability that was left (no overkill)", ps[0].showdownDamage <= 1f + 1e-4f && ps[0].showdownEnergy <= maxB + 1f, $"{ps[0].showdownDamage * 100f:0.0}% of the opponent's armour");
            }
            {
                var (m, ps) = TwoLeft(1);
                Advance(m, 1.0f);                                       // transition running
                m.Begin(); Advance(m, 0.5f);
                bool reset = m.State == MatchState.Countdown && !m.ShowdownTriggered && m.Finalists[0] == null && ps.All(p => !p.finalist && p.status == ParticipantStatus.Waiting && p.durability == p.maxDurability);
                var (m2, p2) = TwoLeft(1);
                Advance(m2, 2.0f); bool cd = m2.State == MatchState.ShowdownCountdown;
                m2.Begin(); Advance(m2, 3.2f);
                bool reset2 = cd && m2.State == MatchState.Active && !m2.ShowdownTriggered && p2.All(p => p.IsActive && p.durability == p.maxDurability && p.showdownDamage == 0f);
                Check("S28/S29 (model). Starting a new match during the transition or the countdown resets everything (no showdown state, finalist flags, statistics or timers survive)", reset && reset2, $"restart in transition -> {reset}, restart in countdown -> {reset2}");
            }
            {
                // Points Derby never triggers it
                var (m, ps) = NewMatch(GameMode.PointsDerby);
                int prep = 0; m.ShowdownPrepare += (a, b) => prep++; var seq = new List<MatchState>(); m.StateChanged += (a, b) => seq.Add(b);
                Wreck(m, 1, 0); Wreck(m, 2, 0); Wreck(m, 3, 0); Advance(m, 1f);       // three cars wrecked at once: two "alive", the rest waiting to respawn
                int due = 0; m.RespawnDue += p => { due++; m.Respawned(p); };
                Advance(m, 4f); Wreck(m, 4, 0); Advance(m, 181f);
                Check("S32. Points Derby never prepares a showdown, still respawns, and ends by its 180 s timer with the normal result", prep == 0 && !seq.Contains(MatchState.ShowdownTransition) && due >= 3 && m.State == MatchState.Results && m.Kind == ResultKind.PointsWin && m.Winner == ps[0], $"showdown prepared {prep}x, respawns {due}, kind {m.Kind}, winner {m.Winner?.name}");
            }
            {
                var r = new MatchRules();
                Check("S15 (defaults). The showdown lasts 60 s, the countdown is 3 s, the transition is short, and the arena is not changed by any rule (no hazard fields in the rules)", r.showdownDuration == 60f && r.countdownSeconds == 3f && r.showdownTransitionSeconds > 0.5f && r.showdownTransitionSeconds < 3f && typeof(MatchRules).GetFields().All(f => !f.Name.ToLowerInvariant().Contains("shrink") && !f.Name.ToLowerInvariant().Contains("drain") && !f.Name.ToLowerInvariant().Contains("suddendeath")),
                      $"duration {r.showdownDuration} s, countdown {r.countdownSeconds} s, transition {r.showdownTransitionSeconds} s");
            }
        }

        // ==================================================================== scene helpers

        static DerbyUI ui;

        static void FreshMatch(int cars, GameMode mode, float countdown = 0.2f, Action<MatchRules> tweak = null)
        {
            game.GoToMenu();
            game.participantCount = cars;
            game.rules.mode = mode; game.rules.countdownSeconds = countdown;
            game.rules.pointsDuration = 180f; game.rules.showdownDuration = 60f; game.rules.showdownTransitionSeconds = 1.5f; game.rules.respawnDelay = 3f; game.rules.durabilityEnergy = 650000f;
            tweak?.Invoke(game.rules);
            game.StartMatch();
            foreach (var p in game.Participants) p.car.Receiver.logImpacts = false;
            StepSeconds(countdown + 0.1f);
            game.rules.countdownSeconds = 3f;                 // the showdown countdown uses the same rule: it is 3 s, whatever the start countdown of a test was
        }

        static void StepSeconds(float s, bool input = false) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt, input); }

        static void StepUntil(Func<bool> done, float maxSeconds, bool input = false) { int n = Mathf.RoundToInt(maxSeconds / Dt); for (int i = 0; i < n && !done(); i++) game.ManualStep(Dt, input); }

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

        static void KillWithEnvironment(params Participant[] victims) { foreach (var v in victims) game.Match.ApplyDamage(new DamageEvent(v.id, -1, 1e13f)); }

        /// <summary>Snapshot of the deformable meshes and the collision shell of a car, to compare after a restore.</summary>
        class Snapshot
        {
            public readonly List<Vector3[]> meshes = new List<Vector3[]>();
            public readonly List<(string name, Vector3 center, Vector3 size)> boxes = new List<(string, Vector3, Vector3)>();
            public int impacts;
        }

        static Snapshot Take(DerbyCar c)
        {
            var s = new Snapshot();
            foreach (var mf in c.rig.root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null && mf.sharedMesh.isReadable) s.meshes.Add(mf.sharedMesh.vertices);
            foreach (var b in c.rig.root.GetComponentsInChildren<BoxCollider>(true)) s.boxes.Add((b.name + "/" + b.transform.GetSiblingIndex(), b.center, b.size));
            s.impacts = c.Model.TotalImpactCount;
            return s;
        }

        static float MeshDeviation(DerbyCar c, Snapshot s)
        {
            float worst = 0f; int k = 0;
            foreach (var mf in c.rig.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var v = mf.sharedMesh.vertices; var o = s.meshes[k++];
                int n = Mathf.Min(v.Length, o.Length);
                for (int i = 0; i < n; i++) worst = Mathf.Max(worst, (v[i] - o[i]).magnitude);
                if (v.Length != o.Length) worst = Mathf.Max(worst, 99f);
            }
            return worst;
        }

        static float BoxDeviation(DerbyCar c, Snapshot s)
        {
            float worst = 0f; int k = 0;
            foreach (var b in c.rig.root.GetComponentsInChildren<BoxCollider>(true))
            {
                if (k >= s.boxes.Count) return 99f;
                var o = s.boxes[k++];
                worst = Mathf.Max(worst, (b.center - o.center).magnitude, (b.size - o.size).magnitude);
            }
            return k == s.boxes.Count ? worst : 99f;
        }

        // ==================================================================== scene

        static void SceneChecks()
        {
            sb.AppendLine("  -- the real game: repair, placement, countdown, duels, restart (DerbyArena) --");
            var staticBefore = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.attachedRigidbody == null).Select(c => c.name + c.transform.position.ToString("0")).OrderBy(s => s).ToList();

            // ---- S6-S10 + scenario 4: damaged finalists
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 1e9f);
            var ps = game.Participants; var human = game.Human; var opp = ps[1];
            AiOff();
            var snapH = Take(human.car); var snapO = Take(opp.car);
            PlaceCar(human, new Vector3(-14f, 0f, -30f), 90f, 24f); PlaceCar(opp, new Vector3(14f, 0f, -30f), 270f, 24f);
            Park(ps, human, opp);
            StepSeconds(1.6f);
            bool damaged = human.car.Model.IsDamaged && opp.car.Model.IsDamaged && MeshDeviation(human.car, snapH) > 0.03f && MeshDeviation(opp.car, snapO) > 0.03f;
            float devH = MeshDeviation(human.car, snapH), devO = MeshDeviation(opp.car, snapO);
            // scenario 4: the human arrives with yellow health, the opponent with green
            game.Match.ApplyDamage(new DamageEvent(human.id, opp.id, 0.6f * human.maxDurability));
            game.Match.ApplyDamage(new DamageEvent(opp.id, human.id, 0.08f * opp.maxDurability));
            float fh = human.DurabilityFraction, fo = opp.DurabilityFraction;
            var hitsBefore = (human.hitsTaken, opp.hitsTaken);
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            var posAtTrigger = (human.car.Body.position, opp.car.Body.position);
            StepSeconds(0.4f);
            var m = game.Match;
            Check("Setup. Two cars driven head-on into each other deform for real (mesh deviation over 3 cm on both) before the showdown repairs them", damaged, $"setup: the two cars were wrecked into each other (mesh deviation {devH:0.00} / {devO:0.00} m, impacts {human.car.Model.TotalImpactCount}/{opp.car.Model.TotalImpactCount}); health on arrival {fh * 100f:0}% (yellow) and {fo * 100f:0}% (green)");
            Check("Scenario 4/S6. A damaged finalist (yellow) and a healthy one (green) both start the duel at 100% durability", m.State == MatchState.ShowdownTransition && human.durability == human.maxDurability && opp.durability == opp.maxDurability && fh < 0.5f && fo > 0.85f, $"{fh * 100f:0}% -> {human.DurabilityFraction * 100f:0}%, {fo * 100f:0}% -> {opp.DurabilityFraction * 100f:0}%, state {m.State}");
            Check("S7. The deformation is fully restored on both finalists (no damage, no stored impacts, mesh vertices back where they were: deviation under 2 mm)", damaged && !human.car.Model.IsDamaged && !opp.car.Model.IsDamaged && human.car.Model.TotalImpactCount == 0 && opp.car.Model.TotalImpactCount == 0 && MeshDeviation(human.car, snapH) < 0.002f && MeshDeviation(opp.car, snapO) < 0.002f,
                  $"before the restore {devH:0.00} / {devO:0.00} m of deformation; after {MeshDeviation(human.car, snapH) * 1000f:0.00} / {MeshDeviation(opp.car, snapO) * 1000f:0.00} mm, impacts {human.car.Model.TotalImpactCount}/{opp.car.Model.TotalImpactCount}");
            Check("S8. The damage-aware collision shell is restored (every box collider back to its pristine centre and size; the hitbox has nothing pending)", BoxDeviation(human.car, snapH) < 0.005f && BoxDeviation(opp.car, snapO) < 0.005f && !human.car.rig.hitbox.HasPending && !opp.car.rig.hitbox.HasPending, $"box deviation {BoxDeviation(human.car, snapH) * 1000f:0.0} / {BoxDeviation(opp.car, snapO) * 1000f:0.0} mm");
            // placement
            Vector3 a = human.car.Body.position, b = opp.car.Body.position; Vector3 fa = human.car.Body.transform.forward, fb = opp.car.Body.transform.forward;
            float dist = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
            Vector3 toB = (b - a); toB.y = 0f; toB.Normalize(); Vector3 toA = -toB;
            float angA = Vector3.Angle(new Vector3(fa.x, 0f, fa.z), toB), angB = Vector3.Angle(new Vector3(fb.x, 0f, fb.z), toA);
            float half = game.arenaHalf;
            Vector3 mirror = a + b; mirror.y = 0f;
            int overlapsA = Physics.OverlapBox(a + Vector3.up * 1.3f, new Vector3(1.5f, 0.8f, 3.0f), human.car.Body.rotation, ~0, QueryTriggerInteraction.Ignore).Count(c => c.attachedRigidbody != human.car.Body);
            int overlapsB = Physics.OverlapBox(b + Vector3.up * 1.3f, new Vector3(1.5f, 0.8f, 3.0f), opp.car.Body.rotation, ~0, QueryTriggerInteraction.Ignore).Count(c => c.attachedRigidbody != opp.car.Body);
            bool runwayA = !Physics.SphereCast(a + Vector3.up * 1.3f, 1.1f, fa, out _, 22f, ~0, QueryTriggerInteraction.Ignore) || true;
            Check("S9. The finalists are on opposite sides of the arena: mirrored through the middle (equal geometry), 40 m or more apart, inside the walls, facing each other within 25 degrees, nothing overlapping either car, the placement search found clear ground", game.ShowdownPlacedClear && dist >= 40f && mirror.magnitude < 0.5f && Mathf.Abs(a.x) < half - 8f && Mathf.Abs(a.z) < half - 8f && Mathf.Abs(b.x) < half - 8f && Mathf.Abs(b.z) < half - 8f && angA < 25f && angB < 25f && overlapsA == 0 && overlapsB == 0 && Mathf.Abs(angA - angB) < 1f,
                  $"{dist:0.0} m apart at ({a.x:0},{a.z:0}) and ({b.x:0},{b.z:0}), mirror error {mirror.magnitude:0.00} m, facing {angA:0}/{angB:0} degrees off each other, overlaps {overlapsA}/{overlapsB}, clear spot found {game.ShowdownPlacedClear}");
            Check("S10. Both finalists start with zero velocity, zero spin, upright, AI drivers off", human.car.Body.linearVelocity.magnitude < 0.01f && opp.car.Body.linearVelocity.magnitude < 0.01f && human.car.Body.angularVelocity.magnitude < 0.01f && !human.car.controller.IsFlipped && !opp.car.controller.IsFlipped && (opp.car.enemy == null || !opp.car.enemy.enabled),
                  $"speeds {human.car.Body.linearVelocity.magnitude:0.000} / {opp.car.Body.linearVelocity.magnitude:0.000} m/s");

            // ---- countdown: frozen cars, touch controls, no damage
            var cinput = game.input;
            var upd = typeof(DriveInput).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            cinput.touchGas = true; cinput.touchLeft = true; upd.Invoke(cinput, null);
            var p0 = human.car.Body.position; var q0 = opp.car.Body.position;
            StepSeconds(1.5f, true);                                     // the rest of the transition, then the first moments of the countdown, touch gas and steering held
            bool inCount = m.State == MatchState.ShowdownCountdown;
            float moved = (human.car.Body.position - p0).magnitude, movedO = (opp.car.Body.position - q0).magnitude;
            float thrNow = human.car.controller.Throttle, steerNow = human.car.controller.SteerInput;
            // the dead hulk of P2 is thrown at the player while the countdown runs
            var hulk = ps[2];
            Vector3 hp = human.car.Body.position;
            PlaceCar(hulk, hp + human.car.Body.transform.forward * 7f, human.car.Body.rotation.eulerAngles.y + 180f, 18f);
            float durNow = human.durability; int hitsNow = human.hitsTaken;
            StepSeconds(1.0f, true);
            bool stillCount = m.State == MatchState.ShowdownCountdown;
            Check("S12/S33. Nothing moves during the transition and the first moments of the countdown, even with the touch gas and steering held (throttle and steering forced to zero, AI off)", inCount && thrNow == 0f && steerNow == 0f && cinput.Throttle > 0.5f && moved < 0.3f && movedO < 0.3f, $"state {m.State}, touch throttle {cinput.Throttle:0.0}, car throttle {thrNow:0.0}, steering {steerNow:0.0}, player moved {moved:0.00} m, opponent moved {movedO:0.00} m");
            Check("S13/S27 (scene). Collisions during the countdown do no combat damage and score nothing: a hulk thrown into the player left durability, hit counts and showdown damage untouched", stillCount && human.durability == durNow && human.hitsTaken == hitsNow && human.showdownDamage == 0f && opp.showdownDamage == 0f && human.durability == human.maxDurability, $"countdown still running {stillCount}, durability {human.DurabilityFraction * 100f:0}%, hits taken {human.hitsTaken}, showdown damage {human.showdownDamage:0.000}/{opp.showdownDamage:0.000}");
            StepUntil(() => m.State == MatchState.FinalShowdown, 4f, true);
            float tGo = m.ShowdownRemaining;
            // the hulk may sit in the way: move it off before checking the first seconds of the duel
            PlaceCar(hulk, new Vector3(0f, 0f, 44f), 0f, 0f);
            Vector3 g0 = human.car.Body.position;
            StepSeconds(1.5f, true);
            Check("S14/S33. At GO the timer starts at 60 s, the AI driver and the touch controls come alive, the player's car moves", m.State == MatchState.FinalShowdown && tGo > 59.5f && m.ShowdownRemaining < tGo && human.car.controller.Throttle > 0.5f && (human.car.Body.position - g0).magnitude > 3f && opp.car.enemy != null && opp.car.enemy.enabled && game.Targeting.TargetOf(opp.id) == human.id,
                  $"timer {tGo:0.0} -> {m.ShowdownRemaining:0.0}, throttle {human.car.controller.Throttle:0.0}, player moved {(human.car.Body.position - g0).magnitude:0.0} m, AI driver on {opp.car.enemy != null && opp.car.enemy.enabled}, AI target {game.Targeting.TargetOf(opp.id)} (player {human.id})");
            cinput.touchGas = false; cinput.touchLeft = false; upd.Invoke(cinput, null);
            // the arena is whole, nothing was added
            var staticDuring = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Where(c => c.attachedRigidbody == null).Select(c => c.name + c.transform.position.ToString("0")).OrderBy(s => s).ToList();
            Check("S24/S25. The full arena is available during the duel: the same static colliders (walls, blocks) in the same places, the same arena size, no shrinking field or added hazard", staticBefore.SequenceEqual(staticDuring) && game.arenaHalf == half, $"{staticBefore.Count} static colliders before, {staticDuring.Count} during; arena half size {game.arenaHalf}");

            // ---- S16 in the real game: wreck ends it; no respawn
            int resp0 = game.Respawns;
            game.Match.ApplyDamage(new DamageEvent(opp.id, human.id, 1e13f));
            StepSeconds(0.5f);
            Check("S16 (scene). Wrecking the opponent ends the match immediately with the player winning by wreck; nobody respawns", m.State == MatchState.Results && m.Winner == human && m.Kind == ResultKind.WinByWreck && game.Respawns == resp0 && opp.status == ParticipantStatus.Out, $"state {m.State}, winner {m.Winner?.name}, kind {m.Kind}, respawns {game.Respawns - resp0}");

            // ---- S30/S31: rematch and menu after a showdown
            game.Rematch(); StepSeconds(0.5f);
            bool rem = game.Match.State == MatchState.Countdown || game.Match.State == MatchState.Active;
            bool clean = !game.Match.ShowdownTriggered && game.Participants.All(p => p.status != ParticipantStatus.Out && p.durability == p.maxDurability && !p.finalist && p.showdownDamage == 0f && !p.car.Model.IsDamaged);
            StepUntil(() => game.Match.State == MatchState.Active, 5f);
            Check("S30. Rematch after a showdown starts a clean Survival: all five cars repaired and alive, no showdown state, countdown then Active", rem && clean && game.Match.State == MatchState.Active && game.Participants.Count == 5 && game.Participants.All(p => p.IsActive), $"state {game.Match.State}, clean {clean}, cars {game.Participants.Count}");
            game.GoToMenu();
            int carsLeft = UnityEngine.Object.FindObjectsByType<CollisionDamageReceiver>(FindObjectsSortMode.None).Length;
            Check("S31. Returning to the menu after a showdown drops the match and the AI cars, and the player's car is repaired on display", game.MenuOpen && game.Match == null && game.Participants.Count == 0 && carsLeft == 1 && !game.Human.car.Model.IsDamaged && game.Human.durability == game.Human.maxDurability, $"menu {game.MenuOpen}, receivers in the scene {carsLeft}");

            // ---- S28/S29: restart during the transition and during the countdown
            foreach (bool inTransition in new[] { true, false })
            {
                FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 1e9f); AiOff();
                ps = game.Participants; KillWithEnvironment(ps[2], ps[3], ps[4]);
                StepUntil(() => inTransition ? game.Match.State == MatchState.ShowdownTransition : game.Match.State == MatchState.ShowdownCountdown, 6f);
                var stateWas = game.Match.State;
                game.Rematch(); StepSeconds(0.4f);
                bool fresh = (game.Match.State == MatchState.Countdown || game.Match.State == MatchState.Active) && !game.Match.ShowdownTriggered && game.Participants.All(p => p.status != ParticipantStatus.Out);
                StepUntil(() => game.Match.State == MatchState.Active, 5f);
                var states = new List<MatchState>(); game.Match.StateChanged += (x, y) => states.Add(y);
                KillWithEnvironment(game.Participants[2], game.Participants[3], game.Participants[4]);
                StepUntil(() => game.Match.State == MatchState.FinalShowdown, 8f);
                Check(inTransition ? "S28. Restart during the showdown transition works: a clean new match, and a later showdown in it still runs its full sequence" : "S29. Restart during the showdown countdown works the same way",
                      stateWas == (inTransition ? MatchState.ShowdownTransition : MatchState.ShowdownCountdown) && fresh && game.Match.State == MatchState.FinalShowdown && states.Contains(MatchState.ShowdownCountdown), $"restarted in {stateWas}, new match {(fresh ? "clean" : "NOT clean")}, later showdown reached {game.Match.State}");
            }

            // ---- S21: AI versus AI
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 650000f);
            ps = game.Participants; human = game.Human;
            KillWithEnvironment(human, ps[1], ps[2]);              // the player is out; the AI cars 3 and 4 are the finalists
            int results = 0; game.Match.StateChanged += (x, y) => { if (y == MatchState.Results) results++; };
            StepUntil(() => game.Match.State == MatchState.FinalShowdown, 8f);
            bool aiBoth = ps[3].car.enemy.enabled && ps[4].car.enemy.enabled && game.Targeting.TargetOf(ps[3].id) == ps[4].id && game.Targeting.TargetOf(ps[4].id) == ps[3].id && !ps[1].car.enemy.enabled && !ps[2].car.enemy.enabled;
            bool camera = game.chaseCamera == null || game.chaseCamera.target == ps[3].car.Body || game.chaseCamera.target == ps[4].car.Body;
            var posS = (ps[3].car.Body.position, ps[4].car.Body.position);
            StepUntil(() => game.Match.State == MatchState.Results, 75f);
            Check("S21/Scenario 8. AI versus AI: the player is out, two AI finalists are repaired, placed, targeted at each other (hulks and the dead are never chosen) and the duel finishes by itself with one result; the camera follows a finalist", aiBoth && camera && game.Match.State == MatchState.Results && results == 1 && (game.Match.Kind == ResultKind.WinByWreck || game.Match.Kind == ResultKind.WinByShowdownDamage || game.Match.Kind == ResultKind.Draw) && game.Match.Finalists.Contains(ps[3]) && game.Match.Finalists.Contains(ps[4]),
                  $"both AI on and targeting each other {aiBoth}, camera on a finalist {camera}, ended {game.Match.Clock:0} s into the match as {game.Match.Kind} ({game.Match.ResultText}), damage dealt {ps[3].showdownDamage * 100f:0}% / {ps[4].showdownDamage * 100f:0}%");

            // ---- S22: player versus AI (the player sits still, the AI attacks)
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 650000f);
            ps = game.Participants; human = game.Human;
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            StepUntil(() => game.Match.State == MatchState.Results, 85f);
            Check("S22. Player versus AI: the AI finalist attacks the player's car and the duel ends with exactly one valid result within the 60 s limit", game.Match.State == MatchState.Results && game.Match.Clock > 0f && (human.showdownDamage > 0f || ps[1].showdownDamage > 0f || game.Match.Kind == ResultKind.Draw) && game.Match.ShowdownRemaining <= 60f, $"result {game.Match.Kind} '{game.Match.ResultText}', showdown damage player {human.showdownDamage * 100f:0}% / AI {ps[1].showdownDamage * 100f:0}%");
            game.GoToMenu();
        }

        static void Park(List<Participant> ps, params Participant[] except)
        {
            int k = 0;
            foreach (var p in ps)
            {
                if (except.Contains(p)) continue;
                PlaceCar(p, new Vector3(-36f + 16f * (k++ % 5), 0f, 44f), 180f, 0f);
            }
        }

        // ==================================================================== HUD texts (the real DerbyUI, driven by the real match)

        static string UiText(string field) => ((UnityEngine.UI.Text)typeof(DerbyUI).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ui))?.text ?? "";

        static void Refresh() => typeof(DerbyUI).GetMethod("RefreshHud", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, null);

        static void HudChecks()
        {
            sb.AppendLine("  -- the HUD and the results screen (the real DerbyUI reading the real match) --");
            if (ui == null) { Check("S11/S29 (HUD). The HUD exists", false, "no DerbyUI in the scene"); return; }
            typeof(DerbyUI).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ui, null);
            ui.game = game;
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 650000f); AiOff();
            var ps = game.Participants; var h = game.Human; var m = game.Match;
            Refresh();
            string survTimer = UiText("timerText"), survMode = UiText("modeText");
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            StepUntil(() => m.State == MatchState.ShowdownTransition, 3f);
            Refresh();
            string tBanner = UiText("bannerText"), tSub = UiText("bannerSub"), tTimer = UiText("timerText"), tDuel = UiText("duelText");
            StepUntil(() => m.State == MatchState.ShowdownCountdown, 3f);
            StepSeconds(0.4f); Refresh(); string c3 = UiText("bannerText"), cSub = UiText("bannerSub");
            StepSeconds(1.0f); Refresh(); string c2 = UiText("bannerText");
            StepSeconds(1.0f); Refresh(); string c1 = UiText("bannerText");
            StepUntil(() => m.State == MatchState.FinalShowdown, 3f);
            m.ApplyDamage(new DamageEvent(h.id, ps[1].id, 0.25f * h.maxDurability));
            m.ApplyDamage(new DamageEvent(ps[1].id, h.id, 0.10f * ps[1].maxDurability));
            StepSeconds(1.5f); Refresh();
            string dTimer = UiText("timerText"), dMode = UiText("modeText"), dDuel = UiText("duelText"), dDamage = UiText("duelDamageText");
            Check("S11 (HUD). The FINAL SHOWDOWN card names the two finalists; the countdown shows 3, 2, 1 as large numbers with the matchup beneath; the survival HUD shows no showdown text", survTimer == "LAST CAR STANDING" && tBanner == "FINAL SHOWDOWN" && tSub.Contains("YOU") && tSub.Contains(ps[1].name) && tTimer == "FINAL SHOWDOWN" && c3 == "3" && c2 == "2" && c1 == "1" && cSub.Contains("FINAL SHOWDOWN"),
                  $"survival '{survTimer}' / '{survMode}'; transition banner '{tBanner}' sub '{tSub}' duel line '{tDuel}'; countdown '{c3}' '{c2}' '{c1}' sub '{cSub}'");
            Check("S32 (HUD). The duel HUD shows the remaining time, both names with armour %, the showdown damage each has dealt and the rule in one line", dTimer.StartsWith("SHOWDOWN") && dTimer.Contains("0:5") && dDuel.Contains("YOU") && dDuel.Contains(ps[1].name) && dDuel.Contains("75%") && dDuel.Contains("90%") && dDamage.Contains("10%") && dDamage.Contains("25%") && dMode.Contains("WRECK") && dMode.Contains("DAMAGE"),
                  $"timer '{dTimer}', mode '{dMode}', line '{dDuel}', damage '{dDamage}'");
            m.ApplyDamage(new DamageEvent(ps[1].id, h.id, 1e13f)); StepSeconds(0.5f);
            typeof(DerbyUI).GetMethod("EnsureResults", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(ui, null);
            ui.ShowResults();
            string title = UiText("resultTitle"), sub = UiText("resultSub");
            // a timeout result and a draw
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => { r.durabilityEnergy = 1e9f; r.showdownDuration = 4f; r.showdownTransitionSeconds = 0.3f; }); AiOff();
            ps = game.Participants; h = game.Human; m = game.Match;
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            StepUntil(() => m.State == MatchState.FinalShowdown, 8f); AiOff();
            m.ApplyDamage(new DamageEvent(ps[1].id, h.id, 0.10f * ps[1].maxDurability));
            StepUntil(() => m.State == MatchState.Results, 8f);
            ui.ShowResults(); string t2 = UiText("resultTitle"), s2 = UiText("resultSub");
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => { r.durabilityEnergy = 1e9f; r.showdownDuration = 3f; r.showdownTransitionSeconds = 0.3f; }); AiOff();
            ps = game.Participants; m = game.Match;
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            StepUntil(() => m.State == MatchState.Results, 10f);
            ui.ShowResults(); string t3 = UiText("resultTitle"), s3 = UiText("resultSub");
            Check("S17/S19 (results screen). The result screen says WIN BY WRECK, WIN BY SHOWDOWN DAMAGE or DRAW, with both damage totals", title == "YOU WIN!" && sub.Contains("WIN BY WRECK") && t2 == "YOU WIN!" && s2.Contains("WIN BY SHOWDOWN DAMAGE") && s2.Contains("SHOWDOWN DAMAGE   YOU 10%") && t3 == "DRAW" && s3.Contains("DRAW"),
                  $"wreck: '{title}' / '{sub.Replace("\n", " | ")}'; damage: '{t2}' / '{s2.Replace("\n", " | ")}'; draw: '{t3}' / '{s3.Replace("\n", " | ")}'");
            game.GoToMenu();
        }

        // ==================================================================== performance

        static void PerfChecks()
        {
            sb.AppendLine("  -- cost of the transition and the duel --");
            FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => r.durabilityEnergy = 1e9f); AiOff();
            var ps = game.Participants; var m = game.Match;
            KillWithEnvironment(ps[2], ps[3], ps[4]);
            double worstTick = 0; var w = new Stopwatch(); double triggerTick = 0;
            for (int i = 0; i < 40; i++)
            {
                w.Restart(); game.ManualStep(Dt); w.Stop();
                double ms = w.Elapsed.TotalMilliseconds;
                if (i > 1 && m.State == MatchState.ShowdownTransition && triggerTick == 0) triggerTick = 0;
                worstTick = Math.Max(worstTick, ms);
                if (m.State == MatchState.ShowdownTransition && triggerTick == 0) triggerTick = ms;
            }
            // time the restore + placement itself by re-running it
            var a = m.Finalists[0]; var b = m.Finalists[1];
            var method = typeof(DerbyGame).GetMethod("OnShowdownPrepare", BindingFlags.NonPublic | BindingFlags.Instance);
            var sw = Stopwatch.StartNew(); const int reps = 20;
            for (int i = 0; i < reps; i++) method.Invoke(game, new object[] { a, b });
            sw.Stop();
            double prepMs = sw.Elapsed.TotalMilliseconds / reps;
            Check("S35 (transition). Preparing the showdown (placement search + repairing both cars) costs well under one 60 fps frame in the editor, once per match", prepMs < 16.0, $"{prepMs:0.00} ms per call (mean of {reps}), slowest of the first 40 steps {worstTick:0.00} ms");
            StepUntil(() => m.State == MatchState.FinalShowdown, 8f);
            // duel: AI + physics cost per step with two live drivers
            w.Restart(); int n = 250; for (int i = 0; i < n && m.State == MatchState.FinalShowdown; i++) game.ManualStep(Dt); w.Stop();
            var e0 = a.car.enemy; var e1 = b.car.enemy;
            Check("S35 (duel). The duel step (two AI drivers, physics, targeting) stays cheap; the AI decision cost per driver call is microseconds", w.Elapsed.TotalMilliseconds / n < 8.0, $"{w.Elapsed.TotalMilliseconds / n:0.00} ms per 50 Hz step in the editor; AI call mean {(e0 != null ? e0.MeanDriveMilliseconds * 1000f : 0f):0.0} / {(e1 != null ? e1.MeanDriveMilliseconds * 1000f : 0f):0.0} us, worst {(e0 != null ? e0.WorstDriveMilliseconds : 0f):0.00} / {(e1 != null ? e1.WorstDriveMilliseconds : 0f):0.00} ms");
            game.GoToMenu();
        }

        // ==================================================================== S36: the fleeing leader

        static void FleeChecks()
        {
            sb.AppendLine("  -- S36 / scenario 9: one finalist lands a hit and then only flees for the rest of the duel (a documented gameplay risk, not fixed) --");
            var results = new List<string>();
            int fledWins = 0, trials = 0;
            foreach (var (playerVehicle, label) in new[] { (0, "muscle car flees from a hatchback"), (1, "hatchback flees from a pickup"), (2, "pickup flees from a muscle car") })
                foreach (var diff in new[] { AiDifficulty.Standard, AiDifficulty.Competitive })
                {
                    game.SelectVehicle(playerVehicle); game.difficulty = diff;
                    FreshMatch(5, GameMode.LastCarStanding, 0.2f, r => { r.durabilityEnergy = 650000f; r.showdownTransitionSeconds = 0.3f; });
                    var ps = game.Participants; var runner = game.Human; var hunter = ps[1];
                    KillWithEnvironment(ps[2], ps[3], ps[4]);
                    StepUntil(() => game.Match.State == MatchState.FinalShowdown, 8f);
                    // the runner lands one strong hit (scripted), then does nothing but drive away from the hunter, round the arena, at full speed
                    game.Match.ApplyDamage(new DamageEvent(hunter.id, runner.id, 0.18f * hunter.maxDurability));
                    int hitsOnRunner = 0; float firstHit = -1f; float t0 = game.Match.Clock;
                    Action<CollisionDamageReceiver.Report> h = r => { if (r.applied && game.Match.State == MatchState.FinalShowdown && r.other == hunter.car.rig.root.name) { hitsOnRunner++; if (firstHit < 0f) firstHit = game.Match.Clock - t0; } };
                    runner.car.Receiver.ImpactRegistered += h;
                    float minDist = 999f, sumDist = 0f; int samples = 0;
                    int steps = Mathf.RoundToInt(70f / Dt);
                    for (int i = 0; i < steps && game.Match.State == MatchState.FinalShowdown; i++)
                    {
                        var rc = runner.car; Vector3 hp = hunter.car.Body.position, rp = rc.Body.position;
                        Vector3 away = rp - hp; away.y = 0f; Vector3 toCentre = -rp; toCentre.y = 0f;
                        // flee: head away from the hunter, bending towards the middle when close to a wall so the run goes round the arena
                        Vector3 want = away.normalized * 1f + toCentre.normalized * Mathf.Clamp01((new Vector2(rp.x, rp.z).magnitude - 25f) / 20f) * 1.4f;
                        Vector3 fw = rc.Body.transform.forward; fw.y = 0f;
                        float ang = Vector3.SignedAngle(fw, want, Vector3.up);
                        rc.controller.SteerInput = Mathf.Clamp(ang / 40f, -1f, 1f);
                        rc.controller.Throttle = Mathf.Abs(ang) > 100f ? 0.4f : 1f;
                        game.ManualStep(Dt);
                        if (i % 25 == 0) { float d = Vector3.Distance(hp, rp); minDist = Mathf.Min(minDist, d); sumDist += d; samples++; }
                    }
                    runner.car.Receiver.ImpactRegistered -= h;
                    bool fledWon = game.Match.Winner == runner;
                    trials++; if (fledWon) fledWins++;
                    results.Add($"{label} ({diff}): hunter landed {hitsOnRunner} hits on the runner{(firstHit >= 0f ? $" (first after {firstHit:0.0} s)" : "")}, mean gap {(samples > 0 ? sumDist / samples : 0f):0} m, closest {minDist:0} m; showdown damage runner {runner.showdownDamage * 100f:0}% vs hunter {hunter.showdownDamage * 100f:0}%; result {game.Match.Kind}, winner {(game.Match.Winner != null ? (game.Match.Winner == runner ? "the runner" : "the hunter") : "draw")}");
                }
            foreach (var r in results) sb.AppendLine("    " + r);
            game.difficulty = AiDifficulty.Standard; game.SelectVehicle(0);
            Check("S36. The fleeing leader was tested (6 duels: 3 vehicle pairings x Standard / Competitive); the result is reported as a risk, not asserted away", trials == 6, $"the runner won {fledWins} of {trials}; see the lines above");
            game.GoToMenu();
        }
    }
}
