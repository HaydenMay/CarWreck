using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.CollisionTest
{
    public enum ImpactDirection { Front, Rear, LeftSide, RightSide, FrontCornerAngled, RearCornerAngled }

    /// <summary>
    /// Collision Lab: places the test sled for the chosen direction, launches it with a real Rigidbody velocity
    /// (physics does the rest) and restores everything on Reset. Damage only comes from
    /// <see cref="CollisionDamageReceiver"/>, i.e. from the actual contact.
    /// </summary>
    public class CollisionLabController : MonoBehaviour
    {
        [Header("Scene")]
        public Rigidbody car;
        public VehicleDamageModel model;
        public CollisionDamageReceiver receiver;
        public Rigidbody sled;
        public InspectionCameraController cameraController;

        [Header("Test")]
        [Range(0f, 30f)] public float speed = 10f;          // m/s
        [Range(100f, 4000f)] public float mass = 1500f;     // kg
        public ImpactDirection direction = ImpactDirection.Front;
        [Tooltip("Gap (m) between the sled's nose and the car's surface before launch.")]
        public float startGap = 5f;
        [Tooltip("Height (m, car space) of the sled's centre line.")]
        public float sledHeight = 0.7f;
        [Tooltip("Impactor width (m): the full-width sled or a narrow block of the same length and height.")]
        public bool narrowImpactor;
        public float wideWidth = 1.9f;
        public float narrowWidth = 0.5f;
        public ImpactFootprintDebug footprintDebug;
        [Tooltip("Sideways shift (m) of the aim point: towards the car's right for front/rear/corner hits, towards the front for side hits.")]
        [Range(-1.5f, 1.5f)] public float aimOffset;
        [Tooltip("Freeze the car in place (it still collides and still takes damage). Off = free physics.")]
        public bool anchored = true;
        public VehicleHitbox hitbox;
        public PhysicsHitboxDebug colliderDebug;

        [Header("Vehicles")]
        [Tooltip("Vehicles the lab can test; each is built from its definition by VehicleRig.")]
        public VehicleDefinition[] vehicles = new VehicleDefinition[0];
        public int vehicleIndex;
        public VehicleRigSettings rigSettings = new VehicleRigSettings();
        [Tooltip("Ground point the vehicles stand on.")]
        public Vector3 vehicleBase = Vector3.zero;
        public Button[] vehicleButtons;
        public Text vehicleInfoText;

        [Header("UI")]
        public Slider speedSlider;
        public InputField speedField;
        public Text speedMphText;
        public Slider massSlider;
        public InputField massField;
        public Button[] directionButtons;   // in ImpactDirection order
        public Button[] impactorButtons;    // wide, narrow
        public Toggle footprintToggle;
        public Button launchButton;
        public Button resetButton;          // RESET ALL
        public Button resetBlockButton;
        public Button repairButton;
        public Toggle anchorToggle;
        public Toggle collidersToggle;
        public Slider offsetSlider;
        public InputField offsetField;
        public Text diagnosticsText;
        public Text statusText;
        public Color selectedColor = new Color(0.95f, 0.48f, 0.1f);
        public Color unselectedColor = new Color(0.2f, 0.215f, 0.235f);

        Vector3 carStartPos;
        Quaternion carStartRot;
        bool launched;
        float launchTime;

        public bool Launched => launched;

        bool hasStartPose;

        void Awake() => Setup();

        /// <summary>Record the car's resting pose once (also called by the edit-mode validation).</summary>
        public void Setup()
        {
            if (hasStartPose || car == null) return;
            carStartPos = car.transform.position;
            carStartRot = car.transform.rotation;
            if (cameraController != null) homePivot = cameraController.pivot;
            hasStartPose = true;
        }

        Vector3 homePivot;

        void LateUpdate()
        {
            // The orbit pivot follows the car (in free mode a heavy hit can shove it many metres).
            if (cameraController == null || !Application.isPlaying) return;
            Vector3 target = car.position + (homePivot - carStartPos);
            cameraController.pivot = Vector3.Lerp(cameraController.pivot, target, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
        }

        void Start()
        {
            // Build the selected vehicle if the scene's car is not it (or there is none).
            if (vehicles != null && vehicles.Length > 0)
            {
                vehicleIndex = Mathf.Clamp(vehicleIndex, 0, vehicles.Length - 1);
                if (car == null || model == null || model.definition != vehicles[vehicleIndex]) SelectVehicle(vehicleIndex);
            }
            if (model != null && !model.IsInitialized) model.Initialize();
            if (hitbox != null) hitbox.Setup();
            CaptureGeometry();
            BindUI();
            SetAnchored(anchored);
            ResetAll();
            SubscribeVehicle();
            RefreshVehicleInfo();
        }

        bool subscribedVehicle;
        void SubscribeVehicle()
        {
            if (subscribedVehicle) return;
            if (receiver != null) receiver.ImpactRegistered += OnImpactRegistered;
            if (hitbox != null) hitbox.Changed += RefreshDiagnostics;
            subscribedVehicle = true;
        }

        void UnsubscribeVehicle()
        {
            if (!subscribedVehicle) return;
            if (receiver != null) receiver.ImpactRegistered -= OnImpactRegistered;
            if (hitbox != null) hitbox.Changed -= RefreshDiagnostics;
            subscribedVehicle = false;
        }

        void OnImpactRegistered(CollisionDamageReceiver.Report r) => RefreshDiagnostics();

        // ------------------------------------------------------------------ vehicles

        VehicleGeometry geometry;
        bool hasGeometry;
        /// <summary>Geometry of the current vehicle (car space).</summary>
        public VehicleGeometry Geometry { get { CaptureGeometry(); return geometry; } }
        public VehicleDefinition CurrentVehicle => model != null ? model.definition : null;
        /// <summary>Warnings from building and initialising the current vehicle (classification, missing roles...).</summary>
        public readonly System.Collections.Generic.List<string> VehicleWarnings = new System.Collections.Generic.List<string>();

        void CaptureGeometry()
        {
            if (hasGeometry || car == null) return;
            var def = model != null ? model.definition : null;
            if (def != null)
            {
                // Measure a model copy at the origin (the scene car may already be placed and deformed).
                var tmp = Instantiate(def.model);
                try { geometry = VehicleRig.Measure(def, tmp); }
                finally { if (Application.isPlaying) Destroy(tmp); else DestroyImmediate(tmp); }
                sledHeight = VehicleRig.ImpactHeight(def, geometry);
            }
            else if (model != null)
            {
                geometry = new VehicleGeometry { body = model.BodyBounds, cabin = model.BodyBounds, wheels = model.BodyBounds };
            }
            hasGeometry = true;
        }

        /// <summary>Replace the current car with vehicle <paramref name="index"/> built from its definition, undamaged and parked.</summary>
        public void SelectVehicle(int index)
        {
            if (vehicles == null || vehicles.Length == 0) return;
            index = Mathf.Clamp(index, 0, vehicles.Length - 1);
            UnsubscribeVehicle();
            if (car != null)
            {
                var old = car.gameObject;
                if (model != null) model.Shutdown();
                if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
            }
            var rig = VehicleRig.Build(vehicles[index], transform.parent, vehicleBase, rigSettings);
            AdoptRig(rig, index);
            model.Initialize();
            hitbox.Setup();
            VehicleWarnings.Clear();
            VehicleWarnings.AddRange(rig.warnings);
            VehicleWarnings.AddRange(model.Warnings);
            carStartPos = car.transform.position;
            carStartRot = car.transform.rotation;
            hasStartPose = true;
            if (cameraController != null && homePivot == Vector3.zero) homePivot = cameraController.pivot;
            SetAnchored(anchored);
            ResetAll();
            SubscribeVehicle();
            if (Application.isPlaying) { footprintDebug.Rebuild(); colliderDebug.Rebuild(); }
            RefreshVehicleInfo();
            RefreshDirectionButtons();
            SetStatus($"{rig.definition.displayName} ready");
        }

        /// <summary>Take over a freshly built rig as the lab's car (no initialisation: the scene builder uses this too).</summary>
        public void AdoptRig(VehicleRig.Rig rig, int index)
        {
            vehicleIndex = index;
            car = rig.body; model = rig.model; receiver = rig.receiver; hitbox = rig.hitbox;
            footprintDebug = rig.footprintDebug; colliderDebug = rig.colliderDebug;
            geometry = rig.geometry; hasGeometry = true;
            sledHeight = VehicleRig.ImpactHeight(rig.definition, geometry);
            if (footprintToggle != null) footprintDebug.show = footprintToggle.isOn;
            if (collidersToggle != null) colliderDebug.show = collidersToggle.isOn;
        }

        public void RefreshVehicleInfo()
        {
            if (vehicleButtons != null)
                for (int i = 0; i < vehicleButtons.Length; i++)
                    if (vehicleButtons[i] != null && vehicleButtons[i].targetGraphic != null)
                        vehicleButtons[i].targetGraphic.color = i == vehicleIndex ? selectedColor : unselectedColor;
            if (vehicleInfoText == null || model == null) return;
            if (model.IsInitialized && VehicleWarnings.Count == 0 && model.Warnings.Count > 0) VehicleWarnings.AddRange(model.Warnings);
            var g = Geometry;
            int rigidCount = g.wheelCenters != null ? g.wheelCenters.Length : 0;
            int colliders = car != null ? car.GetComponentsInChildren<Collider>().Length : 0;
            string name = CurrentVehicle != null ? CurrentVehicle.displayName : car.name;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{name}   {g.body.size.x:0.00} × {g.body.size.z:0.00} m body, {car.mass:0} kg\n");
            sb.Append($"{model.PartCount} deforming parts · {model.DeformableVertices:N0} vertices · {rigidCount} wheels\n");
            sb.Append($"Physics: {colliders} boxes ({(hitbox != null ? hitbox.regions.Length : 0)} adaptive) · impact height {sledHeight:0.00} m\n");
            if (VehicleWarnings.Count == 0) sb.Append("Validation: no warnings");
            else
            {
                sb.Append($"Validation: {VehicleWarnings.Count} warning(s)");
                for (int i = 0; i < Mathf.Min(3, VehicleWarnings.Count); i++) sb.Append("\n• ").Append(VehicleWarnings[i]);
                if (VehicleWarnings.Count > 3) sb.Append($"\n• … {VehicleWarnings.Count - 3} more (see console)");
            }
            vehicleInfoText.text = sb.ToString();
        }

        // ------------------------------------------------------------------ test control

        /// <summary>Park the block for the current settings and give it its launch velocity. Physics does the rest.
        /// The car is NOT repaired: damage from earlier launches stays and new hits add to it.</summary>
        public void Launch()
        {
            ResetBlock();
            sled.mass = mass;
            sled.isKinematic = false;
            sled.linearVelocity = sled.transform.forward * speed;
            sled.angularVelocity = Vector3.zero;
            launched = true;
            launchTime = Time.time;
            SetStatus($"Launched: {speed:0} m/s, {mass:0} kg, {Label(direction)}, {(narrowImpactor ? "narrow block" : "wide sled")}");
        }

        /// <summary>Only the block: back to its starting pose for the current settings, at rest, with the collision
        /// cooldown cleared so the next launch registers. The car's damage (visual and physical) is kept.</summary>
        public void ResetBlock()
        {
            if (hitbox != null) hitbox.ApplyPending();     // collider changes are in effect before the next launch
            PlaceSled();
            Physics.SyncTransforms();
            if (receiver != null) receiver.ClearCooldowns();
            launched = false;
        }

        /// <summary>Remove all damage: original mesh and original physical hitbox. Settings are kept.</summary>
        public void RepairCar()
        {
            if (model != null)
            {
                model.ResetAll();
                model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            if (hitbox != null) hitbox.ApplyPending();
            // The restored hitbox is larger than the crushed one; a block resting in the dent would now overlap it.
            if (SledOverlapsCar()) ResetBlock();
            if (receiver != null) receiver.ResetState();
            RefreshDiagnostics();
            SetStatus("Car repaired");
        }

        /// <summary>The whole experiment: repair, car back to its start pose at rest, block parked.</summary>
        public void ResetAll()
        {
            if (model != null)
            {
                model.ResetAll();
                model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            if (hitbox != null) hitbox.ApplyPending();
            car.linearVelocity = Vector3.zero;
            car.angularVelocity = Vector3.zero;
            car.position = carStartPos;
            car.rotation = carStartRot;
            car.transform.SetPositionAndRotation(carStartPos, carStartRot);
            ResetBlock();
            if (receiver != null) receiver.ResetState();
            RefreshDiagnostics();
            SetStatus("Ready");
        }

        /// <summary>Anchored: the car keeps its pose (all position/rotation axes frozen on the dynamic Rigidbody, so it
        /// still collides with continuous detection and still receives collision events). Off: free physics.</summary>
        public void SetAnchored(bool on)
        {
            anchored = on;
            car.constraints = on ? RigidbodyConstraints.FreezeAll : RigidbodyConstraints.None;
            if (on) { car.linearVelocity = Vector3.zero; car.angularVelocity = Vector3.zero; }
            if (anchorToggle != null) anchorToggle.SetIsOnWithoutNotify(on);
        }

        bool SledOverlapsCar()
        {
            var sc = sled.GetComponent<BoxCollider>();
            if (sc == null) return false;
            Physics.SyncTransforms();
            foreach (var c in car.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger && Physics.ComputePenetration(sc, sc.transform.position, sc.transform.rotation,
                        c, c.transform.position, c.transform.rotation, out _, out float dist) && dist > 1e-4f)
                    return true;
            return false;
        }

        public void SetDirection(ImpactDirection d)
        {
            direction = d;
            if (!launched) PlaceSled();
            RefreshDirectionButtons();
        }

        public void SetNarrowImpactor(bool narrow)
        {
            narrowImpactor = narrow;
            if (!launched) PlaceSled();
            RefreshDirectionButtons();
        }

        /// <summary>Width of the sled's box collider (and mesh) via its x scale; the box asset is 1.9 m wide.</summary>
        void ApplySledWidth()
        {
            var bc = sled.GetComponent<BoxCollider>();
            float baseWidth = bc != null ? bc.size.x : 1.9f;
            var s = sled.transform.localScale;
            s.x = (narrowImpactor ? narrowWidth : wideWidth) / Mathf.Max(0.01f, baseWidth);
            sled.transform.localScale = s;
        }

        /// <summary>Park the sled (kinematic, at rest) facing the target point for the current direction, relative to
        /// the car's current ground pose (the start pose when anchored), shifted sideways by the aim offset.</summary>
        void PlaceSled()
        {
            ApplySledWidth();
            Aim(direction, out Vector3 localTarget, out Vector3 localDir);
            localTarget += AimOffsetVector(direction) * aimOffset;
            Vector3 fwd = car.transform.forward; fwd.y = 0f;
            Quaternion yaw = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd.normalized, Vector3.up) : carStartRot;
            Vector3 basePos = new Vector3(car.transform.position.x, carStartPos.y, car.transform.position.z);
            Vector3 target = basePos + yaw * localTarget;
            Vector3 dir = yaw * localDir;
            dir.y = 0f;
            dir.Normalize();
            float halfLength = sled.GetComponent<BoxCollider>() is BoxCollider bc ? bc.size.z * 0.5f * sled.transform.lossyScale.z : 2f;
            Vector3 pos = target - dir * (startGap + halfLength);
            pos.y = carStartPos.y + sledHeight;
            sled.isKinematic = true;
            sled.position = pos;
            sled.rotation = Quaternion.LookRotation(dir, Vector3.up);
            sled.transform.SetPositionAndRotation(pos, sled.rotation);
        }

        /// <summary>Car-space direction of a positive aim offset: towards the car's right (+X) for front, rear and corner
        /// hits, towards the front (+Z) for side hits.</summary>
        public static Vector3 AimOffsetVector(ImpactDirection d) =>
            d == ImpactDirection.LeftSide || d == ImpactDirection.RightSide ? Vector3.forward : Vector3.right;

        /// <summary>Travel direction of the impactor for a test direction, in car space (car faces +Z, left is -X).</summary>
        public static Vector3 Direction(ImpactDirection d)
        {
            switch (d)
            {
                case ImpactDirection.Rear: return Vector3.forward;
                case ImpactDirection.LeftSide: return Vector3.right;
                case ImpactDirection.RightSide: return Vector3.left;
                case ImpactDirection.FrontCornerAngled: return new Vector3(0.55f, 0f, -0.835f).normalized;
                case ImpactDirection.RearCornerAngled: return new Vector3(-0.55f, 0f, 0.835f).normalized;
                default: return Vector3.back;
            }
        }

        /// <summary>Aim point on the current vehicle's outline (car space), computed from its measured body bounds:
        /// end and side centres, and corner points 8 % of the width / 5 % of the length inside the body's corner.</summary>
        public void Aim(ImpactDirection d, out Vector3 target, out Vector3 dir)
        {
            Bounds b = Geometry.body;
            dir = Direction(d);
            float cx = b.center.x, cz = b.center.z, w = b.size.x, l = b.size.z;
            switch (d)
            {
                case ImpactDirection.Rear: target = new Vector3(cx, 0f, b.min.z); break;
                case ImpactDirection.LeftSide: target = new Vector3(b.min.x, 0f, cz); break;
                case ImpactDirection.RightSide: target = new Vector3(b.max.x, 0f, cz); break;
                case ImpactDirection.FrontCornerAngled: target = new Vector3(b.min.x + 0.08f * w, 0f, b.max.z - 0.05f * l); break;
                case ImpactDirection.RearCornerAngled: target = new Vector3(b.max.x - 0.08f * w, 0f, b.min.z + 0.05f * l); break;
                default: target = new Vector3(cx, 0f, b.max.z); break;
            }
        }

        public static string Label(ImpactDirection d)
        {
            switch (d)
            {
                case ImpactDirection.Rear: return "Rear";
                case ImpactDirection.LeftSide: return "Left side";
                case ImpactDirection.RightSide: return "Right side";
                case ImpactDirection.FrontCornerAngled: return "Front-left corner (angled)";
                case ImpactDirection.RearCornerAngled: return "Rear-right corner (angled)";
                default: return "Front";
            }
        }

        void FixedUpdate()
        {
            AfterPhysicsStep();
            // Settle: once both bodies are slow after an impact, report it.
            if (launched && Time.time - launchTime > 1f && sled.linearVelocity.sqrMagnitude < 0.04f && car.linearVelocity.sqrMagnitude < 0.04f)
                SetStatus(receiver != null && receiver.HasReport ? "Impact recorded. Reset Block to hit again, Repair Car to undo damage." : "Stopped without contact.");
        }

        /// <summary>Safe point for hitbox changes (also called by the edit-mode validation after each physics step):
        /// a crushed region retracts only once the block has stopped pressing into the car (parked, or moving with
        /// it), never while the solver is still resolving the impact.</summary>
        public void AfterPhysicsStep()
        {
            if (hitbox == null || !hitbox.HasPending) return;
            if (sled.isKinematic || (sled.linearVelocity - car.linearVelocity).magnitude < settleSpeed)
                hitbox.ApplyPending();
        }

        [Tooltip("Relative speed (m/s) below which the impact counts as resolved and crushed hitbox regions may retract.")]
        public float settleSpeed = 0.3f;

        // ------------------------------------------------------------------ UI

        void BindUI()
        {
            if (speedSlider != null)
            {
                speedSlider.minValue = 0f; speedSlider.maxValue = 30f; speedSlider.wholeNumbers = true;
                speedSlider.SetValueWithoutNotify(speed);
                speedSlider.onValueChanged.AddListener(v => { speed = v; RefreshValues(); });
            }
            if (speedField != null)
                speedField.onEndEdit.AddListener(t => { if (float.TryParse(t, out float v)) speed = Mathf.Clamp(Mathf.Round(v), 0f, 30f); RefreshValues(); });
            if (massSlider != null)
            {
                massSlider.minValue = 100f; massSlider.maxValue = 4000f; massSlider.wholeNumbers = true;
                massSlider.SetValueWithoutNotify(mass);
                massSlider.onValueChanged.AddListener(v => { mass = Mathf.Round(v / 50f) * 50f; RefreshValues(); });
            }
            if (massField != null)
                massField.onEndEdit.AddListener(t => { if (float.TryParse(t, out float v)) mass = Mathf.Clamp(Mathf.Round(v / 50f) * 50f, 100f, 4000f); RefreshValues(); });
            if (directionButtons != null)
                for (int i = 0; i < directionButtons.Length; i++)
                {
                    var d = (ImpactDirection)i;
                    if (directionButtons[i] != null) directionButtons[i].onClick.AddListener(() => SetDirection(d));
                }
            if (impactorButtons != null)
                for (int i = 0; i < impactorButtons.Length; i++)
                {
                    bool narrow = i == 1;
                    if (impactorButtons[i] != null) impactorButtons[i].onClick.AddListener(() => SetNarrowImpactor(narrow));
                }
            if (footprintToggle != null)
            {
                footprintToggle.SetIsOnWithoutNotify(footprintDebug != null && footprintDebug.show);
                footprintToggle.onValueChanged.AddListener(on => { if (footprintDebug != null) footprintDebug.SetVisible(on); });
            }
            if (launchButton != null) launchButton.onClick.AddListener(() => Launch());
            if (resetButton != null) resetButton.onClick.AddListener(ResetAll);
            if (vehicleButtons != null)
                for (int i = 0; i < vehicleButtons.Length; i++)
                {
                    int v = i;
                    if (vehicleButtons[i] != null) vehicleButtons[i].onClick.AddListener(() => SelectVehicle(v));
                }
            if (resetBlockButton != null) resetBlockButton.onClick.AddListener(() => { ResetBlock(); SetStatus("Block reset"); });
            if (repairButton != null) repairButton.onClick.AddListener(RepairCar);
            if (anchorToggle != null) { anchorToggle.SetIsOnWithoutNotify(anchored); anchorToggle.onValueChanged.AddListener(SetAnchored); }
            if (collidersToggle != null)
            {
                collidersToggle.SetIsOnWithoutNotify(colliderDebug != null && colliderDebug.show);
                collidersToggle.onValueChanged.AddListener(on => { if (colliderDebug != null) colliderDebug.SetVisible(on); });
            }
            if (offsetSlider != null)
            {
                offsetSlider.minValue = -1.5f; offsetSlider.maxValue = 1.5f; offsetSlider.wholeNumbers = false;
                offsetSlider.SetValueWithoutNotify(aimOffset);
                offsetSlider.onValueChanged.AddListener(v => { aimOffset = Mathf.Round(v * 20f) / 20f; if (!launched) PlaceSled(); RefreshValues(); });
            }
            if (offsetField != null)
                offsetField.onEndEdit.AddListener(t => { if (float.TryParse(t, out float v)) aimOffset = Mathf.Clamp(Mathf.Round(v * 20f) / 20f, -1.5f, 1.5f); if (!launched) PlaceSled(); RefreshValues(); });
            RefreshValues();
            RefreshDirectionButtons();
        }

        /// <summary>Push the current speed / mass / direction into the widgets (also used by the screenshot tool).</summary>
        public void RefreshValues()
        {
            if (speedSlider != null) speedSlider.SetValueWithoutNotify(speed);
            if (speedField != null && !speedField.isFocused) speedField.SetTextWithoutNotify(speed.ToString("0"));
            if (speedMphText != null) speedMphText.text = $"{speed:0} m/s  ≈  {speed * 2.23694f:0} mph  ·  {speed * 3.6f:0} km/h";
            if (massSlider != null) massSlider.SetValueWithoutNotify(mass);
            if (massField != null && !massField.isFocused) massField.SetTextWithoutNotify(mass.ToString("0"));
            if (offsetSlider != null) offsetSlider.SetValueWithoutNotify(aimOffset);
            if (offsetField != null && !offsetField.isFocused) offsetField.SetTextWithoutNotify(aimOffset.ToString("0.00"));
            RefreshDirectionButtons();
        }

        void RefreshDirectionButtons()
        {
            if (directionButtons == null) return;
            for (int i = 0; i < directionButtons.Length; i++)
                if (directionButtons[i] != null && directionButtons[i].targetGraphic != null)
                    directionButtons[i].targetGraphic.color = i == (int)direction ? selectedColor : unselectedColor;
            if (impactorButtons != null)
                for (int i = 0; i < impactorButtons.Length; i++)
                    if (impactorButtons[i] != null && impactorButtons[i].targetGraphic != null)
                        impactorButtons[i].targetGraphic.color = (i == 1) == narrowImpactor ? selectedColor : unselectedColor;
        }

        public void RefreshDiagnostics()
        {
            if (diagnosticsText == null) return;
            if (receiver == null || !receiver.HasReport)
            {
                diagnosticsText.text = "No collision yet.\nPick a speed, mass and direction, then press LAUNCH.";
                return;
            }
            var r = receiver.Last;
            diagnosticsText.text =
                $"Relative speed       {r.relativeSpeed:0.0} m/s  ({r.relativeSpeed * 2.23694f:0} mph)\n" +
                $"Closing (normal)     {r.closingSpeed:0.0} m/s\n" +
                $"Impulse              {r.impulse:N0} N·s\n" +
                $"Energy (½μv²)        {r.energy / 1000f:0.0} kJ   μ {r.reducedMass:0} kg\n" +
                $"Impact strength      {r.strength * 100f:0} / 100{(r.applied ? "" : r.strength > 0f ? "  (not applied)" : "  (below threshold)")}\n" +
                $"Location             {r.region}  {r.localPoint.ToString("F2")}{(string.IsNullOrEmpty(r.carCollider) ? "" : $"  [{r.carCollider}]")}\n" +
                (r.footprint.valid
                    ? $"Contact footprint    {r.footprint.kind}, {r.footprint.halfU * 2f:0.00} x {r.footprint.halfV * 2f:0.00} m ({r.footprint.area:0.00} m²)\n" +
                      $"Crush depth          {r.footprint.travel * 100f:0} cm  (area factor ×{r.footprint.areaFactor:0.00})\n"
                    : "") +
                $"Mesh deformation     {(r.applied ? $"{r.deformMilliseconds:0.0} ms" : "-")}\n" +
                (r.applied ? $"  resolve {model.LastResolveMilliseconds:0.0} · accumulate {model.LastAccumulateMilliseconds:0.0} ({model.LastImpactPartsEvaluated}/{model.PartCount} parts) · rebuild {model.LastDeformMilliseconds:0.0} ms ({model.LastRebuiltParts}/{model.PartCount})\n" : "") +
                $"Impacts on car       {model.TotalImpactCount} ({model.ImpactCount} stored, {model.MergedImpactCount} merged into damage)" +
                (hitbox != null && !string.IsNullOrEmpty(hitbox.LastChangeSummary) ? $"\nHitbox update        {hitbox.LastChangeSummary}  ({hitbox.LastUpdateMilliseconds:0.0} ms)" : "");
        }

        void SetStatus(string s)
        {
            if (statusText != null) statusText.text = s;
        }
    }
}
