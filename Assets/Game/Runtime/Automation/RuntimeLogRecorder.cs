using System;
using System.Collections.Generic;
using UnityEngine;

namespace CK.Runtime.Automation
{
    /// <summary>
    /// 统计 Gate 运行期间的 Unity Console 错误/异常，使 summary.json 能
    /// 证明 Console 干净（或精确记录失败）。
    /// </summary>
    public static class RuntimeLogRecorder
    {
        public static int ErrorCount { get; private set; }
        public static int WarningCount { get; private set; }
        public static int ExceptionCount { get; private set; }
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Warnings = new List<string>();

        private static bool _installed;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            Application.logMessageReceived += OnLog;
        }

        public static void ResetCounters()
        {
            ErrorCount = 0;
            WarningCount = 0;
            ExceptionCount = 0;
            Errors.Clear();
            Warnings.Clear();
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Assert:
                    ErrorCount++;
                    if (Errors.Count < 20)
                    {
                        Errors.Add($"[{type}] {condition}");
                    }
                    break;
                case LogType.Exception:
                    ExceptionCount++;
                    ErrorCount++;
                    if (Errors.Count < 20)
                    {
                        Errors.Add($"[Exception] {condition}\n{stackTrace}");
                    }
                    break;
                case LogType.Warning:
                    WarningCount++;
                    if (Warnings.Count < 20)
                    {
                        Warnings.Add(condition);
                    }
                    break;
            }
        }
    }
}
