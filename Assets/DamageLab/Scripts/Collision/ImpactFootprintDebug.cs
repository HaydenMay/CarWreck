using System.Collections.Generic;
using DamageLab.Deformation;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Debug view of the contact footprints of geometry-aware impacts: patch outline (on the crushed surface),
    /// principal axes through the centre, and an arrow along the incoming impact direction. Runtime lines
    /// (LineRenderers parented to the car) so it also works in the browser build; also drawn as editor gizmos.
    /// </summary>
    [ExecuteAlways]
    public class ImpactFootprintDebug : MonoBehaviour
    {
        public VehicleDamageModel model;
        public Material outlineMaterial;
        public Material arrowMaterial;
        public bool show = true;
        public float lineWidth = 0.025f;

        readonly List<GameObject> lines = new List<GameObject>();
        bool subscribed;

        void OnEnable() => Subscribe();
        void OnDisable()
        {
            if (model != null && subscribed) model.ImpactsChanged -= Rebuild;
            subscribed = false;
            Clear();
        }

        void Subscribe()
        {
            if (model == null || subscribed) return;
            model.ImpactsChanged += Rebuild;
            subscribed = true;
            Rebuild();
        }

        public void SetVisible(bool on)
        {
            show = on;
            Rebuild();
        }

        void Clear()
        {
            foreach (var go in lines)
                if (go != null) { if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            lines.Clear();
        }

        public void Rebuild()
        {
            Clear();
            if (!show || model == null) return;
            foreach (var im in model.Impacts)
            {
                if (!im.footprint.valid) continue;
                var fp = im.footprint;
                Vector3 d = im.direction.normalized;
                if (fp.outline != null && fp.outline.Length > 1) Line("FootprintOutline", outlineMaterial, true, fp.outline);
                Vector3 c = fp.center - d * 0.03f;
                Line("FootprintAxisU", outlineMaterial, false, c - fp.axisU * fp.halfU, c + fp.axisU * fp.halfU);
                Line("FootprintAxisV", outlineMaterial, false, c - fp.axisV * fp.halfV, c + fp.axisV * fp.halfV);
                // Incoming direction: shaft ending at the patch centre, with a head.
                Vector3 side = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 tip = fp.center - d * 0.05f;
                Line("ImpactDirection", arrowMaterial, false, tip - d * 1.2f, tip);
                Line("ImpactDirectionHead", arrowMaterial, false, tip - d * 0.25f + side * 0.12f, tip, tip - d * 0.25f - side * 0.12f);
            }
        }

        void Line(string name, Material mat, bool loop, params Vector3[] localPoints)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(model.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = localPoints.Length;
            lr.SetPositions(localPoints);
            lr.widthMultiplier = lineWidth;
            lr.numCornerVertices = 2;
            lr.sharedMaterial = mat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lines.Add(go);
        }

        void OnDrawGizmos()
        {
            if (model == null) return;
            Gizmos.matrix = model.transform.localToWorldMatrix;
            foreach (var im in model.Impacts)
            {
                if (!im.footprint.valid || im.footprint.outline == null) continue;
                var o = im.footprint.outline;
                Gizmos.color = Color.cyan;
                for (int i = 0; i < o.Length; i++) Gizmos.DrawLine(o[i], o[(i + 1) % o.Length]);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(im.footprint.center - im.direction.normalized * 1.2f, im.footprint.center);
            }
        }
    }
}
