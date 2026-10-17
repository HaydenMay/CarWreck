using DamageLab.CollisionTest;
using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// The enemy: a driver that tries to ram the player with force. It only supplies the same three inputs a player has (steering, throttle/brake) to the
    /// car's own <see cref="ArcadeCarController"/>, so it drives, slides, collides and deforms exactly like the player's car (its transform is never written).
    ///
    /// Enemy AI V2 keeps V1's driving, wall check and stuck recovery and adds a small combat loop on top:
    ///   Waiting     brief pause after (re)start.
    ///   Pursue      far away or the straight line is blocked: steer at where the player will be (detouring round obstacles), drive at the speed setting.
    ///   LineUp      within range with a clear line: slow down a little, turn the nose onto the target and build speed (the visible "I am coming for you").
    ///   Reposition  too close for a strong hit (e.g. tailgating at low speed): brake / back off and swing wide to make a run-up, then LineUp.
    ///   Commit      the charge: heads for the locked line at full throttle with slow course corrections (dodgeable); ends on impact, a miss or the time limit.
    ///   Overshoot   after a hit or a miss: keeps rolling roughly straight (avoiding walls and obstacles), then Pursue turns it round.
    ///   Recover     stuck (throttle on, hardly moving): reverses while swinging the nose to the player (unchanged from V1; the same for every skill).
    /// A <see cref="DriverProfile"/> (aggression, skill) decides how readily it attacks and how well it drives; the profile numbers are turned into the
    /// working values in <see cref="ApplyProfile"/>. No NavMesh: obstacles are found with a few sphere casts and a one- or two-waypoint detour round the obstacle's outline.
    /// All state is per driver (no statics), so several enemies can coexist.
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class EnemyDriver : MonoBehaviour
    {
        /// <summary>Centre-to-centre distance (m) at which two cars touch nose to nose, about.</summary>
        const float ContactDistance = 4.8f;

        public enum State { Waiting, Pursue, LineUp, Reposition, Commit, Overshoot, Recover, Breakaway }

        [Header("Driver")]
        [Tooltip("Aggression and skill. Null = a middling driver (0.5 / 0.5).")]
        public DriverProfile profile;
        [Tooltip("Seed of the driver's few random choices (whether to back off). Restart() reseeds it, so a reset reproduces the same behaviour.")]
        public int seed = 1;

        [Header("Basics")]
        [Tooltip("Speed the enemy tries to hold (m/s). 19.4 = 70 km/h.")]
        public float desiredSpeed = 19.4f;
        [Tooltip("Seconds before the enemy starts moving after a (re)start.")]
        public float startDelay = 1.5f;
        [Tooltip("How long (s) a stuck enemy reverses before pursuing again.")]
        public float recoverDuration = 1.3f;

        [Header("Obstacles")]
        [Tooltip("Look for obstacles ahead and between the enemy and the player, and route round them. Off = V1 behaviour (only the stuck recovery).")]
        public bool obstacleAwareness = true;
        public LayerMask obstacleMask = ~0;
        [Tooltip("Dynamic bodies at least this heavy (kg) count as obstacles (heavy blocks); lighter ones (crates) are driven through. Kinematic bodies always count.")]
        public float obstacleMinMass = 300f;
        [Tooltip("Extra room (m) left beside an obstacle's outline when detouring.")]
        public float detourClearance = 2.6f;

        [Header("Fixed behaviour")]
        public float maxCommitTime = 2.4f;
        public float minCommitTime = 0.8f;
        public float stuckSpeed = 1.5f, stuckTime = 0.9f;
        public float arenaHalf = 70f, wallMargin = 7f;
        [Tooltip("Longest time (s) a reposition lasts, however far the player is: the enemy never backs away for long.")]
        public float repositionMaxTime = 2.2f;
        [Tooltip("Longest time (s) a LineUp lasts before the enemy falls back to Pursue.")]
        public float lineUpMaxTime = 5f;
        [Tooltip("Planning / probing period (s): obstacle casts and attack evaluation run this often, not every frame.")]
        public float decisionInterval = 0.12f;

        // ------------------------------------------------------------------ values derived from the profile (see ApplyProfile)
        [Header("Derived from the profile (read-only; tune the formulas in ApplyProfile)")]
        public float aggression = 0.5f;
        public float skill = 0.5f;
        public float reactionTime, leadMax, predictionQuality, fullLockAngle, chargeSteerLimit, believedTurnRate;
        public float alignTolerance, minChargeSpeed, chargeRange, minImpactSpeed, lineUpRange, minRunway, repositionChance, repositionCooldown, engageTimeout, chargeSpeedFactor, overshootTime, lookAheadScale, opportunityThreshold;

        // ------------------------------------------------------------------ breakaway (derby): leave a cluster that produces poor attacks, build a run-up elsewhere, come back
        [Header("Breakaway (needs the game to supply nearbyCars; off in the driving arena)")]
        public bool breakawayEnabled = true;
        [Tooltip("Positions of the other living cars within a radius of this body (fills the array, returns the count). Null = no breakaway.")]
        public System.Func<Rigidbody, Vector3[], float, int> nearbyCars;
        [Tooltip("Lets the game cap how many drivers break away at the same time.")]
        public System.Func<EnemyDriver, bool> breakawayAllowed;
        [Tooltip("True when another driver is already heading for (about) this point.")]
        public System.Func<Vector3, EnemyDriver, bool> destinationTaken;
        // derived from the profile (ApplyProfile)
        public float breakawayThreshold, breakawayChance, breakawayCooldown, breakawayMaxTime, breakawayDistance, breakawayCandidates, breakawayNoise;
        public int Breakaways { get; private set; }
        public int BreakawaysCompleted { get; private set; }
        public int BreakawaysAborted { get; private set; }
        /// <summary>Why breakaway checks that reached the pressure threshold did not start one (diagnostics): no crowd, the chance roll, the game cap, no usable destination.</summary>
        public int RejectedCrowd { get; private set; }
        public int RejectedChance { get; private set; }
        public int RejectedCap { get; private set; }
        public int RejectedNoDestination { get; private set; }
        public int AbortTimeout { get; private set; }
        public int AbortProgress { get; private set; }
        public int AbortRecover { get; private set; }
        /// <summary>How unproductive the recent fighting was (back-offs, recoveries and weak hits add, strong hits and time take away). A breakaway needs it above the profile's threshold.</summary>
        public float Pressure => pressure;
        public Vector3 BreakawayDestination => breakawayDest;
        public State Current { get; private set; } = State.Waiting;
        /// <summary>Hits that damaged the enemy while it was hitting (or being hit by) the player.</summary>
        public int HitsLanded { get; private set; }
        /// <summary>Damaging hits against anything that is not the player (walls, blocks, barriers).</summary>
        public int ObstacleHits { get; private set; }
        public int RecoveriesTriggered { get; private set; }
        public int AttacksCommitted { get; private set; }
        public int Repositions { get; private set; }
        public int DetoursPlanned { get; private set; }
        public int StateChanges { get; private set; }
        /// <summary>Own forward speed (m/s) at the moment the latest charge began, and the closing speed (m/s) at the latest hit on the player.</summary>
        public float LastChargeSpeed { get; private set; }
        public float LastHitClosingSpeed { get; private set; }
        public float DistanceToPlayer { get; private set; }
        public bool Detouring => wpIndex < wpCount;
        public string ProfileName => profile != null ? profile.displayName : "Default";
        /// <summary>The body the driver goes after (the player in the arena; any eligible opponent in a derby). Change it with <see cref="SetTarget"/> at run time.</summary>
        public Rigidbody player;
        public CollisionDamageReceiver receiver;
        /// <summary>Optional: which car bodies count as obstacles (a wrecked hulk does, a living opponent does not). Null = no car is an obstacle (the arena behaviour).</summary>
        public System.Predicate<Rigidbody> carIsObstacle;

        ArcadeCarController controller;
        Rigidbody body;
        System.Random rng;
        float clock, stateTime, stuckTimer, commitTimer, flippedTimer, decisionTimer;
        float lastAttackClock, repositionBlockedUntil, closeTimer, planTimer, detourAge;
        float circleTimer, circleBlockedUntil;
        float throttleOut;
        bool repositionForCircling;
        bool avoidBraking, rearBlocked;
        float pressure, breakawayBlockedUntil, breakawayStarted, breakawayProgressTimer, breakawayBestDist, breakawayEvalUntil;
        int failedBreakaways;
        Vector3 breakawayDest;
        readonly Vector3[] near = new Vector3[16];
        Vector3 seenVelocity;
        bool repositionBackOff;
        float repositionSide;
        Vector3 repositionPoint;
        // cached probe results (refreshed every decisionInterval)
        bool clearToTarget = true, forwardBlocked;
        float forwardHitDistance = float.PositiveInfinity;
        float avoidSteer;
        // detour
        readonly Vector3[] wp = new Vector3[2];
        int wpCount, wpIndex;
        float detourSide;
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly Collider[] overlaps = new Collider[32];

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

        public Rigidbody Target => player;

        /// <summary>
        /// Go after another body. Safe at any time: the target's velocity estimate restarts from the new body, the detour is dropped and an attack that was aimed at the old
        /// target (line-up, back-off, charge) is given up; the car keeps its speed and heading and simply pursues the new one. Null = nothing to attack (the car coasts).
        /// </summary>
        public void SetTarget(Rigidbody t)
        {
            if (t == player) return;
            player = t;
            if (t != null) seenVelocity = t.linearVelocity;
            ClearDetour();
            planTimer = closeTimer = circleTimer = 0f;
            if (Current == State.Commit || Current == State.LineUp || Current == State.Reposition) Enter(State.Pursue);
        }

        /// <summary>Use a profile (null = default 0.5 / 0.5). Safe at any time; takes effect immediately.</summary>
        public void SetProfile(DriverProfile p)
        {
            profile = p;
            ApplyProfile();
        }

        /// <summary>
        /// Turns aggression (a) and skill (s), both 0..1, into the working numbers. Aggression decides willingness: how good an opportunity must be, how short a
        /// run-up is accepted, how often it backs off and for how long it waits before it stops being picky. Skill decides execution: reaction lag, prediction,
        /// steering precision, how quickly a charge is corrected, how long it takes to turn round after a miss and how far ahead it sees obstacles.
        /// Recovery and the stuck detector do not depend on either.
        /// </summary>
        public void ApplyProfile()
        {
            aggression = profile != null ? Mathf.Clamp01(profile.aggression) : 0.5f;
            skill = profile != null ? Mathf.Clamp01(profile.skill) : 0.5f;
            float a = aggression, s = skill;
            // execution (skill)
            reactionTime = Mathf.Lerp(0.6f, 0.25f, s);
            predictionQuality = Mathf.Lerp(0.4f, 1f, s);       // fraction of the ideal lead actually applied
            leadMax = 1.6f;                                      // s, before predictionQuality: about the time a charge takes to arrive
            fullLockAngle = Mathf.Lerp(45f, 28f, s);             // degrees of heading error for full lock: lower = sharper, more precise steering
            chargeSteerLimit = Mathf.Lerp(0.45f, 0.7f, s);       // share of full steering lock a committed charge may use (corrections during the run-in; never a lock-on)
            overshootTime = Mathf.Lerp(1.0f, 0.55f, s);          // s rolling straight after a hit or miss before turning round
            believedTurnRate = 0.55f * Mathf.Lerp(1.45f, 1f, s);   // rad/s the driver thinks the car can turn at speed (the car does about 0.55)
            lookAheadScale = Mathf.Lerp(0.75f, 1.25f, s);        // obstacle probe length
            // willingness (aggression)
            alignTolerance = Mathf.Lerp(12f, 32f, a) * Mathf.Lerp(1f, 0.55f, s);   // degrees off the line at which a charge may start: eager drivers accept more, skilled ones line up properly first
            minChargeSpeed = Mathf.Lerp(7f, 4f, a);              // m/s own speed needed to start a charge
            minRunway = Mathf.Lerp(13f, 6f, a) + 3f * s;                  // shortest run-up (m) accepted without backing off
            minImpactSpeed = Mathf.Lerp(10f, 6f, a) + 3f * s;              // m/s expected closing speed below which an attack is not worth it (unless impatient)
            chargeRange = Mathf.Lerp(20f, 26f, a);               // charges start at most this far from the player
            lineUpRange = chargeRange + 20f;
            repositionChance = Mathf.Lerp(0.95f, 0.3f, a);       // chance to back off when too close for a strong hit
            repositionCooldown = Mathf.Lerp(2f, 5.5f, a);        // s before it will back off again
            engageTimeout = Mathf.Lerp(6f, 3f, a);            // s without an attack after which it stops being picky
            chargeSpeedFactor = Mathf.Lerp(1.0f, 1.2f, a);       // charge speed relative to the speed setting
            opportunityThreshold = Mathf.Lerp(0.6f, 0.15f, a);   // quality (0..1) an attack must reach
            // breakaway: how much unproductive fighting a driver tolerates (aggressive drivers a lot, careful ones little; skill notices sooner), how often it
            // acts on it, how long it may take, how far it goes, how many places it weighs and how sloppy the choice is
            breakawayThreshold = Mathf.Lerp(1.6f, 3.4f, a) - 0.6f * s;   // Cautious 1.75, Hunter 2.2, Rookie 2.5, Brawler 2.8 units of pressure
            breakawayChance = Mathf.Clamp01(Mathf.Lerp(0.95f, 0.5f, a) + 0.1f * s);
            breakawayCooldown = Mathf.Lerp(14f, 10f, a);
            breakawayMaxTime = Mathf.Lerp(8f, 6f, a);
            breakawayDistance = chargeRange + 6f;
            breakawayCandidates = Mathf.Round(Mathf.Lerp(6f, 12f, s));
            breakawayNoise = Mathf.Lerp(0.6f, 0.05f, s);
        }

        /// <summary>Back to the start state (the game places the car first). Clears every piece of combat state.</summary>
        public void Restart()
        {
            if (controller == null) Awake();
            ApplyProfile();
            rng = new System.Random(seed);
            Enter(State.Waiting);
            StateChanges = 0;
            seenVelocity = player != null ? player.linearVelocity : Vector3.zero;
            circleTimer = circleBlockedUntil = 0f;
            throttleOut = 0f;
            WorstDriveMilliseconds = 0f;
            stuckTimer = flippedTimer = decisionTimer = closeTimer = planTimer = detourAge = 0f;
            clock = 0f; lastAttackClock = 0f; repositionBlockedUntil = 0f;
            HitsLanded = ObstacleHits = RecoveriesTriggered = AttacksCommitted = Repositions = DetoursPlanned = 0;
            Breakaways = BreakawaysCompleted = BreakawaysAborted = failedBreakaways = RejectedCrowd = RejectedChance = RejectedCap = RejectedNoDestination = 0; histCount = histHead = 0; histTimer = 0f; Milling = false; pressure = 0f; breakawayBlockedUntil = 0f; breakawayEvalUntil = 0f;
            LastChargeSpeed = LastHitClosingSpeed = 0f;
            ClearDetour();
            clearToTarget = true; forwardBlocked = false; forwardHitDistance = float.PositiveInfinity; avoidSteer = 0f;
            controller.ResetState();
            controller.Throttle = 0f; controller.SteerInput = 0f;
        }

        void OnImpact(CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
            bool withPlayer = player != null && r.other == player.name;
            if (withPlayer) { HitsLanded++; LastHitClosingSpeed = r.closingSpeed; lastAttackClock = clock; }
            else ObstacleHits++;
            // fighting that goes nowhere builds pressure (weak taps), a real hit relieves it
            if (r.closingSpeed < 5f) AddPressure(0.7f);
            else if (r.closingSpeed >= Mathf.Max(7f, minImpactSpeed)) pressure = Mathf.Max(0f, pressure - Mathf.Lerp(0.5f, 1.2f, aggression));   // a real hit is progress, most of all to an aggressive driver
            if (Current == State.Commit || Current == State.Pursue || Current == State.LineUp) Enter(State.Overshoot);
        }

        void Enter(State s) { if (s != Current) StateChanges++; Current = s; stateTime = 0f; }

        /// <summary>Time the decisions (page report only): the slowest Drive call since the last Restart, in ms.</summary>
        public bool measureCost;
        public float WorstDriveMilliseconds { get; private set; }
        /// <summary>Slowest choice of a breakaway destination (ms); only measured when measureCost is on.</summary>
        public float WorstBreakawayMilliseconds { get; private set; }
        readonly System.Diagnostics.Stopwatch costWatch = new System.Diagnostics.Stopwatch();
        readonly System.Diagnostics.Stopwatch pickWatch = new System.Diagnostics.Stopwatch();

        void Update() => Drive(Time.deltaTime);

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ------------------------------------------------------------------ probes (sphere casts; no allocation)

        bool IsObstacle(Collider c)
        {
            var rb = c.attachedRigidbody;
            if (rb == null || rb == body) return false;                                    // ground and other static scenery: not an obstacle
            if (rb.TryGetComponent<ArcadeCarController>(out _) || rb.TryGetComponent<CollisionDamageReceiver>(out _)) return carIsObstacle != null && carIsObstacle(rb);   // cars are targets, not obstacles (unless the game says this one is a dead hulk)
            return rb.isKinematic || rb.mass >= obstacleMinMass;
        }

        /// <summary>Nearest obstacle on a sphere cast from <paramref name="from"/> along the flat direction.</summary>
        bool Cast(Vector3 from, Vector3 dir, float length, float radius, out RaycastHit best)
        {
            best = default;
            if (length <= 0.01f) return false;
            Vector3 origin = new Vector3(from.x, body.position.y + 0.6f, from.z);
            int n = Physics.SphereCastNonAlloc(origin, radius, dir, hits, length, obstacleMask, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (!IsObstacle(hits[i].collider)) continue;
                if (hits[i].distance < bd) { bd = hits[i].distance; best = hits[i]; found = true; }
            }
            return found;
        }

        bool Clear(Vector3 a, Vector3 b, float radius)
        {
            Vector3 d = Flat(b - a); float len = d.magnitude;
            if (len < 0.5f) return true;
            return !Cast(a, d / len, len, radius, out _);
        }

        /// <summary>Distance (m) the car needs to stop from its current speed (plus a margin that grows with lower skill).</summary>
        float StopDistance(float fwdSpeed)
        {
            float v = Mathf.Max(0f, fwdSpeed);
            return v * v / (2f * 22f) + Mathf.Lerp(4.5f, 2.5f, skill);
        }

        /// <summary>Refresh the cached probes: line to the target, the way straight ahead, and the side to steer to if something is in the way.</summary>
        void Probe(Vector3 pos, Vector3 fwd, float fwdSpeed, Vector3 aimPoint)
        {
            float look = Mathf.Clamp(Mathf.Abs(fwdSpeed) * 0.9f + 6f, 8f, 30f) * lookAheadScale;
            forwardBlocked = false; forwardHitDistance = float.PositiveInfinity; avoidSteer = 0f;
            if (fwdSpeed > -0.5f && Cast(pos, fwd, look, 1.0f, out var h))
            {
                forwardBlocked = true; forwardHitDistance = h.distance;
                // steer towards the freer side (probes at +/-35 degrees)
                Vector3 l = Quaternion.Euler(0f, -35f, 0f) * fwd, r = Quaternion.Euler(0f, 35f, 0f) * fwd;
                float dl = Cast(pos, l, look, 1.0f, out var hl) ? hl.distance : look + 1f;
                float dr = Cast(pos, r, look, 1.0f, out var hr) ? hr.distance : look + 1f;
                avoidSteer = dr >= dl ? 1f : -1f;
            }
            clearToTarget = !obstacleAwareness || Clear(pos, aimPoint, 1.0f);
        }

        // ------------------------------------------------------------------ detours (one or two waypoints round an obstacle cluster's outline)

        void ClearDetour() { wpCount = wpIndex = 0; detourAge = 0f; }

        /// <summary>Plan waypoints that take the car round whatever blocks the line to <paramref name="target"/>. False when nothing blocks it or no route was found.</summary>
        bool PlanDetour(Vector3 pos, Vector3 target)
        {
            Vector3 d = Flat(target - pos); float dist = d.magnitude;
            if (dist < 3f || !Cast(pos, d / dist, dist, 1.0f, out var hit)) return false;
            // The obstacle's outline: the blocking collider plus whatever touches it (a wall with wings is one obstacle), as a flat box.
            Bounds cb = hit.collider.bounds;
            for (int pass = 0; pass < 3; pass++)
            {
                int n = Physics.OverlapBoxNonAlloc(cb.center, cb.extents + Vector3.one * 3f, overlaps, Quaternion.identity, obstacleMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (IsObstacle(overlaps[i]) && overlaps[i].bounds.size.magnitude < 40f) cb.Encapsulate(overlaps[i].bounds);
            }
            float m = detourClearance + 1f;
            float x0 = cb.min.x - m, x1 = cb.max.x + m, z0 = cb.min.z - m, z1 = cb.max.z + m;
            float lim = arenaHalf - 4f;
            var corners = new Vector3[]
            {
                new Vector3(Mathf.Clamp(x0, -lim, lim), pos.y, Mathf.Clamp(z0, -lim, lim)), new Vector3(Mathf.Clamp(x1, -lim, lim), pos.y, Mathf.Clamp(z0, -lim, lim)),
                new Vector3(Mathf.Clamp(x1, -lim, lim), pos.y, Mathf.Clamp(z1, -lim, lim)), new Vector3(Mathf.Clamp(x0, -lim, lim), pos.y, Mathf.Clamp(z1, -lim, lim)),
            };
            // Candidate routes: pos -> A -> B -> target (A == B for a single waypoint), cheapest first, the previous side slightly preferred.
            for (int ai = 0; ai < 4; ai++)
                for (int bi = 0; bi < 4; bi++)
                {
                    float c = Flat(corners[ai] - pos).magnitude + Flat(corners[bi] - corners[ai]).magnitude + Flat(target - corners[bi]).magnitude;
                    if (detourSide != 0f && Mathf.Sign(Vector3.Cross(Flat(corners[ai] - pos), d).y) == detourSide) c *= 0.85f;
                    routeCost[ai * 4 + bi] = c;
                }
            int bestA = -1, bestB = -1;
            for (int tries = 0; tries < 6 && bestA < 0; tries++)
            {
                int pick = -1; float pc = float.MaxValue;
                for (int k = 0; k < 16; k++) if (routeCost[k] < pc) { pc = routeCost[k]; pick = k; }
                if (pick < 0) break;
                routeCost[pick] = float.MaxValue;                       // tried
                int ca = pick / 4, cb2 = pick % 4;
                if (Clear(pos, corners[ca], 0.9f) && (ca == cb2 || Clear(corners[ca], corners[cb2], 0.9f)) && Clear(corners[cb2], target, 0.9f)) { bestA = ca; bestB = cb2; }
            }
            if (bestA < 0) return false;
            wpCount = bestA == bestB ? 1 : 2; wpIndex = 0;
            wp[0] = corners[bestA]; wp[1] = corners[bestB];
            detourSide = Mathf.Sign(Vector3.Cross(Flat(wp[0] - pos), d).y);
            detourAge = 0f;
            DetoursPlanned++;
            return true;
        }

        readonly float[] routeCost = new float[16];

        /// <summary>The point to steer at: the next detour waypoint while one is active, else the (predicted) player position.</summary>
        Vector3 SteerTarget(Vector3 pos, Vector3 fwd, Vector3 aimPoint)
        {
            while (wpIndex < wpCount)
            {
                Vector3 to = Flat(wp[wpIndex] - pos);
                if (to.magnitude < 5f || (Vector3.Dot(to, fwd) < 0f && to.magnitude < 9f)) wpIndex++;
                else return wp[wpIndex];
            }
            return aimPoint;
        }

        // ------------------------------------------------------------------ decisions

        /// <summary>One decision (public so the editor validation, which has no Update, can step it).</summary>
        public void Drive(float dt)
        {
            if (!measureCost) { DriveInner(dt); return; }
            costWatch.Restart();
            DriveInner(dt);
            costWatch.Stop();
            double ms = costWatch.Elapsed.TotalMilliseconds;
            WorstDriveMilliseconds = Mathf.Max(WorstDriveMilliseconds, (float)ms);
            totalDriveMs += ms; DriveCalls++;
        }

        public int DriveCalls { get; private set; }
        double totalDriveMs;
        /// <summary>Mean cost of a Drive call (ms) since the driver was created (only measured when measureCost is on).</summary>
        public float MeanDriveMilliseconds => DriveCalls > 0 ? (float)(totalDriveMs / DriveCalls) : 0f;

        void DriveInner(float dt)
        {
            if (controller == null) Awake();
            if (dt <= 0f) return;
            if (player == null) { controller.Throttle = 0f; controller.SteerInput = 0f; return; }   // nothing to attack: coast
            clock += dt;
            pressure = Mathf.Max(0f, pressure - 0.1f * dt);
            TrackMilling(body.position, dt);
            stateTime += dt;
            seenVelocity = Vector3.Lerp(seenVelocity, player.linearVelocity, 1f - Mathf.Exp(-dt / Mathf.Max(0.02f, reactionTime)));   // imperfect eyes (more lag at low skill)

            Vector3 pos = body.position;
            Vector3 fwd = Flat(transform.forward).normalized;
            float fwdSpeed = controller.ForwardSpeed;
            Vector3 toPlayer = Flat(player.position - pos);
            float dist = toPlayer.magnitude;
            DistanceToPlayer = dist;
            Vector3 dirToPlayer = dist > 0.01f ? toPlayer / dist : fwd;
            float closing = Vector3.Dot(Flat(body.linearVelocity - player.linearVelocity), dirToPlayer);    // m/s the gap shrinks

            // A flipped car cannot drive: it sits (no teleporting out of it).
            flippedTimer = controller.IsFlipped ? flippedTimer + dt : 0f;
            if (flippedTimer > 0.5f) { controller.Throttle = 0f; controller.SteerInput = 0f; return; }

            // Where the player will be shortly: capped linear lead scaled by prediction quality (low skill under-leads), never perfect.
            float lead = Mathf.Min(leadMax, dist / Mathf.Max(8f, Mathf.Abs(fwdSpeed))) * predictionQuality;
            Vector3 aimPoint = Flat(player.position + seenVelocity * lead);
            Vector3 aimDir = Flat(aimPoint - pos);

            decisionTimer -= dt;
            if (decisionTimer <= 0f)
            {
                decisionTimer = decisionInterval;
                Probe(pos, fwd, fwdSpeed, aimPoint);
                UpdateDetour(pos, aimPoint);
                if (breakawayEnabled && nearbyCars != null) MaybeBreakaway(pos, fwd);
                if (Current == State.Breakaway) rearBlocked = obstacleAwareness && Cast(pos, -fwd, 8f, 1.0f, out _);
            }
            if (detourAge >= 0f && Detouring) detourAge += dt;

            float throttle = 0f, steer = 0f;
            avoidBraking = false;
            switch (Current)
            {
                case State.Waiting:
                    if (stateTime >= startDelay) Enter(State.Pursue);
                    break;

                case State.Pursue:
                case State.LineUp:
                case State.Breakaway:
                {
                    bool lineUp = Current == State.LineUp, breakaway = Current == State.Breakaway;
                    float refDist = breakaway ? Flat(breakawayDest - pos).magnitude : dist;   // the distance that matters: to the player, or to the breakaway destination
                    Vector3 target = breakaway ? breakawayDest : SteerTarget(pos, fwd, aimPoint);
                    bool wall = WallAhead(pos, fwd, fwdSpeed, out Vector3 toCentre);
                    Vector3 aim = wall ? toCentre : Flat(target - pos);
                    float angle = Vector3.SignedAngle(fwd, aim, Vector3.up);
                    float absAngle = Mathf.Abs(angle);
                    steer = Mathf.Clamp(angle / fullLockAngle, -1f, 1f);
                    // LineUp slows down to a controllable approach speed, except when the player is pulling away (then it must keep its speed up to catch it)
                    float speedGoal = breakaway ? 0.85f * desiredSpeed : lineUp && dist < chargeRange - 2f && closing > 0.25f * desiredSpeed ? Mathf.Max(minChargeSpeed * 1.3f, 0.65f * desiredSpeed) : desiredSpeed;
                    // Braking decision (skill): the bearing must be nearly nulled before the car reaches its charge range, so the speed is limited to what the car can turn through in the
                    // distance that is left (v <= turn rate x room / error). A skilled driver judges the car's turn rate correctly and slows in time; a poor one thinks it turns tighter than it
                    // does, carries too much speed and arrives off the line. Once inside charge range it crawls until lined up, then accelerates into the charge.
                    if (!breakaway && clearToTarget && dist < lineUpRange && absAngle > 6f && closing > 3f)   // only while it is the faster car closing in: never slow down behind a player who is running away
                    {
                        float tolRange = Mathf.Min(alignTolerance, Mathf.Atan2(Mathf.Lerp(1.4f, 4.5f, aggression) * Mathf.Lerp(1f, 0.6f, skill), chargeRange) * Mathf.Rad2Deg);
                        float err = Mathf.Max(1f, absAngle - tolRange) * Mathf.Deg2Rad;
                        float room = dist - chargeRange;
                        float vTurn = room > 0f ? believedTurnRate * room / err : 0f;
                        speedGoal = Mathf.Min(speedGoal, Mathf.Max(Mathf.Max(5.5f, minChargeSpeed), vTurn));
                    }
                    float diff = speedGoal - fwdSpeed;
                    throttle = diff >= 0f ? Mathf.Clamp01(diff) : Mathf.Max(-0.8f, diff / 4f) * Mathf.Lerp(0.5f, 1f, skill);   // brakes when over the goal (a poor driver brakes less)
                    // pointing well away: slow down / creep so it can swing round (a low skill driver carries more speed into the turn and runs wide)
                    float turnLimit = Mathf.Lerp(9f, 6f, skill);
                    if (absAngle > 70f) throttle = fwdSpeed > turnLimit ? -0.5f * Mathf.Lerp(0.4f, 1f, skill) : 0.55f;
                    else if (absAngle > 40f) throttle = Mathf.Min(throttle, Mathf.Lerp(0.8f, 0.6f, skill));
                    if (!breakaway && dist < 10f) { steer *= 0.7f; throttle = Mathf.Min(throttle, 0.8f); }   // very close: no frantic circling
                    if (wall) throttle = Mathf.Min(throttle, 0.6f);
                    // breakaway with the destination behind a car that is pinned nose to nose: back straight out towards it (steering is mirrored in reverse) instead of crawling round in a circle
                    if (breakaway && absAngle > 115f && fwdSpeed < 6f && stateTime < 2f && !rearBlocked) { steer = angle >= 0f ? -1f : 1f; throttle = -1f; }
                    // something solid dead ahead (and it is not the player): steer round it and brake in time
                    if (obstacleAwareness && forwardBlocked && forwardHitDistance < StopDistance(fwdSpeed) + 6f && forwardHitDistance < refDist - 1f)
                    {
                        steer = avoidSteer;
                        throttle = forwardHitDistance < StopDistance(fwdSpeed) ? -0.6f : Mathf.Min(throttle, 0.3f);
                        avoidBraking = true;
                    }

                    if (breakaway) UpdateBreakaway(pos, dt);
                    else if (!wall && clearToTarget && !Detouring && decisionTimer >= decisionInterval - 1e-4f) EvaluateCombat(pos, fwd, fwdSpeed, dist, closing, angle, aimDir);
                    else if (Current == State.LineUp && (!clearToTarget || Detouring || stateTime > lineUpMaxTime)) Enter(State.Pursue);
                    else if (Current == State.Pursue && !Detouring && clearToTarget && dist < lineUpRange && !wall) Enter(State.LineUp);
                    break;
                }

                case State.Reposition:
                {
                    // Make a run-up. Back off (brake, then reverse) while swinging the nose towards the player when the player is ahead; when the player is
                    // beside or behind, drive round to the far point instead. Short and capped: never a retreat.
                    float angleToPlayer = Vector3.SignedAngle(fwd, toPlayer, Vector3.up);
                    if (repositionBackOff)
                    {
                        float want = angleToPlayer + repositionSide * 25f;           // aim slightly to one side so the run-up arrives at an angle, not nose to nose
                        steer = want >= 0f ? -1f : 1f;                               // reversing mirrors the steering
                        throttle = -1f;
                    }
                    else
                    {
                        Vector3 toPoint = Flat(repositionPoint - pos);
                        float a2 = Vector3.SignedAngle(fwd, toPoint, Vector3.up);
                        steer = Mathf.Clamp(a2 / fullLockAngle, -1f, 1f);
                        throttle = Mathf.Min(Mathf.Clamp01(0.8f * desiredSpeed - fwdSpeed), Mathf.Abs(a2) > 70f ? 0.4f : 1f);
                        if (toPoint.magnitude < 5f) stateTime = repositionMaxTime;    // arrived
                    }
                    if (WallAhead(pos, fwd, fwdSpeed, out Vector3 tc) && !repositionBackOff) { steer = Mathf.Clamp(Vector3.SignedAngle(fwd, tc, Vector3.up) / fullLockAngle, -1f, 1f); throttle = Mathf.Min(throttle, 0.5f); }
                    if (dist >= minRunway * 1.7f + 4f || stateTime >= repositionMaxTime || !clearToTarget)
                    {
                        repositionBlockedUntil = clock + repositionCooldown;
                        circleBlockedUntil = clock + 1.5f;
                        Enter(clearToTarget ? State.LineUp : State.Pursue);
                    }
                    break;
                }

                case State.Commit:
                {
                    commitTimer -= dt;
                    // Committed: it keeps heading for the predicted position with a limited steering authority. A good driver can correct a lot, a poor one little;
                    // nobody can follow a late swerve at 70 km/h, so the charge can be dodged. No state change is considered before minCommitTime except an emergency.
                    float angle = Vector3.SignedAngle(fwd, aimDir, Vector3.up);
                    steer = Mathf.Clamp(angle / (fullLockAngle * 0.5f), -chargeSteerLimit, chargeSteerLimit);
                    throttle = Mathf.Clamp01(desiredSpeed * chargeSpeedFactor - fwdSpeed);
                    bool passed = Vector3.Dot(fwd, toPlayer) < 0f && dist > 3f;
                    bool lost = Mathf.Abs(Vector3.SignedAngle(fwd, aimDir, Vector3.up)) > 75f && dist > 6f;
                    // emergency: a solid obstacle before the player and too close to stop
                    bool emergency = obstacleAwareness && forwardBlocked && forwardHitDistance < StopDistance(fwdSpeed) && forwardHitDistance < dist - 2f;
                    if (emergency) { steer = avoidSteer; throttle = -0.6f; Enter(State.Overshoot); }
                    else if (commitTimer <= 0f || passed || (stateTime > minCommitTime && lost)) Enter(State.Overshoot);
                    break;
                }

                case State.Overshoot:
                {
                    // Missed or just hit: keep rolling roughly straight for a moment (no instant U-turn), steering clear of walls and obstacles, then turn round and pursue.
                    bool wall = WallAhead(pos, fwd, fwdSpeed, out Vector3 toCentre);
                    steer = wall ? Mathf.Clamp(Vector3.SignedAngle(fwd, toCentre, Vector3.up) / fullLockAngle, -1f, 1f) : 0f;
                    throttle = wall ? (fwdSpeed > 8f ? -0.7f * Mathf.Lerp(0.5f, 1f, skill) : 0f) : Mathf.Clamp01(0.5f * desiredSpeed - fwdSpeed);   // a wall ahead: brake (a poor driver brakes less)
                    if (obstacleAwareness && forwardBlocked && forwardHitDistance < StopDistance(fwdSpeed) + 5f)
                    {
                        steer = avoidSteer;
                        throttle = forwardHitDistance < StopDistance(fwdSpeed) ? -0.6f : 0.2f;
                        avoidBraking = true;
                    }
                    if (stateTime >= overshootTime) Enter(State.Pursue);
                    break;
                }

                case State.Recover:
                {
                    // Back out while swinging the nose towards the player (reversing mirrors the steering), then pursue again. Unchanged from V1 and the same for every skill.
                    float angle = Vector3.SignedAngle(fwd, toPlayer, Vector3.up);
                    steer = angle >= 0f ? -1f : 1f;
                    throttle = -1f;
                    if (stateTime >= recoverDuration) { stuckTimer = 0f; ClearDetour(); Enter(State.Pursue); }
                    break;
                }
            }

            // Stuck: trying to drive forward but hardly moving (wall, obstacle, nose-to-nose with the player). Unchanged from V1.
            bool tryingToDrive = Current != State.Waiting && Current != State.Recover && Current != State.Reposition && (throttle > 0.3f || avoidBraking);   // braking for an obstacle while hardly moving is being stuck too
            if (tryingToDrive && body.linearVelocity.magnitude < stuckSpeed) stuckTimer += dt;
            else stuckTimer = Mathf.Max(0f, stuckTimer - dt * 2f);
            if (stuckTimer > stuckTime && Current != State.Recover)
            {
                RecoveriesTriggered++;
                AddPressure(1.2f);
                if (Current == State.Breakaway) { AbortRecover++; EndBreakaway(false); }
                stuckTimer = 0f;
                if (Current == State.Commit || Current == State.LineUp) lastAttackClock = clock;
                planTimer = 0f; detourSide = 0f;     // after being stuck, re-plan the approach from scratch (and allow the other side)
                Enter(State.Recover);
                throttle = -1f;
            }

            // Approach driving eases its throttle (no bang-bang between full throttle and braking when a speed limit flickers); emergencies and recovery act at once.
            bool approach = (Current == State.Pursue || Current == State.LineUp) && !avoidBraking;
            throttleOut = approach ? Mathf.MoveTowards(throttleOut, throttle, 6f * dt) : throttle;
            controller.Throttle = throttleOut;
            controller.SteerInput = steer;
        }

        /// <summary>Keep, refresh or drop the detour. Runs every decision tick (cheap unless a plan is needed).</summary>
        void UpdateDetour(Vector3 pos, Vector3 aimPoint)
        {
            if (!obstacleAwareness || Current == State.Recover || Current == State.Waiting || Current == State.Breakaway) { ClearDetour(); return; }
            planTimer -= decisionInterval;
            if (clearToTarget)
            {
                if (Detouring) ClearDetour();
                return;
            }
            // blocked: keep a working detour (hysteresis), replan when there is none, when it ran out or when the leg to the next waypoint is blocked
            bool legOk = Detouring && Clear(pos, wp[wpIndex], 1.0f) && detourAge < 6f;
            if (legOk) return;
            if (planTimer > 0f && !Detouring) return;
            planTimer = 0.4f;
            if (!PlanDetour(pos, aimPoint)) ClearDetour();
        }

        /// <summary>Attack opportunity: charge now, back off to make a run-up, or carry on (Pursue / LineUp keep steering).</summary>
        void EvaluateCombat(Vector3 pos, Vector3 fwd, float fwdSpeed, float dist, float closing, float angle, Vector3 aimDir)
        {
            float absAngle = Mathf.Abs(angle);
            float sinceAttack = clock - lastAttackClock;
            // Patience runs out gradually: after engageTimeout without an attack the driver accepts worse angles, weaker hits and shorter run-ups (so even a cautious driver comes in regularly)
            float patience = Mathf.Clamp01((sinceAttack - engageTimeout) / 5f);
            // How hard could this hit be? Own speed along the line after accelerating over the remaining run-up (the controller's acceleration, capped at the charge speed),
            // minus the speed the player is moving away at. A tailgater at the player's speed with 2 m to spare gets almost nothing; a car 15 m back gets plenty.
            Vector3 dir = Flat(player.position - pos).normalized;
            float vSelf = Mathf.Max(0f, Vector3.Dot(body.linearVelocity, dir)), vPlayer = Vector3.Dot(player.linearVelocity, dir);
            float cap = desiredSpeed * chargeSpeedFactor;
            float vImpact = Mathf.Min(cap, Mathf.Sqrt(vSelf * vSelf + 2f * 13f * Mathf.Max(0f, dist - ContactDistance))) - vPlayer;
            float minImpact = minImpactSpeed * (1f - 0.4f * patience);

            // opportunity quality 0..1: alignment, own speed, expected impact
            float alignScore = 1f - Mathf.Clamp01(absAngle / 45f);
            float speedScore = Mathf.Clamp01(fwdSpeed / (0.75f * desiredSpeed));
            float runwayScore = Mathf.Clamp01(vImpact / (0.8f * desiredSpeed));
            float opportunity = 0.5f * alignScore + 0.3f * speedScore + 0.2f * runwayScore;
            float threshold = opportunityThreshold - 0.35f * patience;
            // alignment: the bearing must be small for this distance (the car must be on the line, not just roughly pointing at the player); the angle cap is the loose end of it
            float maxLateral = Mathf.Lerp(1.4f, 4.5f, aggression) * Mathf.Lerp(1f, 0.6f, skill) * (1f + 1.5f * patience);
            float tol = Mathf.Min(alignTolerance * (1f + patience), Mathf.Atan2(maxLateral, Mathf.Max(dist, 1f)) * Mathf.Rad2Deg);
            float needSpeed = minChargeSpeed * (1f - 0.4f * patience);

            bool inRange = dist <= chargeRange;
            bool enoughRunway = vImpact >= minImpact;
            if (inRange && enoughRunway && absAngle <= tol && fwdSpeed >= needSpeed && opportunity >= threshold)
            {
                BeginCharge(aimDir, fwdSpeed, dist);
                return;
            }

            // too close for a strong hit and not already rolling at the player: back off and make a run-up (chance and cooldown depend on aggression)
            bool tooClose = dist < minRunway * 1.3f && vImpact < minImpact && patience < 0.6f;
            closeTimer = tooClose ? closeTimer + decisionInterval : 0f;
            if (tooClose && closeTimer > 0.35f && clock >= repositionBlockedUntil && Current != State.Reposition)
            {
                closeTimer = 0f;
                if (rng.NextDouble() < repositionChance && BeginReposition(pos, fwd)) return;
                repositionBlockedUntil = clock + 1.2f;                                       // decided to stay and fight: do not re-roll every tick
            }

            // circling: close to the player but pointing well away (the target is inside the car's turning circle): driving round it never lines up, so swing wide and make a run-up instead
            bool circling = dist < 12f && absAngle > 55f && dist > 3f;
            circleTimer = circling ? circleTimer + decisionInterval : 0f;
            if (circleTimer > 0.7f && clock >= circleBlockedUntil && Current != State.Reposition)
            {
                circleTimer = 0f;
                if (BeginReposition(pos, fwd, true)) return;
                circleBlockedUntil = clock + 1.5f;
            }

            if (Current == State.Pursue && dist < lineUpRange) Enter(State.LineUp);
        }

        void BeginCharge(Vector3 aimDir, float fwdSpeed, float dist)
        {
            commitTimer = Mathf.Min(maxCommitTime, dist / Mathf.Max(fwdSpeed, 8f) + 0.9f);
            AttacksCommitted++;
            LastChargeSpeed = fwdSpeed;
            lastAttackClock = clock;
            Enter(State.Commit);
        }

        bool BeginReposition(Vector3 pos, Vector3 fwd, bool circling = false)
        {
            repositionForCircling = circling;
            Vector3 toPlayer = Flat(player.position - pos);
            float angleToPlayer = Vector3.SignedAngle(fwd, toPlayer, Vector3.up);
            // run-up side: the side the nose is already turned to, so the swing is short
            repositionSide = angleToPlayer >= 0f ? 1f : -1f;
            repositionBackOff = Mathf.Abs(angleToPlayer) < 50f;
            if (repositionBackOff)
            {
                // reversing needs room behind
                if (obstacleAwareness && Cast(pos, -fwd, 9f, 1.0f, out _)) return false;
            }
            else
            {
                Vector3 away = (-toPlayer).normalized;
                float runway = minRunway * 1.7f + 6f;
                float lim = arenaHalf - 6f;
                Vector3 best = Vector3.zero; bool found = false;
                for (int k = 0; k < 2 && !found; k++)
                {
                    float side = k == 0 ? repositionSide : -repositionSide;
                    Vector3 p = Flat(player.position) + Quaternion.Euler(0f, side * 40f, 0f) * away * runway;
                    p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
                    if (!obstacleAwareness || Clear(pos, p, 1.0f)) { best = p; found = true; }
                }
                if (!found) return false;
                repositionPoint = best;
            }
            Repositions++;
            AddPressure(1f);
            Enter(State.Reposition);
            return true;
        }

        // ------------------------------------------------------------------ milling: staying in the same few metres with the target close for several seconds

        readonly Vector3[] posHistory = new Vector3[12];
        int histCount, histHead;
        float histTimer;
        /// <summary>True while the driver has stayed within 8 m for the last 6 s with its target within 12 m (a clump that is going nowhere).</summary>
        public bool Milling { get; private set; }

        void TrackMilling(Vector3 pos, float dt)
        {
            histTimer += dt;
            if (histTimer >= 0.5f)
            {
                histTimer = 0f;
                posHistory[histHead] = pos; histHead = (histHead + 1) % posHistory.Length; histCount = Mathf.Min(histCount + 1, posHistory.Length);
                Vector3 oldest = posHistory[histCount < posHistory.Length ? 0 : histHead];
                Milling = histCount >= posHistory.Length && player != null && Current != State.Waiting && Current != State.Breakaway
                          && Flat(pos - oldest).magnitude < 8f && DistanceToPlayer < 12f;
            }
            if (Milling) AddPressure(0.25f * dt);
        }

        // ------------------------------------------------------------------ breakaway

        /// <summary>Adds unproductive-fighting pressure (also used by the validation to force the situation).</summary>
        public void AddPressure(float x) { pressure = Mathf.Min(8f, pressure + x); }

        /// <summary>
        /// Decides whether to leave the cluster: enough unproductive fighting (pressure over the profile's threshold) AND a crowd (two or more other cars within 12 m, or a
        /// two-car lock with much more pressure), then the profile's chance, the game's cap on simultaneous breakaways and a destination that works. Runs on the decision tick only.
        /// </summary>
        void MaybeBreakaway(Vector3 pos, Vector3 fwd)
        {
            if (Current != State.Pursue && Current != State.LineUp && Current != State.Overshoot) return;
            if (pressure < breakawayThreshold || clock < breakawayBlockedUntil || player == null) return;
            breakawayBlockedUntil = clock + 1f;                       // not re-evaluated every tick
            int n = nearbyCars(body, near, 45f);
            int close = 0;
            for (int i = 0; i < n; i++) if (Flat(near[i] - pos).sqrMagnitude < 144f) close++;
            if (!(close >= 2 || pressure >= 1.1f * breakawayThreshold)) { RejectedCrowd++; return; }
            if (rng.NextDouble() > breakawayChance) { pressure *= 0.6f; RejectedChance++; return; }   // stays and fights; may try again later
            if (breakawayAllowed != null && !breakawayAllowed(this)) { RejectedCap++; return; }
            if (measureCost) pickWatch.Restart();
            bool picked = PickBreakawayDestination(pos, fwd, n, out Vector3 d);
            if (measureCost) { pickWatch.Stop(); WorstBreakawayMilliseconds = Mathf.Max(WorstBreakawayMilliseconds, (float)pickWatch.Elapsed.TotalMilliseconds); }
            if (!picked) { RejectedNoDestination++; failedBreakaways++; pressure *= 0.6f; breakawayBlockedUntil = clock + 3f; return; }
            breakawayDest = d; Breakaways++;
            breakawayStarted = clock; breakawayProgressTimer = 0f; breakawayBestDist = Flat(d - pos).magnitude;
            ClearDetour();
            Enter(State.Breakaway);
        }

        /// <summary>
        /// A point about one charge distance from the target, on the open side of the crowd: no car within 10 m of it, reachable in a straight line round the obstacles without
        /// driving through the cluster, away from the arena edge, reached from a different angle than the last contact (skilled drivers weigh that). A few candidate bearings are tried,
        /// the best wins; a low skill driver's choice is noisier.
        /// </summary>
        bool PickBreakawayDestination(Vector3 pos, Vector3 fwd, int n, out Vector3 best)
        {
            best = pos;
            Vector3 anchor = Flat(player.position);
            Vector3 toMe = Flat(pos - anchor);
            float meAngle = Mathf.Atan2(toMe.x, toMe.z) * Mathf.Rad2Deg;
            int k = Mathf.Max(4, (int)breakawayCandidates);
            float phase = (float)rng.NextDouble() * 360f / k;
            float lim = arenaHalf - 9f, bestScore = float.NegativeInfinity;
            for (int i = 0; i < k; i++)
            {
                float ang = phase + i * 360f / k;
                Vector3 p = anchor + new Vector3(Mathf.Sin(ang * Mathf.Deg2Rad), 0f, Mathf.Cos(ang * Mathf.Deg2Rad)) * breakawayDistance;
                p.y = pos.y;
                if (Mathf.Abs(p.x) > lim || Mathf.Abs(p.z) > lim)
                {
                    p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -lim, lim);
                    if (Flat(p - anchor).magnitude < 0.6f * breakawayDistance) continue;
                }
                Vector3 path = Flat(p - pos); float travel = path.magnitude;
                if (travel < 12f) continue;
                Vector3 pd = path / travel;
                float open = 25f; bool blocked = false;
                for (int j = 0; j < n; j++)
                {
                    open = Mathf.Min(open, Flat(near[j] - p).magnitude);
                    // distance of the car to the stretch of the path that starts 7 m out (its immediate neighbours are behind us already)
                    Vector3 a = pos + pd * 7f; Vector3 ab = p - a; float len2 = Flat(ab).sqrMagnitude;
                    float t = len2 > 0.01f ? Mathf.Clamp01(Vector3.Dot(Flat(near[j] - a), Flat(ab)) / len2) : 0f;
                    if (Flat(near[j] - (a + ab * t)).magnitude < 4.5f) { blocked = true; break; }
                }
                if (blocked || open < 10f) continue;
                if (obstacleAwareness && !Clear(pos, p, 1.0f)) continue;
                if (destinationTaken != null && destinationTaken(p, this)) continue;
                float diff = Mathf.Abs(Mathf.DeltaAngle(ang, meAngle));
                float flank = Mathf.Clamp01(1f - Mathf.Abs(diff - 100f) / 80f);          // a new angle on the target rather than the one that is going nowhere
                float edge = Mathf.Clamp01((lim - Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z))) / 10f);
                float turn = Mathf.Abs(Vector3.SignedAngle(Flat(fwd), pd, Vector3.up));
                float score = 0.04f * open + 0.6f * skill * flank + 0.3f * edge - 0.02f * travel - 0.003f * turn + breakawayNoise * ((float)rng.NextDouble() * 2f - 1f);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return bestScore > float.NegativeInfinity;
        }

        void UpdateBreakaway(Vector3 pos, float dt)
        {
            float d = Flat(breakawayDest - pos).magnitude;
            if (d < 7f) { EndBreakaway(true); return; }
            if (clock - breakawayStarted > breakawayMaxTime) { AbortTimeout++; EndBreakaway(false); return; }
            breakawayProgressTimer += dt;
            if (d < breakawayBestDist - 1f) { breakawayBestDist = d; breakawayProgressTimer = 0f; }
            else if (breakawayProgressTimer > 3f) { AbortProgress++; EndBreakaway(false); }              // not getting anywhere: give up and fight
        }

        void EndBreakaway(bool reached)
        {
            if (Current != State.Breakaway) return;
            if (reached) { BreakawaysCompleted++; failedBreakaways = 0; } else { BreakawaysAborted++; failedBreakaways++; }
            pressure = 0f;
            breakawayBlockedUntil = clock + breakawayCooldown * (1 + failedBreakaways);   // a failed attempt makes the next one wait longer
            // back into the fight through the normal combat loop (LineUp, charge); the wait for a good attack is partly used up, more so for an aggressive driver
            lastAttackClock = Mathf.Min(lastAttackClock, clock - engageTimeout - 3f * aggression);
            Enter(State.Pursue);
        }

        /// <summary>True when the car, a moment from now at its current speed, would be too close to the arena edge; also returns the direction to the middle.</summary>
        bool WallAhead(Vector3 pos, Vector3 fwd, float fwdSpeed, out Vector3 toCentre)
        {
            Vector3 ahead = pos + fwd * Mathf.Max(6f, fwdSpeed * 1.1f);
            float limit = arenaHalf - wallMargin;
            toCentre = Flat(-pos);
            return fwdSpeed > 0f && (Mathf.Abs(ahead.x) > limit || Mathf.Abs(ahead.z) > limit);
        }
    }
}
