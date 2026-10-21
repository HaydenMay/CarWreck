using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Attachment V1 / milestone M3 checks for the collision-engine interface (architecture-v1.md section 7.3): attachment proxy colliders on a car's own Rigidbody.
    /// Real PhysX in the DerbyArena, stepped at 50 Hz through DerbyGame.ManualStep. Test proxies are created here by hand (no shipped item uses one yet).
    /// What must hold: attribution still goes to the car; a proxy never appears as the struck shell collider; it never widens the shell bounds or the face fill; it shapes the
    /// dent footprint by itself; the energy of an identical scripted hit is unchanged (the effect is geometry only); one event per body pair; the slot ids reach the reports.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentProxyValidation.Run -quit -logFile proxy.log
    /// </summary>
    public static class AttachmentProxyValidation
    {
        const float Dt = 0.02f;
        static bool markProxies = true;
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Attachments (M3 proxy colliders)", priority = 48)]
        public static void Run()
        {
            Debug.Log(Execute());
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static void Check(string name, bool ok, string detail = "")
        {
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? ": " + detail : "")}");
            if (ok) passes++; else fails++;
        }

        static string Execute()
        {
            sb = new StringBuilder("[Attachments] M3 proxy collider validation (DerbyArena, real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            var prev = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); game.ui = null;
                Registry();
                for (int v = 0; v < game.vehicles.Length; v++) Scenarios(v);
                TBone();
                markProxies = true;
                ShellBoundsUnchanged();
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static void StepSeconds(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt); }

        static void FreshMatch(int vehicle)
        {
            game.GoToMenu();
            game.SelectVehicle(vehicle);
            game.participantCount = 2;
            game.rules.mode = GameMode.PointsDerby;
            game.rules.countdownSeconds = 0.2f;
            game.rules.pointsDuration = 180f; game.rules.durabilityEnergy = 650000f;
            game.StartMatch();
            foreach (var p in game.Participants) { p.car.Receiver.logImpacts = false; if (p.car.enemy != null) { p.car.enemy.enabled = false; p.car.controller.Throttle = 0f; p.car.controller.SteerInput = 0f; } }
            StepSeconds(0.3f);
        }

        static void PlaceCar(Participant p, Vector3 pos, float yaw, float speed)
        {
            var rb = p.car.Body; var rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = new Vector3(pos.x, p.car.GroundLift, pos.z); rb.rotation = rot;
            rb.transform.SetPositionAndRotation(rb.position, rot);
            rb.linearVelocity = rot * Vector3.forward * speed; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            p.car.controller.ResetState(); p.car.Receiver.ClearCooldowns();
        }

        /// <summary>A hand-made proxy box on the car's own Rigidbody (child of the root). Centre and size are in the car's local space.</summary>
        static GameObject AddProxy(DerbyCar car, int slot, string item, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(car.rig.root.transform, false);
            go.transform.localPosition = center;
            var bc = go.AddComponent<BoxCollider>(); bc.size = size;
            if (markProxies) { var px = go.AddComponent<AttachmentProxy>(); px.slot = slot; px.itemId = item; }   // unmarked = the same collider as plain shell, for the like-for-like energy comparison
            Physics.SyncTransforms();
            return go;
        }

        static GameObject FrontProxy(DerbyCar c) { var b = c.rig.geometry.body; return AddProxy(c, 0, "test_front", "Proxy_Front", new Vector3(0f, b.min.y + b.size.y * 0.35f, b.max.z + 0.15f), new Vector3(0.5f, 0.25f, 0.3f)); }
        static GameObject RearProxy(DerbyCar c) { var b = c.rig.geometry.body; return AddProxy(c, 1, "test_rear", "Proxy_Rear", new Vector3(0f, b.min.y + b.size.y * 0.35f, b.min.z - 0.15f), new Vector3(0.9f, 0.25f, 0.3f)); }
        static GameObject LeftProxy(DerbyCar c) { var b = c.rig.geometry.body; return AddProxy(c, 2, "test_left", "Proxy_Left", new Vector3(b.min.x - 0.1f, b.min.y + b.size.y * 0.5f, 0f), new Vector3(0.2f, 0.4f, 1.0f)); }

        class Run1
        {
            public readonly List<CollisionDamageReceiver.Report> attacker = new List<CollisionDamageReceiver.Report>(), victim = new List<CollisionDamageReceiver.Report>();
            public float attackerScore, victimHits;
            public string attackerRoot, victimRoot;
            public double Energy(List<CollisionDamageReceiver.Report> l) => l.Where(r => r.applied).Sum(r => (double)r.energy);
            public CollisionDamageReceiver.Report? FirstApplied(List<CollisionDamageReceiver.Report> l) { foreach (var r in l) if (r.applied) return r; return null; }
        }

        /// <summary>Rear-end (or side) ram with optional proxies. Attacker is the human car.</summary>
        static Run1 Ram(int vehicle, bool tbone, Func<DerbyCar, GameObject> attackerProxy, Func<DerbyCar, GameObject> victimProxy)
        {
            FreshMatch(vehicle);
            var a = game.Human; var v = game.Participants[1];
            var made = new List<GameObject>();
            if (attackerProxy != null) made.Add(attackerProxy(a.car));
            if (victimProxy != null) made.Add(victimProxy(v.car));
            PlaceCar(v, new Vector3(0f, 0f, -40f), tbone ? 0f : 90f, 0f);
            PlaceCar(a, new Vector3(-14f, 0f, -40f), 90f, 16f);
            var run = new Run1 { attackerRoot = a.car.rig.root.name, victimRoot = v.car.rig.root.name };
            Action<CollisionDamageReceiver.Report> ra = r => run.attacker.Add(r), rv = r => run.victim.Add(r);
            a.car.Receiver.ImpactRegistered += ra; v.car.Receiver.ImpactRegistered += rv;
            float s0 = a.score; int h0 = v.hitsTaken;
            for (int i = 0; i < Mathf.RoundToInt(2.2f / Dt); i++)
            {
                a.car.controller.Throttle = a.car.controller.ForwardSpeed < 16f ? 1f : 0.3f; a.car.controller.SteerInput = 0f;
                v.car.controller.Throttle = 0f;
                game.ManualStep(Dt);
            }
            a.car.controller.Throttle = 0f;
            a.car.Receiver.ImpactRegistered -= ra; v.car.Receiver.ImpactRegistered -= rv;
            run.attackerScore = a.score - s0; run.victimHits = v.hitsTaken - h0;
            foreach (var g in made) UnityEngine.Object.DestroyImmediate(g);
            return run;
        }

        static string Key(int v) => game.vehicles[v].name;
        static bool IsShellName(string n) => n != null && n.EndsWith("Box") && !n.StartsWith("Proxy");

        // ------------------------------------------------------------------ registry

        static void Registry()
        {
            sb.AppendLine(" -- the proxy marker");
            var go = new GameObject("RegistryProbe");
            var bc = go.AddComponent<BoxCollider>();
            Check("a collider is not a proxy until the marker is added", !AttachmentProxy.IsProxy(bc));
            var px = go.AddComponent<AttachmentProxy>(); px.slot = 3; px.itemId = "probe";
            Check("adding the marker registers its collider with slot and item id", AttachmentProxy.IsProxy(bc) && AttachmentProxy.TryGet(bc, out var got) && got.slot == 3 && got.itemId == "probe");
            px.enabled = false;
            Check("disabling the marker unregisters it", !AttachmentProxy.IsProxy(bc));
            px.enabled = true;
            UnityEngine.Object.DestroyImmediate(go);
            Check("destroying it leaves no entry behind (null collider is not a proxy)", !AttachmentProxy.IsProxy(null) && !AttachmentProxy.TryGet(null, out _));
        }

        // ------------------------------------------------------------------ scenarios per vehicle

        static string Detail(CollisionDamageReceiver.Report r) =>
            $"E={r.energy:0} vn={r.closingSpeed:0.00} rel={r.relativeSpeed:0.00} raw={r.rawClosing:0.00} mu={r.reducedMass:0} imp={r.impulse:0} pen={r.penetration:0.000} contacts={r.contacts} strength={r.strength:0.000} boxes={r.impactorBoxes} [{r.impactorColliders}] car={r.carCollider}";

        static void Diagnose(int v)
        {
            foreach (var (label, af, vf) in new (string, Func<DerbyCar, GameObject>, Func<DerbyCar, GameObject>)[] { ("none", null, null), ("front proxy", FrontProxy, null), ("rear proxy on victim", null, RearProxy) })
            {
                var run = Ram(v, false, af, vf);
                sb.AppendLine($"   [{label}] victim:   {(run.FirstApplied(run.victim).HasValue ? Detail(run.FirstApplied(run.victim).Value) : "none")}");
                sb.AppendLine($"   [{label}] attacker: {(run.FirstApplied(run.attacker).HasValue ? Detail(run.FirstApplied(run.attacker).Value) : "none")}");
            }
        }

        static void Scenarios(int v)
        {
            if (Environment.GetEnvironmentVariable("PROXY_DIAG") == "1") { sb.AppendLine($" -- diagnostic {Key(v)}"); Diagnose(v); return; }
            sb.AppendLine($" -- {Key(v)} as attacker and victim, rear-end ram at 16 m/s");
            // Baseline: no proxies, three identical runs (their spread is the noise floor of the scenario).
            markProxies = true;
            var b1 = Ram(v, false, null, null); var b2 = Ram(v, false, null, null); var b3 = Ram(v, false, null, null);
            double[] baseV = { b1.Energy(b1.victim), b2.Energy(b2.victim), b3.Energy(b3.victim) };
            double meanV = baseV.Average(), spreadV = (baseV.Max() - baseV.Min()) / meanV;
            double[] baseA = { b1.Energy(b1.attacker), b2.Energy(b2.attacker), b3.Energy(b3.attacker) };
            double meanA = baseA.Average(), spreadA = (baseA.Max() - baseA.Min()) / meanA;
            double noise = Math.Max(spreadV, spreadA);
            double tol = Math.Max(0.01, 3.0 * noise);
            var baseHit = b1.FirstApplied(b1.victim);
            Check("baseline (no proxies): both cars register one applied hit and no proxy fields are set", b1.victim.Count(r => r.applied) == 1 && b1.attacker.Count(r => r.applied) == 1 && b1.victim.All(r => !r.ownProxy && !r.otherProxy) && b1.attacker.All(r => !r.ownProxy && !r.otherProxy), $"energy victim {meanV:0} J, attacker {meanA:0} J, noise {noise:0.0%}");

            // Attacker front proxy: the same collider run as a plain box (unmarked) and as a marked proxy.
            markProxies = false; var u1 = Ram(v, false, FrontProxy, null);
            markProxies = true; var r1 = Ram(v, false, FrontProxy, null);
            var vr = r1.FirstApplied(r1.victim); var ar = r1.FirstApplied(r1.attacker);
            Check("attacker's front proxy: the victim's report names the proxy's slot and item, credits the attacker car, and the footprint is the proxy box alone",
                vr.HasValue && vr.Value.other == r1.attackerRoot && vr.Value.otherProxy && vr.Value.otherSlot == 0 && vr.Value.otherItem == "test_front" && vr.Value.impactorBoxes == 1 && vr.Value.impactorColliders == "Proxy_Front",
                vr.HasValue ? $"other={vr.Value.other}, slot {vr.Value.otherSlot}, boxes {vr.Value.impactorBoxes} ({vr.Value.impactorColliders})" : "no applied report");
            Check("attacker's own report: the hit was on its proxy (slot 0), carCollider names a shell box (never the proxy), and the other body is the victim car",
                ar.HasValue && ar.Value.ownProxy && ar.Value.ownSlot == 0 && IsShellName(ar.Value.carCollider) && ar.Value.other == r1.victimRoot, ar.HasValue ? $"carCollider={ar.Value.carCollider}" : "no applied report");
            Check("the hit still scores for the attacker car and counts as one hit taken by the victim (attribution and scoring path unchanged)", r1.attackerScore > 0f && r1.victimHits == 1f, $"score +{r1.attackerScore}, victim hits {r1.victimHits}");
            Check("one event per body pair: one applied report per receiver, as without the proxy", r1.victim.Count(r => r.applied) == 1 && r1.attacker.Count(r => r.applied) == 1);
            double e1 = r1.Energy(r1.victim), eu1 = u1.Energy(u1.victim);
            Check($"no hidden multiplier: the same collider as a marked proxy gives the same hit energy as when it is plain shell (within {tol:0.0%})", Math.Abs(e1 - eu1) / eu1 <= tol, $"{e1:0} J marked vs {eu1:0} J unmarked ({(e1 - eu1) / eu1:+0.0%;-0.0%}); baseline without the collider {meanV:0} J (contact geometry, not the marker, explains any difference to it)");
            if (baseHit.HasValue && vr.HasValue)
                Check("the narrow proxy makes a smaller dent footprint than the whole front shell (geometry only; the engine already deepens small patches)", vr.Value.footprint.valid && baseHit.Value.footprint.valid && vr.Value.footprint.area < baseHit.Value.footprint.area,
                    $"area {vr.Value.footprint.area:0.000} m2 vs {baseHit.Value.footprint.area:0.000} m2; crush travel {vr.Value.footprint.travel:0.000} m vs {baseHit.Value.footprint.travel:0.000} m");

            // Victim rear proxy only.
            markProxies = false; var u2 = Ram(v, false, null, RearProxy);
            markProxies = true; var r2 = Ram(v, false, null, RearProxy);
            var vr2 = r2.FirstApplied(r2.victim); var ar2 = r2.FirstApplied(r2.attacker);
            Check("victim's rear proxy: its report says the hit was on slot 1 and carCollider names a shell box; the attacker's report sees the other body's proxy (slot 1)",
                vr2.HasValue && ar2.HasValue && vr2.Value.ownProxy && vr2.Value.ownSlot == 1 && IsShellName(vr2.Value.carCollider) && ar2.Value.otherProxy && ar2.Value.otherSlot == 1 && ar2.Value.otherItem == "test_rear",
                vr2.HasValue ? $"carCollider={vr2.Value.carCollider}" : "no applied report");
            Check($"no hidden multiplier on the struck car either (within {tol:0.0%})", Math.Abs(r2.Energy(r2.victim) - u2.Energy(u2.victim)) / u2.Energy(u2.victim) <= tol, $"{r2.Energy(r2.victim):0} J marked vs {u2.Energy(u2.victim):0} J unmarked");

            // Both cars.
            markProxies = false; var u3 = Ram(v, false, FrontProxy, RearProxy);
            markProxies = true; var r3 = Ram(v, false, FrontProxy, RearProxy);
            var vr3 = r3.FirstApplied(r3.victim); var ar3 = r3.FirstApplied(r3.attacker);
            Check("proxies on both cars: each car's report carries a proxy flag from its own side of the contact, attribution and one-event-per-pair hold, carCollider is always a shell box",
                vr3.HasValue && ar3.HasValue && (vr3.Value.ownProxy || vr3.Value.otherProxy) && (ar3.Value.ownProxy || ar3.Value.otherProxy) && vr3.Value.other == r3.attackerRoot && ar3.Value.other == r3.victimRoot
                && r3.victim.Count(r => r.applied) == 1 && r3.attacker.Count(r => r.applied) == 1 && IsShellName(vr3.Value.carCollider) && IsShellName(ar3.Value.carCollider),
                $"victim own {vr3?.ownProxy} other {vr3?.otherProxy}; attacker own {ar3?.ownProxy} other {ar3?.otherProxy}");
            Check($"no hidden multiplier with proxies on both cars (within {tol:0.0%})", Math.Abs(r3.Energy(r3.victim) - u3.Energy(u3.victim)) / u3.Energy(u3.victim) <= tol, $"{r3.Energy(r3.victim):0} J marked vs {u3.Energy(u3.victim):0} J unmarked");
        }

        static void TBone()
        {
            sb.AppendLine(" -- side hit onto a left-side proxy");
            markProxies = false; var u = Ram(1, true, null, LeftProxy);
            markProxies = true; var r = Ram(1, true, null, LeftProxy);
            var vr = r.FirstApplied(r.victim);
            Check("a hit on the victim's left proxy reports slot 2 and a shell box (nearest region), not the proxy",
                vr.HasValue && vr.Value.ownProxy && vr.Value.ownSlot == 2 && IsShellName(vr.Value.carCollider), vr.HasValue ? $"carCollider={vr.Value.carCollider}, own slot {vr.Value.ownSlot}" : "no applied report (the ram may have missed the proxy)");
            double eu = u.Energy(u.victim), er = r.Energy(r.victim);
            Check("no hidden multiplier on a side hit: marked vs unmarked within 5%", Math.Abs(er - eu) / Math.Max(1.0, eu) <= 0.05, $"{er:0} J marked vs {eu:0} J unmarked");
        }

        static void ShellBoundsUnchanged()
        {
            sb.AppendLine(" -- shell bounds");
            FreshMatch(1);
            var c = game.Human.car;
            var m = typeof(CollisionDamageReceiver).GetMethod("ShellBounds", BindingFlags.NonPublic | BindingFlags.Instance);
            var before = (Bounds)m.Invoke(c.Receiver, null);
            var p1 = FrontProxy(c); var p2 = RearProxy(c); var p3 = LeftProxy(c);
            var after = (Bounds)m.Invoke(c.Receiver, null);
            Check("three proxies sticking out of the car do not change the receiver's shell bounds (they would widen the other car's face-fill test)", (before.center - after.center).magnitude < 1e-5f && (before.size - after.size).magnitude < 1e-5f, $"size {before.size} -> {after.size}");
            int boxes = c.rig.root.GetComponentsInChildren<BoxCollider>().Count(bc => !AttachmentProxy.IsProxy(bc));
            Check("the shell is still exactly 14 boxes when proxies are present (proxies are counted separately)", boxes == 14, $"{boxes} shell boxes, {c.rig.root.GetComponentsInChildren<AttachmentProxy>().Length} proxies");
            UnityEngine.Object.DestroyImmediate(p1); UnityEngine.Object.DestroyImmediate(p2); UnityEngine.Object.DestroyImmediate(p3);
            Check("removing proxies restores the bounds exactly", ((Bounds)m.Invoke(c.Receiver, null)).size == before.size);
        }
    }
}
