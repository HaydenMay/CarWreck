using System.IO;
using System.Linq;
using CarWreck.Driving;
using DamageLab.CollisionTest;
using DamageLab.EditorTools;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// Builds the Milestone 1 driving scene (Assets/Driving/Scenes/DriveArena.unity): a flat arena with walls and simple
    /// obstacles, the muscle car (VehicleRig, exactly as the Collision Lab builds it) driven by ArcadeCarController,
    /// a chase camera and the touch/HUD overlay. Primitive geometry and generated textures only. Re-runnable.
    /// </summary>
    public static class DrivingSceneBuilder
    {
        public const string Root = "Assets/Driving";
        public const string ScenePath = Root + "/Scenes/DriveArena.unity";
        const string TextureFolder = Root + "/Art";
        const float ArenaHalf = 70f;

        [MenuItem("CarWreck/Build Driving Scene", priority = 10)]
        public static void Build()
        {
            DamageLabAssets.EnsureFolders();
            foreach (var f in new[] { Root + "/Scenes", TextureFolder })
                if (!AssetDatabase.IsValidFolder(f)) { Directory.CreateDirectory(f); AssetDatabase.Refresh(); }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------------- materials
            var gridTex = EnsureTexture("DriveGrid.png", MakeGrid());
            var hazardTex = EnsureTexture("DriveHazard.png", MakeHazard());
            var ground = LitTiled("DriveGround", new Color(0.78f, 0.8f, 0.82f), 0.05f, gridTex, ArenaHalf * 2.2f / 4f);
            var wall = DamageLabAssets.LitMaterial("DriveWall", new Color(0.62f, 0.63f, 0.65f), 0f, 0.25f);
            var barrier = DamageLabAssets.LitMaterial("DriveBarrier", new Color(0.72f, 0.72f, 0.7f), 0f, 0.2f);
            var pillar = DamageLabAssets.LitMaterial("DrivePillar", new Color(0.9f, 0.45f, 0.1f), 0f, 0.35f);
            var crate = DamageLabAssets.LitMaterial("DriveCrate", new Color(0.62f, 0.45f, 0.26f), 0f, 0.15f);
            var heavy = DamageLabAssets.LitMaterial("DriveHeavy", new Color(0.2f, 0.42f, 0.78f), 0.1f, 0.4f);
            var ramp = DamageLabAssets.LitMaterial("DriveRamp", new Color(0.35f, 0.36f, 0.4f), 0f, 0.2f);
            var pad = DamageLabAssets.LitMaterial("DriveStartPad", new Color(0.95f, 0.45f, 0.1f), 0f, 0.3f);
            var hazard = LitTiled("DriveHazard", Color.white, 0f, hazardTex, 1f);
            hazard.SetTextureScale("_BaseMap", new Vector2(14f, 2f));
            EditorUtility.SetDirty(hazard);

            // ---------------------------------------------------------------- ground and lights
            var arena = new GameObject("Arena").transform;
            var groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundGO.name = "Ground";
            groundGO.transform.SetParent(arena, false);
            groundGO.transform.position = new Vector3(0f, -0.2f, 0f);
            groundGO.transform.localScale = new Vector3(ArenaHalf * 2.2f, 0.4f, ArenaHalf * 2.2f);
            groundGO.GetComponent<MeshRenderer>().sharedMaterial = ground;
            groundGO.isStatic = true;
            var startPad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            startPad.name = "StartPad";
            Object.DestroyImmediate(startPad.GetComponent<Collider>());
            startPad.transform.SetParent(arena, false);
            startPad.transform.position = new Vector3(0f, 0.01f, -50f);
            startPad.transform.localScale = new Vector3(5.5f, 0.02f, 9f);
            startPad.GetComponent<MeshRenderer>().sharedMaterial = pad;

            var key = DamageLabSceneBuilder.CreateLight("Sun", LightType.Directional, new Color(1f, 0.96f, 0.9f), 1.6f, new Vector3(52f, -35f, 0f), LightShadows.Soft);
            key.shadowStrength = 0.85f;
            DamageLabSceneBuilder.CreateLight("Sky Fill", LightType.Directional, new Color(0.8f, 0.88f, 1f), 0.45f, new Vector3(30f, 150f, 0f), LightShadows.None);
            Color sky = new Color(0.58f, 0.68f, 0.8f);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.25f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 90f;
            RenderSettings.fogEndDistance = 260f;
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(DamageLabAssets.Root + "/Art/Textures/StudioReflection.exr");
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
            }

            // ---------------------------------------------------------------- obstacles
            // Every obstacle is a Rigidbody: the damage receiver only registers contacts with other Rigidbodies.
            // Walls/barriers/pillars are kinematic (immovable); crates and heavy blocks are dynamic (they get shoved).
            var obstacles = new GameObject("Obstacles").transform;
            float w = ArenaHalf;
            Block("WallNorth", obstacles, new Vector3(0f, 1f, w + 0.5f), new Vector3(w * 2f + 2f, 2f, 1f), 0f, wall, 0f);
            Block("WallSouth", obstacles, new Vector3(0f, 1f, -w - 0.5f), new Vector3(w * 2f + 2f, 2f, 1f), 0f, wall, 0f);
            Block("WallEast", obstacles, new Vector3(w + 0.5f, 1f, 0f), new Vector3(1f, 2f, w * 2f), 0f, wall, 0f);
            Block("WallWest", obstacles, new Vector3(-w - 0.5f, 1f, 0f), new Vector3(1f, 2f, w * 2f), 0f, wall, 0f);

            // The crash target straight ahead of the start pad: a long hazard-striped concrete wall.
            Block("CrashWall", obstacles, new Vector3(0f, 1.25f, 22f), new Vector3(16f, 2.5f, 1.6f), 0f, hazard, 0f);
            Block("CrashWallSideL", obstacles, new Vector3(-9.5f, 1.25f, 20.2f), new Vector3(1.6f, 2.5f, 5.2f), 0f, hazard, 0f);
            Block("CrashWallSideR", obstacles, new Vector3(9.5f, 1.25f, 20.2f), new Vector3(1.6f, 2.5f, 5.2f), 0f, hazard, 0f);

            // Pillars, kept out of the middle: the target vehicle parks there with open ground all round.
            foreach (var (x, z) in new[] { (-34f, -30f), (-26f, -24f), (26f, -30f), (34f, -22f), (-30f, 12f), (30f, 10f) })
                Block("Pillar", obstacles, new Vector3(x, 1.5f, z), new Vector3(1.3f, 3f, 1.3f), 0f, pillar, 0f);

            // Concrete barriers at angles.
            Block("Barrier", obstacles, new Vector3(-30f, 0.55f, -10f), new Vector3(5f, 1.1f, 0.9f), 25f, barrier, 0f);
            Block("Barrier", obstacles, new Vector3(30f, 0.55f, -2f), new Vector3(5f, 1.1f, 0.9f), -40f, barrier, 0f);
            Block("Barrier", obstacles, new Vector3(-34f, 0.55f, 30f), new Vector3(5f, 1.1f, 0.9f), 90f, barrier, 0f);
            Block("Barrier", obstacles, new Vector3(26f, 0.55f, 40f), new Vector3(5f, 1.1f, 0.9f), 10f, barrier, 0f);

            // Dynamic crate pyramids and heavy blocks: they fly when hit and also dent the car.
            Pyramid(obstacles, new Vector3(-40f, 0f, 5f), crate);
            Pyramid(obstacles, new Vector3(38f, 0f, 22f), crate);
            Pyramid(obstacles, new Vector3(-22f, 0f, -42f), crate);
            Block("HeavyBlock", obstacles, new Vector3(22f, 0.6f, -26f), new Vector3(2.2f, 1.2f, 2.2f), 15f, heavy, 900f);
            Block("HeavyBlock", obstacles, new Vector3(-24f, 0.6f, 18f), new Vector3(2.2f, 1.2f, 2.2f), -20f, heavy, 900f);
            Block("HeavyBlock", obstacles, new Vector3(46f, 0.6f, -20f), new Vector3(2.2f, 1.2f, 2.2f), 0f, heavy, 900f);

            // A gentle kicker (static: it is part of the ground, not a damage target).
            var rampGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rampGO.name = "Ramp";
            rampGO.transform.SetParent(arena, false);
            rampGO.transform.localScale = new Vector3(7f, 0.4f, 9f);
            rampGO.transform.SetPositionAndRotation(new Vector3(-40f, 0.43f, -35f), Quaternion.Euler(-9f, 0f, 0f));
            rampGO.GetComponent<MeshRenderer>().sharedMaterial = ramp;
            rampGO.isStatic = true;

            // ---------------------------------------------------------------- vehicles (built at runtime by DrivingGame from their definitions)
            var vehicles = new[]
            {
                LoadDefinition(VehicleDefinitionSetup.MusclePath),
                LoadDefinition(VehicleDefinitionSetup.HatchPath),
                LoadDefinition(VehicleDefinitionSetup.Folder + "/CompactPickup.asset"),
            };
            var profiles = new[]
            {
                Profile("Muscle", "Muscle Car", "Powerful & balanced: strong launch, high top speed", p =>
                {
                    p.acceleration = 13f; p.maxSpeed = 30f; p.reverseAcceleration = 11f; p.reverseMaxSpeed = 13.5f;
                    p.brakeDeceleration = 26f; p.coastDeceleration = 1.2f;
                    p.steerRateLow = 100f; p.steerRateHigh = 34f; p.steerRise = 5f; p.steerReturn = 9f; p.steerResponse = 28f;
                    p.grip = 9f; p.maxGripAcceleration = 26f; p.brakingGripScale = 0.55f; p.downforce = 0.22f;
                }),
                Profile("Hatchback", "Compact Hatchback", "Agile & light: quick, sharp steering, less momentum", p =>
                {
                    p.acceleration = 15.5f; p.maxSpeed = 29f; p.reverseAcceleration = 12.5f; p.reverseMaxSpeed = 13.5f;
                    p.brakeDeceleration = 30f; p.coastDeceleration = 1.4f;
                    p.steerRateLow = 125f; p.steerRateHigh = 42f; p.steerRise = 7f; p.steerReturn = 11f; p.steerResponse = 30f;
                    p.grip = 11f; p.maxGripAcceleration = 28f; p.brakingGripScale = 0.6f; p.downforce = 0.2f;
                }),
                Profile("Pickup", "Compact Pickup", "Heavy & deliberate: slow to turn, solid hits", p =>
                {
                    p.acceleration = 9.5f; p.maxSpeed = 26f; p.reverseAcceleration = 8.5f; p.reverseMaxSpeed = 11.5f;
                    p.brakeDeceleration = 19f; p.coastDeceleration = 1.0f;
                    p.steerRateLow = 78f; p.steerRateHigh = 26f; p.steerFullAuthoritySpeed = 3.5f; p.steerRise = 3.2f; p.steerReturn = 6f; p.steerResponse = 22f;
                    p.grip = 7f; p.maxGripAcceleration = 20f; p.brakingGripScale = 0.5f; p.downforce = 0.25f;
                }),
            };
            var wire = AssetDatabase.LoadAssetAtPath<Material>(DamageLabAssets.MaterialFolder + "/Wireframe.mat") ?? DamageLabAssets.WireframeMaterial();
            var playerRig = new VehicleRigSettings { bodyPhysicsMaterial = DrivePhysicsMaterial(), wireframe = wire };
            var targetRig = new VehicleRigSettings { bodyPhysicsMaterial = ParkedPhysicsMaterial(), wireframe = wire };
            var playerSpawn = new GameObject("PlayerSpawn").transform;
            playerSpawn.SetPositionAndRotation(new Vector3(0f, 0f, -50f), Quaternion.Euler(0f, 0f, 0f));
            // The target parks sideways to the approach (broadside to the player's start), 42 m ahead, open ground all round it.
            var targetSpawn = new GameObject("TargetSpawn").transform;
            targetSpawn.SetPositionAndRotation(new Vector3(0f, 0f, -8f), Quaternion.Euler(0f, 90f, 0f));

            // ---------------------------------------------------------------- camera, UI, game
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = false;
            cd.antialiasing = AntialiasingMode.None;
            camGO.AddComponent<AudioListener>();
            var chase = camGO.AddComponent<ChaseCamera>();
            camGO.transform.position = playerSpawn.position + new Vector3(0f, 3.1f, -7.5f);
            camGO.transform.LookAt(playerSpawn.position + Vector3.up);

            var input = new GameObject("Driving Input").AddComponent<DriveInput>();
            var ui = new GameObject("Driving UI").AddComponent<DrivingUI>();
            ui.input = input;
            var game = new GameObject("DrivingGame").AddComponent<DrivingGame>();
            game.vehicles = vehicles;
            game.profiles = profiles;
            game.playerRig = playerRig;
            game.targetRig = targetRig;
            game.playerSpawn = playerSpawn;
            game.targetSpawn = targetSpawn;
            game.playerIndex = 0;
            game.targetIndex = 2;
            game.chaseCamera = chase;
            game.input = input;
            game.ui = ui;
            game.obstacles = obstacles;
            ui.game = game;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            string list = string.Join(", ", vehicles.Select(v => v.displayName + " " + v.mass + " kg"));
            Debug.Log($"[Driving] Built {ScenePath}: {vehicles.Length} vehicles ({list}), player spawn {playerSpawn.position}, target spawn {targetSpawn.position}");
        }

        // ------------------------------------------------------------------ helpers


        static VehicleDefinition LoadDefinition(string path)
        {
            var d = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (d == null || d.model == null) throw new System.Exception($"Vehicle definition missing or without a model: {path}");
            return d;
        }

        static DriveProfile Profile(string file, string displayName, string description, System.Action<DriveProfile> set)
        {
            string path = $"{Root}/Profiles/{file}.asset";
            if (!AssetDatabase.IsValidFolder(Root + "/Profiles")) AssetDatabase.CreateFolder(Root, "Profiles");
            var p = AssetDatabase.LoadAssetAtPath<DriveProfile>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<DriveProfile>(); AssetDatabase.CreateAsset(p, path); }
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(ScriptableObject.CreateInstance<DriveProfile>()), p);   // back to code defaults first
            p.displayName = displayName;
            p.description = description;
            set(p);
            EditorUtility.SetDirty(p);
            return p;
        }

        static PhysicsMaterial ParkedPhysicsMaterial()
        {
            const string path = Root + "/Art/CarParked.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null) { m = new PhysicsMaterial("CarParked"); AssetDatabase.CreateAsset(m, path); }
            // Parked with the brakes on: it slides a little when hit and then stops, instead of skating away.
            m.dynamicFriction = 0.5f;
            m.staticFriction = 0.7f;
            m.bounciness = 0f;
            m.frictionCombine = PhysicsMaterialCombine.Average;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(m);
            return m;
        }

        static GameObject Block(string name, Transform parent, Vector3 pos, Vector3 size, float yawDeg, Material mat, float mass)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var rb = go.AddComponent<Rigidbody>();
            if (mass <= 0f) { rb.isKinematic = true; }
            else
            {
                rb.mass = mass;
                rb.linearDamping = 0.15f;
                rb.angularDamping = 0.4f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            return go;
        }

        /// <summary>A stack of 1.1 m crates: 4-3-2-1.</summary>
        static void Pyramid(Transform parent, Vector3 origin, Material mat)
        {
            const float s = 1.1f, gap = 0.03f;
            for (int row = 0; row < 4; row++)
            {
                int n = 4 - row;
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) * 0.5f) * (s + gap);
                    Block("Crate", parent, origin + new Vector3(x, s * 0.5f + row * (s + 0.005f), 0f), Vector3.one * s, 0f, mat, 160f);
                }
            }
        }

        static PhysicsMaterial DrivePhysicsMaterial()
        {
            const string path = Root + "/Art/CarDriving.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null) { m = new PhysicsMaterial("CarDriving"); AssetDatabase.CreateAsset(m, path); }
            // Low friction: the controller supplies grip itself; this just lets the shell slide off walls and obstacles.
            m.dynamicFriction = 0.12f;
            m.staticFriction = 0.15f;
            m.bounciness = 0f;
            m.frictionCombine = PhysicsMaterialCombine.Minimum;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LitTiled(string name, Color color, float smoothness, Texture2D tex, float tiling)
        {
            var m = DamageLabAssets.LitMaterial(name, color, 0f, smoothness, null, tex);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D EnsureTexture(string file, Texture2D generated)
        {
            string path = $"{TextureFolder}/{file}";
            File.WriteAllBytes(path, generated.EncodeToPNG());
            Object.DestroyImmediate(generated);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>4 m tile: two-tone 2 m cells with light lines, so motion and speed read on a flat floor.</summary>
        static Texture2D MakeGrid()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool a = (x < n / 2) ^ (y < n / 2);
                    float v = a ? 0.60f : 0.66f;
                    int cx = x % (n / 2), cy = y % (n / 2);
                    if (cx < 3 || cy < 3) v = 0.82f;
                    byte b = (byte)Mathf.RoundToInt(v * 255f);
                    px[y * n + x] = new Color32(b, b, (byte)Mathf.Min(255, b + 4), 255);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeHazard()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = ((x + y) / 16) % 2 == 0 ? new Color32(245, 190, 20, 255) : new Color32(28, 28, 30, 255);
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
