using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>First-touch oracle for straight adaptive-shell shots. Solver contact averages are deliberately excluded.</summary>
    internal static class AdaptiveContactValidation
    {
        internal const float DeeperTolerance = .03f, FaceTolerance = .06f, RetractionTolerance = .05f;
        internal static Bounds ProtectedCabin(DamageLab.CollisionTest.VehicleGeometry g, Vector2 scale)
        {
            Vector3 lo = new Vector3(g.cabin.center.x - g.cabin.size.x * scale.x * .5f, g.body.max.y,
                g.cabin.center.z - g.cabin.size.z * scale.y * .5f);
            Vector3 hi = new Vector3(g.cabin.center.x + g.cabin.size.x * scale.x * .5f, Mathf.Max(g.cabin.max.y, g.body.max.y + .05f),
                g.cabin.center.z + g.cabin.size.z * scale.y * .5f);
            return new Bounds((lo + hi) * .5f, hi - lo);
        }
        internal static Vector3 ProjectedHalfExtents(Quaternion rotation, Vector3 localHalf)
        {
            Vector3 x = rotation * Vector3.right * localHalf.x, y = rotation * Vector3.up * localHalf.y, z = rotation * Vector3.forward * localHalf.z;
            return new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
        }
        internal static Bounds ColliderBoundsInCarSpace(BoxCollider box, Transform car)
        {
            Matrix4x4 toCar = car.worldToLocalMatrix * box.transform.localToWorldMatrix;
            var bounds = new Bounds(toCar.MultiplyPoint3x4(box.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = box.center + Vector3.Scale(box.size * .5f,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                bounds.Encapsulate(toCar.MultiplyPoint3x4(corner));
            }
            return bounds;
        }
        internal static bool CabinMatches(BoxCollider box, Transform car, Bounds expected)
        {
            if (box == null || !box.enabled) return false;
            Matrix4x4 toCar = car.worldToLocalMatrix * box.transform.localToWorldMatrix;
            // Matching an AABB alone cannot prove the protected volume of a rotated box.
            for (int i = 0; i < 3; i++)
            {
                Vector3 axis = toCar.GetColumn(i);
                if (axis.sqrMagnitude < 1e-12f) return false;
                axis.Normalize();
                for (int a = 0; a < 3; a++) if (a != i && Mathf.Abs(axis[a]) > .00001f) return false;
            }
            return CabinMatches(ColliderBoundsInCarSpace(box, car), expected);
        }
        internal static bool CabinMatches(Bounds actual, Bounds expected) =>
            (actual.min - expected.min).magnitude < .001f && (actual.max - expected.max).magnitude < .001f;

        internal static float ExpectedFace(float adaptiveFace, Bounds cabin, Vector3 parkedCenter, Vector3 half,
            int axis, float sign, out bool cabinFirst)
        {
            bool overlap = true;
            for (int a = 0; a < 3; a++) if (a != axis)
                overlap &= parkedCenter[a] + half[a] > cabin.min[a] && parkedCenter[a] - half[a] < cabin.max[a];
            float protectedFace = sign > 0 ? cabin.max[axis] : -cabin.min[axis];
            cabinFirst = overlap && protectedFace > adaptiveFace;
            return cabinFirst ? protectedFace : adaptiveFace;
        }
        internal static bool Accept(float first, float second, float expected, float retraction,
            bool applied, bool surfaceIdentity, bool cabinIntact) => applied && surfaceIdentity && cabinIntact &&
            !float.IsNaN(first) && !float.IsNaN(second) && second < first - DeeperTolerance &&
            Mathf.Abs(second - expected) < FaceTolerance && retraction > RetractionTolerance;
    }
}
