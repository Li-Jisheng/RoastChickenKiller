using System.IO;
using UnityEditor;
using UnityEngine;
using CK.Domain.Config;

namespace CK.Editor
{
    /// <summary>Human-facing config validation window (M0-T05 / Data-Test tooling).</summary>
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
                _path = EditorGUILayout.TextField("Path", _path);
                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string selected = EditorUtility.OpenFilePanel("Select config", Path.GetDirectoryName(_path), "json");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        _path = selected;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    Validate();
                }
                if (GUILayout.Button("Reset to project config"))
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
                EditorGUILayout.LabelField("Hash", config.RawHash.ToString("X8"));
                EditorGUILayout.LabelField("Tick rate", config.TickRate.ToString());
                EditorGUILayout.LabelField("Abilities", config.Abilities.Count.ToString());
                EditorGUILayout.LabelField("Weapons", config.Weapons.Count.ToString());
                EditorGUILayout.LabelField("Buffs", config.Buffs.Count.ToString());
                EditorGUILayout.LabelField("Loadout", $"weapons={config.Loadout.WeaponSlots} equipment={config.Loadout.EquipmentSlots} inventory={config.Loadout.InventorySlots} itembar={config.Loadout.ItemBarSlots}");
                EditorGUILayout.LabelField("Combo", string.Join(", ", config.Combo.Steps));
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
                ? $"PASS: {_result.ErrorSummary}"
                : $"FAIL ({_result.Errors.Count} errors)";
            if (_result.Config != null)
            {
                _summary += $"\nhash={_result.Config.RawHash:X8} abilities={_result.Config.Abilities.Count} weapons={_result.Config.Weapons.Count}";
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
