using UnityEngine;

namespace DamageLab.Inspection
{
    /// <summary>
    /// Rotates the actual vehicle object about the vertical axis. Driven by the on-screen arrow buttons:
    /// a click turns 45°, holding the button spins continuously.
    /// </summary>
    public class ModelTurntable : MonoBehaviour
    {
        public float stepDegrees = 45f;
        public float holdSpeed = 90f;
        [Tooltip("Seconds a button must be held before continuous rotation starts.")]
        public float holdDelay = 0.25f;
        public float damping = 10f;

        public HoldButton rotateLeftButton;
        public HoldButton rotateRightButton;

        float targetYaw;
        float yaw;
        float heldTime;
        int heldDirection;
        bool continuous;

        /// <summary>Current yaw in degrees, wrapped to [0, 360).</summary>
        public float Yaw => Mathf.Repeat(yaw, 360f);

        void OnEnable()
        {
            if (rotateLeftButton != null) { rotateLeftButton.Pressed += OnLeftDown; rotateLeftButton.Released += OnRelease; }
            if (rotateRightButton != null) { rotateRightButton.Pressed += OnRightDown; rotateRightButton.Released += OnRelease; }
            yaw = targetYaw = transform.localEulerAngles.y;
        }

        void OnDisable()
        {
            if (rotateLeftButton != null) { rotateLeftButton.Pressed -= OnLeftDown; rotateLeftButton.Released -= OnRelease; }
            if (rotateRightButton != null) { rotateRightButton.Pressed -= OnRightDown; rotateRightButton.Released -= OnRelease; }
        }

        void OnLeftDown() => Press(+1);
        void OnRightDown() => Press(-1);

        void Press(int direction)
        {
            heldDirection = direction;
            heldTime = 0f;
            continuous = false;
        }

        void OnRelease()
        {
            if (heldDirection != 0 && !continuous) RotateStep(heldDirection);
            if (continuous) targetYaw = yaw; // stop where we are
            heldDirection = 0;
            continuous = false;
        }

        /// <summary>Rotate one step. +1 = the car turns to show its left side (counter-clockwise from above).</summary>
        public void RotateStep(int direction) => targetYaw += stepDegrees * Mathf.Sign(direction);

        public void ResetRotation() => targetYaw = Mathf.Round(yaw / 360f) * 360f;

        public void SetYawImmediate(float degrees)
        {
            yaw = targetYaw = degrees;
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void Update()
        {
            if (heldDirection != 0)
            {
                heldTime += Time.unscaledDeltaTime;
                if (heldTime >= holdDelay)
                {
                    continuous = true;
                    yaw += heldDirection * holdSpeed * Time.unscaledDeltaTime;
                    targetYaw = yaw;
                }
            }
            if (!continuous)
                yaw = Mathf.Lerp(yaw, targetYaw, 1f - Mathf.Exp(-damping * Time.unscaledDeltaTime));
            transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
