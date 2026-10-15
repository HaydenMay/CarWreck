using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Automated driving checks with real PhysX, stepped by script (Physics.Simulate at 50 Hz) in the editor:
    /// acceleration, braking, reverse, steering, stability, a full-speed crash into the wall (damage + persistence)
    /// and reset. This validates the simulation only; browser input, touch and the camera are checked separately.
    /// Run: Unity -batchmode -projectPath . -executeMethod CarWreck.Driving.EditorTools.DrivingValidation.Run
    /// </summary>
    public static class DrivingValidation
    {
        const float Dt = 0.02f;

        [MenuItem("CarWreck/Validate Driving", priority = 12)]
        public static void Run()
        {
            string text = Execute();
            Debug.Log(text);
        }

        static void Step(ArcadeCarController c, CollisionDamageReceiver r, int steps, float throttle, float steer, System.Action<int> each = null)
        {
            for (int i = 0; i < steps; i++)
            {
                c.Throttle = throttle;
                c.SteerInput = steer;
                c.Tick(Dt);
                Physics.Simulate(Dt);
                r.Step(Dt);
                each?.Invoke(i);
            }
        }

        static void Place(Rigidbody rb, float x, float z, float yaw)
        {
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            rb.position = new Vector3(x, rb.position.y, z);
            rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            rb.transform.SetPositionAndRotation(rb.position, rb.rotation);
            Physics.SyncTransforms();
        }

        static string Execute()
        {
            var sb = new StringBuilder("[Driving] Validation (real PhysX, Physics.Simulate at 50 Hz)\n");
            int fails = 0;
            void Check(string name, bool ok, string detail) { sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}"); if (!ok) fails++; }

            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<DrivingGame>();
            var c = game.controller;
            var rb = game.body;
            var model = game.model;
            var recv = game.receiver;
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                game.Setup();
                recv.logImpacts = false;
                var filters = model.GetComponentsInChildren<MeshFilter>();
                var baseVerts = filters.Select(f => f.sharedMesh.vertices.Clone() as Vector3[]).ToArray();

                // ---------------------------------------------------------------- settle
                Step(c, recv, 50, 0f, 0f);
                Check("Settles on the ground", c.IsGrounded && Mathf.Abs(rb.linearVelocity.magnitude) < 0.5f, $"grounded {c.IsGrounded}, speed {rb.linearVelocity.magnitude:0.00} m/s, y {rb.position.y:0.000}");

                // ---------------------------------------------------------------- accelerate
                var p0 = rb.position;
                float t1 = 0f, t2 = 0f, t3 = 0f;
                Step(c, recv, 200, 1f, 0f, i => { if (i == 49) t1 = c.ForwardSpeed; if (i == 99) t2 = c.ForwardSpeed; if (i == 199) t3 = c.ForwardSpeed; });
                float travelled = Vector3.Distance(p0, rb.position);
                Check("Accelerates forward", t1 > 8f && t3 > 22f, $"1 s {t1:0.0}, 2 s {t2:0.0}, 4 s {t3:0.0} m/s ({t3 * 3.6f:0} km/h), {travelled:0} m, lateral drift {Mathf.Abs(rb.position.x - p0.x):0.00} m");
                Check("Stays level at speed", rb.transform.up.y > 0.98f, $"up.y {rb.transform.up.y:0.000}");

                foreach (Transform o in GameObject.Find("Obstacles").transform) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);   // handling tests need open ground
                // ---------------------------------------------------------------- steering at speed
                Place(rb, 60f, -55f, -90f);
                Step(c, recv, 150, 1f, 0f);
                float yaw0 = rb.rotation.eulerAngles.y;
                Step(c, recv, 100, 1f, 1f);                       // 2 s full right
                float turned = Mathf.DeltaAngle(yaw0, rb.rotation.eulerAngles.y);
                Check("Steers right at speed", turned > 40f && rb.transform.up.y > 0.95f, $"turned {turned:0}° in 2 s, speed {rb.linearVelocity.magnitude:0.0} m/s, up.y {rb.transform.up.y:0.000}");
                float yaw1 = rb.rotation.eulerAngles.y;
                Step(c, recv, 100, 1f, -1f);
                float turnedL = Mathf.DeltaAngle(yaw1, rb.rotation.eulerAngles.y);
                Check("Steers left at speed", turnedL < -40f && rb.transform.up.y > 0.95f, $"turned {turnedL:0}° in 2 s, speed {rb.linearVelocity.magnitude:0.0} m/s, up.y {rb.transform.up.y:0.000}");
                Place(rb, 58f, -64f, -90f);
                Step(c, recv, 150, 1f, 0f);
                float vBefore = rb.linearVelocity.magnitude;
                Step(c, recv, 50, 0f, 1f);                        // 1 s full lock (towards open ground), coasting: how much speed does a turn cost?
                float vAfter = rb.linearVelocity.magnitude;
                Check("Turning does not bleed excessive speed", vAfter > vBefore * 0.7f, $"{vBefore:0.0} -> {vAfter:0.0} m/s over 1 s of full lock while coasting");
                float minUp = 1f;
                Place(rb, 55f, -62f, -90f);
                Step(c, recv, 150, 1f, 0f);
                Step(c, recv, 150, 1f, 1f, i => minUp = Mathf.Min(minUp, rb.transform.up.y));    // 3 s of full lock at speed
                Check("Does not flip in sustained full-lock turn", minUp > 0.85f, $"minimum up.y {minUp:0.000}, speed {rb.linearVelocity.magnitude:0.0} m/s");

                // ---------------------------------------------------------------- brake and reverse
                float peak = rb.linearVelocity.magnitude;
                int brakeSteps = 0;
                Step(c, recv, 250, -1f, 0f, i => { if (c.ForwardSpeed > 0.6f) brakeSteps = i + 1; });
                Check("Brakes to a stop", brakeSteps > 0 && brakeSteps < 200, $"from {peak:0.0} m/s stopped in {brakeSteps * Dt:0.00} s");
                Check("Reverses", c.ForwardSpeed < -5f, $"reverse speed {c.ForwardSpeed:0.0} m/s");
                Step(c, recv, 100, 0f, 0f);
                Check("Coasts to rest with no input", Mathf.Abs(c.ForwardSpeed) < 1f, $"forward speed {c.ForwardSpeed:0.00} m/s after 2 s");

                foreach (Transform o in GameObject.Find("Obstacles").transform) o.gameObject.SetActive(true);
                // ---------------------------------------------------------------- reset + crash
                game.ResetAll();
                Step(c, recv, 20, 0f, 0f);
                Check("Reset restores pose", Vector3.Distance(rb.position, new Vector3(0f, rb.position.y, -50f)) < 0.2f && model.ImpactCount == 0, $"position {rb.position.ToString("F2")}, impacts {model.ImpactCount}");
                int steps = 0;
                float peakSpeed = 0f;
                Step(c, recv, 600, 1f, 0f, i => { steps = i + 1; peakSpeed = Mathf.Max(peakSpeed, c.Speed); });
                var last = recv.Last;
                Check("Crash registers an impact on the car", recv.EventCount > 0 && model.ImpactCount > 0 && last.applied,
                      $"events {recv.EventCount}, impacts {model.ImpactCount}, other {last.other}, closing {last.closingSpeed:0.0} m/s ({last.closingSpeed * 3.6f:0} km/h), E {last.energy / 1000f:0} kJ, strength {last.strength * 100f:0}%, region {last.region}");
                float disp = 0f;
                for (int f = 0; f < filters.Length; f++)
                {
                    var now = filters[f].sharedMesh.vertices;
                    for (int v = 0; v < now.Length; v++) disp = Mathf.Max(disp, (now[v] - baseVerts[f][v]).magnitude);
                }
                Check("Existing deformation shows persistent mesh damage", disp > 0.15f, $"max vertex displacement {disp:0.00} m, deform step {last.deformMilliseconds:0.0} ms (editor timing, not WebGL)");
                Check("Car stays upright after the crash", rb.transform.up.y > 0.7f, $"up.y {rb.transform.up.y:0.00}, z {rb.position.z:0.0}");
                Step(c, recv, 50, 0f, 0f);
                float dispAfter = 0f;
                for (int f = 0; f < filters.Length; f++)
                {
                    var now = filters[f].sharedMesh.vertices;
                    for (int v = 0; v < now.Length; v++) dispAfter = Mathf.Max(dispAfter, (now[v] - baseVerts[f][v]).magnitude);
                }
                Check("Damage persists", dispAfter > 0.15f, $"max displacement 1 s later {dispAfter:0.00} m");

                // driving away from the wall works with a dented hull
                Step(c, recv, 100, -1f, 0f);
                Check("Still drivable when damaged", c.ForwardSpeed < -2f, $"reverse speed {c.ForwardSpeed:0.0} m/s");

                game.ResetAll();
                Step(c, recv, 5, 0f, 0f);
                float dispReset = 0f;
                for (int f = 0; f < filters.Length; f++)
                {
                    var now = filters[f].sharedMesh.vertices;
                    for (int v = 0; v < now.Length; v++) dispReset = Mathf.Max(dispReset, (now[v] - baseVerts[f][v]).magnitude);
                }
                Check("Reset repairs the car", dispReset < 0.01f && model.ImpactCount == 0, $"max displacement {dispReset:0.0000} m, impacts {model.ImpactCount}");

                // ---------------------------------------------------------------- dynamic crates
                var crates = GameObject.Find("Obstacles").transform.GetComponentsInChildren<Rigidbody>().Where(r => r.name == "Crate" && r.position.x > 30f && r.position.z > 20f).ToArray();
                var cratePos = crates.Select(r => r.position).ToArray();
                // drive from the south into the pyramid at (38,0,22)
                Place(rb, 38f, -12f, 0f);
                recv.ResetState();
                Step(c, recv, 400, 1f, 0f);
                float crateMoved = 0f;
                for (int k = 0; k < crates.Length; k++) crateMoved = Mathf.Max(crateMoved, Vector3.Distance(crates[k].position, cratePos[k]));
                Check("Plough through crates (dynamic obstacles move and dent the car)", crateMoved > 1f,
                      $"events {recv.EventCount}, applied impacts {model.ImpactCount}, largest crate move {crateMoved:0.0} m of {crates.Length} crates");
            }
            finally
            {
                game.ResetAll();
                recv.logImpacts = true;
                Physics.simulationMode = prev;
                model.Shutdown();
                EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);   // discard edit-mode changes
            }
            sb.AppendLine(fails == 0 ? "[Driving] ALL CHECKS PASSED" : $"[Driving] {fails} CHECK(S) FAILED");
            return sb.ToString();
        }
    }
}
