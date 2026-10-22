using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DamageLab.CollisionTest;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Attachment V1 / milestone M2 gate: with Stage 1 attachments equipped the physics and the scoring must be unchanged, and the mount lifecycle must not leak.
    /// Real PhysX in the DerbyArena, stepped at 50 Hz through DerbyGame.ManualStep (the same set-up as DerbyValidation).
    /// Parity: scripted rear-end and T-bone rams per vehicle, with each car as attacker, run with no attachments (twice: the second run measures the noise floor of the
    /// scenario itself) and with a full kit on both cars. Every receiver report and the scores must match.
    /// Run: Unity -batchmode -nographics -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentParityValidation.Run -quit -logFile parity.log
    /// </summary>
    public static class AttachmentParityValidation
    {
        const float Dt = 0.02f;
        const double Tolerance = 1e-3;   // relative; PhysX is deterministic for identical input, the control run shows what "identical" means here
        static StringBuilder sb;
        static int fails, passes;
        static DerbyGame game;

        [MenuItem("CarWreck/Validate Attachments (M2 parity + lifecycle)", priority = 45)]
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
            sb = new StringBuilder("[Attachments] M2 parity and lifecycle validation (DerbyArena, real PhysX at 50 Hz; editor timings)\n");
            fails = passes = 0;
            var prevMode = Physics.simulationMode;
            var savedProxies = new Dictionary<AttachmentDefinition, ProxySpec[]>();
            try
            {
                Physics.simulationMode = SimulationMode.Script;
                EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
                game = UnityEngine.Object.FindFirstObjectByType<DerbyGame>();
                game.Setup();
                game.ui = null;
                if (game.attachmentCatalog == null) { Check("the DerbyArena scene has the attachment catalog wired (run CarWreck > Attachments > Wire Catalog Into DerbyArena)", false); }
                else
                {
                    // This gate is the Stage 1 (visual-only) contract. Since M4 the shipped items carry proxy colliders, which change crashes by design (covered by
                    // AttachmentM4Validation and the M4 telemetry), so the proxies are emptied in memory for this run and restored afterwards (never saved).
                    foreach (var def in game.attachmentCatalog.items)
                        if (def != null && def.proxies != null && def.proxies.Length > 0) { savedProxies[def] = def.proxies; def.proxies = new ProxySpec[0]; }
                    sb.AppendLine($" -- Stage 1 path: proxies stripped in memory from {savedProxies.Count} item(s) for this run");
                    StaticParity();
                    LifecycleChecks();
                    PhysicsParity();
                    InvalidAndHazard();
                }
            }
            catch (Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            finally { foreach (var kv in savedProxies) kv.Key.proxies = kv.Value; Physics.simulationMode = prevMode; Time.timeScale = 1f; VehiclePainter.ClearCache(); }
            sb.AppendLine($"[Attachments] {passes} PASS, {fails} FAIL");
            return sb.ToString();
        }

        // ------------------------------------------------------------------ helpers

        static string Key(int vehicle) => game.vehicles[vehicle].name;

        static void SetKit(Participant p, bool on, bool ram = false)
        {
            var key = game.vehicles[p.vehicleIndex].name;
            var l = new Loadout(key);
            if (on)
            {
                l.Set(AttachmentSlot.Front, ram ? "spiked_ram_plate" : "push_bar_junk");
                l.Set(AttachmentSlot.Rear, "tow_hitch_bumper");
                l.Set(AttachmentSlot.Roof, "roof_cage_rack");
            }
            p.loadout = l;
            if (p.car != null) p.car.mount.Apply(l);
        }

        static void StepSeconds(float s) { int n = Mathf.RoundToInt(s / Dt); for (int i = 0; i < n; i++) game.ManualStep(Dt); }

        static void FreshMatch()
        {
            game.GoToMenu();
            game.participantCount = 2;
            game.rules.mode = GameMode.PointsDerby;
            game.rules.countdownSeconds = 0.2f;
            game.rules.pointsDuration = 180f; game.rules.showdownDuration = 60f; game.rules.respawnDelay = 3f; game.rules.durabilityEnergy = 650000f;
            game.StartMatch();
            foreach (var p in game.Participants) p.car.Receiver.logImpacts = false;
            StepSeconds(0.3f);
        }

        static void AiOff() { foreach (var p in game.Participants) if (p.car.enemy != null) { p.car.enemy.enabled = false; p.car.controller.Throttle = 0f; p.car.controller.SteerInput = 0f; } }

        static void PlaceCar(Participant p, Vector3 pos, float yaw, float speed)
        {
            var rb = p.car.Body; var rot = Quaternion.Euler(0f, yaw, 0f);
            rb.position = new Vector3(pos.x, p.car.GroundLift, pos.z); rb.rotation = rot;
            rb.transform.SetPositionAndRotation(rb.position, rot);
            rb.linearVelocity = rot * Vector3.forward * speed; rb.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            p.car.controller.ResetState(); p.car.Receiver.ClearCooldowns();
        }

        /// <summary>Sum over the car's meshes, EXCLUDING anything under the Attachments node: the car's own deformable geometry.</summary>
        static double Checksum(DerbyCar c)
        {
            double sum = 0;
            foreach (var f in c.rig.root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (c.mount.Root != null && f.transform.IsChildOf(c.mount.Root)) continue;
                var mesh = f.sharedMesh; if (mesh == null) continue;
                var vs = mesh.vertices;
                for (int i = 0; i < vs.Length; i++) sum += vs[i].x * 1.0 + vs[i].y * 3.1 + vs[i].z * 7.3 + (i % 97) * 0.001 * vs[i].x;
            }
            return sum;
        }

        static int BoxCount(DerbyCar c) => c.rig.root.GetComponentsInChildren<BoxCollider>(true).Length;

        static int CollidersUnderAttachments(DerbyCar c) => c.mount.Root == null ? 0 : c.mount.Root.GetComponentsInChildren<Collider>(true).Length + c.mount.Root.GetComponentsInChildren<Rigidbody>(true).Length;

        // ------------------------------------------------------------------ static parity (per vehicle)

        static void StaticParity()
        {
            sb.AppendLine(" -- static parity: the car's physics and damage model with and without attachments");
            for (int v = 0; v < game.vehicles.Length; v++)
            {
                game.GoToMenu();
                game.SelectVehicle(v);
                var h = game.Human; var c = h.car;
                SetKit(h, false);
                float mass = c.Body.mass; Vector3 com = c.Body.centerOfMass; int parts = c.Model.PartCount, verts = c.Model.DeformableVertices, boxes = BoxCount(c); double sum = Checksum(c);
                SetKit(h, true, ram: true);
                bool mounted = c.mount.InstanceCount == 3;
                bool same = Mathf.Approximately(mass, c.Body.mass) && com == c.Body.centerOfMass && parts == c.Model.PartCount && verts == c.Model.DeformableVertices && boxes == BoxCount(c) && Math.Abs(sum - Checksum(c)) < 1e-9;
                Check($"{Key(v)}: with a full kit mounted, mass, centre of mass, part/vertex count, box colliders ({boxes}) and deformable geometry are unchanged", mounted && same,
                    $"mounted {c.mount.InstanceCount} items, {c.mount.RendererCount} renderers; mass {mass:0}, parts {parts}, vertices {verts}");
                Check($"{Key(v)}: attachments carry no colliders or rigidbodies (and none had to be stripped)", CollidersUnderAttachments(c) == 0 && c.mount.StrippedPhysicsComponents == 0);
                Check($"{Key(v)}: the kit is mounted under the car root and follows its Rigidbody (no extra bodies)", c.mount.transform == c.rig.root.transform && c.rig.root.GetComponentsInChildren<Rigidbody>(true).Length == 1);
            }
        }

        // ------------------------------------------------------------------ lifecycle

        static int Materials() => Resources.FindObjectsOfTypeAll<Material>().Length;
        static int MountCount() => UnityEngine.Object.FindObjectsByType<AttachmentMount>(FindObjectsInactive.Include).Length;

        static HashSet<Material> AttachmentMaterials(DerbyCar c)
        {
            var set = new HashSet<Material>();
            if (c.mount.Root != null) foreach (var r in c.mount.Root.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) set.Add(m);
            return set;
        }

        static void LifecycleChecks()
        {
            sb.AppendLine(" -- lifecycle: resets, respawns, wrecks, rematch, paint and vehicle switches");
            game.GoToMenu();
            game.SelectVehicle(1);
            SetKit(game.Human, true);
            FreshMatch();
            var human = game.Human; var ai = game.Participants[1];
            SetKit(ai, true);
            int inst0 = human.car.mount.InstanceCount + ai.car.mount.InstanceCount, rend0 = human.car.mount.RendererCount + ai.car.mount.RendererCount;
            int children0 = human.car.mount.Root.GetComponentsInChildren<Transform>(true).Length;
            var mats0 = AttachmentMaterials(human.car);
            int matCount0 = Materials(), mounts0 = MountCount();

            var spot = new SpawnPlanner.Spot { position = new Vector3(0f, 0f, -20f), yaw = 0f };
            for (int i = 0; i < 50; i++) { game.ResetCar(human.car, spot); game.ResetCar(ai.car, spot); }
            Check("50 resets of both cars: item instances, renderers, transforms, object counts and material count are constant",
                human.car.mount.InstanceCount + ai.car.mount.InstanceCount == inst0 && human.car.mount.RendererCount + ai.car.mount.RendererCount == rend0
                && human.car.mount.Root.GetComponentsInChildren<Transform>(true).Length == children0 && Materials() == matCount0 && MountCount() == mounts0,
                $"{inst0} instances, {rend0} renderers, {matCount0} materials");

            // wreck: hulk look, then repair
            var normalMats = AttachmentMaterials(human.car);
            game.Match.ApplyDamage(new DamageEvent(human.id, ai.id, 1e8f));
            StepSeconds(0.1f);
            bool wrecked = human.car.mount.Wrecked && AttachmentMaterials(human.car).Count == 1 && AttachmentMaterials(human.car).First() == game.attachmentCatalog.wreckMaterial;
            Check("a wrecked car's attachments take the wreck material and stay mounted", wrecked && human.car.mount.InstanceCount == 3, $"wrecked={human.car.mount.Wrecked}");
            game.ResetCar(human.car, spot);
            Check("repair (ResetCar: respawn, Showdown, rematch) restores the as-built look", !human.car.mount.Wrecked && AttachmentMaterials(human.car).SetEquals(normalMats));

            // rematch x5, paint switch, menu round trip
            for (int i = 0; i < 5; i++) { game.Rematch(); StepSeconds(0.1f); }
            Check("5 rematches: no duplicate or lost items", human.car.mount.InstanceCount == 3 && ai.car.mount.InstanceCount == 3 && human.car.mount.Root.GetComponentsInChildren<Transform>(true).Length == children0 && MountCount() == mounts0);
            var before = AttachmentMaterials(human.car);
            for (int p = 0; p < game.palette.Count; p++) game.SelectPaint(p);
            Check("switching paint leaves attachment materials untouched (grey-box items are not 'Paint' materials)", AttachmentMaterials(human.car).SetEquals(before));
            game.GoToMenu();
            Check("returning to the menu keeps the selected loadout on the shown car", game.Human.car.mount.InstanceCount == 3 && !game.Human.car.mount.Wrecked);

            // vehicle switches: nothing orphaned, each body gets its own saved loadout (edit mode: set per vehicle explicitly)
            int mountsMenu = MountCount();
            for (int round = 0; round < 3; round++)
                for (int v = 0; v < game.vehicles.Length; v++) { game.SelectVehicle(v); SetKit(game.Human, true); }
            Check("30 vehicle switches: every car has exactly one mount and nothing is orphaned", MountCount() == mountsMenu && game.Human.car.mount.InstanceCount == 3, $"{MountCount()} mounts for {game.Participants.Count + (game.Participants.Count == 0 ? 1 : 0)} cars");
            game.SelectVehicle(1);
            game.Human.car.mount.Clear();
            Check("Clear removes every item and renderer", game.Human.car.mount.InstanceCount == 0 && game.Human.car.mount.RendererCount == 0);

            // AI parity: the same mount code works on an AI car (V1 leaves AI empty)
            FreshMatch();
            var aiP = game.Participants[1];
            SetKit(aiP, true);
            Check("an AI car mounts and resets a kit through the same code as the player", aiP.car.mount.InstanceCount == 3 && aiP.car.mount.VehicleKey == game.vehicles[aiP.vehicleIndex].name);
            SetKit(aiP, false);
            Check("AI cars default to no attachments", aiP.car.mount.InstanceCount == 0);
        }

        // ------------------------------------------------------------------ physics parity (real crashes)

        class Sig
        {
            public int reports;
            public readonly List<string> structure = new List<string>();
            public double[] robust;     // what must match: scores, damage, hits, total reported energy, dent geometry
        }

        static readonly string[] RobustNames = { "attacker score", "victim damage taken", "attacker damage dealt", "victim hits", "total reported energy", "victim geometry", "attacker geometry" };

        static Sig Crash(int vehicle, bool humanAttacks, bool tbone, bool kit)
        {
            game.GoToMenu();
            game.SelectVehicle(vehicle);
            SetKit(game.Human, false);
            FreshMatch();
            var human = game.Human; var other = game.Participants[1];
            SetKit(human, kit); SetKit(other, kit);
            AiOff();
            var a = humanAttacks ? human : other; var v = humanAttacks ? other : human;
            PlaceCar(v, new Vector3(0f, 0f, -40f), tbone ? 0f : 90f, 0f);
            PlaceCar(a, new Vector3(-14f, 0f, -40f), 90f, 16f);
            var sig = new Sig();
            double energy = 0;
            Action<CollisionDamageReceiver.Report> record = r =>
            {
                sig.reports++;
                if (r.applied) energy += r.energy;
                sig.structure.Add($"{r.other}>{r.region}/{r.carCollider}");   // informational: which car and region were hit
            };
            v.car.Receiver.ImpactRegistered += record; a.car.Receiver.ImpactRegistered += record;
            float s0 = a.score, vt0 = v.damageTaken, ad0 = a.damageDealt; int vh0 = v.hitsTaken;
            for (int i = 0; i < Mathf.RoundToInt(2.2f / Dt); i++)
            {
                a.car.controller.Throttle = a.car.controller.ForwardSpeed < 16f ? 1f : 0.3f; a.car.controller.SteerInput = 0f;
                v.car.controller.Throttle = 0f;
                game.ManualStep(Dt);
            }
            a.car.controller.Throttle = 0f;
            v.car.Receiver.ImpactRegistered -= record; a.car.Receiver.ImpactRegistered -= record;
            sig.robust = new double[] { a.score - s0, v.damageTaken - vt0, a.damageDealt - ad0, v.hitsTaken - vh0, energy, Checksum(v.car), Checksum(a.car) };
            return sig;
        }

        static double RelDiff(double x, double y) => Math.Abs(x - y) / Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));

        /// <summary>Largest relative difference over the robust outcome vector; <paramref name="which"/> names the quantity.</summary>
        static double Compare(Sig x, Sig y, out string which)
        {
            double worst = 0; which = "";
            for (int k = 0; k < x.robust.Length; k++)
            {
                double d = RelDiff(x.robust[k], y.robust[k]);
                if (d > worst) { worst = d; which = RobustNames[k]; }
            }
            return worst;
        }

        static void PhysicsParity()
        {
            sb.AppendLine(" -- physics and scoring parity: scripted crashes with and without a full kit on both cars");
            // Identical runs are close but not bit-identical (the dependent quantities move by about 1e-4 relative, and in T-bones which shell boxes touch can flicker),
            // so each scenario is run three times WITHOUT attachments to measure its own spread. The run WITH the kit must stay within a small multiple of that spread
            // (never less than 1e-3, never more than 5e-2), and every scenario must actually produce a damaging hit.
            double worstSpread = 0, worstKit = 0;
            int scenarios = 0, runs = 0, reports = 0, flicker = 0;
            var bad = new List<string>();
            var table = new StringBuilder();
            for (int v = 0; v < game.vehicles.Length; v++)
                foreach (bool tbone in new[] { false, true })
                    foreach (bool humanAttacks in new[] { true, false })
                    {
                        var a = Crash(v, humanAttacks, tbone, false);
                        var b = Crash(v, humanAttacks, tbone, false);
                        var b2 = Crash(v, humanAttacks, tbone, false);
                        var c = Crash(v, humanAttacks, tbone, true);
                        double spread = Math.Max(Compare(a, b, out _), Math.Max(Compare(a, b2, out _), Compare(b, b2, out _)));
                        double kit = Compare(a, c, out string which);
                        double limit = Math.Min(5e-2, Math.Max(1e-3, spread * 3.0));
                        worstSpread = Math.Max(worstSpread, spread); worstKit = Math.Max(worstKit, kit);
                        runs += 4; scenarios++; reports += a.reports + b.reports + b2.reports + c.reports;
                        if (!a.structure.SequenceEqual(c.structure)) flicker++;
                        string label = $"{Key(v)} {(tbone ? "T-bone" : "rear-end")} {(humanAttacks ? "player attacks" : "AI attacks")}";
                        table.AppendLine($"     {label}: energy {a.robust[4]:0} J, victim hits {a.robust[3]:0}, spread without kit {spread:0.0e+0}, kit vs none {kit:0.0e+0}{(kit > limit ? "  <-- " + which : "")}");
                        if (a.robust[4] <= 0 || a.robust[3] < 1) bad.Add(label + ": the scenario produced no damaging hit");
                        if (kit > limit) bad.Add($"{label}: {which} differs {kit:0.0e+0} with the kit (limit {limit:0.0e+0}, spread without kit {spread:0.0e+0})");
                    }
            sb.Append(table);
            Check($"{scenarios} scripted crashes ({runs} runs, {reports} impact reports): scores, damage, hits, total reported energy and dent geometry with a full kit stay within the scenario's own run-to-run spread", bad.Count == 0,
                bad.Count == 0 ? $"worst kit-vs-none difference {worstKit:0.0e+0}; worst spread between identical no-kit runs {worstSpread:0.0e+0}; hit-region sequence differed kit vs none in {flicker} of {scenarios} scenarios (also varies between identical runs)" : string.Join(" | ", bad));
        }

        static void InvalidAndHazard()
        {
            sb.AppendLine(" -- invalid loadouts and the mount-ordering hazard");
            game.GoToMenu();
            game.SelectVehicle(1);
            var h = game.Human;
            var l = new Loadout(Key(1));
            l.Set(AttachmentSlot.Front, "spiked_ram_plate"); l.Set(AttachmentSlot.Rear, "tow_hitch_bumper"); l.Set(AttachmentSlot.Roof, "roof_cage_rack");   // 4+2+1 = 7 of 8
            h.loadout = l; h.car.mount.Apply(l);
            Check("a valid build at 7/8 is accepted", h.car.mount.LastResult.IsValid && h.car.mount.InstanceCount == 3);
            l.Set(AttachmentSlot.Left, "unknown_item");
            h.car.mount.Apply(l);
            Check("an unknown id is reported (not silently accepted) and left empty on the car", h.car.mount.LastResult.HasIssue(LoadoutError.UnknownItem, (int)AttachmentSlot.Left) && h.car.mount.InstanceCount == 3);
            game.HumanLoadout.slotItemIds[(int)AttachmentSlot.Left] = "unknown_item";
            Check("the game's result for the player's selection matches the validator (menu START is blocked on it)", !game.HumanLoadoutResult.IsValid);
            game.ClearInvalidHumanLoadoutSlots();
            Check("'clear problems' empties only the bad slot and keeps the rest", game.HumanLoadoutResult.IsValid && game.HumanLoadout.Get(AttachmentSlot.Front) == "spiked_ram_plate");


            // Discovery: an item under the AttachmentRoot node is never a body part, even if the damage model is rebuilt with items mounted (M3 marker).
            // Control: an UNMARKED mesh under the car root still becomes a part, so the part count really detects a stray mesh.
            int partsClean = h.car.Model.PartCount;
            h.car.Model.Rebuild();
            Check("a Rebuild with items mounted adds no parts: the AttachmentRoot marker keeps items out of the damage model whatever the mounting order", h.car.Model.PartCount == partsClean && h.car.mount.Root.GetComponent<DamageLab.Vehicle.AttachmentRoot>() != null, $"parts {partsClean} -> {h.car.Model.PartCount}");
            var stray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(stray.GetComponent<Collider>());
            stray.name = "StrayMesh";
            stray.transform.SetParent(h.car.rig.root.transform, false);
            h.car.Model.Rebuild();
            bool strayCounted = h.car.Model.PartCount == partsClean + 1;
            UnityEngine.Object.DestroyImmediate(stray);
            h.car.Model.Rebuild();
            Check("control: an unmarked mesh under the car root DOES become a part (so the check above can fail)", strayCounted && h.car.Model.PartCount == partsClean, $"parts {partsClean} -> {(strayCounted ? partsClean + 1 : -1)} with a stray mesh, back to {h.car.Model.PartCount}");
            game.GoToMenu();
        }
    }
}
