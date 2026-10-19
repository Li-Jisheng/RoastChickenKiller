using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CK.Editor
{
    /// <summary>
    /// Best-effort Game View resolution control for readable 1280x720 evidence captures.
    /// Uses Unity's internal GameViewSizes API via reflection; failures are non-fatal and
    /// the actual resolution is recorded in summary.json.
    /// </summary>
    public static class GameViewSizer
    {
        public static bool TrySet(int width, int height)
        {
            try
            {
                Type gameViewType = Type.GetType("UnityEditor.GameView, UnityEditor");
                Type sizesType = Type.GetType("UnityEditor.GameViewSizes, UnityEditor");
                Type sizeType = Type.GetType("UnityEditor.GameViewSize, UnityEditor");
                Type sizeKindType = Type.GetType("UnityEditor.GameViewSizeType, UnityEditor");
                if (gameViewType == null || sizesType == null || sizeType == null || sizeKindType == null)
                {
                    Debug.LogWarning("[CK] GameViewSizes reflection types unavailable; keeping current Game View size");
                    return false;
                }

                EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);
                Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                object sizes = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);
                if (sizes == null)
                {
                    return false;
                }
                PropertyInfo groupTypeProperty = sizesType.GetProperty("currentGroupType",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (groupTypeProperty == null)
                {
                    return false;
                }
                object groupType = groupTypeProperty.GetValue(sizes);
                MethodInfo getGroup = sizesType.GetMethod("GetGroup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object group = getGroup?.Invoke(sizes, new[] { groupType });
                if (group == null)
                {
                    return false;
                }
                Type groupClass = group.GetType();
                int total = (int)(groupClass.GetProperty("totalCount")?.GetValue(group) ?? 0);
                MethodInfo getSize = groupClass.GetMethod("GetGameViewSize");
                for (int i = 0; i < total; i++)
                {
                    object size = getSize?.Invoke(group, new object[] { i });
                    if (size == null)
                    {
                        continue;
                    }
                    int w = (int)(size.GetType().GetProperty("width")?.GetValue(size) ?? 0);
                    int h = (int)(size.GetType().GetProperty("height")?.GetValue(size) ?? 0);
                    if (w == width && h == height)
                    {
                        return Select(gameView, i);
                    }
                }

                ConstructorInfo constructor = sizeType.GetConstructor(new[] { sizeKindType, typeof(int), typeof(int), typeof(string) });
                if (constructor == null)
                {
                    return false;
                }
                object fixedResolution = Enum.Parse(sizeKindType, "FixedResolution");
                object newSize = constructor.Invoke(new object[] { fixedResolution, width, height, $"CK {width}x{height}" });
                groupClass.GetMethod("AddCustomSize")?.Invoke(group, new[] { newSize });
                total = (int)(groupClass.GetProperty("totalCount")?.GetValue(group) ?? 0);
                return Select(gameView, total - 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[CK] Game View resolution set failed: " + ex.GetType().Name + " " + ex.Message);
                return false;
            }
        }

        private static bool Select(EditorWindow gameView, int index)
        {
            MethodInfo callback = gameView.GetType().GetMethod("SizeSelectionCallback",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (callback == null)
            {
                Debug.LogWarning("[CK] GameView.SizeSelectionCallback not found");
                return false;
            }
            callback.Invoke(gameView, new object[] { index, null });
            Debug.Log($"[CK] Game View size set to index {index}");
            return true;
        }
    }
}
