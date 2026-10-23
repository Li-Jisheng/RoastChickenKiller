using System.Collections.Generic;
using UnityEngine;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Runtime.Combat;

namespace CK.Runtime.UI
{
    /// <summary>
    /// IMGUI 原型 HUD。刻意保持无依赖，使截图始终显示来自原生核心的权威数值。
    /// 内部枚举/ABI 标识保持英文，显示层统一转成中文。
    /// </summary>
    public sealed class PrototypeHud : MonoBehaviour
    {
        private const float LogicalHeight = 720f;

        public CombatDirector Director { get; private set; }
        public CombatConfig Config { get; private set; }
        public bool ShowError { get; private set; }
        public string ErrorText { get; private set; } = string.Empty;
        public int DrawCount { get; private set; }
        public string StatusLine { get; private set; } = string.Empty;

        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _title;
        private GUIStyle _mono;
        private Font _uiFont;
        private bool _stylesReady;
        private float _fpsSmoothed = 60f;

        private static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.08f, 0.82f);
        private static readonly Color PanelBorder = new Color(0.32f, 0.42f, 0.52f, 0.9f);
        private static readonly Color TextColor = new Color(0.92f, 0.95f, 1f, 1f);
        private static readonly Color MutedColor = new Color(0.62f, 0.70f, 0.78f, 1f);
        private static readonly Color HpColor = new Color(0.25f, 0.85f, 0.42f, 1f);
        private static readonly Color EnemyHpColor = new Color(0.95f, 0.42f, 0.35f, 1f);
        private static readonly Color DefColor = new Color(0.45f, 0.78f, 1f, 1f);
        private static readonly Color BuffColor = new Color(1f, 0.72f, 0.25f, 1f);
        private static readonly Color GoodColor = new Color(0.55f, 1f, 0.65f, 1f);

        public void Initialize(CombatDirector director, CombatConfig config)
        {
            Director = director;
            Config = config;
            ShowError = false;
            ErrorText = string.Empty;
        }

        public void ShowErrorState(string message)
        {
            ShowError = true;
            ErrorText = message ?? "未知错误";
            Debug.LogError("[CK] HUD 错误状态：" + ErrorText);
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }
            _uiFont = LoadChineseFont();
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft
            };
            if (_uiFont != null)
            {
                _label.font = _uiFont;
            }
            _label.normal.textColor = TextColor;
            _small = new GUIStyle(_label) { fontSize = 13, clipping = TextClipping.Overflow };
            _small.normal.textColor = MutedColor;
            _title = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            _mono = new GUIStyle(_label) { fontSize = 13, fontStyle = FontStyle.Normal, clipping = TextClipping.Overflow };
            _mono.normal.textColor = new Color(0.78f, 0.88f, 0.95f);
            _stylesReady = true;
        }

        /// <summary>优先使用系统中文字体；找不到时回退到 Unity 内置字体（可能不含中文）。</summary>
        private static Font LoadChineseFont()
        {
            string[] candidates =
            {
                "Microsoft YaHei", "Microsoft YaHei UI", "微软雅黑",
                "SimHei", "黑体", "Noto Sans CJK SC", "Arial Unicode MS"
            };
            try
            {
                Font font = Font.CreateDynamicFontFromOSFont(candidates, 16);
                if (font != null)
                {
                    return font;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[CK] 中文字体加载失败：" + ex.Message);
            }
            try
            {
                return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private void Update()
        {
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.1f);
        }

        private void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Max(0.5f, Screen.height / LogicalHeight);
            float logicalWidth = Screen.width / scale;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (ShowError)
            {
                DrawError(logicalWidth);
                GUI.matrix = previous;
                return;
            }
            if (Director == null || Config == null)
            {
                GUI.matrix = previous;
                return;
            }

            ActorSnapshot player = Director.PlayerSnapshot;
            ActorSnapshot enemy = Director.EnemySnapshot;
            if (player == null || enemy == null)
            {
                GUI.matrix = previous;
                return;
            }

            DrawHeader(logicalWidth, player);
            DrawPlayerPanel(player);
            DrawEnemyPanel(enemy);
            DrawRightPanel(logicalWidth, player, enemy);
            DrawBottomBar(logicalWidth);
            if (Director.InventoryVisible)
            {
                DrawInventory(logicalWidth);
            }
            if (Director.MapVisible)
            {
                DrawMap(logicalWidth);
            }
            DrawCount++;
            GUI.matrix = previous;
        }

        private void DrawError(float logicalWidth)
        {
            Box(new Rect(60, 120, logicalWidth - 120, 420), new Color(0.25f, 0.02f, 0.02f, 0.95f));
            Label(new Rect(90, 145, logicalWidth - 180, 40), "配置 / 原生错误 - 原型拒绝运行", _title, new Color(1f, 0.5f, 0.45f));
            Label(new Rect(90, 200, logicalWidth - 180, 300), ErrorText, _label, TextColor);
            Label(new Rect(90, 510, logicalWidth - 180, 30), "修复 Config/CombatPrototype.json 或运行 Tools/build_native.ps1，然后重新进入播放模式。", _small, MutedColor);
        }

        private void DrawHeader(float logicalWidth, ActorSnapshot player)
        {
            Box(new Rect(14, 10, 500, 92), PanelColor);
            Label(new Rect(26, 16, 470, 28), "小鸡杀手 · 战斗原型", _title, TextColor);
            string device = SystemInfo.graphicsDeviceType.ToString();
            Label(new Rect(26, 47, 470, 18),
                $"帧 {Director.Tick}   帧率 {_fpsSmoothed:0}   {Screen.width}×{Screen.height}", _small, MutedColor);
            Label(new Rect(26, 66, 478, 18),
                $"图形：{device}   配置 {Config.RawHash:X8}   原生 {Director.Bridge.NativeConfigHash:X8}", _mono,
                device == "Direct3D11" ? GoodColor : new Color(1f, 0.8f, 0.4f));
            if (!Director.Bridge.IsLoaded)
            {
                Label(new Rect(26, 82, 478, 18), "原生 DLL 未加载", _small, new Color(1f, 0.4f, 0.4f));
            }
            StatusLine = $"帧 {Director.Tick} 生命={player.Health:0} 防御={player.EffectiveDefense:0}";
        }

        private void DrawPlayerPanel(ActorSnapshot player)
        {
            var rect = new Rect(14, 112, 500, 218);
            Box(rect, PanelColor);
            Label(new Rect(26, 118, 300, 24), "玩家", _title, TextColor);
            DrawBar(new Rect(26, 148, 474, 18), player.MaxHealth > 0 ? player.Health / player.MaxHealth : 0f, HpColor);
            Label(new Rect(26, 148, 474, 18), $"生命 {player.Health:0} / {player.MaxHealth:0}", _small, Color.white);
            Label(new Rect(26, 172, 230, 18), $"防御 {player.EffectiveDefense:0}（基础 {Config.Actors.Player.Defense:0}）", _label, DefColor);
            Label(new Rect(266, 172, 232, 18), $"连段 {player.ComboStep}/{Mathf.Max(1, player.ComboCount)}  窗口 {player.ComboWindowRemaining}帧", _label, TextColor);
            AbilityConfig ability = Director.GetAbility(player.AbilityIndex);
            string state = player.Blocking ? "格挡" : player.Invulnerable ? "闪避无敌" : player.Busy
                ? $"动作 {(ability != null ? ability.DisplayName : "-")}@{player.AbilityFrame}（{CombatText.Phase(player.Phase)}）"
                : "待机";
            Label(new Rect(26, 194, 474, 18), $"状态 {state}", _label, player.Blocking || player.Invulnerable ? GoodColor : TextColor);
            Label(new Rect(26, 216, 474, 18), $"位置 {Director.Player.transform.position.x:0.00}, {Director.Player.transform.position.y:0.00}, {Director.Player.transform.position.z:0.00}   速度 {Director.PlayerPlanarSpeed:0.00} 米/秒", _small, MutedColor);
            Label(new Rect(26, 234, 474, 18), $"敌人距离 {Director.DistanceToEnemy:0.00} 米   身体接触 {(Director.PlayerTouchingEnemy ? "是" : "否")}   碰撞标志 {CombatText.Collision(Director.Player.LastCollisionFlags)}", _small, MutedColor);
            string buff = player.BuffActive ? $"自身增益 ×{player.BuffCount} 剩余 {player.BuffRemainingFrames}帧" : "自身增益：无";
            Label(new Rect(26, 252, 474, 18), buff, _small, player.BuffActive ? BuffColor : MutedColor);
            Label(new Rect(26, 270, 474, 18), $"攻击次数 {player.TotalHits}  最近伤害 {Director.LastDamage}  最近事件 {CombatText.Event(Director.LastEventType)}", _small, MutedColor);
            Label(new Rect(26, 288, 474, 30), $"输入：{(string.IsNullOrEmpty(Director.LastInputFeedback) ? "等待" : Director.LastInputFeedback)}", _small, MutedColor);
        }

        private void DrawEnemyPanel(ActorSnapshot enemy)
        {
            var rect = new Rect(14, 340, 500, 190);
            Box(rect, PanelColor);
            Label(new Rect(26, 346, 300, 24), "敌人", _title, new Color(1f, 0.78f, 0.72f));
            DrawBar(new Rect(26, 376, 474, 18), enemy.MaxHealth > 0 ? enemy.Health / enemy.MaxHealth : 0f, EnemyHpColor);
            Label(new Rect(26, 376, 474, 18), $"生命 {enemy.Health:0} / {enemy.MaxHealth:0}", _small, Color.white);
            Label(new Rect(26, 400, 230, 18), $"防御 {enemy.EffectiveDefense:0}（基础 {Config.Actors.Enemy.Defense:0}）", _label, DefColor);
            Label(new Rect(266, 400, 232, 18), $"防御修正 {enemy.EffectiveDefense - enemy.BaseDefense:+0;-0;0}", _label, enemy.EffectiveDefense < enemy.BaseDefense ? BuffColor : TextColor);
            if (enemy.BuffActive)
            {
                string buffName = Config.Buffs.Count > 0 ? Config.Buffs[0].DisplayName : "增益";
                Label(new Rect(26, 422, 474, 20),
                    $"增益 {buffName}  剩余 {enemy.BuffRemainingFrames}帧 / {enemy.BuffRemainingFrames / (float)Config.TickRate:0.0}秒",
                    _label, BuffColor);
            }
            else
            {
                Label(new Rect(26, 422, 474, 20), "增益：无", _label, MutedColor);
            }
            string enemyState = enemy.Busy ? $"挥剑 {CombatText.Phase(enemy.Phase)} {enemy.AbilityFrame}帧" : "待机";
            Label(new Rect(26, 446, 474, 18), $"状态 {enemyState}   挥剑记录 {Director.FormatSwingIntervals()}", _small, TextColor);
            Label(new Rect(26, 464, 474, 18), $"最近事件：{Director.LastEventText}", _small, MutedColor);
            Label(new Rect(26, 484, 474, 18), $"命中：造成 {Director.HitDealtCount} / 承受 {Director.HitTakenCount} / 格挡 {Director.BlockedCount} / 闪避 {Director.DodgedCount}", _small, MutedColor);
            if (enemy.Health <= 0 || Director.PlayerSnapshot.Health <= 0)
            {
                Label(new Rect(26, 502, 474, 18), "生命归零（原型无死亡流程）", _small, new Color(1f, 0.5f, 0.5f));
            }
        }

        private void DrawRightPanel(float logicalWidth, ActorSnapshot player, ActorSnapshot enemy)
        {
            float x = logicalWidth - 514;
            var rect = new Rect(x, 10, 500, 486);
            Box(rect, PanelColor);
            Label(new Rect(x + 12, 16, 300, 24), "武器 / 技能冷却", _title, TextColor);

            float y = 48;
            int slots = Config.Loadout.WeaponSlots;
            for (int i = 0; i < slots; i++)
            {
                bool current = i == Director.WeaponSlotIndex;
                WeaponConfig weapon = i < Config.Weapons.Count ? Config.Weapons[i] : null;
                Color color = current ? GoodColor : MutedColor;
                string text = $"槽位 {i + 1}/{slots}  {(weapon != null ? weapon.DisplayName : "空")}";
                if (weapon != null)
                {
                    text += $"  长 {weapon.Length:0.##}米 半径 {weapon.Radius:0.###}米";
                }
                Label(new Rect(x + 16, y, 466, 18), (current ? "> " : "  ") + text, _small, color);
                y += 22;
            }
            y += 6;
            Label(new Rect(x + 16, y, 466, 18), $"Tab 切换（动作中锁定）  已切换 {Director.WeaponSwitches}  已拒绝 {Director.WeaponSwitchRejections}", _small, MutedColor);
            y += 26;

            Label(new Rect(x + 16, y, 466, 18), "技能", _label, TextColor);
            y += 22;
            DrawSkillRow(x + 16, y, "Q", AbilitySlot.Skill1, player);
            y += 22;
            DrawSkillRow(x + 16, y, "E", AbilitySlot.Skill2, player);
            y += 22;
            DrawSkillRow(x + 16, y, "R", AbilitySlot.Skill3, player);
            y += 22;
            DrawSkillRow(x + 16, y, "F", AbilitySlot.Skill4, player);
            y += 28;

            Label(new Rect(x + 16, y, 466, 18), $"物品栏 {Config.Loadout.ItemBarSlots} 格（1-4，空）  最近 {Director.LastItemSlot}", _label, TextColor);
            y += 22;
            for (int i = 0; i < Config.Loadout.ItemBarSlots; i++)
            {
                bool used = Director.LastItemSlot == i + 1;
                var slotRect = new Rect(x + 16 + i * 116, y, 108, 30);
                Box(slotRect, used ? new Color(0.18f, 0.32f, 0.45f, 0.95f) : new Color(0.08f, 0.12f, 0.17f, 0.9f));
                Label(new Rect(slotRect.x + 8, slotRect.y + 6, slotRect.width - 12, 20), $"{i + 1}  空", _small, used ? Color.white : MutedColor);
            }
            y += 44;

            Label(new Rect(x + 16, y, 466, 18), "最近事件（原生）", _label, TextColor);
            y += 22;
            int start = Mathf.Max(0, Director.RecentEvents.Count - 5);
            for (int i = start; i < Director.RecentEvents.Count; i++)
            {
                CombatEvent evt = Director.RecentEvents[i];
                Label(new Rect(x + 16, y, 466, 16), $"第{evt.Tick}帧 {CombatText.Event(evt.Type)} 值={evt.Amount:0} 重叠={(evt.Overlap ? 1 : 0)} 防{evt.DefenseBefore}->{evt.DefenseAfter}", _small, MutedColor);
                y += 15;
            }

            Label(new Rect(x + 16, 468, 466, 18), $"敌人防御 {enemy.EffectiveDefense:0} / 玩家防御 {player.EffectiveDefense:0}", _small, DefColor);
        }

        private void DrawSkillRow(float x, float y, string key, AbilitySlot slot, ActorSnapshot player)
        {
            AbilityConfig ability = Config.FindAbilityBySlot(slot);
            if (ability == null)
            {
                Label(new Rect(x, y, 466, 18), $"[{key}] <未配置>", _small, new Color(1f, 0.5f, 0.5f));
                return;
            }
            int cooldown = player.CooldownBySlot[(int)slot];
            bool ready = cooldown <= 0;
            string text = $"[{key}] {ability.DisplayName}  ×{ability.Multiplier:0.##}";
            text += ready ? "  就绪" : $"  冷却 {cooldown}帧 / {cooldown / (float)Config.TickRate:0.0}秒";
            Label(new Rect(x, y, 300, 18), text, _label, ready ? GoodColor : new Color(1f, 0.65f, 0.4f));
            Label(new Rect(x + 320, y, 146, 18), $"{ability.TotalFrames}帧", _small, MutedColor);
        }

        private void DrawBottomBar(float logicalWidth)
        {
            float x = 14;
            float y = 560;
            Box(new Rect(x, y, logicalWidth - 28, 64), PanelColor);
            Label(new Rect(x + 12, y + 6, logicalWidth - 60, 18),
                "WASD 移动 | 左键点按 三段 | 左键长按 重击 | 右键/左Shift 闪避 | 空格 格挡 | Q/E/R/F 技能 | Tab 武器 | B 背包 | M 地图 | 1-4 物品栏",
                _label, TextColor);
            Label(new Rect(x + 12, y + 28, logicalWidth - 60, 18),
                $"最近事件：{Director.LastEventText}   最近伤害 {Director.LastDamage}   重叠 {(Director.LastOverlap ? "是" : "否")}",
                _small, Director.LastOverlap ? BuffColor : MutedColor);
            Label(new Rect(x + 12, y + 46, logicalWidth - 60, 16),
                $"输入 {Director.Input?.BackendName}   固定相机   场景 战斗原型   对象：平面 / 玩家圆柱 / 敌人圆柱 / 两把剑   音效计数 {Director.Audio?.PlayedCueCount ?? 0}",
                _small, MutedColor);
        }

        private void DrawInventory(float logicalWidth)
        {
            float width = Mathf.Min(1180f, logicalWidth - 60f);
            float x = (logicalWidth - width) * 0.5f;
            var rect = new Rect(x, 70, width, 640);
            Box(rect, new Color(0.02f, 0.03f, 0.05f, 0.95f));
            Label(new Rect(x + 24, 82, width - 48, 30), $"背包 / 装备 / 武器（B 关闭）  {Config.Loadout.InventorySlots} 格 / {Config.Loadout.EquipmentSlots} 装备 / {Config.Loadout.WeaponSlots} 武器", _title, TextColor);

            float gridX = x + 24;
            float gridY = 122;
            int columns = 10;
            float cellW = (width - 380f) / columns;
            float cellH = 54f;
            for (int i = 0; i < Config.Loadout.InventorySlots; i++)
            {
                int row = i / columns;
                int col = i % columns;
                var cell = new Rect(gridX + col * cellW, gridY + row * cellH, cellW - 6, cellH - 6);
                Box(cell, new Color(0.07f, 0.10f, 0.14f, 0.95f));
                Label(new Rect(cell.x + 6, cell.y + 4, cell.width - 8, 18), $"{i + 1:00}", _small, MutedColor);
                Label(new Rect(cell.x + 6, cell.y + 24, cell.width - 8, 18), "空", _small, new Color(0.4f, 0.46f, 0.52f));
            }

            float sideX = gridX + columns * cellW + 16;
            float sideY = gridY;
            Label(new Rect(sideX, sideY, 340, 20), "装备", _label, TextColor);
            sideY += 24;
            for (int i = 0; i < Config.Loadout.EquipmentSlots; i++)
            {
                Box(new Rect(sideX, sideY, 330, 42), new Color(0.08f, 0.12f, 0.17f, 0.95f));
                Label(new Rect(sideX + 10, sideY + 10, 300, 20), $"装备 {i + 1}/{Config.Loadout.EquipmentSlots}  空", _small, MutedColor);
                sideY += 48;
            }
            sideY += 10;
            Label(new Rect(sideX, sideY, 340, 20), "武器", _label, TextColor);
            sideY += 24;
            for (int i = 0; i < Config.Loadout.WeaponSlots; i++)
            {
                WeaponConfig weapon = i < Config.Weapons.Count ? Config.Weapons[i] : null;
                Box(new Rect(sideX, sideY, 330, 42), i == Director.WeaponSlotIndex ? new Color(0.12f, 0.24f, 0.32f, 0.95f) : new Color(0.08f, 0.12f, 0.17f, 0.95f));
                Label(new Rect(sideX + 10, sideY + 10, 300, 20), $"武器 {i + 1}/{Config.Loadout.WeaponSlots}  {(weapon != null ? weapon.DisplayName : "空")}", _small, i == Director.WeaponSlotIndex ? GoodColor : MutedColor);
                sideY += 48;
            }
            Label(new Rect(x + 24, rect.y + rect.height - 34, width - 48, 20), "阶段一仅数据骨架与占位界面：不支持拖拽、拾取或穿脱。", _small, MutedColor);
        }

        private void DrawMap(float logicalWidth)
        {
            float width = Mathf.Min(980f, logicalWidth - 80f);
            float x = (logicalWidth - width) * 0.5f;
            var rect = new Rect(x, 100, width, 520);
            Box(rect, new Color(0.03f, 0.04f, 0.06f, 0.96f));
            Label(new Rect(x + 24, 116, width - 48, 30), "地图占位（M 关闭）", _title, TextColor);
            Box(new Rect(x + 24, 160, width - 48, 360), new Color(0.08f, 0.12f, 0.16f, 0.95f));
            Label(new Rect(x + 44, 180, width - 88, 28), "地图占位", _title, MutedColor);
            Label(new Rect(x + 44, 254, width - 88, 80),
                "阶段一不实现地图、寻路或传送。\n此面板只验证 M 键事件、面板开关与界面响应。", _label, TextColor);
            Label(new Rect(x + 44, 354, width - 88, 20),
                $"玩家 ({Director.Player.transform.position.x:0.0}, {Director.Player.transform.position.z:0.0})   敌人 ({Director.Enemy.transform.position.x:0.0}, {Director.Enemy.transform.position.z:0.0})   地面 {Config.World.GroundSize:0}米", _small, MutedColor);
            Label(new Rect(x + 24, 566, width - 48, 20), "M/B 面板不暂停战斗；关闭后继续。", _small, MutedColor);
        }

        private void Box(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
            Color borderPrev = GUI.color;
            GUI.color = PanelBorder;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), Texture2D.whiteTexture);
            GUI.color = borderPrev;
        }

        private void DrawBar(Rect rect, float fraction, Color fill)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.12f, 0.15f, 0.18f, 0.95f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void Label(Rect rect, string text, GUIStyle style, Color color)
        {
            rect.height += 6f;
            Color previous = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
            style.normal.textColor = previous;
        }
    }
}
