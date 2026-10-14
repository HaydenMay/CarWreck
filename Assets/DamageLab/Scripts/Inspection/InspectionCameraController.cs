using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace DamageLab.Inspection
{
    /// <summary>
    /// Orbit / pan / zoom inspection camera.
    ///   Mouse: orbit = left or right drag, pan = middle drag or Shift + left drag, zoom = scroll wheel
    ///   Touch: orbit = one-finger drag, zoom = pinch, pan = two-finger drag
    ///   Reset: F key or <see cref="ResetView"/>
    /// Drags that start over UI are ignored so sliders do not spin the camera.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class InspectionCameraController : MonoBehaviour
    {
        [Header("Target")]
        public Vector3 pivot = new Vector3(0f, 0.65f, 0f);

        [Header("Default view")]
        public float defaultYaw = 145f;
        public float defaultPitch = 14f;
        public float defaultDistance = 7.6f;

        [Header("Limits")]
        public float minPitch = -2f;
        public float maxPitch = 80f;
        public float minDistance = 2.6f;
        public float maxDistance = 16f;
        public float panLimit = 3f;

        [Header("Feel")]
        public float orbitSpeed = 0.25f;
        public float touchOrbitSpeed = 0.18f;
        public float zoomStep = 0.12f;
        public float damping = 12f;

        [Tooltip("Horizontal offset (fraction of the screen) so the car sits in the free area left of the UI panel.")]
        public float screenOffset = 0.12f;

        float yaw, pitch, distance;
        float yawTarget, pitchTarget, distanceTarget;
        Vector3 panOffset, panTarget;
        bool orbiting, panning;
        bool touchGesture;          // a touch gesture that started over the 3D view
        float lastPinchDistance;
        Vector2 lastPinchCenter;
        int lastTouchCount;
        Camera cam;

        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        PointerEventData uiPointer;

        public float Yaw => yaw;

        void Awake()
        {
            cam = GetComponent<Camera>();
            ResetView(true);
        }

        public void ResetView() => ResetView(false);

        public void ResetView(bool instant)
        {
            yawTarget = defaultYaw;
            pitchTarget = defaultPitch;
            distanceTarget = defaultDistance;
            panTarget = Vector3.zero;
            if (instant)
            {
                yaw = yawTarget; pitch = pitchTarget; distance = distanceTarget; panOffset = panTarget;
                ApplyTransform();
            }
        }

        /// <summary>Jump to an exact view (used by the screenshot tool).</summary>
        public void SetView(float newYaw, float newPitch, float newDistance)
        {
            yaw = yawTarget = newYaw;
            pitch = pitchTarget = Mathf.Clamp(newPitch, minPitch, maxPitch);
            distance = distanceTarget = Mathf.Clamp(newDistance, minDistance, maxDistance);
            panOffset = panTarget = Vector3.zero;
            if (cam == null) cam = GetComponent<Camera>();
            ApplyTransform();
        }

        void Update()
        {
            // Touch takes priority; browsers also synthesize mouse events from touches.
            if (!HandleTouch()) HandleMouse();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) ResetView();

            float k = 1f - Mathf.Exp(-damping * Time.unscaledDeltaTime);
            yaw = Mathf.Lerp(yaw, yawTarget, k);
            pitch = Mathf.Lerp(pitch, pitchTarget, k);
            distance = Mathf.Lerp(distance, distanceTarget, k);
            panOffset = Vector3.Lerp(panOffset, panTarget, k);
            ApplyTransform();
        }

        bool HandleTouch()
        {
            var ts = Touchscreen.current;
            if (ts == null) return false;

            TouchControl t0 = null, t1 = null;
            int count = 0;
            foreach (var t in ts.touches)
            {
                if (!t.isInProgress) continue;
                if (count == 0) t0 = t; else if (count == 1) t1 = t;
                count++;
            }
            if (count == 0)
            {
                touchGesture = false;
                lastTouchCount = 0;
                return false;
            }

            // A gesture belongs to the camera only if its first finger landed outside the UI.
            if (lastTouchCount == 0) touchGesture = !OverUI(t0.startPosition.ReadValue());
            if (!touchGesture) { lastTouchCount = count; return true; }

            if (count == 1)
            {
                Vector2 delta = t0.delta.ReadValue();
                if (lastTouchCount == 1)
                {
                    yawTarget += delta.x * touchOrbitSpeed;
                    pitchTarget = Mathf.Clamp(pitchTarget - delta.y * touchOrbitSpeed, minPitch, maxPitch);
                }
            }
            else
            {
                Vector2 p0 = t0.position.ReadValue(), p1 = t1.position.ReadValue();
                float dist = Vector2.Distance(p0, p1);
                Vector2 center = (p0 + p1) * 0.5f;
                if (lastTouchCount >= 2 && lastPinchDistance > 1f)
                {
                    distanceTarget = Mathf.Clamp(distanceTarget * (lastPinchDistance / Mathf.Max(dist, 1f)), minDistance, maxDistance);
                    Vector2 d = center - lastPinchCenter;
                    float scale = distance * 0.0016f;
                    panTarget = Vector3.ClampMagnitude(panTarget - (transform.right * d.x + transform.up * d.y) * scale, panLimit);
                }
                lastPinchDistance = dist;
                lastPinchCenter = center;
            }
            lastTouchCount = count;
            return true;
        }

        void HandleMouse()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var keyboard = Keyboard.current;
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;
            bool pressed = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
            bool overUI = pressed && OverUI(mouse.position.ReadValue());

            if ((mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) && !overUI)
            {
                if (mouse.leftButton.wasPressedThisFrame && shift) panning = true;
                else orbiting = true;
            }
            if (mouse.middleButton.wasPressedThisFrame && !overUI) panning = true;
            if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) orbiting = false;
            if (!mouse.middleButton.isPressed && !(mouse.leftButton.isPressed && shift)) panning = false;

            Vector2 delta = mouse.delta.ReadValue();
            if (orbiting && !panning)
            {
                yawTarget += delta.x * orbitSpeed;
                pitchTarget = Mathf.Clamp(pitchTarget - delta.y * orbitSpeed, minPitch, maxPitch);
            }
            else if (panning)
            {
                float scale = distance * 0.0016f;
                panTarget = Vector3.ClampMagnitude(panTarget - (transform.right * delta.x + transform.up * delta.y) * scale, panLimit);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f && !OverUI(mouse.position.ReadValue()))
            {
                distanceTarget *= scroll > 0f ? 1f - zoomStep : 1f + zoomStep;
                distanceTarget = Mathf.Clamp(distanceTarget, minDistance, maxDistance);
            }
        }

        /// <summary>Raycast the UI directly (works on the first frame of a touch, unlike IsPointerOverGameObject).</summary>
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

        void ApplyTransform()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 center = pivot + panOffset;
            Vector3 pos = center - rot * Vector3.forward * distance;
            // Slide the camera sideways (keeping its orientation) so the pivot appears left of centre,
            // in the viewport area not covered by the right-hand UI panel.
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            float fov = cam != null ? cam.fieldOfView : 40f;
            float halfWidth = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspect * distance;
            pos += rot * Vector3.right * (halfWidth * screenOffset * 2f);
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
