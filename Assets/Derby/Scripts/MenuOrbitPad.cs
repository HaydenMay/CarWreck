using CarWreck.Driving;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace CarWreck.Derby
{
    /// <summary>
    /// Invisible full-screen layer behind the menu panel: a drag on the empty part of the screen orbits the shown car, the mouse wheel and a two-finger pinch zoom it.
    /// The menu panel sits in front of this layer, so buttons never reach it. Only active while a menu page is open.
    /// </summary>
    public class MenuOrbitPad : MonoBehaviour, IDragHandler, IScrollHandler
    {
        public ChaseCamera cam;
        public float degreesPerPixel = 0.35f, raiseDegreesPerPixel = 0.22f;
        [Tooltip("Zoom factor per mouse-wheel notch (distance multiplier, below 1 = closer).")] public float wheelFactor = 0.9f;

        float lastPinch = -1f;
        bool pinching;

        public void OnDrag(PointerEventData e)
        {
            if (cam == null || pinching) return;
            cam.OrbitMenu(e.delta.x * degreesPerPixel, -e.delta.y * raiseDegreesPerPixel);
        }

        public void OnScroll(PointerEventData e)
        {
            if (cam == null || Mathf.Approximately(e.scrollDelta.y, 0f)) return;
            cam.ZoomMenu(e.scrollDelta.y > 0f ? wheelFactor : 1f / wheelFactor);
        }

        void OnDisable() { lastPinch = -1f; pinching = false; }

        void Update()
        {
            var screen = Touchscreen.current;
            if (cam == null || screen == null) return;
            int count = 0; Vector2 a = default, b = default;
            foreach (var t in screen.touches)
            {
                if (!t.press.isPressed) continue;
                if (count == 0) a = t.position.ReadValue(); else if (count == 1) b = t.position.ReadValue();
                count++;
            }
            pinching = count >= 2;
            if (!pinching) { lastPinch = -1f; return; }
            float d = (a - b).magnitude;
            if (lastPinch > 1f && d > 1f) cam.ZoomMenu(lastPinch / d);      // fingers apart = closer
            lastPinch = d;
        }
    }
}
