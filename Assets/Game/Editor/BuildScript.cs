using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CK.Editor
{
    /// <summary>
    /// Windows x64 player builds. DX11-only is enforced by ProjectSetup before every
    /// build, and Config/CombatPrototype.json is copied into StreamingAssets.
    /// </summary>
    public static class BuildScript
    {
        private const string MonoOutput = "Build/Win64-Mono/ChickenKiller.exe";
        private const string Il2CppOutput = "Build/Win64/ChickenKiller.exe";

        [MenuItem("Tools/Prototype/Build/Windows64 Mono (M0 smoke)", priority = 40)]
        public static void BuildMono()
        {
            Build(ScriptingImplementation.Mono2x, BuildOptions.Development, MonoOutput);
        }

        [MenuItem("Tools/Prototype/Build/Windows64 IL2CPP (M7)", priority = 41)]
        public static void BuildIl2Cpp()
        {
            Build(ScriptingImplementation.IL2CPP, BuildOptions.None, Il2CppOutput);
        }

        /// <summary>Entry point for -executeMethod BuildScript.BuildIl2CppCli.</summary>
        public static void BuildIl2CppCli()
        {
            Build(ScriptingImplementation.IL2CPP, BuildOptions.None, Il2CppOutput);
        }

        public static void BuildMonoCli()
        {
            Build(ScriptingImplementation.Mono2x, BuildOptions.Development, MonoOutput);
        }

        private static void Build(ScriptingImplementation backend, BuildOptions options, string output)
        {
            try
            {
                ProjectSetup.ConfigurePlayerSettings();
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
                ProjectSetup.SyncConfigToStreamingAssets();
                ProjectSetup.EnsureScene();
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(ProjectSetup.ScenePath, true)
                };

                string directory = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Debug.Log($"[CK] building {backend} -> {output}, graphics APIs=" +
                          string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64)));
                var buildOptions = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = options | BuildOptions.DetailedBuildReport
                };
                BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
                BuildSummary summary = report.summary;
                Debug.Log($"[CK] build result={summary.result} size={summary.totalSize} bytes " +
                          $"time={summary.totalTime} output={summary.outputPath} errors={summary.totalErrors} warnings={summary.totalWarnings}");
                if (summary.result != BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Build failed: " + summary.result);
                }
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CK] build failed: " + ex);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }
    }
}
