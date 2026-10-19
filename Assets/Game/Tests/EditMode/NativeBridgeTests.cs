using System.Collections.Generic;
using System.IO;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Native;
using NUnit.Framework;
using UnityEngine;

namespace CK.Tests.EditMode
{
    /// <summary>
    /// Exercises the marshaling and the authoritative rules through the same
    /// NativeCombatBridge the Unity host uses. No Play Mode required.
    /// </summary>
    public sealed class NativeBridgeTests
    {
        private CombatConfig _config;
        private NativeCombatBridge _bridge;
        private uint _sequence;
        private readonly List<CombatEvent> _events = new List<CombatEvent>();

        [SetUp]
        public void SetUp()
        {
            string path = Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");
            ConfigLoadResult result = CombatConfigLoader.LoadFromFile(path);
            Assert.IsTrue(result.Success, result.ErrorSummary);
            _config = result.Config;
            _bridge = new NativeCombatBridge();
            Assert.IsTrue(_bridge.Initialize(_config, out string error), error);
            _sequence = 0;
            _events.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _bridge?.Dispose();
            _bridge = null;
        }

        [Test]
        public void ProbesAbiVersion()
        {
            Assert.AreEqual(1, NativeCombatBridge.GetAbiVersion());
            Assert.IsTrue(NativeCombatBridge.Probe(out string message), message);
            StringAssert.Contains("ABI 1", message);
        }

        [Test]
        public void InitializesBothActorsFromConfig()
        {
            Assert.AreEqual(2, _bridge.ActorCount);
            ActorSnapshot player = _bridge.GetSnapshot(0);
            ActorSnapshot enemy = _bridge.GetSnapshot(1);
            Assert.AreEqual(1000f, player.Health);
            Assert.AreEqual(50f, player.EffectiveDefense);
            Assert.AreEqual(100f, player.AttackPower);
            Assert.AreEqual(1000f, enemy.Health);
            Assert.AreEqual(0, _bridge.TickIndex);
            Assert.AreNotEqual(0u, _bridge.NativeConfigHash);
        }

        [Test]
        public void PrimaryDeals67()
        {
            Submit(CombatCommandKind.Primary);
            Run(40);
            Assert.AreEqual(933f, _bridge.GetSnapshot(1).Health);
            CombatEvent? hit = Find(CombatEventType.Hit);
            Assert.IsNotNull(hit);
            Assert.AreEqual(67, Mathf.RoundToInt(hit.Value.Amount));
            Assert.IsTrue(hit.Value.Overlap);
        }

        [Test]
        public void HeavyDeals100()
        {
            Submit(CombatCommandKind.Heavy);
            Run(60);
            Assert.AreEqual(900f, _bridge.GetSnapshot(1).Health);
            CombatEvent? hit = Find(CombatEventType.Hit);
            Assert.IsNotNull(hit);
            Assert.AreEqual(100, Mathf.RoundToInt(hit.Value.Amount));
        }

        [Test]
        public void ComboChainsThreeHits()
        {
            Submit(CombatCommandKind.Primary);
            Run(40);
            Submit(CombatCommandKind.Primary);
            Run(40);
            Submit(CombatCommandKind.Primary);
            Run(40);
            Assert.AreEqual(799f, _bridge.GetSnapshot(1).Health);
            Assert.AreEqual(3, _bridge.GetSnapshot(0).ComboStep);
        }

        [Test]
        public void QAppliesDefenseDebuff()
        {
            Submit(CombatCommandKind.Skill1);
            Run(60);
            ActorSnapshot enemy = _bridge.GetSnapshot(1);
            Assert.AreEqual(967f, enemy.Health);
            Assert.AreEqual(0f, enemy.EffectiveDefense);
            Assert.IsTrue(enemy.BuffActive);
            Assert.GreaterOrEqual(enemy.BuffRemainingFrames, 540);
            CombatEvent? applied = Find(CombatEventType.BuffApplied);
            Assert.IsNotNull(applied);
            Assert.AreEqual(0, applied.Value.DefenseAfter);
        }

        [Test]
        public void DamageChangesAfterDebuff()
        {
            Submit(CombatCommandKind.Skill1);
            Run(60);
            Submit(CombatCommandKind.Primary);
            Run(40);
            Assert.AreEqual(867f, _bridge.GetSnapshot(1).Health);
            Submit(CombatCommandKind.Heavy);
            Run(60);
            Assert.AreEqual(717f, _bridge.GetSnapshot(1).Health);
        }

        [Test]
        public void BlockPreventsDamage()
        {
            Submit(CombatCommandKind.BlockPress);
            Submit(CombatCommandKind.EnemyAttack, 1);
            Run(70);
            Assert.AreEqual(1000f, _bridge.GetSnapshot(0).Health);
            CombatEvent? blocked = Find(CombatEventType.Blocked);
            Assert.IsNotNull(blocked);
            Assert.IsTrue(blocked.Value.Overlap);
            Assert.AreEqual(0, Mathf.RoundToInt(blocked.Value.Amount));
        }

        [Test]
        public void DodgePreventsDamageDuringInvulnerability()
        {
            Submit(CombatCommandKind.EnemyAttack, 1);
            Run(28);
            Submit(CombatCommandKind.Dodge);
            Run(20);
            Assert.AreEqual(1000f, _bridge.GetSnapshot(0).Health);
            CombatEvent? dodged = Find(CombatEventType.Dodged);
            Assert.IsNotNull(dodged);
            Assert.IsTrue(dodged.Value.Overlap);
        }

        [Test]
        public void DodgeInvulnerabilityExpires()
        {
            Submit(CombatCommandKind.Dodge);
            Submit(CombatCommandKind.EnemyAttack, 1);
            Run(35);
            Assert.AreEqual(933f, _bridge.GetSnapshot(0).Health);
        }

        [Test]
        public void SkillCooldownBlocksRepeat()
        {
            Submit(CombatCommandKind.Skill1);
            Run(45);
            Submit(CombatCommandKind.Skill1);
            Run(10);
            CombatEvent? rejected = Find(CombatEventType.Rejected);
            Assert.IsNotNull(rejected);
            Assert.AreEqual(CombatRejectReason.Cooldown, rejected.Value.Reason);
            Assert.Greater(_bridge.GetSnapshot(0).CooldownBySlot[(int)AbilitySlot.Skill1], 400);
        }

        [Test]
        public void ResetRestoresInitialState()
        {
            Submit(CombatCommandKind.Heavy);
            Run(60);
            Assert.AreEqual(900f, _bridge.GetSnapshot(1).Health);
            _bridge.Reset();
            Assert.AreEqual(1000f, _bridge.GetSnapshot(1).Health);
            Assert.AreEqual(0u, _bridge.TickIndex);
        }

        private void Submit(CombatCommandKind kind, int actorId = 0)
        {
            _sequence++;
            _bridge.SubmitCommand(actorId, kind, _bridge.TickIndex, _sequence);
        }

        private void Run(int ticks, bool playerWeaponHits = true, bool enemyWeaponHits = true)
        {
            for (int i = 0; i < ticks; i++)
            {
                _bridge.Tick(BuildFrames(playerWeaponHits, enemyWeaponHits));
                _bridge.DrainEvents(_events);
            }
        }

        private List<ActorFrameVolumes> BuildFrames(bool playerWeaponHits, bool enemyWeaponHits)
        {
            HitVolumeConfig playerBody = _config.GetActorHitVolume(Faction.Player);
            HitVolumeConfig enemyBody = _config.GetActorHitVolume(Faction.Enemy);
            WeaponConfig weapon = _config.Weapons[0];
            float playerZ = _config.World.PlayerSpawn.Z;
            float enemyZ = _config.World.EnemySpawn.Z;
            float halfSegment = playerBody.Height * 0.5f - playerBody.Radius;

            float playerHandX = weapon.HandOffset.X;
            float enemyHandX = -weapon.HandOffset.X;
            float playerStartZ = playerZ + weapon.HandOffset.Z;
            float enemyStartZ = enemyZ - weapon.HandOffset.Z;

            var frames = new List<ActorFrameVolumes>
            {
                new ActorFrameVolumes
                {
                    ActorId = 0,
                    Body = new CapsuleVolume
                    {
                        Ax = 0f, Ay = playerBody.CenterY - halfSegment, Az = playerZ,
                        Bx = 0f, By = playerBody.CenterY + halfSegment, Bz = playerZ,
                        Radius = playerBody.Radius
                    },
                    Weapon = new CapsuleVolume
                    {
                        Ax = playerWeaponHits ? playerHandX : playerHandX + 6f,
                        Ay = weapon.HandOffset.Y, Az = playerStartZ,
                        Bx = playerWeaponHits ? playerHandX : playerHandX + 6f,
                        By = weapon.HandOffset.Y, Bz = playerStartZ + weapon.Length,
                        Radius = weapon.Radius
                    }
                },
                new ActorFrameVolumes
                {
                    ActorId = 1,
                    Body = new CapsuleVolume
                    {
                        Ax = 0f, Ay = enemyBody.CenterY - halfSegment, Az = enemyZ,
                        Bx = 0f, By = enemyBody.CenterY + halfSegment, Bz = enemyZ,
                        Radius = enemyBody.Radius
                    },
                    Weapon = new CapsuleVolume
                    {
                        Ax = enemyWeaponHits ? enemyHandX : enemyHandX - 6f,
                        Ay = weapon.HandOffset.Y, Az = enemyStartZ,
                        Bx = enemyWeaponHits ? enemyHandX : enemyHandX - 6f,
                        By = weapon.HandOffset.Y, Bz = enemyStartZ - weapon.Length,
                        Radius = weapon.Radius
                    }
                }
            };
            return frames;
        }

        private CombatEvent? Find(CombatEventType type)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                if (_events[i].Type == type)
                {
                    return _events[i];
                }
            }
            return null;
        }
    }
}

