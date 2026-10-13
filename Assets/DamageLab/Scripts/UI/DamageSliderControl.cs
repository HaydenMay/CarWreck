using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.UI;

namespace DamageLab.UI
{
    /// <summary>One damage slider card: slider + numeric box, bound to one direction of the vehicle.</summary>
    public class DamageSliderControl : MonoBehaviour
    {
        public DamageDirection direction;
        public Slider slider;
        public InputField valueField;
        public Image accentBar;

        VehicleDamageModel model;
        bool syncing;

        public void Bind(VehicleDamageModel target)
        {
            model = target;
            slider.minValue = 0f;
            slider.maxValue = 100f;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(OnSlider);
            valueField.onEndEdit.AddListener(OnField);
            Sync();
        }

        void OnDestroy()
        {
            if (slider != null) slider.onValueChanged.RemoveListener(OnSlider);
            if (valueField != null) valueField.onEndEdit.RemoveListener(OnField);
        }

        void OnSlider(float value)
        {
            if (syncing || model == null) return;
            model.SetDamage(direction, value);
            SetFieldText(value);
        }

        void OnField(string text)
        {
            if (model == null) return;
            if (!float.TryParse(text, out float v)) v = model.GetDamage(direction);
            v = Mathf.Clamp(Mathf.Round(v), 0f, 100f);
            model.SetDamage(direction, v);
            Sync();
        }

        /// <summary>Pull the model value into the widgets (e.g. after Reset All).</summary>
        public void Sync()
        {
            if (model == null) return;
            syncing = true;
            float v = model.GetDamage(direction);
            slider.SetValueWithoutNotify(v);
            SetFieldText(v);
            syncing = false;
        }

        void SetFieldText(float v)
        {
            if (valueField != null && !valueField.isFocused) valueField.SetTextWithoutNotify(Mathf.RoundToInt(v).ToString());
        }

        void Update()
        {
            if (accentBar != null && model != null)
            {
                float a = model.GetAppliedDamage(direction) / 100f;
                accentBar.fillAmount = a;
            }
        }
    }
}
