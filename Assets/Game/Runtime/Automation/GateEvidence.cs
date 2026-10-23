using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;
using CK.Runtime.Combat;
using CK.Runtime.UI;

namespace CK.Runtime.Automation
{
    public sealed class ScreenshotRecord
    {
        public string File;
        public string Label;
        public string CapturedAt;
        public Dictionary<string, object> Expected = new Dictionary<string, object>();
        public Dictionary<string, object> Observed = new Dictionary<string, object>();
        public bool Matched = true;
        public List<string> Mismatches = new List<string>();
    }

    public sealed class TestCounts
    {
        public int Total;
        public int Passed;
        public int Failed;
    }

    public sealed class GateTests
    {
        public TestCounts EditMode = new TestCounts();
        public TestCounts PlayMode = new TestCounts();
        public TestCounts Native = new TestCounts();
    }

    public sealed class GateSummary
    {
        public string Module = string.Empty;
        public string Status = "PASS";
        public string Timestamp = string.Empty;
        public string UnityVersion = string.Empty;
        public string GitCommit = string.Empty;
        public string Scene = string.Empty;
        public string ConfigPath = string.Empty;
        public string ConfigHash = string.Empty;
        public string NativeHash = string.Empty;
        public int TickRate;
        public string Resolution = string.Empty;
        public string GraphicsDevice = string.Empty;
        public string PlayerBuild = string.Empty;
        public string Verdict = "PENDING_VERIFIER";
        public string Verifier = string.Empty;
        public int ConsoleErrors;
        public int ConsoleWarnings;
        public List<string> ConsoleErrorDetails = new List<string>();
        public GateTests Tests = new GateTests();
        public List<ScreenshotRecord> Screenshots = new List<ScreenshotRecord>();
        public Dictionary<string, object> Metrics = new Dictionary<string, object>();
        public List<string> Failures = new List<string>();
        public string Notes = string.Empty;
    }

    /// <summary>收集截图、实测值与最终 Gate summary。</summary>
    public sealed class GateEvidence
    {
        public string Module { get; }
        public string Directory { get; }
        public string ScreenshotDirectory { get; }
        public GateSummary Summary { get; }

        public GateEvidence(string module, string evidenceDirectory, string gitCommit)
        {
            Module = module;
            Directory = evidenceDirectory;
            ScreenshotDirectory = Path.Combine(evidenceDirectory, "screenshots");
            System.IO.Directory.CreateDirectory(ScreenshotDirectory);
            Summary = new GateSummary
            {
                Module = module,
                Timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                UnityVersion = Application.unityVersion,
                GitCommit = gitCommit ?? string.Empty,
                Scene = "Assets/Scenes/CombatPrototype.unity",
                TickRate = 60,
                GraphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
                Resolution = $"{Screen.width}x{Screen.height}"
            };
        }

        public static string DefaultDirectory(string module)
        {
            string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(root))
            {
                root = Application.persistentDataPath;
            }
            return Path.Combine(root, "TestArtifacts", module, DateTime.Now.ToString("yyyyMMddTHHmmss"));
        }

        public void Configure(CombatConfigLite info)
        {
            Summary.ConfigPath = info.ConfigPath;
            Summary.ConfigHash = info.ConfigHash;
            Summary.NativeHash = info.NativeHash;
            Summary.TickRate = info.TickRate;
            Summary.PlayerBuild = info.PlayerBuild;
        }

        public Dictionary<string, object> Observe(CombatDirector director, PrototypeHud hud)
        {
            var observed = new Dictionary<string, object>();
            if (director == null || director.PlayerSnapshot == null || director.EnemySnapshot == null)
            {
                observed["error"] = "director not ready";
                return observed;
            }
            var player = director.PlayerSnapshot;
            var enemy = director.EnemySnapshot;
            observed["tick"] = director.Tick;
            observed["playerHp"] = Mathf.RoundToInt(player.Health);
            observed["playerMaxHp"] = Mathf.RoundToInt(player.MaxHealth);
            observed["enemyHp"] = Mathf.RoundToInt(enemy.Health);
            observed["enemyMaxHp"] = Mathf.RoundToInt(enemy.MaxHealth);
            observed["playerDef"] = Mathf.RoundToInt(player.EffectiveDefense);
            observed["enemyDef"] = Mathf.RoundToInt(enemy.EffectiveDefense);
            observed["buffRemaining"] = enemy.BuffRemainingFrames;
            observed["buffActive"] = enemy.BuffActive;
            observed["combo"] = player.ComboStep;
            observed["comboCount"] = player.ComboCount;
            observed["weaponSlot"] = director.WeaponSlotIndex + 1;
            observed["weaponId"] = director.CurrentWeapon != null ? director.CurrentWeapon.Id : "<empty>";
            observed["weaponLength"] = director.CurrentWeapon != null ? director.CurrentWeapon.Length : 0f;
            observed["lastEvent"] = director.LastEventType.ToString();
            observed["lastDamage"] = director.LastDamage;
            observed["overlap"] = director.LastOverlap;
            observed["blocked"] = director.BlockedCount;
            observed["dodged"] = director.DodgedCount;
            observed["hitTaken"] = director.HitTakenCount;
            observed["hitDealt"] = director.HitDealtCount;
            observed["playerX"] = Mathf.Round(director.Player.transform.position.x * 100f) / 100f;
            observed["playerZ"] = Mathf.Round(director.Player.transform.position.z * 100f) / 100f;
            observed["distance"] = Mathf.Round(director.DistanceToEnemy * 100f) / 100f;
            observed["touching"] = director.PlayerTouchingEnemy;
            observed["inventoryOpen"] = director.InventoryVisible;
            observed["mapOpen"] = director.MapVisible;
            observed["itemSlot"] = director.LastItemSlot;
            observed["itemHistory"] = string.Join(",", director.ItemSlotHistory);
            observed["enemySwings"] = director.EnemySwingTicks.Count;
            observed["swingTicks"] = string.Join(",", director.EnemySwingTicks);
            observed["swingIntervals"] = director.FormatSwingIntervals();
            observed["weaponSwitches"] = director.WeaponSwitches;
            observed["switchRejections"] = director.WeaponSwitchRejections;
            observed["resolution"] = $"{Screen.width}x{Screen.height}";
            observed["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString();
            observed["hudDraws"] = hud != null ? hud.DrawCount : 0;
            observed["inputBackend"] = director.Input != null ? director.Input.BackendName : "none";
            observed["weaponSlots"] = director.Config != null ? director.Config.Loadout.WeaponSlots : 0;
            observed["equipmentSlots"] = director.Config != null ? director.Config.Loadout.EquipmentSlots : 0;
            observed["inventorySlots"] = director.Config != null ? director.Config.Loadout.InventorySlots : 0;
            observed["itemBarSlots"] = director.Config != null ? director.Config.Loadout.ItemBarSlots : 0;
            observed["build"] = Application.isEditor ? "Editor" : "Player";
            observed["audioCues"] = director.Audio != null ? director.Audio.PlayedCueCount : 0;
            return observed;
        }

        public ScreenshotRecord Record(string fileName, string label, Dictionary<string, object> expected,
            Dictionary<string, object> observed)
        {
            var record = new ScreenshotRecord
            {
                File = "screenshots/" + fileName,
                Label = label,
                CapturedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
                Expected = expected ?? new Dictionary<string, object>(),
                Observed = observed ?? new Dictionary<string, object>()
            };
            foreach (KeyValuePair<string, object> pair in record.Expected)
            {
                if (!record.Observed.TryGetValue(pair.Key, out object actual))
                {
                    record.Mismatches.Add($"{pair.Key}: missing observed value");
                    continue;
                }
                if (!ValuesMatch(pair.Value, actual))
                {
                    record.Mismatches.Add($"{pair.Key}: expected {pair.Value}, observed {actual}");
                }
            }
            record.Matched = record.Mismatches.Count == 0;
            if (!record.Matched)
            {
                Summary.Failures.Add($"{fileName}: " + string.Join("; ", record.Mismatches));
            }
            Summary.Screenshots.Add(record);
            return record;
        }

        public void Fail(string message)
        {
            Summary.Failures.Add(message);
            Debug.LogError("[CK] GATE FAIL: " + message);
        }

        public void Metric(string key, object value)
        {
            Summary.Metrics[key] = value;
        }

        public string Write(string notes = null)
        {
            Summary.ConsoleErrors = RuntimeLogRecorder.ErrorCount;
            Summary.ConsoleWarnings = RuntimeLogRecorder.WarningCount;
            Summary.ConsoleErrorDetails.Clear();
            Summary.ConsoleErrorDetails.AddRange(RuntimeLogRecorder.Errors);
            Summary.Status = Summary.Failures.Count == 0 && Summary.ConsoleErrors == 0 ? "PASS" : "FAIL";
            if (Summary.ConsoleErrors > 0)
            {
                Summary.Failures.Add($"console errors: {Summary.ConsoleErrors}");
            }
            if (Summary.Screenshots.Count == 0)
            {
                Summary.Failures.Add("no screenshots captured");
            }
            if (!string.IsNullOrEmpty(notes))
            {
                Summary.Notes = notes;
            }

            string path = Path.Combine(Directory, "summary.json");
            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Include
            };
            File.WriteAllText(path, JsonConvert.SerializeObject(Summary, settings), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Directory, "DONE.txt"), Summary.Status + "\n" + Summary.Timestamp + "\n", new UTF8Encoding(false));
            Debug.Log($"[CK] gate summary written: {path} status={Summary.Status} screenshots={Summary.Screenshots.Count} errors={Summary.ConsoleErrors}");
            return path;
        }

        private static bool ValuesMatch(object expected, object actual)
        {
            if (expected == null || actual == null)
            {
                return expected == actual;
            }
            if (IsNumber(expected) && IsNumber(actual))
            {
                double a = Convert.ToDouble(expected, CultureInfo.InvariantCulture);
                double b = Convert.ToDouble(actual, CultureInfo.InvariantCulture);
                return Math.Abs(a - b) <= 0.001;
            }
            if (expected is bool && actual is bool)
            {
                return (bool)expected == (bool)actual;
            }
            return string.Equals(expected.ToString(), actual.ToString(), StringComparison.Ordinal);
        }

        private static bool IsNumber(object value)
        {
            return value is int || value is long || value is float || value is double || value is short ||
                   value is byte || value is uint;
        }
    }

    public struct CombatConfigLite
    {
        public string ConfigPath;
        public string ConfigHash;
        public string NativeHash;
        public int TickRate;
        public string PlayerBuild;
    }
}
