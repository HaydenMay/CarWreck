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

namespace CarWreck.Derby.EditorTools
{
    /// <summary>
    /// Builds Assets/Derby/Scenes/DerbyArena.unity: the demolition derby arena (a 100 m dirt square inside a concrete perimeter, a small pinwheel of barriers in the
    /// middle, tyre stacks and barrier pairs, stands and floodlights for the look), the spawn ring, the camera, the UI and the <see cref="DerbyGame"/> with its assets
    /// (paint palette, AI driver profiles). Primitive geometry and generated textures only. Existing scenes are untouched. Re-runnable.
    /// </summary>
    public static class DerbySceneBuilder
    {
        public const string Root = "Assets/Derby";
        public const string ScenePath = Root + "/Scenes/DerbyArena.unity";
        const string ArtFolder = Root + "/Art", SettingsFolder = Root + "/Settings";
        public const float ArenaHalf = 50f;

        [MenuItem("CarWreck/Build Derby Scene", priority = 20)]
        public static void Build()
        {
            DamageLabAssets.EnsureFolders();
            foreach (var f in new[] { Root + "/Scenes", ArtFolder, SettingsFolder })
                if (!AssetDatabase.IsValidFolder(f)) { Directory.CreateDirectory(f); AssetDatabase.Refresh(); }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------------- materials
            var dirtTex = EnsureTexture("DerbyDirt.png", MakeDirt(), true);
            var barrierTex = EnsureTexture("DerbyBarrier.png", MakeBarrier(), false);
            var crowdTex = EnsureTexture("DerbyCrowd.png", MakeCrowd(), false);
            var dirt = Tiled("DerbyDirt", Color.white, 0.02f, dirtTex, ArenaHalf * 2.3f / 7f);
            var perimeter = Tiled("DerbyPerimeter", Color.white, 0.15f, barrierTex, 1f);
            var concrete = DamageLabAssets.LitMaterial("DerbyConcrete", new Color(0.66f, 0.65f, 0.62f), 0f, 0.2f);
            var tyres = DamageLabAssets.LitMaterial("DerbyTyres", new Color(0.09f, 0.09f, 0.1f), 0f, 0.35f);
            var hazard = DamageLabAssets.LitMaterial("DerbyHazard", new Color(0.95f, 0.72f, 0.1f), 0f, 0.3f);
            var stands = Tiled("DerbyStands", Color.white, 0.05f, crowdTex, 1f);
            var pole = DamageLabAssets.LitMaterial("DerbyPole", new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.4f);
            var lamp = DamageLabAssets.LitMaterial("DerbyLamp", new Color(1f, 0.95f, 0.8f), 0f, 0.2f, new Color(2.4f, 2.2f, 1.7f));

            // ---------------------------------------------------------------- ground
            var arena = new GameObject("Arena").transform;
            float G = ArenaHalf;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(arena, false);
            ground.transform.position = new Vector3(0f, -0.2f, 0f);
            ground.transform.localScale = new Vector3(G * 2.3f, 0.4f, G * 2.3f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = dirt;
            ground.isStatic = true;

            var key = DamageLabSceneBuilder.CreateLight("Sun", LightType.Directional, new Color(1f, 0.88f, 0.72f), 1.7f, new Vector3(40f, -48f, 0f), LightShadows.Soft);
            key.shadowStrength = 0.85f;
            DamageLabSceneBuilder.CreateLight("Sky Fill", LightType.Directional, new Color(0.75f, 0.82f, 1f), 0.4f, new Vector3(30f, 140f, 0f), LightShadows.None);
            Color dust = new Color(0.74f, 0.64f, 0.52f);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.64f, 0.7f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.45f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.24f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = dust;
            RenderSettings.fogStartDistance = 85f;
            RenderSettings.fogEndDistance = 260f;
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(DamageLabAssets.Root + "/Art/Textures/StudioReflection.exr");
            if (cube != null) { RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom; RenderSettings.customReflectionTexture = cube; }

            // ---------------------------------------------------------------- perimeter and obstacles (kinematic Rigidbodies: the damage receiver only registers contacts with Rigidbodies)
            var obstacles = new GameObject("Obstacles").transform;
            float wallH = 2.2f, wallT = 1.2f;
            Wall("WallNorth", obstacles, new Vector3(0f, wallH / 2, G + wallT / 2), new Vector3(G * 2f + wallT * 2f, wallH, wallT), 0f, perimeter);
            Wall("WallSouth", obstacles, new Vector3(0f, wallH / 2, -G - wallT / 2), new Vector3(G * 2f + wallT * 2f, wallH, wallT), 0f, perimeter);
            Wall("WallEast", obstacles, new Vector3(G + wallT / 2, wallH / 2, 0f), new Vector3(wallT, wallH, G * 2f), 0f, perimeter);
            Wall("WallWest", obstacles, new Vector3(-G - wallT / 2, wallH / 2, 0f), new Vector3(wallT, wallH, G * 2f), 0f, perimeter);
            // corner wedges: no car gets pinned in a right-angle corner
            foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
                Wall("Corner", obstacles, new Vector3(sx * (G - 3.2f), wallH / 2, sz * (G - 3.2f)), new Vector3(10f, wallH, wallT), Mathf.Atan2(-sx, -sz) * Mathf.Rad2Deg, concrete);

            // the pinwheel in the middle: four barriers, each 7 m long, with 7 m gaps between their ends
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad;
                Block("Pinwheel", obstacles, new Vector3(Mathf.Sin(a) * 9f, 0.6f, Mathf.Cos(a) * 9f), new Vector3(7f, 1.2f, 1.2f), i * 90f, concrete);
            }
            // tyre stacks and hazard-striped barrier pairs towards the edges, clear of the spawn ring
            foreach (var (x, z) in new[] { (-26f, -26f), (26f, -26f), (-26f, 26f), (26f, 26f) })
            {
                Block("TyreStack", obstacles, new Vector3(x, 0.8f, z), new Vector3(2.4f, 1.6f, 2.4f), 45f, tyres);
                Block("TyreBand", obstacles, new Vector3(x, 1.65f, z), new Vector3(2.5f, 0.12f, 2.5f), 45f, hazard);
            }
            foreach (var (x, yaw) in new[] { (-36f, 90f), (36f, 90f) })
                Block("Barrier", obstacles, new Vector3(x, 0.55f, 0f), new Vector3(8f, 1.1f, 1f), yaw, concrete);

            // ---------------------------------------------------------------- scenery (no colliders): stands and floodlights
            float s = G + 8f;
            Scenery("StandsNorth", arena, new Vector3(0f, 1.3f, s + 4f), new Vector3(G * 2f + 16f, 2.6f, 8f), stands, G * 2f / 6f);
            Scenery("StandsNorthUpper", arena, new Vector3(0f, 3.6f, s + 9f), new Vector3(G * 2f + 16f, 2.6f, 8f), stands, G * 2f / 6f);
            Scenery("StandsSouth", arena, new Vector3(0f, 1.3f, -s - 4f), new Vector3(G * 2f + 16f, 2.6f, 8f), stands, G * 2f / 6f);
            Scenery("StandsSouthUpper", arena, new Vector3(0f, 3.6f, -s - 9f), new Vector3(G * 2f + 16f, 2.6f, 8f), stands, G * 2f / 6f);
            Scenery("StandsEast", arena, new Vector3(s + 4f, 1.3f, 0f), new Vector3(8f, 2.6f, G * 2f + 16f), stands, G * 2f / 6f);
            Scenery("StandsWest", arena, new Vector3(-s - 4f, 1.3f, 0f), new Vector3(8f, 2.6f, G * 2f + 16f), stands, G * 2f / 6f);
            foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                Scenery("Pole", arena, new Vector3(x * (G + 6f), 7f, z * (G + 6f)), new Vector3(0.6f, 14f, 0.6f), pole, 1f);
                Scenery("Lamp", arena, new Vector3(x * (G + 6f), 14.2f, z * (G + 6f)), new Vector3(3f, 0.6f, 1.6f), lamp, 1f);
            }

            // ---------------------------------------------------------------- spawn ring (children face the middle, alternately turned a little so the opening charges are not all head-on)
            var spawns = new GameObject("SpawnPoints").transform;
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + 45f * i) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(a) * 31f, 0f, Mathf.Cos(a) * 31f);
                var t = new GameObject("Spawn" + i).transform;
                t.SetParent(spawns, false);
                t.SetPositionAndRotation(p, Quaternion.Euler(0f, SpawnPlanner.FaceCentre(p) + (i % 2 == 0 ? 22f : -22f), 0f));
            }
            var menuSpawn = new GameObject("MenuSpawn").transform;
            menuSpawn.SetPositionAndRotation(new Vector3(0f, 0f, -24f), Quaternion.Euler(0f, 0f, 0f));

            // ---------------------------------------------------------------- vehicles, profiles, palette
            var vehicles = new[]
            {
                LoadDefinition(VehicleDefinitionSetup.MusclePath),
                LoadDefinition(VehicleDefinitionSetup.HatchPath),
                LoadDefinition(VehicleDefinitionSetup.Folder + "/CompactPickup.asset"),
            };
            var driveProfiles = new[] { Load<DriveProfile>("Assets/Driving/Profiles/Muscle.asset"), Load<DriveProfile>("Assets/Driving/Profiles/Hatchback.asset"), Load<DriveProfile>("Assets/Driving/Profiles/Pickup.asset") };
            var wire = AssetDatabase.LoadAssetAtPath<Material>(DamageLabAssets.MaterialFolder + "/Wireframe.mat") ?? DamageLabAssets.WireframeMaterial();
            var rigSettings = new VehicleRigSettings { bodyPhysicsMaterial = Load<PhysicsMaterial>("Assets/Driving/Art/CarDriving.physicMaterial"), wireframe = wire };
            var palette = PaintPaletteAsset();
            var aiProfiles = new[]
            {
                Load<DriverProfile>("Assets/Driving/Profiles/Drivers/Brawler.asset"), Load<DriverProfile>("Assets/Driving/Profiles/Drivers/Hunter.asset"),
                Load<DriverProfile>("Assets/Driving/Profiles/Drivers/Cautious.asset"), RookieProfile(),
            };

            // ---------------------------------------------------------------- camera, UI, game
            var camGO = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = dust;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = false;
            cd.antialiasing = AntialiasingMode.None;
            camGO.AddComponent<AudioListener>();
            var chase = camGO.AddComponent<ChaseCamera>();
            camGO.transform.position = menuSpawn.position + new Vector3(0f, 3.1f, -7.5f);
            camGO.transform.LookAt(menuSpawn.position + Vector3.up);

            var input = new GameObject("Driving Input").AddComponent<DriveInput>();
            var ui = new GameObject("Derby UI").AddComponent<DerbyUI>();
            ui.input = input;
            // The page's touch hook sends its message to an object called "DrivingGame" (the arena's name), so the derby manager carries the same name.
            var game = new GameObject("DrivingGame").AddComponent<DerbyGame>();
            game.vehicles = vehicles;
            game.driveProfiles = driveProfiles;
            game.rigSettings = rigSettings;
            game.palette = palette;
            game.aiProfiles = aiProfiles;
            game.arenaHalf = ArenaHalf;
            game.spawnPoints = spawns;
            game.menuSpawn = menuSpawn;
            game.chaseCamera = chase;
            game.input = input;
            game.ui = ui;
            game.participantCount = 5;
            ui.game = game;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            // build settings: the derby first (it is the default entry), then the existing scenes as they were
            var scenes = EditorBuildSettings.scenes.Where(e => e.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Derby] Built {ScenePath}: {vehicles.Length} vehicles, {palette.Count} paint colours, {spawns.childCount} spawn points, arena {G * 2f:0} m");
        }

        // ------------------------------------------------------------------ assets

        static PaintPalette PaintPaletteAsset()
        {
            const string path = SettingsFolder + "/PaintPalette.asset";
            var p = AssetDatabase.LoadAssetAtPath<PaintPalette>(path);
            if (p == null) { p = ScriptableObject.CreateInstance<PaintPalette>(); AssetDatabase.CreateAsset(p, path); }
            Color cream = new Color(0.95f, 0.93f, 0.86f), ink = new Color(0.1f, 0.1f, 0.12f);
            p.colors = new[]
            {
                new PaintPalette.Entry { name = "Crimson", body = new Color(0.80f, 0.07f, 0.09f), stripe = cream },
                new PaintPalette.Entry { name = "Cobalt", body = new Color(0.09f, 0.30f, 0.88f), stripe = cream },
                new PaintPalette.Entry { name = "Signal Yellow", body = new Color(0.98f, 0.80f, 0.04f), stripe = ink },
                new PaintPalette.Entry { name = "Lime", body = new Color(0.30f, 0.80f, 0.16f), stripe = cream },
                new PaintPalette.Entry { name = "Orange", body = new Color(0.98f, 0.44f, 0.03f), stripe = cream },
                new PaintPalette.Entry { name = "Violet", body = new Color(0.56f, 0.2f, 0.82f), stripe = cream },
                new PaintPalette.Entry { name = "Arctic White", body = new Color(0.92f, 0.93f, 0.95f), stripe = ink },
                new PaintPalette.Entry { name = "Hot Pink", body = new Color(0.96f, 0.22f, 0.58f), stripe = cream },
            };
            p.muscleStripeMask = MuscleStripeMask();
            EditorUtility.SetDirty(p);
            return p;
        }

        /// <summary>The muscle car's original stripe texture (blue with cream stripes) as a neutral mask: white where it was body, a darker grey where it was a stripe.</summary>
        static Texture2D MuscleStripeMask()
        {
            string src = DamageLabAssets.StripeTexturePath, dst = ArtFolder + "/MuscleStripeMask.png";
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(src));
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                bool stripe = px[i].r > 150 && px[i].g > 150;       // the stripe is near white, the body blue
                byte v = (byte)(stripe ? 78 : 255);
                px[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(dst, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceUpdate);
            var original = (TextureImporter)AssetImporter.GetAtPath(src);
            var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
            imp.wrapMode = original != null ? original.wrapMode : TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Bilinear;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        }

        static DriverProfile RookieProfile()
        {
            const string path = Root + "/Settings/Rookie.asset";
            var p = AssetDatabase.LoadAssetAtPath<DriverProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<DriverProfile>();
                p.displayName = "Rookie"; p.aggression = 0.6f; p.skill = 0.25f; p.combatIntelligence = 0.15f;
                p.description = "Keen but sloppy: charges often, lines up badly and misses a lot.";
                AssetDatabase.CreateAsset(p, path);
            }
            return p;
        }

        static T Load<T>(string path) where T : Object
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null) throw new System.Exception("Missing asset: " + path);
            return a;
        }

        static VehicleDefinition LoadDefinition(string path)
        {
            var d = AssetDatabase.LoadAssetAtPath<VehicleDefinition>(path);
            if (d == null || d.model == null) throw new System.Exception($"Vehicle definition missing or without a model: {path}");
            return d;
        }

        // ------------------------------------------------------------------ geometry helpers

        static GameObject Wall(string name, Transform parent, Vector3 pos, Vector3 size, float yawDeg, Material mat)
        {
            var go = Block(name, parent, pos, size, yawDeg, mat);
            return go;
        }

        static GameObject Block(string name, Transform parent, Vector3 pos, Vector3 size, float yawDeg, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<Rigidbody>().isKinematic = true;
            return go;
        }

        static void Scenery(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, float tileX)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        static Material Tiled(string name, Color color, float smoothness, Texture2D tex, float tiling)
        {
            var m = DamageLabAssets.LitMaterial(name, color, 0f, smoothness, null, tex);
            m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ generated textures

        static Texture2D EnsureTexture(string file, Texture2D generated, bool mips)
        {
            string path = $"{ArtFolder}/{file}";
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

        static float Hash(int x, int y, int period, int seed)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            unchecked { uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519u); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
        }

        static float Noise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, period, seed), b = Hash(xi + 1, yi, period, seed), c = Hash(xi, yi + 1, period, seed), d = Hash(xi + 1, yi + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        /// <summary>7 m tile of churned dirt: brown with large darker and lighter patches and fine grain, seamless.</summary>
        static Texture2D MakeDirt()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    float big = Noise(u * 4f, v * 4f, 4, 1), mid = Noise(u * 16f, v * 16f, 16, 2), fine = Noise(u * 64f, v * 64f, 64, 3);
                    float t = 0.5f * big + 0.3f * mid + 0.2f * fine;
                    // wheel ruts: a few darker streaks
                    float rut = Mathf.Abs(Mathf.Sin((u + 0.15f * big) * Mathf.PI * 6f));
                    t -= 0.12f * Mathf.Pow(1f - rut, 8f);
                    Color c = Color.Lerp(new Color(0.30f, 0.21f, 0.14f), new Color(0.56f, 0.42f, 0.29f), Mathf.Clamp01(t));
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Concrete barrier livery: red and white blocks (4 m per repeat) with a dark base line.</summary>
        static Texture2D MakeBarrier()
        {
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool red = x < w / 2;
                    Color c = red ? new Color(0.72f, 0.1f, 0.1f) : new Color(0.88f, 0.88f, 0.86f);
                    if (y < 8) c = Color.Lerp(c, new Color(0.15f, 0.15f, 0.16f), 0.75f);
                    if (x % (w / 2) < 2) c *= 0.7f;
                    px[y * w + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D MakeCrowd()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            var px = new Color32[n * n];
            var rnd = new System.Random(7);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool person = (x % 4 < 3) && (y % 4 < 3);
                    float r = (float)rnd.NextDouble();
                    Color c = person ? Color.HSVToRGB((float)rnd.NextDouble(), 0.35f + 0.4f * r, 0.45f + 0.4f * r) : new Color(0.12f, 0.12f, 0.14f);
                    px[y * n + x] = c;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
