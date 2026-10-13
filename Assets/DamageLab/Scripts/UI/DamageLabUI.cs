using DamageLab.Deformation;
using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.UI
{
    /// <summary>Wires the lab panel (sliders, reset, wireframe, info readouts, rotation bar) to the vehicle.</summary>
    public class DamageLabUI : MonoBehaviour
    {
        public VehicleDamageModel model;
        public ModelTurntable turntable;
        public InspectionCameraController cameraController;

        [Header("Damage panel")]
        public DamageSliderControl[] sliders;
        public Button resetAllButton;
        public Toggle wireframeToggle;

        [Header("Quick info")]
        public Text trianglesText;
        public Text verticesText;
        public Text deformTimeText;
        public Text statusText;
        public Text summaryText;

        [Header("Inspection")]
        public Text yawText;
        public Button resetViewButton;

        public Color undamagedColor = new Color(0.35f, 0.85f, 0.45f);
        public Color damagedColor = new Color(1f, 0.55f, 0.15f);

        float infoTimer;

        void Start()
        {
            if (model != null && !model.IsInitialized) model.Initialize();
            foreach (var s in sliders) s.Bind(model);
            if (resetAllButton != null) resetAllButton.onClick.AddListener(ResetAll);
            if (wireframeToggle != null)
            {
                wireframeToggle.SetIsOnWithoutNotify(model != null && model.Wireframe);
                wireframeToggle.onValueChanged.AddListener(on => model.SetWireframe(on));
            }
            if (resetViewButton != null)
                resetViewButton.onClick.AddListener(() =>
                {
                    if (cameraController != null) cameraController.ResetView();
                    if (turntable != null) turntable.ResetRotation();
                });
            RefreshInfo();
        }

        public void ResetAll()
        {
            if (model == null) return;
            model.ResetAll();
            foreach (var s in sliders) s.Sync();
        }

        /// <summary>Immediately refresh every readout (used by the screenshot tool in edit mode).</summary>
        public void SyncAll()
        {
            foreach (var s in sliders)
            {
                if (s == null || model == null) continue;
                s.slider.SetValueWithoutNotify(model.GetDamage(s.direction));
                s.valueField.SetTextWithoutNotify(Mathf.RoundToInt(model.GetDamage(s.direction)).ToString());
                if (s.accentBar != null) s.accentBar.fillAmount = model.GetDamage(s.direction) / 100f;
            }
            if (wireframeToggle != null && wireframeToggle.graphic != null && model != null)
            {
                wireframeToggle.SetIsOnWithoutNotify(model.Wireframe);
                wireframeToggle.graphic.canvasRenderer.SetAlpha(model.Wireframe ? 1f : 0f);
            }
            RefreshInfo();
        }

        void Update()
        {
            infoTimer -= Time.unscaledDeltaTime;
            if (infoTimer <= 0f)
            {
                infoTimer = 0.1f;
                RefreshInfo();
            }
            if (yawText != null && turntable != null)
                yawText.text = $"{Mathf.RoundToInt(turntable.Yaw)}°";
        }

        void RefreshInfo()
        {
            if (model == null) return;
            if (trianglesText != null) trianglesText.text = model.TotalTriangles.ToString("N0");
            if (verticesText != null) verticesText.text = $"{model.DeformableVertices:N0} in {model.PartCount} parts";
            if (deformTimeText != null) deformTimeText.text = $"{model.LastDeformMilliseconds:0.0} ms";
            if (statusText != null)
            {
                float max = 0f;
                foreach (var d in DamageDirections.All) max = Mathf.Max(max, model.GetDamage(d));
                string label = max <= 0f ? "Undamaged" : max < 35f ? "Light Damage" : max < 65f ? "Moderate Damage" : max < 90f ? "Heavy Damage" : "Severe Damage";
                statusText.text = label;
                statusText.color = max <= 0f ? undamagedColor : damagedColor;
            }
            if (summaryText != null)
            {
                summaryText.text = $"F {Mathf.RoundToInt(model.GetDamage(DamageDirection.Front))}  ·  R {Mathf.RoundToInt(model.GetDamage(DamageDirection.Rear))}  ·  " +
                                   $"L {Mathf.RoundToInt(model.GetDamage(DamageDirection.Left))}  ·  Rt {Mathf.RoundToInt(model.GetDamage(DamageDirection.Right))}";
            }
        }
    }
}
