using System.Collections.Generic;
using System.IO;
using System.Text;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Attachment fit tooling (milestone M2). Builds each vehicle with the derby's own <see cref="VehicleRig"/>, then:
    /// - SeedSockets: proposes the five sockets per body from the measured geometry (rays onto the physics shell), saved into the per-vehicle fit assets;
    /// - RunMatrix: mounts every catalog item on every body and reports envelope, shell penetration, seat gap, neighbour-slot and wheel clearance, then the gap
    ///   left after a heavy front crush / roof dent, and writes screenshots to Previews/Attachments;
    /// - WireScene: assigns the catalog to the DerbyGame in the DerbyArena scene.
    /// Editor only; nothing here runs in a build. Socket axes: +Z points outward from the slot; envelope axes are socket-local (x width, y height or length, z outward).
    /// Run: Unity -projectPath . -executeMethod CarWreck.Derby.EditorTools.AttachmentFitTool.RunMatrix -quit -logFile fit.log   (graphics needed for the screenshots)
    /// </summary>
    public static class AttachmentFitTool
    {
        const string VehicleFolder = "Assets/DamageLab/Settings/Vehicles";
        public const string PreviewFolder = "Previews/Attachments";
        public const string ReportPath = "Docs/Attachments/m2-fit-report.md";
        const float PenetrationTolerance = 0.005f;   // 5 mm
        const float SeatGapLimit = 0.04f;            // the mount plane may float at most 4 cm off the physics shell (brackets bridge this)

        static readonly string[] Keys = { "CompactHatchback", "MuscleCar", "CompactPickup" };

        // ------------------------------------------------------------------ scene and rig helpers

        static VehicleRigSettings OpenArenaAndGetSettings()
        {
            EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<DerbyGame>();
            return game != null ? game.rigSettings : new VehicleRigSettings();
        }

        public static VehicleRig.Rig BuildRig(string key, VehicleRigSettings settings)
        {
            var def = AssetDatabase.LoadAssetAtPath<VehicleDefinition>($"{VehicleFolder}/{key}.asset");
            if (def == null) throw new System.InvalidOperationException($"No vehicle definition for '{key}'.");
            var rig = VehicleRig.Build(def, null, new Vector3(0f, 0f, -24f), settings);
            if (rig.footprintDebug != null) Object.DestroyImmediate(rig.footprintDebug);
            if (rig.colliderDebug != null) Object.DestroyImmediate(rig.colliderDebug);
            rig.model.progressiveCrush = new ProgressiveCrushSettings { enabled = false };
            rig.model.Initialize();
            rig.hitbox.Setup();
            rig.root.transform.position = new Vector3(0f, rig.geometry.groundLift, -24f);
            rig.body.position = rig.root.transform.position;
            Physics.SyncTransforms();
            return rig;
        }

        static void Dispose(VehicleRig.Rig rig)
        {
            if (rig == null || rig.root == null) return;
            rig.model.Shutdown();
            Object.DestroyImmediate(rig.root);
        }

        static List<BoxCollider> ShellBoxes(VehicleRig.Rig rig)
        {
            var boxes = new List<BoxCollider>();
            foreach (var bc in rig.root.GetComponentsInChildren<BoxCollider>()) if (bc.attachedRigidbody == rig.body && !DamageLab.CollisionTest.AttachmentProxy.IsProxy(bc)) boxes.Add(bc);   // an item's own proxies are not shell
            return boxes;
        }

        // ------------------------------------------------------------------ socket seeding

        [MenuItem("CarWreck/Attachments/Seed Sockets From Geometry", priority = 42)]
        public static void SeedSockets()
        {
            var settings = OpenArenaAndGetSettings();
            var catalog = AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentAssetBuilder.CatalogPath);
            if (catalog == null) { Debug.LogError("[Attachments] Build the placeholder data first."); return; }
            foreach (var key in Keys)
            {
                var fit = catalog.FitFor(key);
                if (fit == null) continue;
                var rig = BuildRig(key, settings);
                try
                {
                    fit.sockets = Seed(rig);
                    EditorUtility.SetDirty(fit);
                    Debug.Log($"[Attachments] Seeded sockets for {key}: body {rig.geometry.body.size:F2}, cabin {rig.geometry.cabin.size:F2}");
                }
                finally { Dispose(rig); }
            }
            AssetDatabase.SaveAssets();
        }

        static AttachmentSocket[] Seed(VehicleRig.Rig rig)
        {
            var b = rig.geometry.body; var c = rig.geometry.cabin;
            var boxes = ShellBoxes(rig);
            Vector3 origin = rig.root.transform.position;
            // distance along a car-space ray (start, dir) to the physics shell, or the fallback when it misses
            float Hit(Vector3 start, Vector3 dir, Vector3 lateral, float half, float fallback)
            {
                float best = float.MaxValue;
                for (int k = -3; k <= 3; k++)   // seven rays across the item width: the socket sits on the outermost point so no corner of the item sinks into the shell
                {
                    var ray = new Ray(origin + start + lateral * (half * k / 3f), dir);
                    foreach (var bc in boxes) if (bc.Raycast(ray, out var h, 6f) && h.distance < best) best = h.distance;
                }
                return best < float.MaxValue ? best : fallback;
            }
            var s = new AttachmentSocket[VehicleAttachmentFit.SlotCount];
            float low = b.min.y + b.size.y * 0.35f, mid = b.min.y + b.size.y * 0.5f;

            Vector3 fs = new Vector3(b.center.x, low, b.max.z + 1f);
            s[(int)AttachmentSlot.Front] = new AttachmentSocket { localPosition = fs + Vector3.back * Hit(fs, Vector3.back, Vector3.right, 0.75f, 1f), localEulerAngles = new Vector3(0, 0, 0), envelopeSize = new Vector3(b.size.x * 0.95f, 0.7f, 0.6f) };
            Vector3 rs = new Vector3(b.center.x, low, b.min.z - 1f);
            s[(int)AttachmentSlot.Rear] = new AttachmentSocket { localPosition = rs + Vector3.forward * Hit(rs, Vector3.forward, Vector3.right, 0.75f, 1f), localEulerAngles = new Vector3(0, 180, 0), envelopeSize = new Vector3(b.size.x * 0.95f, 0.7f, 0.6f) };
            Vector3 ls = new Vector3(b.min.x - 1f, mid, b.center.z);
            s[(int)AttachmentSlot.Left] = new AttachmentSocket { localPosition = ls + Vector3.right * Hit(ls, Vector3.right, Vector3.forward, 0.6f, 1f), localEulerAngles = new Vector3(0, -90, 0), envelopeSize = new Vector3(b.size.z * 0.5f, 0.7f, 0.3f) };
            Vector3 rgs = new Vector3(b.max.x + 1f, mid, b.center.z);
            s[(int)AttachmentSlot.Right] = new AttachmentSocket { localPosition = rgs + Vector3.left * Hit(rgs, Vector3.left, Vector3.forward, 0.6f, 1f), localEulerAngles = new Vector3(0, 90, 0), envelopeSize = new Vector3(b.size.z * 0.5f, 0.7f, 0.3f) };
            Vector3 roofs = new Vector3(c.center.x, c.max.y + 1f, c.center.z);
            // roof: socket +Z up, socket +Y vehicle forward (Euler -90,180,0); envelope x = width, y = length, z = height above the roof
            s[(int)AttachmentSlot.Roof] = new AttachmentSocket { localPosition = roofs + Vector3.down * Hit(roofs, Vector3.down, Vector3.forward, 0.6f, 1f), localEulerAngles = new Vector3(-90, 180, 0), envelopeSize = new Vector3(c.size.x * 0.9f, c.size.z * 0.9f, 0.4f) };
            for (int i = 0; i < s.Length; i++)
            {
                var p = s[i].localPosition;
                s[i].localPosition = new Vector3(Mathf.Round(p.x * 1000f) / 1000f, Mathf.Round(p.y * 1000f) / 1000f, Mathf.Round(p.z * 1000f) / 1000f);
            }
            return s;
        }

        // ------------------------------------------------------------------ the fit matrix

        class Row
        {
            public string vehicle, item, slot;
            public bool envelope, neighbours, wheels;
            public float penetration, seatGap;
            public bool Pass => envelope && neighbours && wheels && penetration <= PenetrationTolerance && seatGap <= SeatGapLimit;
        }

        [MenuItem("CarWreck/Attachments/Run Fit Matrix", priority = 43)]
        public static void RunMatrix()
        {
            var sb = new StringBuilder();
            int fails = 0;
            try { fails = Execute(sb); }
            catch (System.Exception e) { sb.AppendLine("EXCEPTION: " + e); fails++; }
            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static int Execute(StringBuilder sb)
        {
            var settings = OpenArenaAndGetSettings();
            var catalog = AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentAssetBuilder.CatalogPath);
            if (catalog == null) throw new System.InvalidOperationException("Build the placeholder data first.");
            Directory.CreateDirectory(PreviewFolder);
            var rows = new List<Row>();
            var crush = new List<string>();
            var shots = new List<string>();
            foreach (var key in Keys)
            {
                foreach (var item in catalog.items)
                {
                    var rig = BuildRig(key, settings);
                    try { rows.Add(Measure(rig, catalog, key, item)); }
                    finally { Dispose(rig); }
                }
                var kit = BuildRig(key, settings);
                try
                {
                    var mount = kit.root.AddComponent<AttachmentMount>();
                    mount.Configure(catalog, key);
                    var loadoutA = Kit("push_bar_junk", "tow_hitch_bumper", "roof_cage_rack", key);
                    mount.Apply(loadoutA);
                    shots.AddRange(Shoot(kit, key, "A", new[] { "front3q", "rear3q", "side", "top" }));
                    var loadoutB = Kit("spiked_ram_plate", "tow_hitch_bumper", "roof_cage_rack", key);
                    mount.Apply(loadoutB);
                    shots.AddRange(Shoot(kit, key, "B", new[] { "front3q" }));
                    mount.Apply(loadoutA);
                    crush.Add(CrushGap(kit, mount, key));
                    shots.AddRange(Shoot(kit, key, "A_crushed", new[] { "front3q", "top" }));
                    mount.SetWrecked(true);
                    shots.AddRange(Shoot(kit, key, "A_wrecked", new[] { "front3q" }));
                }
                finally { Dispose(kit); }
            }
            int fails = 0;
            sb.AppendLine("# Attachment fit report (milestone M2, generated)");
            sb.AppendLine();
            sb.AppendLine("Generated by `CarWreck/Attachments/Run Fit Matrix`. Placeholder grey-box items; sockets seeded from geometry. Penetration limit 5 mm, seat gap limit 40 mm, envelope/neighbour/wheel checks are AABB-based approximations.");
            sb.AppendLine();
            sb.AppendLine("| Vehicle | Item | Slot | Envelope | Neighbours | Wheels | Penetration (mm) | Seat gap (mm) | Result |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in rows)
            {
                if (!r.Pass) fails++;
                sb.AppendLine($"| {r.vehicle} | {r.item} | {r.slot} | {Mark(r.envelope)} | {Mark(r.neighbours)} | {Mark(r.wheels)} | {r.penetration * 1000f:0.0} | {r.seatGap * 1000f:0.0} | **{(r.Pass ? "PASS" : "FAIL")}** |");
            }
            sb.AppendLine();
            sb.AppendLine("## Gap behind a rigid item after crush damage (informational; the owner accepted about 160 mm for V1)");
            sb.AppendLine();
            foreach (var line in crush) sb.AppendLine("- " + line);
            sb.AppendLine();
            sb.AppendLine("## Screenshots (Previews/Attachments)");
            sb.AppendLine();
            foreach (var s in shots) sb.AppendLine("- " + s);
            sb.AppendLine();
            sb.AppendLine($"**{rows.Count - fails} PASS, {fails} FAIL of {rows.Count} item/body pairs.**");
            File.WriteAllText(ReportPath, sb.ToString());
            return fails;
        }

        static string Mark(bool ok) => ok ? "ok" : "FAIL";

        static Loadout Kit(string front, string rear, string roof, string key)
        {
            var l = new Loadout(key);
            l.Set(AttachmentSlot.Front, front); l.Set(AttachmentSlot.Rear, rear); l.Set(AttachmentSlot.Roof, roof);
            return l;
        }

        static Row Measure(VehicleRig.Rig rig, AttachmentCatalog catalog, string key, AttachmentDefinition item)
        {
            var fit = catalog.FitFor(key);
            var row = new Row { vehicle = key, item = item.id, slot = item.slot.ToString() };
            var mount = rig.root.AddComponent<AttachmentMount>();
            mount.Configure(catalog, key);
            var l = new Loadout(key); l.Set(item.slot, item.id);
            mount.Apply(l);
            Physics.SyncTransforms();
            int s = (int)item.slot;
            var slotNode = mount.Instance(item.slot).transform.parent;
            var socket = fit.sockets[s];

            // item bounds in socket space
            Bounds? local = null;
            foreach (var mf in mount.Instance(item.slot).GetComponentsInChildren<MeshFilter>())
                foreach (var corner in Corners(mf.sharedMesh.bounds))
                {
                    Vector3 p = slotNode.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (local == null) local = new Bounds(p, Vector3.zero); else { var bb = local.Value; bb.Encapsulate(p); local = bb; }
                }
            var lb = local.Value;
            var env = socket.envelopeSize;
            row.envelope = lb.size.x <= env.x + 1e-3f && lb.size.y <= env.y + 1e-3f && lb.max.z <= env.z + 1e-3f && lb.min.z >= -1e-3f;

            // penetration into the physics shell and the distance from the mount plane to it
            var tmp = new GameObject("fit-probe"); tmp.hideFlags = HideFlags.HideAndDontSave;
            var box = tmp.AddComponent<BoxCollider>();
            tmp.transform.SetPositionAndRotation(slotNode.TransformPoint(lb.center), slotNode.rotation);
            box.size = lb.size;
            Vector3 plane = slotNode.position;
            float maxPen = 0f, minGap = float.MaxValue;
            foreach (var bc in ShellBoxes(rig))
            {
                if (Physics.ComputePenetration(box, tmp.transform.position, tmp.transform.rotation, bc, bc.transform.position, bc.transform.rotation, out _, out float dist)) maxPen = Mathf.Max(maxPen, dist);
                Vector3 closest = Physics.ClosestPoint(plane, bc, bc.transform.position, bc.transform.rotation);
                minGap = Mathf.Min(minGap, Vector3.Distance(closest, plane));
            }
            Object.DestroyImmediate(tmp);
            row.penetration = maxPen; row.seatGap = minGap;

            // neighbouring slots' envelopes and the wheels, as car-space AABBs
            Bounds itemBounds = CarBounds(rig.root.transform, mount.Instance(item.slot));
            row.neighbours = true;
            for (int o = 0; o < VehicleAttachmentFit.SlotCount; o++)
            {
                if (o == s) continue;
                if (itemBounds.Intersects(EnvelopeBounds(fit.sockets[o]))) row.neighbours = false;
            }
            row.wheels = true;
            var wh = rig.geometry.wheels;
            float r = wh.size.y * 0.5f;
            foreach (var wc in rig.geometry.wheelCenters)
                if (itemBounds.Intersects(new Bounds(wc, new Vector3(0.3f, r * 2f, r * 2f)))) row.wheels = false;
            return row;
        }

        static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (int i = 0; i < 8; i++) yield return b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        }

        static Bounds CarBounds(Transform root, GameObject go)
        {
            Bounds? acc = null;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                foreach (var corner in Corners(mf.sharedMesh.bounds))
                {
                    Vector3 p = root.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (acc == null) acc = new Bounds(p, Vector3.zero); else { var bb = acc.Value; bb.Encapsulate(p); acc = bb; }
                }
            return acc.Value;
        }

        static Bounds EnvelopeBounds(AttachmentSocket s)
        {
            var q = Quaternion.Euler(s.localEulerAngles);
            var e = s.envelopeSize;
            Bounds? acc = null;
            foreach (var corner in Corners(new Bounds(new Vector3(0f, 0f, e.z * 0.5f), e)))
            {
                Vector3 p = s.localPosition + q * corner;
                if (acc == null) acc = new Bounds(p, Vector3.zero); else { var bb = acc.Value; bb.Encapsulate(p); acc = bb; }
            }
            var r = acc.Value;
            r.Expand(-0.01f);     // touching at an edge is not an overlap
            return r;
        }

        // ------------------------------------------------------------------ crush gap

        static string CrushGap(VehicleRig.Rig rig, AttachmentMount mount, string key)
        {
            var b = rig.geometry.body; var c = rig.geometry.cabin;
            float frontBefore = SurfaceExtent(rig, mount, new Vector2(b.center.x, b.min.y + b.size.y * 0.35f), 'z', +1);
            float roofBefore = SurfaceExtent(rig, mount, new Vector2(c.center.x, c.center.z), 'y', +1);
            var frontHit = new LocalImpact { point = new Vector3(b.center.x, b.min.y + b.size.y * 0.35f, b.max.z), direction = Vector3.back, normal = Vector3.forward };
            var roofHit = new LocalImpact { point = new Vector3(c.center.x, c.max.y, c.center.z), direction = Vector3.down, normal = Vector3.up };
            // a firm hit (strength 0.5, no severe-crush gain) first, then a maximum one (strength 1, depth gain 3) on top of it
            frontHit.strength = roofHit.strength = 0.5f; frontHit.depthScale = roofHit.depthScale = 1f;
            rig.model.AddImpact(frontHit); rig.model.AddImpact(roofHit);
            float frontMid = SurfaceExtent(rig, mount, new Vector2(b.center.x, b.min.y + b.size.y * 0.35f), 'z', +1);
            float roofMid = SurfaceExtent(rig, mount, new Vector2(c.center.x, c.center.z), 'y', +1);
            frontHit.strength = roofHit.strength = 1f; frontHit.depthScale = roofHit.depthScale = 3f;
            rig.model.AddImpact(frontHit); rig.model.AddImpact(roofHit);
            float frontAfter = SurfaceExtent(rig, mount, new Vector2(b.center.x, b.min.y + b.size.y * 0.35f), 'z', +1);
            float roofAfter = SurfaceExtent(rig, mount, new Vector2(c.center.x, c.center.z), 'y', +1);
            return $"{key}: firm hit (strength 0.5): front {(frontBefore - frontMid) * 1000f:0} mm, roof {(roofBefore - roofMid) * 1000f:0} mm; maximum (strength 1, depth gain 3): front {(frontBefore - frontAfter) * 1000f:0} mm, roof {(roofBefore - roofAfter) * 1000f:0} mm. The rigid item stays put, so these are the gaps behind it.";
        }

        /// <summary>Furthest car-space coordinate of the body's vertices inside a narrow lane around (u,v); axis 'z' scans the lane (x,y) for max z, axis 'y' the lane (x,z) for max y.</summary>
        static float SurfaceExtent(VehicleRig.Rig rig, AttachmentMount mount, Vector2 lane, char axis, int sign)
        {
            float best = float.MinValue;
            foreach (var mf in rig.root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.transform.IsChildOf(mount.Root)) continue;
                var mesh = mf.sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                foreach (var v in mesh.vertices)
                {
                    Vector3 p = rig.root.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (axis == 'z') { if (Mathf.Abs(p.x - lane.x) < 0.2f && Mathf.Abs(p.y - lane.y) < 0.2f) best = Mathf.Max(best, p.z); }
                    else if (Mathf.Abs(p.x - lane.x) < 0.2f && Mathf.Abs(p.z - lane.y) < 0.2f) best = Mathf.Max(best, p.y);
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ screenshots

        static List<string> Shoot(VehicleRig.Rig rig, string key, string kit, string[] views)
        {
            var written = new List<string>();
            var b = rig.geometry.body;
            Vector3 center = rig.root.transform.TransformPoint(b.center + Vector3.up * 0.1f);
            float dist = b.size.magnitude * 1.5f;
            var camGo = new GameObject("fit-cam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            const int w = 960, h = 540;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                foreach (var view in views)
                {
                    Vector3 dir = view == "front3q" ? new Vector3(0.65f, 0.4f, 1f) : view == "rear3q" ? new Vector3(-0.65f, 0.4f, -1f) : view == "side" ? new Vector3(1f, 0.15f, 0f) : new Vector3(0f, 1f, 0.12f);
                    camGo.transform.position = center + dir.normalized * dist;
                    camGo.transform.LookAt(center);
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string file = $"{key}_{kit}_{view}.png";
                    File.WriteAllBytes(Path.Combine(PreviewFolder, file), tex.EncodeToPNG());
                    written.Add(file);
                }
            }
            finally { Object.DestroyImmediate(camGo); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            return written;
        }

        // ------------------------------------------------------------------ scene wiring

        [MenuItem("CarWreck/Attachments/Wire Catalog Into DerbyArena", priority = 44)]
        public static void WireScene()
        {
            var scene = EditorSceneManager.OpenScene(DerbySceneBuilder.ScenePath, OpenSceneMode.Single);
            var game = Object.FindFirstObjectByType<DerbyGame>();
            var catalog = AssetDatabase.LoadAssetAtPath<AttachmentCatalog>(AttachmentAssetBuilder.CatalogPath);
            if (game == null || catalog == null) { Debug.LogError("[Attachments] DerbyGame or the catalog is missing."); return; }
            game.attachmentCatalog = catalog;
            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Attachments] Catalog wired into " + DerbySceneBuilder.ScenePath);
        }
    }
}
