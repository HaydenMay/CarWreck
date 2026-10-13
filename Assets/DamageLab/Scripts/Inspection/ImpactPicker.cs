using System;
using System.Collections.Generic;
using DamageLab.Vehicle;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DamageLab.Inspection
{
    /// <summary>
    /// Click-to-damage input. When enabled, a left click or one-finger tap on the car applies a localized
    /// impact along the camera ray. A press only counts as a click if it started off the UI, stayed (almost)
    /// still and was short, so orbit / pan / pinch gestures never damage the car. The camera controller
    /// keeps working unchanged alongside this.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class ImpactPicker : MonoBehaviour
    {
        public VehicleDamageModel model;
        public bool clickToDamage;
        [Range(0f, 100f)] public float strength = 50f;

        [Tooltip("Max pointer travel (px at 1080p, scaled with screen height) for a press to count as a click.")]
        public float clickSlop = 10f;
        public float maxClickSeconds = 0.6f;

        /// <summary>Raised after every click attempt on the 3D view: (applied, message).</summary>
        public event Action<bool, string> ImpactAttempted;

        Camera cam;
        // mouse
        bool mousePending;
        Vector2 mouseStart;
        float mouseStartTime, mouseTravel;
        // touch
        bool touchPending, touchCancelled;
        Vector2 touchStart, touchLast;
        float touchStartTime, touchTravel, lastTouchTime = -10f;
        int lastTouchCount;

        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        PointerEventData uiPointer;

        void Awake() => cam = GetComponent<Camera>();

        void Update()
        {
            if (!clickToDamage || model == null)
            {
                mousePending = touchPending = false;
                return;
            }
            float slop = clickSlop * Mathf.Max(0.5f, Screen.height / 1080f);
            HandleTouch(slop);
            // Browsers also synthesize mouse events from touches; ignore the mouse right after touch input.
            if (Time.unscaledTime - lastTouchTime > 0.5f) HandleMouse(slop);
            else mousePending = false;
        }

        void HandleMouse(float slop)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            var kb = Keyboard.current;
            bool shift = kb != null && kb.shiftKey.isPressed;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // Shift + left drag is pan; presses on the UI belong to the UI.
                mousePending = !shift && !OverUI(pos);
                mouseStart = pos;
                mouseStartTime = Time.unscaledTime;
                mouseTravel = 0f;
            }
            if (!mousePending) return;
            mouseTravel = Mathf.Max(mouseTravel, Vector2.Distance(pos, mouseStart));
            if (mouse.rightButton.isPressed || mouse.middleButton.isPressed || mouseTravel > slop) { mousePending = false; return; }
            if (mouse.leftButton.wasReleasedThisFrame)
            {
                mousePending = false;
                if (Time.unscaledTime - mouseStartTime <= maxClickSeconds) Fire(pos);
            }
        }

        void HandleTouch(float slop)
        {
            var ts = Touchscreen.current;
            if (ts == null) return;
            TouchControl first = null;
            int count = 0;
            foreach (var t in ts.touches)
            {
                if (!t.isInProgress) continue;
                if (count == 0) first = t;
                count++;
            }
            if (count > 0) lastTouchTime = Time.unscaledTime;

            if (count > 0 && lastTouchCount == 0)
            {
                touchStart = touchLast = first.startPosition.ReadValue();
                touchStartTime = Time.unscaledTime;
                touchTravel = 0f;
                touchCancelled = OverUI(touchStart);
                touchPending = true;
            }
            if (touchPending && count > 0)
            {
                if (count > 1) touchCancelled = true; // pinch / two-finger pan
                touchLast = first.position.ReadValue();
                touchTravel = Mathf.Max(touchTravel, Vector2.Distance(touchLast, touchStart));
                if (touchTravel > slop * 1.5f) touchCancelled = true;
            }
            if (touchPending && count == 0)
            {
                touchPending = false;
                if (!touchCancelled && Time.unscaledTime - touchStartTime <= maxClickSeconds) Fire(touchLast);
            }
            lastTouchCount = count;
        }

        void Fire(Vector2 screenPosition)
        {
            if (cam == null) cam = GetComponent<Camera>();
            bool ok = model.TryApplyImpact(cam.ScreenPointToRay(screenPosition), strength / 100f, out string message);
            ImpactAttempted?.Invoke(ok, message);
        }

        bool OverUI(Vector2 screenPosition)
        {
            var es = EventSystem.current;
            if (es == null) return false;
            uiPointer ??= new PointerEventData(es);
            uiPointer.position = screenPosition;
            uiHits.Clear();
            es.RaycastAll(uiPointer, uiHits);
            return uiHits.Count > 0;
        }
    }
}
