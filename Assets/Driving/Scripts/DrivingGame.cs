using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Vehicle;
using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Milestone 1 glue: wires input -> car -> camera, resets the car (position, damage, obstacles), shows the
    /// last impact (with the deformation cost, so hitches are visible) and reports state to the web page.
    /// The destruction itself is entirely the existing engine: this only configures it for driving.
    /// </summary>
    public class DrivingGame : MonoBehaviour
    {
        public ArcadeCarController controller;
        public VehicleDamageModel model;
        public CollisionDamageReceiver receiver;
        public VehicleHitbox hitbox;
        public Rigidbody body;
        public ChaseCamera chaseCamera;
        public DriveInput input;
        public DrivingUI ui;
        [Tooltip("Parent of the arena obstacles: dynamic ones are put back at their start pose on reset.")]
        public Transform obstacles;
        public Bounds bodyBounds;
        public float tyreContactY;
        [Tooltip("Impacts whose deformation took longer than this (ms) are flagged as hitches in the HUD and the log.")]
        public float hitchMilliseconds = 40f;

        struct StartPose { public Rigidbody rb; public Vector3 pos; public Quaternion rot; }
        readonly List<StartPose> startPoses = new List<StartPose>();
        Vector3 carStartPos;
        Quaternion carStartRot;
        float flippedTime;
        float reportTimer;
        int impactCount;
        float worstDeformMs;
        string lastImpact = "";
        // After a crash the camera swings round to show the damage until the player drives on.
        bool damageViewArmed;
        float damageViewYaw, sinceImpact, damageViewShown;

        public float SpeedKmh => controller != null ? controller.Speed * 3.6f : 0f;
        public bool Flipped => flippedTime > 1.2f;
        public int ImpactCount => impactCount;

        void Start() => Setup();

        bool isSetUp;

        /// <summary>One-time wiring (also called by the edit-mode validation, which has no Start).</summary>
        public void Setup()
        {
            if (isSetUp) return;
            isSetUp = true;
            if (model != null && !model.IsInitialized) model.Initialize();
            if (hitbox != null) hitbox.Setup();
            carStartPos = body.position;
            carStartRot = body.rotation;
            if (obstacles != null)
                foreach (var rb in obstacles.GetComponentsInChildren<Rigidbody>())
                    if (!rb.isKinematic) startPoses.Add(new StartPose { rb = rb, pos = rb.position, rot = rb.rotation });

            controller.Configure(bodyBounds, tyreContactY);
            if (receiver != null)
            {
                // The lab arms each impactor once per launch; driving keeps hitting the same walls, so every hit counts
                // (the receiver's own cooldown still stops one contact from being counted twice).
                receiver.oneImpactPerLaunch = false;
                receiver.logImpacts = true;
                receiver.ImpactRegistered += OnImpact;
            }
            if (ui != null) { ui.input = input; ui.game = this; }
            if (chaseCamera != null) { chaseCamera.target = body; chaseCamera.Snap(); }
        }

        void OnDestroy()
        {
            if (receiver != null) receiver.ImpactRegistered -= OnImpact;
        }

        void Update()
        {
            controller.Throttle = input.Throttle;
            controller.SteerInput = input.Steer;
            if (input.ConsumeReset()) ResetAll();
            UpdateDamageView();

            flippedTime = controller.IsFlipped && body.linearVelocity.magnitude < 2f ? flippedTime + Time.deltaTime : 0f;

            reportTimer -= Time.unscaledDeltaTime;
            if (reportTimer <= 0f) { reportTimer = 0.25f; ReportToPage(); }
        }

        void OnImpact(CollisionDamageReceiver.Report r)
        {
            if (!r.applied) return;
            impactCount++;
            worstDeformMs = Mathf.Max(worstDeformMs, r.deformMilliseconds);
            bool hitch = r.deformMilliseconds >= hitchMilliseconds;
            lastImpact = $"Crash: {r.closingSpeed * 3.6f:0} km/h into {r.other}, strength {r.strength * 100f:0}%, {r.region}\n" +
                         $"Deformation took {r.deformMilliseconds:0} ms{(hitch ? "  (visible hitch)" : "")}";
            if (ui != null) ui.SetImpact(lastImpact);
            // Camera position that looks at the struck side: the impact direction in car space (0 = nose, 90 = right, 180 = tail), a little to one side.
            Vector3 c = model.BodyBounds.center;
            float angle = Mathf.Atan2(r.localPoint.x - c.x, r.localPoint.z - c.z) * Mathf.Rad2Deg;
            damageViewYaw = angle + 180f + (angle >= 0f ? -32f : 32f);
            damageViewArmed = true; sinceImpact = 0f; damageViewShown = 0f;
            if (hitch) Debug.LogWarning($"[CarWreck] HITCH: deformation {r.deformMilliseconds:0.0} ms for impact with {r.other} at {r.closingSpeed:0.0} m/s");
        }

        void UpdateDamageView()
        {
            if (chaseCamera == null) return;
            sinceImpact += Time.deltaTime;
            bool still = body.linearVelocity.sqrMagnitude < 4f;
            bool driving = Mathf.Abs(input.Throttle) > 0.1f || Mathf.Abs(input.Steer) > 0.1f;
            bool show = damageViewArmed && still && sinceImpact < 12f && !(driving && damageViewShown > 1.5f);
            if (show && damageViewShown == 0f) damageViewYaw = chaseCamera.FindClearYawOffset(damageViewYaw);   // first frame of the view: pick a side that is not walled off
            if (show) damageViewShown += Time.deltaTime;
            else if (damageViewArmed && (!still && sinceImpact > 0.5f && damageViewShown > 0f || sinceImpact >= 12f || driving && damageViewShown > 1.5f)) damageViewArmed = false;
            chaseCamera.yawOffset = show ? damageViewYaw : 0f;
        }

        /// <summary>Back to the start: car pose, all damage, and any obstacles that were knocked around.</summary>
        public void ResetAll()
        {
            if (model != null)
            {
                model.ResetAll();
                model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            if (hitbox != null) hitbox.ApplyPending();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = carStartPos;
            body.rotation = carStartRot;
            body.transform.SetPositionAndRotation(carStartPos, carStartRot);
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
            if (receiver != null) receiver.ResetState();
            controller.ResetState();
            flippedTime = 0f;
            lastImpact = "";
            impactCount = 0;
            damageViewArmed = false;
            if (chaseCamera != null) chaseCamera.yawOffset = 0f;
            if (ui != null) ui.SetImpact("Car reset. Drive into something solid to see the damage.");
            if (chaseCamera != null) chaseCamera.Snap();
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
            var p = body.position;
            var v = body.linearVelocity;
            var sb = new StringBuilder(256);
            var ci = CultureInfo.InvariantCulture;
            sb.Append("{\"scene\":\"drive\",\"speedKmh\":").Append(SpeedKmh.ToString("0.0", ci))
              .Append(",\"fwdSpeed\":").Append(controller.ForwardSpeed.ToString("0.00", ci))
              .Append(",\"x\":").Append(p.x.ToString("0.00", ci)).Append(",\"y\":").Append(p.y.ToString("0.00", ci)).Append(",\"z\":").Append(p.z.ToString("0.00", ci))
              .Append(",\"yaw\":").Append(body.rotation.eulerAngles.y.ToString("0.0", ci))
              .Append(",\"throttle\":").Append(input.Throttle.ToString("0.0", ci)).Append(",\"steer\":").Append(input.Steer.ToString("0.0", ci))
              .Append(",\"grounded\":").Append(controller.IsGrounded ? "true" : "false")
              .Append(",\"touchControls\":").Append(ui != null && ui.TouchVisible ? "true" : "false")
              .Append(",\"touchBtns\":\"").Append(input.touchLeft ? 'L' : '-').Append(input.touchRight ? 'R' : '-').Append(input.touchGas ? 'G' : '-').Append(input.touchBrake ? 'B' : '-').Append('"')
              .Append(",\"src\":\"").Append(input.Sources).Append('"')
              .Append(",\"impacts\":").Append(impactCount)
              .Append(",\"worstDeformMs\":").Append(worstDeformMs.ToString("0.0", ci))
              .Append(",\"damaged\":").Append(model != null && model.IsDamaged ? "true" : "false")
              .Append(",\"fps\":").Append((1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("0", ci))
              .Append('}');
            CarWreckBridge.Report(sb.ToString());
#endif
        }
    }
}
