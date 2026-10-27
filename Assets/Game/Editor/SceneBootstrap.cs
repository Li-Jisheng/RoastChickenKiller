using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CK.Editor
{
    /// <summary>
    /// 启动时恢复默认场景。Unity 有时会在没有 LastSceneManagerSetup 的情况下
    /// 打开一个空的 Untitled 场景，此时 Game View 没有相机/世界，看起来像“什么都没了”。
    /// 本类只在交互式编辑器、且当前场景为空/无效时打开 CombatPrototype；
    /// 批处理测试/构建和 Play Mode 中不会干预。
    /// </summary>
    [InitializeOnLoad]
    public static class SceneBootstrap
    {
        private const string AttemptKey = "CK.SceneBootstrap.Attempted.v3";

        static SceneBootstrap()
        {
            EditorApplication.delayCall += OpenDefaultSceneIfNeeded;
        }

        [MenuItem("Tools/Prototype/Open CombatPrototype Scene", priority = 5)]
        public static void OpenDefaultScene()
        {
            if (!File.Exists(ProjectSetup.ScenePath))
            {
                Debug.LogError("[CK] 场景不存在：" + ProjectSetup.ScenePath);
                return;
            }
            EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
            Debug.Log("[CK] 已打开场景：" + ProjectSetup.ScenePath);
        }

        private static void OpenDefaultSceneIfNeeded()
        {
            if (Application.isBatchMode)
            {
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
            if (SessionState.GetBool(AttemptKey, false))
            {
                return;
            }
            SessionState.SetBool(AttemptKey, true);

            Scene active = SceneManager.GetActiveScene();
            bool hasValidScene = !string.IsNullOrEmpty(active.path) && File.Exists(active.path);
            if (hasValidScene)
            {
                return;
            }

            // 未保存的 Untitled 只有在“默认相机/灯光”或空场景时才自动替换；
            // 用户自己搭的场景不自动丢弃，只提示使用菜单。
            if (active.isDirty && !IsDefaultUntitled(active))
            {
                Debug.LogWarning("[CK] 当前是未保存的 Untitled 场景。请使用 Tools/Prototype/Open CombatPrototype Scene 打开默认场景。");
                return;
            }

            if (!File.Exists(ProjectSetup.ScenePath))
            {
                Debug.LogError("[CK] 默认场景不存在：" + ProjectSetup.ScenePath);
                return;
            }

            EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
            Debug.Log("[CK] 启动时检测到空场景，已自动打开：" + ProjectSetup.ScenePath);
        }

        /// <summary>只有默认空场景（空、Main Camera、Directional Light）才允许自动替换。</summary>
        private static bool IsDefaultUntitled(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            if (roots == null || roots.Length == 0)
            {
                return true;
            }
            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }
                if (root.name == "Main Camera" || root.name == "Directional Light")
                {
                    continue;
                }
                return false;
            }
            return true;
        }
    }
}
