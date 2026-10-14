using System;
using System.Collections.Generic;
using System.Diagnostics;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Damage-aware physical hitbox for the FRONT of the car (Collision Lab phase 3).
    ///
    /// The car's physics shape is a structural core box (rear to just ahead of the front axle), the cabin box, and
    /// three front sections - left, centre, right - that run from the core's front face to the bodywork's front.
    /// Each section's front face is derived from the CURRENT deformed mesh: the section's frontal area is split into
    /// a grid of cells, the front-most deformed vertex of each cell is found, and the section face is set to the
    /// median of those cell fronts. Undamaged, that follows the front contour; when metal is crushed back, the
    /// face retracts by about as much as most of the section's metal moved (protruding leftovers and single
    /// wrinkles don't hold it out). Nothing is stored apart from the mesh, so physics and visuals cannot disagree.
    ///
    /// Updates are deferred: an impact marks the hitbox pending, and <see cref="ApplyPending"/> is called at a safe
    /// point (by the lab, once the impactor has stopped pressing into the car, and always before the next launch).
    /// Sections only ever shrink between repairs, so a change never creates an overlap; a repair (which grows them
    /// back) is handled by the lab re-parking a block that would overlap.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public class FrontHitbox : MonoBehaviour
    {
        public VehicleDamageModel model;
        [Tooltip("Structural core behind the deformable front (the shortened body box).")]
        public BoxCollider core;
        [Tooltip("Front-left, front-centre, front-right (car-space -X to +X). Their back faces sit on the core's front face.")]
        public BoxCollider[] sections = new BoxCollider[3];
        [Tooltip("Grid of cells across each section's frontal area (columns x rows).")]
        public int columns = 6;
        public int rows = 4;
        [Tooltip("A section never gets thinner than this (m).")]
        public float minDepth = 0.06f;
        [Tooltip("Ignore face changes smaller than this (m).")]
        public float changeTolerance = 0.002f;

        public event Action Changed;
        public bool HasPending { get; private set; }
        public float LastUpdateMilliseconds { get; private set; }
        public int UpdateCount { get; private set; }
        public int SampleCount { get; private set; }
        /// <summary>Car-space Z of each section's front face: undamaged, and now.</summary>
        public readonly float[] OriginalFront = new float[3];
        public readonly float[] CurrentFront = new float[3];
        public float CoreFrontZ { get; private set; }

        struct Sample { public int part, index, cell; }
        List<Sample>[] samples;
        int partCount = -1;
        bool subscribed;
        readonly Stopwatch stopwatch = new Stopwatch();

        void OnEnable()
        {
            if (model != null && !subscribed) { model.ImpactsChanged += MarkPending; subscribed = true; }
        }

        void OnDisable()
        {
            if (model != null && subscribed) model.ImpactsChanged -= MarkPending;
            subscribed = false;
        }

        void MarkPending() => HasPending = true;

        bool Ready => samples != null && model != null && model.IsInitialized && model.Parts.Count == partCount;

        /// <summary>Find the vertices behind each section's frontal cells (original mesh, once) and fit the undamaged faces.</summary>
        public bool Setup()
        {
            OnEnable();   // (re)subscribe if the model was assigned after this component was enabled
            if (Ready) return true;
            if (model == null || !model.IsInitialized || sections == null || sections.Length != 3) return false;
            CoreFrontZ = Back(sections[0]);
            samples = new List<Sample>[3];
            SampleCount = 0;
            for (int s = 0; s < 3; s++)
            {
                samples[s] = new List<Sample>();
                Bounds b = CarBounds(sections[s]);
                for (int p = 0; p < model.Parts.Count; p++)
                {
                    var pos = model.Parts[p].VehiclePositions;
                    for (int i = 0; i < pos.Length; i++)
                    {
                        Vector3 v = pos[i];
                        if (v.z < CoreFrontZ - 0.02f || v.x < b.min.x || v.x >= b.max.x || v.y < b.min.y || v.y > b.max.y) continue;
                        int col = Mathf.Clamp((int)((v.x - b.min.x) / b.size.x * columns), 0, columns - 1);
                        int row = Mathf.Clamp((int)((v.y - b.min.y) / b.size.y * rows), 0, rows - 1);
                        samples[s].Add(new Sample { part = p, index = i, cell = row * columns + col });
                    }
                }
                SampleCount += samples[s].Count;
            }
            partCount = model.Parts.Count;
            var f = Measure();
            for (int s = 0; s < 3; s++) { OriginalFront[s] = f[s]; CurrentFront[s] = float.NaN; }
            HasPending = true;
            ApplyPending();
            return true;
        }

        /// <summary>Fit the section faces to the current mesh and resize the colliders that changed. Call at a safe point.</summary>
        public void ApplyPending()
        {
            if (!Setup()) { HasPending = false; return; }
            if (!HasPending) return;
            stopwatch.Restart();
            var f = Measure();
            // Undamaged (after Repair / Reset All): exactly the original fit, so repeated repairs can never drift.
            if (model.ImpactCount == 0) for (int s = 0; s < 3; s++) f[s] = OriginalFront[s];
            bool changed = false;
            for (int s = 0; s < 3; s++)
            {
                if (!float.IsNaN(CurrentFront[s]) && (f[s] == CurrentFront[s] || (Mathf.Abs(f[s] - CurrentFront[s]) < changeTolerance && model.ImpactCount > 0))) continue;
                SetFront(sections[s], f[s]);
                CurrentFront[s] = f[s];
                changed = true;
            }
            stopwatch.Stop();
            LastUpdateMilliseconds = (float)stopwatch.Elapsed.TotalMilliseconds;
            UpdateCount++;
            HasPending = false;
            if (changed) Changed?.Invoke();
        }

        /// <summary>Median over each section's cells of the front-most current (deformed) vertex, car-space Z.</summary>
        float[] Measure()
        {
            var result = new float[3];
            var cells = new float[columns * rows];
            var fronts = new List<float>(cells.Length);
            for (int s = 0; s < 3; s++)
            {
                for (int c = 0; c < cells.Length; c++) cells[c] = float.MinValue;
                foreach (var smp in samples[s])
                {
                    float z = model.Parts[smp.part].DeformedVehiclePosition(smp.index).z;
                    if (z > cells[smp.cell]) cells[smp.cell] = z;
                }
                fronts.Clear();
                foreach (var z in cells) if (z > float.MinValue) fronts.Add(z);
                fronts.Sort();
                float median = fronts.Count == 0 ? CoreFrontZ + minDepth
                    : fronts.Count % 2 == 1 ? fronts[fronts.Count / 2] : 0.5f * (fronts[fronts.Count / 2 - 1] + fronts[fronts.Count / 2]);
                result[s] = Mathf.Max(median, CoreFrontZ + minDepth);
            }
            return result;
        }

        // The collider objects sit at the car root's origin with no rotation or scale (built that way by the scene builder),
        // so collider-local Z equals car-space Z.
        Bounds CarBounds(BoxCollider bc) => new Bounds(bc.center, bc.size);
        static float Back(BoxCollider bc) => bc.center.z - bc.size.z * 0.5f;

        static void SetFront(BoxCollider bc, float frontZ)
        {
            float back = Back(bc);
            var size = bc.size; var c = bc.center;
            size.z = frontZ - back;
            c.z = back + size.z * 0.5f;
            bc.size = size;
            bc.center = c;
        }
    }
}
