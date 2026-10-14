using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Damage-aware physical hitbox for the whole car (Collision Lab phase 4; phase 3 did the front only).
    ///
    /// Layout (built by the scene builder, all boxes at the car root's origin so collider-local = car space):
    ///   core      structural box inside the shell (fixed)
    ///   cabin     the cabin box (fixed)
    ///   front L/C/R and rear L/C/R   full-width thirds of the nose and tail; their end face adapts, and the corner
    ///             sections (L, R) also adapt their outer side face, so a corner can be crushed from the end or the side
    ///   left F/M/R and right F/M/R   the side shell between the nose and tail sections; their outer side face adapts
    /// The pieces tile the body exactly (shared faces, no overlaps, no gaps).
    ///
    /// Every adaptive face is fitted to the CURRENT deformed mesh: the face's area is split into a grid of cells, the
    /// outermost deformed vertex of each cell is found, and the face goes to the median of those cell values. Undamaged
    /// that follows the body contour; crushed, it retracts by about as much as most of that region's metal moved. Faces only
    /// ever move inward between repairs (never past the undamaged fit, never outward again), never closer than
    /// <see cref="minDepth"/> to the region's inner face, and only change at a safe point (<see cref="ApplyPending"/>).
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class VehicleHitbox : MonoBehaviour
    {
        [Serializable]
        public class Region
        {
            public string name;
            public BoxCollider box;
            [Tooltip("Which outer faces of this box follow the bodywork: +Z (front), -Z (rear), -X (left), +X (right).")]
            public bool posZ, negZ, negX, posX;
            [Tooltip("The region's undamaged outer box (car space), set by the scene builder. Faces are sampled and fixed faces kept from it.")]
            public Bounds outer;
            public Bounds Outer => outer.size.sqrMagnitude > 0f ? outer : new Bounds(box.center, box.size);
        }

        public VehicleDamageModel model;
        public BoxCollider core;
        public BoxCollider cabin;
        public Region[] regions = new Region[0];
        [Tooltip("Grid of cells across each adaptive face (columns along the face's horizontal direction x rows in height).")]
        public int columns = 6;
        public int rows = 4;
        [Tooltip("A region never gets thinner than this (m).")]
        public float minDepth = 0.06f;
        [Tooltip("Ignore face changes smaller than this (m).")]
        public float changeTolerance = 0.002f;

        public event Action Changed;
        public bool HasPending { get; private set; }
        public float LastUpdateMilliseconds { get; private set; }
        public int UpdateCount { get; private set; }
        public int SampleCount { get; private set; }
        /// <summary>Faces re-fitted in the last update (the others were too far from the new impacts to have changed).</summary>
        public int LastRefitFaces { get; private set; }
        public int FaceCount => faces?.Count ?? 0;
        /// <summary>Regions whose faces moved in the last update, e.g. "FrontLeft +Z -12 cm, LeftFront -X -8 cm".</summary>
        public string LastChangeSummary { get; private set; } = "";
        /// <summary>True if any update ever tried to move a face outward (it is clamped; for the validation).</summary>
        public int OutwardClamps { get; private set; }

        // Phase 3 compatibility: front sections and their +Z faces (car-space Z).
        public readonly float[] OriginalFront = new float[3];
        public readonly float[] CurrentFront = new float[3];
        public float CoreFrontZ { get; private set; }

        public class Face
        {
            public Region region;
            public int axis;        // 0 = X, 2 = Z
            public float sign;      // +1 outer face is the max side of the box, -1 the min side
            public float original;  // undamaged fit (outward coordinate = sign * car-space coordinate)
            public float current;
            public float inner;     // outward coordinate of the opposite (fixed) face
            public List<Vector2Int> samples;   // (part, vertex) packed: x = part, y = vertex index
            public List<int> cells;
            public string Label => $"{region.name} {(axis == 0 ? (sign > 0 ? "+X" : "-X") : (sign > 0 ? "+Z" : "-Z"))}";
        }

        List<Face> faces;
        int partCount = -1;
        bool subscribed;
        readonly Stopwatch stopwatch = new Stopwatch();

        public IReadOnlyList<Face> Faces => faces;

        void OnEnable()
        {
            if (model != null && !subscribed) { model.ImpactsChanged += MarkPending; subscribed = true; }
        }

        void OnDisable()
        {
            if (model != null && subscribed) model.ImpactsChanged -= MarkPending;
            subscribed = false;
        }

        [Tooltip("Only faces within this distance (m, plus the footprint size) of a new impact are re-fitted; repairs re-fit all.")]
        public float refitReach = 1.2f;

        bool pendingAll = true;
        readonly List<Vector4> pendingSpots = new List<Vector4>();   // xyz = impact point (car space), w = reach

        void MarkPending()
        {
            HasPending = true;
            if (model == null || model.TotalImpactCount == 0 || model.ImpactCount == 0) { pendingAll = true; return; }
            var im = model.Impacts[model.ImpactCount - 1];
            float size = im.footprint.valid ? Mathf.Max(im.footprint.halfU, im.footprint.halfV) : 0.6f;
            pendingSpots.Add(new Vector4(im.point.x, im.point.y, im.point.z, size + refitReach));
        }

        bool Ready => faces != null && model != null && model.IsInitialized && model.Parts.Count == partCount;

        /// <summary>Region by name (null if absent).</summary>
        public Region Find(string name)
        {
            foreach (var r in regions) if (r.name == name) return r;
            return null;
        }

        /// <summary>Face of a region along an axis/sign ("FrontLeft", 2, +1 = its front face).</summary>
        public Face FaceOf(string region, int axis, float sign)
        {
            if (faces == null) return null;
            foreach (var f in faces) if (f.region.name == region && f.axis == axis && f.sign == sign) return f;
            return null;
        }

        /// <summary>Collect the mesh vertices behind every adaptive face (original mesh, once) and fit the undamaged faces.</summary>
        public bool Setup()
        {
            OnEnable();   // (re)subscribe if the model was assigned after this component was enabled
            if (Ready) return true;
            if (model == null || !model.IsInitialized || regions == null) return false;
            faces = new List<Face>();
            SampleCount = 0;
            foreach (var r in regions)
            {
                if (r.box == null) continue;
                if (r.posZ) AddFace(r, 2, +1f);
                if (r.negZ) AddFace(r, 2, -1f);
                if (r.negX) AddFace(r, 0, -1f);
                if (r.posX) AddFace(r, 0, +1f);
            }
            partCount = model.Parts.Count;
            var m = Measure();
            for (int i = 0; i < faces.Count; i++) { faces[i].original = m[i]; faces[i].current = float.NaN; }
            HasPending = true;
            ApplyPending();
            return true;
        }

        void AddFace(Region r, int axis, float sign)
        {
            var f = new Face { region = r, axis = axis, sign = sign, samples = new List<Vector2Int>(), cells = new List<int>() };
            Bounds b = r.Outer;
            f.inner = sign * (sign > 0 ? b.min[axis] : b.max[axis]);
            int uAxis = axis == 2 ? 0 : 2;                       // horizontal direction across the face
            for (int p = 0; p < model.Parts.Count; p++)
            {
                var pos = model.Parts[p].VehiclePositions;
                for (int i = 0; i < pos.Length; i++)
                {
                    Vector3 v = pos[i];
                    if (v.x < b.min.x - 0.02f || v.x > b.max.x + 0.02f || v.y < b.min.y || v.y > b.max.y || v.z < b.min.z - 0.02f || v.z > b.max.z + 0.02f) continue;
                    // horizontal extent across the face is exact (so neighbouring regions don't share cells)
                    if (v[uAxis] < b.min[uAxis] || v[uAxis] >= b.max[uAxis]) continue;
                    int col = Mathf.Clamp((int)((v[uAxis] - b.min[uAxis]) / b.size[uAxis] * columns), 0, columns - 1);
                    int row = Mathf.Clamp((int)((v.y - b.min.y) / b.size.y * rows), 0, rows - 1);
                    f.samples.Add(new Vector2Int(p, i));
                    f.cells.Add(row * columns + col);
                }
            }
            SampleCount += f.samples.Count;
            faces.Add(f);
        }

        /// <summary>Fit the faces to the current mesh and resize the colliders that changed. Call at a safe point.</summary>
        public void ApplyPending()
        {
            if (!Setup()) { HasPending = false; return; }
            if (!HasPending) return;
            stopwatch.Restart();
            bool undamaged = model.TotalImpactCount == 0;
            // Re-fit only the faces near the impacts since the last update (all of them after a repair / at setup).
            var selected = new bool[faces.Count];
            int refit = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                bool sel = pendingAll || undamaged || float.IsNaN(faces[i].current);
                Bounds ob = faces[i].region.Outer;
                for (int k = 0; !sel && k < pendingSpots.Count; k++)
                    sel = ob.SqrDistance(pendingSpots[k]) <= pendingSpots[k].w * pendingSpots[k].w;
                selected[i] = sel;
                if (sel) refit++;
            }
            pendingAll = false;
            pendingSpots.Clear();
            LastRefitFaces = refit;
            var m = Measure(selected);

            var moves = new List<(float d, string s)>();
            var touched = new HashSet<Region>();
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                if (!selected[i]) continue;
                float target;
                if (undamaged) target = f.original;                          // repair: exactly the undamaged fit
                else
                {
                    target = Mathf.Min(m[i], f.original);                     // never outward of the undamaged body
                    if (!float.IsNaN(f.current) && target > f.current) { target = f.current; OutwardClamps++; }  // never grows back
                    target = Mathf.Max(target, f.inner + minDepth);
                }
                if (!float.IsNaN(f.current) && (target == f.current || (Mathf.Abs(target - f.current) < changeTolerance && !undamaged))) continue;
                if (!float.IsNaN(f.current) && Mathf.Abs(target - f.current) >= 0.01f)
                    moves.Add((target - f.current, $"{f.Label} {(target - f.current) * 100f:+0;-0} cm"));
                f.current = target;
                touched.Add(f.region);
            }
            foreach (var r in touched) ApplyBox(r);
            if (undamaged && touched.Count > 0)
            {
                // PhysX re-creates a resized shape at the end of the body's shape list, so a repair that only touched some
                // boxes would leave a different shape order (and a slightly different solver order) than the original car.
                // Re-insert every region box in a fixed order so each repaired car starts from the same physics layout.
                foreach (var r in regions) { if (r.box == null) continue; r.box.enabled = false; r.box.enabled = true; }
            }
            for (int s = 0; s < 3; s++)
            {
                var f = FaceOf(FrontNames[s], 2, +1f);
                if (f != null) { OriginalFront[s] = f.original; CurrentFront[s] = f.current; }
            }
            var fl = Find(FrontNames[0]);
            if (fl != null) CoreFrontZ = fl.Outer.min.z;
            stopwatch.Stop();
            LastUpdateMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
            UpdateCount++;
            HasPending = false;
            if (undamaged) LastChangeSummary = touched.Count > 0 ? "restored to undamaged" : LastChangeSummary;
            else if (moves.Count > 0)
            {
                moves.Sort((a, b) => a.d.CompareTo(b.d));   // biggest retraction first
                var sb = new StringBuilder();
                for (int i = 0; i < Mathf.Min(4, moves.Count); i++) sb.Append(i > 0 ? ", " : "").Append(moves[i].s);
                if (moves.Count > 4) sb.Append($" (+{moves.Count - 4} more)");
                LastChangeSummary = sb.ToString();
            }
            if (touched.Count > 0) Changed?.Invoke();
        }

        static readonly string[] FrontNames = { "FrontLeft", "FrontCentre", "FrontRight" };

        /// <summary>Set a region's box from its faces' current values (fixed faces keep their builder-time position).</summary>
        void ApplyBox(Region r)
        {
            Vector3 min = r.Outer.min, max = r.Outer.max;
            foreach (var f in faces)
            {
                if (f.region != r) continue;
                if (f.sign > 0) max[f.axis] = f.current; else min[f.axis] = -f.current;
            }
            r.box.center = (min + max) * 0.5f;
            r.box.size = max - min;
        }

        /// <summary>For each face, the median over its cells of the outermost CURRENT (deformed) vertex, in outward coordinates.</summary>
        float[] Measure(bool[] selected = null)
        {
            var result = new float[faces.Count];
            var cellMax = new float[columns * rows];
            var values = new List<float>(cellMax.Length);
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                if (selected != null && !selected[i]) { result[i] = float.NaN; continue; }
                for (int c = 0; c < cellMax.Length; c++) cellMax[c] = float.MinValue;
                for (int k = 0; k < f.samples.Count; k++)
                {
                    var s = f.samples[k];
                    float v = f.sign * model.Parts[s.x].DeformedVehiclePosition(s.y)[f.axis];
                    int c = f.cells[k];
                    if (v > cellMax[c]) cellMax[c] = v;
                }
                values.Clear();
                foreach (var v in cellMax) if (v > float.MinValue) values.Add(v);
                values.Sort();
                result[i] = values.Count == 0 ? f.inner + minDepth
                    : values.Count % 2 == 1 ? values[values.Count / 2] : 0.5f * (values[values.Count / 2 - 1] + values[values.Count / 2]);
            }
            return result;
        }
    }
}
