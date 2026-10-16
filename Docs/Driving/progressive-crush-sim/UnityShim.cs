// Minimal pure-C# stand-in for the parts of UnityEngine the deformation code uses, so the REAL DeformablePart / ImpactField
// code can execute under Mono for a numeric test. Math follows Unity's definitions (float, same formulas).
using System;
using System.Collections.Generic;
namespace UnityEngine.Rendering { public enum IndexFormat { UInt16, UInt32 } public enum ShadowCastingMode { Off, On } }
namespace UnityEngine
{
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class SerializeField : Attribute { }
    public class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; public int order; }
    public enum HideFlags { None, DontSave }
    public enum MeshTopology { Triangles, Lines }
    public static class Application { public static bool isPlaying = true; }
    public static class Debug { public static void Log(object o) => Console.WriteLine(o); public static void LogWarning(object o, Object c = null) => Console.WriteLine(o); }

    public static class Mathf
    {
        public const float PI = (float)Math.PI, Deg2Rad = PI / 180f, Rad2Deg = 180f / PI, Epsilon = 1.401298E-45f;
        public static float Abs(float f) => Math.Abs(f); public static int Abs(int f) => Math.Abs(f);
        public static float Max(float a, float b) => a > b ? a : b; public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b; public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) m = Max(m, x); return m; }
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v; public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static float Sqrt(float f) => (float)Math.Sqrt(f); public static float Exp(float f) => (float)Math.Exp(f); public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Sin(float f) => (float)Math.Sin(f); public static float Cos(float f) => (float)Math.Cos(f); public static float Tan(float f) => (float)Math.Tan(f);
        public static float Asin(float f) => (float)Math.Asin(f); public static float Acos(float f) => (float)Math.Acos(f); public static float Atan(float f) => (float)Math.Atan(f); public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Floor(float f) => (float)Math.Floor(f); public static int FloorToInt(float f) => (int)Math.Floor(f); public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f); public static float Round(float f) => (float)Math.Round(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f; public static float Log(float f) => (float)Math.Log(f);
        public static float Repeat(float t, float l) => Clamp(t - Floor(t / l) * l, 0f, l);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        public float sqrMagnitude => x * x + y * y; public float magnitude => Mathf.Sqrt(sqrMagnitude);
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? this / m : zero; } }
    }

    public struct Vector3 : IEquatable<Vector3>
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int i] { get => i == 0 ? x : i == 1 ? y : z; set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public static Vector3 zero => new Vector3(0, 0, 0); public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0); public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 right => new Vector3(1, 0, 0); public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 forward => new Vector3(0, 0, 1); public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public bool Equals(Vector3 o) => x.Equals(o.x) && y.Equals(o.y) && z.Equals(o.z);
        public override bool Equals(object o) => o is Vector3 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t); }
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        public static Vector3 Normalize(Vector3 v) { float m = v.magnitude; return m > 1E-05f ? v / m : zero; }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) { float s = Dot(n, n); return s < Mathf.Epsilon ? v : v - n * (Dot(v, n) / s); }
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public float sqrMagnitude => x * x + y * y + z * z; public float magnitude => Mathf.Sqrt(sqrMagnitude);
        public Vector3 normalized => Normalize(this);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})"; public string ToString(string f) => $"({x.ToString(f)}, {y.ToString(f)}, {z.ToString(f)})";
    }

    public struct Vector3Int { public int x, y, z; public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; } public override int GetHashCode() => x * 73856093 ^ y * 19349663 ^ z * 83492791; public override bool Equals(object o) => o is Vector3Int v && v.x == x && v.y == y && v.z == z; }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y + a.y * b.w + a.z * b.x - a.x * b.z,
            a.w * b.z + a.z * b.w + a.x * b.y - a.y * b.x, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public static Vector3 operator *(Quaternion r, Vector3 p)
        {
            float x = r.x * 2f, y = r.y * 2f, z = r.z * 2f, xx = r.x * x, yy = r.y * y, zz = r.z * z, xy = r.x * y, xz = r.x * z, yz = r.y * z, wx = r.w * x, wy = r.w * y, wz = r.w * z;
            return new Vector3((1f - (yy + zz)) * p.x + (xy - wz) * p.y + (xz + wy) * p.z, (xy + wz) * p.x + (1f - (xx + zz)) * p.y + (yz - wx) * p.z, (xz - wy) * p.x + (yz + wx) * p.y + (1f - (xx + yy)) * p.z);
        }
        public static Quaternion Inverse(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);
        public static Quaternion Euler(float ex, float ey, float ez)
        {
            float cx = Mathf.Cos(ex * Mathf.Deg2Rad * 0.5f), sx = Mathf.Sin(ex * Mathf.Deg2Rad * 0.5f), cy = Mathf.Cos(ey * Mathf.Deg2Rad * 0.5f), sy = Mathf.Sin(ey * Mathf.Deg2Rad * 0.5f), cz = Mathf.Cos(ez * Mathf.Deg2Rad * 0.5f), sz = Mathf.Sin(ez * Mathf.Deg2Rad * 0.5f);
            var qx = new Quaternion(sx, 0, 0, cx); var qy = new Quaternion(0, sy, 0, cy); var qz = new Quaternion(0, 0, sz, cz);
            return qy * qx * qz;   // Unity order: Z, then X, then Y
        }
        public static float Angle(Quaternion a, Quaternion b) { float d = Mathf.Min(Mathf.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w), 1f); return d > 0.999999f ? 0f : Mathf.Acos(d) * 2f * Mathf.Rad2Deg; }
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity => new Matrix4x4 { m00 = 1, m11 = 1, m22 = 1, m33 = 1 };
        public static Matrix4x4 TRS(Vector3 t, Quaternion r, Vector3 s)
        {
            Vector3 cx = r * new Vector3(s.x, 0, 0), cy = r * new Vector3(0, s.y, 0), cz = r * new Vector3(0, 0, s.z);
            return new Matrix4x4 { m00 = cx.x, m10 = cx.y, m20 = cx.z, m01 = cy.x, m11 = cy.y, m21 = cy.z, m02 = cz.x, m12 = cz.y, m22 = cz.z, m03 = t.x, m13 = t.y, m23 = t.z, m33 = 1 };
        }
        public Vector3 MultiplyPoint3x4(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z + m03, m10 * p.x + m11 * p.y + m12 * p.z + m13, m20 * p.x + m21 * p.y + m22 * p.z + m23);
        public Vector3 MultiplyVector(Vector3 p) => new Vector3(m00 * p.x + m01 * p.y + m02 * p.z, m10 * p.x + m11 * p.y + m12 * p.z, m20 * p.x + m21 * p.y + m22 * p.z);
        public Vector3 MultiplyPoint(Vector3 p) => MultiplyPoint3x4(p);
        float this[int r, int c] => new[] { m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33 }[r * 4 + c];
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
        {
            var r = new float[16];
            for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { float s = 0; for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j]; r[i * 4 + j] = s; }
            return From(r);
        }
        static Matrix4x4 From(float[] r) => new Matrix4x4 { m00 = r[0], m01 = r[1], m02 = r[2], m03 = r[3], m10 = r[4], m11 = r[5], m12 = r[6], m13 = r[7], m20 = r[8], m21 = r[9], m22 = r[10], m23 = r[11], m30 = r[12], m31 = r[13], m32 = r[14], m33 = r[15] };
        public Matrix4x4 inverse
        {
            get
            {   // affine inverse (rotation*scale + translation)
                double a = m00, b = m01, c = m02, d = m10, e = m11, f = m12, g = m20, h = m21, i = m22;
                double det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
                double[] inv = { (e * i - f * h) / det, (c * h - b * i) / det, (b * f - c * e) / det, (f * g - d * i) / det, (a * i - c * g) / det, (c * d - a * f) / det, (d * h - e * g) / det, (b * g - a * h) / det, (a * e - b * d) / det };
                var m = new Matrix4x4 { m00 = (float)inv[0], m01 = (float)inv[1], m02 = (float)inv[2], m10 = (float)inv[3], m11 = (float)inv[4], m12 = (float)inv[5], m20 = (float)inv[6], m21 = (float)inv[7], m22 = (float)inv[8], m33 = 1 };
                Vector3 t = m.MultiplyVector(new Vector3(m03, m13, m23));
                m.m03 = -t.x; m.m13 = -t.y; m.m23 = -t.z;
                return m;
            }
        }
    }

    public struct Bounds
    {
        public Vector3 center, extents;
        public Bounds(Vector3 c, Vector3 size) { center = c; extents = size * 0.5f; }
        public Vector3 size { get => extents * 2f; set => extents = value * 0.5f; }
        public Vector3 min { get => center - extents; set => SetMinMax(value, max); }
        public Vector3 max { get => center + extents; set => SetMinMax(min, value); }
        public void SetMinMax(Vector3 a, Vector3 b) { extents = (b - a) * 0.5f; center = a + extents; }
        public void Encapsulate(Vector3 p) => SetMinMax(Vector3.Min(min, p), Vector3.Max(max, p));
        public void Encapsulate(Bounds b) { Encapsulate(b.center - b.extents); Encapsulate(b.center + b.extents); }
        public void Expand(float a) { extents += new Vector3(a, a, a) * 0.5f; }
        public bool Intersects(Bounds b) => min.x <= b.max.x && max.x >= b.min.x && min.y <= b.max.y && max.y >= b.min.y && min.z <= b.max.z && max.z >= b.min.z;
        public bool Contains(Vector3 p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
        public float SqrDistance(Vector3 p) { Vector3 q = Vector3.Max(min, Vector3.Min(max, p)); return (q - p).sqrMagnitude; }
    }

    public class Object { public string name; public HideFlags hideFlags;
        public static T Instantiate<T>(T o) where T : Object => (T)o.Clone();
        protected virtual Object Clone() => (Object)MemberwiseClone();
        public static void Destroy(Object o) { } public static void DestroyImmediate(Object o) { }
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null); }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); }
    public class Material : Object { }
    public class Mesh : Object
    {
        public Vector3[] vertices = new Vector3[0], normals; public int[] triangles = new int[0];
        public Bounds bounds; public Rendering.IndexFormat indexFormat; public int uploads;
        protected override Object Clone() { var m = (Mesh)MemberwiseClone(); m.vertices = (Vector3[])vertices.Clone(); m.normals = normals == null ? null : (Vector3[])normals.Clone(); return m; }
        public void MarkDynamic() { }
        public void SetVertices(Vector3[] v) { vertices = (Vector3[])v.Clone(); uploads++; }
        public void SetVertices(List<Vector3> v) { vertices = v.ToArray(); }
        public void SetNormals(Vector3[] n) { normals = (Vector3[])n.Clone(); }
        public void SetIndices(List<int> i, MeshTopology t, int s) { }
        public void RecalculateBounds() { var b = new Bounds(vertices[0], Vector3.zero); foreach (var v in vertices) b.Encapsulate(v); bounds = b; }
        public void RecalculateNormals() { normals = new Vector3[vertices.Length]; }
    }
    public class Component : Object { public GameObject gameObject; public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>(); }
    public class Transform : Component
    {
        public Transform parent; public Vector3 localPosition; public Quaternion localRotation = Quaternion.identity; public Matrix4x4 local = Matrix4x4.identity;
        public Matrix4x4 localToWorldMatrix => parent == null ? local : parent.localToWorldMatrix * local;
        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
        public Vector3 TransformVector(Vector3 v) => localToWorldMatrix.MultiplyVector(v);
        public Vector3 InverseTransformVector(Vector3 v) => worldToLocalMatrix.MultiplyVector(v);
        public Vector3 InverseTransformPoint(Vector3 v) => worldToLocalMatrix.MultiplyPoint3x4(v);
        public Vector3 position => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
        public void SetParent(Transform p, bool w) { parent = p; }
    }
    public class GameObject : Object
    {
        readonly List<Component> comps = new List<Component>(); public Transform transform;
        public GameObject(string n = "go") { name = n; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component, new() { var c = new T { gameObject = this }; comps.Add(c); return c; }
        public T GetComponent<T>() where T : Component { foreach (var c in comps) if (c is T t) return t; return null; }
        public void SetActive(bool b) { }
    }
    public class MeshFilter : Component { public Mesh sharedMesh; }
    public class Renderer : Component { public Bounds bounds; public Material sharedMaterial; public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows; }
    public class MeshRenderer : Renderer { }
}
