using System.Collections.Generic;
using System.Linq;
using DamageLab.CollisionTest;
using DamageLab.Deformation;
using DamageLab.Inspection;
using DamageLab.UI;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using static DamageLab.EditorTools.DamageLabUIBuilder;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Builds the VehicleCollisionLab scene: flat arena, the muscle car as a dynamic Rigidbody with simple box
    /// colliders, an orange physics test sled, an orbit camera and a small control panel. Reuses the Damage Lab's
    /// materials, post-processing and UI widgets; vehicles come from their definitions (VehicleRig). The Damage Lab scene is not touched.
    /// </summary>
    public static class CollisionLabSceneBuilder
    {
        public const string ScenePath = DamageLabAssets.Root + "/Scenes/VehicleCollisionLab.unity";

        [MenuItem("Damage Lab/Build Collision Lab Scene", priority = 3)]
        public static void Build()
        {
            DamageLabAssets.EnsureFolders();
            DamageLabAssets.CreateSprites();
            var wireMat = AssetDatabase.LoadAssetAtPath<Material>(DamageLabAssets.MaterialFolder + "/Wireframe.mat") ?? DamageLabAssets.WireframeMaterial();
            DamageLabSceneBuilder.ConfigurePipeline();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------------- arena
            var groundMat = DamageLabAssets.LitMaterial("ArenaGround", new Color(0.33f, 0.335f, 0.345f), 0f, 0.25f);
            var lineMat = DamageLabAssets.LitMaterial("ArenaLine", new Color(0.95f, 0.45f, 0.1f), 0f, 0.4f);
            var sledMat = DamageLabAssets.LitMaterial("TestSled", new Color(0.96f, 0.45f, 0.08f), 0.1f, 0.45f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(80f, 0.2f, 80f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
            ground.isStatic = true;
            // Painted ring around the test position (visual only).
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f;
                var dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = "Marking";
                Object.DestroyImmediate(dash.GetComponent<Collider>());
                dash.transform.SetParent(ground.transform.parent, false);
                dash.transform.position = new Vector3(Mathf.Cos(a) * 6.5f, 0.003f, Mathf.Sin(a) * 6.5f);
                dash.transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                dash.transform.localScale = new Vector3(0.12f, 0.006f, 0.5f);
                dash.GetComponent<MeshRenderer>().sharedMaterial = lineMat;
            }

            var key = DamageLabSceneBuilder.CreateLight("Key Light", LightType.Directional, new Color(1f, 0.97f, 0.93f), 1.7f, new Vector3(50f, 150f, 0f), LightShadows.Soft);
            key.shadowStrength = 0.8f;
            DamageLabSceneBuilder.CreateLight("Fill Light", LightType.Directional, new Color(0.82f, 0.88f, 1f), 0.55f, new Vector3(28f, -35f, 0f), LightShadows.None);
            DamageLabSceneBuilder.CreateLight("Rim Light", LightType.Directional, Color.white, 0.4f, new Vector3(20f, 60f, 0f), LightShadows.None);
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.6f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.21f);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(DamageLabAssets.Root + "/Art/Textures/StudioReflection.exr");
            if (cube != null)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
            }

            // ---------------------------------------------------------------- vehicles (built from their definitions)
            var definitions = VehicleDefinitionSetup.Build();
            var rigSettings = new VehicleRigSettings
            {
                bodyPhysicsMaterial = PhysicsMat("CarTyres", 0.55f, 0.7f, PhysicsMaterialCombine.Average),
                wireframe = wireMat,
                footprintOutline = UnlitMaterial("FootprintOutline", new Color(0.1f, 0.95f, 1f)),
                footprintArrow = UnlitMaterial("FootprintArrow", new Color(1f, 0.85f, 0.1f)),
                hitboxLines = UnlitMaterial("PhysicsHitbox", new Color(0.2f, 1f, 0.3f)),
            };
            var rig = VehicleRig.Build(definitions[0], null, Vector3.zero, rigSettings);
            rig.footprintDebug.show = true;

            // ---------------------------------------------------------------- test sled
            var sledGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sledGO.name = "TestSled";
            sledGO.GetComponent<MeshRenderer>().sharedMaterial = sledMat;
            sledGO.transform.localScale = Vector3.one;
            var sledBox = sledGO.GetComponent<BoxCollider>();
            sledBox.size = new Vector3(1.9f, 1.1f, 3.8f);          // vehicle-sized block
            sledGO.GetComponent<MeshFilter>().sharedMesh = DamageLabAssets.SaveMesh(BoxMesh(sledBox.size), "TestSledBox");
            sledBox.sharedMaterial = PhysicsMat("TestSled", 0.3f, 0.35f, PhysicsMaterialCombine.Average);
            var sled = sledGO.AddComponent<Rigidbody>();
            sled.mass = 1500f;
            sled.useGravity = false;                                // rides on a frozen height: no ground friction
            sled.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            sled.interpolation = RigidbodyInterpolation.Interpolate;
            sled.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            sled.linearDamping = 0f;
            sled.isKinematic = true;

            // ---------------------------------------------------------------- camera
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.31f, 0.32f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGO.AddComponent<AudioListener>();
            var camController = camGO.AddComponent<InspectionCameraController>();
            camController.pivot = new Vector3(0f, 0.6f, 1.5f);
            camController.defaultYaw = 130f;
            camController.defaultPitch = 22f;
            camController.defaultDistance = 17f;
            camController.maxDistance = 40f;
            camController.panLimit = 10f;
            camController.SetView(camController.defaultYaw, camController.defaultPitch, camController.defaultDistance);

            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DamageLabAssets.VolumePath);

            // ---------------------------------------------------------------- lab + UI
            var lab = new GameObject("CollisionLab").AddComponent<CollisionLabController>();
            lab.sled = sled;
            lab.cameraController = camController;
            lab.vehicles = definitions;
            lab.rigSettings = rigSettings;
            lab.AdoptRig(rig, 0);
            BuildUI(cam, lab, camController);
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // Park the sled for the default direction so the scene looks right before Play.
            lab.Aim(lab.direction, out Vector3 t, out Vector3 d);
            sledGO.transform.SetPositionAndRotation(new Vector3(t.x, lab.sledHeight + rig.geometry.groundLift, t.z) - d * (lab.startGap + 1.9f), Quaternion.LookRotation(d));

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[CollisionLab] Built {ScenePath} with {definitions.Length} vehicles; default {definitions[0].displayName}: " +
                      $"{rig.hitbox.regions.Length} adaptive regions, body {rig.geometry.body.size.ToString("F3")}, lifted {rig.geometry.groundLift:0.000} m; warnings: {string.Join(" | ", rig.warnings)}");
        }

        static PhysicsMaterial PhysicsMat(string name, float dynamicFriction, float staticFriction, PhysicsMaterialCombine combine)
        {
            string path = $"{DamageLabAssets.SettingsFolder}/{name}.physicMaterial";
            var m = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (m == null) { m = new PhysicsMaterial(name); AssetDatabase.CreateAsset(m, path); }
            m.dynamicFriction = dynamicFriction;
            m.staticFriction = staticFriction;
            m.bounciness = 0f;
            m.frictionCombine = combine;
            m.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material UnlitMaterial(string name, Color color)
        {
            string path = $"{DamageLabAssets.MaterialFolder}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh BoxMesh(Vector3 size)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var src = tmp.GetComponent<MeshFilter>().sharedMesh;
            var m = Object.Instantiate(src);
            Object.DestroyImmediate(tmp);
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], size);
            m.vertices = v;
            m.RecalculateBounds();
            m.name = "TestSledBox";
            return m;
        }

        // ------------------------------------------------------------------ UI

        const float PanelWidth = 470f;

        static void BuildUI(Camera cam, CollisionLabController lab, InspectionCameraController camController)
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGO = new GameObject("CollisionCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 0.5f;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            var root = canvasGO.transform;

            // Right panel
            var panel = Img("CollisionPanel", root, PanelBg, null);
            var prt = panel.rectTransform;
            prt.anchorMin = new Vector2(1, 0); prt.anchorMax = new Vector2(1, 1); prt.pivot = new Vector2(1, 0.5f);
            prt.sizeDelta = new Vector2(PanelWidth, 0); prt.anchoredPosition = Vector2.zero;
            var header = Img("Header", panel.transform, Hex("202226"), null);
            Stretch(header.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 96));
            var icon = Img("Icon", header.transform, Accent, DamageLabAssets.GetSprite("IconCar"));
            Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 0), new Vector2(54, 54), new Vector2(0, 0.5f));
            var underline = Img("Underline", header.transform, Accent, null);
            Place(underline.rectTransform, new Vector2(0, 0), new Vector2(16, 0), new Vector2(72, 4), new Vector2(0, 0));
            var title = Label("Title", header.transform, $"Vehicle <color=#{ColorUtility.ToHtmlStringRGB(Accent)}>Collision Lab</color>", 32, TextMain, FontStyle.Bold, TextAnchor.MiddleLeft);
            Place(title.rectTransform, new Vector2(0, 0.5f), new Vector2(96, 14), new Vector2(360, 44), new Vector2(0, 0.5f));
            var sub = Label("Subtitle", header.transform, "P H Y S I C S    I M P A C T    T E S T", 13, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Place(sub.rectTransform, new Vector2(0, 0.5f), new Vector2(98, -20), new Vector2(360, 22), new Vector2(0, 0.5f));

            var viewport = new GameObject("Scroll", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(panel.transform, false);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(16, 0); viewport.offsetMax = new Vector2(-16, -108);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10; vlg.childControlHeight = true; vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            vlg.padding = new RectOffset(0, 0, 0, 14);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content; scroll.viewport = viewport; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;

            // Vehicle selector + info (developer tool)
            var vehCard = AutoCard("Vehicle", content);
            var vh = Label("Heading", vehCard.transform, "Vehicle", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(vh.gameObject, 28);
            var vgrid = new GameObject("VehicleButtons", typeof(RectTransform)).GetComponent<RectTransform>();
            vgrid.SetParent(vehCard.transform, false);
            var vgl = vgrid.gameObject.AddComponent<GridLayoutGroup>();
            vgl.cellSize = new Vector2(196, 44); vgl.spacing = new Vector2(8, 8);
            vgl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; vgl.constraintCount = 2;
            int vrows = Mathf.Max(1, (lab.vehicles.Length + 1) / 2);
            Layout(vgrid.gameObject, 44 * vrows + 8 * (vrows - 1));
            lab.vehicleButtons = new Button[lab.vehicles.Length];
            for (int i = 0; i < lab.vehicles.Length; i++)
            {
                var bg = Img("Vehicle" + i, vgrid, Hex("33373C"), DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.colors = ButtonColors();
                var l = Label("Label", bg.transform, lab.vehicles[i].displayName, 16, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                lab.vehicleButtons[i] = b;
            }
            lab.vehicleInfoText = Label("Info", vehCard.transform, "", 14, TextMain, FontStyle.Normal, TextAnchor.UpperLeft);
            lab.vehicleInfoText.supportRichText = false;
            Layout(lab.vehicleInfoText.gameObject, 118);

            // Speed + mass
            var sliders = AutoCard("TestSetup", content);
            var speedRow = Row(sliders.transform, "SpeedRow", 80);
            SliderWidgets(speedRow, "Impact Speed (m/s)", out lab.speedSlider, out lab.speedField);
            lab.speedMphText = Label("Mph", sliders.transform, "", 14, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Layout(lab.speedMphText.gameObject, 22);
            var massRow = Row(sliders.transform, "MassRow", 80);
            SliderWidgets(massRow, "Impacting Mass (kg)", out lab.massSlider, out lab.massField);
            lab.massField.characterLimit = 4;
            lab.speedSlider.minValue = 0f; lab.speedSlider.maxValue = 30f; lab.speedSlider.value = lab.speed;
            lab.massSlider.minValue = 100f; lab.massSlider.maxValue = 4000f; lab.massSlider.value = lab.mass;
            foreach (var t in massRow.GetComponentsInChildren<Text>()) if (t.name == "Max") t.text = "4000";
            foreach (var t in massRow.GetComponentsInChildren<Text>()) if (t.name == "Min") t.text = "100";
            foreach (var t in speedRow.GetComponentsInChildren<Text>()) if (t.name == "Max") t.text = "30";
            var offsetRow = Row(sliders.transform, "OffsetRow", 80);
            SliderWidgets(offsetRow, "Aim Offset (m)", out lab.offsetSlider, out lab.offsetField);
            lab.offsetSlider.wholeNumbers = false; lab.offsetSlider.minValue = -1.5f; lab.offsetSlider.maxValue = 1.5f; lab.offsetSlider.value = 0f;
            lab.offsetField.contentType = InputField.ContentType.DecimalNumber; lab.offsetField.characterLimit = 5;
            lab.offsetField.text = "0.00";
            foreach (var t in offsetRow.GetComponentsInChildren<Text>()) if (t.name == "Max") t.text = "+1.5"; else if (t.name == "Min") t.text = "-1.5";
            var offsetHint = Label("OffsetHint", sliders.transform, "Front / rear / corner: + towards car's right  ·  Sides: + towards front", 14, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Layout(offsetHint.gameObject, 22);

            // Direction
            var dirCard = AutoCard("Direction", content);
            var dh = Label("Heading", dirCard.transform, "Impact Direction", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(dh.gameObject, 28);
            var grid = new GameObject("Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            grid.SetParent(dirCard.transform, false);
            var gl = grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = new Vector2(196, 44); gl.spacing = new Vector2(8, 8);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 2;
            Layout(grid.gameObject, 44 * 4 + 24);
            // Index = ImpactDirection value; the angled corners are listed in enum order (front-left, rear-right, front-right, rear-left).
            string[] names = { "Front", "Rear", "Left Side", "Right Side", "Front-Left Corner", "Rear-Right Corner", "Front-Right Corner", "Rear-Left Corner" };
            lab.directionButtons = new Button[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var bg = Img(names[i].Replace(" ", ""), grid, Hex("33373C"), DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.colors = ButtonColors();
                var l = Label("Label", bg.transform, names[i], 16, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                lab.directionButtons[i] = b;
            }
            var ih = Label("ImpactorHeading", dirCard.transform, "Impactor", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(ih.gameObject, 30);
            var igrid = new GameObject("ImpactorButtons", typeof(RectTransform)).GetComponent<RectTransform>();
            igrid.SetParent(dirCard.transform, false);
            var igl = igrid.gameObject.AddComponent<GridLayoutGroup>();
            igl.cellSize = new Vector2(196, 44); igl.spacing = new Vector2(8, 8);
            igl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; igl.constraintCount = 2;
            Layout(igrid.gameObject, 44);
            string[] inames = { "Wide Sled 1.9 m", "Narrow Block 0.5 m" };
            lab.impactorButtons = new Button[inames.Length];
            for (int i = 0; i < inames.Length; i++)
            {
                var bg = Img(i == 0 ? "Wide" : "Narrow", igrid, Hex("33373C"), DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
                var b = bg.gameObject.AddComponent<Button>();
                b.targetGraphic = bg;
                b.colors = ButtonColors();
                var l = Label("Label", bg.transform, inames[i], 16, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
                Stretch(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                lab.impactorButtons[i] = b;
            }
            lab.footprintToggle = CheckToggle(Row(dirCard.transform, "FootprintRow", 42), "Show Contact Footprint");
            lab.footprintToggle.isOn = true;
            lab.anchorToggle = CheckToggle(Row(dirCard.transform, "AnchorRow", 42), "Anchor Vehicle");
            lab.anchorToggle.isOn = lab.anchored;
            lab.collidersToggle = CheckToggle(Row(dirCard.transform, "CollidersRow", 42), "Show Physics Colliders");
            lab.collidersToggle.isOn = false;

            // Launch / Reset
            lab.launchButton = BigButton(content, "LaunchButton", "LAUNCH", Accent, Color.white, 58, "ArrowRight");
            var resetRow = new GameObject("ResetButtons", typeof(RectTransform)).GetComponent<RectTransform>();
            resetRow.SetParent(content, false);
            var rgl = resetRow.gameObject.AddComponent<GridLayoutGroup>();
            rgl.cellSize = new Vector2(140, 44); rgl.spacing = new Vector2(8, 8);
            rgl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; rgl.constraintCount = 3;
            Layout(resetRow.gameObject, 44);
            lab.resetBlockButton = SmallButton(resetRow, "ResetBlockButton", "RESET BLOCK");
            lab.repairButton = SmallButton(resetRow, "RepairCarButton", "REPAIR CAR");
            lab.resetButton = SmallButton(resetRow, "ResetAllButton", "RESET ALL");
            lab.statusText = Label("Status", content, "Ready", 15, TextDim, FontStyle.Normal, TextAnchor.MiddleCenter);
            Layout(lab.statusText.gameObject, 24);

            // Random testing: one random block, or a seeded Chaos Run of 10 / 25 / 50 blocks (never repairs the car)
            var rndCard = AutoCard("RandomTesting", content);
            var rh = Label("Heading", rndCard.transform, "Random Testing", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(rh.gameObject, 28);
            RectTransform Grid(string name, Vector2 cell)
            {
                var g = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                g.SetParent(rndCard.transform, false);
                var gl = g.gameObject.AddComponent<GridLayoutGroup>();
                gl.cellSize = cell; gl.spacing = new Vector2(8, 8);
                gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount; gl.constraintCount = 3;
                Layout(g.gameObject, cell.y);
                return g;
            }
            var actions = Grid("RandomButtons", new Vector2(128, 44));
            lab.randomLaunchButton = SmallButton(actions, "RandomLaunchButton", "RANDOM LAUNCH");
            lab.chaosButton = SmallButton(actions, "ChaosRunButton", "CHAOS RUN");
            lab.stopButton = SmallButton(actions, "StopButton", "STOP");
            var ch = Label("ChaosCountHeading", rndCard.transform, "Chaos run impacts", 15, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Layout(ch.gameObject, 24);
            var counts = Grid("ChaosCounts", new Vector2(128, 38));
            lab.chaosCountButtons = new Button[lab.chaosCounts.Length];
            for (int i = 0; i < lab.chaosCounts.Length; i++) lab.chaosCountButtons[i] = SmallButton(counts, "Count" + lab.chaosCounts[i], lab.chaosCounts[i].ToString());
            lab.seedText = ch;   // "Chaos run impacts · next shot N" (kept up to date by the lab)
            var seedRow = Grid("SeedRow", new Vector2(196, 38));
            seedRow.GetComponent<GridLayoutGroup>().constraintCount = 2;
            // Editable seed (the same seed replays the same random selections), next to NEW SEED.
            var seedBg = Img("SeedField", seedRow, Well, DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            var seedLabel = Label("Text", seedBg.transform, lab.randomSeed.ToString(), 17, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(seedLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            seedLabel.supportRichText = false;
            lab.seedField = seedBg.gameObject.AddComponent<InputField>();
            lab.seedField.textComponent = seedLabel;
            lab.seedField.contentType = InputField.ContentType.IntegerNumber;
            lab.seedField.characterLimit = 6;
            lab.seedField.targetGraphic = seedBg;
            lab.seedField.text = lab.randomSeed.ToString();
            lab.seedField.caretColor = Accent;
            lab.seedField.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.4f);
            lab.newSeedButton = SmallButton(seedRow, "NewSeedButton", "NEW SEED");
            lab.varyWidthToggle = CheckToggle(Row(rndCard.transform, "VaryWidthRow", 42), "Random Block Width");
            lab.varyWidthToggle.isOn = lab.randomOptions.varyWidth;

            // Diagnostics
            var diag = AutoCard("Diagnostics", content);
            var dgh = Label("Heading", diag.transform, "Last Collision", 17, TextDim, FontStyle.Bold, TextAnchor.MiddleLeft);
            Layout(dgh.gameObject, 28);
            lab.diagnosticsText = Label("Text", diag.transform, "No collision yet.", 15, TextMain, FontStyle.Normal, TextAnchor.UpperLeft);
            lab.diagnosticsText.supportRichText = false;
            Layout(lab.diagnosticsText.gameObject, 450);

            // Controls hint (top-left)
            var hint = Img("ControlsHint", root, new Color(0.11f, 0.12f, 0.13f, 0.86f), DamageLabAssets.RoundedRectSprite, sliced: true);
            Place(hint.rectTransform, new Vector2(0, 1), new Vector2(20, -20), new Vector2(330, 74), new Vector2(0, 1));
            var hintText = Label("Text", hint.transform, "Orbit: drag  ·  Zoom: scroll / pinch\nPan: middle drag / two fingers", 15, TextDim, FontStyle.Normal, TextAnchor.MiddleLeft);
            Stretch(hintText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(6, 0), new Vector2(-28, -10));

            var responsive = canvasGO.AddComponent<ResponsiveLabLayout>();
            responsive.scaler = scaler;
            responsive.sidePanel = prt;
            responsive.cameraController = camController;
            responsive.hintTexts = new Text[0];
        }

        static Image AutoCard(string name, Transform parent)
        {
            var card = CardBox(name, parent, 0);
            var le = card.GetComponent<LayoutElement>();
            le.minHeight = -1; le.preferredHeight = -1;
            var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(18, 18, 10, 12); v.spacing = 4;
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return card;
        }

        static Button BigButton(Transform parent, string name, string text, Color bgColor, Color textColor, float height, string iconSprite)
        {
            var bg = Img(name, parent, bgColor, DamageLabAssets.RoundedRectSprite, sliced: true);
            Layout(bg.gameObject, height);
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.colors = ButtonColors();
            var ic = Img("Icon", bg.transform, textColor == Color.white ? Color.white : Accent, DamageLabAssets.GetSprite(iconSprite));
            Place(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-70, 0), new Vector2(28, 28));
            var l = Label("Label", bg.transform, text, 22, textColor, FontStyle.Bold, TextAnchor.MiddleCenter);
            Place(l.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(12, 0), new Vector2(200, 40));
            return b;
        }


        static Button SmallButton(Transform parent, string name, string text)
        {
            var bg = Img(name, parent, Hex("33373C"), DamageLabAssets.GetSprite("RoundedRectSmall"), sliced: true);
            var b = bg.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.colors = ButtonColors();
            var l = Label("Label", bg.transform, text, 15, TextMain, FontStyle.Bold, TextAnchor.MiddleCenter);
            Stretch(l.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return b;
        }
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
