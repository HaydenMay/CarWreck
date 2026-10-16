// Executes the REAL DeformablePart / ImpactField / CrushField code (compiled from Assets/) on a synthetic subdivided car body,
// through a UnityEngine shim. The vehicle-level clock below mirrors VehicleDamageModel.StartCrush / AdvanceCrush.
using System;
using System.Collections.Generic;
using System.Linq;
using DamageLab.Deformation;
using UnityEngine;

static class Program
{
    static int fails;
    static void Check(string name, bool ok, string detail) { Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}: {detail}"); if (!ok) fails++; }

    static VehicleDamageProfile profile;
    static CrushField[] fields;
    static RegionState[] states = new RegionState[4];
    static readonly ProgressiveCrushSettings settings = new ProgressiveCrushSettings { enabled = true, stagger = float.Parse(Environment.GetEnvironmentVariable("STAGGER") ?? "0.3") };   // arena defaults: 0.06 / 0.24 s, 0.6 m, stagger 0.35

    static Mesh BoxMesh(Vector3 min, Vector3 max, int n)
    {
        var v = new List<Vector3>(); var t = new List<int>(); var nr = new List<Vector3>();
        void Face(Vector3 o, Vector3 u, Vector3 w, Vector3 normal)
        {
            int b = v.Count;
            for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) { v.Add(o + u * (i / (float)n) + w * (j / (float)n)); nr.Add(normal); }
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                int a = b + j * (n + 1) + i;
                t.AddRange(new[] { a, a + n + 1, a + 1, a + 1, a + n + 1, a + n + 2 });
            }
        }
        Vector3 s = max - min;
        Face(new Vector3(min.x, min.y, max.z), new Vector3(s.x, 0, 0), new Vector3(0, s.y, 0), Vector3.forward);   // front
        Face(new Vector3(max.x, min.y, min.z), new Vector3(-s.x, 0, 0), new Vector3(0, s.y, 0), Vector3.back);   // rear
        Face(new Vector3(min.x, min.y, min.z), new Vector3(0, 0, s.z), new Vector3(0, s.y, 0), Vector3.left);    // left
        Face(new Vector3(max.x, min.y, max.z), new Vector3(0, 0, -s.z), new Vector3(0, s.y, 0), Vector3.right);  // right
        Face(new Vector3(min.x, max.y, max.z), new Vector3(s.x, 0, 0), new Vector3(0, 0, -s.z), Vector3.up);     // top
        Face(new Vector3(min.x, min.y, min.z), new Vector3(s.x, 0, 0), new Vector3(0, 0, s.z), Vector3.down);    // bottom
        var m = new Mesh { name = "body", vertices = v.ToArray(), triangles = t.ToArray(), normals = nr.ToArray() };
        m.RecalculateBounds();
        return m;
    }

    class Car
    {
        public DeformablePart part; public ImpactField field; public List<DeformablePart> crushing = new List<DeformablePart>();
        public float elapsed, duration, invDepth, travel; public Vector3 point, dir; public bool progressive; public int impacts;
        public bool IsCrushing => crushing.Count > 0;
        public float Progress => IsCrushing ? Mathf.Clamp01(elapsed / duration) : 1f;
        public Vector3[] Shown => part.RuntimeMesh.vertices;
        public Vector3[] ShownNormals => part.RuntimeMesh.normals;

        public void AddImpact(LocalImpact im)
        {
            int index = field.Add(im);
            part.AccumulateImpact(field, index);
            impacts++;
            bool rebuilt = part.Apply(fields, states, profile.roofLineStart, profile.roofLineEnd, field, true, progressive);
            // mirror of VehicleDamageModel.StartCrush (single part)
            if (!progressive) { crushing.RemoveAll(p => !p.Ramping); return; }
            var rebuiltNow = rebuilt ? new List<DeformablePart> { part } : new List<DeformablePart>();
            foreach (var p in crushing) if (!rebuiltNow.Contains(p)) p.RestartRamp();
            crushing.RemoveAll(p => !p.Ramping);
            foreach (var p in rebuiltNow) if (p.Ramping && !crushing.Contains(p)) crushing.Add(p);
            if (crushing.Count == 0) return;
            travel = crushing.Max(p => p.RampTravel);
            duration = settings.DurationFor(travel); elapsed = 0f; invDepth = 1f / Mathf.Max(0.15f, travel);
            point = im.point; dir = im.direction.normalized;
        }
        public void Advance(float dt)
        {
            if (crushing.Count == 0) return;
            elapsed += Mathf.Max(0f, dt);
            if (elapsed >= duration) { foreach (var p in crushing) p.FinishRamp(); crushing.Clear(); }
            else foreach (var p in crushing) p.ShowRamp(elapsed / duration, point, dir, invDepth, settings.stagger);
        }
        public int TickToEnd(float dt) { int k = 0; while (IsCrushing && k < 10000) { Advance(dt); k++; } return k; }
        public void TickTo(float fraction, float dt) { float target = fraction * duration, t = 0f; while (IsCrushing && t + dt <= target + 1e-6f) { Advance(dt); t += dt; } }
        public void Clear() { field.Clear(); part.ClearImpacts(); part.Apply(fields, states, profile.roofLineStart, profile.roofLineEnd, field, true, false); crushing.RemoveAll(p => !p.Ramping); }
    }

    static Mesh source;
    static Car NewCar(bool progressive)
    {
        var go = new GameObject("car"); var child = new GameObject("body"); child.transform.SetParent(go.transform, false);
        var mf = child.AddComponent<MeshFilter>(); mf.sharedMesh = source;
        var part = new DeformablePart(mf, go.transform, 1f);
        part.Precompute(fields);
        var f = new ImpactField(profile.impact, 1.8f);
        f.SetStructure(new[] { new Vector3(-0.8f, 0.35f, 1.4f), new Vector3(0.8f, 0.35f, 1.4f), new Vector3(-0.8f, 0.35f, -1.4f), new Vector3(0.8f, 0.35f, -1.4f) }, profile.roofLineStart);
        return new Car { part = part, field = f, progressive = progressive };
    }

    static float MaxDiff(Vector3[] a, Vector3[] b) { float d = 0f; for (int i = 0; i < a.Length; i++) d = Math.Max(d, (a[i] - b[i]).magnitude); return d; }

    static float Progress(Vector3[] before, Vector3[] final, Vector3[] frame)
    {
        double num = 0, den = 0;
        for (int i = 0; i < before.Length; i++) { Vector3 p = final[i] - before[i]; float l = p.magnitude; if (l < 1e-3f) continue; num += Vector3.Dot(frame[i] - before[i], p) / l; den += l; }
        return den > 0 ? (float)(num / den) : 1f;
    }

    static int Flipped(int[] tris, Vector3[] a, Vector3[] reference)
    {   // triangles whose facing reversed relative to the undamaged mesh
        int f = 0;
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 n0 = Vector3.Cross(reference[tris[t + 1]] - reference[tris[t]], reference[tris[t + 2]] - reference[tris[t]]);
            Vector3 n1 = Vector3.Cross(a[tris[t + 1]] - a[tris[t]], a[tris[t + 2]] - a[tris[t]]);
            if (Vector3.Dot(n0, n1) < 0f) f++;
        }
        return f;
    }

    static LocalImpact Hit(Vector3 p, Vector3 d, float strength, float depthScale = 1f) => new LocalImpact { point = p, direction = d.normalized, normal = -d.normalized, strength = strength, depthScale = depthScale };

    static void Main()
    {
        profile = ScriptableObject.CreateInstance<VehicleDamageProfile>(); profile.ResetToDefaults();
        source = BoxMesh(new Vector3(-0.85f, 0.3f, -2.2f), new Vector3(0.85f, 1.3f, 2.2f), 28);
        var faceBounds = source.bounds;
        fields = new CrushField[4];
        for (int d = 0; d < 4; d++) { var r = profile.Get((DamageDirection)d); r.direction = (DamageDirection)d; fields[d] = new CrushField(r, faceBounds); }
        { var a = new Vector3[74000]; var b = new Vector3[74000]; var w = System.Diagnostics.Stopwatch.StartNew(); for (int k = 0; k < 100; k++) { a.CopyTo(b, 0); b.CopyTo(a, 0); } Console.WriteLine($"  INFO  shim upload cost (2 x 74k Vector3 copies): {w.Elapsed.TotalMilliseconds / 100:0.00} ms"); }
        Console.WriteLine($"[ProgressiveCrushSim] stagger {settings.stagger:0.00}; real DeformablePart/ImpactField code on a {source.vertices.Length}-vertex subdivided body, Mono JIT; dt = 1/50 s unless noted");

        var front = Hit(new Vector3(0f, 0.75f, 2.2f), Vector3.back, 0.95f, 2.6f);
        var light = Hit(new Vector3(0f, 0.75f, 2.2f), Vector3.back, 0.12f);
        var moderate = Hit(new Vector3(0f, 0.75f, 2.2f), Vector3.back, 0.6f);
        var offset = Hit(new Vector3(0.6f, 0.75f, 2.2f), Vector3.back, 0.8f, 1.5f);
        var rear = Hit(new Vector3(0f, 0.75f, -2.2f), Vector3.forward, 0.7f);
        var side = Hit(new Vector3(0.85f, 0.75f, 0.3f), Vector3.left, 0.7f);
        const float dt = 0.02f;

        // ---- single impacts: progressive vs instant
        foreach (var (label, im) in new[] { ("light frontal", light), ("moderate frontal", moderate), ("severe frontal", front), ("offset frontal", offset), ("side", side), ("rear", rear) })
        {
            var inst = NewCar(false); inst.AddImpact(im);
            var prog = NewCar(true);
            var before = prog.Shown; int uploads0 = prog.part.RuntimeMesh.uploads;
            prog.AddImpact(im);
            bool noUploadAtHit = prog.part.RuntimeMesh.uploads == uploads0 && MaxDiff(prog.Shown, before) == 0f;
            var final = inst.Shown;
            var frames = new List<Vector3[]>();
            while (prog.IsCrushing) { prog.Advance(dt); frames.Add(prog.Shown); }
            var prog1 = frames.Select(f => Progress(before, final, f)).ToList();
            int back = 0, maxFlip = 0;
            int flipsFinal = Flipped(source.triangles, final, source.vertices);
            var prev = before;
            foreach (var f in frames)
            {
                for (int i = 0; i < f.Length; i++) { Vector3 p = final[i] - before[i]; float l = p.magnitude; if (l > 1e-3f && Vector3.Dot(f[i] - prev[i], p / l) < -1e-4f) back++; }
                maxFlip = Math.Max(maxFlip, Flipped(source.triangles, f, source.vertices));
                prev = f;
            }
            // front-first at frame 1: vertices within the front third of the crush depth vs the back third
            float depth = Mathf.Max(0.15f, prog.travel);
            Vector3[] vp = prog.part.VehiclePositions;
            float Band(float d0, float d1)
            {
                double num = 0, den = 0;
                for (int i = 0; i < vp.Length; i++)
                {
                    float dd = Vector3.Dot(vp[i] - im.point, im.direction.normalized); if (dd < d0 || dd >= d1) continue;
                    Vector3 p = final[i] - before[i]; float l = p.magnitude; if (l < 1e-3f) continue;
                    num += Vector3.Dot(frames[0][i] - before[i], p) / l; den += l;
                }
                return den > 0 ? (float)(num / den) : float.NaN;
            }
            float fr = Band(float.NegativeInfinity, depth / 3f), bk = Band(2f * depth / 3f, float.PositiveInfinity);
            float diff = MaxDiff(prog.Shown, final), nd = MaxDiff(prog.ShownNormals, inst.ShownNormals);
            bool ok = noUploadAtHit && diff == 0f && nd == 0f && back == 0 && frames.Count >= 2 && prog1[0] > 0.02f && prog1[0] < 0.95f && (float.IsNaN(bk) || fr >= bk) && maxFlip <= flipsFinal;
            Check($"{label}", ok, $"travel {prog.travel:0.000} m -> {prog.duration * 1000f:0} ms, {frames.Count} frames [{string.Join(" ", prog1.Select(p => (p * 100f).ToString("0") + "%"))}], " +
                  $"front/back third at frame 1 {fr * 100f:0}%/{bk * 100f:0}%, backward moves {back}, flipped tris max {maxFlip} (final {flipsFinal}), final vs instant {diff:0.0e0} m / normals {nd:0.0e0}, no upload at the hit {noUploadAtHit}");
        }

        // ---- repeated impacts during a crush
        void Sequence(string label, (LocalImpact im, float at)[] hits, int expectRestarts = -1)
        {
            if (expectRestarts < 0) expectRestarts = hits.Length - 1;
            var prog = NewCar(true); float worstJumpAtHit = 0f, worstFirst = 0f; int over = 0, restarts = 0, maxFlip = 0;
            for (int h = 0; h < hits.Length; h++)
            {
                if (h > 0) prog.TickTo(hits[h].at, dt);
                var b = prog.Shown; bool was = prog.IsCrushing;
                prog.AddImpact(hits[h].im); if (was) restarts++;
                worstJumpAtHit = Math.Max(worstJumpAtHit, MaxDiff(b, prog.Shown));
                prog.Advance(dt);
                float allowed = prog.IsCrushing ? prog.travel * ProgressiveCrushSettings.Ease(dt / prog.duration, 0f) + 1e-4f : float.PositiveInfinity;
                float step = MaxDiff(b, prog.Shown); worstFirst = Math.Max(worstFirst, step); if (step > allowed) over++;
                maxFlip = Math.Max(maxFlip, Flipped(source.triangles, prog.Shown, source.vertices));
            }
            while (prog.IsCrushing) { prog.Advance(dt); maxFlip = Math.Max(maxFlip, Flipped(source.triangles, prog.Shown, source.vertices)); }
            var inst = NewCar(false); foreach (var x in hits) inst.AddImpact(x.im);
            float diff = MaxDiff(prog.Shown, inst.Shown), nd = MaxDiff(prog.ShownNormals, inst.ShownNormals);
            int flipsFinal = Flipped(source.triangles, inst.Shown, source.vertices);
            bool ok = diff == 0f && nd == 0f && worstJumpAtHit == 0f && over == 0 && restarts == expectRestarts && maxFlip <= flipsFinal;
            Check($"repeated: {label}", ok, $"{restarts} hits during a running crush; jump at a hit {worstJumpAtHit:0.0e0}; first-frame step {worstFirst:0.000} m (over limit {over}); flipped tris max {maxFlip} (final {flipsFinal}); final vs instant {diff:0.0e0} / normals {nd:0.0e0}");
        }
        Sequence("same region at 40%", new[] { (front, 0f), (front, 0.4f) });
        Sequence("rear at 30% of a frontal crush", new[] { (front, 0f), (rear, 0.3f) });
        Sequence("side, front at 20%, rear at 20%", new[] { (side, 0f), (front, 0.2f), (rear, 0.2f) });
        Sequence("five rapid hits at 10%", new[] { (front, 0f), (offset, 0.1f), (front, 0.1f), (side, 0.1f), (rear, 0.1f) });
        Sequence("hit after the crush finished", new[] { (front, 0f), (rear, 1.0f) }, 0);

        // ---- frame rate
        {
            var inst = NewCar(false); inst.AddImpact(front);
            var rows = new List<string>(); bool ok = true;
            foreach (float fps in new[] { 20f, 30f, 50f, 60f, 144f })
            {
                var c = NewCar(true); c.AddImpact(front);
                int ticks = c.TickToEnd(1f / fps); float done = ticks / fps;
                bool r = MaxDiff(c.Shown, inst.Shown) == 0f && done >= c.duration - 1e-4f && done < c.duration + 1f / fps + 1e-4f;
                ok &= r; rows.Add($"{fps:0} fps {ticks} frames = {done * 1000f:0} ms");
            }
            Check("frame-rate independence", ok, $"crush {NewCarDuration(front):0} ms: " + string.Join(", ", rows));
        }

        // ---- clear (reset) during a crush
        {
            var c = NewCar(true); c.AddImpact(front); c.TickTo(0.3f, dt);
            c.Clear();
            bool restored = MaxDiff(c.Shown, source.vertices) == 0f && !c.part.Ramping && !c.IsCrushing;
            c.AddImpact(rear); c.TickToEnd(dt);
            var inst = NewCar(false); inst.AddImpact(rear);
            float diff = MaxDiff(c.Shown, inst.Shown);
            Check("reset during a crush, then a new hit", restored && diff == 0f, $"restored exactly {restored}; next hit vs instant {diff:0.0e0}");
        }

        // ---- cost of one crush frame (indicative: Mono desktop JIT + shim mesh copies, not Unity / WebGL)
        {
            var big = BoxMesh(new Vector3(-0.85f, 0.3f, -2.2f), new Vector3(0.85f, 1.3f, 2.2f), 110);   // ~73k vertices
            var keep = source; source = big;
            var c = NewCar(true); c.AddImpact(front);
            var sw = System.Diagnostics.Stopwatch.StartNew(); int frames = 0; double loop = 0;
            c.part.ShowRamp(0.1f, c.point, c.dir, c.invDepth, settings.stagger);   // JIT warm-up
            while (c.elapsed + 1f / 60f < c.duration) { sw.Restart(); c.part.ShowRamp((c.elapsed += 1f / 60f) / c.duration, c.point, c.dir, c.invDepth, settings.stagger); loop += sw.Elapsed.TotalMilliseconds; frames++; }
            Console.WriteLine($"  INFO  one crush frame, {big.vertices.Length} vertices, {c.part.RampVertexCount} moving: {loop / frames:0.00} ms mean over {frames} frames (Mono JIT on this container; includes the shim's array copies for SetVertices/SetNormals; NOT a Unity or WebGL number)");
            source = keep;
        }
        Console.WriteLine(fails == 0 ? "[ProgressiveCrushSim] ALL CHECKS PASSED" : $"[ProgressiveCrushSim] {fails} CHECK(S) FAILED");
    }

    static float NewCarDuration(LocalImpact im) { var c = NewCar(true); c.AddImpact(im); return c.duration * 1000f; }
}
