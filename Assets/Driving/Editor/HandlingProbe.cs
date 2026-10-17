using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CarWreck.Derby;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Measures how each playable vehicle actually handles (real PhysX, Physics.Simulate at 50 Hz, the DerbyArena with its obstacles switched off):
    /// steady-state turning at several speeds (yaw rate, radius, and how much of the commanded yaw rate the body really achieves), steering onset time,
    /// a 180-degree U-turn at 15 m/s, a 180-degree turn from rest (the "repositioning after a collision" case) and acceleration.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Driving.EditorTools.HandlingProbe.Run -quit -logFile handling.log
    /// </summary>
    public static class HandlingProbe
    {
        const float Dt = 0.02f;

        [MenuItem("CarWreck/Diagnostics/Handling Probe", priority = 42)]
        public static void Run() => Debug.Log(Execute());

        static DerbyGame game;
        static DerbyCar car;
        static ArcadeCarController C => car.controller;
        static Rigidbody B => car.Body;

        public class Result
        {
            public string name;
            public float mass;
            public readonly Dictionary<float, (float yawDeg, float radius, float commandedDeg)> steady = new Dictionary<float, (float, float, float)>();
            public float onset15, uturnTime, uturnDiameter, restTime180, restRadius, restTime90, t0to15, t0to20, uturnSpeedEnd;
        }

        public static List<Result> Measure()
        {
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            var list = new List<Result>();
            try
            {
                EditorSceneManager.OpenScene(CarWreck.Derby.EditorTools.DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); game.ui = null;
                foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                    if (rb.isKinematic && !rb.name.StartsWith("Wall")) rb.gameObject.SetActive(false);
                for (int v = 0; v < game.vehicles.Length; v++)
                {
                    game.SelectVehicle(v);
                    car = game.HumanCar;
                    list.Add(Profile(game.vehicles[v].displayName));
                }
            }
            finally { Physics.simulationMode = prev; }
            return list;
        }

        static string Execute()
        {
            var results = Measure();
            var sb = new StringBuilder("[Handling] measured on flat ground, 50 Hz, full lock held, speed held by a simple throttle controller\n");
            foreach (var r in results)
            {
                sb.AppendLine($"  {r.name} ({r.mass:0} kg)");
                foreach (var kv in r.steady.OrderBy(k => k.Key))
                    sb.AppendLine($"    steady turn at {kv.Key:0} m/s: yaw {kv.Value.yawDeg:0.0} deg/s (commanded {kv.Value.commandedDeg:0.0}), radius {kv.Value.radius:0.0} m");
                sb.AppendLine($"    steering onset at 15 m/s (90% of final yaw rate) {r.onset15:0.00} s; U-turn at 15 m/s: {r.uturnTime:0.00} s, diameter {r.uturnDiameter:0.0} m, exit speed {r.uturnSpeedEnd:0.0} m/s");
                sb.AppendLine($"    from rest, full throttle + full lock: 90 deg in {r.restTime90:0.00} s, 180 deg in {r.restTime180:0.00} s (widest point {r.restRadius:0.0} m from the start); 0-15 m/s {r.t0to15:0.00} s, 0-20 m/s {r.t0to20:0.00} s");
            }
            return sb.ToString();
        }

        static void Place(Vector3 pos, float yaw, float speed)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            B.position = new Vector3(pos.x, car.GroundLift, pos.z); B.rotation = rot;
            B.transform.SetPositionAndRotation(B.position, rot);
            B.linearVelocity = rot * Vector3.forward * speed; B.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            C.ResetState(); C.Throttle = 0f; C.SteerInput = 0f;
            for (int i = 0; i < 10; i++) { C.Tick(Dt); Physics.Simulate(Dt); }     // settle on the ground
            B.linearVelocity = rot * Vector3.forward * speed; B.angularVelocity = Vector3.zero;
        }

        static void Step(float throttle, float steer)
        {
            C.Throttle = throttle; C.SteerInput = steer;
            C.Tick(Dt); Physics.Simulate(Dt);
        }

        static float Hold(float v) => Mathf.Clamp((v - C.ForwardSpeed) * 0.6f, -1f, 1f);
        static float Heading() => B.rotation.eulerAngles.y;

        static Result Profile(string name)
        {
            var r = new Result { name = name, mass = B.mass };
            foreach (float v in new[] { 6f, 10f, 15f, 20f })
            {
                Place(new Vector3(0f, 0f, 45f), 90f, v);
                float sumYaw = 0f, sumV = 0f; int n = 0;
                for (int i = 0; i < 250; i++)
                {
                    Step(Hold(v), 1f);
                    if (i >= 150) { sumYaw += Mathf.Abs(B.angularVelocity.y) * Mathf.Rad2Deg; sumV += B.linearVelocity.magnitude; n++; }
                }
                float yaw = sumYaw / n, sp = sumV / n;
                float t = Mathf.Clamp01(sp / Mathf.Max(0.1f, C.maxSpeed));
                float cmd = Mathf.Lerp(C.steerRateLow, C.steerRateHigh, t) * Mathf.Clamp01(sp / Mathf.Max(0.1f, C.steerFullAuthoritySpeed));
                r.steady[v] = (yaw, sp / (yaw * Mathf.Deg2Rad), cmd);
            }
            // steering onset at 15 m/s
            {
                Place(new Vector3(0f, 0f, 45f), 90f, 15f);
                var yaws = new List<float>();
                for (int i = 0; i < 150; i++) { Step(Hold(15f), 1f); yaws.Add(Mathf.Abs(B.angularVelocity.y)); }
                float final = yaws.Skip(100).Average();
                int k = yaws.FindIndex(y => y >= 0.9f * final);
                r.onset15 = k * Dt;
            }
            // U-turn at 15 m/s
            {
                Place(new Vector3(-30f, 0f, 20f), 90f, 15f);
                float h0 = Heading(), turned = 0f, last = h0, maxOff = 0f; float z0 = B.position.z; int i;
                for (i = 0; i < 500 && turned < 180f; i++)
                {
                    Step(Hold(15f), 1f);
                    float h = Heading(); turned += Mathf.Abs(Mathf.DeltaAngle(last, h)); last = h;
                    maxOff = Mathf.Max(maxOff, Mathf.Abs(B.position.z - z0));
                }
                r.uturnTime = i * Dt; r.uturnDiameter = maxOff; r.uturnSpeedEnd = B.linearVelocity.magnitude;
            }
            // from rest: full throttle, full lock
            {
                Place(new Vector3(-20f, 0f, 0f), 90f, 0f);
                Vector3 start = B.position; float turned = 0f, last = Heading(), far = 0f; float t90 = -1f, t180 = -1f;
                for (int i = 0; i < 500 && t180 < 0f; i++)
                {
                    Step(1f, 1f);
                    float h = Heading(); turned += Mathf.Abs(Mathf.DeltaAngle(last, h)); last = h;
                    far = Mathf.Max(far, Vector3.Distance(B.position, start));
                    if (t90 < 0f && turned >= 90f) t90 = i * Dt;
                    if (turned >= 180f) t180 = i * Dt;
                }
                r.restTime90 = t90; r.restTime180 = t180; r.restRadius = far;
            }
            // acceleration
            {
                Place(new Vector3(-45f, 0f, 0f), 90f, 0f);
                float t15 = -1f, t20 = -1f;
                for (int i = 0; i < 700 && t20 < 0f; i++)
                {
                    Step(1f, 0f);
                    float v = C.ForwardSpeed;
                    if (t15 < 0f && v >= 15f) t15 = i * Dt;
                    if (v >= 20f) t20 = i * Dt;
                }
                r.t0to15 = t15; r.t0to20 = t20;
            }
            return r;
        }
    }
}
