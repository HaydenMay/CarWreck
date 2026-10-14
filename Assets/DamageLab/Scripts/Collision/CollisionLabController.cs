using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.CollisionTest
{
    public enum ImpactDirection { Front, Rear, LeftSide, RightSide, FrontCornerAngled }

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

        [Header("UI")]
        public Slider speedSlider;
        public InputField speedField;
        public Text speedMphText;
        public Slider massSlider;
        public InputField massField;
        public Button[] directionButtons;   // in ImpactDirection order
        public Button launchButton;
        public Button resetButton;
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
            if (hasStartPose) return;
            carStartPos = car.transform.position;
            carStartRot = car.transform.rotation;
            if (cameraController != null) homePivot = cameraController.pivot;
            hasStartPose = true;
        }

        Vector3 homePivot;

        void LateUpdate()
        {
            // After launch the camera's orbit pivot follows the car (a heavy hit can shove it many metres).
            if (cameraController == null || !Application.isPlaying) return;
            Vector3 target = launched ? car.position + Vector3.up * 0.6f : homePivot;
            cameraController.pivot = Vector3.Lerp(cameraController.pivot, target, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
        }

        void Start()
        {
            if (model != null && !model.IsInitialized) model.Initialize();
            BindUI();
            ResetTest();
            if (receiver != null) receiver.ImpactRegistered += _ => RefreshDiagnostics();
        }

        // ------------------------------------------------------------------ test control

        /// <summary>Fresh experiment: reset, then give the sled its launch velocity. Physics does the rest.</summary>
        public void Launch()
        {
            ResetTest();
            sled.mass = mass;
            sled.isKinematic = false;
            sled.linearVelocity = sled.transform.forward * speed;
            sled.angularVelocity = Vector3.zero;
            launched = true;
            launchTime = Time.time;
            SetStatus($"Launched: {speed:0} m/s, {mass:0} kg, {Label(direction)}");
        }

        /// <summary>Restore car and sled poses and velocities, remove all impacts and restore the original mesh.</summary>
        public void ResetTest()
        {
            if (model != null)
            {
                model.ResetAll();
                model.SetDamageImmediate(0f, 0f, 0f, 0f);
            }
            car.linearVelocity = Vector3.zero;
            car.angularVelocity = Vector3.zero;
            car.position = carStartPos;
            car.rotation = carStartRot;
            car.transform.SetPositionAndRotation(carStartPos, carStartRot);
            PlaceSled();
            Physics.SyncTransforms();
            if (receiver != null) receiver.ResetState();
            launched = false;
            RefreshDiagnostics();
            SetStatus("Ready");
        }

        public void SetDirection(ImpactDirection d)
        {
            direction = d;
            if (!launched) PlaceSled();
            RefreshDirectionButtons();
        }

        /// <summary>Park the sled (kinematic, at rest) facing the target point for the current direction.</summary>
        void PlaceSled()
        {
            Aim(direction, out Vector3 localTarget, out Vector3 localDir);
            Vector3 target = carStartPos + carStartRot * localTarget;
            Vector3 dir = carStartRot * localDir;
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

        /// <summary>Aim point on the car's outline and travel direction, in car space (car faces +Z, left is -X).</summary>
        public static void Aim(ImpactDirection d, out Vector3 target, out Vector3 dir)
        {
            switch (d)
            {
                case ImpactDirection.Rear: target = new Vector3(0f, 0f, -2.45f); dir = Vector3.forward; break;
                case ImpactDirection.LeftSide: target = new Vector3(-1.0f, 0f, -0.15f); dir = Vector3.right; break;
                case ImpactDirection.RightSide: target = new Vector3(1.0f, 0f, -0.15f); dir = Vector3.left; break;
                case ImpactDirection.FrontCornerAngled: target = new Vector3(-0.85f, 0f, 2.25f); dir = new Vector3(0.55f, 0f, -0.835f).normalized; break;
                default: target = new Vector3(0f, 0f, 2.45f); dir = Vector3.back; break;
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
                default: return "Front";
            }
        }

        void FixedUpdate()
        {
            // Settle: once both bodies are slow after an impact, stop the sled so it doesn't creep on.
            if (launched && Time.time - launchTime > 1f && sled.linearVelocity.sqrMagnitude < 0.04f && car.linearVelocity.sqrMagnitude < 0.04f)
                SetStatus(receiver != null && receiver.HasReport ? "Impact recorded. Orbit to inspect, Reset to run again." : "Stopped without contact.");
        }

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
            if (launchButton != null) launchButton.onClick.AddListener(Launch);
            if (resetButton != null) resetButton.onClick.AddListener(ResetTest);
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
            RefreshDirectionButtons();
        }

        void RefreshDirectionButtons()
        {
            if (directionButtons == null) return;
            for (int i = 0; i < directionButtons.Length; i++)
                if (directionButtons[i] != null && directionButtons[i].targetGraphic != null)
                    directionButtons[i].targetGraphic.color = i == (int)direction ? selectedColor : unselectedColor;
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
                $"Impact strength      {r.strength * 100f:0} / 100{(r.applied ? "" : "  (below threshold)")}\n" +
                $"Location             {r.region}  {r.localPoint.ToString("F2")}\n" +
                $"Mesh deformation     {(r.applied ? $"{r.deformMilliseconds:0.0} ms" : "-")}";
        }

        void SetStatus(string s)
        {
            if (statusText != null) statusText.text = s;
        }
    }
}
