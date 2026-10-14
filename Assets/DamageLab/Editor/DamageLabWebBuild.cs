using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Builds the lab for the browser (WebGL) into &lt;project&gt;/Builds/WebGL. Gzip with decompression
    /// fallback, so the build runs from any static host without special Content-Encoding headers.
    /// </summary>
    public static class DamageLabWebBuild
    {
        public const string OutputPath = "Builds/WebGL";

        [MenuItem("Damage Lab/Build WebGL", priority = 60)]
        public static void BuildMenu() => Build();

        public static void Build() => Build(new[] { DamageLabSceneBuilder.ScenePath }, OutputPath, "Vehicle Damage Lab");

        public const string CollisionOutputPath = "Builds/WebGLCollision";

        [MenuItem("Damage Lab/Build Collision Lab WebGL", priority = 61)]
        public static void BuildCollision() => Build(new[] { CollisionLabSceneBuilder.ScenePath }, CollisionOutputPath, "Vehicle Collision Lab");

        public static void Build(string[] scenes, string outputPath, string productName)
        {
            PlayerSettings.productName = productName;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false; // IndexedDB may be unavailable in sandboxed hosts
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[DamageLab] WebGL build {s.result}: {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:0}s -> {outputPath}");
            if (s.result != BuildResult.Succeeded) throw new System.Exception("WebGL build failed");
        }
    }
}
