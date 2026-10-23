using System;
using System.Collections;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Runtime.Actors;
using CK.Runtime.Automation;
using CK.Runtime.Bootstrap;
using CK.Runtime.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CK.Tests.PlayMode
{
    /// <summary>
    /// 针对生成的 CombatPrototype 场景的真实 Play Mode 测试。
    /// 输入通过 Input System 注入，与 Gate 驱动完全一致。
    /// </summary>
    public sealed class PrototypePlayModeTests
    {
        private AutomationInput _input;
        private CombatPrototypeBootstrap _bootstrap;
        private CombatDirector _director;
        private int _enemySwingIndex = -1;
        private int _enemyActiveStart = 30;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("CombatPrototype", LoadSceneMode.Single);
            yield return null;
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            _bootstrap = CombatPrototypeBootstrap.Instance;
            Assert.IsNotNull(_bootstrap, "CombatPrototypeBootstrap did not initialize");
            Assert.IsFalse(_bootstrap.Failed, _bootstrap.FailureText);
            _director = _bootstrap.Director;
            Assert.IsNotNull(_director);
            Assert.IsTrue(_director.Initialized);
            _input = new AutomationInput();
            AbilityConfig enemyAbility = _bootstrap.Config.FindAbilityBySlot(AbilitySlot.EnemyAttack);
            _enemySwingIndex = _bootstrap.Config.FindAbilityIndex(enemyAbility.Id);
            _enemyActiveStart = enemyAbility.ActiveStartFrame;
            yield return WaitTicks(5);
        }

        [TearDown]
        public void TearDown()
        {
            if (_input != null)
            {
                _input.Release();
                _input.Disable();
            }
            _input = null;
        }

        [UnityTest]
        public IEnumerator SceneBootstrapsActorsHudAndNativeCore()
        {
            Assert.AreEqual(2, _director.Bridge.ActorCount);
            Assert.AreEqual(1000f, _director.PlayerSnapshot.Health);
            Assert.AreEqual(1000f, _director.EnemySnapshot.Health);
            Assert.AreEqual(50f, _director.PlayerSnapshot.EffectiveDefense);
            Assert.AreEqual(50f, _director.EnemySnapshot.EffectiveDefense);
            Assert.AreEqual(100f, _director.PlayerSnapshot.AttackPower);
            Assert.IsNotNull(_director.Player.GetComponent<CharacterController>());
            Assert.IsNotNull(_director.Enemy.GetComponent<CapsuleCollider>());
            Assert.IsNotNull(_bootstrap.Hud);
            Assert.AreEqual(3, _bootstrap.Config.Loadout.WeaponSlots);
            Assert.AreEqual(50, _bootstrap.Config.Loadout.InventorySlots);
            Assert.AreEqual(3, _bootstrap.Config.Loadout.EquipmentSlots);
            Assert.AreEqual(4, _bootstrap.Config.Loadout.ItemBarSlots);
            yield return null;
            if (!Application.isBatchMode)
            {
                Assert.Greater(_bootstrap.Hud.DrawCount, 0, "HUD did not render");
                Assert.AreEqual("Direct3D11", SystemInfo.graphicsDeviceType.ToString());
            }
            else
            {
                Debug.Log("[CK] batchmode graphics device: " + SystemInfo.graphicsDeviceType);
            }
        }

        [UnityTest]
        public IEnumerator WasdMovementAndBodyCollision()
        {
            Vector3 start = _director.Player.transform.position;
            _input.Apply(new AutomationKeys { D = true });
            yield return WaitTicks(25);
            _input.Release();
            yield return WaitTicks(3);
            Assert.Greater(_director.Player.transform.position.x - start.x, 1.5f, "D axis movement too small");
            Assert.Less(Mathf.Abs(_director.Player.transform.position.z - start.z), 0.1f);

            _director.ResetCombat();
            yield return WaitTicks(3);
            start = _director.Player.transform.position;
            _input.Apply(new AutomationKeys { A = true });
            yield return WaitTicks(25);
            _input.Release();
            yield return WaitTicks(3);
            Assert.Greater(start.x - _director.Player.transform.position.x, 1.5f, "A axis movement too small");
            Assert.Less(Mathf.Abs(_director.Player.transform.position.z - start.z), 0.1f);

            _director.ResetCombat();
            yield return WaitTicks(3);
            start = _director.Player.transform.position;
            _input.Apply(new AutomationKeys { S = true });
            yield return WaitTicks(25);
            _input.Release();
            yield return WaitTicks(3);
            Assert.Greater(start.z - _director.Player.transform.position.z, 1.5f, "S axis movement too small");
            Assert.Less(Mathf.Abs(_director.Player.transform.position.x - start.x), 0.1f);

            _director.ResetCombat();
            yield return WaitTicks(3);
            _input.Apply(new AutomationKeys { W = true });
            yield return WaitTicks(90);
            _input.Release();
            yield return WaitTicks(3);
            Assert.LessOrEqual(_director.DistanceToEnemy, 1.25f, "player passed through the enemy body");
            Assert.GreaterOrEqual(_director.DistanceToEnemy, 0.9f, "player stopped too far from the enemy");
            Assert.IsTrue(_director.PlayerTouchingEnemy);
        }

        [UnityTest]
        public IEnumerator PrimaryComboDeals67EachHit()
        {
            yield return TapPrimary();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.Health) == 933, 120);
            yield return WaitUntil(() => !_director.PlayerSnapshot.Busy, 120);
            yield return TapPrimary();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.Health) == 866, 120);
            yield return WaitUntil(() => !_director.PlayerSnapshot.Busy, 120);
            yield return TapPrimary();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.Health) == 799, 120);
            Assert.AreEqual(3, _director.PlayerSnapshot.ComboStep);
        }

        [UnityTest]
        public IEnumerator HeavyDeals100()
        {
            _input.Apply(new AutomationKeys { LeftMouse = true });
            yield return WaitTicks(20);
            _input.Release();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.Health) == 900, 180);
            Assert.AreEqual(100, _director.LastDamage);
        }

        [UnityTest]
        public IEnumerator EnemySwingDamagesAndBlockPreventsIt()
        {
            yield return WaitUntil(() => Mathf.RoundToInt(_director.PlayerSnapshot.Health) == 933, 400);
            Assert.AreEqual(67, _director.LastDamage);
            int taken = _director.HitTakenCount;
            Assert.GreaterOrEqual(taken, 1);

            _director.ResetCombat();
            yield return WaitTicks(3);
            _input.Apply(new AutomationKeys { Space = true });
            yield return WaitUntil(() => _director.BlockedCount > 0, 400);
            CombatEventType lastEvent = _director.LastEventType;
            _input.Release();
            yield return WaitTicks(3);
            Assert.AreEqual(1000f, _director.PlayerSnapshot.Health);
            Assert.AreEqual(0, _director.LastDamage);
            Assert.AreEqual(CombatEventType.Blocked, lastEvent);
        }

        [UnityTest]
        public IEnumerator DodgeWhileOverlappingProducesDodged()
        {
            yield return WaitUntil(() =>
                _director.EnemySnapshot.Busy &&
                _director.EnemySnapshot.AbilityIndex == _enemySwingIndex &&
                _director.EnemySnapshot.AbilityFrame >= Mathf.Max(0, _enemyActiveStart - 1), 400);
            _input.Apply(new AutomationKeys { RightMouse = true });
            yield return WaitTicks(3);
            _input.Release();
            yield return WaitUntil(() => _director.DodgedCount > 0, 120);
            Assert.AreEqual(1000f, _director.PlayerSnapshot.Health);
            Assert.IsTrue(_director.LastOverlap, "Dodged must be triggered by an actual weapon-body overlap");
        }

        [UnityTest]
        public IEnumerator QAppliesDefenseDebuffAndChangesDamage()
        {
            _input.Apply(new AutomationKeys { Q = true });
            yield return WaitTicks(3);
            _input.Release();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.EffectiveDefense) == 0, 180);
            Assert.AreEqual(967f, _director.EnemySnapshot.Health);
            Assert.IsTrue(_director.EnemySnapshot.BuffActive);
            yield return WaitUntil(() => !_director.PlayerSnapshot.Busy, 120);
            yield return TapPrimary();
            yield return WaitUntil(() => Mathf.RoundToInt(_director.EnemySnapshot.Health) == 867, 120);
            Assert.AreEqual(100, _director.LastDamage);
        }

        [UnityTest]
        public IEnumerator WeaponSlotsAndPanelsRespondToKeys()
        {
            Assert.AreEqual(0, _director.WeaponSlotIndex);
            yield return TapKeys(new AutomationKeys { Tab = true });
            Assert.AreEqual(1, _director.WeaponSlotIndex);
            Assert.AreEqual("sword_02", _director.CurrentWeapon.Id);
            yield return TapKeys(new AutomationKeys { Tab = true });
            Assert.AreEqual(2, _director.WeaponSlotIndex);
            yield return TapKeys(new AutomationKeys { Inventory = true });
            Assert.IsTrue(_director.InventoryVisible);
            yield return TapKeys(new AutomationKeys { Inventory = true });
            Assert.IsFalse(_director.InventoryVisible);
            yield return TapKeys(new AutomationKeys { Map = true });
            Assert.IsTrue(_director.MapVisible);
            yield return TapKeys(new AutomationKeys { Map = true });
            Assert.IsFalse(_director.MapVisible);
            yield return TapKeys(new AutomationKeys { Digit4 = true });
            Assert.AreEqual(4, _director.LastItemSlot);
        }

        private IEnumerator TapPrimary()
        {
            _input.Apply(new AutomationKeys { LeftMouse = true });
            yield return WaitTicks(4);
            _input.Release();
            yield return WaitTicks(2);
        }

        private IEnumerator TapKeys(AutomationKeys keys)
        {
            _input.Apply(keys);
            yield return WaitTicks(3);
            _input.Release();
            yield return WaitTicks(2);
        }

        private static IEnumerator WaitTicks(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private IEnumerator WaitUntil(Func<bool> predicate, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (predicate())
                {
                    yield break;
                }
                yield return new WaitForFixedUpdate();
            }
            Assert.Fail($"condition not met within {maxTicks} ticks (tick={_director.Tick})");
        }
    }
}
