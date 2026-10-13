using System.Linq;
using DamageLab.Inspection;
using DamageLab.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// One-click construction of the VehicleDamageLab scene: neutral studio, lighting, the imported car with
    /// its damage model, inspection camera and the lab UI. Re-running rebuilds the scene from scratch.
    /// </summary>
    public static class DamageLabSceneBuilder
    {
        public const string ScenePath = DamageLabAssets.Root + "/Scenes/VehicleDamageLab.unity";

        [MenuItem("Damage Lab/Build Lab Scene", priority = 1)]
        public static void BuildMenu() => Build(false);

        [MenuItem("Damage Lab/Build Lab Scene (reset damage profile)", priority = 2)]
        public static void BuildResetMenu() => Build(true);

        public static void Build(bool resetProfile)
        {
            DamageLabAssets.EnsureFolders();
            DamageLabAssets.CreateSprites();
            DamageLabAssets.CreateCarMaterials();
            var wireMat = DamageLabAssets.WireframeMaterial();
            var profile = DamageLabAssets.DamageProfile(resetProfile);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(DamageLabAssets.ModelPath, ImportAssetOptions.ForceUpdate);
            ConfigurePipeline();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------------------------------------------------------------- studio
            var env = new GameObject("Environment").transform;
            var studioMat = DamageLabAssets.LitMaterial("Studio", new Color(0.36f, 0.365f, 0.375f), 0f, 0.22f);
            var platformMat = DamageLabAssets.LitMaterial("Platform", new Color(0.2f, 0.205f, 0.215f), 0.15f, 0.55f);
            var ringMat = DamageLabAssets.LitMaterial("PlatformRing", new Color(0.95f, 0.45f, 0.1f), 0f, 0.5f, new Color(1f, 0.42f, 0.08f) * 1.4f);
            var softboxMat = DamageLabAssets.LitMaterial("Softbox", Color.black, 0f, 0f, new Color(1f, 0.98f, 0.95f) * 3.2f);

            var bowlProfile = new[] { new Vector2(0.01f, 0f), new Vector2(4f, 0f), new Vector2(8f, 0f) }
                .Concat(Enumerable.Range(1, 12).Select(i =>
                {
                    float a = -90f + 90f * i / 12f;
                    return new Vector2(8f + 5f * Mathf.Cos(a * Mathf.Deg2Rad), 5f + 5f * Mathf.Sin(a * Mathf.Deg2Rad));
                }))
                .Concat(new[] { new Vector2(13f, 9f), new Vector2(13f, 14f) }).ToArray();
            var bowl = DamageLabAssets.SaveMesh(DamageLabAssets.OrientedLathe("StudioCyclorama", bowlProfile, 128, new Vector3(4f, 0f, 0f), Vector3.up), "StudioCyclorama");
            MeshObject("Cyclorama", env, bowl, studioMat, castShadows: false);

            var platformProfile = new[] { new Vector2(0.01f, 0.04f), new Vector2(1.5f, 0.04f), new Vector2(3.0f, 0.04f), new Vector2(3.05f, 0.03f), new Vector2(3.06f, 0.0f) };
            var platform = DamageLabAssets.SaveMesh(DamageLabAssets.OrientedLathe("InspectionPlatform", platformProfile, 128, new Vector3(1f, 0.04f, 0f), Vector3.up), "InspectionPlatform");
            MeshObject("InspectionPlatform", env, platform, platformMat, castShadows: false);
            var ring = DamageLabAssets.SaveMesh(DamageLabAssets.OrientedLathe("PlatformRing", new[] { new Vector2(2.9f, 0.0415f), new Vector2(2.96f, 0.0415f) }, 128, new Vector3(2.93f, 0.0415f, 0f), Vector3.up), "PlatformRing");
            MeshObject("PlatformRing", env, ring, ringMat, castShadows: false);

            // Overhead softboxes: they read as studio lights in the paint and chrome reflections.
            Softbox(env, softboxMat, new Vector3(0f, 7.5f, 0f), new Vector3(7f, 2.2f, 1f), new Vector3(-90f, 0f, 0f));
            Softbox(env, softboxMat, new Vector3(-5.5f, 6.0f, 3f), new Vector3(5f, 1.6f, 1f), new Vector3(-60f, 60f, 0f));
            Softbox(env, softboxMat, new Vector3(5.5f, 5.5f, -3f), new Vector3(5f, 1.4f, 1f), new Vector3(-60f, -120f, 0f));

            // ---------------------------------------------------------------- lighting
            var key = CreateLight("Key Light", LightType.Directional, new Color(1f, 0.97f, 0.93f), 1.7f, new Vector3(48f, 150f, 0f), LightShadows.Soft);
            key.shadowStrength = 0.82f;
            key.shadowNormalBias = 0.3f;
            CreateLight("Fill Light", LightType.Directional, new Color(0.82f, 0.88f, 1f), 0.55f, new Vector3(28f, -35f, 0f), LightShadows.None);
            CreateLight("Rim Light", LightType.Directional, new Color(1f, 1f, 1f), 0.45f, new Vector3(20f, 60f, 0f), LightShadows.None);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.58f, 0.6f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.21f);
            RenderSettings.fog = false;

            // ---------------------------------------------------------------- vehicle
            var turntableGO = new GameObject("VehicleTurntable");
            turntableGO.transform.position = new Vector3(0f, 0.04f, 0f);
            var turntable = turntableGO.AddComponent<ModelTurntable>();

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(DamageLabAssets.ModelPath);
            var car = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, turntableGO.transform);
            car.name = "MuscleCar";
            car.transform.localPosition = Vector3.zero;
            car.transform.localRotation = Quaternion.identity;
            var headlights = car.transform.Find("Headlights");
            if (headlights != null)
            {
                float z = headlights.GetComponent<Renderer>().bounds.center.z;
                // The damage directions are defined in the vehicle's local space, so the model must
                // import facing +Z with an identity root (do not fix orientation by rotating the root).
                if (z > 0) Debug.Log($"[DamageLab] Car import check: headlights at z={z:0.00}, front faces +Z (OK)");
                else Debug.LogError($"[DamageLab] Car imports facing -Z (headlights at z={z:0.00}); re-export it facing +Y from Blender.");
            }
            foreach (var r in car.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            var damage = car.AddComponent<VehicleDamageModel>();
            damage.profile = profile;
            damage.wireframeMaterial = wireMat;

            // ---------------------------------------------------------------- camera + post
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.3f, 0.31f, 0.32f);
            cam.allowHDR = true;
            cam.allowMSAA = true;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGO.AddComponent<AudioListener>();
            var camController = camGO.AddComponent<InspectionCameraController>();
            camController.SetView(camController.defaultYaw, camController.defaultPitch, camController.defaultDistance);

            var volumeGO = new GameObject("Post Processing");
            var volume = volumeGO.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = DamageLabAssets.LabVolumeProfile();

            // ---------------------------------------------------------------- reflections
            var probeGO = new GameObject("Studio Reflection Probe");
            probeGO.transform.position = new Vector3(0f, 2.5f, 0f);
            var probe = probeGO.AddComponent<ReflectionProbe>();
            probe.size = new Vector3(30f, 20f, 30f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.mode = ReflectionProbeMode.Baked;
            car.SetActive(false); // keep the car out of its own reflections
            string cubePath = DamageLabAssets.Root + "/Art/Textures/StudioReflection.exr";
            if (Lightmapping.BakeReflectionProbe(probe, cubePath))
            {
                AssetDatabase.ImportAsset(cubePath);
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(cubePath);
                probe.mode = ReflectionProbeMode.Custom;
                probe.customBakedTexture = cube;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
            }
            else Debug.LogWarning("[DamageLab] Reflection probe bake failed; using default reflections.");
            car.SetActive(true);

            // ---------------------------------------------------------------- UI
            DamageLabUIBuilder.Build(cam, damage, turntable, camController);
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log("[DamageLab] Built " + ScenePath);
        }

        static void ConfigurePipeline()
        {
            // The "Mobile" quality level is the default for WebGL builds: give it sharper soft shadows and MSAA.
            var mobile = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobile != null)
            {
                mobile.shadowDistance = 18f;
                mobile.msaaSampleCount = 2;
                mobile.renderScale = 1f;
                var mso = new SerializedObject(mobile);
                SetInt(mso, "m_MainLightShadowmapResolution", 2048);
                SetBool(mso, "m_SoftShadowsSupported", true);
                mso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(mobile);
            }
            var asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) { Debug.LogWarning("[DamageLab] No URP asset found."); return; }
            asset.shadowDistance = 22f;
            asset.msaaSampleCount = 4;
            var so = new SerializedObject(asset);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_ShadowCascadeCount", 2);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        static void Softbox(Transform parent, Material mat, Vector3 pos, Vector3 scale, Vector3 euler)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Softbox";
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.position = pos;
            q.transform.localScale = scale;
            q.transform.rotation = Quaternion.Euler(euler);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Light CreateLight(string name, LightType type, Color color, float intensity, Vector3 euler, LightShadows shadows)
        {
            var go = new GameObject(name);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.shadows = shadows;
            return l;
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
