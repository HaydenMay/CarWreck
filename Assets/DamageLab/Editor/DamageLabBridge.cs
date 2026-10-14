using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DamageLab.EditorTools
{
    /// <summary>
    /// Development hook for driving the open editor from outside (build scene, capture screenshots) while
    /// iterating on the deformation. Write commands, one per line, to Temp/DamageLabBridge/command.txt;
    /// results are written to Temp/DamageLabBridge/result.txt. Commands:
    ///   build [reset]            rebuild the VehicleDamageLab scene (optionally reset the damage profile)
    ///   reimport                 reimport the car FBX
    ///   capture-set              comparison screenshots
    ///   capture name f r l rt yaw pitch dist ui    one custom screenshot
    ///   refresh                  AssetDatabase.Refresh (recompiles scripts; runs last)
    /// </summary>
    [InitializeOnLoad]
    public static class DamageLabBridge
    {
        const string Dir = "Temp/DamageLabBridge";
        static double nextPoll;

        static DamageLabBridge()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 0.5;
            string cmdPath = Path.Combine(Dir, "command.txt");
            if (!File.Exists(cmdPath) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string[] lines;
            try { lines = File.ReadAllLines(cmdPath); File.Delete(cmdPath); }
            catch (IOException) { return; }

            var log = new StringBuilder();
            bool refresh = false;
            Application.LogCallback capture = (msg, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning || msg.StartsWith("[DamageLab]"))
                    log.AppendLine($"[{type}] {msg}");
            };
            Application.logMessageReceived += capture;
            try
            {
                foreach (var raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line == "refresh") { refresh = true; continue; }
                    log.AppendLine("> " + line);
                    try { Execute(line.Split(' ')); }
                    catch (Exception e) { log.AppendLine("ERROR " + e); }
                }
            }
            finally
            {
                Application.logMessageReceived -= capture;
            }
            log.AppendLine("DONE");
            File.WriteAllText(Path.Combine(Dir, "result.txt"), log.ToString());
            if (refresh) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>
        /// Batch-mode entry point: Unity.exe -batchmode -projectPath . -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "build;capture-set" -quit
        /// </summary>
        public static void RunFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-dlcmd");
            if (i < 0 || i + 1 >= args.Length) { Debug.LogError("[DamageLab] missing -dlcmd"); return; }
            foreach (var cmd in args[i + 1].Split(';'))
            {
                var c = cmd.Trim();
                if (c.Length == 0) continue;
                Debug.Log("[DamageLab] > " + c);
                Execute(c.Split(' '));
            }
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Execute(string[] a)
        {
            switch (a[0])
            {
                case "build":
                    DamageLabSceneBuilder.Build(a.Length > 1 && a[1] == "reset");
                    break;
                case "reimport":
                    AssetDatabase.ImportAsset(DamageLabAssets.ModelPath, ImportAssetOptions.ForceUpdate);
                    break;
                case "webgl":
                    DamageLabWebBuild.Build();
                    break;
                case "verify":
                    Debug.Log(DamageLabValidation.Run());
                    break;
                case "impact-set":
                    DamageLabCapture.CaptureImpactSet();
                    break;
                case "stress-set":
                    DamageLabCapture.CaptureStressSet();
                    break;
                case "collision-build":
                    CollisionLabSceneBuilder.Build();
                    break;
                case "collision-verify":
                    Debug.Log(CollisionLabValidation.Run());
                    break;
                case "collision-geometry":
                    Debug.Log(CollisionGeometryValidation.Run());
                    break;
                case "collision-webgl":
                    DamageLabWebBuild.BuildCollision();
                    break;
                case "verify-stress":
                    Debug.Log(DamageLabValidation.RunStress());
                    break;
                case "verify-impacts":
                    Debug.Log(DamageLabValidation.RunImpacts());
                    break;
                case "capture-set":
                    DamageLabCapture.CaptureSet(DamageLabCapture.ComparisonSet, 145f, 14f, 7.6f, true, "");
                    break;
                case "capture":
                    DamageLabCapture.CaptureSet(new[] { new DamageLabCapture.Shot(a[1], F(a[2]), F(a[3]), F(a[4]), F(a[5])) },
                        F(a[6]), F(a[7]), F(a[8]), a.Length > 9 && a[9] == "1", "", 1600, 900, a.Length > 10 && a[10] == "1");
                    break;
                default:
                    throw new ArgumentException("Unknown command " + a[0]);
            }
        }
    }
}
