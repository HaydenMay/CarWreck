using System.Collections.Generic;
using CarWreck.Driving;
using UnityEngine;

namespace CarWreck.Derby
{
    /// <summary>
    /// Chooses which opponent each AI driver goes after. It replaces the arena's fixed "enemy pursues the player" by feeding each <see cref="EnemyDriver"/> a target body
    /// (the driver's combat state machine is untouched).
    ///
    /// Who is eligible: every other participant that is active and a valid opponent under the match's <see cref="IOpponentRule"/>. Nothing here reads
    /// <see cref="Participant.control"/>: the human car is scored like any AI car, so the player gets no special priority (and no protection).
    ///
    /// How a choice is made: each candidate gets a score from a few terms, weighted by the driver's profile (aggression, skill):
    ///   near      closer is better; an aggressive driver weights it most (brawlers go for whoever is close)
    ///   weak      the opponent's lost durability; skilled and careful drivers weight it most (hunters go for the exposed)
    ///   ahead     how easy the interception is: facing it already and closing on it (skill)
    ///   head-on   the opponent is driving straight at us: a penalty that a cautious driver feels and a reckless one barely does
    ///   crowd     other AI already attacking it: a small spread penalty (dogpiles are allowed, just not the default)
    ///   revenge   a small bonus for whoever hit this driver in the last few seconds, fading away (no permanent rivalries)
    ///   noise     a slowly changing, per-pair pseudo-random term, so identical situations do not make every driver choose alike
    /// Stability: the current target gets a commitment bonus and a minimum hold time (a hard-hit target also releases the hold, a landed hit ends an engagement);
    /// the driver switches early only for a clearly better opportunity. A target that cannot be reached (no progress for several seconds) is dropped for a while.
    /// The work per decision is one pass over the other cars; each driver decides every ~0.35 s (staggered), never per frame.
    /// </summary>
    public class AiTargeting
    {
        public class Record
        {
            public int targetId = -1;
            public float holdUntil, nextEval, refDist, refTime;
            public int lastHitsLanded;
            public readonly Dictionary<int, float> blockedUntil = new Dictionary<int, float>();
            public int switches;
        }

        public float evalInterval = 0.35f;
        public float unreachableTime = 7f, unreachableProgress = 2.5f, blockTime = 6f;
        public IOpponentRule rule = FreeForAllRule.Instance;
        public int Switches { get; private set; }
        public int Evaluations { get; private set; }

        readonly Dictionary<int, Record> records = new Dictionary<int, Record>();
        readonly Dictionary<int, int> attackers = new Dictionary<int, int>();

        public Record RecordOf(int id) { if (!records.TryGetValue(id, out var r)) records[id] = r = new Record(); return r; }
        public int TargetOf(int id) => records.TryGetValue(id, out var r) ? r.targetId : -1;

        public void Reset() { records.Clear(); attackers.Clear(); Switches = 0; Evaluations = 0; }

        /// <summary>A target is gone (wrecked): everyone who was after it chooses again at once.</summary>
        public void TargetLost(int id)
        {
            foreach (var r in records.Values) if (r.targetId == id) { r.holdUntil = 0f; r.nextEval = 0f; }
        }

        /// <summary>Re-evaluates every driver whose turn has come. <paramref name="now"/> is the match clock.</summary>
        public void Update(DerbyMatch match, float now)
        {
            attackers.Clear();
            foreach (var kv in records) if (kv.Value.targetId >= 0) { attackers.TryGetValue(kv.Value.targetId, out int n); attackers[kv.Value.targetId] = n + 1; }
            for (int i = 0; i < match.participants.Count; i++)
            {
                var self = match.participants[i];
                var car = self.car;
                if (car == null || car.enemy == null) continue;
                var rec = RecordOf(self.id);
                if (!self.IsActive) { rec.targetId = -1; rec.nextEval = 0f; continue; }
                var tgt = rec.targetId >= 0 ? match.Find(rec.targetId) : null;
                bool invalid = tgt == null || !tgt.IsActive || !rule.IsOpponent(self, tgt) || tgt.car == null;
                if (!invalid)
                {
                    TrackProgress(self, rec, tgt, now);
                    if (car.enemy.HitsLanded != rec.lastHitsLanded)
                    {
                        // an engagement just ended with a hit (or the driver was restarted): the hold is released soon, the driver may look around
                        if (car.enemy.HitsLanded > rec.lastHitsLanded) rec.holdUntil = Mathf.Min(rec.holdUntil, now + 0.5f);
                        rec.lastHitsLanded = car.enemy.HitsLanded;
                    }
                }
                if (!invalid && now < rec.nextEval) continue;
                rec.nextEval = now + evalInterval * (0.8f + 0.4f * Hash01(self.id, 17, (int)(now * 3f)));
                Evaluations++;
                Choose(match, self, rec, now, invalid);
            }
        }

        void TrackProgress(Participant self, Record rec, Participant tgt, float now)
        {
            var e = self.car.enemy;
            float d = e.DistanceToPlayer;
            bool fighting = e.Current == EnemyDriver.State.Commit || e.Current == EnemyDriver.State.Overshoot || e.Current == EnemyDriver.State.Reposition || e.Current == EnemyDriver.State.Recover || e.Current == EnemyDriver.State.Waiting;
            if (fighting || d < 9f || d < rec.refDist - unreachableProgress || rec.refTime == 0f) { rec.refDist = d; rec.refTime = now; return; }
            if (now - rec.refTime > unreachableTime)
            {
                rec.blockedUntil[tgt.id] = now + blockTime;     // no progress for a long time: pick someone else for a while
                rec.holdUntil = 0f; rec.nextEval = 0f; rec.refTime = now; rec.refDist = d;
            }
        }

        void Choose(DerbyMatch match, Participant self, Record rec, float now, bool mustChange)
        {
            var p = Tuning.For(self.profile);
            Participant best = null; float bestScore = float.NegativeInfinity, currentScore = float.NegativeInfinity;
            for (int i = 0; i < match.participants.Count; i++)
            {
                var c = match.participants[i];
                if (c == self || !c.IsActive || c.car == null || !rule.IsOpponent(self, c)) continue;
                if (rec.blockedUntil.TryGetValue(c.id, out float until) && now < until) continue;
                float s = Score(self, c, p, now, attackers);
                bool isCurrent = c.id == rec.targetId;
                if (isCurrent) s += p.commitBonus;
                if (s > bestScore) { bestScore = s; best = c; }
                if (isCurrent) currentScore = s;
            }
            if (best == null)
            {
                if (rec.targetId != -1) { rec.targetId = -1; self.car.enemy.SetTarget(null); }
                return;
            }
            bool keep = !mustChange && rec.targetId >= 0 && currentScore > float.NegativeInfinity;
            if (keep)
            {
                // the commitment bonus is already inside currentScore; inside the hold time only a much better opportunity wins
                float margin = now < rec.holdUntil ? p.commitBonus * 1.5f : 0f;
                if (best.id == rec.targetId || bestScore <= currentScore + margin) return;
            }
            if (best.id != rec.targetId)
            {
                rec.targetId = best.id;
                rec.holdUntil = now + p.holdTime;
                rec.refDist = self.car.enemy.DistanceToPlayer; rec.refTime = 0f;
                rec.lastHitsLanded = 0;
                rec.switches++; Switches++;
                self.car.enemy.SetTarget(best.car.Body);
                rec.lastHitsLanded = self.car.enemy.HitsLanded;
            }
        }

        /// <summary>The weights a driver's profile turns into (read-only numbers, tuned here).</summary>
        public struct Tuning
        {
            public float near, weak, ahead, headOn, crowd, revenge, noise, commitBonus, holdTime;
            public static Tuning For(DriverProfile profile)
            {
                float a = profile != null ? Mathf.Clamp01(profile.aggression) : 0.5f, s = profile != null ? Mathf.Clamp01(profile.skill) : 0.5f;
                return new Tuning
                {
                    near = Mathf.Lerp(0.6f, 1.5f, a),                       // brawlers: whoever is close
                    weak = 0.1f + 0.8f * s + 0.25f * (1f - a),              // hunters (then cautious drivers): the wounded
                    ahead = 0.3f + 0.5f * s,                                 // skilled drivers: easy interceptions
                    headOn = Mathf.Lerp(0.8f, 0f, a),                       // cautious drivers avoid a car coming straight at them
                    crowd = 0.2f,
                    revenge = 0.1f + 0.3f * a,
                    noise = 0.15f,
                    commitBonus = Mathf.Lerp(0.30f, 0.12f, a) + 0.15f * s, // hunters and cautious drivers stick to a target, brawlers drop it for a better one
                    holdTime = Mathf.Lerp(5f, 2.5f, a),
                };
            }
        }

        /// <summary>The score of <paramref name="c"/> as a target for <paramref name="self"/> (higher = better). Public for the validation.</summary>
        public static float Score(Participant self, Participant c, Tuning t, float now, Dictionary<int, int> attackerCounts = null)
        {
            Rigidbody sb = self.car.Body, cb = c.car.Body;
            Vector3 to = cb.position - sb.position; to.y = 0f;
            float d = Mathf.Max(0.5f, to.magnitude);
            Vector3 dir = to / d;
            Vector3 fs = sb.transform.forward; fs.y = 0f; fs.Normalize();
            Vector3 fc = cb.transform.forward; fc.y = 0f; fc.Normalize();
            float prox = 1f / (1f + d / 20f);
            float exposed = 1f - c.DurabilityFraction;
            float facing = (Vector3.Dot(fs, dir) + 1f) * 0.5f;
            float closing = Mathf.Clamp01(Vector3.Dot(sb.linearVelocity - cb.linearVelocity, dir) / 15f);
            float headOn = Mathf.Max(0f, Vector3.Dot(fc, -dir)) * Mathf.Clamp01(Vector3.Dot(cb.linearVelocity, -dir) / 8f);
            int others = 0;
            if (attackerCounts != null && attackerCounts.TryGetValue(c.id, out int n)) others = n - (TargetIs(self, c) ? 1 : 0);
            float age = now - self.lastAttackedAt;
            float revenge = self.lastAttackerId == c.id && age >= 0f && age < 6f ? 1f - age / 6f : 0f;
            float noise = Hash01(self.id, c.id, (int)(now / 4f));
            return t.near * prox + t.weak * exposed + t.ahead * (facing * 0.6f + closing * 0.4f) - t.headOn * headOn - t.crowd * Mathf.Min(2, Mathf.Max(0, others)) + t.revenge * revenge + t.noise * noise;
        }

        static bool TargetIs(Participant self, Participant c) => self.car != null && self.car.enemy != null && self.car.enemy.Target == c.car.Body;

        /// <summary>Stable pseudo-random 0..1 from three integers (no System.Random state, same result everywhere).</summary>
        public static float Hash01(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 374761393u + (uint)b * 668265263u + (uint)c * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }
    }
}
