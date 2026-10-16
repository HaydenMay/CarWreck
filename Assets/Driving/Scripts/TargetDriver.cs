using DamageLab.CollisionTest;
using UnityEngine;

namespace CarWreck.Driving
{
    /// <summary>
    /// Test driver for the moving target vehicle. It is NOT an AI: it only feeds throttle to the target's own
    /// <see cref="ArcadeCarController"/> (the same physics-driven controller the player uses; the target's transform is never
    /// written), holds steering straight, and keeps a configurable speed along the heading it started with.
    ///   Driving  : throttle follows (desired speed - current speed), so the car accelerates to and holds the speed.
    ///   Struck   : after a real collision the driver lifts off (no throttle, no steering) and the car just reacts to physics.
    ///   Stopping : at the end of the test route the car brakes to a halt (and stays put) so it never runs into the arena wall.
    /// </summary>
    [RequireComponent(typeof(ArcadeCarController))]
    public class TargetDriver : MonoBehaviour
    {
        public enum State { Driving, Struck, Stopping }

        public float desiredSpeed = 11f;          // m/s
        public float routeLength = 110f;          // m from the start point along the start heading
        public State Current { get; private set; } = State.Driving;
        public CollisionDamageReceiver receiver;

        ArcadeCarController controller;
        Rigidbody body;
        Vector3 startPos, startForward;

        void Awake()
        {
            controller = GetComponent<ArcadeCarController>();
            body = GetComponent<Rigidbody>();
        }

        void OnDestroy()
        {
            if (receiver != null) receiver.ImpactRegistered -= OnImpact;
        }

        /// <summary>Remember the start point and heading (call after the car has been placed) and start driving.</summary>
        public void Begin(float speedMetresPerSecond, float route)
        {
            if (controller == null) Awake();
            desiredSpeed = speedMetresPerSecond;
            routeLength = route;
            startPos = body.position;
            startForward = transform.forward; startForward.y = 0f; startForward.Normalize();
            Current = State.Driving;
            if (receiver != null) { receiver.ImpactRegistered -= OnImpact; receiver.ImpactRegistered += OnImpact; }
        }

        /// <summary>Back to the start state after a reset (the game places the car; call this afterwards).</summary>
        public void Restart()
        {
            if (controller == null) Awake();
            startPos = body.position;
            startForward = transform.forward; startForward.y = 0f; startForward.Normalize();
            Current = State.Driving;
            controller.ResetState();
        }

        void OnImpact(CollisionDamageReceiver.Report r)
        {
            if (r.applied && Current == State.Driving) Current = State.Struck;
        }

        void Update() => Drive();

        /// <summary>One driving decision (public so the editor validation, which has no Update, can call it each step).</summary>
        public void Drive()
        {
            if (controller == null) Awake();
            float along = Vector3.Dot(body.position - startPos, startForward);
            if (Current == State.Driving && along >= routeLength) Current = State.Stopping;

            float fwd = controller.ForwardSpeed;
            switch (Current)
            {
                case State.Driving:
                    controller.Throttle = Mathf.Clamp01(desiredSpeed - fwd);
                    controller.SteerInput = 0f;
                    break;
                case State.Stopping:
                    controller.Throttle = fwd > 0.5f ? -1f : 0f;       // brake only while rolling forward (never reverse)
                    controller.SteerInput = 0f;
                    break;
                default:                                                // Struck: driver lifts off
                    controller.Throttle = 0f;
                    controller.SteerInput = 0f;
                    break;
            }
        }
    }
}
