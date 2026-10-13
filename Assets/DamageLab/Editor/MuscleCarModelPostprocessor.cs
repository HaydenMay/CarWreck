using UnityEditor;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Import settings for the Blender-exported vehicles: readable meshes (runtime deformation needs CPU
    /// access), imported normals, no compression, and material remaps onto the lab's URP materials.
    /// </summary>
    public class MuscleCarModelPostprocessor : AssetPostprocessor
    {
        public const string ModelFolder = "Assets/DamageLab/Art/Models";

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelFolder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.isReadable = true;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.optimizeMeshVertices = false; // keep Blender's vertex order (stable precompute)
            importer.weldVertices = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;

            foreach (var name in DamageLabAssets.CarMaterialNames)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>($"{DamageLabAssets.MaterialFolder}/{name}.mat");
                if (mat != null) importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), mat);
            }
        }
    }
}
