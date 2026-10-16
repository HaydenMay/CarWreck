using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Vehicle;
using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Milestone 2 glue. Builds the player's vehicle and a parked target vehicle at runtime from the chosen
    /// <see cref="VehicleDefinition"/>s (the same <see cref="VehicleRig"/> the Collision Lab uses), drives the player with the shared
    /// <see cref="ArcadeCarController"/> + a per-vehicle <see cref="DriveProfile"/>, resets everything, runs the selection menu and
    /// reports impacts (with deformation time per vehicle) to the HUD and the web page.
    /// Damage for both vehicles comes only from each vehicle's own <see cref="CollisionDamageReceiver"/>.
    /// </summary>
    public class DrivingGame : MonoBehaviour
    {
        [Header("Vehicles (same order everywhere: muscle car, hatchback, pickup)")]
        public VehicleDefinition[] vehicles;
        public DriveProfile[] profiles;
        public VehicleRigSettings playerRig = new VehicleRigSettings();
        [Tooltip("The parked target has more ground friction, so it stops sliding like a car with its brakes on.")]
        public VehicleRigSettings targetRig = new VehicleRigSettings();
        public Transform playerSpawn, targetSpawn;
        [Tooltip("Spawn points for the moving-target scenarios (same lane, east side of the arena, clear of obstacles).")]
        public Transform sameDirPlayerSpawn, sameDirTargetSpawn, headOnPlayerSpawn, headOnTargetSpawn;
        [Tooltip("Where the enemy starts in the ENEMY encounter (the player starts at playerSpawn).")]
        public Transform enemySpawn;
        public int playerIndex = 0, targetIndex = 2;

        public enum TargetMode { Parked, SameDirection, HeadOn, Enemy }
        [Header("Target scenario")]
        public TargetMode targetMode = TargetMode.Parked;
        [Tooltip("Speed the moving target drives at (km/h); also its speed at the moment of reset. Menu buttons change it in steps.")]
        public float targetSpeedKmh = 40f;
        public float minTargetSpeedKmh = 10f, maxTargetSpeedKmh = 80f, targetSpeedStepKmh = 10f;
        [Tooltip("Length of the straight test route (m). A target that reaches the end brakes to a stop.")]
        public float routeLength = 95f;

        [Header("Enemy encounter (ENEMY mode)")]
        [Tooltip("Speed the enemy tries to hold (km/h).")]
        public float enemySpeedKmh = 70f;
        [Tooltip("Seconds before the enemy starts moving after a start or reset.")]
        public float enemyStartDelay = 1.5f;
        [Tooltip("Seconds a stuck enemy reverses before pursuing again.")]
        public float enemyRecoverDuration = 1.3f;
        [Tooltip("Driver personalities (aggression, skill) for the enemy: Brawler, Hunter, Cautious. Empty = built-in presets with the same numbers. The menu's DRIVER button and enemyProfileIndex pick one.")]
        public DriverProfile[] driverProfiles;
        [Tooltip("Which driver profile the enemy uses (index into driverProfiles).")]
        public int enemyProfileIndex = 0;
        [Tooltip("The enemy looks for obstacles and routes round them. Off = the V1 enemy (only the stuck recovery).")]
        public bool enemyObstacleAwareness = true;

        [Header("Scene")]
        public ChaseCamera chaseCamera;
        public DriveInput input;
        public DrivingUI ui;
        [Tooltip("Parent of the arena obstacles: dynamic ones are put back at their start pose on reset.")]
        public Transform obstacles;
        [Header("Damage mapping (the receivers' own settings; the engine itself is untouched)")]
        [Tooltip("Collision energy (J) above which a hit leaves damage. Receiver default 1500.")]
        public float damageEnergyThreshold = 1500f;
        [Tooltip("Energy above the threshold for about 63% strength. The receiver default (60000) saturates at about 70 km/h, so every hard hit looked the same; a larger value spreads damage across the speed range so harder hits do visibly more.")]
        public float damageEnergyScale = 80000f;
        [Tooltip("Vehicle-vs-vehicle hits: the two cars share the energy the collision dissipates by momentum (the car that was attacking takes less, a stationary or retreating one takes more) instead of each taking all of it.")]
        public bool shareEnergyBetweenVehicles = true;
        [Tooltip("Severe crush: above this energy (J, this car's share) a hit keeps crushing deeper instead of stopping at full strength, " +
                 "so a highway head-on wrecks the front end while a car-park bump stays a dent. 0 = off.")]
        public float severeCrushEnergy = 100000f;
        [Tooltip("Energy (J) above severeCrushEnergy for 63% of the extra depth.")]
        public float severeCrushEnergyScale = 250000f;
        [Tooltip("Largest extra crush, as a fraction of the full-strength depth (2 = up to 3x as deep).")]
        public float severeCrushDepthGain = 2f;
        [Tooltip("Multiplies how far each car's metal can be crushed (1 = the damage profile's limits, tuned for the Collision Lab).")]
        public float crushCapacityScale = 1.8f;
        [Tooltip("Impacts whose deformation took longer than this (ms) are flagged as hitches.")]
        public float hitchMilliseconds = 40f;

        [Header("Damage-aware colliders (the Collision Lab's VehicleHitbox, refitted after hits)")]
        [Tooltip("Refit each car's physics shell to its crushed bodywork after a hit (VehicleHitbox.ApplyPending), so later contacts meet the damaged surface instead of the undamaged one. Off = the shell stays at its undamaged size.")]
        public bool refitColliders = true;
        [Tooltip("Seconds after a car's latest damaging hit before its shell is refitted, so the solver has finished resolving that hit. A further hit in the meantime restarts the wait.")]
        public float colliderSettleDelay = 0.2f;
        [Tooltip("A pending refit is applied after this long at the latest, however often the car is hit meanwhile.")]
        public float colliderMaxDelay = 1f;

        [Header("Progressive crushing (how each dent is shown; the damage solve and physics are unchanged)")]
        [Tooltip("Dents crush in over a short time instead of appearing in one frame. Off = instant dents (the arena before progressive crushing).")]
        public bool progressiveCrush = true;
        [Tooltip("Crush time (s) of a light dent.")]
        public float crushTimeLight = 0.06f;
        [Tooltip("Crush time (s) of the deepest dents.")]
        public float crushTimeHeavy = 0.24f;
        [Tooltip("Metal travel (m) that takes the heavy crush time; lighter dents take proportionally less (down to the light time).")]
        public float crushHeavyTravel = 0.6f;
        [Tooltip("How much later the metal deep behind the struck surface starts moving than the surface itself (fraction of the crush time).")]
        [Range(0f, 0.5f)] public float crushStagger = 0.3f;

        /// <summary>One built vehicle (player or target).</summary>
        public class Slot
        {
            public VehicleRig.Rig rig;
            public VehicleDefinition def;
            public ArcadeCarController controller;   // player, and the moving target
            public TargetDriver driver;              // moving target only
            public EnemyDriver enemy;                // enemy encounter only
            public System.Action<CollisionDamageReceiver.Report> handler;
            public Vector3 startPos;
            public Quaternion startRot;
            public float refitWait, refitAge;       // seconds since the latest / the first damaging hit still waiting for a collider refit
            public int refits;                      // collider refits applied since the car was built (diagnostics)
            public Rigidbody Body => rig.body;
            public VehicleDamageModel Model => rig.model;
            public CollisionDamageReceiver Receiver => rig.receiver;
        }

        public Slot Player { get; private set; }
        public Slot Target { get; private set; }
        public bool MenuOpen { get; private set; }

        struct StartPose { public Rigidbody rb; public Vector3 pos; public Quaternion rot; }
        readonly List<StartPose> startPoses = new List<StartPose>();
        float flippedTime, reportTimer, fpsEma = 60f;
        bool isSetUp;

        // After a crash the camera swings round to show the damage until the player drives on.
        bool damageViewArmed;
        float damageViewYaw, sinceImpact, damageViewShown;

        // Impact bookkeeping for the HUD / page: per vehicle, plus a rolling log.
        string lastPlayerLine = "", lastTargetLine = "";
        float lastPlayerMs, lastTargetMs, lastPlayerTime = -99f, lastTargetTime = -99f;
        float worstPlayerMs, worstTargetMs, worstPairMs;
        int playerImpacts, targetImpacts;
        readonly List<string> log = new List<string>();

        public ArcadeCarController Controller => Player?.controller;
        public Rigidbody Body => Player?.Body;
        public float SpeedKmh => Player != null ? Player.controller.Speed * 3.6f : 0f;
        public bool Flipped => flippedTime > 1.2f;
        public string PlayerName => Player != null ? Player.def.displayName : "";
        public string TargetName => Target != null ? Target.def.displayName : "";

        void Start() => Setup();

        /// <summary>One-time setup (also called by the edit-mode validation, which has no Start).</summary>
        public void Setup()
        {
            if (isSetUp) return;
            isSetUp = true;
            if (obstacles != null)
                foreach (var rb in obstacles.GetComponentsInChildren<Rigidbody>())
                    if (!rb.isKinematic) startPoses.Add(new StartPose { rb = rb, pos = rb.position, rot = rb.rotation });
            playerIndex = Mathf.Clamp(playerIndex, 0, vehicles.Length - 1);
            targetIndex = Mathf.Clamp(targetIndex, 0, vehicles.Length - 1);
            Player = Spawn(playerIndex, PlayerSpawnFor(targetMode), playerRig, true);
            Target = SpawnTarget();
            if (ui != null) { ui.input = input; ui.game = this; }
            if (chaseCamera != null) { chaseCamera.target = Player.Body; chaseCamera.Snap(); }
            if (Application.isPlaying) OpenMenu();
        }

        // ------------------------------------------------------------------ building vehicles

        Transform PlayerSpawnFor(TargetMode m) => m == TargetMode.SameDirection ? sameDirPlayerSpawn : m == TargetMode.HeadOn ? headOnPlayerSpawn : playerSpawn;
        Transform TargetSpawnFor(TargetMode m) => m == TargetMode.SameDirection ? sameDirTargetSpawn : m == TargetMode.HeadOn ? headOnTargetSpawn : m == TargetMode.Enemy ? enemySpawn : targetSpawn;

        /// <summary>The parked target (own high-friction material, no controller) or a driven one (same low-friction shell, controller and handling profile as the player's cars).</summary>
        Slot SpawnTarget() => Spawn(targetIndex, TargetSpawnFor(targetMode), targetMode == TargetMode.Parked ? targetRig : playerRig, false, targetMode != TargetMode.Parked);

        Slot Spawn(int index, Transform spawn, VehicleRigSettings settings, bool isPlayer, bool driven = false)
        {
            var def = vehicles[index];
            var rig = VehicleRig.Build(def, null, spawn.position, settings);
            // Lab-only debug views: not needed here.
            Kill(rig.footprintDebug);
            Kill(rig.colliderDebug);
            rig.model.impactCapacityScale = crushCapacityScale;
            rig.model.progressiveCrush = new DamageLab.Deformation.ProgressiveCrushSettings
            {
                enabled = progressiveCrush, minDuration = crushTimeLight, maxDuration = crushTimeHeavy, fullTravel = crushHeavyTravel, stagger = crushStagger,
            };
            rig.model.Initialize();
            rig.hitbox.Setup();
            var slot = new Slot { def = def, rig = rig };
            Quaternion rot = Quaternion.Euler(0f, spawn.eulerAngles.y, 0f);
            Vector3 pos = spawn.position + Vector3.up * rig.geometry.groundLift;
            rig.root.transform.SetPositionAndRotation(pos, rot);
            rig.body.position = pos;
            rig.body.rotation = rot;
            slot.startPos = pos;
            slot.startRot = rot;
            if (isPlayer || driven)
            {
                var c = rig.root.AddComponent<ArcadeCarController>();
                c.Apply(profiles[index]);
                c.Configure(rig.geometry.body, rig.geometry.wheels.min.y);
                slot.controller = c;
            }
            rig.root.name = (isPlayer ? "Player - " : "Target - ") + def.displayName;
            var receiver = rig.receiver;
            // The lab arms each impactor once per launch; here every distinct hit counts (the receiver's own 0.5 s
            // per-body cooldown still stops one contact from being counted twice).
            receiver.oneImpactPerLaunch = false;
            receiver.logImpacts = true;
            receiver.energyThreshold = damageEnergyThreshold;
            receiver.energyScale = damageEnergyScale;
            receiver.shareEnergyWithVehicles = shareEnergyBetweenVehicles;
            receiver.severeEnergy = severeCrushEnergy;
            receiver.severeEnergyScale = severeCrushEnergyScale;
            receiver.severeDepthGain = severeCrushDepthGain;
            slot.handler = r => OnImpact(slot, isPlayer, r);
            receiver.ImpactRegistered += slot.handler;
            if (driven && targetMode == TargetMode.Enemy)
            {
                // Enemy: an EnemyDriver feeds the car's own controller (steering, throttle, brake only). It starts at rest and pursues the player.
                var e = rig.root.AddComponent<EnemyDriver>();
                e.receiver = receiver;
                e.player = Player != null ? Player.Body : null;
                e.startDelay = enemyStartDelay; e.recoverDuration = enemyRecoverDuration; e.obstacleAwareness = enemyObstacleAwareness; e.measureCost = true; e.SetProfile(EnemyProfile);
                e.Begin(enemySpeedKmh / 3.6f);
                slot.enemy = e;
            }
            else if (driven)
            {
                // Moving target: a TargetDriver feeds the car's own controller (physics only; the transform is never written). It starts at speed.
                slot.driver = rig.root.AddComponent<TargetDriver>();
                slot.driver.receiver = receiver;
                slot.driver.Begin(targetSpeedKmh / 3.6f, routeLength);
                rig.body.linearVelocity = rot * Vector3.forward * (targetSpeedKmh / 3.6f);
            }
            Physics.SyncTransforms();
            return slot;
        }

        void Despawn(Slot s)
        {
            if (s == null) return;
            if (s.Receiver != null) s.Receiver.ImpactRegistered -= s.handler;
            if (s.Model != null) s.Model.Shutdown();
            s.rig.root.SetActive(false);        // leaves the physics scene immediately; Destroy only happens at the end of the frame
            Kill(s.rig.root);
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        public void SelectPlayer(int index)
        {
            index = Mathf.Clamp(index, 0, vehicles.Length - 1);
            if (Player != null && index == playerIndex) return;
            Despawn(Player);
            playerIndex = index;
            Player = Spawn(playerIndex, PlayerSpawnFor(targetMode), playerRig, true);
            if (Target != null && Target.enemy != null) Target.enemy.player = Player.Body;
            if (chaseCamera != null) { chaseCamera.target = Player.Body; if (MenuOpen) chaseCamera.SetMenuFocus(Player.rig.root.transform); }
            ClearHud();
        }

        public void SelectTarget(int index)
        {
            index = Mathf.Clamp(index, 0, vehicles.Length - 1);
            if (Target != null && index == targetIndex) return;
            Despawn(Target);
            targetIndex = index;
            Target = SpawnTarget();
            if (chaseCamera != null && MenuOpen) chaseCamera.SetMenuFocus(Target.rig.root.transform);
            ClearHud();
        }

        // ------------------------------------------------------------------ target scenario (stationary / same direction / head-on)

        /// <summary>0 = parked target, 1 = moving target driving the same way as the player, 2 = moving target driving towards the player, 3 = the ENEMY (pursues and rams the player).</summary>
        public void SetTargetMode(int mode)
        {
            var m = (TargetMode)Mathf.Clamp(mode, 0, 3);
            if (m == targetMode && Target != null) return;
            targetMode = m;
            Despawn(Target);
            var ps = PlayerSpawnFor(targetMode);
            Player.startPos = ps.position + Vector3.up * Player.rig.geometry.groundLift;
            Player.startRot = Quaternion.Euler(0f, ps.eulerAngles.y, 0f);
            ResetSlot(Player);
            Target = SpawnTarget();
            Physics.SyncTransforms();
            if (chaseCamera != null) { chaseCamera.Snap(); if (MenuOpen) chaseCamera.SetMenuFocus(Player.rig.root.transform); }
            ClearHud();
        }

        /// <summary>The speed the menu's -/+ buttons change: the moving target's speed, or the enemy's top speed in ENEMY mode.</summary>
        public float ActiveSpeedKmh => targetMode == TargetMode.Enemy ? enemySpeedKmh : targetSpeedKmh;

        public void SetTargetSpeedKmh(float kmh)
        {
            kmh = Mathf.Clamp(Mathf.Round(kmh), minTargetSpeedKmh, maxTargetSpeedKmh);
            if (targetMode == TargetMode.Enemy)
            {
                enemySpeedKmh = kmh;
                if (Target != null && Target.enemy != null) Target.enemy.desiredSpeed = kmh / 3.6f;
                return;
            }
            targetSpeedKmh = kmh;
            if (Target != null && Target.driver != null) Target.driver.desiredSpeed = targetSpeedKmh / 3.6f;
        }

        public void StepTargetSpeed(int direction) => SetTargetSpeedKmh(ActiveSpeedKmh + direction * targetSpeedStepKmh);

        // ------------------------------------------------------------------ enemy driver profiles

        DriverProfile[] presetProfiles;

        /// <summary>The assigned profiles, or three built-in presets (Brawler, Hunter, Cautious) when none are assigned.</summary>
        public DriverProfile[] DriverProfiles
        {
            get
            {
                if (driverProfiles != null && driverProfiles.Length > 0 && driverProfiles[0] != null) return driverProfiles;
                if (presetProfiles == null) { presetProfiles = new DriverProfile[DriverProfile.PresetCount]; for (int i = 0; i < presetProfiles.Length; i++) presetProfiles[i] = DriverProfile.CreatePreset(i); }
                return presetProfiles;
            }
        }

        public DriverProfile EnemyProfile { get { var all = DriverProfiles; return all[Mathf.Clamp(enemyProfileIndex, 0, all.Length - 1)]; } }
        public string EnemyProfileName => EnemyProfile.displayName;

        /// <summary>Menu DRIVER button: next profile (Brawler, Hunter, Cautious, ...). Takes effect at once on a live enemy; a reset restarts it with the new numbers.</summary>
        public void CycleEnemyProfile()
        {
            enemyProfileIndex = (Mathf.Clamp(enemyProfileIndex, 0, DriverProfiles.Length - 1) + 1) % DriverProfiles.Length;
            if (Target != null && Target.enemy != null) Target.enemy.SetProfile(EnemyProfile);
        }

        public string TargetModeLabel => targetMode == TargetMode.Parked ? "parked" : targetMode == TargetMode.Enemy ? $"ENEMY, top speed {enemySpeedKmh:0} km/h" : targetMode == TargetMode.SameDirection ? $"same direction, {targetSpeedKmh:0} km/h" : $"head-on, {targetSpeedKmh:0} km/h";

        // ------------------------------------------------------------------ menu / pause

        public void OpenMenu()
        {
            MenuOpen = true;
            Time.timeScale = 0f;
            if (chaseCamera != null) chaseCamera.SetMenuFocus(Player.rig.root.transform);
            if (ui != null) ui.ShowMenu(true);
        }

        /// <summary>DRIVE: close the menu and start with everything reset (so a changed car or target starts clean).</summary>
        public void CloseMenu()
        {
            MenuOpen = false;
            Time.timeScale = 1f;
            if (chaseCamera != null) chaseCamera.ClearMenu();
            if (ui != null) ui.ShowMenu(false);
            ResetAll();
        }

        // ------------------------------------------------------------------ per-frame

        void Update()
        {
            if (Player == null) return;
            if (input.ConsumeMenu()) { if (MenuOpen) CloseMenu(); else OpenMenu(); }
            var c = Player.controller;
            if (MenuOpen)
            {
                input.ConsumeReset();
                c.Throttle = 0f; c.SteerInput = 0f;
            }
            else
            {
                c.Throttle = input.Throttle;
                c.SteerInput = input.Steer;
                if (input.ConsumeReset()) ResetAll();
                UpdateDamageView();
                flippedTime = c.IsFlipped && Player.Body.linearVelocity.magnitude < 2f ? flippedTime + Time.deltaTime : 0f;
            }
            fpsEma = Mathf.Lerp(fpsEma, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            reportTimer -= Time.unscaledDeltaTime;
            if (reportTimer <= 0f) { reportTimer = 0.25f; ReportToPage(); }
        }

        // FixedUpdate runs before each physics step, so a collider change made here is never made while the solver is working.
        void FixedUpdate() => AfterPhysicsStep(Time.fixedDeltaTime);

        /// <summary>
        /// The arena's equivalent of the Collision Lab's safe point (CollisionLabController.AfterPhysicsStep): once a car has been quiet
        /// for <see cref="colliderSettleDelay"/> after a damaging hit (or <see cref="colliderMaxDelay"/> after the first of a burst), its shell is
        /// refitted to the deformed mesh. With progressive crushing the refit also waits until the dent has finished crushing in, so the
        /// shell never retracts ahead of the visible metal (the hard <see cref="colliderMaxDelay"/> cap still applies).
        /// Called by FixedUpdate, and by the edit-mode validation after each Physics.Simulate step.
        /// </summary>
        public void AfterPhysicsStep(float dt)
        {
            if (!refitColliders) return;
            RefitIfDue(Player, dt);
            RefitIfDue(Target, dt);
        }

        void RefitIfDue(Slot s, float dt)
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

        void OnImpact(Slot who, bool isPlayer, CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
            who.refitWait = 0f;      // a new hit restarts the settle wait (the hitbox itself was marked pending by the model)
            float now = Time.unscaledTime;
            bool hitch = r.deformMilliseconds >= hitchMilliseconds;
            string line = $"{r.region} {r.closingSpeed * 3.6f:0} km/h into {r.other}: strength {r.strength * 100f:0}%, deformation {r.deformMilliseconds:0} ms{(hitch ? " (hitch)" : "")}";
            if (isPlayer)
            {
                playerImpacts++; lastPlayerMs = r.deformMilliseconds; lastPlayerTime = now; worstPlayerMs = Mathf.Max(worstPlayerMs, r.deformMilliseconds);
                lastPlayerLine = "You: " + line;
            }
            else
            {
                targetImpacts++; lastTargetMs = r.deformMilliseconds; lastTargetTime = now; worstTargetMs = Mathf.Max(worstTargetMs, r.deformMilliseconds);
                lastTargetLine = "Target: " + line;
            }
            string pair = "";
            if (Mathf.Abs(lastPlayerTime - lastTargetTime) < 0.3f)
            {
                float total = lastPlayerMs + lastTargetMs;
                worstPairMs = Mathf.Max(worstPairMs, total);
                pair = $"\nBoth cars deformed this frame: {total:0} ms total";
            }
            if (ui != null) ui.SetImpact(lastPlayerLine + (lastTargetLine.Length > 0 ? "\n" + lastTargetLine : "") + pair);
            log.Add($"{{\"w\":\"{(isPlayer ? "P" : "T")}\",\"v\":\"{who.def.displayName}\",\"o\":\"{r.other}\",\"kmh\":{(r.closingSpeed * 3.6f).ToString("0", CultureInfo.InvariantCulture)},\"str\":{(r.strength * 100f).ToString("0", CultureInfo.InvariantCulture)},\"ms\":{r.deformMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)}}}");
            if (log.Count > 10) log.RemoveAt(0);

            if (isPlayer)
            {
                // Camera position that looks at the struck side: the impact direction in car space (0 = nose, 90 = right, 180 = tail), a little to one side.
                Vector3 c = who.Model.BodyBounds.center;
                float angle = Mathf.Atan2(r.localPoint.x - c.x, r.localPoint.z - c.z) * Mathf.Rad2Deg;
                damageViewYaw = angle + 180f + (angle >= 0f ? -32f : 32f);
                damageViewArmed = true; sinceImpact = 0f; damageViewShown = 0f;
            }
            if (hitch) Debug.LogWarning($"[CarWreck] HITCH: {(isPlayer ? "player" : "target")} {who.def.displayName} deformation {r.deformMilliseconds:0.0} ms (impact with {r.other} at {r.closingSpeed:0.0} m/s)");
        }

        void UpdateDamageView()
        {
            if (chaseCamera == null) return;
            sinceImpact += Time.deltaTime;
            if (Time.unscaledTime - chaseCamera.LastLookTime < 0.5f) damageViewArmed = false;   // the player is steering the camera themself
            bool still = Player.Body.linearVelocity.sqrMagnitude < 4f;
            bool driving = Mathf.Abs(input.Throttle) > 0.1f || Mathf.Abs(input.Steer) > 0.1f;
            bool show = damageViewArmed && still && sinceImpact < 12f && !(driving && damageViewShown > 1.5f);
            if (show && damageViewShown == 0f) damageViewYaw = chaseCamera.FindClearYawOffset(damageViewYaw);   // first frame of the view: pick a side that is not walled off
            if (show) damageViewShown += Time.deltaTime;
            else if (damageViewArmed && (!still && sinceImpact > 0.5f && damageViewShown > 0f || sinceImpact >= 12f || driving && damageViewShown > 1.5f)) damageViewArmed = false;
            chaseCamera.yawOffset = show ? damageViewYaw : 0f;
        }

        void ClearHud()
        {
            lastPlayerLine = lastTargetLine = "";
            lastPlayerTime = lastTargetTime = -99f;
            if (ui != null) ui.SetImpact("");
        }

        /// <summary>Everything back to the start: both vehicles (pose, velocity, all damage) and any obstacles that were knocked around.</summary>
        public void ResetAll()
        {
            ResetSlot(Player);
            ResetSlot(Target);
            if (Target.enemy != null) Target.enemy.Restart();   // enemy: back to Waiting, counters cleared (ResetSlot already zeroed its velocity)
            if (Target.driver != null)
            {
                // Moving scenario: the target starts each run already at its test speed (an initial velocity, then its own controller drives).
                Target.Body.linearVelocity = Target.startRot * Vector3.forward * (targetSpeedKmh / 3.6f);
                Target.driver.desiredSpeed = targetSpeedKmh / 3.6f;
                Target.driver.Restart();
            }
            foreach (var p in startPoses)
            {
                if (p.rb == null) continue;
                p.rb.linearVelocity = Vector3.zero;
                p.rb.angularVelocity = Vector3.zero;
                p.rb.position = p.pos;
                p.rb.rotation = p.rot;
                p.rb.transform.SetPositionAndRotation(p.pos, p.rot);
            }
            Physics.SyncTransforms();
            Player.Receiver.ResetState();
            Target.Receiver.ResetState();
            Player.controller.ResetState();
            flippedTime = 0f;
            playerImpacts = targetImpacts = 0;
            log.Clear();
            damageViewArmed = false;
            if (chaseCamera != null) { chaseCamera.yawOffset = 0f; chaseCamera.Snap(); }
            ClearHud();
            if (ui != null) ui.SetImpact($"Reset. Target: {TargetName}, {TargetModeLabel}.");
        }

        static void ResetSlot(Slot s)
        {
            if (s.Model != null)
            {
                s.Model.ResetAll();
                s.Model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            if (s.rig.hitbox != null) s.rig.hitbox.ApplyPending();
            s.refitWait = s.refitAge = 0f;
            var rb = s.Body;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = s.startPos;
            rb.rotation = s.startRot;
            rb.transform.SetPositionAndRotation(s.startPos, s.startRot);
        }

        // ------------------------------------------------------------------ web page bridge

        /// <summary>Called by the web page (SendMessage) when it sees a touch device.</summary>
        public void SetTouchDevice(string on)
        {
            if (input != null) input.MarkTouchSeen();
            if (ui != null && on == "1") ui.SetTouch(true, false);
        }

        /// <summary>Largest distance (m) any face of the car's damage-aware shell has retracted from its undamaged position (page report only).</summary>
        static float ShellRetraction(Slot s)
        {
            float m = 0f;
            if (s == null || s.rig == null || s.rig.hitbox == null || s.rig.hitbox.Faces == null) return m;
            foreach (var f in s.rig.hitbox.Faces) m = Mathf.Max(m, f.original - f.current);
            return float.IsNaN(m) ? 0f : m;
        }

        void ReportToPage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var b = Player.Body;
            var p = b.position;
            var tp = Target.Body.position;
            var sb = new StringBuilder(512);
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\"scene\":\"drive\",\"menu\":").Append(MenuOpen ? "true" : "false")
              .Append(",\"mode\":").Append((int)targetMode).Append(",\"tspeedSet\":").Append(targetSpeedKmh.ToString("0", ci)).Append(",\"tdrv\":\"").Append(Target.driver != null ? Target.driver.Current.ToString() : "-").Append('"')
              .Append(",\"enemy\":\"").Append(Target.enemy != null ? Target.enemy.Current.ToString() : "-").Append("\",\"edist\":").Append(Target.enemy != null ? Target.enemy.DistanceToPlayer.ToString("0", ci) : "0").Append(",\"ehits\":").Append(Target.enemy != null ? Target.enemy.HitsLanded : 0).Append(",\"estuck\":").Append(Target.enemy != null ? Target.enemy.RecoveriesTriggered : 0)
              .Append(",\"eprof\":\"").Append(Target.enemy != null ? Target.enemy.ProfileName : "-").Append("\",\"echarges\":").Append(Target.enemy != null ? Target.enemy.AttacksCommitted : 0).Append(",\"erepos\":").Append(Target.enemy != null ? Target.enemy.Repositions : 0).Append(",\"edetours\":").Append(Target.enemy != null ? Target.enemy.DetoursPlanned : 0).Append(",\"eobst\":").Append(Target.enemy != null ? Target.enemy.ObstacleHits : 0).Append(",\"edrvMs\":").Append(Target.enemy != null ? Target.enemy.WorstDriveMilliseconds.ToString("0.000", ci) : "0")
              .Append(",\"prefits\":").Append(Player.refits).Append(",\"trefits\":").Append(Target.refits).Append(",\"pshell\":").Append(ShellRetraction(Player).ToString("0.000", ci)).Append(",\"tshell\":").Append(ShellRetraction(Target).ToString("0.000", ci))
              .Append(",\"pcrushes\":").Append(Player.Model.CrushStarts).Append(",\"tcrushes\":").Append(Target.Model.CrushStarts)
              .Append(",\"pcrushMs\":").Append(Player.Model.WorstCrushFrameMilliseconds.ToString("0.00", ci)).Append(",\"tcrushMs\":").Append(Target.Model.WorstCrushFrameMilliseconds.ToString("0.00", ci))
              .Append(",\"vehicle\":\"").Append(PlayerName).Append("\",\"target\":\"").Append(TargetName).Append('"')
              .Append(",\"speedKmh\":").Append(SpeedKmh.ToString("0.0", ci))
              .Append(",\"fwdKmh\":").Append((Player.controller.ForwardSpeed * 3.6f).ToString("0.0", ci))
              .Append(",\"x\":").Append(p.x.ToString("0.00", ci)).Append(",\"y\":").Append(p.y.ToString("0.00", ci)).Append(",\"z\":").Append(p.z.ToString("0.00", ci))
              .Append(",\"yaw\":").Append(b.rotation.eulerAngles.y.ToString("0.0", ci))
              .Append(",\"tx\":").Append(tp.x.ToString("0.00", ci)).Append(",\"tz\":").Append(tp.z.ToString("0.00", ci)).Append(",\"tyaw\":").Append(Target.Body.rotation.eulerAngles.y.ToString("0.0", ci))
              .Append(",\"look\":").Append(chaseCamera != null ? chaseCamera.UserYaw.ToString("0.0", ci) : "0")
              .Append(",\"tspeed\":").Append((Target.Body.linearVelocity.magnitude * 3.6f).ToString("0.0", ci))
              .Append(",\"throttle\":").Append(input.Throttle.ToString("0.0", ci)).Append(",\"steer\":").Append(input.Steer.ToString("0.0", ci))
              .Append(",\"grounded\":").Append(Player.controller.IsGrounded ? "true" : "false")
              .Append(",\"touchControls\":").Append(ui != null && ui.TouchVisible ? "true" : "false")
              .Append(",\"touchBtns\":\"").Append(input.touchLeft ? 'L' : '-').Append(input.touchRight ? 'R' : '-').Append(input.touchGas ? 'G' : '-').Append(input.touchBrake ? 'B' : '-').Append('"')
              .Append(",\"src\":\"").Append(input.Sources).Append('"')
              .Append(",\"impacts\":").Append(playerImpacts).Append(",\"targetImpacts\":").Append(targetImpacts)
              .Append(",\"worstPlayerMs\":").Append(worstPlayerMs.ToString("0.0", ci)).Append(",\"worstTargetMs\":").Append(worstTargetMs.ToString("0.0", ci)).Append(",\"worstPairMs\":").Append(worstPairMs.ToString("0.0", ci))
              .Append(",\"damaged\":").Append(Player.Model.IsDamaged ? "true" : "false").Append(",\"targetDamaged\":").Append(Target.Model.IsDamaged ? "true" : "false")
              .Append(",\"fps\":").Append(fpsEma.ToString("0", ci))
              .Append(",\"log\":[").Append(string.Join(",", log)).Append("]}");
            CarWreckBridge.Report(sb.ToString());
#endif
        }
    }
}
