using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CK.Domain.Config;
using CK.Native;
using CK.Runtime.Actors;
using CK.Runtime.Audio;
using CK.Runtime.Automation;
using CK.Runtime.Combat;
using CK.Runtime.Input;
using CK.Runtime.UI;

namespace CK.Runtime.Bootstrap
{
    /// <summary>
    /// Scene entry point. Builds the whole flat prototype world from
    /// Config/CombatPrototype.json: plane, fixed camera, light, two cylinder actors,
    /// two swords, HUD and the native combat host.
    /// </summary>
    public sealed class CombatPrototypeBootstrap : MonoBehaviour
    {
        public static CombatPrototypeBootstrap Instance { get; private set; }

        public bool AutoStart = true;
        public string ConfigPathOverride;
        public CombatConfig ConfigOverride;
        public IPrototypeInputBackend InputBackendOverride;
        public bool EnableAutomationFromCommandLine = true;

        public CombatConfig Config { get; private set; }
        public CombatDirector Director { get; private set; }
        public PrototypeHud Hud { get; private set; }
        public NullAudioService Audio { get; private set; }
        public ConfigLoadResult LoadResult { get; private set; }
        public ActorView PlayerView { get; private set; }
        public ActorView EnemyView { get; private set; }
        public bool Initialized { get; private set; }
        public bool Failed { get; private set; }
        public string FailureText { get; private set; } = string.Empty;
        public PrototypeAutomation Automation { get; private set; }
        public string SceneName => gameObject.scene.name;

        private void Awake()
        {
            Instance = this;
            if (AutoStart)
            {
                StartPrototype();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static string ResolveConfigPath()
        {
            string fromArgs = CommandLine.GetValue("-ckConfig");
            if (!string.IsNullOrEmpty(fromArgs) && File.Exists(fromArgs))
            {
                return Path.GetFullPath(fromArgs);
            }
            string streaming = Path.Combine(Application.streamingAssetsPath, "Config", "CombatPrototype.json");
            if (!Application.isEditor && File.Exists(streaming))
            {
                return streaming;
            }
            string editorPath = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");
            if (File.Exists(editorPath))
            {
                return editorPath;
            }
            return streaming;
        }

        public void StartPrototype()
        {
            if (Initialized || Failed)
            {
                return;
            }

            Audio = new NullAudioService();
            Hud = gameObject.GetComponent<PrototypeHud>();
            if (Hud == null)
            {
                Hud = gameObject.AddComponent<PrototypeHud>();
            }

            CombatConfig config = ConfigOverride;
            if (config == null)
            {
                string path = !string.IsNullOrEmpty(ConfigPathOverride) ? ConfigPathOverride : ResolveConfigPath();
                LoadResult = CombatConfigLoader.LoadFromFile(path);
                if (!LoadResult.Success)
                {
                    Fail("Config validation failed: " + LoadResult.ErrorSummary, LoadResult.Config);
                    return;
                }
                config = LoadResult.Config;
                Debug.Log($"[CK] config loaded: {config} path={path} rawHash={config.RawHash:X8}");
            }
            Config = config;

            if (!NativeCombatBridge.Probe(out string probeMessage))
            {
                Fail("Native plugin unavailable: " + probeMessage, config);
                return;
            }
            Debug.Log("[CK] " + probeMessage);

            ApplyProjectSettings(config);
            SetupCamera(config);
            SetupLight();
            SetupGround(config);

            HitVolumeConfig playerBody = config.GetActorHitVolume(Faction.Player);
            HitVolumeConfig enemyBody = config.GetActorHitVolume(Faction.Enemy);
            if (playerBody == null || enemyBody == null)
            {
                Fail("Config is missing player or enemy body hit volume", config);
                return;
            }

            WeaponConfig startWeapon = config.Weapons.Count > 0 ? config.Weapons[0] : null;
            PlayerView = ActorView.Create(CombatDirector.PlayerActorId, config.Actors.Player, playerBody,
                ToVector3(config.World.PlayerSpawn), config.World.PlayerYaw, startWeapon);
            EnemyView = ActorView.Create(CombatDirector.EnemyActorId, config.Actors.Enemy, enemyBody,
                ToVector3(config.World.EnemySpawn), config.World.EnemyYaw, startWeapon);
            PlayerView.name = "Player";
            EnemyView.name = "Enemy";

            Director = gameObject.GetComponent<CombatDirector>();
            if (Director == null)
            {
                Director = gameObject.AddComponent<CombatDirector>();
            }
            IPrototypeInputBackend backend = InputBackendOverride ?? new DeviceInputBackend();
            Director.Initialize(config, Audio, backend, PlayerView, EnemyView, out string error);
            Hud.Initialize(Director, config);
            if (!string.IsNullOrEmpty(error))
            {
                Fail("Combat initialization failed: " + error, config);
                return;
            }

            Initialized = true;
            Debug.Log($"[CK] prototype ready scene={SceneName} playerSpawn={PlayerView.transform.position} enemySpawn={EnemyView.transform.position} camera={Camera.main?.transform.position}");

            if (EnableAutomationFromCommandLine)
            {
                string gate = CommandLine.GetValue("-ckGate");
                if (string.IsNullOrEmpty(gate))
                {
                    gate = Environment.GetEnvironmentVariable("CK_GATE") ?? string.Empty;
                }
                string evidence = CommandLine.GetValue("-ckEvidence");
                if (string.IsNullOrEmpty(evidence))
                {
                    evidence = Environment.GetEnvironmentVariable("CK_EVIDENCE") ?? string.Empty;
                }
                string screens = CommandLine.GetValue("-ckScreens");
                if (!string.IsNullOrEmpty(gate) || screens == "1")
                {
                    Automation = gameObject.AddComponent<PrototypeAutomation>();
                    Automation.Begin(this, gate, evidence, screens == "1");
                }
            }
        }

        private void Fail(string message, CombatConfig config)
        {
            Failed = true;
            FailureText = message;
            Config = config;
            Debug.LogError("[CK] " + message);
            if (Hud != null)
            {
                Hud.Initialize(Director, config);
                Hud.ShowErrorState(message);
            }
        }

        private static void ApplyProjectSettings(CombatConfig config)
        {
            Application.targetFrameRate = config.TickRate;
            QualitySettings.vSyncCount = 0;
            Time.fixedDeltaTime = 1f / config.TickRate;
            Physics.gravity = new Vector3(0f, config.World.Gravity, 0f);
            Debug.Log($"[CK] project settings: targetFps={config.TickRate} fixedDeltaTime={Time.fixedDeltaTime:0.00000} gravity={Physics.gravity}");
        }

        private static void SetupCamera(CombatConfig config)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("PrototypeCamera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            camera.transform.position = ToVector3(config.World.CameraPosition);
            camera.transform.LookAt(ToVector3(config.World.CameraTarget));
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.16f, 1f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200f;
            var cameras = new List<string>();
            foreach (Camera existing in Camera.allCameras)
            {
                cameras.Add($"{existing.name}(tag={existing.tag},depth={existing.depth},clear={existing.clearFlags},bg={existing.backgroundColor},pos={existing.transform.position},enabled={existing.enabled})");
            }
            Debug.Log($"[CK] camera setup: main={camera.name} pos={camera.transform.position} rot={camera.transform.eulerAngles} " +
                      $"clear={camera.clearFlags} bg={camera.backgroundColor} mask={camera.cullingMask} rect={camera.pixelRect} " +
                      $"allCameras=[{string.Join("; ", cameras)}] " +
                      $"rp={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?.GetType().Name} quality={QualitySettings.GetQualityLevel()}");
        }

        private static void SetupLight()
        {
            Light light = FindDirectionalLight();
            if (light == null)
            {
                var lightObject = new GameObject("DirectionalLight");
                light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.97f, 0.92f, 1f);
            light.shadows = LightShadows.Soft;
        }

        private static Light FindDirectionalLight()
        {
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional)
                {
                    return lights[i];
                }
            }
            return null;
        }

        private static void SetupGround(CombatConfig config)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground_Plane";
            ground.transform.position = Vector3.zero;
            float size = config.World.GroundSize;
            ground.transform.localScale = new Vector3(size / 10f, 1f, size / 10f);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }
            if (shader == null)
            {
                Debug.LogError("[CK] ground shader missing (URP/Lit not included in this build)");
                return;
            }
            var material = new Material(shader) { name = "GroundRuntimeMaterial" };
            Texture2D checker = CreateCheckerTexture(new Color(0.16f, 0.19f, 0.23f), new Color(0.21f, 0.25f, 0.30f), 8);
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", checker);
                material.SetTextureScale("_BaseMap", new Vector2(size * 0.5f, size * 0.5f));
            }
            else if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", checker);
                material.SetTextureScale("_MainTex", new Vector2(size * 0.5f, size * 0.5f));
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.05f);
            }
            ground.GetComponent<MeshRenderer>().sharedMaterial = material;
            Debug.Log($"[CK] ground plane {size}x{size}m created at origin");
        }

        private static Texture2D CreateCheckerTexture(Color a, Color b, int cells)
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = "GroundChecker",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            int cellSize = Mathf.Max(1, resolution / Mathf.Max(1, cells));
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    bool even = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                    texture.SetPixel(x, y, even ? a : b);
                }
            }
            texture.Apply();
            return texture;
        }

        public static Vector3 ToVector3(Vector3Data data)
        {
            return new Vector3(data.X, data.Y, data.Z);
        }
    }

    /// <summary>Tiny command line reader used by gate automation and player builds.</summary>
    public static class CommandLine
    {
        public static string GetValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : string.Empty;
                }
                if (args[i].StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(name.Length + 1);
                }
            }
            return string.Empty;
        }

        public static bool HasFlag(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static IReadOnlyList<string> All()
        {
            return Environment.GetCommandLineArgs();
        }
    }
}
