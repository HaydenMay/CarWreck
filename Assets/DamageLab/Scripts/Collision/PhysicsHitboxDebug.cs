using System.Collections.Generic;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>
    /// Green wireframe of the car's CURRENT physics colliders (every non-trigger BoxCollider under the car), as
    /// runtime lines so it also shows in the browser build. Rebuilt only when the hitbox changes.
    /// </summary>
    [ExecuteAlways]
    public class PhysicsHitboxDebug : MonoBehaviour
    {
        public Transform car;
        public FrontHitbox hitbox;
        public Material lineMaterial;
        public bool show;
        public float lineWidth = 0.018f;

        // Corner order (bit0 = +x, bit1 = +y, bit2 = +z) tracing all 12 edges in one strip (a few edges twice).
        static readonly int[] Path = { 0, 1, 3, 2, 0, 4, 5, 7, 6, 4, 6, 2, 3, 7, 5, 1 };

        readonly List<GameObject> lines = new List<GameObject>();
        bool subscribed;

        void OnEnable()
        {
            if (hitbox != null && !subscribed) { hitbox.Changed += Rebuild; subscribed = true; }
            Rebuild();
        }

        void OnDisable()
        {
            if (hitbox != null && subscribed) hitbox.Changed -= Rebuild;
            subscribed = false;
            Clear();
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
            if (hitbox != null && !subscribed) { hitbox.Changed += Rebuild; subscribed = true; }
            Clear();
            if (!show || car == null) return;
            foreach (var bc in car.GetComponentsInChildren<BoxCollider>())
            {
                if (!bc.enabled || bc.isTrigger) continue;
                var pts = new Vector3[Path.Length];
                Vector3 h = bc.size * 0.5f;
                for (int i = 0; i < Path.Length; i++)
                {
                    int k = Path[i];
                    var local = bc.center + new Vector3((k & 1) != 0 ? h.x : -h.x, (k & 2) != 0 ? h.y : -h.y, (k & 4) != 0 ? h.z : -h.z);
                    pts[i] = car.InverseTransformPoint(bc.transform.TransformPoint(local));
                }
                var go = new GameObject("Hitbox_" + bc.name) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(car, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                lr.positionCount = pts.Length;
                lr.SetPositions(pts);
                lr.widthMultiplier = lineWidth;
                lr.sharedMaterial = lineMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lines.Add(go);
            }
        }
    }
}
