using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Third-person chase camera. The heading follows the car's yaw (blended a little towards the direction of
    /// travel) through a critically damped filter, so quick steering inputs and impacts do not whip the view.
    /// Position is locked to the car (no positional lag, so the car never drifts out of frame) with a smoothed
    /// height; field of view widens slightly with speed. No camera shake.
    /// </summary>
    public class ChaseCamera : MonoBehaviour
    {
        public Rigidbody target;
        [Header("Framing")]
        public float distance = 7.5f;
        public float height = 3.1f;
        [Tooltip("Height above the car's origin the camera looks at.")]
        public float lookHeight = 1.0f;
        [Header("Smoothing")]
        [Tooltip("Seconds the heading takes to catch up with the car's turn.")]
        public float yawSmoothTime = 0.28f;
        [Tooltip("How much the view leans towards the direction of travel (0 = car's nose, 1 = velocity).")]
        [Range(0f, 1f)] public float velocityBlend = 0.35f;
        public float heightSharpness = 6f;
        public float lookSharpness = 14f;
        [Header("Field of view")]
        public float fovMin = 58f;
        public float fovMax = 70f;
        public float fovSpeed = 30f;

        [Header("Damage view")]
        [Tooltip("Extra heading (deg) added to the chase heading: the game swings the view round to the damage after a crash.")]
        public float yawOffset;
        public float offsetSmoothTime = 0.7f;

        [Header("Obstruction")]
        [Tooltip("Walls and other fixed obstacles between the car and the camera pull the camera in (and up) instead of hiding the car.")]
        public float obstructionRadius = 0.35f;
        public float minDistance = 3.4f;
        public float obstructionRaise = 1.4f;

        float yaw, yawVelocity, smoothedY, usedFraction = 1f, offsetSmoothed, offsetVelocity;
        readonly RaycastHit[] castHits = new RaycastHit[8];
        Camera cam;
        bool snapped;

        void Awake() => cam = GetComponent<Camera>();

        // ---- vehicle-selection view: a slow orbit round the chosen car, parked to one side so the menu does not cover it
        Transform menuFocus;
        float menuAngle;
        public float menuSpin = 22f;
        public float menuDistance = 8.5f;
        public float menuShift = 2.6f;

        public void SetMenuFocus(Transform focus)
        {
            if (focus != null && menuFocus != focus) menuAngle = focus.eulerAngles.y + 150f;
            menuFocus = focus;
        }

        public void ClearMenu() { menuFocus = null; Snap(); }

        void MenuOrbit()
        {
            if (cam == null) cam = GetComponent<Camera>();
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            menuAngle += menuSpin * dt;
            Vector3 p = menuFocus.position;
            Vector3 pos = p + Quaternion.Euler(0f, menuAngle, 0f) * new Vector3(0f, 2.4f, -menuDistance);
            Vector3 look = p + Vector3.up * 0.7f;
            Quaternion wanted = Quaternion.LookRotation(look - pos, Vector3.up);
            look -= wanted * Vector3.right * menuShift;          // look left of the car: the car sits right of centre
            wanted = Quaternion.LookRotation(look - pos, Vector3.up);
            float k = 1f - Mathf.Exp(-7f * dt);
            transform.position = Vector3.Lerp(transform.position, pos, k);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, k);
            if (cam != null) cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 42f, k);
        }

        public void Snap()
        {
            snapped = false;
            if (isActiveAndEnabled && target != null) LateUpdate();
        }

        /// <summary>The yaw offset closest to <paramref name="preferred"/> (degrees, relative to the car) whose camera position is not behind a wall.</summary>
        public float FindClearYawOffset(float preferred)
        {
            Transform t = target.transform;
            Vector3 origin = t.position + Vector3.up * (lookHeight + 0.5f);
            float baseYaw = t.eulerAngles.y;
            foreach (float d in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 180f })
            {
                float off = preferred + d;
                Vector3 dest = t.position + Quaternion.Euler(0f, baseYaw + off, 0f) * new Vector3(0f, height, -distance);
                Vector3 dir = dest - origin;
                float len = dir.magnitude;
                int n = Physics.SphereCastNonAlloc(origin, obstructionRadius, dir / len, castHits, len, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < n; i++)
                {
                    var rbHit = castHits[i].collider.attachedRigidbody;
                    if (rbHit == target || castHits[i].distance <= 0f || (rbHit != null && !rbHit.isKinematic)) continue;
                    blocked = true; break;
                }
                if (!blocked) return off;
            }
            return preferred;
        }

        void LateUpdate()
        {
            if (menuFocus != null) { MenuOrbit(); return; }
            if (target == null) return;
            if (cam == null) cam = GetComponent<Camera>();
            Transform t = target.transform;
            Vector3 fwd = t.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
            float targetYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

            Vector3 v = target.linearVelocity; v.y = 0f;
            if (v.magnitude > 4f && Vector3.Dot(v, fwd) > 0f)
            {
                float velYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                targetYaw = Mathf.LerpAngle(targetYaw, velYaw, velocityBlend * Mathf.Clamp01((v.magnitude - 4f) / 8f));
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            offsetSmoothed = snapped ? Mathf.SmoothDampAngle(offsetSmoothed, yawOffset, ref offsetVelocity, offsetSmoothTime, Mathf.Infinity, dt) : yawOffset;
            targetYaw += offsetSmoothed;
            if (!snapped) { yaw = targetYaw; yawVelocity = 0f; smoothedY = t.position.y; }
            else yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, yawSmoothTime, Mathf.Infinity, dt);

            // Smooth only the vertical component (ramps, landings); the horizontal offset stays locked to the car.
            smoothedY = snapped ? Mathf.Lerp(smoothedY, t.position.y, 1f - Mathf.Exp(-heightSharpness * dt)) : t.position.y;
            Quaternion heading = Quaternion.Euler(0f, yaw, 0f);
            Vector3 anchor = new Vector3(t.position.x, smoothedY, t.position.z);
            Vector3 look = anchor + Vector3.up * lookHeight;
            Vector3 pos = anchor + heading * new Vector3(0f, height, -distance);

            // Pull the camera in when something solid is behind the car (instantly in, eased back out).
            Vector3 toCam = pos - look;
            float full = toCam.magnitude;
            float free = 1f;
            int n = Physics.SphereCastNonAlloc(look, obstructionRadius, toCam / full, castHits, full, ~0, QueryTriggerInteraction.Ignore);
            float nearest = full;
            for (int i = 0; i < n; i++)
            {
                var h = castHits[i];
                var rbHit = h.collider.attachedRigidbody;
                if (rbHit == target || h.distance <= 0f) continue;
                if (rbHit != null && !rbHit.isKinematic) continue;       // crates and other movable things never block the view
                nearest = Mathf.Min(nearest, h.distance);
            }
            free = Mathf.Clamp01(Mathf.Max(nearest, Mathf.Min(minDistance, full)) / full);
            usedFraction = !snapped || free < usedFraction ? free : Mathf.Lerp(usedFraction, free, 1f - Mathf.Exp(-4f * dt));
            pos = look + toCam * usedFraction + Vector3.up * ((1f - usedFraction) * obstructionRaise);

            Quaternion wanted = Quaternion.LookRotation(look - pos, Vector3.up);
            transform.position = pos;
            transform.rotation = snapped ? Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-lookSharpness * dt)) : wanted;

            if (cam != null)
            {
                float f = Mathf.Clamp01(target.linearVelocity.magnitude / Mathf.Max(1f, fovSpeed));
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Mathf.Lerp(fovMin, fovMax, f), 1f - Mathf.Exp(-3f * dt));
            }
            snapped = true;
        }
    }
}
