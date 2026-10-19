using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Runtime.Bootstrap;
using CK.Runtime.Combat;

namespace CK.Runtime.Automation
{
    /// <summary>
    /// Play Mode gate driver. Runs a deterministic scenario in the real editor/player
    /// loop (input is injected through the Input System), captures Game View
    /// screenshots and writes TestArtifacts/&lt;module&gt;/&lt;timestamp&gt;/summary.json.
    /// </summary>
    public sealed class PrototypeAutomation : MonoBehaviour
    {
        private CombatPrototypeBootstrap _bootstrap;
        private CombatDirector _director;
        private GateEvidence _evidence;
        private AutomationInput _input;
        private string _gate = "M0";
        private bool _screensMode;
        private bool _fatal;
        private bool _finished;
        private int _enemySwingAbilityIndex = -1;
        private int _enemySwingActiveFrame = -1;

        private CombatDirector Director => _director;
        private ActorSnapshot Player => _director != null ? _director.PlayerSnapshot : null;
        private ActorSnapshot Enemy => _director != null ? _director.EnemySnapshot : null;

        public void Begin(CombatPrototypeBootstrap bootstrap, string gate, string evidenceDirectory, bool screensMode)
        {
            _bootstrap = bootstrap;
            _director = bootstrap.Director;
            _screensMode = screensMode;
            _gate = string.IsNullOrEmpty(gate) ? (screensMode ? "M7" : "M0") : gate.Trim();
            string module = _gate;
            string directory = !string.IsNullOrEmpty(evidenceDirectory)
                ? evidenceDirectory
                : GateEvidence.DefaultDirectory(module);
            string gitCommit = CommandLine.GetValue("-ckGitCommit");
            _evidence = new GateEvidence(module, directory, gitCommit);

            string configPath = bootstrap.LoadResult != null ? bootstrap.LoadResult.SourcePath : bootstrap.ConfigPathOverride;
            _evidence.Configure(new CombatConfigLite
            {
                ConfigPath = configPath,
                ConfigHash = bootstrap.Config != null ? bootstrap.Config.RawHash.ToString("X8") : "00000000",
                NativeHash = bootstrap.Director != null && bootstrap.Director.Bridge != null
                    ? bootstrap.Director.Bridge.NativeConfigHash.ToString("X8")
                    : "00000000",
                TickRate = bootstrap.Config != null ? bootstrap.Config.TickRate : 0,
                PlayerBuild = Application.isEditor ? "Editor" : Application.productName
            });
            if (bootstrap.Config != null && bootstrap.Config.TryGetAbilityBySlot(AbilitySlot.EnemyAttack, out AbilityConfig enemyAbility))
            {
                _enemySwingAbilityIndex = bootstrap.Config.FindAbilityIndex(enemyAbility.Id);
                _enemySwingActiveFrame = enemyAbility.ActiveStartFrame;
            }

            RuntimeLogRecorder.Install();
            _input = new AutomationInput();
            Debug.Log($"[CK] gate automation start module={module} dir={directory} screens={screensMode} build={(Application.isEditor ? "Editor" : "Player")}");
            StartCoroutine(RunGate());
        }

        private IEnumerator RunGate()
        {
            yield return null;
            yield return new WaitForFixedUpdate();

            if (_bootstrap == null || _bootstrap.Failed || _director == null || !_director.Initialized)
            {
                string message = _bootstrap != null ? _bootstrap.FailureText : "bootstrap missing";
                _evidence.Fail("prototype not initialized: " + message);
                Finish("initialization failed");
                yield break;
            }

            switch (_gate.ToUpperInvariant())
            {
                case "M0":
                    yield return RunM0();
                    break;
                case "M1":
                    yield return RunM1();
                    break;
                case "M2":
                    yield return RunM2();
                    break;
                case "M3":
                    yield return RunM3();
                    break;
                case "M3_4STEPS":
                case "M3-4STEPS":
                    yield return RunM3FourSteps();
                    break;
                case "M4":
                    yield return RunM4();
                    break;
                case "M5":
                    yield return RunM5();
                    break;
                case "M6":
                    yield return RunM6();
                    break;
                case "M6_4SLOTS":
                case "M6-4SLOTS":
                    yield return RunM6FourSlots();
                    break;
                case "M7":
                    yield return RunM7();
                    break;
                case "STABILITY":
                    yield return RunStability();
                    break;
                default:
                    _evidence.Fail("unknown gate module: " + _gate);
                    break;
            }

            Finish(_fatal ? "scenario aborted after a failed assertion" : "scenario complete");

            if (!Application.isEditor)
            {
                yield return new WaitForSecondsRealtime(2f);
                Application.Quit(_evidence.Summary.Status == "PASS" ? 0 : 1);
            }
        }

        private void Finish(string notes)
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            if (_input != null)
            {
                _input.Release();
            }
            _evidence.Metric("build", Application.isEditor ? "Editor" : "Player");
            _evidence.Metric("unityVersion", Application.unityVersion);
            _evidence.Metric("graphicsDevice", SystemInfo.graphicsDeviceType.ToString());
            _evidence.Metric("graphicsName", SystemInfo.graphicsDeviceName);
            _evidence.Metric("screen", $"{Screen.width}x{Screen.height}");
            _evidence.Metric("platform", Application.platform.ToString());
            _evidence.Metric("automationInputInjections", _input != null ? _input.Injections : 0);
            _evidence.Metric("automationInputError", _input != null ? _input.LastError : string.Empty);
            _evidence.Write(notes);
            Debug.Log("[CK] gate automation finished: " + _evidence.Summary.Status);
        }

        // ------------------------------------------------------------------
        // Scenarios
        // ------------------------------------------------------------------

        private IEnumerator RunM0()
        {
            yield return WaitTicks(20);
            yield return Capture("01_prototype_scene.png", "M0 原型场景：平面、玩家/敌人圆柱、双剑、HUD",
                Expect(("playerHp", 1000), ("enemyHp", 1000), ("playerDef", 50), ("enemyDef", 50),
                    ("build", Application.isEditor ? "Editor" : "Player")));
            yield return WaitTicks(5);
            yield return Capture(Application.isEditor ? "02_dx11_editor.png" : "02_dx11_player.png",
                "M0 DX11 运行：HUD 显示 Direct3D11 与配置哈希",
                Expect(("graphicsDevice", "Direct3D11")));
            _evidence.Metric("sceneObjects", "Ground_Plane,Player,Enemy,2xSwordBlade,PrototypeCamera,DirectionalLight");
            _evidence.Metric("nativeAbi", NativeCombatBridgeAbi());
            _evidence.Metric("nativeBuild", CK.Native.NativeCombatBridge.BuildInfo);
        }

        private IEnumerator RunM1()
        {
            _director.EnemyAttacksEnabled = false;
            yield return WaitTicks(20);
            yield return Capture("01_actors_hud.png", "M1 初始 Actor 与 HUD：HP 1000/1000，DEF 50/50，3 武器槽，按键提示",
                Expect(("playerHp", 1000), ("enemyHp", 1000), ("playerDef", 50), ("enemyDef", 50),
                    ("weaponSlot", 1), ("weaponSlots", 3), ("combo", 0)));

            // Move far out of the enemy's reach first: M1 is an input-mapping gate and
            // must not produce combat damage. All subsequent keys are still sampled
            // through the real input/command path, but the sword cannot reach.
            yield return TapKeys(new AutomationKeys { S = true }, 100);
            yield return WaitTicks(3);
            _evidence.Metric("m1KeyTestZ", Mathf.Round(_director.Player.transform.position.z * 100f) / 100f);

            // Exercise every mapped key through the Input System path and log feedback.
            yield return TapKeys(AutomationKeys.MoveW, 6);
            yield return TapKeys(new AutomationKeys { A = true }, 6);
            yield return TapKeys(new AutomationKeys { S = true }, 6);
            yield return TapKeys(new AutomationKeys { D = true }, 6);
            // Hold block while pressing combat keys: M1 proves the mapping and HUD/log
            // feedback without starting any attack or skill (they are rejected).
            var block = new AutomationKeys { Space = true };
            _input.Apply(block);
            yield return WaitTicks(3);
            yield return TapWhileHolding(block, new AutomationKeys { LeftMouse = true }, 4);
            yield return TapWhileHolding(block, new AutomationKeys { RightMouse = true }, 3);
            yield return TapWhileHolding(block, new AutomationKeys { LeftShift = true }, 3);
            yield return TapWhileHolding(block, new AutomationKeys { Q = true }, 3);
            yield return TapWhileHolding(block, new AutomationKeys { E = true }, 3);
            yield return TapWhileHolding(block, new AutomationKeys { R = true }, 3);
            yield return TapWhileHolding(block, new AutomationKeys { F = true }, 3);
            yield return WaitTicks(5);
            _input.Release();
            yield return WaitTicks(5);
            yield return WaitUntil(() => Player != null && !Player.Busy, 300, "M1 idle before weapon switch");
            yield return TapKeys(new AutomationKeys { Tab = true }, 3);
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit1 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit2 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit3 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit4 = true }, 3);
            yield return WaitTicks(15);
            yield return Capture("02_input_hud.png", "M1 按键映射反馈：移动、技能、武器切换、物品栏 1-4 均有 HUD/日志反馈",
                Expect(("weaponSlot", 2), ("itemSlot", 4), ("itemHistory", "1,2,3,4"), ("weaponSlots", 3),
                    ("itemBarSlots", 4), ("playerHp", 1000), ("enemyHp", 1000), ("combo", 0)));
            _evidence.Metric("keySmokeCovered", "WASD,Space,LMB,RMB,LShift,Q,E,R,F,Tab,M,B,1,2,3,4");
            _evidence.Metric("audioCues", _director.Audio != null ? _director.Audio.PlayedCueCount : 0);
            _director.EnemyAttacksEnabled = true;
        }

        private IEnumerator RunM2()
        {
            yield return ResetState();
            yield return Capture("01_before_move.png", "M2 移动前：玩家 (0,0,0)，敌人固定 (0,0,2)",
                Expect(("playerX", 0f), ("playerZ", 0f), ("enemyHp", 1000), ("touching", false)));

            float startX = _director.Player.transform.position.x;
            yield return TapKeys(AutomationKeys.MoveD, 25);
            yield return WaitTicks(3);
            float movedX = _director.Player.transform.position.x;
            if (movedX - startX < 1.5f)
            {
                _evidence.Fail($"M2 D 方向位移不足: {movedX - startX:0.00}m");
                _fatal = true;
            }
            _evidence.Metric("moveDeltaX", Mathf.Round((movedX - startX) * 100f) / 100f);
            yield return Capture("02_after_move.png", "M2 移动后：WASD 位移与 HUD 坐标同步变化",
                Expect(("playerX", 2.08f), ("playerZ", 0f), ("enemyHp", 1000)));

            // A / S directions (TC-04 requires all four WASD directions).
            yield return ResetState();
            float baseX = _director.Player.transform.position.x;
            yield return TapKeys(new AutomationKeys { A = true }, 25);
            yield return WaitTicks(3);
            float leftDelta = baseX - _director.Player.transform.position.x;
            _evidence.Metric("moveDeltaA", Mathf.Round(leftDelta * 100f) / 100f);
            if (leftDelta < 1.5f)
            {
                _evidence.Fail($"M2 A 方向位移不足: {leftDelta:0.00}m");
                _fatal = true;
            }

            yield return ResetState();
            float baseZ = _director.Player.transform.position.z;
            yield return TapKeys(new AutomationKeys { S = true }, 25);
            yield return WaitTicks(3);
            float backDelta = baseZ - _director.Player.transform.position.z;
            _evidence.Metric("moveDeltaS", Mathf.Round(backDelta * 100f) / 100f);
            if (backDelta < 1.5f)
            {
                _evidence.Fail($"M2 S 方向位移不足: {backDelta:0.00}m");
                _fatal = true;
            }

            yield return ResetState();
            yield return TapKeys(AutomationKeys.MoveW, 70);
            yield return WaitTicks(4);
            float distance = _director.DistanceToEnemy;
            _evidence.Metric("collisionDistance", Mathf.Round(distance * 100f) / 100f);
            if (distance > 1.25f || !_director.PlayerTouchingEnemy)
            {
                _evidence.Fail($"M2 身体碰撞失败: distance={distance:0.00}, touching={_director.PlayerTouchingEnemy}");
                _fatal = true;
            }
            yield return Capture("03_body_collision.png", "M2 身体碰撞：玩家贴住敌人未穿透，HUD 显示距离/接触",
                Expect(("touching", true), ("enemyHp", 1000)));
        }

        private IEnumerator RunM3()
        {
            yield return ResetState();
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 933, 90, "M3 combo hit 1");
            yield return Capture("01_combo_hit_1.png", "M3 连段 1/3：普通攻击命中 67，敌人 HP 933",
                Expect(("enemyHp", 933), ("lastDamage", 67), ("combo", 1), ("lastEvent", "Hit")));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M3 idle after combo 1");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 866, 90, "M3 combo hit 2");
            yield return Capture("02_combo_hit_2.png", "M3 连段 2/3：第二段命中 67，敌人 HP 866",
                Expect(("enemyHp", 866), ("lastDamage", 67), ("combo", 2), ("lastEvent", "Hit")));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M3 idle after combo 2");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 799, 90, "M3 combo hit 3");
            yield return Capture("03_combo_hit_3.png", "M3 连段 3/3：第三段命中 67，敌人 HP 799",
                Expect(("enemyHp", 799), ("lastDamage", 67), ("combo", 3), ("lastEvent", "Hit")));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M3 idle after combo 3");
            yield return WaitTicks(40);
            yield return HoldPrimary(20);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 699, 150, "M3 heavy hit");
            yield return Capture("04_heavy_hit.png", "M3 重击命中：长按左键 0.22s 触发一次重击 100，敌人 HP 699",
                Expect(("enemyHp", 699), ("lastDamage", 100), ("lastEvent", "Hit")));
            _evidence.Metric("comboHits", new[] { 67, 67, 67 });
            _evidence.Metric("heavyHit", 100);
            _evidence.Metric("swingCount", _director.EnemySwingTicks.Count);
        }

        private IEnumerator RunM3FourSteps()
        {
            yield return ResetState();
            int[] expected = { 933, 866, 799, 732 };
            for (int i = 0; i < 4; i++)
            {
                yield return TapPrimary();
                int target = expected[i];
                yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == target, 120,
                    $"M3 4-step combo hit {i + 1}");
                yield return WaitUntil(() => Player != null && !Player.Busy, 90, $"M3 4-step idle {i + 1}");
                yield return WaitTicks(2);
            }
            yield return Capture("05_combo_4_config.png", "M3 连段扩展：仅改 JSON 为 4 段，连段显示 4/4，代码零改动",
                Expect(("enemyHp", 732), ("combo", 4), ("comboCount", 4), ("lastDamage", 67)));
            _evidence.Metric("comboStepCount", _bootstrap.Config.Combo.Steps.Count);
            _evidence.Metric("comboSteps", string.Join(",", _bootstrap.Config.Combo.Steps));
        }

        private IEnumerator RunM4()
        {
            yield return ResetState();
            yield return WaitUntil(() => Player != null && Mathf.RoundToInt(Player.Health) == 933, 400, "M4 unblocked enemy hit");
            yield return Capture("01_enemy_unblocked.png", "M4 未防御：敌人武器命中玩家，HP -67，事件 Hit，overlap=true",
                Expect(("playerHp", 933), ("lastDamage", 67), ("lastEvent", "Hit"), ("overlap", true),
                    ("hitTaken", 1)));

            yield return ResetState();
            _input.Apply(new AutomationKeys { Space = true });
            yield return WaitUntil(() => _director.BlockedCount > 0, 400, "M4 blocked enemy hit");
            yield return Capture("02_block_immune.png", "M4 格挡：武器命中本体但免疫，HP 1000，事件 Blocked，伤害 0",
                Expect(("playerHp", 1000), ("lastDamage", 0), ("lastEvent", "Blocked"), ("overlap", true),
                    ("blocked", 1)));
            _input.Release();
            yield return WaitTicks(3);

            yield return ResetState();
            yield return WaitUntil(() =>
                Enemy != null && Enemy.Busy && Enemy.AbilityIndex == _enemySwingAbilityIndex &&
                Enemy.AbilityFrame >= Mathf.Max(0, _enemySwingActiveFrame - 1), 400, "M4 enemy active window approach");
            yield return TapKeys(new AutomationKeys { RightMouse = true }, 3);
            yield return WaitUntil(() => _director.DodgedCount > 0, 90, "M4 dodged enemy hit");
            yield return Capture("03_dodge_immune.png", "M4 闪避：重叠发生且处于无敌帧，HP 1000，事件 Dodged，伤害 0",
                Expect(("playerHp", 1000), ("lastDamage", 0), ("lastEvent", "Dodged"), ("overlap", true),
                    ("dodged", 1)));

            yield return ResetState();
            _input.Apply(new AutomationKeys { W = true });
            yield return WaitTicks(40);
            yield return WaitUntil(() => _director.EnemySwingTicks.Count >= 1, 200, "M4 swing start for dodge window test");
            _input.Apply(new AutomationKeys { W = true, RightMouse = true });
            yield return WaitTicks(3);
            _input.Apply(new AutomationKeys { W = true });
            yield return WaitUntil(() => Player != null && Mathf.RoundToInt(Player.Health) < 1000, 200,
                "M4 dodge invulnerability expires then hit");
            _input.Release();
            yield return WaitTicks(3);
            yield return Capture("04_dodge_window_miss.png", "M4 无敌结束：闪避提前释放后仍被命中，HP -67（非永久免疫）",
                Expect(("playerHp", 933), ("lastDamage", 67), ("hitTaken", 1)));

            yield return ResetState();
            yield return WaitUntil(() => _director.EnemySwingTicks.Count >= 3, 500, "M4 three enemy swings");
            yield return WaitUntil(() => _director.HitTakenCount >= 3, 200, "M4 three enemy hits landed");
            yield return WaitTicks(5);
            int swings = _director.EnemySwingTicks.Count;
            if (swings < 3)
            {
                _evidence.Fail($"M4 挥剑次数不足: {swings}");
                _fatal = true;
            }
            yield return Capture("05_swing_interval.png", "M4 敌人挥剑间隔：连续 3 次约 120 帧（2 秒），HUD/日志记录 interval",
                Expect(("enemySwings", 3), ("playerHp", 799)));
            _evidence.Metric("swingTicks", string.Join(",", _director.EnemySwingTicks));
            _evidence.Metric("swingIntervals", _director.FormatSwingIntervals());
        }

        private IEnumerator RunM5()
        {
            yield return ResetState();
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 933, 90, "M5 pre-Q normal hit");
            yield return Capture("01_before_q.png", "M5 Q 前：敌人 DEF 50，普通攻击实测 67",
                Expect(("enemyDef", 50), ("lastDamage", 67), ("enemyHp", 933)));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before Q");
            yield return TapKeys(new AutomationKeys { Q = true }, 3);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.EffectiveDefense) == 0, 150, "M5 Q debuff applied");
            int qDamage = _director.LastDamage;
            if (qDamage != 33)
            {
                _evidence.Fail($"M5 Q 伤害预期 33，实测 {qDamage}");
                _fatal = true;
            }
            yield return Capture("02_q_hit_debuff.png", "M5 Q 命中：伤害 33，敌人 DEF 0，破防 Buff 600 帧",
                Expect(("enemyHp", 900), ("enemyDef", 0), ("buffActive", true), ("lastDamage", 33)));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle after Q");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 800, 90, "M5 normal after debuff");
            yield return Capture("03_normal_after_debuff.png", "M5 破防后普攻：伤害 100（67 → 100）",
                Expect(("enemyHp", 800), ("lastDamage", 100), ("enemyDef", 0)));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before heavy");
            yield return WaitTicks(25);
            yield return HoldPrimary(20);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 650, 150, "M5 heavy after debuff");
            yield return Capture("04_heavy_after_debuff.png", "M5 破防后重击：伤害 150（100 → 150）",
                Expect(("enemyHp", 650), ("lastDamage", 150), ("enemyDef", 0)));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before cooldown test");
            yield return TapKeys(new AutomationKeys { Q = true }, 3);
            yield return WaitUntil(() => _director.LastEventType == CombatEventType.Rejected, 30, "M5 Q cooldown rejection");
            yield return Capture("05_q_cooldown.png", "M5 Q 冷却：冷却中再次按键被拒绝，HUD 显示剩余冷却",
                Expect(("lastEvent", "Rejected"), ("enemyHp", 650), ("enemyDef", 0)));

            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before E");
            yield return TapKeys(new AutomationKeys { E = true }, 3);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 570, 120, "M5 E damage");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before R");
            yield return TapKeys(new AutomationKeys { R = true }, 3);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 510, 120, "M5 R damage");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M5 idle before F");
            yield return TapKeys(new AutomationKeys { F = true }, 3);
            yield return WaitUntil(() => Player != null && Player.CooldownBySlot[(int)AbilitySlot.Skill4] > 0, 60, "M5 F cooldown");
            yield return WaitTicks(20);
            yield return Capture("06_erf_skills.png", "M5 E/R/F 占位技能：E 80、R 60、F 无伤害但有冷却，四技能独立冷却",
                Expect(("enemyHp", 510), ("lastDamage", 60)));
            _evidence.Metric("qDamageFirst", 33);
            _evidence.Metric("normalAfterDebuff", 100);
            _evidence.Metric("heavyAfterDebuff", 150);
            _evidence.Metric("eDamage", 80);
            _evidence.Metric("rDamage", 60);
            _evidence.Metric("buffRemainingFrames", Enemy != null ? Enemy.BuffRemainingFrames : 0);
        }

        private IEnumerator RunM6()
        {
            yield return ResetState();
            yield return Capture("01_weapon_1.png", "M6 武器槽 1/3：训练剑 I，长度 1.6m",
                Expect(("weaponSlot", 1), ("weaponId", "sword_01"), ("weaponSlots", 3)));

            yield return TapKeys(new AutomationKeys { Tab = true }, 3);
            yield return WaitUntil(() => _director.WeaponSlotIndex == 1, 30, "M6 switch to weapon 2");
            yield return Capture("02_weapon_2.png", "M6 武器槽 2/3：训练剑 II，长度 2.0m，颜色变化",
                Expect(("weaponSlot", 2), ("weaponId", "sword_02")));

            // Prove attacks still work after switching to the longer slot-2 sword.
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 933, 90,
                "M6 attack after weapon switch");
            int switchedDamage = _director.LastDamage;
            if (switchedDamage != 67)
            {
                _evidence.Fail($"M6 切换武器后攻击伤害预期 67，实测 {switchedDamage}");
                _fatal = true;
            }
            _evidence.Metric("weapon2AttackDamage", switchedDamage);
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M6 idle after switched attack");

            yield return TapKeys(new AutomationKeys { Tab = true }, 3);
            yield return WaitUntil(() => _director.WeaponSlotIndex == 2, 30, "M6 switch to weapon 3");
            yield return Capture("03_weapon_3.png", "M6 武器槽 3/3：训练剑 III，长度 1.2m，颜色变化",
                Expect(("weaponSlot", 3), ("weaponId", "sword_03")));

            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return WaitUntil(() => _director.InventoryVisible, 30, "M6 inventory open");
            yield return Capture("04_inventory.png", "M6 背包面板：50 格背包、3 装备槽、3 武器槽（占位 UI）",
                Expect(("inventoryOpen", true), ("inventorySlots", 50), ("equipmentSlots", 3), ("weaponSlots", 3)));

            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return WaitUntil(() => !_director.InventoryVisible, 30, "M6 inventory close");
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return WaitUntil(() => _director.MapVisible, 30, "M6 map open");
            yield return Capture("05_map.png", "M6 地图占位面板：M 键开关，不影响战斗",
                Expect(("mapOpen", true)));
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return WaitUntil(() => !_director.MapVisible, 30, "M6 map close");

            yield return TapKeys(new AutomationKeys { Digit1 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit2 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit3 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit4 = true }, 3);
            yield return Capture("06_item_keys.png", "M6 物品栏按键：1-4 触发槽位反馈（空槽）",
                Expect(("itemSlot", 4), ("itemHistory", "1,2,3,4"), ("itemBarSlots", 4)));

            yield return TapPrimary();
            yield return WaitUntil(() => Player != null && Player.Busy, 60, "M6 busy for switch rejection");
            int slotBefore = _director.WeaponSlotIndex;
            yield return TapKeys(new AutomationKeys { Tab = true }, 2);
            yield return WaitTicks(4);
            if (_director.WeaponSlotIndex != slotBefore)
            {
                _evidence.Fail("M6 动作中 Tab 不应切换武器");
                _fatal = true;
            }
            yield return Capture("07_switch_rejected.png", "M6 武器切换限制：攻击动作中 Tab 被拒绝，槽位不变",
                Expect(("weaponSlot", 3), ("switchRejections", 1)));
            yield return WaitUntil(() => Player != null && !Player.Busy, 120, "M6 idle after rejection test");
            _evidence.Metric("weaponSwitchRejections", _director.WeaponSwitchRejections);
        }

        private IEnumerator RunM6FourSlots()
        {
            yield return ResetState();
            int[] expectedSlots = { 1, 2, 3, 4 };
            for (int i = 0; i < 4; i++)
            {
                if (i > 0)
                {
                    yield return TapKeys(new AutomationKeys { Tab = true }, 3);
                }
                int expected = expectedSlots[i];
                yield return WaitUntil(() => _director.WeaponSlotIndex == expected - 1, 30, $"M6 drill slot {expected}");
            }
            yield return Capture("07_4_slots_drill.png", "M6 动态槽位演练：仅改 JSON 得到 4 武器槽并切换到第 4 把武器",
                Expect(("weaponSlot", 4), ("weaponSlots", 4), ("weaponId", "sword_04")));

            yield return TapKeys(new AutomationKeys { Tab = true }, 3);
            yield return WaitUntil(() => _director.WeaponSlotIndex == 0, 30, "M6 drill wrap to slot 1");
            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return WaitUntil(() => _director.InventoryVisible, 30, "M6 drill inventory open");
            yield return Capture("08_4_equip_panel.png", "M6 动态槽位演练：B 面板显示 4 武器槽 + 4 装备槽，代码零改动",
                Expect(("inventoryOpen", true), ("weaponSlots", 4), ("equipmentSlots", 4), ("weaponSlot", 1)));
            _evidence.Metric("drillWeapons", string.Join(",", _bootstrap.Config.Weapons.ConvertAll(w => w.Id)));
        }

        private IEnumerator RunM7()
        {
            yield return WaitTicks(30);
            yield return Capture("01_player_dx11.png", "M7 Player 构建运行：Direct3D11，无编辑器依赖",
                Expect(("graphicsDevice", "Direct3D11"), ("playerHp", 1000), ("enemyHp", 1000),
                    ("build", Application.isEditor ? "Editor" : "Player")));

            // Phase A: movement, three-hit combo, heavy, incoming attack, block, dodge.
            yield return TapKeys(AutomationKeys.MoveW, 60);
            yield return WaitTicks(3);
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 933, 90, "M7 combo 1");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M7 idle 1");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 866, 90, "M7 combo 2");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M7 idle 2");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 799, 90, "M7 combo 3");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M7 idle 3");
            yield return WaitTicks(25);
            yield return HoldPrimary(20);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 699, 150, "M7 heavy");

            yield return WaitUntil(() => _director.HitTakenCount >= 1, 400, "M7 enemy unblocked hit");
            int hitTakenAfterFirstSwing = _director.HitTakenCount;
            _input.Apply(new AutomationKeys { Space = true });
            yield return WaitUntil(() => _director.BlockedCount > 0, 400, "M7 blocked hit");
            _input.Release();
            yield return WaitTicks(3);
            int swingsBeforeDodge = _director.EnemySwingTicks.Count;
            yield return WaitUntil(() =>
                _director.EnemySwingTicks.Count > swingsBeforeDodge &&
                Enemy != null && Enemy.Busy && Enemy.AbilityIndex == _enemySwingAbilityIndex &&
                Enemy.AbilityFrame >= Mathf.Max(0, _enemySwingActiveFrame - 1), 400, "M7 dodge approach");
            yield return TapKeys(new AutomationKeys { RightMouse = true }, 3);
            yield return WaitUntil(() => _director.DodgedCount > 0, 90, "M7 dodged hit");

            _evidence.Metric("phaseAEnemyHp", Enemy != null ? Mathf.RoundToInt(Enemy.Health) : -1);
            _evidence.Metric("phaseAPlayerHp", Player != null ? Mathf.RoundToInt(Player.Health) : -1);
            _evidence.Metric("phaseAHitTaken", hitTakenAfterFirstSwing);
            _evidence.Metric("phaseABlocked", _director.BlockedCount);
            _evidence.Metric("phaseADodged", _director.DodgedCount);
            _evidence.Metric("phaseASwings", _director.EnemySwingTicks.Count);

            // Phase B: deterministic Q -> normal -> heavy damage demonstration.
            // Resetting is documented in summary notes: it keeps the expected damage
            // numbers stable while every mechanic above stays demonstrated.
            yield return ResetState();
            yield return TapKeys(new AutomationKeys { Tab = true }, 3);
            yield return WaitUntil(() => _director.WeaponSlotIndex == 1, 30, "M7 weapon switch");
            yield return TapKeys(new AutomationKeys { Q = true }, 3);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.EffectiveDefense) == 0, 150, "M7 Q debuff");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M7 idle after Q");
            yield return TapPrimary();
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 867, 90, "M7 normal after debuff");
            yield return WaitUntil(() => Player != null && !Player.Busy, 90, "M7 idle before heavy 2");
            yield return TapKeys(new AutomationKeys { LeftMouse = true }, 20);
            yield return WaitUntil(() => Enemy != null && Mathf.RoundToInt(Enemy.Health) == 717, 150, "M7 heavy after debuff");
            yield return Capture("02_full_combat.png", "M7 完整战斗：三段+重击、敌人命中、格挡、闪避、Q 破防、伤害变化、武器切换",
                Expect(("enemyHp", 717), ("enemyDef", 0), ("weaponSlot", 2), ("lastDamage", 150),
                    ("buffActive", true), ("playerHp", 1000)));

            yield return TapKeys(new AutomationKeys { Digit1 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit2 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit3 = true }, 3);
            yield return TapKeys(new AutomationKeys { Digit4 = true }, 3);
            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return WaitUntil(() => _director.InventoryVisible, 30, "M7 inventory open");
            yield return Capture("03_full_inventory.png", "M7 完整背包/装备/武器槽面板",
                Expect(("inventoryOpen", true), ("inventorySlots", 50), ("equipmentSlots", 3), ("weaponSlots", 3),
                    ("enemyHp", 717)));
            yield return TapKeys(new AutomationKeys { Inventory = true }, 3);
            yield return WaitUntil(() => !_director.InventoryVisible, 30, "M7 inventory close");
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return WaitUntil(() => _director.MapVisible, 30, "M7 map open");
            yield return TapKeys(new AutomationKeys { Map = true }, 3);
            yield return WaitUntil(() => !_director.MapVisible, 30, "M7 map close");
            yield return WaitTicks(5);
            yield return Capture("04_final_scene.png", "M7 最终原型场景：平面、圆柱角色、双剑、HUD、固定相机",
                Expect(("enemyHp", 717), ("enemyDef", 0)));
            _evidence.Metric("finalEnemyHp", Enemy != null ? Mathf.RoundToInt(Enemy.Health) : -1);
            _evidence.Metric("finalPlayerHp", Player != null ? Mathf.RoundToInt(Player.Health) : -1);
            _evidence.Metric("finalWeaponSlot", _director.WeaponSlotIndex + 1);
            _evidence.Metric("notes", "Phase A and phase B are reset-separated so expected damage values stay deterministic; all mechanics are exercised in one continuous player session.");
        }

        private IEnumerator RunStability()
        {
            float duration = 300f;
            string durationArg = CommandLine.GetValue("-ckDuration");
            if (float.TryParse(durationArg, out float parsed) && parsed > 0f)
            {
                duration = parsed;
            }
            _evidence.Metric("requestedDurationSeconds", duration);
            float startTime = Time.realtimeSinceStartup;
            long memoryStart = GC.GetTotalMemory(false);
            long memoryPeak = memoryStart;
            int frameCount = 0;
            int tickStart = _director.Tick;
            float maxDelta = 0f;
            float totalDelta = 0f;
            float nextMemorySample = 0f;
            uint nextActionTick = 0;
            int action = 0;
            float endTime = startTime + duration;

            while (Time.realtimeSinceStartup < endTime)
            {
                float dt = Time.unscaledDeltaTime;
                maxDelta = Mathf.Max(maxDelta, dt);
                totalDelta += dt;
                frameCount++;

                if (_director.Tick >= nextActionTick)
                {
                    switch (action % 8)
                    {
                        case 0:
                            _input.Apply(new AutomationKeys { W = true });
                            break;
                        case 1:
                            _input.Apply(new AutomationKeys { D = true });
                            break;
                        case 2:
                            _input.Apply(new AutomationKeys { S = true });
                            break;
                        case 3:
                            _input.Apply(new AutomationKeys { A = true });
                            break;
                        case 4:
                            _input.Apply(new AutomationKeys { LeftMouse = true });
                            break;
                        case 5:
                            _input.Release();
                            break;
                        case 6:
                            _input.Apply(new AutomationKeys { Space = true });
                            break;
                        default:
                            _input.Release();
                            break;
                    }
                    action++;
                    nextActionTick = (uint)_director.Tick + 25u;
                }

                if (Time.realtimeSinceStartup - startTime > nextMemorySample)
                {
                    nextMemorySample += 30f;
                    memoryPeak = Math.Max(memoryPeak, GC.GetTotalMemory(false));
                }
                yield return null;
            }

            _input.Release();
            long memoryEnd = GC.GetTotalMemory(false);
            float elapsed = Time.realtimeSinceStartup - startTime;
            float avgFps = frameCount / Mathf.Max(0.001f, elapsed);
            _evidence.Metric("durationSeconds", Mathf.Round(elapsed * 10f) / 10f);
            _evidence.Metric("frames", frameCount);
            _evidence.Metric("avgFps", Mathf.Round(avgFps * 10f) / 10f);
            _evidence.Metric("minFps", Mathf.Round((1f / Mathf.Max(0.0001f, maxDelta)) * 10f) / 10f);
            _evidence.Metric("maxFrameMs", Mathf.Round(maxDelta * 1000f * 100f) / 100f);
            _evidence.Metric("memoryStartMB", Mathf.Round(memoryStart / 1048576f * 10f) / 10f);
            _evidence.Metric("memoryEndMB", Mathf.Round(memoryEnd / 1048576f * 10f) / 10f);
            _evidence.Metric("memoryPeakMB", Mathf.Round(memoryPeak / 1048576f * 10f) / 10f);
            _evidence.Metric("tickStart", tickStart);
            _evidence.Metric("tickEnd", _director.Tick);
            _evidence.Metric("consoleErrorsDuringRun", RuntimeLogRecorder.ErrorCount);
            yield return WaitTicks(20);
            yield return Capture("05_stability.png", "M7 稳定性运行结束画面（5 分钟循环移动/攻击）",
                new Dictionary<string, object>());
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private IEnumerator ResetState()
        {
            if (_input != null)
            {
                _input.Release();
            }
            yield return WaitTicks(2);
            _director.ResetCombat();
            yield return WaitTicks(3);
        }

        private IEnumerator WaitTicks(int ticks)
        {
            if (_fatal)
            {
                yield break;
            }
            for (int i = 0; i < ticks; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator TapKeys(AutomationKeys keys, int holdTicks)
        {
            if (_fatal)
            {
                yield break;
            }
            _input.Apply(keys);
            for (int i = 0; i < Mathf.Max(1, holdTicks); i++)
            {
                yield return new WaitForFixedUpdate();
            }
            _input.Release();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        /// <summary>Tap one key while keeping a base state (e.g. block) held.</summary>
        private IEnumerator TapWhileHolding(AutomationKeys held, AutomationKeys tap, int holdTicks)
        {
            if (_fatal)
            {
                yield break;
            }
            _input.Apply(MergeKeys(held, tap));
            for (int i = 0; i < Mathf.Max(1, holdTicks); i++)
            {
                yield return new WaitForFixedUpdate();
            }
            _input.Apply(held);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static AutomationKeys MergeKeys(AutomationKeys a, AutomationKeys b)
        {
            a.W |= b.W;
            a.A |= b.A;
            a.S |= b.S;
            a.D |= b.D;
            a.Space |= b.Space;
            a.Q |= b.Q;
            a.E |= b.E;
            a.R |= b.R;
            a.F |= b.F;
            a.Tab |= b.Tab;
            a.Map |= b.Map;
            a.Inventory |= b.Inventory;
            a.Digit1 |= b.Digit1;
            a.Digit2 |= b.Digit2;
            a.Digit3 |= b.Digit3;
            a.Digit4 |= b.Digit4;
            a.LeftShift |= b.LeftShift;
            a.LeftMouse |= b.LeftMouse;
            a.RightMouse |= b.RightMouse;
            return a;
        }

        private IEnumerator TapPrimary()
        {
            yield return TapKeys(new AutomationKeys { LeftMouse = true }, 4);
        }

        private IEnumerator HoldPrimary(int holdTicks)
        {
            if (_fatal)
            {
                yield break;
            }
            _input.Apply(new AutomationKeys { LeftMouse = true });
            for (int i = 0; i < holdTicks; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            _input.Release();
            yield return new WaitForFixedUpdate();
        }

        private IEnumerator WaitUntil(Func<bool> predicate, int maxTicks, string description)
        {
            if (_fatal)
            {
                yield break;
            }
            for (int i = 0; i < maxTicks; i++)
            {
                if (predicate())
                {
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
            _evidence.Fail($"{description}: 超时 {maxTicks} ticks (tick={_director.Tick})");
            _fatal = true;
        }

        private IEnumerator Capture(string fileName, string label, Dictionary<string, object> expected)
        {
            if (_fatal)
            {
                yield break;
            }
            yield return new WaitForFixedUpdate();
            string path = Path.Combine(_evidence.ScreenshotDirectory, fileName);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 1);
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (File.Exists(path))
                {
                    var info = new FileInfo(path);
                    if (info.Length > 1024)
                    {
                        break;
                    }
                }
                yield return null;
            }
            yield return null;
            if (!File.Exists(path))
            {
                _evidence.Fail($"截图未生成: {fileName}");
                _fatal = true;
                yield break;
            }
            Dictionary<string, object> observed = _evidence.Observe(_director, _bootstrap.Hud);
            ScreenshotRecord record = _evidence.Record(fileName, label, expected, observed);
            observed.TryGetValue("tick", out object observedTick);
            observed.TryGetValue("playerHp", out object observedPlayerHp);
            observed.TryGetValue("enemyHp", out object observedEnemyHp);
            Debug.Log($"[CK] screenshot {fileName} matched={record.Matched} tick={observedTick} hp={observedPlayerHp}/{observedEnemyHp}");
            _evidence.Metric("lastScreenshot", fileName);
        }

        private static Dictionary<string, object> Expect(params (string Key, object Value)[] pairs)
        {
            var dictionary = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i++)
            {
                dictionary[pairs[i].Key] = pairs[i].Value;
            }
            return dictionary;
        }

        private static string NativeCombatBridgeAbi()
        {
            int abi = CK.Native.NativeCombatBridge.GetAbiVersion();
            return abi < 0 ? "unavailable" : abi.ToString();
        }
    }
}
