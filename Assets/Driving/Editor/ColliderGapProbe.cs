using System;
using System.Collections.Generic;
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
    /// Diagnostic (not part of the game): how far does a car's PHYSICAL front face sit from its VISIBLE (deformed) front face?
    /// Both are measured on the real objects, in the car's own space, along a grid of lanes across the nose
    /// (13 lanes x 3 heights):
    ///   collider z  = where a ray travelling backwards along the car meets the car's BoxColliders (real PhysX shapes, queried with Collider.Raycast)
    ///   visible z   = the front-most deformed vertex within 8 cm / 10 cm of the lane (the mesh that is drawn)
    ///   gap         = collider z - visible z      (+ = an invisible wall in front of the metal)
    ///   excess gap  = gap - the same lane's gap on the undamaged car (the undamaged shell is a plain box around a rounded nose)
    ///   crush       = undamaged visible z - visible z
    /// Every scenario runs twice with real PhysX (Physics.Simulate at 50 Hz): DrivingGame.refitColliders OFF (the arena before the fix:
    /// the shell stays undamaged) and ON. Measurements are runtime observations; "retraction" columns are read from the hitbox.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Driving.EditorTools.ColliderGapProbe.Run -quit -logFile probe.log
    /// </summary>
    public static class ColliderGapProbe
    {
        const float Dt = 0.02f;
        const int Lanes = 13;
        static readonly float[] HeightFrac = { 0.45f, 0.65f, 0.85f };
        static DrivingGame game;

        [MenuItem("CarWreck/Diagnostics/Collider Gap Probe", priority = 40)]
        public static void Run()
        {
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            var prev = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            VehicleDamageModel.ProgressiveInEditMode = Environment.GetEnvironmentVariable("CGP_PROGRESSIVE") == "1";   // progressive crushing on: one rendered frame per physics step
            var sb = new StringBuilder("[ColliderGapProbe] muscle car (player) vs compact pickup (target), head-on in open ground; metres; real PhysX, editor timings\n");
            try
            {
                game.Setup();
                foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);
                game.SelectPlayer(0); game.SelectTarget(2);
                game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false;
                foreach (bool refit in new[] { false, true }) RunConfig(refit, sb);
            }
            finally
            {
                VehicleDamageModel.ProgressiveInEditMode = false;
                game.refitColliders = true;
                Physics.simulationMode = prev;
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ scan

        class Scan { public readonly float[] vis = new float[Lanes * 3], col = new float[Lanes * 3]; public float[] xs = new float[Lanes]; }

        /// <summary>Visible and collider front-face z (car space) along the nose lanes.</summary>
        static Scan Measure(DrivingGame.Slot s)
        {
            var hb = s.rig.hitbox; var model = s.Model; var root = s.rig.root.transform;
            Bounds fl = hb.Find("FrontLeft").Outer, fr = hb.Find("FrontRight").Outer, fc = hb.Find("FrontCentre").Outer;
            float x0 = fl.min.x, x1 = fr.max.x, laneW = (x1 - x0) / Lanes, halfW = laneW * 0.4f;
            var r = new Scan();
            for (int i = 0; i < r.vis.Length; i++) { r.vis[i] = float.NaN; r.col[i] = float.NaN; }
            for (int l = 0; l < Lanes; l++) r.xs[l] = x0 + (l + 0.5f) * laneW;
            float[] ys = HeightFrac.Select(f => fc.min.y + f * fc.size.y).ToArray();
            foreach (var part in model.Parts)
            {
                int n = part.VehiclePositions.Length;
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = part.DeformedVehiclePosition(i);
                    int l = Mathf.FloorToInt((v.x - x0) / laneW);
                    if (l < 0 || l >= Lanes || Mathf.Abs(v.x - r.xs[l]) > halfW) continue;
                    for (int h = 0; h < ys.Length; h++)
                        if (Mathf.Abs(v.y - ys[h]) < 0.1f)
                        {
                            int k = l * 3 + h;
                            if (float.IsNaN(r.vis[k]) || v.z > r.vis[k]) r.vis[k] = v.z;
                        }
                }
            }
            var boxes = s.rig.root.GetComponentsInChildren<BoxCollider>().Where(b => b.enabled && !b.isTrigger).ToArray();
            for (int l = 0; l < Lanes; l++)
                for (int h = 0; h < ys.Length; h++)
                {
                    var ray = new Ray(root.TransformPoint(new Vector3(r.xs[l], ys[h], fc.max.z + 4f)), root.TransformDirection(Vector3.back));
                    float best = float.PositiveInfinity;
                    foreach (var b in boxes) if (b.Raycast(ray, out var hit, 12f) && hit.distance < best) { best = hit.distance; r.col[l * 3 + h] = root.InverseTransformPoint(hit.point).z; }
                }
            return r;
        }

        struct Stat
        {
            public float crushMax, crushMean, gapMax, gapMean, gapAll; public int dented;
            public override string ToString() => dented == 0 ? $"no crushed lanes (all-lane excess gap {gapAll:0.000})"
                : $"crush max {crushMax:0.000} / mean {crushMean:0.000}  |  excess gap max {gapMax:0.000} / mean {gapMean:0.000} over {dented} crushed lanes (all lanes {gapAll:0.000})";
        }

        static Stat Compare(Scan undamaged, Scan now)
        {
            var st = new Stat(); float all = 0f; int allN = 0, nd = 0;
            for (int k = 0; k < now.vis.Length; k++)
            {
                if (float.IsNaN(undamaged.vis[k]) || float.IsNaN(now.vis[k]) || float.IsNaN(now.col[k]) || float.IsNaN(undamaged.col[k])) continue;
                float crush = undamaged.vis[k] - now.vis[k];
                float excess = (now.col[k] - now.vis[k]) - (undamaged.col[k] - undamaged.vis[k]);
                all += excess; allN++;
                if (crush > 0.02f) { nd++; st.crushMean += crush; st.gapMean += excess; st.crushMax = Mathf.Max(st.crushMax, crush); st.gapMax = Mathf.Max(st.gapMax, excess); }
            }
            st.dented = nd; st.gapAll = allN > 0 ? all / allN : 0f;
            if (nd > 0) { st.crushMean /= nd; st.gapMean /= nd; }
            return st;
        }

        static string Faces(DrivingGame.Slot s)
        {
            var moved = s.rig.hitbox.Faces.Where(f => f.original - f.current > 0.01f).Select(f => $"{f.Label} -{(f.original - f.current) * 100f:0}cm").ToArray();
            return moved.Length == 0 ? "no face retracted" : string.Join(", ", moved);
        }

        // ------------------------------------------------------------------ harness

        static CollisionDamageReceiver.Report? repP, repT;
        static readonly List<float> refitMs = new List<float>(), refitKb = new List<float>();
        static int refitFacesMax;

        static void Sim(float seconds, bool pushWithPlayer = false, Action<int> each = null)
        {
            var hbP = game.Player.rig.hitbox; var hbT = game.Target.rig.hitbox;
            int steps = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < steps; i++)
            {
                if (pushWithPlayer) { game.Player.controller.Throttle = 1f; game.Player.controller.SteerInput = 0f; game.Player.controller.Tick(Dt); }
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt);
                game.Player.Model.AdvanceCrush(Dt); game.Target.Model.AdvanceCrush(Dt);
                int uP = hbP.UpdateCount, uT = hbT.UpdateCount;
                long b0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                game.AfterPhysicsStep(Dt);
                long bytes = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - b0;
                if (hbP.UpdateCount != uP || hbT.UpdateCount != uT)
                {
                    var hb = hbP.UpdateCount != uP ? hbP : hbT;
                    refitMs.Add(hb.LastUpdateMilliseconds); refitKb.Add(bytes / 1024f); refitFacesMax = Mathf.Max(refitFacesMax, hb.LastRefitFaces);
                }
                each?.Invoke(i);
            }
        }

        static void Place(Rigidbody b, Vector3 pos, float yaw, Vector3 vel)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
            b.transform.SetPositionAndRotation(b.position, rot);
            b.linearVelocity = vel; b.angularVelocity = Vector3.zero;
        }

        /// <summary>Both cars facing each other 9 m apart (centres), each at half the closing speed; the player offset sideways by <paramref name="offsetX"/>.</summary>
        static void Launch(float closingKmh, float offsetX = 0f)
        {
            float v = closingKmh / 3.6f * 0.5f;
            Place(game.Target.Body, new Vector3(0f, 0f, 4.5f), 180f, Vector3.back * v);
            Place(game.Player.Body, new Vector3(offsetX, 0f, -4.5f), 0f, Vector3.forward * v);
            Physics.SyncTransforms();
            game.Player.Receiver.ClearCooldowns(); game.Target.Receiver.ClearCooldowns();
            game.Player.controller.ResetState();
            repP = repT = null;
        }

        static string Contact(CollisionDamageReceiver.Report? r) => r == null ? "no hit" :
            $"contact z {r.Value.contactPoint.z:0.000} pen {r.Value.penetration:0.000} travel {r.Value.footprint.travel:0.000} str {r.Value.strength * 100f:0}%";

        static void RunConfig(bool refit, StringBuilder sb)
        {
            game.refitColliders = refit;
            game.ResetAll();
            Action<CollisionDamageReceiver.Report> hp = r => { if (r.applied && repP == null) repP = r; };
            Action<CollisionDamageReceiver.Report> ht = r => { if (r.applied && repT == null) repT = r; };
            game.Player.Receiver.ImpactRegistered += hp; game.Target.Receiver.ImpactRegistered += ht;
            refitMs.Clear(); refitKb.Clear(); refitFacesMax = 0;
            sb.AppendLine($"\n=== refitColliders {(refit ? "ON  (after the fix)" : "OFF (arena before the fix)")} ===");
            try
            {
                var P = game.Player; var T = game.Target;
                var refP = Measure(P); var refT = Measure(T);
                int dentedBase = Compare(refT, refT).dented;
                float base0 = 0f; int n0 = 0;
                for (int k = 0; k < refT.vis.Length; k++) if (!float.IsNaN(refT.vis[k]) && !float.IsNaN(refT.col[k])) { base0 += refT.col[k] - refT.vis[k]; n0++; }
                sb.AppendLine($"1 Undamaged: pickup nose box-to-metal gap (mean over {n0} lanes) {base0 / Mathf.Max(1, n0):0.000} m (the shell is a box round a rounded nose; every later gap below is measured relative to this) {(dentedBase == 0 ? "" : "?")}");

                void Report(string label)
                {
                    sb.AppendLine($"   {label}");
                    sb.AppendLine($"     pickup: {Compare(refT, Measure(T))}  [{Faces(T)}]");
                    sb.AppendLine($"     muscle: {Compare(refP, Measure(P))}  [{Faces(P)}]");
                }

                Launch(60f); Sim(1.4f);
                Report($"2 Moderate frontal (60 km/h closing): pickup {Contact(repT)}; muscle {Contact(repP)}");

                game.ResetAll(); Launch(170f); Sim(1.4f);
                Report($"3 Severe frontal (170 km/h closing): pickup {Contact(repT)}; muscle {Contact(repP)}");

                game.ResetAll();
                for (int hit = 1; hit <= 4; hit++)
                {
                    Launch(100f); Sim(1.4f);
                    Report($"4 Repeated frontal, hit {hit} (100 km/h closing): pickup {Contact(repT)}; muscle {Contact(repP)}");
                }
                var repeatedP = Measure(P); var repeatedT = Measure(T);

                // Two damaged cars pressed nose to nose (damage disabled for the push so only the existing shapes matter).
                float thP = P.Receiver.energyThreshold, thT = T.Receiver.energyThreshold;
                P.Receiver.energyThreshold = T.Receiver.energyThreshold = 1e12f;
                float maxAngP = 0f, maxAngT = 0f, maxUp = 0f, maxSpeedT = 0f; bool nan = false;
                Place(T.Body, new Vector3(0f, 0f, 3.5f), 180f, Vector3.zero);
                Place(P.Body, new Vector3(0f, 0f, -3.5f), 0f, Vector3.zero);
                Physics.SyncTransforms();
                Sim(3f, true, i =>
                {
                    maxAngP = Mathf.Max(maxAngP, P.Body.angularVelocity.magnitude); maxAngT = Mathf.Max(maxAngT, T.Body.angularVelocity.magnitude);
                    maxUp = Mathf.Max(maxUp, Mathf.Abs(P.Body.linearVelocity.y), Mathf.Abs(T.Body.linearVelocity.y));
                    maxSpeedT = Mathf.Max(maxSpeedT, T.Body.linearVelocity.magnitude);
                    nan |= float.IsNaN(P.Body.position.x) || float.IsNaN(T.Body.position.x);
                });
                sb.AppendLine($"6 Two damaged cars pressed nose to nose (muscle drives into the pickup, 3 s): nose-to-nose separation (m, + = daylight, - = overlap): {NoseGap(P, T)}; " +
                              $"pickup pushed to {T.Body.linearVelocity.magnitude:0.0} m/s, max angular speed {maxAngP:0.00}/{maxAngT:0.00} rad/s, max vertical speed {maxUp:0.00} m/s, NaN {nan}");
                P.Receiver.energyThreshold = thP; T.Receiver.energyThreshold = thT;

                game.ResetAll(); Launch(100f, 0.9f); Sim(1.4f);
                Report($"5 Offset frontal (100 km/h closing, player shifted 0.9 m): pickup {Contact(repT)}; muscle {Contact(repP)}");

                // Narrow dent: a thin heavy pole into the middle of the parked pickup's nose.
                game.ResetAll();
                var refT2 = Measure(T);
                var pole = new GameObject("GapProbePole"); var bc = pole.AddComponent<BoxCollider>(); bc.size = new Vector3(0.15f, 0.8f, 0.15f);
                var prb = pole.AddComponent<Rigidbody>(); prb.mass = 1500f; prb.useGravity = false; prb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var root = T.rig.root.transform; var fc = T.rig.hitbox.Find("FrontCentre").Outer;
                pole.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(0f, fc.center.y, fc.max.z + 6f)), root.rotation);
                prb.linearVelocity = -root.forward * 14f; Physics.SyncTransforms();
                repT = null; Sim(1.4f);
                UnityEngine.Object.DestroyImmediate(pole);
                var narrow = Measure(T);
                sb.AppendLine($"8 Narrow dent (0.15 m pole, 14 m/s, pickup centre): pickup {Contact(repT)}  [{Faces(T)}]");
                sb.AppendLine($"     {Compare(refT2, narrow)}");
                sb.AppendLine("     per lane (x, crush, excess gap) at mid height: " + string.Join("  ", Enumerable.Range(0, Lanes).Select(l => $"{narrow.xs[l]:0.0}:{refT2.vis[l * 3 + 1] - narrow.vis[l * 3 + 1]:0.00}/{(narrow.col[l * 3 + 1] - narrow.vis[l * 3 + 1]) - (refT2.col[l * 3 + 1] - refT2.vis[l * 3 + 1]):0.00}")));

                // Narrow dent in a long side region: the same pole into the middle of the pickup's left side.
                game.ResetAll();
                var sidePole = new GameObject("GapProbePole"); var sbc = sidePole.AddComponent<BoxCollider>(); sbc.size = new Vector3(0.15f, 0.8f, 0.15f);
                var srb = sidePole.AddComponent<Rigidbody>(); srb.mass = 1500f; srb.useGravity = false; srb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var lm = T.rig.hitbox.Find("LeftMid").Outer;
                sidePole.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(lm.min.x - 6f, lm.center.y, lm.center.z)), root.rotation);
                srb.linearVelocity = root.right * 14f; Physics.SyncTransforms();
                repT = null; Sim(1.4f);
                UnityEngine.Object.DestroyImmediate(sidePole);
                sb.AppendLine($"8b Narrow dent in a side region (same pole into the middle of the pickup's left side, region {lm.size.z:0.00} m long): pickup {Contact(repT)}; deepest dent {MaxCrush(T):0.000} m; [{Faces(T)}]");

                // Core limit: severe hits repeated until the dent is deeper than the region depth.
                game.ResetAll();
                var refP3 = Measure(P); var refT3 = Measure(T);
                var fcT = T.rig.hitbox.Find("FrontCentre"); var fcP = P.rig.hitbox.Find("FrontCentre");
                sb.AppendLine($"7 Core limit: severe hits (170 km/h closing) until the dent is deeper than the nose section. Core front plane (car space): pickup z {fcT.Outer.min.z:0.000} (nose {fcT.Outer.max.z:0.000}), muscle z {fcP.Outer.min.z:0.000} (nose {fcP.Outer.max.z:0.000})");
                for (int hit = 1; hit <= 6; hit++)
                {
                    Launch(170f); Sim(1.4f);
                    var sT = Measure(T); var sP = Measure(P);
                    var cT = Compare(refT3, sT); var cP = Compare(refP3, sP);
                    var faceT = T.rig.hitbox.FaceOf("FrontCentre", 2, +1f); var faceP = P.rig.hitbox.FaceOf("FrontCentre", 2, +1f);
                    int lane = Lanes / 2;
                    sb.AppendLine($"   hit {hit}: pickup crush max {cT.crushMax:0.000}, excess gap max {cT.gapMax:0.000}, centre box face z {faceT.current:0.000} (limit {faceT.inner + T.rig.hitbox.minDepth:0.000}), centre metal z {sT.vis[lane * 3 + 1]:0.000} | " +
                                  $"muscle crush max {cP.crushMax:0.000}, excess gap max {cP.gapMax:0.000}, centre box face z {faceP.current:0.000} (limit {faceP.inner + P.rig.hitbox.minDepth:0.000}), centre metal z {sP.vis[lane * 3 + 1]:0.000}");
                }

                // Walls: the player into the north wall, then the pickup pinned between the player and the wall (driven, 3 s).
                {
                    game.ResetAll();
                    var wallRep = new System.Text.StringBuilder();
                    float wMaxAng = 0f, wMaxUp = 0f, wMaxSpeed = 0f; bool wNan = false;
                    Action<int> watchW = i =>
                    {
                        wMaxAng = Mathf.Max(wMaxAng, P.Body.angularVelocity.magnitude, T.Body.angularVelocity.magnitude);
                        wMaxUp = Mathf.Max(wMaxUp, Mathf.Abs(P.Body.linearVelocity.y), Mathf.Abs(T.Body.linearVelocity.y));
                        wMaxSpeed = Mathf.Max(wMaxSpeed, P.Body.linearVelocity.magnitude, T.Body.linearVelocity.magnitude);
                        wNan |= float.IsNaN(P.Body.position.x) || float.IsNaN(T.Body.position.x);
                    };
                    Place(T.Body, new Vector3(-40f, 0f, -40f), 0f, Vector3.zero);
                    Place(P.Body, new Vector3(30f, 0f, 40f), 0f, Vector3.forward * (80f / 3.6f));
                    Physics.SyncTransforms(); P.Receiver.ClearCooldowns(); T.Receiver.ClearCooldowns(); P.controller.ResetState();
                    Sim(3f, true, watchW);
                    float wallRetract = P.rig.hitbox.Faces.Max(f => f.original - f.current);
                    sb.AppendLine($"9a Player into the north wall at 80 km/h, then driving on into it (3 s): player impacts {P.Model.ImpactCount}, deepest dent {MaxCrush(P):0.000} m, shell retracted {wallRetract:0.000} m, refits {P.refits}, crushing now {P.Model.IsCrushing}; " +
                                  $"max angular speed {wMaxAng:0.00} rad/s, max vertical speed {wMaxUp:0.00} m/s, max speed {wMaxSpeed * 3.6f:0} km/h, NaN {wNan}, player speed now {P.Body.linearVelocity.magnitude * 3.6f:0.0} km/h, position z {P.Body.position.z:0.00} (wall at 70)");

                    game.ResetAll(); wMaxAng = wMaxUp = wMaxSpeed = 0f; wNan = false;
                    Place(T.Body, new Vector3(0f, 0f, 66.3f), 0f, Vector3.zero);
                    Place(P.Body, new Vector3(0f, 0f, 55f), 0f, Vector3.forward * (50f / 3.6f));
                    Physics.SyncTransforms(); P.Receiver.ClearCooldowns(); T.Receiver.ClearCooldowns(); P.controller.ResetState();
                    Sim(4f, true, watchW);
                    float tR = T.rig.hitbox.Faces.Max(f => f.original - f.current), pR = P.rig.hitbox.Faces.Max(f => f.original - f.current);
                    sb.AppendLine($"9b Pickup pinned between the muscle car (50 km/h, then driven on 4 s) and the wall: impacts player {P.Model.ImpactCount} / pickup {T.Model.ImpactCount}, deepest dents {MaxCrush(P):0.000} / {MaxCrush(T):0.000} m, " +
                                  $"shells retracted {pR:0.000} / {tR:0.000} m, refits {P.refits} / {T.refits}; max angular speed {wMaxAng:0.00} rad/s, max vertical speed {wMaxUp:0.00} m/s, NaN {wNan}, speeds now {P.Body.linearVelocity.magnitude * 3.6f:0.0} / {T.Body.linearVelocity.magnitude * 3.6f:0.0} km/h, " +
                                  $"pickup z {T.Body.position.z:0.00}, player z {P.Body.position.z:0.00}, crushing {P.Model.IsCrushing}/{T.Model.IsCrushing}");
                }

                if (refitMs.Count > 0)
                    sb.AppendLine($"Refit cost this configuration: {refitMs.Count} refits, {refitMs.Average():0.000} ms avg / {refitMs.Max():0.000} ms max, {refitKb.Average():0.0} KB allocated avg / {refitKb.Max():0.0} KB max, most faces refitted at once {refitFacesMax}; refits applied player {P.refits} / target {T.refits}");
                else sb.AppendLine("Refit cost this configuration: no refits (refitColliders off)");
            }
            finally
            {
                game.Player.Receiver.ImpactRegistered -= hp; game.Target.Receiver.ImpactRegistered -= ht;
            }
        }

        /// <summary>
        /// Nose-to-nose separation along the line between two cars, in metres (+ = daylight, - = overlap): of the front-most visible metal in the
        /// centre lane at any height ("band"), at each of the three scan heights ("lanes"), and of the colliders (centre lane, mid height).
        /// </summary>
        static string NoseGap(DrivingGame.Slot a, DrivingGame.Slot b)
        {
            Vector3 d = b.Body.position - a.Body.position; d.y = 0f; d.Normalize();
            float[] aMax = { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            float[] bMin = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            foreach (var (s, isA) in new[] { (a, true), (b, false) })
            {
                var root = s.rig.root.transform; var fc = s.rig.hitbox.Find("FrontCentre").Outer;
                foreach (var part in s.Model.Parts)
                {
                    int n = part.VehiclePositions.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 v = part.DeformedVehiclePosition(i);
                        if (Mathf.Abs(v.x) > 0.08f) continue;
                        float p = float.NaN;
                        bool inBand = v.y >= fc.min.y + 0.3f * fc.size.y && v.y <= fc.min.y + 0.9f * fc.size.y;
                        for (int h = -1; h < 3; h++)
                        {
                            bool inWin = h < 0 ? inBand : Mathf.Abs(v.y - (fc.min.y + HeightFrac[h] * fc.size.y)) < 0.1f;
                            if (!inWin) continue;
                            if (float.IsNaN(p)) p = Vector3.Dot(root.TransformPoint(v), d);
                            int k = h + 1;
                            if (isA) aMax[k] = Mathf.Max(aMax[k], p); else bMin[k] = Mathf.Min(bMin[k], p);
                        }
                    }
                }
            }
            float ColliderFront(DrivingGame.Slot s, bool isA)
            {
                var root = s.rig.root.transform; var fc = s.rig.hitbox.Find("FrontCentre").Outer;
                var boxes = s.rig.root.GetComponentsInChildren<BoxCollider>().Where(c => c.enabled && !c.isTrigger).ToArray();
                var ray = new Ray(root.TransformPoint(new Vector3(0f, fc.min.y + HeightFrac[1] * fc.size.y, fc.max.z + 4f)), root.TransformDirection(Vector3.back));
                float best = float.PositiveInfinity; Vector3 pt = Vector3.zero;
                foreach (var c in boxes) if (c.Raycast(ray, out var hit, 12f) && hit.distance < best) { best = hit.distance; pt = hit.point; }
                return Vector3.Dot(pt, d);
            }
            float colGap = ColliderFront(b, false) - ColliderFront(a, true);
            return $"front-most metal at any height {bMin[0] - aMax[0]:0.000}; at heights low/mid/high {bMin[1] - aMax[1]:0.000} / {bMin[2] - aMax[2]:0.000} / {bMin[3] - aMax[3]:0.000}; colliders (mid height) {colGap:0.000}";
        }

        /// <summary>Largest distance any vertex of the car has moved from its undamaged position (m).</summary>
        static float MaxCrush(DrivingGame.Slot s)
        {
            float m = 0f;
            foreach (var part in s.Model.Parts)
                for (int i = 0; i < part.VehiclePositions.Length; i++) m = Mathf.Max(m, (part.DeformedVehiclePosition(i) - part.VehiclePositions[i]).magnitude);
            return m;
        }
    }
}
