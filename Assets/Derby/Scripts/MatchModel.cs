using System;
using System.Collections.Generic;
using CarWreck.Driving;
using UnityEngine;

namespace CarWreck.Derby
{
    public enum ControlType { Human, AI }
    public enum GameMode { PointsDerby, LastCarStanding }
    public enum MatchState { Menu, Preparing, Countdown, Active, FinalShowdown, Results }

    /// <summary>
    /// Active: driving and fighting. Wrecked: durability ran out, the hulk is still on the track and (Points Derby) comes back after the respawn delay.
    /// Out: eliminated for the rest of the match (Last Car Standing). Waiting: not in a match yet.
    /// </summary>
    public enum ParticipantStatus { Waiting, Active, Wrecked, Out }

    /// <summary>Who is a valid opponent of whom. V1 is free-for-all; a later team mode only has to supply another rule.</summary>
    public interface IOpponentRule
    {
        bool IsOpponent(Participant a, Participant b);
    }

    public sealed class FreeForAllRule : IOpponentRule
    {
        public static readonly FreeForAllRule Instance = new FreeForAllRule();
        public bool IsOpponent(Participant a, Participant b) => a != null && b != null && a.id != b.id;
    }

    /// <summary>
    /// One driver in a match: identity, vehicle choice, personality (AI), durability, score and statistics. It says nothing about how the car is driven:
    /// <see cref="control"/> is the only difference between the human and the AI drivers, and no rule (targeting, scoring, wrecking) reads it.
    /// </summary>
    public class Participant
    {
        public readonly int id;
        public string name;
        public ControlType control;
        /// <summary>For future team modes (-1 = no team). Not read by any V1 rule.</summary>
        public int team = -1;
        public int vehicleIndex, paintIndex;
        public DriverProfile profile;
        public ParticipantStatus status = ParticipantStatus.Waiting;
        public float durability, maxDurability;
        public int score;
        public int wrecksDealt, timesWrecked, hitsDealt, hitsTaken;
        public float damageDealt, damageTaken;     // J: collision energy this car's hits put into opponents / that opponents put into it
        public float wreckedAt = -99f, outAt = -99f, respawnAt;
        public int lastAttackerId = -1;
        public float lastAttackedAt = -99f;
        public int placement;                      // filled in when the match ends (1 = winner)
        /// <summary>The scene object (null in the pure-logic tests).</summary>
        public DerbyCar car;

        public Participant(int id, string name, ControlType control) { this.id = id; this.name = name; this.control = control; }

        public bool IsActive => status == ParticipantStatus.Active;
        public float DurabilityFraction => maxDurability > 0f ? Mathf.Clamp01(durability / maxDurability) : 0f;

        public void ResetForMatch(float maxDurability)
        {
            this.maxDurability = maxDurability; durability = maxDurability;
            status = ParticipantStatus.Waiting;
            score = wrecksDealt = timesWrecked = hitsDealt = hitsTaken = 0;
            damageDealt = damageTaken = 0f;
            wreckedAt = outAt = -99f; respawnAt = 0f; lastAttackerId = -1; lastAttackedAt = -99f; placement = 0;
        }
    }

    /// <summary>Every tunable number of a match (modes, timers, durability, scoring) in one place.</summary>
    [Serializable]
    public class MatchRules
    {
        public GameMode mode = GameMode.PointsDerby;
        [Header("Timers (s)")]
        public float pointsDuration = 180f;
        public float showdownDuration = 60f;
        public float countdownSeconds = 3f;
        [Tooltip("Points Derby: seconds a wrecked car stays on the track before it comes back.")]
        public float respawnDelay = 3f;
        [Tooltip("Eliminations this close together (s) count as simultaneous.")]
        public float simultaneousWindow = 0.15f;

        [Header("Durability")]
        [Tooltip("Collision energy (J) a 1400 kg car absorbs before it is wrecked. Heavier cars scale with mass. Energy is the damage receiver's own number for what the hit did to this car (after the two cars share the collision energy).")]
        public float durabilityEnergy = 450000f;
        public float durabilityReferenceMass = 1400f;
        [Tooltip("How much of the energy of a hit against walls and obstacles counts against durability (0 = environment cannot hurt a car).")]
        public float environmentDamageFactor = 0.4f;

        [Header("Scoring (victim-side energy of one hit, J -> points)")]
        public float[] tierEnergy = { 3000f, 15000f, 40000f, 90000f };
        public int[] tierPoints = { 10, 30, 70, 120 };
        public string[] tierNames = { "GLANCING", "SOLID HIT", "HEAVY HIT", "DEVASTATING" };
        public int wreckBonus = 150;
        [Tooltip("The same attacker scores on the same victim again only after this long (s), or at once with a clearly harder hit (>= 1.5x).")]
        public float pairCooldown = 0.75f;
        [Tooltip("A car that knocks an opponent into a wall still gets the wreck bonus if it hit it within this time (s).")]
        public float creditWindow = 4f;

        public bool RespawnsEnabled => mode == GameMode.PointsDerby;
        public float DurabilityFor(float massKg) => durabilityEnergy * Mathf.Max(0.1f, massKg) / Mathf.Max(1f, durabilityReferenceMass);

        /// <summary>Tier index (0..) for the victim-side energy of a hit, -1 when it is too light to score.</summary>
        public int TierFor(float energy)
        {
            int t = -1;
            for (int i = 0; i < tierEnergy.Length; i++) if (energy >= tierEnergy[i]) t = i;
            return t;
        }
    }

    /// <summary>One verified hit, as the damage receiver reports it: the victim's absorbed energy and who caused it (-1 = walls and obstacles).</summary>
    public struct DamageEvent
    {
        public int victim, attacker;
        public float energy, strength;
        public DamageEvent(int victim, int attacker, float energy, float strength = 1f) { this.victim = victim; this.attacker = attacker; this.energy = energy; this.strength = strength; }
    }

    /// <summary>
    /// The rules of a match, with no scene objects in it: participants, the state machine (countdown, active, final showdown, results), damage -> durability and
    /// points, wrecks, respawns and the winner. The scene only feeds it <see cref="DamageEvent"/>s and calls <see cref="Tick"/>; because nothing here reads the
    /// control type, the human and the AI drivers follow exactly the same rules. Deterministic: the same events and ticks give the same result.
    /// </summary>
    public class DerbyMatch
    {
        public readonly MatchRules rules;
        public readonly List<Participant> participants;
        public IOpponentRule opponentRule = FreeForAllRule.Instance;

        public MatchState State { get; private set; } = MatchState.Menu;
        /// <summary>Seconds since the match went active.</summary>
        public float Clock { get; private set; }
        public float CountdownRemaining { get; private set; }
        public float TimeRemaining => rules.mode == GameMode.PointsDerby ? Mathf.Max(0f, rules.pointsDuration - Clock) : 0f;
        public float ShowdownRemaining { get; private set; }
        public Participant Winner { get; private set; }
        public bool IsDraw { get; private set; }
        public string ResultText { get; private set; } = "";
        public readonly List<Participant> Ranking = new List<Participant>();
        public int DuplicateEventsIgnored { get; private set; }
        public int EventsScored { get; private set; }

        public event Action<MatchState, MatchState> StateChanged;
        /// <summary>(victim, attacker that gets the credit or null)</summary>
        public event Action<Participant, Participant> Wrecked;
        /// <summary>(attacker, victim, points, tier name)</summary>
        public event Action<Participant, Participant, int, string> Scored;
        /// <summary>A wrecked car's respawn delay is over: the scene places it, then calls <see cref="Respawned"/>.</summary>
        public event Action<Participant> RespawnDue;
        /// <summary>The final showdown began with these two cars.</summary>
        public event Action ShowdownStarted;

        readonly Dictionary<long, PairHit> pairs = new Dictionary<long, PairHit>();
        struct PairHit { public float time, energy; }
        float endPendingSince = -1f, lastWreckTime = -99f;

        public DerbyMatch(MatchRules rules, List<Participant> participants)
        {
            this.rules = rules;
            this.participants = participants;
        }

        public Participant Find(int id)
        {
            for (int i = 0; i < participants.Count; i++) if (participants[i].id == id) return participants[i];
            return null;
        }

        public int ActiveCount { get { int n = 0; foreach (var p in participants) if (p.IsActive) n++; return n; } }
        public bool InPlay => State == MatchState.Active || State == MatchState.FinalShowdown;

        void SetState(MatchState s) { if (s == State) return; var old = State; State = s; StateChanged?.Invoke(old, s); }

        /// <summary>Menu / Preparing markers used by the scene while it builds the cars.</summary>
        public void SetPreparing() => SetState(MatchState.Preparing);

        /// <summary>Everyone starts fresh and the countdown runs. The scene has placed the cars.</summary>
        public void Begin()
        {
            foreach (var p in participants) p.ResetForMatch(rules.DurabilityFor(p.car != null && p.car.def != null ? p.car.def.mass : rules.durabilityReferenceMass));
            pairs.Clear();
            Clock = 0f; ShowdownRemaining = 0f; Winner = null; IsDraw = false; ResultText = ""; Ranking.Clear();
            DuplicateEventsIgnored = EventsScored = 0;
            endPendingSince = -1f; lastWreckTime = -99f;
            CountdownRemaining = Mathf.Max(0f, rules.countdownSeconds);
            SetState(MatchState.Countdown);
            if (CountdownRemaining <= 0f) Activate();
        }

        void Activate()
        {
            foreach (var p in participants) p.status = ParticipantStatus.Active;
            CountdownRemaining = 0f;
            SetState(MatchState.Active);
        }

        /// <summary>Back to the menu state (no match running).</summary>
        public void ToMenu()
        {
            foreach (var p in participants) p.status = ParticipantStatus.Waiting;
            SetState(MatchState.Menu);
        }

        // ------------------------------------------------------------------ time

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            switch (State)
            {
                case MatchState.Countdown:
                    CountdownRemaining -= dt;
                    if (CountdownRemaining <= 0f) Activate();
                    break;
                case MatchState.Active:
                case MatchState.FinalShowdown:
                    Clock += dt;
                    if (rules.RespawnsEnabled) UpdateRespawns();
                    if (rules.mode == GameMode.PointsDerby)
                    {
                        if (Clock >= rules.pointsDuration) EndPointsDerby();
                    }
                    else UpdateLastCarStanding(dt);
                    break;
            }
        }

        void UpdateRespawns()
        {
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                if (p.status == ParticipantStatus.Wrecked && Clock >= p.respawnAt) RespawnDue?.Invoke(p);
            }
        }

        /// <summary>The scene placed a wrecked car at a safe spot with fresh geometry: it fights again (score is kept).</summary>
        public void Respawned(Participant p)
        {
            if (!InPlay || p.status != ParticipantStatus.Wrecked) return;
            p.status = ParticipantStatus.Active;
            p.durability = p.maxDurability;
            p.wreckedAt = -99f;
            p.lastAttackerId = -1; p.lastAttackedAt = -99f;
            foreach (var key in new List<long>(pairs.Keys)) if (key / 4096 == p.id || key % 4096 == p.id) pairs.Remove(key);
        }

        // ------------------------------------------------------------------ damage, points, wrecks

        static long PairKey(int attacker, int victim) => (long)attacker * 4096 + victim;

        /// <summary>The attacker may still score: active, or wrecked just now (the other half of a head-on that wrecked both).</summary>
        bool CanEarn(Participant a) => a != null && (a.IsActive || (a.wreckedAt >= 0f && Clock - a.wreckedAt <= rules.simultaneousWindow));

        /// <summary>
        /// Applies one verified hit. Returns the points awarded. Nothing happens outside a running match, to a car that is already wrecked, or for a hit that is
        /// too light; the attacker (never the victim itself, never a wrecked car) scores by the victim-side energy of the hit; walls and obstacles only wear
        /// durability down. The same attacker/victim pair cannot score again within the pair cooldown unless the hit is clearly harder, so a pinned,
        /// jittering contact is not farmable.
        /// </summary>
        public int ApplyDamage(DamageEvent e)
        {
            if (!InPlay) return 0;
            var victim = Find(e.victim);
            if (victim == null || !victim.IsActive || !(e.energy > 0f)) return 0;
            Participant attacker = e.attacker >= 0 && e.attacker != e.victim ? Find(e.attacker) : null;
            bool opponent = attacker != null && opponentRule.IsOpponent(attacker, victim);

            // durability: a car's hit counts in full, walls and obstacles at the environment factor
            float wear = attacker != null ? e.energy : e.energy * rules.environmentDamageFactor;
            victim.durability -= wear;
            victim.damageTaken += e.energy; victim.hitsTaken++;
            if (attacker != null) { victim.lastAttackerId = attacker.id; victim.lastAttackedAt = Clock; }

            int points = 0;
            if (opponent && CanEarn(attacker))
            {
                int tier = rules.TierFor(e.energy);
                if (tier >= 0)
                {
                    long key = PairKey(attacker.id, victim.id);
                    bool ok = true;
                    if (pairs.TryGetValue(key, out var last) && Clock - last.time < rules.pairCooldown && e.energy < last.energy * 1.5f) ok = false;
                    if (ok)
                    {
                        pairs[key] = new PairHit { time = Clock, energy = e.energy };
                        points = rules.tierPoints[tier];
                        attacker.score += points; attacker.hitsDealt++; attacker.damageDealt += e.energy;
                        EventsScored++;
                        Scored?.Invoke(attacker, victim, points, tier < rules.tierNames.Length ? rules.tierNames[tier] : "HIT");
                    }
                    else DuplicateEventsIgnored++;
                }
            }

            if (victim.durability <= 0f)
            {
                // who gets the wreck bonus: the car that just hit it, or one that hit it very recently before a wall finished it
                Participant credit = opponent && CanEarn(attacker) ? attacker : null;
                if (credit == null && victim.lastAttackerId >= 0 && Clock - victim.lastAttackedAt <= rules.creditWindow)
                {
                    var c = Find(victim.lastAttackerId);
                    if (c != null && c != victim && opponentRule.IsOpponent(c, victim) && CanEarn(c)) credit = c;
                }
                Wreck(victim, credit);
            }
            return points;
        }

        void Wreck(Participant victim, Participant credit)
        {
            victim.durability = 0f;
            victim.timesWrecked++;
            victim.wreckedAt = Clock;
            lastWreckTime = Clock;
            if (rules.RespawnsEnabled) { victim.status = ParticipantStatus.Wrecked; victim.respawnAt = Clock + rules.respawnDelay; }
            else { victim.status = ParticipantStatus.Out; victim.outAt = Clock; }
            if (credit != null) { credit.score += rules.wreckBonus; credit.wrecksDealt++; }
            Wrecked?.Invoke(victim, credit);
        }

        // ------------------------------------------------------------------ Points Derby

        void EndPointsDerby()
        {
            var order = new List<Participant>(participants);
            order.Sort(ComparePoints);
            Ranking.Clear(); Ranking.AddRange(order);
            var top = order[0];
            int ties = 0;
            foreach (var p in order) if (ComparePoints(p, top) == 0) ties++;
            for (int i = 0; i < order.Count; i++) order[i].placement = i + 1;
            if (ties > 1) { IsDraw = true; Winner = null; ResultText = "DRAW"; foreach (var p in order) if (ComparePoints(p, top) == 0) p.placement = 1; }
            else { Winner = top; ResultText = top.name + " WINS"; }
            SetState(MatchState.Results);
        }

        /// <summary>Score, then wrecks dealt, then fewer times wrecked. Everything equal = a tie (a draw if it is for first place).</summary>
        static int ComparePoints(Participant a, Participant b)
        {
            int c = b.score.CompareTo(a.score);
            if (c == 0) c = b.wrecksDealt.CompareTo(a.wrecksDealt);
            if (c == 0) c = a.timesWrecked.CompareTo(b.timesWrecked);
            return c;
        }

        // ------------------------------------------------------------------ Last Car Standing

        void UpdateLastCarStanding(float dt)
        {
            int active = ActiveCount;
            if (State == MatchState.Active && active == 2)
            {
                ShowdownRemaining = rules.showdownDuration;
                SetState(MatchState.FinalShowdown);
                ShowdownStarted?.Invoke();
            }
            if (State == MatchState.FinalShowdown && active == 2)
            {
                ShowdownRemaining -= dt;
                if (ShowdownRemaining <= 0f) { ShowdownRemaining = 0f; EndShowdownTimeout(); return; }
            }
            if (active <= 1)
            {
                // wait a moment so a second car wrecked by the same collision sequence still counts as simultaneous
                if (endPendingSince < 0f) endPendingSince = Clock;
                if (Clock - lastWreckTime >= rules.simultaneousWindow) EndLastCarStanding();
            }
            else endPendingSince = -1f;
        }

        void EndShowdownTimeout()
        {
            var a = participants.FindAll(p => p.IsActive);
            Decide(a, "TIME UP - HIGHEST SCORE");
        }

        void EndLastCarStanding()
        {
            var alive = participants.FindAll(p => p.IsActive);
            if (alive.Count == 1) { Decide(alive, "LAST CAR STANDING"); return; }
            // nobody left: the cars wrecked within the simultaneous window of the final wreck contest it
            float last = float.MinValue;
            foreach (var p in participants) if (p.status == ParticipantStatus.Out) last = Mathf.Max(last, p.outAt);
            var tied = participants.FindAll(p => p.status == ParticipantStatus.Out && last - p.outAt <= rules.simultaneousWindow);
            Decide(tied, tied.Count > 1 ? "SIMULTANEOUS WRECKS - HIGHEST SCORE" : "LAST CAR STANDING");
        }

        /// <summary>Winner among the candidates: the only one, else highest score, else most durability left, else a draw.</summary>
        void Decide(List<Participant> candidates, string reason)
        {
            candidates.Sort((a, b) =>
            {
                int c = b.score.CompareTo(a.score);
                if (c == 0) c = b.DurabilityFraction.CompareTo(a.DurabilityFraction);
                return c;
            });
            Participant top = candidates.Count > 0 ? candidates[0] : null;
            int ties = 0;
            foreach (var p in candidates) if (top != null && p.score == top.score && Mathf.Approximately(p.DurabilityFraction, top.DurabilityFraction)) ties++;
            BuildLcsRanking(candidates);
            if (candidates.Count > 1 && ties > 1) { IsDraw = true; Winner = null; ResultText = "DRAW"; foreach (var p in candidates) if (p.score == top.score && Mathf.Approximately(p.DurabilityFraction, top.DurabilityFraction)) p.placement = 1; }
            else { Winner = top; ResultText = (top != null ? top.name : "NOBODY") + " WINS"; }
            ResultText += "  -  " + reason;
            SetState(MatchState.Results);
        }

        void BuildLcsRanking(List<Participant> contenders)
        {
            // contenders (finalists) first in their decided order, then the others by how long they lasted
            var rest = participants.FindAll(p => !contenders.Contains(p));
            rest.Sort((a, b) =>
            {
                float ao = a.status == ParticipantStatus.Out ? a.outAt : float.MaxValue, bo = b.status == ParticipantStatus.Out ? b.outAt : float.MaxValue;
                int c = bo.CompareTo(ao);
                if (c == 0) c = b.score.CompareTo(a.score);
                return c;
            });
            Ranking.Clear(); Ranking.AddRange(contenders); Ranking.AddRange(rest);
            for (int i = 0; i < Ranking.Count; i++) Ranking[i].placement = i + 1;
        }

        /// <summary>Ends a running match at once (used when the player quits a match).</summary>
        public void Abort() { if (State != MatchState.Results) SetState(MatchState.Menu); }
    }
}
