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
    /// Attachment V1 / milestone M4 checks: the shipped items' proxy boxes, mounted through the real loadout path (AttachmentMount.Apply) on all three chassis.
    /// Real PhysX in the DerbyArena, stepped at 50 Hz through DerbyGame.ManualStep, same rear-end ram as the M3 harness (16 m/s).
    /// What must hold: the item's slot reaches the reports; attribution and scoring still go to the car; one event per body pair; the footprint comes from the proxy box
    /// (the narrow ram plate dents a smaller area than the wide push bar, for the same energy); mass, centre of mass and inertia are the bare car's; the shell is still 14 boxes
    /// and its bounds are unchanged; proxies clear the ground; removing an item removes its proxy at once; the Final Showdown still finds clear spots with the largest proxies.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentM4Validation.Run -quit -logFile m4.log
    /// </summary>
    public static class AttachmentM4Validation
    {
        const float Dt = 0.02f;
        const string PushBar = "push_bar_junk", RamPlate = "spiked_ram_plate", Hitch = "tow_hitch_bumper", Rack = "roof_cage_rack";
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Attachments (M4 proxy items)", priority = 49)]
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
            sb = new StringBuilder("[Attachments] M4 proxy item validation (DerbyArena, real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            var prev = Physics.simulationMode;
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup(); game.ui = null;
                Data();
                for (int v = 0; v < game.vehicles.Length; v++) { Mounting(v); Hits(v); }
                Showdown();
                StartWithLoadout();
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { Physics.simulationMode = prev; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static void StepSeconds(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt); }

        static string Key(int v) => game.vehicles[v].name;

        static Loadout Make(string key, params string[] ids)
        {
            var l = new Loadout(key);
            foreach (var id in ids) l.Set(game.attachmentCatalog.items.First(d => d.id == id).slot, id);
            return l;
        }

        static void FreshMatch(int vehicle, int cars = 2)
        {
            game.GoToMenu();
            game.SelectVehicle(vehicle);
            game.Human.loadout = new Loadout(Key(vehicle));
            if (game.Human.car != null) game.Human.car.mount.Apply(game.Human.loadout);   // the human car survives GoToMenu: never carry items over from the previous test
            game.participantCount = cars;
            game.rules.mode = GameMode.PointsDerby;
            game.rules.countdownSeconds = 0.2f;
            game.rules.pointsDuration = 180f; game.rules.durabilityEnergy = 650000f;
            game.StartMatch();
            foreach (var p in game.Participants) { p.car.Receiver.logImpacts = false; if (p.car.enemy != null) { p.car.enemy.enabled = false; p.car.controller.Throttle = 0f; p.car.controller.SteerInput = 0f; } }
            StepSeconds(0.3f);
        }

        static void Bare(DerbyCar c) => c.mount.Apply(new Loadout(c.mount.VehicleKey));

        static void PlaceCar(Participant p, Vector3 pos, float yaw, float speed)
        {
            var rb = p.car.Body; var rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = new Vector3(pos.x, p.car.GroundLift, pos.z); rb.rotation = rot;
            rb.transform.SetPositionAndRotation(rb.position, rot);
            rb.linearVelocity = rot * Vector3.forward * speed; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            p.car.controller.ResetState(); p.car.Receiver.ClearCooldowns();
        }

        class Run1
        {
            public readonly List<CollisionDamageReceiver.Report> attacker = new List<CollisionDamageReceiver.Report>(), victim = new List<CollisionDamageReceiver.Report>();
            public readonly List<int> victimSteps = new List<int>();
            public float attackerScore, victimHits;
            public string Timeline() => string.Join(", ", victim.Select((r, i) => $"step {victimSteps[i]} {(r.applied ? "applied" : "skipped")} {r.energy:0} J"));
            public string attackerRoot, victimRoot;
            public double Energy(List<CollisionDamageReceiver.Report> l) => l.Where(r => r.applied).Sum(r => (double)r.energy);
            public CollisionDamageReceiver.Report? FirstApplied(List<CollisionDamageReceiver.Report> l) { foreach (var r in l) if (r.applied) return r; return null; }
        }

        /// <summary>
        /// Rear-end ram at 16 m/s with the given items mounted on the attacker (the human car) and the victim, the attacker <paramref name="lateral"/> metres to the victim's
        /// right of centre. Both cars are bare again afterwards. The run-up is short (about 3 m) on purpose: over the M3 harness' 14 m the attacker drifted up to 0.4 m and
        /// 6 degrees off line, enough to push the 0.4 m ram block across the victim's centre/side shell seam, so the "identical scripted hit" was not identical (M4 finding).
        /// </summary>
        static Run1 Ram(int vehicle, string[] attackerItems, string[] victimItems, float lateral = 0f)
        {
            FreshMatch(vehicle);
            var a = game.Human; var v = game.Participants[1];
            a.car.mount.Apply(Make(a.car.mount.VehicleKey, attackerItems));
            v.car.mount.Apply(Make(v.car.mount.VehicleKey, victimItems));
            PlaceCar(v, new Vector3(0f, 0f, -40f), 90f, 0f);
            PlaceCar(a, new Vector3(-7.5f, 0f, -40f - lateral), 90f, 16f);   // heading 90 = +X, so the victim's right is -Z
            var run = new Run1 { attackerRoot = a.car.rig.root.name, victimRoot = v.car.rig.root.name };
            int step = 0;
            Action<CollisionDamageReceiver.Report> ra = r => run.attacker.Add(r), rv = r => { run.victim.Add(r); run.victimSteps.Add(step); };
            a.car.Receiver.ImpactRegistered += ra; v.car.Receiver.ImpactRegistered += rv;
            float s0 = a.score; int h0 = v.hitsTaken;
            for (; step < Mathf.RoundToInt(2.2f / Dt); step++)
            {
                // Off the throttle from the first contact: one ram, not a ram and a push that re-hits after the pair cooldown.
                a.car.controller.Throttle = run.victim.Count > 0 ? 0f : a.car.controller.ForwardSpeed < 16f ? 1f : 0.3f; a.car.controller.SteerInput = 0f;
                v.car.controller.Throttle = 0f;
                game.ManualStep(Dt);
            }
            a.car.controller.Throttle = 0f;
            a.car.Receiver.ImpactRegistered -= ra; v.car.Receiver.ImpactRegistered -= rv;
            run.attackerScore = a.score - s0; run.victimHits = v.hitsTaken - h0;
            Bare(a.car); Bare(v.car);
            return run;
        }

        static string[] None => new string[0];
        static bool IsShellName(string n) => n != null && n.EndsWith("Box") && !n.StartsWith("Proxy");
        static string Fp(CollisionDamageReceiver.Report? r) => r.HasValue ? $"E={r.Value.energy:0} J, footprint {r.Value.footprint.area:0.000} m2 / travel {r.Value.footprint.travel:0.000} m, boxes {r.Value.impactorBoxes} [{r.Value.impactorColliders}], vn {r.Value.closingSpeed:0.00} (raw {r.Value.rawClosing:0.00}, |v| {r.Value.relativeSpeed:0.00}) m/s, mu {r.Value.reducedMass:0}, {r.Value.contacts} contacts, pen {r.Value.penetration:0.000}" : "no applied report";
        static string Pct(double a, double b) => $"{(a - b) / b:+0.0%;-0.0%}";

        // ------------------------------------------------------------------ data

        static void Data()
        {
            sb.AppendLine(" -- item data");
            var cat = game.attachmentCatalog;
            AttachmentDefinition Def(string id) => cat.items.First(d => d.id == id);
            Check("push bar: one front proxy, wide and low", Def(PushBar).slot == AttachmentSlot.Front && Def(PushBar).proxies.Length == 1 && Def(PushBar).proxies[0].size.x >= 1.4f && Def(PushBar).proxies[0].size.y <= 0.4f, $"{Def(PushBar).proxies.FirstOrDefault().size}");
            Check("ram plate: one front proxy, narrower than the push bar", Def(RamPlate).slot == AttachmentSlot.Front && Def(RamPlate).proxies.Length == 1 && Def(RamPlate).proxies[0].size.x < Def(PushBar).proxies[0].size.x * 0.5f, $"{Def(RamPlate).proxies.FirstOrDefault().size}");
            Check("tow hitch: one rear proxy", Def(Hitch).slot == AttachmentSlot.Rear && Def(Hitch).proxies.Length == 1, $"{Def(Hitch).proxies.FirstOrDefault().size}");
            Check("roof rack: no proxy (visual only)", Def(Rack).proxies.Length == 0);
            Check("no item prefab carries a collider or Rigidbody (collision comes from the definitions only)", cat.items.All(d => d.prefab == null || d.prefab.GetComponentsInChildren<Collider>(true).Length == 0 && d.prefab.GetComponentsInChildren<Rigidbody>(true).Length == 0));
        }

        // ------------------------------------------------------------------ mounting

        static void Mounting(int v)
        {
            sb.AppendLine($" -- {Key(v)}: mounting");
            FreshMatch(v);
            var c = game.Human.car; var rb = c.Body;
            float mass0 = rb.mass; Vector3 com0 = rb.centerOfMass, it0 = rb.inertiaTensor; Quaternion itr0 = rb.inertiaTensorRotation;
            var sbm = typeof(CollisionDamageReceiver).GetMethod("ShellBounds", BindingFlags.NonPublic | BindingFlags.Instance);
            var bounds0 = (Bounds)sbm.Invoke(c.Receiver, null);

            c.mount.Apply(Make(Key(v), PushBar, Hitch, Rack));
            Physics.SyncTransforms();
            var px = c.rig.root.GetComponentsInChildren<AttachmentProxy>();
            Check("push bar + hitch + rack: two proxies, slots 0 and 1, item ids from the definitions, three items mounted",
                c.mount.ProxyCount == 2 && px.Length == 2 && px.Any(p => p.slot == 0 && p.itemId == PushBar) && px.Any(p => p.slot == 1 && p.itemId == Hitch) && c.mount.InstanceCount == 3,
                $"{c.mount.ProxyCount} proxies [{string.Join(", ", px.Select(p => $"{p.slot}:{p.itemId}"))}], {c.mount.InstanceCount} items");
            Check("every proxy is a registered BoxCollider on the car's own Rigidbody, on the shell's layer, with no physics component stripped from the prefabs",
                c.mount.Proxies.All(bc => AttachmentProxy.IsProxy(bc) && bc.attachedRigidbody == rb && bc.gameObject.layer == c.rig.root.GetComponentsInChildren<BoxCollider>().First(s => !AttachmentProxy.IsProxy(s)).gameObject.layer) && c.mount.StrippedPhysicsComponents == 0);
            StepSeconds(0.5f);
            Check("mass and centre of mass are the bare car's, and the inertia tensor is locked to the bare value (also after stepping)",
                rb.mass == mass0 && (rb.centerOfMass - com0).sqrMagnitude < 1e-10f && (rb.inertiaTensor - it0).magnitude <= 1e-4f * it0.magnitude && Quaternion.Angle(rb.inertiaTensorRotation, itr0) < 0.01f && c.mount.InertiaLocked,
                $"mass {mass0}->{rb.mass}, inertia {it0}->{rb.inertiaTensor}");
            var bounds1 = (Bounds)sbm.Invoke(c.Receiver, null);
            int shell = c.rig.root.GetComponentsInChildren<BoxCollider>().Count(bc => !AttachmentProxy.IsProxy(bc));
            Check("the shell is still 14 boxes and its bounds are unchanged with the items on", shell == 14 && (bounds0.size - bounds1.size).magnitude < 1e-4f, $"{shell} boxes, size {bounds0.size} -> {bounds1.size}");

            // Ground clearance: the car is resting on the arena floor after the settle steps.
            float wheelBottom = c.rig.root.transform.TransformPoint(new Vector3(0f, c.rig.geometry.wheels.min.y, 0f)).y;
            float lowest = c.mount.Proxies.Min(bc => bc.bounds.min.y);
            Check("every proxy clears the wheel contact plane by at least 5 cm (no ground scrape, no lift on bumps)", lowest - wheelBottom >= 0.05f, $"lowest proxy {lowest - wheelBottom:0.000} m above the wheel bottoms");

            // Ram plate swap and removal.
            c.mount.Apply(Make(Key(v), RamPlate, Hitch, Rack));
            Check("swapping the push bar for the ram plate leaves one front proxy (the ram's) and the hitch", c.mount.ProxyCount == 2 && c.mount.Proxies.Count(bc => AttachmentProxy.TryGet(bc, out var p) && p.itemId == RamPlate) == 1 && !c.mount.Proxies.Any(bc => AttachmentProxy.TryGet(bc, out var p) && p.itemId == PushBar));
            c.mount.Apply(Make(Key(v), Rack));
            Check("removing both collision items drops every proxy at once and releases the inertia lock (roof rack alone is visual only)", c.mount.ProxyCount == 0 && !c.mount.InertiaLocked && c.mount.InstanceCount == 1 && c.rig.root.GetComponentsInChildren<AttachmentProxy>().Length == 0);
            StepSeconds(0.1f);
            Check("with no proxies left the inertia is the bare car's again", (rb.inertiaTensor - it0).magnitude <= 1e-3f * it0.magnitude, $"{it0} vs {rb.inertiaTensor}");
            Bare(c);
        }

        // ------------------------------------------------------------------ hits

        static void Hits(int v)
        {
            sb.AppendLine($" -- {Key(v)}: rear-end ram at 16 m/s");
            var b1 = Ram(v, None, None); var b2 = Ram(v, None, None); var b3 = Ram(v, None, None);
            double[] baseV = { b1.Energy(b1.victim), b2.Energy(b2.victim), b3.Energy(b3.victim) };
            double meanV = baseV.Average(), noise = (baseV.Max() - baseV.Min()) / meanV;
            double tol = Math.Max(0.01, 3.0 * noise);
            sb.AppendLine($"   bare: victim {Fp(b1.FirstApplied(b1.victim))}; noise over 3 runs {noise:0.0%}");

            var pb = Ram(v, new[] { PushBar }, None);
            var vr = pb.FirstApplied(pb.victim); var ar = pb.FirstApplied(pb.attacker);
            sb.AppendLine($"   push bar: victim {Fp(vr)} ({Pct(pb.Energy(pb.victim), meanV)} vs bare)");
            Check("push bar: the victim's report names the attacker car, the push bar and slot 0, and the footprint comes from the proxy box alone",
                vr.HasValue && vr.Value.other == pb.attackerRoot && vr.Value.otherProxy && vr.Value.otherSlot == 0 && vr.Value.otherItem == PushBar && vr.Value.impactorBoxes == 1 && vr.Value.impactorColliders.StartsWith("Proxy_"),
                vr.HasValue ? $"other={vr.Value.other}, slot {vr.Value.otherSlot}, item {vr.Value.otherItem}, [{vr.Value.impactorColliders}]" : "no applied report");
            Check("push bar: the attacker's own report has ownSlot = Front, a shell box as carCollider and the victim as the other body",
                ar.HasValue && ar.Value.ownProxy && ar.Value.ownSlot == 0 && IsShellName(ar.Value.carCollider) && ar.Value.other == pb.victimRoot, ar.HasValue ? $"carCollider={ar.Value.carCollider}" : "no applied report");
            Check("push bar: scoring and hit counting unchanged (attacker scores, victim takes one hit, one applied report per receiver)",
                pb.attackerScore > 0f && pb.victimHits == 1f && pb.victim.Count(r => r.applied) == 1 && pb.attacker.Count(r => r.applied) == 1, $"score +{pb.attackerScore:0}, victim hits {pb.victimHits}");

            var rp = Ram(v, new[] { RamPlate }, None);
            var vr2 = rp.FirstApplied(rp.victim);
            double ePb = pb.Energy(pb.victim), eRp = rp.Energy(rp.victim);
            double tolE = Math.Max(0.05, 3.0 * noise);
            sb.AppendLine($"   ram plate: victim {Fp(vr2)} ({Pct(eRp, meanV)} vs bare)");
            Check("ram plate: reported as the ram plate in slot 0, credited to the attacker car", vr2.HasValue && vr2.Value.otherProxy && vr2.Value.otherSlot == 0 && vr2.Value.otherItem == RamPlate && vr2.Value.other == rp.attackerRoot && rp.victimHits == 1f, rp.Timeline());
            Check("ram plate: the victim's dent footprint is smaller than with the push bar", vr.HasValue && vr2.HasValue && vr.Value.footprint.valid && vr2.Value.footprint.valid && vr2.Value.footprint.area < vr.Value.footprint.area,
                vr.HasValue && vr2.HasValue ? $"{vr2.Value.footprint.area:0.000} m2 vs {vr.Value.footprint.area:0.000} m2; travel {vr2.Value.footprint.travel:0.000} vs {vr.Value.footprint.travel:0.000} m" : "");
            Check($"ram plate: the energy of the identical scripted hit is the push bar's (within {tolE:0.0%}): the item changes where it dents, not how hard", ePb > 0 && Math.Abs(eRp - ePb) / ePb <= tolE, $"{eRp:0} J vs {ePb:0} J ({Pct(eRp, ePb)})");
            // Informational (documented known issue, not a pass/fail): the same hit 0.3 m off centre puts the ram block across the victim's centre/side shell seam.
            var rpOff = Ram(v, new[] { RamPlate }, None, 0.3f); var pbOff = Ram(v, new[] { PushBar }, None, 0.3f);
            sb.AppendLine($"   info: 0.3 m off centre: ram plate {rpOff.Energy(rpOff.victim):0} J vs push bar {pbOff.Energy(pbOff.victim):0} J ({Pct(rpOff.Energy(rpOff.victim), pbOff.Energy(pbOff.victim))}); ram {Fp(rpOff.FirstApplied(rpOff.victim))}");

            var th = Ram(v, None, new[] { Hitch });
            var vr3 = th.FirstApplied(th.victim); var ar3 = th.FirstApplied(th.attacker);
            sb.AppendLine($"   tow hitch on the victim: victim {Fp(vr3)} ({Pct(th.Energy(th.victim), meanV)} vs bare)");
            Check("tow hitch on the victim: its own report says ownSlot = Rear with a shell box as carCollider; the attacker sees the hitch in slot 1",
                vr3.HasValue && ar3.HasValue && vr3.Value.ownProxy && vr3.Value.ownSlot == 1 && IsShellName(vr3.Value.carCollider) && ar3.Value.otherProxy && ar3.Value.otherSlot == 1 && ar3.Value.otherItem == Hitch,
                vr3.HasValue ? $"carCollider={vr3.Value.carCollider}" : "no applied report");
            Check("tow hitch: attribution and one event per pair hold", th.attackerScore > 0f && th.victimHits == 1f && th.victim.Count(r => r.applied) == 1 && th.attacker.Count(r => r.applied) == 1, th.Timeline());

            var rk = Ram(v, new[] { Rack }, new[] { Rack });
            double eRk = rk.Energy(rk.victim);
            Check($"roof racks on both cars (visual only) leave the bare hit unchanged (within {tol:0.0%})", Math.Abs(eRk - meanV) / meanV <= tol && rk.victim.All(r => !r.ownProxy && !r.otherProxy), $"{eRk:0} J vs {meanV:0} J ({Pct(eRk, meanV)})");
        }

        // ------------------------------------------------------------------ showdown and match start

        static void Showdown()
        {
            sb.AppendLine(" -- Final Showdown placement with the largest proxies");
            var prepare = typeof(DerbyGame).GetMethod("OnShowdownPrepare", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int v = 0; v < game.vehicles.Length; v++)
            {
                FreshMatch(v, 5);
                foreach (var p in game.Participants) p.car.mount.Apply(Make(p.car.mount.VehicleKey, PushBar, Hitch, Rack));
                Physics.SyncTransforms();
                var a = game.Participants[0]; var b = game.Participants[1];
                prepare.Invoke(game, new object[] { a, b });
                Check($"{Key(v)}: the duel finds clear spots for two cars carrying push bar + hitch + rack, and both keep their proxies through the reset",
                    game.ShowdownPlacedClear && a.car.mount.ProxyCount == 2 && b.car.mount.ProxyCount == 2, $"placed clear {game.ShowdownPlacedClear}");
                foreach (var p in game.Participants) Bare(p.car);
            }
        }

        static void StartWithLoadout()
        {
            sb.AppendLine(" -- match start with a full player loadout");
            game.GoToMenu();
            game.SelectVehicle(2);
            game.SetHumanLoadoutItem(AttachmentSlot.Front, RamPlate);
            game.SetHumanLoadoutItem(AttachmentSlot.Rear, Hitch);
            game.SetHumanLoadoutItem(AttachmentSlot.Roof, Rack);
            game.participantCount = 5;
            game.StartMatch();
            StepSeconds(1f);
            var c = game.Human.car;
            Check("StartMatch with ram + hitch + rack: valid loadout, three items and two proxies on the player car, every car on its wheels",
                game.HumanLoadoutResult.IsValid && c.mount.InstanceCount == 3 && c.mount.ProxyCount == 2 && game.Participants.All(p => p.car.Body.position.y < 3f && Vector3.Dot(p.car.Body.transform.up, Vector3.up) > 0.8f),
                $"{c.mount.InstanceCount} items, {c.mount.ProxyCount} proxies");
            game.SetHumanLoadoutItem(AttachmentSlot.Front, ""); game.SetHumanLoadoutItem(AttachmentSlot.Rear, ""); game.SetHumanLoadoutItem(AttachmentSlot.Roof, "");
            game.GoToMenu();
        }
    }
}
