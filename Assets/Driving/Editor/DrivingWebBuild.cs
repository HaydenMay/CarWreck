using System;
using System.Diagnostics;
using DamageLab.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CarWreck.Driving.EditorTools
{
    /// <summary>
    /// WebGL build into Builds/WebGLDrive: the CarWreck derby (DerbyArena, the default entry) plus the diagnostic driving arena (open it with ?scene=drive), using the project's own page template
    /// (Assets/WebGLTemplates/CarWreck: loading bar, touch-safe full-viewport canvas, rotate prompt).
    /// Gzip + decompression fallback, as the other lab builds, so it runs from any static host (GitHub Pages included).
    /// Run from the command line: Unity -batchmode -quit -projectPath . -executeMethod CarWreck.Driving.EditorTools.DrivingWebBuild.Build
    /// </summary>
    public static class DrivingWebBuild
    {
        public const string OutputPath = "Builds/WebGLDrive";

        [MenuItem("CarWreck/Build Driving WebGL", priority = 11)]
        public static void BuildMenu() => Build();

        public static void Build()
        {
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmm") + "-" + Git("rev-parse --short HEAD");
            PlayerSettings.productName = "CarWreck";
            PlayerSettings.companyName = "CarWreck";
            PlayerSettings.bundleVersion = id;
            PlayerSettings.WebGL.template = "PROJECT:CarWreck";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = false;          // IndexedDB may be unavailable in private / sandboxed browsers
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.initialMemorySize = 256;      // MB: avoids early growth hitches; mesh rebuilds allocate
            PlayerSettings.WebGL.maximumMemorySize = 1024;     // MB: stays within mobile browser limits
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.runInBackground = false;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { CarWreck.Derby.EditorTools.DerbySceneBuilder.ScenePath, DrivingSceneBuilder.ScenePath },   // the derby is the entry scene; the driving arena stays reachable with ?scene=drive
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[Driving] WebGL build {s.result} ({id}): {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:0}s -> {OutputPath}");
            if (s.result != BuildResult.Succeeded) throw new Exception("WebGL build failed");
        }

        static string Git(string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", args) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                string o = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                return string.IsNullOrEmpty(o) ? "nogit" : o;
            }
            catch { return "nogit"; }
        }
    }
}
