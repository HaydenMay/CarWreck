using System;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageLab.Inspection
{
    /// <summary>A button that reports press and release, for click-or-hold controls.</summary>
    public class HoldButton : Selectable, IPointerDownHandler, IPointerUpHandler
    {
        public event Action Pressed;
        public event Action Released;
        public bool IsHeld { get; private set; }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable()) return;
            IsHeld = true;
            Pressed?.Invoke();
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);
            if (!IsHeld) return;
            IsHeld = false;
            Released?.Invoke();
        }

        protected override void OnDisable()
        {
            if (IsHeld) { IsHeld = false; Released?.Invoke(); }
            base.OnDisable();
        }
    }
}
