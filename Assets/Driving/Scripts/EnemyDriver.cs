using DamageLab.CollisionTest;
using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// The first enemy: a driver that tries to ram the player. It is not a homing missile and not a new vehicle system: it only supplies the
    /// same three inputs a player has (steering, throttle/brake) to the car's own <see cref="ArcadeCarController"/>, so the enemy drives, slides,
    /// collides and deforms exactly like the player's car (its transform is never written).
    ///
    /// States:
    ///   Waiting   brief pause after (re)start so the player can orient.
    ///   Pursue    steer at where the player will be shortly (linear lead, capped) and drive at the desired speed. Eases off the throttle when the
    ///             car is pointing far away from the player so it can turn instead of running wide.
    ///   Commit    close, aligned and at speed: lock the attack direction and stop re-aiming. The player can dodge the locked line. Ends on
    ///             impact, when the player has been passed, or when the commit time is up.
    ///   Overshoot after a hit or a miss: keep rolling straight for a moment (the car "completes its movement"), then pursue again.
    ///   Recover   stuck (throttle on, hardly moving) or nose-to-nose: brake, reverse for a moment while swinging the nose towards the player, then pursue.
    /// A simple arena-edge check turns the car back towards the middle before it drives into a wall. A car that is flipped just sits there (no teleporting).
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class EnemyDriver : MonoBehaviour
    {
        public enum State { Waiting, Pursue, Commit, Overshoot, Recover }

        [Header("Tuning (the few knobs that matter)")]
        [Tooltip("Speed the enemy tries to hold (m/s). 19.4 = 70 km/h.")]
        public float desiredSpeed = 19.4f;
        [Tooltip("Seconds before the enemy starts moving after a (re)start.")]
        public float startDelay = 1.5f;
        [Tooltip("Heading error (degrees) that gives full steering lock while pursuing. Smaller = twitchier / more aggressive turning.")]
        public float fullLockAngle = 35f;
        [Tooltip("Distance (m) inside which a well-aligned enemy commits to its attack line.")]
        public float attackCommitDistance = 14f;
        [Tooltip("How far ahead (s, capped) the enemy leads a moving player. 0 = aims at where the player is now.")]
        public float leadTime = 0.6f;
        [Tooltip("How long (s) a stuck enemy reverses before pursuing again.")]
        public float recoverDuration = 1.3f;
        [Tooltip("Reaction lag (s): the enemy sees the player's velocity through a low-pass filter this slow, so a sudden swerve fools its lead for a moment. 0 = perfect tracking.")]
        public float reactionTime = 0.4f;

        [Header("Fixed behaviour")]
        public float commitAlignAngle = 25f;
        public float maxCommitTime = 2.2f;
        public float overshootTime = 0.6f;
        public float stuckSpeed = 1.5f, stuckTime = 0.9f;
        public float arenaHalf = 70f, wallMargin = 7f;

        public State Current { get; private set; } = State.Waiting;
        public int HitsLanded { get; private set; }
        public int RecoveriesTriggered { get; private set; }
        public int AttacksCommitted { get; private set; }
        public float DistanceToPlayer { get; private set; }
        public Rigidbody player;
        public CollisionDamageReceiver receiver;

        ArcadeCarController controller;
        Rigidbody body;
        float stateTime, stuckTimer, commitTimer, flippedTimer;
        Vector3 commitDir;
        Vector3 seenVelocity;

        void Awake()
        {
            controller = GetComponent<ArcadeCarController>();
            body = GetComponent<Rigidbody>();
        }

        void OnDestroy()
        {
            if (receiver != null) receiver.ImpactRegistered -= OnImpact;
        }

        public void Begin(float speedMetresPerSecond)
        {
            if (controller == null) Awake();
            desiredSpeed = speedMetresPerSecond;
            if (receiver != null) { receiver.ImpactRegistered -= OnImpact; receiver.ImpactRegistered += OnImpact; }
            Restart();
        }

        /// <summary>Back to the start state (the game places the car first).</summary>
        public void Restart()
        {
            if (controller == null) Awake();
            Enter(State.Waiting);
            seenVelocity = player != null ? player.linearVelocity : Vector3.zero;
            stuckTimer = flippedTimer = 0f;
            HitsLanded = RecoveriesTriggered = AttacksCommitted = 0;
            controller.ResetState();
            controller.Throttle = 0f; controller.SteerInput = 0f;
        }

        void OnImpact(CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
            HitsLanded++;
            if (Current == State.Commit || Current == State.Pursue) Enter(State.Overshoot);
        }

        void Enter(State s) { Current = s; stateTime = 0f; }

        void Update() => Drive(Time.deltaTime);

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>One decision (public so the editor validation, which has no Update, can step it).</summary>
        public void Drive(float dt)
        {
            if (controller == null) Awake();
            if (player == null || dt <= 0f) return;
            stateTime += dt;
            seenVelocity = Vector3.Lerp(seenVelocity, player.linearVelocity, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, reactionTime)));   // imperfect eyes

            Vector3 pos = body.position;
            Vector3 fwd = Flat(transform.forward).normalized;
            float fwdSpeed = controller.ForwardSpeed;
            Vector3 toPlayer = Flat(player.position - pos);
            float dist = toPlayer.magnitude;
            DistanceToPlayer = dist;

            // A flipped car cannot drive: it sits (no teleporting out of it).
            flippedTimer = controller.IsFlipped ? flippedTimer + dt : 0f;
            if (flippedTimer > 0.5f) { controller.Throttle = 0f; controller.SteerInput = 0f; return; }

            float throttle = 0f, steer = 0f;
            switch (Current)
            {
                case State.Waiting:
                    if (stateTime >= startDelay) Enter(State.Pursue);
                    break;

                case State.Pursue:
                {
                    // Where the player will be shortly (capped linear lead), so a moving player is led, not chased from behind.
                    float lead = Mathf.Min(leadTime, dist / Mathf.Max(8f, Mathf.Abs(fwdSpeed)));
                    Vector3 aim = Flat(player.position + seenVelocity * lead) - pos;
                    bool wall = WallAhead(pos, fwd, fwdSpeed, out Vector3 toCentre);
                    if (wall) aim = toCentre;
                    float angle = Vector3.SignedAngle(fwd, aim, Vector3.up);
                    steer = Mathf.Clamp(angle / fullLockAngle, -1f, 1f);
                    float absAngle = Mathf.Abs(angle);
                    throttle = Mathf.Clamp01(desiredSpeed - fwdSpeed);
                    if (absAngle > 70f) throttle = fwdSpeed > 9f ? 0f : 0.55f;      // pointing well away: slow down / creep so it can swing round
                    else if (absAngle > 40f) throttle = Mathf.Min(throttle, 0.7f);
                    if (dist < 10f) { steer *= 0.7f; throttle = Mathf.Min(throttle, 0.8f); }  // very close: no frantic circling
                    if (wall) throttle = Mathf.Min(throttle, 0.6f);

                    if (!wall && dist < attackCommitDistance && absAngle < commitAlignAngle && fwdSpeed > 7f)
                    {
                        commitDir = aim.normalized;                                  // lock the attack line: the player can still dodge it
                        commitTimer = Mathf.Min(maxCommitTime, dist / Mathf.Max(fwdSpeed, 8f) + 0.7f);
                        AttacksCommitted++;
                        Enter(State.Commit);
                    }
                    break;
                }

                case State.Commit:
                {
                    commitTimer -= dt;
                    float angle = Vector3.SignedAngle(fwd, commitDir, Vector3.up);
                    steer = Mathf.Clamp(angle / (fullLockAngle * 1.5f), -1f, 1f) * 0.35f;  // hold the locked line, small corrections only
                    throttle = Mathf.Clamp01(desiredSpeed * 1.15f - fwdSpeed);
                    bool passed = Vector3.Dot(fwd, toPlayer) < 0f && dist > 3f;
                    if (commitTimer <= 0f || passed) Enter(State.Overshoot);
                    break;
                }

                case State.Overshoot:
                {
                    // Missed or just hit: keep rolling roughly straight for a moment, steer back from walls, then turn round and pursue.
                    bool wall = WallAhead(pos, fwd, fwdSpeed, out Vector3 toCentre);
                    steer = wall ? Mathf.Clamp(Vector3.SignedAngle(fwd, toCentre, Vector3.up) / fullLockAngle, -1f, 1f) : 0f;
                    throttle = wall ? 0f : Mathf.Clamp01(0.5f * desiredSpeed - fwdSpeed);
                    if (stateTime >= overshootTime) Enter(State.Pursue);
                    break;
                }

                case State.Recover:
                {
                    // Back out while swinging the nose towards the player (reversing mirrors the steering), then pursue again.
                    float angle = Vector3.SignedAngle(fwd, toPlayer, Vector3.up);
                    steer = angle >= 0f ? -1f : 1f;
                    throttle = -1f;
                    if (stateTime >= recoverDuration) { stuckTimer = 0f; Enter(State.Pursue); }
                    break;
                }
            }

            // Stuck: trying to drive forward but hardly moving (wall, obstacle, nose-to-nose with the player).
            bool tryingToDrive = Current != State.Waiting && Current != State.Recover && throttle > 0.3f;
            if (tryingToDrive && body.linearVelocity.magnitude < stuckSpeed) stuckTimer += dt;
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);
            if (stuckTimer > stuckTime && Current != State.Recover)
            {
                RecoveriesTriggered++;
                stuckTimer = 0f;
                Enter(State.Recover);
                throttle = -1f;
            }

            controller.Throttle = throttle;
            controller.SteerInput = steer;
        }

        /// <summary>True when the car, a moment from now at its current speed, would be too close to the arena edge; also returns the direction to the middle.</summary>
        bool WallAhead(Vector3 pos, Vector3 fwd, float fwdSpeed, out Vector3 toCentre)
        {
            Vector3 ahead = pos + fwd * Mathf.Max(6f, fwdSpeed * 0.9f);
            float limit = arenaHalf - wallMargin;
            toCentre = Flat(-pos);
            return fwdSpeed > 0f && (Mathf.Abs(ahead.x) > limit || Mathf.Abs(ahead.z) > limit);
        }
    }
}
