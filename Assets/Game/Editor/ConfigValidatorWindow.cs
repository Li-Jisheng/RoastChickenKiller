using System.IO;
using UnityEditor;
using UnityEngine;
using CK.Domain.Config;

namespace CK.Editor
{
    /// <summary>面向人工的配置校验窗口（M0-T05 / 数据测试工具）。</summary>
    public sealed class ConfigValidatorWindow : EditorWindow
    {
        private string _path;
        private string _summary = "未校验";
        private Vector2 _scroll;
        private ConfigLoadResult _result;

        [MenuItem("Tools/Prototype/Config Validator")]
        public static void Open()
        {
            var window = GetWindow<ConfigValidatorWindow>("CK Config");
            window.minSize = new Vector2(620f, 380f);
            window.Show();
        }

        private void OnEnable()
        {
            _path = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");
        }

        private void OnGUI()
        {
            GUILayout.Label("CombatPrototype.json 校验", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _path = EditorGUILayout.TextField("路径", _path);
                if (GUILayout.Button("浏览", GUILayout.Width(80)))
                {
                    string selected = EditorUtility.OpenFilePanel("选择配置", Path.GetDirectoryName(_path), "json");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        _path = selected;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("校验"))
                {
                    Validate();
                }
                if (GUILayout.Button("重置为项目配置"))
                {
                    _path = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");
                    Validate();
                }
            }
            EditorGUILayout.HelpBox(_summary, _result != null && _result.Success ? MessageType.Info : MessageType.Error);
            if (_result == null)
            {
                return;
            }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_result.Config != null)
            {
                CombatConfig config = _result.Config;
                EditorGUILayout.LabelField("哈希", config.RawHash.ToString("X8"));
                EditorGUILayout.LabelField("帧率", config.TickRate.ToString());
                EditorGUILayout.LabelField("技能数", config.Abilities.Count.ToString());
                EditorGUILayout.LabelField("武器数", config.Weapons.Count.ToString());
                EditorGUILayout.LabelField("增益数", config.Buffs.Count.ToString());
                EditorGUILayout.LabelField("槽位", $"武器={config.Loadout.WeaponSlots} 装备={config.Loadout.EquipmentSlots} 背包={config.Loadout.InventorySlots} 物品栏={config.Loadout.ItemBarSlots}");
                EditorGUILayout.LabelField("连段", string.Join(", ", config.Combo.Steps));
            }
            foreach (string error in _result.Errors)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
            }
            EditorGUILayout.EndScrollView();
        }

        private void Validate()
        {
            _result = CombatConfigLoader.LoadFromFile(_path);
            _summary = _result.Success
                ? $"通过：{_result.ErrorSummary}"
                : $"失败（{_result.Errors.Count} 个错误）";
            if (_result.Config != null)
            {
                _summary += $"\n哈希={_result.Config.RawHash:X8} 技能={_result.Config.Abilities.Count} 武器={_result.Config.Weapons.Count}";
            }
            if (_result.Success)
            {
                Debug.Log("[CK] config validation PASS: " + _path);
            }
            else
            {
                Debug.LogError("[CK] config validation FAIL: " + _result.ErrorSummary);
            }
        }
    }
}
