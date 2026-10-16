using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Progressive crushing checks (scenarios A-N of the Progressive Crushing V1 brief). Real PhysX in the DriveArena, stepped by script
    /// (Physics.Simulate at 50 Hz); one simulated rendered frame per physics step advances the crush (VehicleDamageModel.AdvanceCrush, which
    /// Update calls in play mode). Final shapes are compared with an instant (non-progressive) replay of the same impact records, which is
    /// exactly what the engine showed before progressive crushing. Editor timings only; WebGL is not measured here.
    /// Run: Unity -batchmode -projectPath . -executeMethod CarWreck.Driving.EditorTools.ProgressiveCrushValidation.Run -quit
    /// </summary>
    public static class ProgressiveCrushValidation
    {
        const float Dt = 0.02f;
        const float ExactTolerance = 1e-6f;      // mesh space, m: instant replay vs progressive final (same arithmetic, so bit-identical is expected)
        const float LogicalTolerance = 1e-4f;    // vehicle space, m: displayed vs DeformedVehiclePosition through a recomposed matrix

        [MenuItem("CarWreck/Validate Progressive Crush", priority = 13)]
        public static void Run() => Debug.Log(Execute());

        static DrivingGame game;
        static StringBuilder sb;
        static int fails;
        static readonly Dictionary<string, string> verdicts = new Dictionary<string, string>();

        static void Check(string name, bool ok, string detail) { sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}"); if (!ok) fails++; }

        static void Verdict(string test, bool ok)
        {
            string v = ok ? "PASS" : "FAIL";
            verdicts[test] = verdicts.TryGetValue(test, out var old) && old == "FAIL" ? "FAIL" : v;
        }

        // ------------------------------------------------------------------ mesh helpers

        /// <summary>The meshes currently on screen, per part (mesh space).</summary>
        static Vector3[][] Displayed(VehicleDamageModel m) => m.Parts.Select(p => p.RuntimeMesh.vertices).ToArray();

        static Matrix4x4 MeshToVehicle(VehicleDamageModel m, DeformablePart p) => m.transform.worldToLocalMatrix * p.filter.transform.localToWorldMatrix;

        /// <summary>Largest distance between two per-part vertex sets (same parts, same order).</summary>
        static float MaxDiff(Vector3[][] a, Vector3[][] b)
        {
            float d = 0f;
            for (int p = 0; p < a.Length; p++)
                for (int i = 0; i < a[p].Length; i++) d = Mathf.Max(d, (a[p][i] - b[p][i]).magnitude);
            return d;
        }

        /// <summary>Displayed mesh vs the solver's logical state (DeformedVehiclePosition), vehicle space.</summary>
        static float DisplayedVsLogical(VehicleDamageModel m)
        {
            float d = 0f;
            for (int p = 0; p < m.Parts.Count; p++)
            {
                var part = m.Parts[p]; var M = MeshToVehicle(m, part); var shown = part.RuntimeMesh.vertices;
                for (int i = 0; i < shown.Length; i++) d = Mathf.Max(d, (M.MultiplyPoint3x4(shown[i]) - part.DeformedVehiclePosition(i)).magnitude);
            }
            return d;
        }

        /// <summary>Displayed mesh vs the imported source mesh (0 = undamaged on screen).</summary>
        static float DisplayedVsSource(VehicleDamageModel m)
        {
            float d = 0f;
            foreach (var p in m.Parts)
            {
                var a = p.RuntimeMesh.vertices; var b = p.sourceMesh.vertices;
                for (int i = 0; i < a.Length; i++) d = Mathf.Max(d, (a[i] - b[i]).magnitude);
            }
            return d;
        }

        static bool NormalsEqual(VehicleDamageModel a, VehicleDamageModel b, out float worst)
        {
            worst = 0f;
            for (int p = 0; p < a.Parts.Count; p++)
            {
                var na = a.Parts[p].RuntimeMesh.normals; var nb = b.Parts[p].RuntimeMesh.normals;
                for (int i = 0; i < na.Length; i++) worst = Mathf.Max(worst, (na[i] - nb[i]).magnitude);
            }
            return worst <= ExactTolerance;
        }

        /// <summary>
        /// Progress of a ramp from <paramref name="before"/> to <paramref name="final"/> as shown in <paramref name="frame"/>: the travel-weighted
        /// mean fraction of each moving vertex's path, optionally only for vertices whose depth behind the impact surface is in [d0, d1).
        /// </summary>
        static float Progress(VehicleDamageModel m, Vector3[][] before, Vector3[][] final, Vector3[][] frame, LocalImpact im = default, float d0 = float.NegativeInfinity, float d1 = float.PositiveInfinity)
        {
            double num = 0, den = 0;
            Vector3 dir = im.direction.sqrMagnitude > 0f ? im.direction.normalized : Vector3.zero;
            for (int p = 0; p < before.Length; p++)
            {
                var vp = m.Parts[p].VehiclePositions;
                for (int i = 0; i < before[p].Length; i++)
                {
                    Vector3 path = final[p][i] - before[p][i];
                    float len = path.magnitude;
                    if (len < 1e-3f) continue;
                    if (dir != Vector3.zero) { float depth = Vector3.Dot(vp[i] - im.point, dir); if (depth < d0 || depth >= d1) continue; }
                    num += Vector3.Dot(frame[p][i] - before[p][i], path) / len;
                    den += len;
                }
            }
            return den > 0 ? (float)(num / den) : 1f;
        }

        /// <summary>Vertices that moved backwards along their path between two consecutive frames (more than 0.1 mm).</summary>
        static int Backwards(Vector3[][] before, Vector3[][] final, Vector3[][] a, Vector3[][] b)
        {
            int n = 0;
            for (int p = 0; p < before.Length; p++)
                for (int i = 0; i < before[p].Length; i++)
                {
                    Vector3 path = final[p][i] - before[p][i];
                    float len = path.magnitude;
                    if (len < 1e-3f) continue;
                    if (Vector3.Dot(b[p][i] - a[p][i], path / len) < -1e-4f) n++;
                }
            return n;
        }

        // ------------------------------------------------------------------ standalone rigs (deterministic, no physics)

        static readonly List<VehicleRig.Rig> rigs = new List<VehicleRig.Rig>();

        static ProgressiveCrushSettings Settings(bool on) => new ProgressiveCrushSettings
        {
            enabled = on, minDuration = game.crushTimeLight, maxDuration = game.crushTimeHeavy, fullTravel = game.crushHeavyTravel, stagger = game.crushStagger,
        };

        static VehicleRig.Rig Rig(VehicleDefinition def, bool progressive)
        {
            var rig = VehicleRig.Build(def, null, new Vector3(400f + 12f * rigs.Count, -200f, 400f), game.playerRig);
            rig.body.isKinematic = true;   // never simulated: these cars only replay impact records
            rig.model.impactCapacityScale = game.crushCapacityScale;
            rig.model.progressiveCrush = Settings(progressive);
            rig.model.Initialize();
            rig.receiver.logImpacts = false;
            rigs.Add(rig);
            return rig;
        }

        static void DestroyRigs()
        {
            foreach (var r in rigs) { if (r.model != null) r.model.Shutdown(); if (r.root != null) UnityEngine.Object.DestroyImmediate(r.root); }
            rigs.Clear();
        }

        /// <summary>The instant result for an impact list: what the engine displayed before progressive crushing.</summary>
        static VehicleRig.Rig InstantReplay(VehicleDefinition def, IEnumerable<LocalImpact> impacts)
        {
            var rig = Rig(def, false);
            foreach (var im in impacts) rig.model.AddImpact(im);
            return rig;
        }

        static int TickUntilDone(VehicleDamageModel m, float dt, List<float> ms = null, List<long> bytes = null)
        {
            int ticks = 0;
            while (m.IsCrushing && ticks < 1000)
            {
                long b0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                m.AdvanceCrush(dt);
                long b1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
                ms?.Add(m.LastCrushFrameMilliseconds); bytes?.Add(b1 - b0);
                ticks++;
            }
            return ticks;
        }

        static void TickFraction(VehicleDamageModel m, float fraction, float dt)
        {
            float target = fraction * m.CrushDuration;
            float t = 0f;
            while (m.IsCrushing && t + dt <= target + 1e-6f) { m.AdvanceCrush(dt); t += dt; }
        }

        // ------------------------------------------------------------------ arena scenarios (real PhysX)

        class Trace
        {
            public string label;
            public DrivingGame.Slot car;
            public int hitStep = -1, endStep = -1, refitStep = -1, crushStarts, ticks;
            public float duration, travel, maxJump, lastJump, firstFrame = -1f, front = -1f, back = -1f, maxGapDuring = float.NegativeInfinity, minGapDuring = float.PositiveInfinity;
            public int backwards, refitDuringCrush, restartedDuringCrush;
            public float[] facesAtHit;
            public bool shellMovedDuringCrush;
            public Vector3[][] before, final;
            public LocalImpact impact;
            public readonly List<Vector3[][]> frames = new List<Vector3[][]>();
            public readonly List<float> ms = new List<float>();
            public bool shownInstantly;
            public float logicalDiff, replayDiff = -1f, replayNormals = -1f;
            public bool Hit => hitStep >= 0;
        }

        static void Place(Rigidbody b, Vector3 pos, float yaw, Vector3 vel)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            b.position = new Vector3(pos.x, b.position.y, pos.z); b.rotation = rot;
            b.transform.SetPositionAndRotation(b.position, rot);
            b.linearVelocity = vel; b.angularVelocity = Vector3.zero;
        }

        static void Arm()
        {
            Physics.SyncTransforms();
            game.Player.Receiver.ClearCooldowns(); game.Target.Receiver.ClearCooldowns();
            if (game.Player.controller != null) game.Player.controller.ResetState();
        }

        /// <summary>Front face of the car's physics shell minus the front-most DISPLAYED metal, centre lane at mid height (car space, m;
        /// + = the box is in front of the metal). Only meaningful for frontal hits.</summary>
        static float ShownGap(DrivingGame.Slot s)
        {
            var root = s.rig.root.transform; var fc = s.rig.hitbox.Find("FrontCentre").Outer;
            float y = fc.min.y + 0.65f * fc.size.y, best = float.PositiveInfinity, col = float.NaN, vis = float.NegativeInfinity;
            var ray = new Ray(root.TransformPoint(new Vector3(0f, y, fc.max.z + 4f)), root.TransformDirection(Vector3.back));
            foreach (var b in s.rig.root.GetComponentsInChildren<BoxCollider>())
                if (b.enabled && !b.isTrigger && b.Raycast(ray, out var h, 12f) && h.distance < best) { best = h.distance; col = root.InverseTransformPoint(h.point).z; }
            foreach (var part in s.Model.Parts)
            {
                var M = MeshToVehicle(s.Model, part); var shown = part.RuntimeMesh.vertices;
                for (int i = 0; i < shown.Length; i++)
                {
                    Vector3 v = M.MultiplyPoint3x4(shown[i]);
                    if (Mathf.Abs(v.x) < 0.08f && Mathf.Abs(v.y - y) < 0.1f) vis = Mathf.Max(vis, v.z);
                }
            }
            return col - vis;
        }

        /// <summary>Simulate <paramref name="seconds"/> and trace the crush of each watched car's FIRST damaging hit.</summary>
        static void Sim(float seconds, bool drive, bool frontGap, params Trace[] watch)
        {
            int steps = Mathf.RoundToInt(seconds / Dt);
            var counts = watch.Select(w => w.car.Model.TotalImpactCount).ToArray();
            var refits = watch.Select(w => w.car.refits).ToArray();
            for (int step = 0; step < steps; step++)
            {
                if (drive)
                {
                    var c = game.Player.controller; c.Throttle = 0f; c.SteerInput = 0f; c.Tick(Dt);
                    if (game.Target.controller != null) { game.Target.driver?.Drive(); game.Target.enemy?.Drive(Dt); game.Target.controller.Tick(Dt); }
                }
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt);   // impacts are added here (FixedUpdate at runtime)
                for (int k = 0; k < watch.Length; k++)
                {
                    var w = watch[k]; var m = w.car.Model;
                    if (m.TotalImpactCount == counts[k]) continue;
                    counts[k] = m.TotalImpactCount;
                    w.crushStarts++;
                    if (w.Hit) { if (w.endStep < 0) w.restartedDuringCrush++; continue; }
                    // The new state is solved, but nothing is uploaded until the next rendered frame: the screen still shows the pre-hit mesh.
                    w.hitStep = step;
                    w.before = Displayed(m);
                    w.impact = m.Impacts[m.ImpactCount - 1];
                    w.duration = m.CrushDuration; w.travel = m.CrushTravel;
                    w.shownInstantly = !m.IsCrushing && DisplayedVsLogical(m) <= LogicalTolerance;
                    w.facesAtHit = w.car.rig.hitbox.Faces.Select(f => f.current).ToArray();
                }
                // One rendered frame (Update in play mode).
                game.Player.Model.AdvanceCrush(Dt); game.Target.Model.AdvanceCrush(Dt);
                foreach (var w in watch)
                {
                    if (!w.Hit || w.endStep >= 0) continue;
                    w.frames.Add(Displayed(w.car.Model));
                    w.ms.Add(w.car.Model.LastCrushFrameMilliseconds);
                    if (frontGap && w.car.Model.IsCrushing) { float g = ShownGap(w.car); w.maxGapDuring = Mathf.Max(w.maxGapDuring, g); w.minGapDuring = Mathf.Min(w.minGapDuring, g); }
                    if (!w.car.Model.IsCrushing)
                    {
                        w.endStep = step;
                        w.logicalDiff = DisplayedVsLogical(w.car.Model);
                        w.shellMovedDuringCrush = !w.car.rig.hitbox.Faces.Select(f => f.current).SequenceEqual(w.facesAtHit);
                    }
                }
                game.AfterPhysicsStep(Dt);   // the collider-refit safe point (FixedUpdate at runtime; ordering within a frame does not matter for it)
                for (int k = 0; k < watch.Length; k++)
                {
                    var w = watch[k];
                    if (w.car.refits == refits[k]) continue;
                    refits[k] = w.car.refits;
                    if (w.Hit && w.refitStep < 0) { w.refitStep = step; if (w.car.Model.IsCrushing) w.refitDuringCrush++; }
                }
            }
            foreach (var w in watch) Analyse(w);
        }

        static void Analyse(Trace w)
        {
            if (!w.Hit || w.frames.Count == 0) return;
            var m = w.car.Model;
            w.final = w.frames[w.frames.Count - 1];
            w.ticks = w.frames.Count;
            w.firstFrame = Progress(m, w.before, w.final, w.frames[0]);
            float prev = 0f;
            var prevFrame = w.before;
            for (int f = 0; f < w.frames.Count; f++)
            {
                float p = Progress(m, w.before, w.final, w.frames[f]);
                w.maxJump = Mathf.Max(w.maxJump, p - prev);
                if (f == w.frames.Count - 1) w.lastJump = p - prev;
                w.backwards += Backwards(w.before, w.final, prevFrame, w.frames[f]);
                prev = p; prevFrame = w.frames[f];
            }
            // Front-first: at the first frame, metal in the front third of the crush depth vs the back third.
            float depth = Mathf.Max(0.15f, w.travel);
            w.front = Progress(m, w.before, w.final, w.frames[0], w.impact, float.NegativeInfinity, depth / 3f);
            w.back = Progress(m, w.before, w.final, w.frames[0], w.impact, 2f * depth / 3f, float.PositiveInfinity);
        }

        static string Describe(Trace w)
        {
            if (!w.Hit) return "no damaging hit";
            string samples = string.Join(" ", Enumerable.Range(0, w.frames.Count).Select(f => $"{Progress(w.car.Model, w.before, w.final, w.frames[f]) * 100f:0}%"));
            return $"travel {w.travel:0.000} m, crush {w.duration * 1000f:0} ms over {w.ticks} frames at 50 fps, progress per frame [{samples}], " +
                   $"front/back third at frame 1 {w.front * 100f:0}%/{w.back * 100f:0}%, last-frame step {w.lastJump * 100f:0}% (largest {w.maxJump * 100f:0}%), " +
                   $"backward vertex moves {w.backwards}, final vs solver {w.logicalDiff:0.0e0} m{(w.replayDiff >= 0f ? $", vs instant replay {w.replayDiff:0.0e0} m (normals {w.replayNormals:0.0e0})" : "")}, " +
                   $"refit {(w.refitStep >= 0 ? $"{(w.refitStep - w.hitStep) * Dt:0.00} s after the hit" : "none")}";
        }

        /// <summary>Compare the traced car's final displayed mesh with an instant replay of its impact records.</summary>
        static void Replay(Trace w)
        {
            var m = w.car.Model;
            if (!w.Hit || m.MergedImpactCount > 0) return;
            TickUntilDone(m, Dt);   // a later hit in the scenario may still be crushing in
            var r = InstantReplay(w.car.def, m.Impacts);
            w.replayDiff = MaxDiff(Displayed(m), Displayed(r.model));
            NormalsEqual(m, r.model, out w.replayNormals);
            DestroyRigs();
        }

        /// <summary>The standard per-scenario checks: prompt start, progressive, monotonic, no end snap, exact final.</summary>
        static bool CommonChecks(string label, Trace w, string test)
        {
            bool started = w.Hit && !w.shownInstantly && w.firstFrame > 0.02f && w.firstFrame < 0.95f;
            bool progressive = w.ticks >= 2;
            bool exact = w.logicalDiff <= LogicalTolerance && (w.replayDiff < 0f || (w.replayDiff <= ExactTolerance && w.replayNormals <= ExactTolerance));
            bool smooth = w.backwards == 0 && w.lastJump <= Mathf.Max(0.35f, w.maxJump) + 1e-4f;
            int expectTicks = Mathf.CeilToInt(w.duration / Dt - 1e-4f);
            bool timing = Mathf.Abs(w.duration - Settings(true).DurationFor(w.travel)) < 1e-4f && w.ticks >= expectTicks && w.ticks <= expectTicks + 1;
            bool ok = started && progressive && exact && smooth && timing && w.restartedDuringCrush == 0;
            Check($"{label}: crushes in progressively and ends exactly on the solver's shape", ok,
                  Describe(w) + (w.restartedDuringCrush > 0 ? " (hit again during its crush: scenario not clean)" : w.crushStarts > 1 ? $" ({w.crushStarts - 1} later hit(s) after the crush)" : ""));
            Verdict(test, ok);
            return ok;
        }

        // ------------------------------------------------------------------ run

        static string Execute()
        {
            sb = new StringBuilder("[ProgressiveCrush] Validation (real PhysX, Physics.Simulate at 50 Hz, one rendered frame per step; editor timings)\n");
            fails = 0; verdicts.Clear();
            EditorSceneManager.OpenScene(DrivingSceneBuilder.ScenePath, OpenSceneMode.Single);
            game = UnityEngine.Object.FindFirstObjectByType<DrivingGame>();
            var prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            VehicleDamageModel.ProgressiveInEditMode = true;
            try
            {
                game.progressiveCrush = true;
                game.refitColliders = true;
                game.Setup();
                foreach (Transform o in game.obstacles) if (!o.name.StartsWith("Wall")) o.gameObject.SetActive(false);   // open ground
                game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
                game.SelectPlayer(1); game.SelectPlayer(0);                     // fresh spawns pick up the progressive settings
                game.SelectTarget(0); game.SelectTarget(2);
                Quiet();
                sb.AppendLine($"  settings: light {game.crushTimeLight * 1000f:0} ms, heavy {game.crushTimeHeavy * 1000f:0} ms at {game.crushHeavyTravel:0.00} m travel, stagger {game.crushStagger:0.00}; " +
                              $"player {game.Player.def.displayName}, target {game.Target.def.displayName}");

                FrontalSeverities();
                var records = Localisation();
                Repeated(records);
                FrameRate(records);
                Simultaneous(records);
                PlayerAndAi();
                Colliders();
                Reset(records);
                Performance(records);
                InstantModesUnchanged(records);
            }
            catch (Exception e)
            {
                fails++;
                sb.AppendLine($"  FAIL  exception: {e}");
            }
            finally
            {
                DestroyRigs();
                VehicleDamageModel.ProgressiveInEditMode = false;
                Physics.simulationMode = prevMode;
            }
            sb.AppendLine("  -- brief scenarios --");
            foreach (var t in new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L" })
                sb.AppendLine($"  {t}: {(verdicts.TryGetValue(t, out var v) ? v : "NOT TESTED")}");
            sb.AppendLine("  M (Collision Lab regression) and N (driving regression): run the existing validations (see Docs/Driving/progressive-crushing-v1.md).");
            sb.AppendLine(fails == 0 ? "[ProgressiveCrush] ALL CHECKS PASSED" : $"[ProgressiveCrush] {fails} CHECK(S) FAILED");
            return sb.ToString();
        }

        static void Quiet() { game.Player.Receiver.logImpacts = false; game.Target.Receiver.logImpacts = false; }

        /// <summary>Head-on into the parked target: both cars facing each other 9 m apart, each at half the closing speed.</summary>
        static void HeadOn(float closingKmh, float offsetX = 0f)
        {
            float v = closingKmh / 3.6f * 0.5f;
            Place(game.Target.Body, new Vector3(0f, 0f, 4.5f), 180f, Vector3.back * v);
            Place(game.Player.Body, new Vector3(offsetX, 0f, -4.5f), 0f, Vector3.forward * v);
            Arm();
        }

        // A, B, C
        static void FrontalSeverities()
        {
            sb.AppendLine("  -- frontal severities (muscle car into the pickup, head-on, open ground) --");
            var cases = new (string test, string label, float kmh)[] { ("A", "A. Light frontal (30 km/h closing)", 30f), ("B", "B. Moderate frontal (80 km/h closing)", 80f), ("C", "C. Severe frontal (170 km/h closing)", 170f) };
            var durations = new List<float>();
            foreach (var c in cases)
            {
                game.ResetAll(); Quiet();
                HeadOn(c.kmh);
                var t = new Trace { label = c.label, car = game.Target };
                Sim(1.2f, false, true, t);
                Replay(t);
                CommonChecks(c.label + " (pickup)", t, c.test);
                bool front = t.front >= t.back - 1e-4f;
                Check($"{c.label}: the struck surface leads, the metal behind it follows", front, $"progress at frame 1: front third {t.front * 100f:0}%, back third {t.back * 100f:0}%");
                Verdict(c.test, front);
                durations.Add(t.duration);
            }
            bool rising = durations.Count == 3 && durations[0] < durations[1] && durations[1] < durations[2];
            Check("A-C. Heavier hits crush for longer", rising, $"crush time light/moderate/severe {string.Join(" / ", durations.Select(d => (d * 1000f).ToString("0")))} ms");
            Verdict("A", rising); Verdict("B", rising); Verdict("C", rising);
        }

        /// <summary>D, E, F. Returns impact records (pickup) for the deterministic tests: front, side, rear.</summary>
        static (LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) Localisation()
        {
            sb.AppendLine("  -- localisation (pickup struck by the muscle car) --");
            var T = game.Target;
            float Disp(Func<Vector3, bool> where)
            {
                float d = 0f;
                foreach (var p in T.Model.Parts)
                    for (int i = 0; i < p.VehiclePositions.Length; i++)
                        if (where(p.VehiclePositions[i])) d = Mathf.Max(d, (p.DeformedVehiclePosition(i) - p.VehiclePositions[i]).magnitude);
                return d;
            }
            Bounds b = T.Model.BodyBounds;
            float frontZ = b.center.z + 0.3f * b.extents.z, rearZ = b.center.z - 0.3f * b.extents.z;

            // Severe head-on record for later (front).
            game.ResetAll(); Quiet(); HeadOn(140f);
            var tf = new Trace { label = "front record", car = T };
            Sim(1.2f, false, false, tf);
            var front = tf.Hit ? tf.impact : default;

            // D. Offset: player shifted 0.9 m to the player's right = the pickup's right side (it faces the player).
            game.ResetAll(); Quiet(); HeadOn(100f, 0.9f);
            var td = new Trace { label = "D. Offset frontal", car = T };
            Sim(1.2f, false, false, td);
            Replay(td);
            CommonChecks("D. Offset frontal (100 km/h, 0.9 m)", td, "D");
            float struck = Disp(v => v.z > frontZ && v.x < b.center.x - 0.25f * b.extents.x), other = Disp(v => v.z > frontZ && v.x > b.center.x + 0.25f * b.extents.x);
            if (struck < other) (struck, other) = (other, struck);   // whichever corner was hit, it must clearly dominate
            float rearD = Disp(v => v.z < rearZ);
            bool local = td.Hit && struck > 0.05f && other < 0.6f * struck && rearD < 0.02f;
            Check("D. Offset: the struck corner takes the damage", local, $"struck corner {struck:0.000} m, opposite corner {other:0.000} m, rear {rearD:0.000} m");
            Verdict("D", local);
            var offset = td.Hit ? td.impact : default;

            // E. Side: the parked pickup broadside, the muscle car into its left side at 60 km/h.
            game.ResetAll(); Quiet();
            Place(T.Body, new Vector3(0f, 0f, 4.5f), 90f, Vector3.zero);
            Place(game.Player.Body, new Vector3(-1.5f, 0f, -3.5f), 0f, Vector3.forward * (60f / 3.6f));
            Arm();
            var te = new Trace { label = "E. Side", car = T };
            Sim(1.2f, false, false, te);
            Replay(te);
            CommonChecks("E. Side impact (60 km/h T-bone)", te, "E");
            float l = Disp(v => v.x < b.center.x - 0.5f * b.extents.x), r = Disp(v => v.x > b.center.x + 0.5f * b.extents.x);
            bool sideLocal = te.Hit && Mathf.Max(l, r) > 0.05f && Mathf.Min(l, r) < 0.5f * Mathf.Max(l, r);
            Check("E. Side: the struck side compresses inward, the other side does not", sideLocal, $"left side {l:0.000} m, right side {r:0.000} m, hit '{T.Receiver.Last.region}'");
            Verdict("E", sideLocal);
            var side = te.Hit ? te.impact : default;

            // F. Rear: the parked pickup facing away, the muscle car into its tail at 60 km/h.
            game.ResetAll(); Quiet();
            Place(T.Body, new Vector3(0f, 0f, 4.5f), 0f, Vector3.zero);
            Place(game.Player.Body, new Vector3(0f, 0f, -3.5f), 0f, Vector3.forward * (60f / 3.6f));
            Arm();
            var tr = new Trace { label = "F. Rear", car = T };
            Sim(1.2f, false, false, tr);
            Replay(tr);
            CommonChecks("F. Rear impact (60 km/h)", tr, "F");
            float rearDisp = Disp(v => v.z < rearZ), frontDisp = Disp(v => v.z > frontZ);
            bool rearLocal = tr.Hit && rearDisp > 0.05f && frontDisp < 0.02f;
            Check("F. Rear: the tail compresses, the nose does not", rearLocal, $"rear {rearDisp:0.000} m, front {frontDisp:0.000} m");
            Verdict("F", rearLocal);
            var rear = tr.Hit ? tr.impact : default;

            game.ResetAll();
            return (front, side, rear, offset);
        }

        /// <summary>G. New hits while a crush is still running (deterministic replays of recorded impacts on standalone pickups).</summary>
        static void Repeated((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- repeated impacts during a crush (recorded pickup impacts, replayed) --");
            var def = game.Target.def;
            if (rec.front.strength <= 0f || rec.side.strength <= 0f || rec.rear.strength <= 0f) { Check("G. impact records available", false, "a recording scenario produced no hit"); Verdict("G", false); return; }

            void Sequence(string label, (LocalImpact im, float at)[] hits)
            {
                var prog = Rig(def, true);
                var m = prog.model;
                float worstJump = 0f, worstAllowed = 0f; int backwards = 0, restarts = 0;
                Vector3[][] shown = Displayed(m);
                for (int h = 0; h < hits.Length; h++)
                {
                    if (h > 0) TickFraction(m, hits[h].at, Dt);
                    var beforeHit = Displayed(m);
                    bool wasCrushing = m.IsCrushing;
                    m.AddImpact(hits[h].im);
                    if (wasCrushing) restarts++;
                    // Nothing jumps at the hit itself (the mesh is only uploaded by the next frame) ...
                    worstJump = Mathf.Max(worstJump, MaxDiff(beforeHit, Displayed(m)));
                    // ... and the first frame of the new crush moves no vertex further than its own eased first step.
                    m.AdvanceCrush(Dt);
                    float allowed = m.IsCrushing ? m.CrushTravel * ProgressiveCrushSettings.Ease(Dt / m.CrushDuration, 0f) + 1e-4f : float.PositiveInfinity;
                    float step = MaxDiff(beforeHit, Displayed(m));
                    if (step > allowed) backwards++;
                    worstAllowed = Mathf.Max(worstAllowed, step);
                }
                TickUntilDone(m, Dt);
                var instant = InstantReplay(def, hits.Select(x => x.im));
                float diff = MaxDiff(Displayed(m), Displayed(instant.model));
                NormalsEqual(m, instant.model, out float nd);
                float logical = DisplayedVsLogical(m);
                bool ok = diff <= ExactTolerance && nd <= ExactTolerance && worstJump == 0f && backwards == 0 && restarts == hits.Length - 1 && logical <= LogicalTolerance
                          && m.TotalImpactCount == hits.Length;
                Check($"G. {label}", ok, $"{hits.Length} hits, {restarts} during a running crush; jump at a hit {worstJump:0.0e0} m; first-frame step {worstAllowed:0.000} m (over the eased limit: {backwards}); " +
                                         $"final vs instant replay {diff:0.0e0} m, normals {nd:0.0e0}; vs solver {logical:0.0e0} m; impacts kept {m.TotalImpactCount}");
                Verdict("G", ok);
                DestroyRigs();
            }

            Sequence("repeated hit on the same region at 40% of its crush", new[] { (rec.front, 0f), (rec.front, 0.4f) });
            Sequence("hit on another region (rear) at 30% of a frontal crush", new[] { (rec.front, 0f), (rec.rear, 0.3f) });
            Sequence("three regions in quick succession (side, front at 20%, rear at 20%)", new[] { (rec.side, 0f), (rec.front, 0.2f), (rec.rear, 0.2f) });
            Sequence("five rapid hits, each at 10% of the previous crush", new[] { (rec.front, 0f), (rec.offset, 0.1f), (rec.front, 0.1f), (rec.side, 0.1f), (rec.rear, 0.1f) });
        }

        /// <summary>Elapsed time, not frame count: the same crush at 20, 50, 60 and 144 fps.</summary>
        static void FrameRate((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- frame-rate independence --");
            if (rec.front.strength <= 0f) return;
            var def = game.Target.def;
            var instant = InstantReplay(def, new[] { rec.front });
            var reference = Displayed(instant.model);
            var rows = new List<string>(); bool ok = true;
            foreach (float fps in new[] { 20f, 50f, 60f, 144f })
            {
                var r = Rig(def, true);
                var before = Displayed(r.model);
                r.model.AddImpact(rec.front);
                float dur = r.model.CrushDuration;
                // progress at ~half the crush time, measured at the frame nearest to it
                int half = Mathf.Max(1, Mathf.RoundToInt(0.5f * dur * fps));
                for (int k = 0; k < half; k++) r.model.AdvanceCrush(1f / fps);
                float mid = Progress(r.model, before, reference, Displayed(r.model));
                float midTime = half / fps;
                int ticks = half + TickUntilDone(r.model, 1f / fps);
                float done = ticks / fps;
                float diff = MaxDiff(Displayed(r.model), reference);
                bool rowOk = diff <= ExactTolerance && done >= dur - 1e-4f && done < dur + 1f / fps + 1e-4f;
                ok &= rowOk;
                rows.Add($"{fps:0} fps: done after {done * 1000f:0} ms ({ticks} frames), {mid * 100f:0}% at {midTime * 1000f:0} ms, final diff {diff:0.0e0}");
            }
            Check("Frame rate: same crush time and same final shape at 20/50/60/144 fps", ok, string.Join("; ", rows) + $" (crush {Settings(true).DurationFor(0f) * 1000f:0}-{game.crushTimeHeavy * 1000f:0} ms)");
            Verdict("G", ok);
            DestroyRigs();
        }

        /// <summary>H. Several cars and regions crushing at once.</summary>
        static void Simultaneous((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- simultaneous crushes --");
            // Both arena cars from one head-on.
            game.ResetAll(); Quiet(); HeadOn(120f);
            var tp = new Trace { label = "player", car = game.Player }; var tt = new Trace { label = "target", car = game.Target };
            Sim(1.2f, false, false, tp, tt);
            Replay(tp); Replay(tt);
            int overlap = tp.Hit && tt.Hit ? Mathf.Max(0, Mathf.Min(tp.endStep, tt.endStep) - Mathf.Max(tp.hitStep, tt.hitStep)) : 0;
            bool both = CommonChecks("H. Head-on, muscle car", tp, "H") & CommonChecks("H. Head-on, pickup", tt, "H");
            Check("H. Both cars crush in the same frames without corrupting each other", both && overlap >= 1, $"frames crushing together {overlap}");
            Verdict("H", both && overlap >= 1);

            // Four standalone pickups, each hit in a different region, crushing in the same frames.
            if (rec.front.strength <= 0f) return;
            var hits = new[] { rec.front, rec.side, rec.rear, rec.offset };
            var cars = hits.Select(_ => Rig(game.Target.def, true)).ToArray();
            for (int k = 0; k < cars.Length; k++) cars[k].model.AddImpact(hits[k]);
            int frames = 0;
            while (cars.Any(c => c.model.IsCrushing) && frames < 100) { foreach (var c in cars) c.model.AdvanceCrush(1f / 60f); frames++; }
            float worst = 0f;
            for (int k = 0; k < cars.Length; k++)
            {
                var inst = InstantReplay(game.Target.def, new[] { hits[k] });
                worst = Mathf.Max(worst, MaxDiff(Displayed(cars[k].model), Displayed(inst.model)));
            }
            bool ok = worst <= ExactTolerance;
            Check("H. Four cars crushing at once (front / side / rear / offset)", ok, $"{frames} frames at 60 fps; worst final difference to instant replays {worst:0.0e0} m");
            Verdict("H", ok);
            DestroyRigs();
        }

        /// <summary>I. The enemy (AI) and the player use the same crush.</summary>
        static void PlayerAndAi()
        {
            sb.AppendLine("  -- player and AI (ENEMY mode: the pickup enemy attacks the stationary muscle car) --");
            game.SetTargetMode((int)DrivingGame.TargetMode.Enemy);
            game.SelectTarget(2);
            Quiet();
            game.ResetAll(); Quiet();
            var tp = new Trace { label = "player", car = game.Player }; var te = new Trace { label = "enemy", car = game.Target };
            Sim(16f, true, false, tp, te);
            Replay(tp); Replay(te);
            bool okP = tp.Hit && tp.ticks >= 2 && !tp.shownInstantly && tp.logicalDiff <= LogicalTolerance && tp.backwards == 0 && (tp.replayDiff < 0f || tp.replayDiff <= ExactTolerance);
            bool okE = te.Hit && te.ticks >= 2 && !te.shownInstantly && te.logicalDiff <= LogicalTolerance && te.backwards == 0 && (te.replayDiff < 0f || te.replayDiff <= ExactTolerance);
            Check("I. Player car crushes progressively when the enemy hits it", okP, Describe(tp));
            Check("I. Enemy car crushes progressively with the same engine", okE, Describe(te));
            Verdict("I", okP && okE);
            game.SetTargetMode((int)DrivingGame.TargetMode.Parked);
            game.SelectTarget(2);
            Quiet();
        }

        /// <summary>J. The shell is refitted only once the metal has finished crushing in, and then matches it as before.</summary>
        static void Colliders()
        {
            sb.AppendLine("  -- damage-aware colliders --");
            foreach (float kmh in new[] { 80f, 170f })
            {
                game.ResetAll(); Quiet(); HeadOn(kmh);
                var t = new Trace { label = "pickup", car = game.Target };
                float settle = game.colliderSettleDelay;
                Sim(1.4f, false, true, t);
                float gapAfter = ShownGap(game.Target);
                int expect = Mathf.CeilToInt(Mathf.Max(settle, t.duration) / Dt - 1e-4f);
                bool timing = t.Hit && t.refitStep >= t.endStep && t.refitDuringCrush == 0 && t.refitStep - t.hitStep <= expect + 1;
                // While the dent crushes in, the shell does not move (it can only retract, and must not get ahead of the metal); after the refit it sits on the metal.
                bool aligned = t.Hit && !t.shellMovedDuringCrush && gapAfter < 0.1f;
                Check($"J. {kmh:0} km/h: refit waits for the crush, never inside it", timing,
                      $"hit at step {t.hitStep}, crush done at step {t.endStep} ({t.duration * 1000f:0} ms), refit at step {t.refitStep} (settle {settle:0.00} s -> expected within {expect + 1} steps)");
                Check($"J. {kmh:0} km/h: shell holds still while the metal crushes in, then fits it", aligned,
                      $"shell moved during the crush {t.shellMovedDuringCrush}; box-to-visible-metal gap during the crush {t.minGapDuring:0.000}..{t.maxGapDuring:0.000} m, after the refit {gapAfter:0.000} m (centre lane, mid height)");
                Verdict("J", timing && aligned);
            }
        }

        /// <summary>K. Reset during a running crush restores mesh, shell and transition state.</summary>
        static void Reset((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- reset --");
            // Arena: reset while both cars are mid-crush.
            game.ResetAll(); Quiet(); HeadOn(170f);
            bool crushingSeen = false;
            int steps = 0;
            while (steps < 100)
            {
                Physics.Simulate(Dt);
                game.Player.Receiver.Step(Dt); game.Target.Receiver.Step(Dt);
                game.Player.Model.AdvanceCrush(Dt); game.Target.Model.AdvanceCrush(Dt);
                game.AfterPhysicsStep(Dt);
                steps++;
                if (game.Target.Model.IsCrushing && game.Target.Model.CrushProgress > 0.2f) { crushingSeen = true; break; }
            }
            game.ResetAll();
            bool meshes = DisplayedVsSource(game.Player.Model) < 1e-6f && DisplayedVsSource(game.Target.Model) < 1e-6f;
            bool state = !game.Player.Model.IsCrushing && !game.Target.Model.IsCrushing && game.Player.Model.ImpactCount == 0 && game.Target.Model.ImpactCount == 0;
            bool shells = game.Player.rig.hitbox.Faces.All(f => f.current == f.original) && game.Target.rig.hitbox.Faces.All(f => f.current == f.original)
                          && !game.Player.rig.hitbox.HasPending && !game.Target.rig.hitbox.HasPending;
            for (int i = 0; i < 20; i++) { game.Player.Model.AdvanceCrush(Dt); game.Target.Model.AdvanceCrush(Dt); }
            bool stays = DisplayedVsSource(game.Target.Model) < 1e-6f;
            bool ok = crushingSeen && meshes && state && shells && stays;
            Check("K. Reset during a running crush restores meshes, shells and stops the crush", ok,
                  $"crush seen {crushingSeen}; meshes restored {meshes}; no crush or impacts left {state}; shells undamaged {shells}; still restored 20 frames later {stays}");
            Verdict("K", ok);

            // The next hit after a reset crushes from the undamaged car and matches the instant result.
            HeadOn(80f);
            var t = new Trace { label = "after reset", car = game.Target };
            Sim(1.2f, false, false, t);
            Replay(t);
            bool next = t.Hit && t.logicalDiff <= LogicalTolerance && (t.replayDiff < 0f || t.replayDiff <= ExactTolerance) && t.backwards == 0 && MaxDiff(t.before, Enumerable.Range(0, game.Target.Model.Parts.Count).Select(p => game.Target.Model.Parts[p].sourceMesh.vertices).ToArray()) < 1e-6f;
            Check("K. The first hit after a reset crushes from the undamaged shape", next, Describe(t));
            Verdict("K", next);

            // Standalone: reset at 30% of a crush, then a new hit.
            if (rec.front.strength <= 0f) return;
            var r = Rig(game.Target.def, true);
            r.model.AddImpact(rec.front);
            TickFraction(r.model, 0.3f, Dt);
            r.model.ResetAll(); r.model.SetDamageImmediate(0f, 0f, 0f, 0f);
            bool clean = DisplayedVsSource(r.model) < 1e-6f && !r.model.IsCrushing;
            r.model.AddImpact(rec.rear);
            TickUntilDone(r.model, Dt);
            var inst = InstantReplay(game.Target.def, new[] { rec.rear });
            float diff = MaxDiff(Displayed(r.model), Displayed(inst.model));
            Check("K. Model reset mid-crush, then a new hit", clean && diff <= ExactTolerance, $"restored {clean}; next hit vs instant replay {diff:0.0e0} m");
            Verdict("K", clean && diff <= ExactTolerance);
            DestroyRigs();
        }

        /// <summary>L. Cost per crush frame, allocations, memory, and the impact-frame spike with and without the ramp.</summary>
        static void Performance((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- performance (editor, Mono; the crush frame is single-threaded in the editor and in WebGL) --");
            // Every vehicle: head-on as the player into the pickup; both cars' crush frames.
            var rows = new List<string>();
            bool allocOk = true;
            for (int v = 0; v < game.vehicles.Length; v++)
            {
                game.SelectPlayer(v); Quiet();
                game.ResetAll(); Quiet(); HeadOn(120f);
                var tp = new Trace { car = game.Player }; var tt = new Trace { car = game.Target };
                Sim(1.0f, false, false, tp, tt);
                foreach (var t in new[] { tp, tt })
                {
                    if (!t.Hit) continue;
                    int verts = t.car.Model.DeformableVertices;
                    rows.Add($"{t.car.def.displayName} ({verts} verts): {t.ms.Count} frames, mean {t.ms.Average():0.00} ms, max {t.ms.Max():0.00} ms (last frame uploads the final mesh)");
                }
            }
            game.SelectPlayer(0); Quiet();
            sb.AppendLine("    " + string.Join("\n    ", rows));

            if (rec.front.strength <= 0f) return;
            // Allocations per crush frame and extra memory (standalone pickups).
            var r = Rig(game.Target.def, true);
            r.model.AddImpact(rec.front);   // first ramp allocates the part's ramp buffers (once per part)
            r.model.CompleteCrush();
            long bufBytes = r.model.Parts.Sum(p => (long)p.VertexCount * (4 * 12 + 4));
            var ms = new List<float>(); var bytes = new List<long>();
            r.model.AddImpact(rec.side);
            int frames = TickUntilDone(r.model, 1f / 60f, ms, bytes);
            long allocs = bytes.Sum();
            allocOk = allocs <= 0;
            Check("L. No managed allocations per crush frame (buffers reused after the first crush)", allocOk,
                  $"{frames} frames, Mono heap growth {allocs} bytes in total, mean {ms.Average():0.00} ms, max {ms.Max():0.00} ms; ramp buffers {bufBytes / 1024f / 1024f:0.0} MB for this car if every part crushes (52 bytes per vertex, allocated on a part's first crush)");
            Verdict("L", allocOk);

            // Impact-frame cost with and without the ramp (the ramp defers the mesh upload to the crush frames).
            var a = Rig(game.Target.def, false); var b = Rig(game.Target.def, true);
            var sw = new System.Diagnostics.Stopwatch();
            sw.Restart(); a.model.AddImpact(rec.front); float instantMs = (float)sw.Elapsed.TotalMilliseconds;
            sw.Restart(); b.model.AddImpact(rec.front); float progMs = (float)sw.Elapsed.TotalMilliseconds;
            sb.AppendLine($"    impact frame (AddImpact): instant {instantMs:0.0} ms (rebuild {a.model.LastDeformMilliseconds:0.0}), progressive {progMs:0.0} ms (rebuild {b.model.LastDeformMilliseconds:0.0}); single measurement, includes JIT/cache effects");

            // Four cars crushing in the same frames.
            DestroyRigs();
            var cars = new[] { rec.front, rec.side, rec.rear, rec.offset }.Select(im => { var c = Rig(game.Target.def, true); c.model.AddImpact(rec.front); c.model.CompleteCrush(); c.model.AddImpact(im); return c; }).ToArray();
            var frameMs = new List<float>();
            while (cars.Any(c => c.model.IsCrushing))
            {
                sw.Restart();
                foreach (var c in cars) c.model.AdvanceCrush(1f / 60f);
                frameMs.Add((float)sw.Elapsed.TotalMilliseconds);
            }
            sb.AppendLine($"    four pickups crushing at once: {frameMs.Count} frames at 60 fps, mean {frameMs.Average():0.00} ms, max {frameMs.Max():0.00} ms per frame for all four");
            Verdict("L", true);
            DestroyRigs();
        }

        /// <summary>Progressive off (and edit-mode tools that do not opt in) keep the instant behaviour exactly.</summary>
        static void InstantModesUnchanged((LocalImpact front, LocalImpact side, LocalImpact rear, LocalImpact offset) rec)
        {
            sb.AppendLine("  -- instant paths unchanged --");
            if (rec.front.strength <= 0f) return;
            var off = Rig(game.Target.def, false);
            off.model.AddImpact(rec.front);
            bool offOk = !off.model.IsCrushing && DisplayedVsLogical(off.model) <= LogicalTolerance;
            VehicleDamageModel.ProgressiveInEditMode = false;
            var editTool = Rig(game.Target.def, true);
            editTool.model.AddImpact(rec.front);
            bool editOk = !editTool.model.IsCrushing && MaxDiff(Displayed(editTool.model), Displayed(off.model)) <= ExactTolerance;
            VehicleDamageModel.ProgressiveInEditMode = true;
            Check("Progressive off: the dent appears at once, as before", offOk, $"crushing {off.model.IsCrushing}, displayed vs solver {DisplayedVsLogical(off.model):0.0e0} m");
            Check("Edit-mode tools that do not opt in (existing validations, probes) still see instant dents", editOk, $"crushing {editTool.model.IsCrushing}");
            DestroyRigs();
        }
    }
}
