using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LivingDiorama.EditorTools
{
    /// <summary>
    /// Android builds, driven from the menu or from the command line so CI and a laptop
    /// produce byte-for-byte the same thing.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . \
    ///     -executeMethod LivingDiorama.EditorTools.AndroidBuilder.BuildFromCommandLine \
    ///     -outputPath Build/LivingDiorama.apk [-aab] [-release]
    /// </summary>
    public static class AndroidBuilder
    {
        const string DefaultApk = "Build/LivingDiorama.apk";

        [MenuItem("Living Diorama/Build Android APK", priority = 20)]
        public static void BuildApkFromMenu() => Build(DefaultApk, aab: false, release: false);

        [MenuItem("Living Diorama/Build Android AAB (release)", priority = 21)]
        public static void BuildAabFromMenu() => Build("Build/LivingDiorama.aab", aab: true, release: true);

        public static void BuildFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = ArgValue(args, "-outputPath") ?? DefaultApk;
            bool aab = args.Contains("-aab");
            bool release = args.Contains("-release");

            BuildReport report = Build(output, aab, release);

            // Batch mode has to exit with a failing code or CI will happily publish nothing.
            if (report == null || report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        static string ArgValue(string[] args, string flag)
        {
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        static BuildReport Build(string outputPath, bool aab, bool release)
        {
            // Content and project settings are generated, so a build always regenerates
            // them first. A build that silently used stale content would be worse than
            // a slow one.
            ProjectConfigurator.RebuildEverything();

            EditorUserBuildSettings.buildAppBundle = aab;
            EditorUserBuildSettings.androidBuildType = release
                ? AndroidBuildType.Release
                : AndroidBuildType.Development;

            EditorUserBuildSettings.development = !release;
            EditorUserBuildSettings.allowDebugging = false;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[AndroidBuilder] no scenes are enabled in the build settings");
                return null;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = release ? BuildOptions.None : BuildOptions.Development,
            };

            Debug.Log($"[AndroidBuilder] building {(aab ? "AAB" : "APK")} " +
                      $"({(release ? "release" : "development")}) -> {outputPath}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AndroidBuilder] succeeded: {summary.totalSize / (1024 * 1024)} MB " +
                          $"in {summary.totalTime.TotalSeconds:0}s -> {summary.outputPath}");
            }
            else
            {
                Debug.LogError($"[AndroidBuilder] {summary.result}: " +
                               $"{summary.totalErrors} error(s)");
            }

            return report;
        }
    }
}
