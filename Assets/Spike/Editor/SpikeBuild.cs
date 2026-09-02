using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Spike.Editor
{
    /// <summary>
    /// M0 player builds, driven from the command line:
    ///   Unity.exe -batchmode -quit -projectPath . -buildTarget Win64   -executeMethod Spike.Editor.SpikeBuild.WindowsMono
    ///   Unity.exe -batchmode -quit -projectPath . -buildTarget Android -executeMethod Spike.Editor.SpikeBuild.AndroidIl2cpp
    /// </summary>
    public static class SpikeBuild
    {
        private static readonly string[] Scenes = { "Assets/Scenes/SampleScene.unity" };

        public static void WindowsMono()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            
            Run(new BuildPlayerOptions
            {
                scenes = Scenes,
                target = BuildTarget.StandaloneWindows64,
                locationPathName = "Builds/Windows/HttpMonitorSpike.exe",
                options = BuildOptions.Development,
            });
        }

        public static void AndroidIl2cpp()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            
            Run(new BuildPlayerOptions
            {
                scenes = Scenes,
                target = BuildTarget.Android,
                locationPathName = "Builds/Android/HttpMonitorSpike.apk",
                options = BuildOptions.Development,
            });
        }

        private static void Run(BuildPlayerOptions options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.locationPathName) ?? string.Empty);
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            
            Debug.Log($"[SpikeBuild] {summary.platform} {summary.result} in {summary.totalTime.TotalSeconds:F0}s, " +
                      $"{summary.totalErrors} error(s), output: {summary.outputPath}");

            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
