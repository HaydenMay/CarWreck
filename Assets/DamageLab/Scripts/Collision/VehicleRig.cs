using System;
using System.Collections.Generic;
using DamageLab.Vehicle;
using UnityEngine;

namespace DamageLab.CollisionTest
{
    /// <summary>Reference geometry of a vehicle, measured from its classified components (car space, model at the origin).</summary>
    [Serializable]
    public struct VehicleGeometry
    {
        public Bounds body;          // Body + Bumper components: the outer impact faces and the physics shell
        public Bounds cabin;         // Cabin components
        public Bounds wheels;        // Wheel components
        public Vector3[] wheelCenters;
        public float groundLift;     // root height that puts the tyres on the ground
        public float Beltline => body.max.y;
    }

    /// <summary>Materials and physics material the rig gives every vehicle (shared lab assets).</summary>
    [Serializable]
    public class VehicleRigSettings
    {
        public PhysicsMaterial bodyPhysicsMaterial;
        public Material wireframe;
        public Material footprintOutline;
        public Material footprintArrow;
        public Material hitboxLines;
    }

    /// <summary>
    /// Builds a destructible physics vehicle from a <see cref="VehicleDefinition"/>: instantiates the model, measures its
    /// geometry from the definition's component roles, places it on the ground, and adds the Rigidbody, the 14-box
    /// physics shell (core, cabin, 12 adaptive regions sized from the definition), the damage model, the collision
    /// receiver, the adaptive hitbox and the debug views. Works at runtime (vehicle switching, WebGL) and in the editor.
    /// Nothing here is specific to one vehicle.
    /// </summary>
    public static class VehicleRig
    {
        public class Rig
        {
            public VehicleDefinition definition;
            public GameObject root;
            public Rigidbody body;
            public VehicleDamageModel model;
            public CollisionDamageReceiver receiver;
            public VehicleHitbox hitbox;
            public ImpactFootprintDebug footprintDebug;
            public PhysicsHitboxDebug colliderDebug;
            public VehicleGeometry geometry;
            public readonly List<string> warnings = new List<string>();
        }

        /// <summary>Measure a model instance placed at the origin with identity rotation.</summary>
        public static VehicleGeometry Measure(VehicleDefinition def, GameObject instance, List<string> warnings = null)
        {
            var rules = def.components;
            Bounds? body = null, cabin = null, wheels = null, all = null;
            var wheelCenters = new List<Vector3>();
            foreach (var r in instance.GetComponentsInChildren<MeshRenderer>(true))
            {
                var rule = VehiclePartRule.Find(rules, r.name, out _);
                if (rule != null && rule.mode == PartMode.Ignore) continue;
                Bounds b = r.bounds;
                all = Grow(all, b);
                var role = rule?.role ?? VehicleComponentRole.Unspecified;
                if (rule != null && rule.DefinesImpactFaces) body = Grow(body, b);
                if (role == VehicleComponentRole.Cabin) cabin = Grow(cabin, b);
                if (role == VehicleComponentRole.Wheel || (rule != null && rule.mode == PartMode.Rigid)) { wheels = Grow(wheels, b); wheelCenters.Add(b.center); }
            }
            if (all == null) throw new InvalidOperationException($"{def.displayName}: model has no renderers");
            if (body == null) { warnings?.Add("No Body/Bumper components: physics shell uses the whole model's bounds."); body = all; }
            if (cabin == null)
            {
                warnings?.Add("No Cabin components: cabin box spans the top of the model above the body.");
                var a = all.Value; var bb = body.Value;
                cabin = new Bounds(new Vector3(bb.center.x, (bb.max.y + a.max.y) * 0.5f, bb.center.z), new Vector3(bb.size.x * 0.8f, Mathf.Max(0.05f, a.max.y - bb.max.y), bb.size.z * 0.5f));
            }
            if (wheels == null) { warnings?.Add("No Wheel components: ground placement uses the lowest point of the model."); wheels = new Bounds(all.Value.center, all.Value.size); }
            if (def.profile != null && Mathf.Abs(def.profile.roofLineStart - body.Value.max.y) > 0.12f)
                warnings?.Add($"Damage profile cabin line starts at {def.profile.roofLineStart:0.00} m but the measured beltline is {body.Value.max.y:0.00} m: cabin protection is misplaced.");
            return new VehicleGeometry
            {
                body = body.Value, cabin = cabin.Value, wheels = wheels.Value,
                wheelCenters = wheelCenters.ToArray(),
                groundLift = -wheels.Value.min.y + 0.002f,
            };
        }

        static Bounds Grow(Bounds? acc, Bounds b) { if (acc == null) return b; var a = acc.Value; a.Encapsulate(b); return a; }

        public static Rig Build(VehicleDefinition def, Transform parent, Vector3 basePosition, VehicleRigSettings s)
        {
            if (def == null || def.model == null) throw new ArgumentException("Vehicle definition has no model");
            var rig = new Rig { definition = def };
            var car = UnityEngine.Object.Instantiate(def.model, parent);
            car.name = string.IsNullOrEmpty(def.displayName) ? def.model.name : def.displayName;
            car.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            car.transform.localScale = Vector3.one;
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            var g = Measure(def, car, rig.warnings);
            rig.geometry = g;
            car.transform.position = basePosition + Vector3.up * g.groundLift;
            rig.root = car;

            // ---------------------------------------------------------------- physics shell
            // Core inside, nose and tail thirds (end face adapts; corners also adapt their side face), side shell
            // thirds between them (side face adapts), and the cabin box. The pieces tile the body exactly.
            var shapes = new GameObject("PhysicsShape").transform;
            shapes.SetParent(car.transform, false);
            Bounds body = g.body;
            float coreFront = body.max.z - def.FrontDepth(body), coreRear = body.min.z + def.RearDepth(body);
            float side = Mathf.Min(def.SideDepth(body), body.size.x * 0.45f);
            float y0 = g.wheels.min.y, y1 = body.max.y;
            var regions = new List<VehicleHitbox.Region>();
            BoxCollider Box(string n, Vector3 a, Vector3 b)
            {
                var bc = new GameObject(n).AddComponent<BoxCollider>();
                bc.transform.SetParent(shapes, false);
                bc.center = (a + b) * 0.5f; bc.size = b - a;
                bc.sharedMaterial = s?.bodyPhysicsMaterial;
                return bc;
            }
            void Region(string n, Vector3 a, Vector3 b, bool pz, bool nz, bool nx, bool px)
            {
                var bc = Box(n + "Box", a, b);
                regions.Add(new VehicleHitbox.Region { name = n, box = bc, posZ = pz, negZ = nz, negX = nx, posX = px, outer = new Bounds(bc.center, bc.size) });
            }
            float x0 = body.min.x, x3 = body.max.x, third = body.size.x / 3f, x1 = x0 + third, x2 = x0 + 2f * third;
            float sxL = x0 + side, sxR = x3 - side;
            var core = Box("BodyCoreBox", new Vector3(sxL, y0, coreRear), new Vector3(sxR, y1, coreFront));
            Region("FrontLeft", new Vector3(x0, y0, coreFront), new Vector3(x1, y1, body.max.z), true, false, true, false);
            Region("FrontCentre", new Vector3(x1, y0, coreFront), new Vector3(x2, y1, body.max.z), true, false, false, false);
            Region("FrontRight", new Vector3(x2, y0, coreFront), new Vector3(x3, y1, body.max.z), true, false, false, true);
            Region("RearLeft", new Vector3(x0, y0, body.min.z), new Vector3(x1, y1, coreRear), false, true, true, false);
            Region("RearCentre", new Vector3(x1, y0, body.min.z), new Vector3(x2, y1, coreRear), false, true, false, false);
            Region("RearRight", new Vector3(x2, y0, body.min.z), new Vector3(x3, y1, coreRear), false, true, false, true);
            float len = (coreFront - coreRear) / 3f;
            string[] along = { "Rear", "Mid", "Front" };
            for (int i = 0; i < 3; i++)
            {
                float za = coreRear + len * i, zb = coreRear + len * (i + 1);
                Region("Left" + along[i], new Vector3(x0, y0, za), new Vector3(sxL, y1, zb), false, false, true, false);
                Region("Right" + along[i], new Vector3(sxR, y0, za), new Vector3(x3, y1, zb), false, false, false, true);
            }
            Bounds cab = g.cabin;
            var cabin = Box("CabinBox",
                new Vector3(cab.center.x - cab.size.x * def.cabinBoxScale.x * 0.5f, body.max.y, cab.center.z - cab.size.z * def.cabinBoxScale.y * 0.5f),
                new Vector3(cab.center.x + cab.size.x * def.cabinBoxScale.x * 0.5f, Mathf.Max(cab.max.y, body.max.y + 0.05f), cab.center.z + cab.size.z * def.cabinBoxScale.y * 0.5f));

            // ---------------------------------------------------------------- body and destruction components
            var rb = car.AddComponent<Rigidbody>();
            rb.mass = def.mass;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = new Vector3(0f, g.wheels.min.y + 0.45f, body.center.z);
            rig.body = rb;

            var model = car.AddComponent<VehicleDamageModel>();
            model.definition = def;
            model.profile = def.profile;
            model.partRules = new List<VehiclePartRule>(def.components);
            model.wireframeMaterial = s?.wireframe;
            model.createPickingColliders = false;            // concave MeshColliders cannot live on a dynamic Rigidbody
            model.maxImpacts = Mathf.Max(1, def.maxImpactRecords);
            model.impactOverflow = ImpactOverflow.MergeOldest;
            rig.model = model;

            var receiver = car.AddComponent<CollisionDamageReceiver>();
            receiver.model = model;
            receiver.body = rb;
            rig.receiver = receiver;

            var fp = car.AddComponent<ImpactFootprintDebug>();
            fp.model = model;
            fp.outlineMaterial = s?.footprintOutline;
            fp.arrowMaterial = s?.footprintArrow;
            rig.footprintDebug = fp;

            var hb = car.AddComponent<VehicleHitbox>();
            hb.model = model;
            hb.core = core;
            hb.cabin = cabin;
            hb.regions = regions.ToArray();
            rig.hitbox = hb;

            var cd = car.AddComponent<PhysicsHitboxDebug>();
            cd.car = car.transform;
            cd.hitbox = hb;
            cd.lineMaterial = s?.hitboxLines;
            cd.show = false;
            rig.colliderDebug = cd;
            return rig;
        }

        /// <summary>Impactor centre-line height above the car's base for this vehicle.</summary>
        public static float ImpactHeight(VehicleDefinition def, VehicleGeometry g) =>
            def != null && def.impactHeight > 0f ? def.impactHeight : Mathf.Max(0.6f, g.body.center.y);   // above the car root
    }
}
