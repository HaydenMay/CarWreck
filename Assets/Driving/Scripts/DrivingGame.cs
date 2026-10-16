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
        public int playerIndex = 0, targetIndex = 2;

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
        [Tooltip("Vehicle-vs-vehicle hits: the two cars share the energy the collision dissipates instead of each taking all of it.")]
        public bool shareEnergyBetweenVehicles = true;
        [Tooltip("Impacts whose deformation took longer than this (ms) are flagged as hitches.")]
        public float hitchMilliseconds = 40f;

        /// <summary>One built vehicle (player or target).</summary>
        public class Slot
        {
            public VehicleRig.Rig rig;
            public VehicleDefinition def;
            public ArcadeCarController controller;   // player only
            public System.Action<CollisionDamageReceiver.Report> handler;
            public Vector3 startPos;
            public Quaternion startRot;
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
            Player = Spawn(playerIndex, playerSpawn, playerRig, true);
            Target = Spawn(targetIndex, targetSpawn, targetRig, false);
            if (ui != null) { ui.input = input; ui.game = this; }
            if (chaseCamera != null) { chaseCamera.target = Player.Body; chaseCamera.Snap(); }
            if (Application.isPlaying) OpenMenu();
        }

        // ------------------------------------------------------------------ building vehicles

        Slot Spawn(int index, Transform spawn, VehicleRigSettings settings, bool isPlayer)
        {
            var def = vehicles[index];
            var rig = VehicleRig.Build(def, null, spawn.position, settings);
            // Lab-only debug views: not needed here.
            Kill(rig.footprintDebug);
            Kill(rig.colliderDebug);
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
            if (isPlayer)
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
            receiver.softness = profiles[index].damageSoftness;
            slot.handler = r => OnImpact(slot, isPlayer, r);
            receiver.ImpactRegistered += slot.handler;
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
            Player = Spawn(playerIndex, playerSpawn, playerRig, true);
            if (chaseCamera != null) { chaseCamera.target = Player.Body; if (MenuOpen) chaseCamera.SetMenuFocus(Player.rig.root.transform); }
            ClearHud();
        }

        public void SelectTarget(int index)
        {
            index = Mathf.Clamp(index, 0, vehicles.Length - 1);
            if (Target != null && index == targetIndex) return;
            Despawn(Target);
            targetIndex = index;
            Target = Spawn(targetIndex, targetSpawn, targetRig, false);
            if (chaseCamera != null && MenuOpen) chaseCamera.SetMenuFocus(Target.rig.root.transform);
            ClearHud();
        }

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

        void OnImpact(Slot who, bool isPlayer, CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
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
            if (ui != null) ui.SetImpact("Reset. Drive into the target vehicle or any obstacle.");
        }

        static void ResetSlot(Slot s)
        {
            if (s.Model != null)
            {
                s.Model.ResetAll();
                s.Model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            if (s.rig.hitbox != null) s.rig.hitbox.ApplyPending();
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

        void ReportToPage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var b = Player.Body;
            var p = b.position;
            var tp = Target.Body.position;
            var sb = new StringBuilder(512);
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\"scene\":\"drive\",\"menu\":").Append(MenuOpen ? "true" : "false")
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
