using DamageLab.Deformation;
using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.UI
{
    /// <summary>Wires the lab panel (sliders, reset, wireframe, impact testing, info readouts, rotation bar) to the vehicle.</summary>
    public class DamageLabUI : MonoBehaviour
    {
        public VehicleDamageModel model;
        public ModelTurntable turntable;
        public InspectionCameraController cameraController;

        [Header("Damage panel")]
        public DamageSliderControl[] sliders;
        public Button resetAllButton;
        public Toggle wireframeToggle;

        [Header("Impact testing")]
        public ImpactPicker impactPicker;
        public Toggle clickToDamageToggle;
        public Slider impactStrengthSlider;
        public InputField impactStrengthField;
        public Text impactCountText;
        public Text impactHintText;
        public Button clearImpactsButton;

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
        public Color hintColor = new Color(0.64f, 0.66f, 0.68f);

        float infoTimer;
        string lastImpactMessage;
        float lastImpactMessageTime = -10f;

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
            BindImpactControls();
            RefreshInfo();
        }

        void BindImpactControls()
        {
            if (impactPicker == null) return;
            if (clickToDamageToggle != null)
            {
                clickToDamageToggle.SetIsOnWithoutNotify(impactPicker.clickToDamage);
                clickToDamageToggle.onValueChanged.AddListener(on => { impactPicker.clickToDamage = on; RefreshImpactInfo(); });
            }
            if (impactStrengthSlider != null)
            {
                impactStrengthSlider.SetValueWithoutNotify(impactPicker.strength);
                impactStrengthSlider.onValueChanged.AddListener(v =>
                {
                    impactPicker.strength = v;
                    if (impactStrengthField != null && !impactStrengthField.isFocused) impactStrengthField.SetTextWithoutNotify(Mathf.RoundToInt(v).ToString());
                });
            }
            if (impactStrengthField != null)
            {
                impactStrengthField.SetTextWithoutNotify(Mathf.RoundToInt(impactPicker.strength).ToString());
                impactStrengthField.onEndEdit.AddListener(text =>
                {
                    if (!float.TryParse(text, out float v)) v = impactPicker.strength;
                    v = Mathf.Clamp(Mathf.Round(v), 0f, 100f);
                    impactPicker.strength = v;
                    if (impactStrengthSlider != null) impactStrengthSlider.SetValueWithoutNotify(v);
                    impactStrengthField.SetTextWithoutNotify(Mathf.RoundToInt(v).ToString());
                });
            }
            if (clearImpactsButton != null) clearImpactsButton.onClick.AddListener(() => { if (model != null) model.ClearImpacts(); });
            impactPicker.ImpactAttempted += (ok, msg) =>
            {
                lastImpactMessage = msg;
                lastImpactMessageTime = Time.unscaledTime;
                RefreshImpactInfo();
            };
            if (model != null) model.ImpactsChanged += RefreshImpactInfo;
        }

        void OnDestroy()
        {
            if (model != null) model.ImpactsChanged -= RefreshImpactInfo;
        }

        public void ResetAll()
        {
            if (model == null) return;
            model.ResetAll(); // sliders to 0 and localized impacts cleared
            foreach (var s in sliders) s.Sync();
            RefreshImpactInfo();
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
            if (impactPicker != null)
            {
                if (clickToDamageToggle != null)
                {
                    clickToDamageToggle.SetIsOnWithoutNotify(impactPicker.clickToDamage);
                    if (clickToDamageToggle.graphic != null) clickToDamageToggle.graphic.canvasRenderer.SetAlpha(impactPicker.clickToDamage ? 1f : 0f);
                }
                if (impactStrengthSlider != null) impactStrengthSlider.SetValueWithoutNotify(impactPicker.strength);
                if (impactStrengthField != null) impactStrengthField.SetTextWithoutNotify(Mathf.RoundToInt(impactPicker.strength).ToString());
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

        void RefreshImpactInfo()
        {
            if (model == null) return;
            if (impactCountText != null)
            {
                impactCountText.text = $"{model.ImpactCount} / {model.maxImpacts}";
                impactCountText.color = model.ImpactLimitReached ? damagedColor : Color.white;
            }
            if (impactHintText == null) return;
            string hint;
            if (model.ImpactLimitReached) hint = "Limit reached. Clear impacts to add more.";
            else if (impactPicker == null || !impactPicker.clickToDamage) hint = "Enable, then click or tap the car.";
            else if (Time.unscaledTime - lastImpactMessageTime < 2.5f && !string.IsNullOrEmpty(lastImpactMessage)) hint = lastImpactMessage;
            else hint = "Click or tap the car to dent it.";
            impactHintText.text = hint;
            impactHintText.color = model.ImpactLimitReached ? damagedColor : hintColor;
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
                bool localized = model.ImpactCount > 0;
                string label = max <= 0f ? (localized ? "Localized Damage" : "Undamaged") : max < 35f ? "Light Damage" : max < 65f ? "Moderate Damage" : max < 90f ? "Heavy Damage" : "Severe Damage";
                statusText.text = label;
                statusText.color = max <= 0f && !localized ? undamagedColor : damagedColor;
            }
            if (summaryText != null)
            {
                summaryText.text = $"F {Mathf.RoundToInt(model.GetDamage(DamageDirection.Front))}  ·  R {Mathf.RoundToInt(model.GetDamage(DamageDirection.Rear))}  ·  " +
                                   $"L {Mathf.RoundToInt(model.GetDamage(DamageDirection.Left))}  ·  Rt {Mathf.RoundToInt(model.GetDamage(DamageDirection.Right))}";
            }
            RefreshImpactInfo();
        }
    }
}
