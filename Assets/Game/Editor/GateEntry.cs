using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CK.Runtime.Automation;
using CK.Runtime.Bootstrap;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CK.Editor
{
    /// <summary>
    /// Command-line / menu entry that enters Play Mode, lets PrototypeAutomation run
    /// a gate scenario, then merges the test XMLs into summary.json and exits.
    /// State is kept in SessionState so the play-mode domain reload cannot lose it.
    /// </summary>
    public static class GateEntry
    {
        private const string ActiveKey = "CK.Gate.Active";
        private const string ModuleKey = "CK.Gate.Module";
        private const string EvidenceKey = "CK.Gate.Evidence";
        private const string ExitKey = "CK.Gate.Exit";
        private const string PhaseKey = "CK.Gate.Phase";
        private const string DeadlineKey = "CK.Gate.Deadline";
        private const string GitKey = "CK.Gate.Git";

        private static double _startDeadline;

        [InitializeOnLoadMethod]
        private static void ResumeAfterDomainReload()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                if (SessionState.GetString(PhaseKey, "starting") == "starting")
                {
                    _startDeadline = EditorApplication.timeSinceStartup + 120.0;
                }
                Hook();
                Debug.Log("[CK] gate entry resumed after domain reload, phase=" + SessionState.GetString(PhaseKey, "?"));
            }
        }

        public static void Run()
        {
            string module = CommandLine.GetValue("-ckGate");
            if (string.IsNullOrEmpty(module))
            {
                module = Environment.GetEnvironmentVariable("CK_GATE") ?? string.Empty;
            }
            if (string.IsNullOrEmpty(module))
            {
                Debug.LogError("[CK] -ckGate <module> is required for GateEntry.Run");
                EditorApplication.Exit(2);
                return;
            }
            string evidence = CommandLine.GetValue("-ckEvidence");
            if (string.IsNullOrEmpty(evidence))
            {
                evidence = Environment.GetEnvironmentVariable("CK_EVIDENCE") ?? GateEvidence.DefaultDirectory(module);
            }
            string git = CommandLine.GetValue("-ckGitCommit");
            float timeout = 600f;
            if (float.TryParse(CommandLine.GetValue("-ckTimeout"), out float customTimeout) && customTimeout > 0f)
            {
                timeout = customTimeout;
            }
            Begin(module, evidence, git, true, timeout);
        }

        [MenuItem("Tools/Prototype/Run Gate M0", priority = 10)]
        private static void MenuM0() => BeginMenu("M0");

        [MenuItem("Tools/Prototype/Run Gate M1", priority = 11)]
        private static void MenuM1() => BeginMenu("M1");

        [MenuItem("Tools/Prototype/Run Gate M2", priority = 12)]
        private static void MenuM2() => BeginMenu("M2");

        [MenuItem("Tools/Prototype/Run Gate M3", priority = 13)]
        private static void MenuM3() => BeginMenu("M3");

        [MenuItem("Tools/Prototype/Run Gate M4", priority = 14)]
        private static void MenuM4() => BeginMenu("M4");

        [MenuItem("Tools/Prototype/Run Gate M5", priority = 15)]
        private static void MenuM5() => BeginMenu("M5");

        [MenuItem("Tools/Prototype/Run Gate M6", priority = 16)]
        private static void MenuM6() => BeginMenu("M6");

        [MenuItem("Tools/Prototype/Run Gate M7", priority = 17)]
        private static void MenuM7() => BeginMenu("M7");

        private static void BeginMenu(string module)
        {
            string evidence = GateEvidence.DefaultDirectory(module);
            Begin(module, evidence, string.Empty, false, 600f);
        }

        public static void Begin(string module, string evidenceDirectory, string gitCommit, bool exitWhenDone, float timeoutSeconds)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[CK] cannot start gate while Play Mode is active");
                return;
            }
            if (string.IsNullOrEmpty(evidenceDirectory))
            {
                evidenceDirectory = GateEvidence.DefaultDirectory(module);
            }
            Directory.CreateDirectory(evidenceDirectory);
            Directory.CreateDirectory(Path.Combine(evidenceDirectory, "screenshots"));

            ProjectSetup.EnsureScene();
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());

            Environment.SetEnvironmentVariable("CK_GATE", module);
            Environment.SetEnvironmentVariable("CK_EVIDENCE", evidenceDirectory);
            Environment.SetEnvironmentVariable("CK_GIT_COMMIT", gitCommit ?? string.Empty);

            SessionState.SetBool(ActiveKey, true);
            SessionState.SetString(ModuleKey, module);
            SessionState.SetString(EvidenceKey, evidenceDirectory);
            SessionState.SetBool(ExitKey, exitWhenDone);
            SessionState.SetString(PhaseKey, "starting");
            SessionState.SetString(GitKey, gitCommit ?? string.Empty);
            SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + timeoutSeconds).ToString("R", CultureInfo.InvariantCulture));
            _startDeadline = EditorApplication.timeSinceStartup + 120.0;

            Debug.Log($"[CK] gate {module} beginning. evidence={evidenceDirectory} exitWhenDone={exitWhenDone} timeout={timeoutSeconds}s");
            Hook();
            EditorApplication.isPlaying = true;
        }

        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Unhook()
        {
            EditorApplication.update -= Tick;
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                Unhook();
                return;
            }
            string phase = SessionState.GetString(PhaseKey, "starting");
            string module = SessionState.GetString(ModuleKey, "?");
            string evidence = SessionState.GetString(EvidenceKey, string.Empty);
            double now = EditorApplication.timeSinceStartup;

            switch (phase)
            {
                case "starting":
                    if (EditorApplication.isPlaying)
                    {
                        SessionState.SetString(PhaseKey, "playing");
                        Debug.Log("[CK] gate " + module + " entered Play Mode");
                    }
                    else if (!EditorApplication.isPlayingOrWillChangePlaymode && now > _startDeadline)
                    {
                        Abort(module, evidence, "editor did not enter Play Mode");
                    }
                    break;

                case "playing":
                    string doneFile = Path.Combine(evidence, "DONE.txt");
                    if (File.Exists(doneFile))
                    {
                        Debug.Log("[CK] gate " + module + " finished; merging test results");
                        SessionState.SetString(PhaseKey, "finalizing");
                        MergeSummary(module, evidence);
                        EditorApplication.isPlaying = false;
                    }
                    else if (now > ParseDeadline())
                    {
                        Abort(module, evidence, "gate scenario timed out");
                    }
                    break;

                case "finalizing":
                    if (EditorApplication.isPlaying)
                    {
                        return;
                    }
                    Complete(module, evidence);
                    break;
            }
        }

        private static double ParseDeadline()
        {
            string text = SessionState.GetString(DeadlineKey, "0");
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }
            return double.MaxValue;
        }

        private static void Abort(string module, string evidence, string reason)
        {
            Debug.LogError($"[CK] gate {module} aborted: {reason}");
            WriteFailureSummary(module, evidence, reason);
            SessionState.SetString(PhaseKey, "finalizing");
            EditorApplication.isPlaying = false;
        }

        private static void Complete(string module, string evidence)
        {
            bool exit = SessionState.GetBool(ExitKey, false);
            string doneFile = Path.Combine(evidence, "DONE.txt");
            string status = "FAIL";
            if (File.Exists(doneFile))
            {
                status = File.ReadAllText(doneFile).Trim().Split('\n')[0];
            }
            Debug.Log($"[CK] gate {module} complete status={status} evidence={evidence}");
            SessionState.SetBool(ActiveKey, false);
            Unhook();
            if (exit)
            {
                EditorApplication.Exit(status == "PASS" ? 0 : 1);
            }
        }

        private static void WriteFailureSummary(string module, string evidence, string reason)
        {
            if (string.IsNullOrEmpty(evidence))
            {
                return;
            }
            Directory.CreateDirectory(evidence);
            string path = Path.Combine(evidence, "summary.json");
            JObject root;
            if (File.Exists(path))
            {
                try
                {
                    root = JObject.Parse(File.ReadAllText(path));
                }
                catch (Exception)
                {
                    root = new JObject();
                }
            }
            else
            {
                root = new JObject();
            }
            root["module"] = module;
            root["status"] = "FAIL";
            root["timestamp"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
            root["graphicsDevice"] = SystemInfo.graphicsDeviceType.ToString();
            var failures = root["failures"] as JArray ?? new JArray();
            failures.Add(reason);
            root["failures"] = failures;
            root["verdict"] = "PENDING_VERIFIER";
            File.WriteAllText(path, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(evidence, "DONE.txt"), "FAIL\n" + reason + "\n", new UTF8Encoding(false));
        }

        private static void MergeSummary(string module, string evidence)
        {
            string path = Path.Combine(evidence, "summary.json");
            JObject root;
            if (File.Exists(path))
            {
                try
                {
                    root = JObject.Parse(File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    Debug.LogError("[CK] cannot parse summary.json: " + ex.Message);
                    root = new JObject();
                }
            }
            else
            {
                root = new JObject
                {
                    ["module"] = module,
                    ["status"] = "FAIL",
                    ["failures"] = new JArray("automation did not write summary.json"),
                    ["verdict"] = "PENDING_VERIFIER"
                };
            }

            var tests = new JObject
            {
                ["editMode"] = ReadNUnitCounts(Path.Combine(evidence, "editmode-results.xml")),
                ["playMode"] = ReadNUnitCounts(Path.Combine(evidence, "playmode-results.xml")),
                ["native"] = ReadJUnitCounts(Path.Combine(evidence, "native-results.xml"))
            };
            root["tests"] = tests;
            root["gitCommit"] = SessionState.GetString(GitKey, root.Value<string>("gitCommit") ?? string.Empty);
            root["unityVersion"] = Application.unityVersion;
            root["scene"] = "Assets/Scenes/CombatPrototype.unity";
            File.WriteAllText(path, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            Debug.Log("[CK] summary.json updated with test counts: " + tests.ToString(Formatting.None));
        }

        private static JObject ReadNUnitCounts(string path)
        {
            var result = new JObject { ["total"] = 0, ["passed"] = 0, ["failed"] = 0, ["found"] = false };
            if (!File.Exists(path))
            {
                return result;
            }
            try
            {
                XDocument document = XDocument.Load(path);
                XElement root = document.Root;
                if (root == null)
                {
                    return result;
                }
                if (root.Name.LocalName == "test-run")
                {
                    result["total"] = ParseInt(root.Attribute("total"));
                    result["passed"] = ParseInt(root.Attribute("passed"));
                    result["failed"] = ParseInt(root.Attribute("failed"));
                    result["found"] = true;
                }
                else if (root.Name.LocalName == "test-results")
                {
                    result["total"] = ParseInt(root.Attribute("total"));
                    result["failed"] = ParseInt(root.Attribute("failures")) + ParseInt(root.Attribute("errors"));
                    result["passed"] = ParseInt(root.Attribute("total")) - ParseInt(root.Attribute("failures")) - ParseInt(root.Attribute("errors"));
                    result["found"] = true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CK] failed to parse " + path + ": " + ex.Message);
            }
            return result;
        }

        private static JObject ReadJUnitCounts(string path)
        {
            var result = new JObject { ["total"] = 0, ["passed"] = 0, ["failed"] = 0, ["found"] = false };
            if (!File.Exists(path))
            {
                return result;
            }
            try
            {
                XDocument document = XDocument.Load(path);
                int total = 0;
                int failed = 0;
                foreach (XElement suite in document.Descendants("testsuite"))
                {
                    total += ParseInt(suite.Attribute("tests"));
                    failed += ParseInt(suite.Attribute("failures")) + ParseInt(suite.Attribute("errors"));
                }
                result["total"] = total;
                result["failed"] = failed;
                result["passed"] = Math.Max(0, total - failed);
                result["found"] = total > 0;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CK] failed to parse " + path + ": " + ex.Message);
            }
            return result;
        }

        private static int ParseInt(XAttribute attribute)
        {
            if (attribute == null)
            {
                return 0;
            }
            return int.TryParse(attribute.Value, out int value) ? value : 0;
        }
    }
}
