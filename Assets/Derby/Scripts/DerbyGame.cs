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
        public AttachmentMount mount;                // visual attachments; always present, empty when the loadout is empty
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

        [Header("Attachments (visual only in Stage 1)")]
        [Tooltip("Items, per-vehicle fits and the wreck material. Null = the feature is off and cars are built exactly as before.")]
        public AttachmentCatalog attachmentCatalog;

        [Header("Match")]
        public MatchRules rules = new MatchRules();
        [Tooltip("Cars in the match, the player's included. Default 5. A core setting, not a limit: the page accepts ?cars=N for scaling checks.")]
        public int participantCount = 5;
        public int playerVehicle = 0, playerPaint = 0;
        [Tooltip("AI quality for the next match (Casual / Standard / Competitive). Kept across rematches and visits to the menu.")]
        public AiDifficulty difficulty = AiDifficulty.Standard;
        [Tooltip("AI V3 (combat intelligence, difficulty, score awareness). Off = the previous AI, for before/after comparisons.")]
        public bool aiV3 = true;
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

        /// <summary>Page parameters (development / smoke tests, none needed to play): ?cars=N, ?mode=last|points, ?ai=casual|standard|competitive, ?time=seconds (Points Derby length), ?showdown=seconds, ?hp=multiplier (durability), ?scene=drive (the diagnostic driving arena).</summary>
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
                if (k == "ai") difficulty = v.StartsWith("cas") ? AiDifficulty.Casual : v.StartsWith("comp") ? AiDifficulty.Competitive : AiDifficulty.Standard;
                var ci = CultureInfo.InvariantCulture;
                if (k == "time" && float.TryParse(v, NumberStyles.Float, ci, out float secs)) rules.pointsDuration = Mathf.Clamp(secs, 5f, 900f);
                if (k == "showdown" && float.TryParse(v, NumberStyles.Float, ci, out float sd)) rules.showdownDuration = Mathf.Clamp(sd, 3f, 300f);
                if (k == "hp" && float.TryParse(v, NumberStyles.Float, ci, out float hp)) rules.durabilityEnergy *= Mathf.Clamp(hp, 0.05f, 10f);
            }
            return false;
        }

        // ------------------------------------------------------------------ building cars

        // ------------------------------------------------------------------ attachments (Stage 1: visual only)

        string VehicleKey(int vehicleIndex) => vehicles[Mathf.Clamp(vehicleIndex, 0, vehicles.Length - 1)].name;

        /// <summary>The player's saved loadout for the chosen vehicle. Read only while playing, so edit-mode validations never depend on a developer's saved preferences.</summary>
        void LoadHumanLoadout()
        {
            string key = VehicleKey(playerVehicle);
            Loadout l = new Loadout(key);
            if (attachmentCatalog != null && Application.isPlaying)
            {
                var result = LoadoutStore.Load(key, out l);
                if (result == LoadoutStore.ParseResult.Corrupt || result == LoadoutStore.ParseResult.UnknownVersion)
                    Debug.LogWarning($"[Attachments] Saved loadout for {key} could not be read ({result}); starting empty.");
            }
            Human.loadout = l;
        }

        /// <summary>The loadout currently selected for the player's vehicle (never null).</summary>
        public Loadout HumanLoadout { get { if (Human.loadout == null) LoadHumanLoadout(); return Human.loadout; } }

        public VehicleAttachmentFit HumanFit => attachmentCatalog != null ? attachmentCatalog.FitFor(VehicleKey(playerVehicle)) : null;

        /// <summary>What the validator says about the player's selection (the menu disables START while this is not valid).</summary>
        public LoadoutResult HumanLoadoutResult => LoadoutValidator.Validate(HumanLoadout, HumanFit, attachmentCatalog);

        public bool AttachmentsAvailable => attachmentCatalog != null && HumanFit != null && attachmentCatalog.items != null && attachmentCatalog.items.Length > 0;

        /// <summary>Selects (or, with an empty id, removes) the item in a slot, saves the build for this vehicle and refreshes the shown car.</summary>
        public void SetHumanLoadoutItem(AttachmentSlot slot, string itemId)
        {
            if (attachmentCatalog == null) return;
            HumanLoadout.Set(slot, itemId);
            if (Application.isPlaying) LoadoutStore.Save(HumanLoadout);
            RefreshHumanAttachments();
        }

        /// <summary>Empties every slot that has a problem of its own (unknown id, wrong slot, missing asset), keeping the sound ones. Overspend is fixed by the player.</summary>
        public void ClearInvalidHumanLoadoutSlots()
        {
            foreach (var issue in HumanLoadoutResult.issues)
                if (issue.slot >= 0) HumanLoadout.slotItemIds[issue.slot] = "";
            if (Application.isPlaying) LoadoutStore.Save(HumanLoadout);
            RefreshHumanAttachments();
        }

        void RefreshHumanAttachments()
        {
            if (Human.car != null && Human.car.mount != null) Human.car.mount.Apply(HumanLoadout);
        }

        void BuildHumanCar()
        {
            Human.vehicleIndex = playerVehicle; Human.paintIndex = playerPaint;
            LoadHumanLoadout();
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
            // Attachments are mounted AFTER Initialize on purpose: Initialize returns early once done and the derby never calls Rebuild, so the damage model never
            // discovers an attachment mesh as a deformable part. Moving this above Initialize would change deformation. (Visual only: no colliders, no mass.)
            car.mount = rig.root.AddComponent<AttachmentMount>();
            car.mount.Configure(attachmentCatalog, def.name);
            if (attachmentCatalog != null && p.loadout != null) car.mount.Apply(p.loadout);
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
                WireDriver(e);
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
            if (car.mount != null) car.mount.SetWrecked(true);
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

        public void SelectDifficulty(AiDifficulty d) { difficulty = d; ConfigureDrivers(); }

        /// <summary>Hands the current difficulty to every AI driver (they keep their personalities; a profile is re-applied so the derived numbers follow).</summary>
        void ConfigureDrivers()
        {
            foreach (var p in aiParticipants)
            {
                var e = p.car != null ? p.car.enemy : null;
                if (e == null) continue;
                WireDriver(e);
                e.SetProfile(e.profile);
            }
        }

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

        /// <summary>Each AI car gets the palette colour that is furthest from every colour already in the match (starting from the player's), so no two cars look alike for as long as the palette allows.</summary>
        void AssignAiPaints()
        {
            if (palette == null || palette.Count == 0) return;
            var chosen = new List<int> { playerPaint };
            foreach (var p in aiParticipants)
            {
                int best = 0; float bestD = -1f;
                for (int c = 0; c < palette.Count; c++)
                {
                    if (chosen.Contains(c) && chosen.Count < palette.Count) continue;
                    float d = float.MaxValue;
                    foreach (int k in chosen)
                    {
                        Color a = palette.Get(c).body, b = palette.Get(k).body;
                        d = Mathf.Min(d, Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b)));
                    }
                    if (d > bestD) { bestD = d; best = c; }
                }
                p.paintIndex = best; chosen.Add(best);
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
            if (attachmentCatalog != null && !HumanLoadoutResult.IsValid)   // the menu blocks START in this state; this is only the defensive path (e.g. ?-started or scripted matches)
                Debug.LogWarning("[Attachments] The player's loadout has problems; only its sound slots are mounted: " + string.Join("; ", HumanLoadoutResult.issues));
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
            AssignAiPaints();
            ConfigureDrivers();
            candidates = SpawnPlanner.Candidates(spawnPoints, arenaHalf, Participants.Count, StaticBlocked);
            var layout = SpawnPlanner.InitialLayout(candidates, Participants.Count, MatchesStarted * 7919);
            // shuffle who gets which spot (seeded, so a match is reproducible but the player's neighbours change from match to match)
            var order = new List<int>(); for (int i = 0; i < layout.Length; i++) order.Add(i);
            var r = new System.Random(MatchesStarted * 31 + 5);
            for (int i = order.Count - 1; i > 0; i--) { int j = r.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            for (int i = 0; i < Participants.Count; i++)
            {
                var p = Participants[i];
                var spot = layout[order[i % layout.Length]];
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
            Match.ShowdownPrepare += OnShowdownPrepare;
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
            if (Match != null) { Match.StateChanged -= OnStateChanged; Match.Wrecked -= OnWrecked; Match.Scored -= OnScored; Match.RespawnDue -= OnRespawnDue; Match.ShowdownStarted -= OnShowdownStarted; Match.ShowdownPrepare -= OnShowdownPrepare; Match.Abort(); }
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
                case MatchState.ShowdownTransition:
                case MatchState.ShowdownCountdown:
                    // nobody drives and no AI decision can start a movement until GO
                    foreach (var p in Participants) Idle(p.car);
                    break;
                case MatchState.FinalShowdown:
                {
                    // the duel: only the two finalists are driven again (everybody else is out of the match), each AI finalist starts with a clean driver
                    Targeting.Reset();
                    int k = 0;
                    foreach (var f in Match.Finalists)
                    {
                        var c = f != null ? f.car : null;
                        if (c == null || c.enemy == null) continue;
                        c.enemy.enabled = true;
                        c.enemy.startDelay = 0.3f + 0.12f * k++;
                        c.enemy.Restart();
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

        void OnShowdownStarted()
        {
            if (ui != null) ui.Banner("GO!", 1f);
            AddFeed("FINAL SHOWDOWN - GO");
        }

        /// <summary>
        /// The final two are known. Both cars get the same treatment the respawn path gives a wrecked car - full deformation reset, damage-aware colliders refitted, motion, handling and
        /// AI state cleared (<see cref="ResetCar"/>) - and are put on opposite sides of the arena on clear ground, facing each other. The match model has already ignored damage events
        /// since the transition began; nobody can drive until the countdown is over (see <see cref="OnStateChanged"/>).
        /// </summary>
        void OnShowdownPrepare(Participant a, Participant b)
        {
            foreach (var p in Participants) Idle(p.car);
            Rigidbody ba = a.car.Body, bb = b.car.Body;
            scratchPositions.Clear();
            foreach (var o in Participants) if (o != a && o != b && o.car != null) scratchPositions.Add(o.car.Body.position);
            bool found = SpawnPlanner.PickShowdown(arenaHalf, s => BlockedIgnoring(s, ba, bb), s => Clearance(s, scratchPositions), s => RunwayBlocked(s, ba, bb), MatchesStarted, out var sa, out var sb);
            ResetCar(a.car, sa);
            ResetCar(b.car, sb);
            ShowdownPlacedClear = found;
            spectated = null;
            if (chaseCamera != null) { UpdateCamera(); chaseCamera.Snap(); }
            AddFeed($"FINAL SHOWDOWN  {a.name} vs {b.name}");
            if (ui != null) ui.OnShowdownPrepared(a, b);
        }

        /// <summary>False when no pair of clear spots was found for the duel (the best of the rest was used): reported by the tests.</summary>
        public bool ShowdownPlacedClear { get; private set; } = true;

        bool BlockedIgnoring(SpawnPlanner.Spot s, Rigidbody ignoreA, Rigidbody ignoreB)
        {
            int n = Physics.OverlapBoxNonAlloc(s.position + Vector3.up * 1.3f, new Vector3(2.0f, 0.9f, 3.6f), overlapBuffer, Quaternion.Euler(0f, s.yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = overlapBuffer[i].attachedRigidbody;
                if (rb != null && (rb == ignoreA || rb == ignoreB)) continue;
                return true;
            }
            return false;
        }

        static float Clearance(SpawnPlanner.Spot s, List<Vector3> cars)
        {
            float d = 40f;
            for (int i = 0; i < cars.Count; i++) { Vector3 v = cars[i] - s.position; v.y = 0f; d = Mathf.Min(d, v.magnitude); }
            return d;
        }

        static readonly RaycastHit[] castBuffer = new RaycastHit[16];

        /// <summary>Something solid (a wall, a block, a hulk) within the first 22 m ahead of the spot: not enough room to accelerate.</summary>
        bool RunwayBlocked(SpawnPlanner.Spot s, Rigidbody ignoreA, Rigidbody ignoreB)
        {
            Vector3 dir = Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward;
            int n = Physics.SphereCastNonAlloc(s.position + Vector3.up * 1.3f, 1.1f, dir, castBuffer, 22f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = castBuffer[i].collider.attachedRigidbody;
                if (rb != null && (rb == ignoreA || rb == ignoreB)) continue;
                return true;
            }
            return false;
        }

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

        static readonly Collider[] overlapBuffer = new Collider[16];

        /// <summary>True when a car standing at the spot would overlap something (another car, a hulk, an obstacle). The ground is below the check box.</summary>
        bool SpotBlocked(SpawnPlanner.Spot s) => Blocked(s, true);

        /// <summary>The same, ignoring cars (used to choose the layout of a new match, when the old cars still stand where they were).</summary>
        bool StaticBlocked(SpawnPlanner.Spot s) => Blocked(s, false);

        bool Blocked(SpawnPlanner.Spot s, bool includeCars)
        {
            int n = Physics.OverlapBoxNonAlloc(s.position + Vector3.up * 1.3f, new Vector3(1.6f, 0.9f, 3.2f), overlapBuffer, Quaternion.Euler(0f, s.yaw, 0f), ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var rb = overlapBuffer[i].attachedRigidbody;
                if (includeCars || rb == null || !byBody.ContainsKey(rb)) return true;
            }
            return false;
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
            if (c.mount != null) c.mount.ResetVisuals();
            ApplyPaint(c, c.participant.paintIndex);
        }

        // ------------------------------------------------------------------ what the AI drivers may ask the game (breakaway)

        /// <summary>Gives a driver the game's view of the other cars (positions of living cars near it, how many are already breaking away, where they are heading).</summary>
        public void WireDriver(EnemyDriver e)
        {
            e.nearbyCars = NearbyCars;
            e.breakawayAllowed = BreakawayAllowed;
            e.destinationTaken = DestinationTaken;
            e.v3Enabled = aiV3; e.difficulty = difficulty;
            e.targetHealth = BodyHealth;
        }

        float BodyHealth(Rigidbody rb) => byBody.TryGetValue(rb, out var c) ? c.participant.DurabilityFraction : 1f;

        int NearbyCars(Rigidbody self, Vector3[] buffer, float radius)
        {
            int n = 0; float r2 = radius * radius; Vector3 p = self.position;
            foreach (var q in Participants)
            {
                if (q.car == null || !q.IsActive || q.car.Body == self) continue;
                Vector3 d = q.car.Body.position - p; d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                if (n < buffer.Length) buffer[n++] = q.car.Body.position;
            }
            return n;
        }

        /// <summary>At most about a third of the field breaks away at once, so a cluster never empties in one go.</summary>
        bool BreakawayAllowed(EnemyDriver self)
        {
            int away = 0;
            foreach (var q in Participants) if (q.car != null && q.car.enemy != null && q.car.enemy != self && q.car.enemy.Current == EnemyDriver.State.Breakaway) away++;
            return away < Mathf.Max(1, (Participants.Count + 1) / 3);
        }

        bool DestinationTaken(Vector3 p, EnemyDriver self)
        {
            foreach (var q in Participants)
            {
                var e = q.car != null ? q.car.enemy : null;
                if (e != null && e != self && e.Current == EnemyDriver.State.Breakaway && (e.BreakawayDestination - p).sqrMagnitude < 196f) return true;
            }
            return false;
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
        /// physics, damage receivers and collider refits - in the order the player loop runs them. <paramref name="readInput"/> also feeds the player's keyboard / touch input to the car, exactly as Update does.
        /// </summary>
        public void ManualStep(float dt, bool readInput = false)
        {
            if (Match != null) Frame(dt, readInput);
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
              .Append(",\"difficulty\":\"").Append(difficulty).Append("\",\"showdownDamage\":[").Append(Match != null && Match.ShowdownTriggered && Match.Finalists[0] != null ? (Match.Finalists[0].showdownDamage * 100f).ToString("0.0", ci) + "," + (Match.Finalists[1].showdownDamage * 100f).ToString("0.0", ci) : "").Append(']')
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
                  .Append(",\"st\":\"").Append(q.status.ToString()[0]).Append("\",\"t\":").Append(tgt).Append(",\"bk\":").Append(q.car != null && q.car.enemy != null ? q.car.enemy.Breakaways : 0).Append('}');
            }
            sb.Append("]}");
            CarWreckBridge.Report(sb.ToString());
#endif
        }
    }
}
