// Command-line builds and screenshots.
//   Unity -batchmode -quit -projectPath unity -executeMethod Halcyon.EditorTools.HalcyonBuild.BuildWindows
// builds Builds/Windows/Halcyon.exe. Run that with
//   Halcyon.exe -screen-width 1280 -screen-height 720 -halcyon-capture <dir>
// to render the standard viewpoints to PNGs and quit (see HalcyonGame).
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Halcyon.EditorTools
{
    public static class HalcyonBuild
    {
        [MenuItem("Halcyon/Build Windows Player")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Halcyon.exe");

        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "Builds/Mac/Halcyon.app");

        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/Halcyon.x86_64");

        static void Build(BuildTarget target, string path)
        {
            HalcyonProjectSetup.Run(false);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Halcyon.unity" },
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"Halcyon build {s.result}: {path}, {s.totalSize / 1e6:F0} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:F0} s");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
