using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace CK.Editor
{
    /// <summary>
    /// Makes the Play Mode Game View large enough for readable evidence screenshots.
    /// Strategy: resize the editor window, then maximize the Game View tab; if that
    /// fails, create a floating Game View window at 1300x800. The actual resolution
    /// is recorded in summary.json.
    /// </summary>
    public static class GameViewSizer
    {
        private const uint SwpShowWindow = 0x0040;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int command);

        public static bool PrepareForPlay(int windowWidth = 1300, int windowHeight = 790)
        {
            try
            {
                Type gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
                if (gameViewType == null)
                {
                    Debug.LogWarning("[CK] GameView type not found");
                    return false;
                }

                IntPtr handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, 9); // SW_RESTORE
                    float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
                    int physicalWidth = Mathf.RoundToInt(windowWidth * pixelsPerPoint);
                    int physicalHeight = Mathf.RoundToInt(windowHeight * pixelsPerPoint);
                    SetWindowPos(handle, IntPtr.Zero, 10, 10, physicalWidth, physicalHeight, SwpShowWindow);
                    Debug.Log($"[CK] editor window resized for capture: {windowWidth}x{windowHeight} logical, {physicalWidth}x{physicalHeight} physical, pixelsPerPoint={pixelsPerPoint}");
                }

                EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
                gameView.Show();
                gameView.Focus();
                PropertyInfo maximizeOnPlay = gameViewType.GetProperty("maximizeOnPlay",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (maximizeOnPlay != null && maximizeOnPlay.CanWrite)
                {
                    maximizeOnPlay.SetValue(gameView, true);
                }
                try
                {
                    gameView.maximized = true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[CK] Game View maximize failed: " + ex.Message);
                }
                gameView.Repaint();
                Debug.Log($"[CK] Game View prepared: pos={gameView.position} maximized={gameView.maximized} " +
                          $"screen={Screen.width}x{Screen.height}");
                return true;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                Debug.LogWarning("[CK] Game View preparation failed: " + inner.GetType().Name + " " + inner.Message);
                return false;
            }
        }

        /// <summary>Diagnostics entry retained for troubleshooting capture resolution.</summary>
        public static void DumpInfo()
        {
            try
            {
                Type gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
                Debug.Log("[CK] screen=" + Screen.width + "x" + Screen.height);
                if (gameViewType != null)
                {
                    EditorWindow window = EditorWindow.GetWindow(gameViewType, false, "Game", false);
                    object selected = gameViewType.GetProperty("selectedSizeIndex",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(window);
                    object onPlay = gameViewType.GetProperty("maximizeOnPlay",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(window);
                    Debug.Log($"[CK] GameView selectedSizeIndex={selected} maximizeOnPlay={onPlay} position={window.position} maximized={window.maximized}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CK] GameView dump failed: " + ex);
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
    }
}
