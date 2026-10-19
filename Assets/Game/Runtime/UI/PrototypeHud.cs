using System.Collections.Generic;
using UnityEngine;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Runtime.Combat;

namespace CK.Runtime.UI
{
    /// <summary>
    /// IMGUI prototype HUD. Deliberately dependency-free so screenshots always show
    /// the authoritative values coming back from the native core.
    /// English labels are used because the standalone player has no CJK font fallback.
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
            ErrorText = message ?? "unknown error";
            Debug.LogError("[CK] HUD error state: " + ErrorText);
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                richText = false,
                wordWrap = false,
                clipping = TextClipping.Overflow,
                alignment = TextAnchor.UpperLeft
            };
            _label.normal.textColor = TextColor;
            _small = new GUIStyle(_label) { fontSize = 13, clipping = TextClipping.Overflow };
            _small.normal.textColor = MutedColor;
            _title = new GUIStyle(_label) { fontSize = 22, fontStyle = FontStyle.Bold, clipping = TextClipping.Overflow };
            _mono = new GUIStyle(_label) { fontSize = 13, fontStyle = FontStyle.Normal, clipping = TextClipping.Overflow };
            _mono.normal.textColor = new Color(0.78f, 0.88f, 0.95f);
            _stylesReady = true;
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
            Label(new Rect(90, 145, logicalWidth - 180, 40), "CONFIG / NATIVE ERROR - PROTOTYPE REFUSED TO RUN", _title, new Color(1f, 0.5f, 0.45f));
            Label(new Rect(90, 200, logicalWidth - 180, 300), ErrorText, _label, TextColor);
            Label(new Rect(90, 510, logicalWidth - 180, 30), "Fix Config/CombatPrototype.json or run Tools/build_native.ps1, then re-enter Play Mode.", _small, MutedColor);
        }

        private void DrawHeader(float logicalWidth, ActorSnapshot player)
        {
            Box(new Rect(14, 10, 500, 92), PanelColor);
            Label(new Rect(26, 16, 470, 28), "CHICKEN KILLER - PROTOTYPE", _title, TextColor);
            string device = SystemInfo.graphicsDeviceType.ToString();
            Label(new Rect(26, 47, 470, 18),
                $"tick {Director.Tick}   FPS {_fpsSmoothed:0}   {Screen.width}x{Screen.height}", _small, MutedColor);
            Label(new Rect(26, 66, 478, 18),
                $"DX: {device}   config {Config.RawHash:X8}   native {Director.Bridge.NativeConfigHash:X8}", _mono,
                device == "Direct3D11" ? GoodColor : new Color(1f, 0.8f, 0.4f));
            if (!Director.Bridge.IsLoaded)
            {
                Label(new Rect(26, 82, 478, 18), "NATIVE DLL NOT LOADED", _small, new Color(1f, 0.4f, 0.4f));
            }
            StatusLine = $"tick {Director.Tick} hp={player.Health:0} def={player.EffectiveDefense:0}";
        }

        private void DrawPlayerPanel(ActorSnapshot player)
        {
            var rect = new Rect(14, 112, 500, 218);
            Box(rect, PanelColor);
            Label(new Rect(26, 118, 300, 24), $"PLAYER  {Config.Actors.Player.DisplayName}", _title, TextColor);
            DrawBar(new Rect(26, 148, 474, 18), player.MaxHealth > 0 ? player.Health / player.MaxHealth : 0f, HpColor);
            Label(new Rect(26, 148, 474, 18), $"HP {player.Health:0} / {player.MaxHealth:0}", _small, Color.white);
            Label(new Rect(26, 172, 230, 18), $"DEF {player.EffectiveDefense:0} (base {Config.Actors.Player.Defense:0})", _label, DefColor);
            Label(new Rect(266, 172, 232, 18), $"COMBO {player.ComboStep}/{Mathf.Max(1, player.ComboCount)}  window {player.ComboWindowRemaining}f", _label, TextColor);
            string state = player.Blocking ? "BLOCKING" : player.Invulnerable ? "DODGE INVULN" : player.Busy ? $"ACTION {Director.GetAbility(player.AbilityIndex)?.Id}@{player.AbilityFrame}({player.PhaseName})" : "IDLE";
            Label(new Rect(26, 194, 474, 18), $"STATE {state}", _label, player.Blocking || player.Invulnerable ? GoodColor : TextColor);
            Label(new Rect(26, 216, 474, 18), $"POS {Director.Player.transform.position.x:0.00}, {Director.Player.transform.position.y:0.00}, {Director.Player.transform.position.z:0.00}   SPEED {Director.PlayerPlanarSpeed:0.00} m/s", _small, MutedColor);
            Label(new Rect(26, 234, 474, 18), $"ENEMY DIST {Director.DistanceToEnemy:0.00} m   BODY CONTACT {(Director.PlayerTouchingEnemy ? "YES" : "NO")}   collisionFlags {Director.Player.LastCollisionFlags}", _small, MutedColor);
            string buff = player.BuffActive ? $"SELF BUFF x{player.BuffCount} left {player.BuffRemainingFrames}f" : "SELF BUFF: none";
            Label(new Rect(26, 252, 474, 18), buff, _small, player.BuffActive ? BuffColor : MutedColor);
            Label(new Rect(26, 270, 474, 18), $"ATTACKS {player.TotalHits}  LAST DMG {Director.LastDamage}  LAST EVENT {Director.LastEventType}", _small, MutedColor);
            Label(new Rect(26, 288, 474, 30), $"INPUT: {(string.IsNullOrEmpty(Director.LastInputFeedback) ? "waiting" : Director.LastInputFeedback)}", _small, MutedColor);
        }

        private void DrawEnemyPanel(ActorSnapshot enemy)
        {
            var rect = new Rect(14, 340, 500, 190);
            Box(rect, PanelColor);
            Label(new Rect(26, 346, 300, 24), $"ENEMY  {Config.Actors.Enemy.DisplayName}", _title, new Color(1f, 0.78f, 0.72f));
            DrawBar(new Rect(26, 376, 474, 18), enemy.MaxHealth > 0 ? enemy.Health / enemy.MaxHealth : 0f, EnemyHpColor);
            Label(new Rect(26, 376, 474, 18), $"HP {enemy.Health:0} / {enemy.MaxHealth:0}", _small, Color.white);
            Label(new Rect(26, 400, 230, 18), $"DEF {enemy.EffectiveDefense:0} (base {Config.Actors.Enemy.Defense:0})", _label, DefColor);
            Label(new Rect(266, 400, 232, 18), $"DEF MOD {enemy.EffectiveDefense - enemy.BaseDefense:+0;-0;0}", _label, enemy.EffectiveDefense < enemy.BaseDefense ? BuffColor : TextColor);
            if (enemy.BuffActive)
            {
                string buffName = Config.Buffs.Count > 0 ? Config.Buffs[0].DisplayName : "Buff";
                Label(new Rect(26, 422, 474, 20),
                    $"BUFF {buffName}  LEFT {enemy.BuffRemainingFrames}f / {enemy.BuffRemainingFrames / (float)Config.TickRate:0.0}s",
                    _label, BuffColor);
            }
            else
            {
                Label(new Rect(26, 422, 474, 20), "BUFF: none", _label, MutedColor);
            }
            string enemyState = enemy.Busy ? $"SWING {enemy.PhaseName} f{enemy.AbilityFrame}" : "IDLE";
            Label(new Rect(26, 446, 474, 18), $"STATE {enemyState}   SWINGS {Director.FormatSwingIntervals()}", _small, TextColor);
            Label(new Rect(26, 464, 474, 18), $"LAST EVENT: {Director.LastEventText}", _small, MutedColor);
            Label(new Rect(26, 484, 474, 18), $"HITS dealt {Director.HitDealtCount} / taken {Director.HitTakenCount} / blocked {Director.BlockedCount} / dodged {Director.DodgedCount}", _small, MutedColor);
            if (enemy.Health <= 0 || Director.PlayerSnapshot.Health <= 0)
            {
                Label(new Rect(26, 502, 474, 18), "HP ZERO (prototype has no death flow)", _small, new Color(1f, 0.5f, 0.5f));
            }
        }

        private void DrawRightPanel(float logicalWidth, ActorSnapshot player, ActorSnapshot enemy)
        {
            float x = logicalWidth - 514;
            var rect = new Rect(x, 10, 500, 486);
            Box(rect, PanelColor);
            Label(new Rect(x + 12, 16, 300, 24), "WEAPONS / SKILL COOLDOWNS", _title, TextColor);

            float y = 48;
            int slots = Config.Loadout.WeaponSlots;
            for (int i = 0; i < slots; i++)
            {
                bool current = i == Director.WeaponSlotIndex;
                WeaponConfig weapon = i < Config.Weapons.Count ? Config.Weapons[i] : null;
                Color color = current ? GoodColor : MutedColor;
                string text = $"SLOT {i + 1}/{slots}  {(weapon != null ? weapon.DisplayName : "EMPTY")}";
                if (weapon != null)
                {
                    text += $"  len {weapon.Length:0.##}m  r {weapon.Radius:0.###}m";
                }
                Label(new Rect(x + 16, y, 466, 18), (current ? "> " : "  ") + text, _label, color);
                y += 22;
            }
            y += 6;
            Label(new Rect(x + 16, y, 466, 18), $"Tab switch (locked during action)  switched {Director.WeaponSwitches}  rejected {Director.WeaponSwitchRejections}", _small, MutedColor);
            y += 26;

            Label(new Rect(x + 16, y, 466, 18), "SKILLS", _label, TextColor);
            y += 22;
            DrawSkillRow(x + 16, y, "Q", AbilitySlot.Skill1, player);
            y += 22;
            DrawSkillRow(x + 16, y, "E", AbilitySlot.Skill2, player);
            y += 22;
            DrawSkillRow(x + 16, y, "R", AbilitySlot.Skill3, player);
            y += 22;
            DrawSkillRow(x + 16, y, "F", AbilitySlot.Skill4, player);
            y += 28;

            Label(new Rect(x + 16, y, 466, 18), $"ITEM BAR {Config.Loadout.ItemBarSlots} slots (1-4, empty)  last {Director.LastItemSlot}", _label, TextColor);
            y += 22;
            for (int i = 0; i < Config.Loadout.ItemBarSlots; i++)
            {
                bool used = Director.LastItemSlot == i + 1;
                var slotRect = new Rect(x + 16 + i * 116, y, 108, 30);
                Box(slotRect, used ? new Color(0.18f, 0.32f, 0.45f, 0.95f) : new Color(0.08f, 0.12f, 0.17f, 0.9f));
                Label(new Rect(slotRect.x + 8, slotRect.y + 6, slotRect.width - 12, 20), $"{i + 1}  EMPTY", _small, used ? Color.white : MutedColor);
            }
            y += 44;

            Label(new Rect(x + 16, y, 466, 18), "RECENT EVENTS (native)", _label, TextColor);
            y += 22;
            int start = Mathf.Max(0, Director.RecentEvents.Count - 8);
            for (int i = start; i < Director.RecentEvents.Count; i++)
            {
                CombatEvent evt = Director.RecentEvents[i];
                Label(new Rect(x + 16, y, 466, 16), $"t{evt.Tick} {evt.Type} dmg={evt.Amount:0} overlap={(evt.Overlap ? 1 : 0)} def={evt.DefenseBefore}->{evt.DefenseAfter}", _small, MutedColor);
                y += 15;
            }

            Label(new Rect(x + 16, 468, 466, 18), $"ENEMY DEF {enemy.EffectiveDefense:0} / PLAYER DEF {player.EffectiveDefense:0}", _small, DefColor);
        }

        private void DrawSkillRow(float x, float y, string key, AbilitySlot slot, ActorSnapshot player)
        {
            AbilityConfig ability = Config.FindAbilityBySlot(slot);
            if (ability == null)
            {
                Label(new Rect(x, y, 466, 18), $"[{key}] <not configured>", _small, new Color(1f, 0.5f, 0.5f));
                return;
            }
            int cooldown = player.CooldownBySlot[(int)slot];
            bool ready = cooldown <= 0;
            string text = $"[{key}] {ability.DisplayName}  x{ability.Multiplier:0.##}";
            text += ready ? "  READY" : $"  CD {cooldown}f / {cooldown / (float)Config.TickRate:0.0}s";
            Label(new Rect(x, y, 300, 18), text, _label, ready ? GoodColor : new Color(1f, 0.65f, 0.4f));
            Label(new Rect(x + 320, y, 146, 18), $"{ability.TotalFrames}f", _small, MutedColor);
        }

        private void DrawBottomBar(float logicalWidth)
        {
            float x = 14;
            float y = 560;
            Box(new Rect(x, y, logicalWidth - 28, 64), PanelColor);
            Label(new Rect(x + 12, y + 6, logicalWidth - 60, 18),
                "WASD move | LMB tap 3-hit combo | LMB hold heavy | RMB/LShift dodge | hold Space block | Q/E/R/F skills | Tab weapon | B inventory | M map | 1-4 item bar",
                _label, TextColor);
            Label(new Rect(x + 12, y + 28, logicalWidth - 60, 18),
                $"LAST EVENT: {Director.LastEventText}   LAST DMG {Director.LastDamage}   overlap {(Director.LastOverlap ? "true" : "false")}",
                _small, Director.LastOverlap ? BuffColor : MutedColor);
            Label(new Rect(x + 12, y + 46, logicalWidth - 60, 16),
                $"INPUT {Director.Input?.BackendName}   FIXED CAMERA   SCENE CombatPrototype   OBJECTS: plane / player cylinder / enemy cylinder / two swords",
                _small, MutedColor);
            Label(new Rect(logicalWidth - 230, y + 6, 210, 18), $"AUDIO CUES {Director.Audio?.PlayedCueCount ?? 0}", _small, MutedColor);
        }

        private void DrawInventory(float logicalWidth)
        {
            float width = Mathf.Min(1180f, logicalWidth - 60f);
            float x = (logicalWidth - width) * 0.5f;
            var rect = new Rect(x, 70, width, 640);
            Box(rect, new Color(0.02f, 0.03f, 0.05f, 0.95f));
            Label(new Rect(x + 24, 82, width - 48, 30), $"INVENTORY / EQUIPMENT / WEAPONS  (B to close)  {Config.Loadout.InventorySlots} slots / {Config.Loadout.EquipmentSlots} equipment / {Config.Loadout.WeaponSlots} weapons", _title, TextColor);

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
                Label(new Rect(cell.x + 6, cell.y + 24, cell.width - 8, 18), "empty", _small, new Color(0.4f, 0.46f, 0.52f));
            }

            float sideX = gridX + columns * cellW + 16;
            float sideY = gridY;
            Label(new Rect(sideX, sideY, 340, 20), "EQUIPMENT", _label, TextColor);
            sideY += 24;
            for (int i = 0; i < Config.Loadout.EquipmentSlots; i++)
            {
                Box(new Rect(sideX, sideY, 330, 42), new Color(0.08f, 0.12f, 0.17f, 0.95f));
                Label(new Rect(sideX + 10, sideY + 10, 300, 20), $"EQUIP {i + 1}/{Config.Loadout.EquipmentSlots}  EMPTY", _small, MutedColor);
                sideY += 48;
            }
            sideY += 10;
            Label(new Rect(sideX, sideY, 340, 20), "WEAPONS", _label, TextColor);
            sideY += 24;
            for (int i = 0; i < Config.Loadout.WeaponSlots; i++)
            {
                WeaponConfig weapon = i < Config.Weapons.Count ? Config.Weapons[i] : null;
                Box(new Rect(sideX, sideY, 330, 42), i == Director.WeaponSlotIndex ? new Color(0.12f, 0.24f, 0.32f, 0.95f) : new Color(0.08f, 0.12f, 0.17f, 0.95f));
                Label(new Rect(sideX + 10, sideY + 10, 300, 20), $"WEAPON {i + 1}/{Config.Loadout.WeaponSlots}  {(weapon != null ? weapon.DisplayName : "EMPTY")}", _small, i == Director.WeaponSlotIndex ? GoodColor : MutedColor);
                sideY += 48;
            }
            Label(new Rect(x + 24, rect.y + rect.height - 34, width - 48, 20), "Phase 1 data skeleton and placeholder UI: no drag/drop, pickup or equip toggling.", _small, MutedColor);
        }

        private void DrawMap(float logicalWidth)
        {
            float width = Mathf.Min(980f, logicalWidth - 80f);
            float x = (logicalWidth - width) * 0.5f;
            var rect = new Rect(x, 100, width, 520);
            Box(rect, new Color(0.03f, 0.04f, 0.06f, 0.96f));
            Label(new Rect(x + 24, 116, width - 48, 30), "MAP PLACEHOLDER (M to close)", _title, TextColor);
            Box(new Rect(x + 24, 160, width - 48, 360), new Color(0.08f, 0.12f, 0.16f, 0.95f));
            Label(new Rect(x + 44, 180, width - 88, 28), "MAP PLACEHOLDER", _title, MutedColor);
            Label(new Rect(x + 44, 254, width - 88, 80),
                "Phase 1 does not implement map, pathfinding or teleport.\nThis panel only verifies the M key event, panel toggle and HUD responsiveness.", _label, TextColor);
            Label(new Rect(x + 44, 354, width - 88, 20),
                $"PLAYER ({Director.Player.transform.position.x:0.0}, {Director.Player.transform.position.z:0.0})   ENEMY ({Director.Enemy.transform.position.x:0.0}, {Director.Enemy.transform.position.z:0.0})   GROUND {Config.World.GroundSize:0}m", _small, MutedColor);
            Label(new Rect(x + 24, 566, width - 48, 20), "M/B panels do not pause combat; close them to continue.", _small, MutedColor);
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
