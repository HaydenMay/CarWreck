using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CarWreck.Driving;
using DamageLab.CollisionTest;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby
{
    /// <summary>One built vehicle of a participant: the destructible rig, its handling controller and (AI) the driver that feeds it.</summary>
    public class DerbyCar
    {
        public Participant participant;
        public VehicleRig.Rig rig;
        public VehicleDefinition def;
        public ArcadeCarController controller;
        public EnemyDriver enemy;                    // AI only
        public System.Action<CollisionDamageReceiver.Report> handler;
        public float refitWait, refitAge;            // seconds since the latest / the first damaging hit still waiting for a collider refit
        public int refits, unflips;
        public float flippedTime;
        public Rigidbody Body => rig.body;
        public VehicleDamageModel Model => rig.model;
        public CollisionDamageReceiver Receiver => rig.receiver;
        public float GroundLift => rig.geometry.groundLift;
    }

    /// <summary>
    /// The playable derby: builds the participants' cars from the project's vehicle definitions (the same <see cref="VehicleRig"/> and <see cref="ArcadeCarController"/> the
    /// driving arena uses), paints them, spawns and respawns them, feeds verified collision damage into the <see cref="DerbyMatch"/> rules, runs the AI
    /// targeting and the player's input, and tells the UI what to show. It adds a gameplay layer around the existing systems: the physics, the damage and
    /// deformation engine, progressive crushing, the damage-aware colliders and the AI driver are used as they are.
    /// </summary>
    public class DerbyGame : MonoBehaviour
    {
        [Header("Vehicles (same order as the driving arena: muscle car, hatchback, pickup)")]
        public VehicleDefinition[] vehicles;
        public DriveProfile[] driveProfiles;
        public VehicleRigSettings rigSettings = new VehicleRigSettings();
        public PaintPalette palette;
        [Tooltip("Driver personalities handed out to the AI cars in order (Brawler, Hunter, Cautious, Rookie, then round again).")]
        public DriverProfile[] aiProfiles;

        [Header("Match")]
        public MatchRules rules = new MatchRules();
        [Tooltip("Cars in the match, the player's included. Default 5. A core setting, not a limit: the page accepts ?cars=N for scaling checks.")]
        public int participantCount = 5;
        public int playerVehicle = 0, playerPaint = 0;
        [Tooltip("Speed the AI tries to hold (km/h), as in the arena's enemy.")]
        public float aiSpeedKmh = 70f;
        [Tooltip("Flipped AI cars are put back on their wheels after this long (s). The player does it with the RESET / R button.")]
        public float aiUnflipDelay = 2.5f;

        [Header("Arena")]
        public float arenaHalf = 50f;
        [Tooltip("Parent of the authored spawn points (children face the middle).")]
        public Transform spawnPoints;
        public Transform menuSpawn;

        [Header("Scene")]
        public ChaseCamera chaseCamera;
        public DriveInput input;
        public DerbyUI ui;

        [Header("Damage mapping (the same values as the driving arena; the engine itself is untouched)")]
        public float damageEnergyThreshold = 1500f;
        public float damageEnergyScale = 80000f;
        public bool shareEnergyBetweenVehicles = true;
        public float severeCrushEnergy = 100000f, severeCrushEnergyScale = 250000f, severeCrushDepthGain = 2f;
        public float crushCapacityScale = 1.8f;
        [Header("Damage-aware colliders and progressive crushing (as in the arena)")]
        public bool refitColliders = true;
        public float colliderSettleDelay = 0.2f, colliderMaxDelay = 1f;
        public bool progressiveCrush = true;
        public float crushTimeLight = 0.06f, crushTimeHeavy = 0.24f, crushHeavyTravel = 0.6f;
        [Range(0f, 0.5f)] public float crushStagger = 0.3f;

        // ------------------------------------------------------------------ state

        public DerbyMatch Match { get; private set; }
        public Participant Human { get; private set; }
        public readonly List<Participant> Participants = new List<Participant>();
        public readonly AiTargeting Targeting = new AiTargeting();
        public bool MenuOpen { get; private set; } = true;
        public bool Paused { get; private set; }
        public bool IsSetUp => isSetUp;
        public int MatchesStarted { get; private set; }
        public float LastBuildMilliseconds { get; private set; }
        public float WorstAiMilliseconds { get; private set; }
        public int Respawns { get; private set; }
        public int WorstHitchCount { get; private set; }
        public string LastFeed { get; private set; } = "";

        bool isSetUp;
        System.Random rng = new System.Random(1);
        List<SpawnPlanner.Spot> candidates, respawnPool;
        readonly Dictionary<string, Participant> byName = new Dictionary<string, Participant>();
        readonly Dictionary<Rigidbody, DerbyCar> byBody = new Dictionary<Rigidbody, DerbyCar>();
        readonly List<Participant> aiParticipants = new List<Participant>();
        float fpsEma = 60f, reportTimer;
        Participant spectated;
        readonly Stopwatch watch = new Stopwatch();
        readonly List<Vector3> scratchPositions = new List<Vector3>(16);

        public ChaseCamera Camera => chaseCamera;
        public DerbyCar HumanCar => Human != null ? Human.car : null;
        public float PlayerSpeedKmh => HumanCar != null ? HumanCar.controller.Speed * 3.6f : 0f;
        public bool HumanFlipped => HumanCar != null && HumanCar.flippedTime > 1.2f;

        void Start() => Setup();

        void OnDestroy()
        {
            Time.timeScale = 1f;
            VehiclePainter.ClearCache();
        }

        /// <summary>One-time setup (also called by the edit-mode validation, which has no Start).</summary>
        public void Setup()
        {
            if (isSetUp) return;
            isSetUp = true;
            if (Application.isPlaying && HandleUrl()) return;
            playerVehicle = Mathf.Clamp(playerVehicle, 0, vehicles.Length - 1);
            candidates = SpawnPlanner.Candidates(spawnPoints, arenaHalf, participantCount);
            respawnPool = SpawnPlanner.RespawnPool(spawnPoints, arenaHalf);
            Human = new Participant(0, "YOU", ControlType.Human) { vehicleIndex = playerVehicle, paintIndex = playerPaint };
            if (ui != null) { ui.input = input; ui.game = this; }
            BuildHumanCar();
            if (Application.isPlaying) OpenMenu();
        }

        /// <summary>Page parameters (development / scaling checks): ?cars=N, ?mode=last|points, ?scene=drive (the diagnostic driving arena).</summary>
        bool HandleUrl()
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url) || !url.Contains("?")) return false;
            string q = url.Substring(url.IndexOf('?') + 1);
            foreach (var pair in q.Split('&'))
            {
                var kv = pair.Split('=');
                if (kv.Length != 2) continue;
                string k = kv[0].ToLowerInvariant(), v = kv[1].ToLowerInvariant();
                if (k == "scene" && v == "drive") { SceneManager.LoadScene("DriveArena"); isSetUp = false; return true; }
                if (k == "cars" && int.TryParse(v, out int n)) participantCount = Mathf.Clamp(n, 2, 16);
                if (k == "mode") rules.mode = v.StartsWith("last") ? GameMode.LastCarStanding : GameMode.PointsDerby;
            }
            return false;
        }

        // ------------------------------------------------------------------ building cars

        void BuildHumanCar()
        {
            Human.vehicleIndex = playerVehicle; Human.paintIndex = playerPaint;
            var spot = MenuSpot();
            Human.car = BuildCar(Human, spot);
            RegisterCar(Human.car);
            if (chaseCamera != null) { chaseCamera.target = Human.car.Body; chaseCamera.Snap(); }
        }

        SpawnPlanner.Spot MenuSpot()
        {
            if (menuSpawn != null) return new SpawnPlanner.Spot { position = menuSpawn.position, yaw = menuSpawn.eulerAngles.y };
            return candidates.Count > 0 ? candidates[0] : new SpawnPlanner.Spot { position = new Vector3(0f, 0f, -30f), yaw = 0f };
        }

        DerbyCar BuildCar(Participant p, SpawnPlanner.Spot spot)
        {
            var def = vehicles[p.vehicleIndex];
            var rig = VehicleRig.Build(def, null, spot.position, rigSettings);
            if (rig.footprintDebug != null) Kill(rig.footprintDebug);      // Lab-only debug views
            if (rig.colliderDebug != null) Kill(rig.colliderDebug);
            rig.model.impactCapacityScale = crushCapacityScale;
            rig.model.progressiveCrush = new DamageLab.Deformation.ProgressiveCrushSettings
            {
                enabled = progressiveCrush, minDuration = crushTimeLight, maxDuration = crushTimeHeavy, fullTravel = crushHeavyTravel, stagger = crushStagger,
            };
            rig.model.Initialize();
            rig.hitbox.Setup();
            var car = new DerbyCar { participant = p, rig = rig, def = def };
            Quaternion rot = Quaternion.Euler(0f, spot.yaw, 0f);
            Vector3 pos = spot.position + Vector3.up * rig.geometry.groundLift;
            rig.root.transform.SetPositionAndRotation(pos, rot);
            rig.body.position = pos; rig.body.rotation = rot;
            var c = rig.root.AddComponent<ArcadeCarController>();
            c.Apply(driveProfiles[p.vehicleIndex]);
            c.Configure(rig.geometry.body, rig.geometry.wheels.min.y);
            car.controller = c;
            rig.root.name = $"Car{p.id} {p.name}";
            var receiver = rig.receiver;
            receiver.oneImpactPerLaunch = false;
            receiver.logImpacts = false;
            receiver.energyThreshold = damageEnergyThreshold;
            receiver.energyScale = damageEnergyScale;
            receiver.shareEnergyWithVehicles = shareEnergyBetweenVehicles;
            receiver.severeEnergy = severeCrushEnergy;
            receiver.severeEnergyScale = severeCrushEnergyScale;
            receiver.severeDepthGain = severeCrushDepthGain;
            car.handler = r => OnImpact(car, r);
            receiver.ImpactRegistered += car.handler;
            if (p.control == ControlType.AI)
            {
                var e = rig.root.AddComponent<EnemyDriver>();
                e.receiver = receiver;
                e.player = null;
                e.arenaHalf = arenaHalf;
                e.carIsObstacle = IsHulk;
                e.measureCost = true;
                e.seed = 101 + p.id * 17 + MatchesStarted;
                e.SetProfile(p.profile);
                e.Begin(aiSpeedKmh / 3.6f);
                e.enabled = false;           // driven only while the match is active
                car.enemy = e;
            }
            ApplyPaint(car, p.paintIndex);
            Physics.SyncTransforms();
            return car;
        }

        void RegisterCar(DerbyCar car)
        {
            byName[car.rig.root.name] = car.participant;
            byBody[car.Body] = car;
        }

        void DestroyCar(DerbyCar car)
        {
            if (car == null) return;
            byName.Remove(car.rig.root.name);
            byBody.Remove(car.Body);
            if (car.Receiver != null) car.Receiver.ImpactRegistered -= car.handler;
            if (car.Model != null) car.Model.Shutdown();
            car.rig.root.SetActive(false);        // leaves the physics scene at once
            Kill(car.rig.root);
            car.participant.car = null;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        bool IsHulk(Rigidbody rb) => byBody.TryGetValue(rb, out var c) && (c.participant.status == ParticipantStatus.Wrecked || c.participant.status == ParticipantStatus.Out);

        void ApplyPaint(DerbyCar car, int paintIndex)
        {
            if (palette == null || palette.Count == 0) return;
            VehiclePainter.Apply(car.rig.root, palette.Get(paintIndex), palette);
        }

        void ApplyWreckPaint(DerbyCar car)
        {
            if (palette == null) return;
            VehiclePainter.Apply(car.rig.root, palette.wreck, palette);
        }

        // ------------------------------------------------------------------ menu choices (before a match)

        public void SelectVehicle(int index)
        {
            index = Mathf.Clamp(index, 0, vehicles.Length - 1);
            if (index == playerVehicle && Human.car != null) return;
            playerVehicle = index;
            if (Human.car != null) { byName.Remove(Human.car.rig.root.name); DestroyCar(Human.car); }
            DespawnAi();                                  // the AI roster depends on the player's vehicle
            BuildHumanCar();
            if (chaseCamera != null && MenuOpen) chaseCamera.SetMenuFocus(Human.car.rig.root.transform);
        }

        public void SelectPaint(int index)
        {
            playerPaint = Mathf.Clamp(index, 0, Mathf.Max(0, palette.Count - 1));
            Human.paintIndex = playerPaint;
            if (Human.car != null) ApplyPaint(Human.car, playerPaint);
        }

        public void SelectMode(GameMode mode) => rules.mode = mode;

        // ------------------------------------------------------------------ roster

        /// <summary>The AI participants for the current setting: profiles in order, vehicles rotating from the player's, colours rotating from the player's.</summary>
        void BuildAiRoster()
        {
            DespawnAi();
            int n = Mathf.Max(0, participantCount - 1);
            var nameCount = new Dictionary<string, int>();
            for (int i = 1; i <= n; i++)
            {
                var prof = aiProfiles != null && aiProfiles.Length > 0 ? aiProfiles[(i - 1) % aiProfiles.Length] : null;
                string baseName = prof != null ? prof.displayName.ToUpperInvariant() : "DRIVER";
                nameCount.TryGetValue(baseName, out int seen);
                nameCount[baseName] = seen + 1;
                var p = new Participant(i, seen == 0 ? baseName : baseName + " " + (seen + 1), ControlType.AI)
                {
                    profile = prof,
                    vehicleIndex = (playerVehicle + i) % vehicles.Length,
                    paintIndex = palette != null && palette.Count > 0 ? (playerPaint + i) % palette.Count : 0,
                };
                aiParticipants.Add(p);
            }
        }

        void DespawnAi()
        {
            foreach (var p in aiParticipants) DestroyCar(p.car);
            aiParticipants.Clear();
        }

        // ------------------------------------------------------------------ match lifecycle

        /// <summary>Menu START / RESULTS REMATCH: a clean match with the current choices.</summary>
        public void StartMatch()
        {
            if (!isSetUp) Setup();
            watch.Restart();
            MatchesStarted++;
            Paused = false;
            MenuOpen = false;
            Time.timeScale = 1f;
            // roster: kept for a rematch, rebuilt when the count or the player's choices changed
            int wantAi = Mathf.Max(0, participantCount - 1);
            if (aiParticipants.Count != wantAi || aiParticipants.Exists(p => p.car == null) || aiParticipants.Count > 0 && aiParticipants[0].vehicleIndex != (playerVehicle + 1) % vehicles.Length)
                BuildAiRoster();
            Participants.Clear();
            Participants.Add(Human);
            Participants.AddRange(aiParticipants);
            candidates = SpawnPlanner.Candidates(spawnPoints, arenaHalf, Participants.Count);
            var layout = SpawnPlanner.InitialLayout(candidates, Participants.Count, MatchesStarted * 7919);
            // shuffle who gets which spot (seeded, so a match is reproducible but the player's neighbours change from match to match)
            var order = new List<int>(); for (int i = 0; i < layout.Length; i++) order.Add(i);
            var r = new System.Random(MatchesStarted * 31 + 5);
            for (int i = order.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < Participants.Count; i++)
            {
                var p = Participants[i];
                var spot = layout[order[i % layout.Length]];
                if (p.control == ControlType.AI && palette != null && palette.Count > 0) p.paintIndex = (playerPaint + p.id) % palette.Count;   // colours follow the player's choice
                if (p.car == null) { p.car = BuildCar(p, spot); RegisterCar(p.car); }
                ResetCar(p.car, spot);
            }
            Targeting.Reset();
            Match = new DerbyMatch(rules, Participants);
            Match.StateChanged += OnStateChanged;
            Match.Wrecked += OnWrecked;
            Match.Scored += OnScored;
            Match.RespawnDue += OnRespawnDue;
            Match.ShowdownStarted += OnShowdownStarted;
            Match.SetPreparing();
            Match.Begin();
            spectated = null;
            if (chaseCamera != null) { chaseCamera.ClearMenu(); chaseCamera.target = Human.car.Body; chaseCamera.yawOffset = 0f; chaseCamera.Snap(); }
            if (ui != null) ui.OnMatchStarted();
            watch.Stop();
            LastBuildMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        public void Rematch() => StartMatch();

        /// <summary>Back to the vehicle selection: the match is dropped, the AI cars go, the player's car is repaired and shown.</summary>
        public void GoToMenu()
        {
            if (Match != null) { Match.StateChanged -= OnStateChanged; Match.Wrecked -= OnWrecked; Match.Scored -= OnScored; Match.RespawnDue -= OnRespawnDue; Match.ShowdownStarted -= OnShowdownStarted; Match.Abort(); }
            Match = null;
            Paused = false;
            DespawnAi();
            Participants.Clear();
            Targeting.Reset();
            if (Human.car != null) { ResetCar(Human.car, MenuSpot()); Human.status = ParticipantStatus.Waiting; Human.score = 0; }
            OpenMenu();
        }

        void OpenMenu()
        {
            MenuOpen = true;
            Time.timeScale = 0f;
            if (chaseCamera != null && Human.car != null) { chaseCamera.target = Human.car.Body; chaseCamera.SetMenuFocus(Human.car.rig.root.transform); }
            if (ui != null) ui.ShowMenu(true);
        }

        public void TogglePause()
        {
            if (MenuOpen || Match == null || Match.State == MatchState.Results) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
            if (ui != null) ui.ShowPause(Paused);
        }

        void OnStateChanged(MatchState old, MatchState now)
        {
            switch (now)
            {
                case MatchState.Countdown:
                    foreach (var p in Participants) Idle(p.car);
                    break;
                case MatchState.Active:
                {
                    int k = 0;
                    foreach (var p in Participants)
                    {
                        if (p.car.enemy == null) continue;
                        p.car.enemy.enabled = true;
                        p.car.enemy.startDelay = 0.3f + 0.12f * k++;
                        p.car.enemy.Restart();
                    }
                    Targeting.Update(Match, Match.Clock);
                    break;
                }
                case MatchState.Results:
                    foreach (var p in Participants) Idle(p.car);
                    if (ui != null) ui.ShowResults();
                    break;
            }
            if (ui != null) ui.OnStateChanged(old, now);
        }

        static void Idle(DerbyCar c)
        {
            if (c == null) return;
            if (c.enemy != null) c.enemy.enabled = false;
            c.controller.Throttle = 0f; c.controller.SteerInput = 0f;
        }

        void OnShowdownStarted() { if (ui != null) ui.Banner("FINAL SHOWDOWN", 2.5f); AddFeed("FINAL SHOWDOWN - last two cars"); }

        void OnWrecked(Participant victim, Participant credit)
        {
            Idle(victim.car);
            ApplyWreckPaint(victim.car);
            Targeting.TargetLost(victim.id);
            AddFeed(credit != null ? $"{credit.name} wrecked {victim.name}  +{Match.rules.wreckBonus}" : $"{victim.name} crashed out");
            if (victim == Human && ui != null) ui.Banner(rules.RespawnsEnabled ? "WRECKED" : "YOU ARE OUT", 2.2f);
            if (credit == Human && ui != null) ui.Banner($"WRECKED {victim.name}  +{Match.rules.wreckBonus}", 1.6f);
        }

        void OnScored(Participant attacker, Participant victim, int points, string tier)
        {
            if (attacker == Human && ui != null) ui.ShowPoints(points, tier);
        }

        void OnRespawnDue(Participant p)
        {
            if (p.car == null) return;
            scratchPositions.Clear();
            foreach (var o in Participants) if (o != p && o.car != null) scratchPositions.Add(o.car.Body.position);
            var spot = SpawnPlanner.PickRespawn(respawnPool, scratchPositions, SpotBlocked, rng, out _);
            ResetCar(p.car, spot);
            if (p.car.enemy != null) { p.car.enemy.enabled = true; p.car.enemy.startDelay = 0.6f; p.car.enemy.Restart(); }
            Match.Respawned(p);
            Respawns++;
            AddFeed($"{p.name} is back");
            if (p == Human)
            {
                if (chaseCamera != null) { chaseCamera.target = p.car.Body; chaseCamera.Snap(); }
                if (ui != null) ui.Banner("GO!", 0.9f);
            }
        }

        static readonly Collider[] overlapBuffer = new Collider[8];

        /// <summary>True when a car standing at the spot would overlap something (another car, a hulk, an obstacle). The ground is below the check box.</summary>
        bool SpotBlocked(SpawnPlanner.Spot s)
        {
            int n = Physics.OverlapBoxNonAlloc(s.position + Vector3.up * 1.3f, new Vector3(1.6f, 0.9f, 3.2f), overlapBuffer, Quaternion.Euler(0f, s.yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
            return n > 0;
        }

        /// <summary>Places a car on the spot with its geometry, damage, collision shell, motion and AI state restored (the Reset path the arena has used from the start).</summary>
        public void ResetCar(DerbyCar c, SpawnPlanner.Spot spot)
        {
            var m = c.Model;
            m.ResetAll();
            m.SetDamageImmediate(0f, 0f, 0f, 0f);
            if (c.rig.hitbox != null) c.rig.hitbox.ApplyPending();
            c.refitWait = c.refitAge = 0f; c.flippedTime = 0f;
            var rb = c.Body;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Vector3 pos = spot.position + Vector3.up * c.GroundLift;
            Quaternion rot = Quaternion.Euler(0f, spot.yaw, 0f);
            rb.position = pos; rb.rotation = rot;
            rb.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
            c.Receiver.ResetState();
            c.controller.ResetState();
            c.controller.Throttle = 0f; c.controller.SteerInput = 0f;
            if (c.enemy != null) { c.enemy.SetTarget(null); c.enemy.Restart(); c.enemy.enabled = false; }
            ApplyPaint(c, c.participant.paintIndex);
            if (c.participant.status != ParticipantStatus.Active) c.participant.status = ParticipantStatus.Waiting;
        }

        // ------------------------------------------------------------------ damage -> match

        void OnImpact(DerbyCar victimCar, CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
            victimCar.refitWait = 0f;      // a new hit restarts the settle wait (the hitbox was marked pending by the model)
            if (r.deformMilliseconds >= 40f) WorstHitchCount++;
            if (Match == null) return;
            int attackerId = byName.TryGetValue(r.other, out var a) ? a.id : -1;
            Match.ApplyDamage(new DamageEvent(victimCar.participant.id, attackerId, r.energy, r.strength));
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (!isSetUp || Human == null || Human.car == null) return;
            if (input != null)
            {
                if (input.ConsumeMenu()) { if (MenuOpen) { } else TogglePause(); }
                if (input.ConsumeReset() && !MenuOpen && !Paused) Unflip(Human.car);
            }
            if (Match != null && !MenuOpen) Frame(Time.deltaTime, true);
            fpsEma = Mathf.Lerp(fpsEma, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            reportTimer -= Time.unscaledDeltaTime;
            if (reportTimer <= 0f) { reportTimer = 0.25f; ReportToPage(); }
        }

        // FixedUpdate runs before each physics step: a collider change made here is never made while the solver is working.
        void FixedUpdate() => AfterPhysicsStep(Time.fixedDeltaTime);

        /// <summary>Everything that is not physics, once per rendered frame: the match clock, the player's inputs, AI targeting, flipped cars, camera.</summary>
        void Frame(float dt, bool readInput)
        {
            Match.Tick(dt);
            var hc = Human.car;
            if (readInput && input != null && Match.InPlay && Human.IsActive && !Paused) { hc.controller.Throttle = input.Throttle; hc.controller.SteerInput = input.Steer; }
            else if (readInput) { hc.controller.Throttle = 0f; hc.controller.SteerInput = 0f; }
            if (Match.InPlay)
            {
                watch.Restart();
                Targeting.Update(Match, Match.Clock);
                watch.Stop();
                WorstAiMilliseconds = Mathf.Max(WorstAiMilliseconds, (float)watch.Elapsed.TotalMilliseconds);
            }
            foreach (var p in Participants)
            {
                var c = p.car;
                if (c == null) continue;
                c.flippedTime = c.controller.IsFlipped && c.Body.linearVelocity.magnitude < 2f ? c.flippedTime + dt : 0f;
                if (p.control == ControlType.AI && p.IsActive && c.flippedTime > aiUnflipDelay) Unflip(c);
            }
            UpdateCamera();
        }

        /// <summary>The car upright where it is (a flipped car cannot drive). Rules-neutral: no points, no damage.</summary>
        public void Unflip(DerbyCar c)
        {
            if (c == null) return;
            var rb = c.Body;
            Vector3 p = rb.position;
            float yaw = rb.rotation.eulerAngles.y;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Vector3 pos = new Vector3(p.x, c.GroundLift + 0.3f, p.z);
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = pos; rb.rotation = rot;
            rb.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
            c.flippedTime = 0f; c.unflips++;
        }

        void UpdateCamera()
        {
            if (chaseCamera == null || Match == null) return;
            // The player's car, or - once the player is out of the match - the car that is still fighting.
            Participant want = Human;
            if (!Human.IsActive && Human.status == ParticipantStatus.Out)
            {
                if (spectated == null || !spectated.IsActive)
                {
                    spectated = null; float best = float.MaxValue;
                    foreach (var p in Participants)
                        if (p.IsActive && p.car != null) { float d = (p.car.Body.position - Human.car.Body.position).sqrMagnitude; if (d < best) { best = d; spectated = p; } }
                }
                if (spectated != null) want = spectated;
            }
            if (want.car != null && chaseCamera.target != want.car.Body) { chaseCamera.target = want.car.Body; chaseCamera.Snap(); }
        }

        /// <summary>The arena's safe point for collider refits, for every car (see DrivingGame.AfterPhysicsStep). Also called by the edit-mode validation.</summary>
        public void AfterPhysicsStep(float dt)
        {
            if (!refitColliders) return;
            foreach (var p in Participants) RefitIfDue(p.car, dt);
            if (Participants.Count == 0) RefitIfDue(Human != null ? Human.car : null, dt);
        }

        void RefitIfDue(DerbyCar s, float dt)
        {
            if (s == null || s.rig == null || s.rig.hitbox == null) return;
            var hb = s.rig.hitbox;
            if (!hb.HasPending) { s.refitWait = s.refitAge = 0f; return; }
            s.refitWait += dt; s.refitAge += dt;
            if (s.refitWait < colliderSettleDelay && s.refitAge < colliderMaxDelay) return;
            if (s.Model != null && s.Model.IsCrushing && s.refitAge < colliderMaxDelay) return;
            hb.ApplyPending();
            s.refitWait = s.refitAge = 0f;
            s.refits++;
        }

        /// <summary>
        /// Edit-mode validation only (no Update / FixedUpdate runs there): one fixed step of everything - match clock, inputs, AI drivers, car controllers,
        /// physics, damage receivers and collider refits - in the order the player loop runs them.
        /// </summary>
        public void ManualStep(float dt)
        {
            if (Match != null) Frame(dt, false);
            foreach (var p in Participants)
                if (p.car != null && p.car.enemy != null && p.car.enemy.enabled) p.car.enemy.Drive(dt);
            AfterPhysicsStep(dt);
            foreach (var p in Participants) if (p.car != null) p.car.controller.Tick(dt);
            Physics.Simulate(dt);
            foreach (var p in Participants) if (p.car != null) p.car.Receiver.Step(dt);
        }

        void AddFeed(string s)
        {
            LastFeed = s;
            if (ui != null) ui.PushFeed(s);
        }

        // ------------------------------------------------------------------ queries for the HUD

        /// <summary>Position of the human among all participants by current score (1 = leading).</summary>
        public int HumanRank()
        {
            int rank = 1;
            foreach (var p in Participants) if (p != Human && (p.score > Human.score)) rank++;
            return rank;
        }

        public Participant FindByBody(Rigidbody rb) => byBody.TryGetValue(rb, out var c) ? c.participant : null;

        // ------------------------------------------------------------------ web page bridge

        /// <summary>Called by the web page (SendMessage) when it sees a touch device.</summary>
        public void SetTouchDevice(string on)
        {
            if (input != null) input.MarkTouchSeen();
            if (ui != null && on == "1") ui.SetTouch(true, false);
        }

        void ReportToPage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var sb = new StringBuilder(768);
            var ci = CultureInfo.InvariantCulture;
            var b = Human.car.Body; var p = b.position;
            sb.Append("{\"scene\":\"derby\",\"menu\":").Append(MenuOpen ? "true" : "false")
              .Append(",\"paused\":").Append(Paused ? "true" : "false")
              .Append(",\"state\":\"").Append(Match != null ? Match.State.ToString() : "Menu").Append('"')
              .Append(",\"mode\":\"").Append(rules.mode).Append('"')
              .Append(",\"cars\":").Append(Participants.Count)
              .Append(",\"clock\":").Append(Match != null ? Match.Clock.ToString("0.0", ci) : "0")
              .Append(",\"remaining\":").Append(Match != null ? Match.TimeRemaining.ToString("0.0", ci) : "0")
              .Append(",\"showdown\":").Append(Match != null ? Match.ShowdownRemaining.ToString("0.0", ci) : "0")
              .Append(",\"score\":").Append(Human.score).Append(",\"durability\":").Append((Human.DurabilityFraction * 100f).ToString("0", ci))
              .Append(",\"status\":\"").Append(Human.status).Append('"')
              .Append(",\"vehicle\":\"").Append(vehicles[playerVehicle].displayName).Append('"')
              .Append(",\"x\":").Append(p.x.ToString("0.0", ci)).Append(",\"z\":").Append(p.z.ToString("0.0", ci))
              .Append(",\"speedKmh\":").Append(PlayerSpeedKmh.ToString("0.0", ci))
              .Append(",\"throttle\":").Append(input.Throttle.ToString("0.0", ci)).Append(",\"steer\":").Append(input.Steer.ToString("0.0", ci))
              .Append(",\"winner\":\"").Append(Match != null && Match.State == MatchState.Results ? Match.ResultText : "").Append('"')
              .Append(",\"respawns\":").Append(Respawns).Append(",\"buildMs\":").Append(LastBuildMilliseconds.ToString("0", ci))
              .Append(",\"aiMs\":").Append(WorstAiMilliseconds.ToString("0.000", ci))
              .Append(",\"hitches\":").Append(WorstHitchCount)
              .Append(",\"touchControls\":").Append(ui != null && ui.TouchVisible ? "true" : "false")
              .Append(",\"fps\":").Append(fpsEma.ToString("0", ci))
              .Append(",\"table\":[");
            for (int i = 0; i < Participants.Count; i++)
            {
                var q = Participants[i];
                if (i > 0) sb.Append(',');
                int tgt = Targeting.TargetOf(q.id);
                sb.Append("{\"n\":\"").Append(q.name).Append("\",\"s\":").Append(q.score).Append(",\"d\":").Append((q.DurabilityFraction * 100f).ToString("0", ci))
                  .Append(",\"st\":\"").Append(q.status.ToString()[0]).Append("\",\"t\":").Append(tgt).Append('}');
            }
            sb.Append("]}");
            CarWreckBridge.Report(sb.ToString());
#endif
        }
    }
}
