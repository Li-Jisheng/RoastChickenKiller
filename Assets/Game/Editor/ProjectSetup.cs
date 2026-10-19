using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using CK.Runtime.Bootstrap;

namespace CK.Editor
{
    /// <summary>
    /// One-shot project baseline setup: DX11-only, 60 Hz, linear color, IL2CPP Win64
    /// and the generated CombatPrototype scene. Safe to run repeatedly.
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/CombatPrototype.unity";
        public const string ConfigAssetPath = "Assets/StreamingAssets/Config/CombatPrototype.json";

        public static void Bootstrap()
        {
            try
            {
                ConfigurePlayerSettings();
                ConfigureTimeManager();
                EnsureScene();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[CK] project setup complete: DX11-only, 60Hz, IL2CPP Win64, scene=" + ScenePath);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[CK] project setup failed: " + ex);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        public static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "ChickenKiller";
            PlayerSettings.productName = "ChickenKiller";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = false;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);

            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode3D;
            Debug.Log("[CK] graphics APIs (Win64): " +
                      string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64)) +
                      " useDefault=" + PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64));
        }

        public static void ConfigureTimeManager()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[CK] TimeManager.asset not found; runtime bootstrap will still force 60 Hz");
                return;
            }
            var serialized = new SerializedObject(assets[0]);
            SerializedProperty fixedTimestep = serialized.FindProperty("Fixed Timestep");
            if (fixedTimestep != null)
            {
                fixedTimestep.floatValue = 1f / 60f;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[CK] TimeManager Fixed Timestep = 0.0166667 (60 Hz)");
        }

        public static void EnsureScene()
        {
            string directory = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            if (!File.Exists(ScenePath))
            {
                CreateScene();
            }
            EnsureBuildSettings();
        }

        private static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("CombatPrototype");
            root.AddComponent<CombatPrototypeBootstrap>();
            var cameraObject = new GameObject("MainCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 6f, -9f);
            camera.transform.LookAt(new Vector3(0f, 1f, 1f));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.16f, 1f);
            cameraObject.AddComponent<AudioListener>();
            var lightObject = new GameObject("DirectionalLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[CK] created scene " + ScenePath);
        }

        public static void EnsureBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>();
            bool found = false;
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path == ScenePath)
                {
                    found = true;
                    scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                }
                else
                {
                    scenes.Add(existing);
                }
            }
            if (!found)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        [MenuItem("Tools/Prototype/Setup Project Baseline")]
        public static void MenuSetup()
        {
            ConfigurePlayerSettings();
            ConfigureTimeManager();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[CK] baseline setup done");
        }

        /// <summary>Copies the single source-of-truth JSON into StreamingAssets for Player builds.</summary>
        public static void SyncConfigToStreamingAssets()
        {
            string source = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("Config/CombatPrototype.json not found", source);
            }
            string destinationDirectory = Path.GetDirectoryName(ConfigAssetPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }
            File.Copy(source, ConfigAssetPath, true);
            AssetDatabase.ImportAsset(ConfigAssetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[CK] config copied to " + ConfigAssetPath);
        }
    }
}
