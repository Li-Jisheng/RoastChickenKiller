using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CK.Editor
{
    /// <summary>
    /// Editor-side screenshot helper for manual evidence. Gate automation performs its
    /// own captures on WaitForEndOfFrame; this menu is for humans taking extra shots.
    /// </summary>
    public static class ScreenshotCapture
    {
        [MenuItem("Tools/Prototype/Screenshot/Capture Game View", priority = 60)]
        public static void CaptureGameView()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[CK] enter Play Mode before capturing the Game View");
                return;
            }
            string directory = Path.Combine(ProjectRoot, "TestArtifacts", "Manual", DateTime.Now.ToString("yyyyMMddTHHmmss"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "gameview.png");
            ScreenCapture.CaptureScreenshot(path, 1);
            Debug.Log($"[CK] screenshot requested: {path} (resolution {Screen.width}x{Screen.height})");
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(path))
                {
                    Debug.Log($"[CK] screenshot saved: {path} ({new FileInfo(path).Length} bytes)");
                }
                else
                {
                    Debug.LogWarning("[CK] screenshot not written yet: " + path);
                }
            };
        }

        [MenuItem("Tools/Prototype/Open TestArtifacts Folder", priority = 61)]
        public static void OpenArtifacts()
        {
            string path = Path.Combine(ProjectRoot, "TestArtifacts");
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        public static string ProjectRoot
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.persistentDataPath;
            }
        }
    }
}
