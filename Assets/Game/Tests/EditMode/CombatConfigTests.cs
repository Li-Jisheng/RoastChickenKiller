using System;
using System.IO;
using CK.Domain.Config;
using NUnit.Framework;
using UnityEngine;

namespace CK.Tests.EditMode
{
    public sealed class CombatConfigTests
    {
        private static string ConfigPath =>
            Path.Combine(Directory.GetParent(Application.dataPath)?.FullName ?? ".", "Config", "CombatPrototype.json");

        private static string RawJson()
        {
            Assert.IsTrue(File.Exists(ConfigPath), "missing " + ConfigPath);
            return File.ReadAllText(ConfigPath);
        }

        private static ConfigLoadResult LoadMutated(string anchor, string replacement)
        {
            string json = RawJson();
            int index = json.IndexOf(anchor, StringComparison.Ordinal);
            Assert.GreaterOrEqual(index, 0, "mutation anchor not found: " + anchor);
            string mutated = json.Substring(0, index) + replacement + json.Substring(index + anchor.Length);
            return CombatConfigLoader.LoadFromText(mutated, ConfigPath);
        }

        [Test]
        public void ValidConfigLoadsWithExpectedValues()
        {
            ConfigLoadResult result = CombatConfigLoader.LoadFromFile(ConfigPath);
            Assert.IsTrue(result.Success, result.ErrorSummary);
            CombatConfig config = result.Config;
            Assert.AreEqual(60, config.TickRate);
            Assert.AreEqual(100f, config.Combat.DefenseConstant);
            Assert.AreEqual(0f, config.Combat.MinDefense);
            Assert.AreEqual("AwayFromZero", config.Combat.Rounding);
            Assert.AreEqual(21, config.Combo.WindowFrames);
            Assert.AreEqual(10, config.Combat.InputBufferFrames);
            Assert.AreEqual(18, config.Combat.DodgeInvulnFrames);
            Assert.AreEqual(30, config.Combat.DodgeCooldownFrames);
            Assert.AreEqual(4f, config.Combat.DodgeDistance);
            Assert.AreEqual(0.3f, config.Combat.BlockMoveMultiplier);
            Assert.AreEqual(1000f, config.Actors.Player.MaxHealth);
            Assert.AreEqual(100f, config.Actors.Player.AttackPower);
            Assert.AreEqual(50f, config.Actors.Player.Defense);
            Assert.AreEqual(50f, config.Actors.Enemy.Defense);
            Assert.AreEqual(3, config.Loadout.WeaponSlots);
            Assert.AreEqual(3, config.Loadout.EquipmentSlots);
            Assert.AreEqual(50, config.Loadout.InventorySlots);
            Assert.AreEqual(4, config.Loadout.ItemBarSlots);
            Assert.AreEqual(120, config.EnemyAttack.IntervalFrames);
            Assert.AreEqual(3, config.Weapons.Count);
            Assert.AreEqual(3, config.Combo.Steps.Count);
            Assert.IsTrue(config.RawHash != 0);
        }

        [Test]
        public void AbilityMilestonesMatchTaskBook()
        {
            ConfigLoadResult result = CombatConfigLoader.LoadFromFile(ConfigPath);
            Assert.IsTrue(result.Success, result.ErrorSummary);
            CombatConfig config = result.Config;
            Assert.AreEqual(1.0f, config.FindAbility("normal_1").Multiplier);
            Assert.AreEqual(1.5f, config.FindAbility("heavy").Multiplier);
            Assert.AreEqual(0.5f, config.FindAbility("q_break").Multiplier);
            Assert.AreEqual(0.8f, config.FindAbility("e_placeholder").Multiplier);
            Assert.AreEqual(0.6f, config.FindAbility("r_placeholder").Multiplier);
            Assert.AreEqual(0.0f, config.FindAbility("f_placeholder").Multiplier);
            Assert.AreEqual(480, config.FindAbility("q_break").CooldownFrames);
            Assert.AreEqual(360, config.FindAbility("e_placeholder").CooldownFrames);
            Assert.AreEqual(600, config.FindAbility("r_placeholder").CooldownFrames);
            Assert.AreEqual(720, config.FindAbility("f_placeholder").CooldownFrames);
            Assert.AreEqual(10, config.FindAbility("normal_1").ActiveStartFrame);
            Assert.AreEqual(15, config.FindAbility("normal_1").ActiveEndFrame);
            Assert.AreEqual(59, config.FindAbility("enemy_swing").EndFrame);
            Assert.AreEqual(-50f, config.Buffs[0].Add);
            Assert.AreEqual(600, config.Buffs[0].DurationFrames);
        }

        [Test]
        public void RejectsMissingField()
        {
            ConfigLoadResult result = CombatConfigLoader.LoadFromText("{}", "empty");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("combat"), result.ErrorSummary);
        }

        [Test]
        public void RejectsNegativeHealth()
        {
            ConfigLoadResult result = LoadMutated("\"maxHealth\": 1000", "\"maxHealth\": -5");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("maxHealth"), result.ErrorSummary);
        }

        [Test]
        public void RejectsDuplicateWeaponId()
        {
            ConfigLoadResult result = LoadMutated("\"id\": \"sword_02\"", "\"id\": \"sword_01\"");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("duplicate"), result.ErrorSummary);
        }

        [Test]
        public void RejectsComboReferenceToUnknownAbility()
        {
            ConfigLoadResult result = LoadMutated("\"steps\": [\"normal_1\"", "\"steps\": [\"does_not_exist\"");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("combo.steps"), result.ErrorSummary);
        }

        [Test]
        public void RejectsOverlappingPhases()
        {
            ConfigLoadResult result = LoadMutated("\"startFrame\": 10", "\"startFrame\": 5");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("overlap"), result.ErrorSummary);
        }

        [Test]
        public void RejectsWrongTickRate()
        {
            ConfigLoadResult result = LoadMutated("\"tickRate\": 60", "\"tickRate\": 30");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("tickRate"), result.ErrorSummary);
        }

        [Test]
        public void RejectsZeroSlotCounts()
        {
            ConfigLoadResult result = LoadMutated("\"inventorySlots\": 50", "\"inventorySlots\": 0");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("loadout"), result.ErrorSummary);
        }

        [Test]
        public void RejectsEnemyIntervalMismatch()
        {
            ConfigLoadResult result = LoadMutated("\"intervalFrames\": 120", "\"intervalFrames\": 100");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("intervalFrames"), result.ErrorSummary);
        }

        [Test]
        public void RejectsDamageAbilityWithoutHitVolume()
        {
            ConfigLoadResult result = LoadMutated("\"hitVolume\": \"player_sword\"", "\"hitVolume\": \"\"");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("hitVolume"), result.ErrorSummary);
        }

        [Test]
        public void RejectsUnknownBuffReference()
        {
            ConfigLoadResult result = LoadMutated("\"onHitBuff\": \"def_down_50\"", "\"onHitBuff\": \"missing_buff\"");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("onHitBuff"), result.ErrorSummary);
        }

        [Test]
        public void RawHashIsStableForSameText()
        {
            string json = RawJson();
            ConfigLoadResult first = CombatConfigLoader.LoadFromText(json, "a");
            ConfigLoadResult second = CombatConfigLoader.LoadFromText(json, "b");
            Assert.IsTrue(first.Success, first.ErrorSummary);
            Assert.IsTrue(second.Success, second.ErrorSummary);
            Assert.AreEqual(first.Config.RawHash, second.Config.RawHash);
            Assert.AreNotEqual(first.Config.RawHash, CombatConfigLoader.Fnv1a(json + " "));
        }

        [Test]
        public void InvalidJsonIsRejectedWithMessage()
        {
            ConfigLoadResult result = CombatConfigLoader.LoadFromText("{ not json", "bad");
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.ErrorSummary.Contains("JSON parse error"), result.ErrorSummary);
        }
    }
}
