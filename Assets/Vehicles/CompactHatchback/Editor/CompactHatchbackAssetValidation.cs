using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CompactHatchback.EditorTools
{
    // Vehicle-scoped authoring checks; neither lab nor shared damage code is used.
    public static class CompactHatchbackAssetValidation
    {
        const string Root = "Assets/Vehicles/CompactHatchback";
        const string Model = Root + "/Models/CompactHatchback.fbx";
        const string PreviewRoot = "Previews/CompactHatchback/StageC";
        [Serializable] class PaletteEntry
        {
            public string name;
            public float[] color_linear, primary_srgb, secondary_srgb, emission_linear;
            public float metallic, smoothness, emission_strength;
        }
        [Serializable] class Palette { public PaletteEntry[] materials; }
        [Serializable] class Report
        {
            public string unityVersion, status;
            public float[] bounds_xyz_m;
            public string[] materialNames;
            public int meshCount, importedVertices;
            public bool readable, forwardPositiveZ, leftNegativeX, maskLinear;
            public bool independentPaintProperties, shaderCompiled, destructionTestsRun, renderedColorChanges;
            public bool additionalLightRendered;
        }
        [MenuItem("Assets/Compact Hatchback/Prepare and Validate")]
        public static void PrepareAndValidate()
        {
            try
            {
                Directory.CreateDirectory(PreviewRoot);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                string maskPath = Root + "/Textures/StripeMask.png";
                var textureImporter = (TextureImporter)AssetImporter.GetAtPath(maskPath);
                textureImporter.sRGBTexture = false;
                textureImporter.mipmapEnabled = false;
                textureImporter.wrapMode = TextureWrapMode.Clamp;
                textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                textureImporter.SaveAndReimport();
                var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                var palette = JsonUtility.FromJson<Palette>(File.ReadAllText(Root + "/palette.json"));
                var paintShader = Shader.Find("CarWreck/CompactHatchback/Two Color Paint");
                Require(paintShader != null, "Paint shader is missing");
                var materials = new Dictionary<string, Material>();
                foreach (var entry in palette.materials)
                {
                    string path = Root + "/Materials/" + entry.name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    // Keep user-edited colors on subsequent validation runs.
                    if (mat == null)
                    {
                        mat = new Material(entry.name == "Hatch_Paint" ? paintShader : Shader.Find("Universal Render Pipeline/Lit"));
                        mat.name = entry.name;
                        if (entry.name == "Hatch_Paint")
                        {
                            mat.SetColor("_PrimaryColor", Rgb(entry.primary_srgb).linear);
                            mat.SetColor("_StripeColor", Rgb(entry.secondary_srgb).linear);
                            mat.SetTexture("_StripeMask", mask);
                        }
                        else mat.SetColor("_BaseColor", new Color(entry.color_linear[0], entry.color_linear[1], entry.color_linear[2], 1));
                        mat.SetFloat("_Metallic", entry.metallic);
                        mat.SetFloat("_Smoothness", entry.smoothness);
                        AssetDatabase.CreateAsset(mat, path);
                    }
                    if (entry.emission_strength > 0)
                    {
                        mat.SetColor("_EmissionColor", new Color(entry.emission_linear[0],entry.emission_linear[1],entry.emission_linear[2]) * entry.emission_strength);
                        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
                        MaterialEditor.FixupEmissiveFlag(mat);
                        mat.EnableKeyword("_EMISSION");
                        EditorUtility.SetDirty(mat);
                    }
                    materials.Add(entry.name, mat);
                }
                AssetDatabase.SaveAssets();
                var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
                importer.isReadable = true;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.optimizeMeshVertices = false;
                importer.optimizeMeshPolygons = false;
                importer.weldVertices = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importCameras = false; importer.importLights = false;
                importer.importAnimation = false;
                importer.globalScale = 1; importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
                foreach (var pair in materials)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
                importer.SaveAndReimport();
                foreach (var entry in palette.materials.Where(e => e.emission_strength > 0))
                    Require(materials[entry.name].IsKeywordEnabled("_EMISSION") &&
                        (materials[entry.name].globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) == 0,
                        "Lamp emission did not persist: " + entry.name);
                AssetDatabase.SaveAssets();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
                Require(prefab != null, "FBX import failed");
                var instance = UnityEngine.Object.Instantiate(prefab);
                try
                {
                    var filters = instance.GetComponentsInChildren<MeshFilter>();
                    var renderers = instance.GetComponentsInChildren<MeshRenderer>();
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    Debug.Log("HATCHBACK_IMPORT_BOUNDS " + bounds.size.ToString("F4"));
                    foreach (var renderer in renderers.OrderByDescending(r => r.bounds.size.y).Take(6))
                        Debug.Log("HATCHBACK_PART_BOUNDS " + renderer.name + " size=" + renderer.bounds.size.ToString("F4") + " centre=" + renderer.bounds.center.ToString("F4"));
                    foreach (var renderer in renderers.OrderBy(r => r.bounds.min.y).Take(6))
                        Debug.Log("HATCHBACK_LOW_PART " + renderer.name + " min=" + renderer.bounds.min.ToString("F4") + " centre=" + renderer.bounds.center.ToString("F4"));
                    Transform Find(string name) => instance.GetComponentsInChildren<Transform>().Single(t => t.name == name);
                    Require(Find("Wheel_FL").position.z > Find("Wheel_RL").position.z, "Front wheels do not point +Z");
                    Require(Find("Wheel_FL").position.x < 0, "Left wheel does not lie at -X");
                    Require(Mathf.Abs(Vector3.Distance(Find("Wheel_FL").position, Find("Wheel_RL").position) - 2.32f) < .01f, "Wheelbase/scale mismatch");
                    Require(bounds.size.z > 3.8f && bounds.size.z < 4.1f && bounds.size.y > 1.35f && bounds.size.y < 1.5f, "Incorrect metre-scale bounds");
                    Require(filters.All(f => f.sharedMesh.isReadable && f.sharedMesh.normals.Length == f.sharedMesh.vertexCount && f.sharedMesh.uv2.Length == f.sharedMesh.vertexCount), "Missing readable mesh data, normals or stripe UV1");
                    Require(renderers.SelectMany(r => r.sharedMaterials).All(m => m != null && materials.Values.Contains(m)), "Missing material remap");
                    Require(!ShaderUtil.ShaderHasError(paintShader), "Paint shader has compilation errors");
                    var paint = materials["Hatch_Paint"];
                    Require(paint.HasProperty("_PrimaryColor") && paint.HasProperty("_StripeColor") && paint.GetTexture("_StripeMask") == mask, "Paint controls or mask missing");
                    RenderColorChecks(instance, paint);
                    Require(!ShaderUtil.ShaderHasError(paintShader), "Paint shader failed after rendering");
                    var report = new Report
                    {
                        unityVersion = Application.unityVersion, status = "PASS: asset import and paint setup",
                        bounds_xyz_m = new[] { bounds.size.x, bounds.size.y, bounds.size.z },
                        meshCount = filters.Length, importedVertices = filters.Sum(f => f.sharedMesh.vertexCount),
                        materialNames = materials.Keys.ToArray(), readable = true, forwardPositiveZ = true,
                        leftNegativeX = true, maskLinear = !textureImporter.sRGBTexture,
                        independentPaintProperties = true, shaderCompiled = true, destructionTestsRun = false, renderedColorChanges = true, additionalLightRendered = true
                    };
                    File.WriteAllText(PreviewRoot + "/unity_import_report.json", JsonUtility.ToJson(report, true));
                    Debug.Log("COMPACT_HATCHBACK_VALIDATION_PASS " + JsonUtility.ToJson(report));
                }
                finally { UnityEngine.Object.DestroyImmediate(instance); }
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }
        static Color Rgb(float[] c) => new Color(c[0], c[1], c[2], 1);
        static void RenderColorChecks(GameObject instance, Material paint)
        {
            Require(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "Validation must use URP");
            foreach (var t in instance.GetComponentsInChildren<Transform>()) t.gameObject.layer = 31;
            var temporaryPaint = new Material(paint);
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m == paint ? temporaryPaint : m).ToArray();
            var cameraObject = new GameObject("HatchbackValidationCamera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.14f,.17f);
            camera.orthographic = true; camera.orthographicSize = 2;
            camera.transform.position = new Vector3(-4.8f,3.1f,6.4f);
            camera.transform.LookAt(new Vector3(0,.7f,0));
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
            var lightObject = new GameObject("HatchbackValidationLight");
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2; light.cullingMask = 1 << 31;
            light.transform.rotation = Quaternion.Euler(45,-35,0);
            var target = new RenderTexture(960,640,24,RenderTextureFormat.ARGB32);
            var pointObject = new GameObject("HatchbackValidationPointLight");
            var point = pointObject.AddComponent<Light>(); point.type = LightType.Point;
            point.intensity = 25; point.range = 8; point.cullingMask = 1 << 31; point.enabled = false;
            point.transform.position = new Vector3(-2,1.6f,2.6f);
            target.Create();
            var oldAmbient = RenderSettings.ambientMode; var oldColor = RenderSettings.ambientLight;
            var oldActive = RenderTexture.active;
            bool oldAsyncCompilation = ShaderUtil.allowAsyncCompilation;
            try
            {
                RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.gray;
                ShaderUtil.allowAsyncCompilation = false;
                Color32[] Render(string filename)
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    var texture = new Texture2D(960,640,TextureFormat.RGB24,false);
                    texture.ReadPixels(new Rect(0,0,960,640),0,0); texture.Apply();
                    File.WriteAllBytes(PreviewRoot + "/" + filename + ".png", texture.EncodeToPNG());
                    var pixels = texture.GetPixels32(); UnityEngine.Object.DestroyImmediate(texture); return pixels;
                }
                temporaryPaint.SetColor("_PrimaryColor", new Color(.86f,.065f,.035f).linear);
                temporaryPaint.SetColor("_StripeColor", new Color(.94f,.93f,.88f).linear);
                var first = Render("unity_red_white");
                point.enabled = true;
                var pointLit = Render("unity_point_light");
                point.enabled = false;
                temporaryPaint.SetColor("_PrimaryColor", new Color(.055f,.24f,.62f).linear);
                var second = Render("unity_blue_white");
                temporaryPaint.SetColor("_StripeColor", new Color(.97f,.62f,.07f).linear);
                var third = Render("unity_blue_gold");
                int Changed(Color32[] a, Color32[] b) => a.Zip(b,(x,y) => Mathf.Abs(x.r-y.r)+Mathf.Abs(x.g-y.g)+Mathf.Abs(x.b-y.b)>20).Count(v => v);
                int primaryPixels = Changed(first,second), stripePixels = Changed(second,third), pointPixels = Changed(first,pointLit);
                Require(primaryPixels > 2000 && stripePixels > 300 && primaryPixels > stripePixels, "Independent paint colors did not affect expected rendered regions");
                Require(pointPixels > 1000, "Additional light did not affect the Forward+ render");
                Debug.Log($"HATCHBACK_PAINT_PIXEL_CHECK primary={primaryPixels} stripe={stripePixels} additional_light={pointPixels}");
            }
            finally
            {
                RenderTexture.active = oldActive;
                ShaderUtil.allowAsyncCompilation = oldAsyncCompilation;
                RenderSettings.ambientMode = oldAmbient; RenderSettings.ambientLight = oldColor;
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(pointObject);
                UnityEngine.Object.DestroyImmediate(temporaryPaint);
            }
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
