using UnityEngine;
using UnityEngine.InputSystem;

namespace CarWreck.Driving
{
    /// <summary>
    /// One place that turns keyboard, gamepad and on-screen touch buttons into the three driving inputs
    /// (throttle -1..1, steer -1..1, reset). Several sources may be active at once; the strongest wins.
    /// Touch buttons only set the four flags below, so they can be held together (multi-touch).
    /// </summary>
    public class DriveInput : MonoBehaviour
    {
        // Set by the on-screen buttons (TouchControls).
        [System.NonSerialized] public bool touchGas, touchBrake, touchLeft, touchRight;

        /// <summary>Which sources contributed this frame (diagnostics): k = keyboard, g = gamepad, t = touch buttons.</summary>
        public string Sources { get; private set; } = "";

        /// <summary>+1 = accelerate, -1 = brake / reverse.</summary>
        public float Throttle { get; private set; }
        /// <summary>-1 = left, +1 = right.</summary>
        public float Steer { get; private set; }
        /// <summary>True on the frame the user asked for a reset (call <see cref="ConsumeReset"/> to read it once).</summary>
        bool resetPressed;

        public bool ConsumeReset()
        {
            bool r = resetPressed;
            resetPressed = false;
            return r;
        }

        public void RequestReset() => resetPressed = true;

        /// <summary>True the first time a touch has been seen this session (the HUD then shows the touch controls).</summary>
        public bool TouchSeen { get; private set; }

        void Update()
        {
            float gas = 0f, brake = 0f, steer = 0f;
            bool k = false, g = false;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) gas = 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed || kb.spaceKey.isPressed) brake = 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                if (kb.rKey.wasPressedThisFrame) resetPressed = true;
                k = gas > 0f || brake > 0f || steer != 0f;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                gas = Mathf.Max(gas, pad.rightTrigger.ReadValue(), pad.buttonSouth.isPressed ? 1f : 0f);
                brake = Mathf.Max(brake, pad.leftTrigger.ReadValue(), pad.buttonEast.isPressed ? 1f : 0f);
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) < 0.12f) stick = 0f;
                if (Mathf.Abs(stick) > Mathf.Abs(steer)) steer = stick;
                if (pad.dpad.left.isPressed) steer = -1f;
                if (pad.dpad.right.isPressed) steer = 1f;
                if (pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame) resetPressed = true;
                g = gas > 0f || brake > 0f || steer != 0f;
            }

            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.isPressed) TouchSeen = true;

            if (touchGas) gas = 1f;
            if (touchBrake) brake = 1f;
            if (touchLeft) steer -= 1f;
            if (touchRight) steer += 1f;

            Sources = (k ? "k" : "") + (g ? "g" : "") + (touchGas || touchBrake || touchLeft || touchRight ? "t" : "");

            // Gas and brake together cancel out (the car then just coasts / holds).
            Throttle = Mathf.Clamp(gas - brake, -1f, 1f);
            Steer = Mathf.Clamp(steer, -1f, 1f);
        }

        /// <summary>Marks a touch device (the web page calls this on the first touch).</summary>
        public void MarkTouchSeen() => TouchSeen = true;
    }
}
