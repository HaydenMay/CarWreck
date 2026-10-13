using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DamageLab.Inspection
{
    /// <summary>
    /// Orbit / pan / zoom inspection camera.
    ///   Orbit: left or right mouse drag      Pan: middle drag or Shift + left drag
    ///   Zoom:  scroll wheel                   Reset: F key or <see cref="ResetView"/>
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
        public float zoomStep = 0.12f;
        public float damping = 12f;

        [Tooltip("Horizontal offset (fraction of the screen) so the car sits in the free area left of the UI panel.")]
        public float screenOffset = 0.12f;

        float yaw, pitch, distance;
        float yawTarget, pitchTarget, distanceTarget;
        Vector3 panOffset, panTarget;
        bool orbiting, panning;
        Camera cam;

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
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null)
            {
                bool shift = keyboard != null && keyboard.shiftKey.isPressed;
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

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
                    panTarget -= (transform.right * delta.x + transform.up * delta.y) * scale;
                    panTarget = Vector3.ClampMagnitude(panTarget, panLimit);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f && !overUI)
                {
                    distanceTarget *= scroll > 0f ? 1f - zoomStep : 1f + zoomStep;
                    distanceTarget = Mathf.Clamp(distanceTarget, minDistance, maxDistance);
                }
            }
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) ResetView();

            float k = 1f - Mathf.Exp(-damping * Time.unscaledDeltaTime);
            yaw = Mathf.Lerp(yaw, yawTarget, k);
            pitch = Mathf.Lerp(pitch, pitchTarget, k);
            distance = Mathf.Lerp(distance, distanceTarget, k);
            panOffset = Vector3.Lerp(panOffset, panTarget, k);
            ApplyTransform();
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
