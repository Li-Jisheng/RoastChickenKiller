using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CK.Domain.Config
{
    public sealed class ConfigLoadResult
    {
        public CombatConfig Config;
        public readonly List<string> Errors = new List<string>();
        public string SourcePath;
        public string SourceName;
        public string RawJson;

        public bool Success => Config != null && Errors.Count == 0;

        public string ErrorSummary
        {
            get
            {
                if (Errors.Count == 0)
                {
                    return string.Empty;
                }
                var builder = new StringBuilder();
                for (int i = 0; i < Errors.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(" | ");
                    }
                    builder.Append(Errors[i]);
                }
                return builder.ToString();
            }
        }
    }

    /// <summary>
    /// 读取 Config/CombatPrototype.json 的唯一入口。
    /// 生成完整校验的 DTO 图或一组可读错误；
    /// 绝不会对玩法数值静默回退默认值。
    /// </summary>
    public static class CombatConfigLoader
    {
        private static readonly Regex ColorRegex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

        public static string DefaultConfigPath
        {
            get
            {
                string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName;
                return Path.Combine(projectRoot ?? ".", "Config", "CombatPrototype.json");
            }
        }

        public static ConfigLoadResult LoadFromFile(string path)
        {
            var result = new ConfigLoadResult { SourcePath = path, SourceName = Path.GetFileName(path) };
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                result.Errors.Add($"config file not found: {path}");
                return result;
            }
            string json;
            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"cannot read config file: {ex.Message}");
                return result;
            }
            return LoadFromText(json, path);
        }

        public static ConfigLoadResult LoadFromText(string json, string sourceName)
        {
            var result = new ConfigLoadResult { SourcePath = sourceName, SourceName = sourceName, RawJson = json };
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Errors.Add("config is empty");
                return result;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                result.Errors.Add($"JSON parse error: {ex.Message}");
                return result;
            }

            var reader = new Reader(result.Errors);
            var config = new CombatConfig
            {
                SourcePath = sourceName,
                SourceName = Path.GetFileName(sourceName ?? "CombatPrototype.json"),
                RawHash = Fnv1a(json)
            };

            config.Version = reader.Int(root, "version", "root", 1);
            config.TickRate = reader.Int(root, "tickRate", "root", 0);
            if (config.TickRate != 60)
            {
                result.Errors.Add($"tickRate must be 60 (got {config.TickRate})");
            }

            config.Combat = ParseCombat(reader, root);
            config.World = ParseWorld(reader, root);
            config.Actors = ParseActors(reader, root);
            config.HitVolumes = ParseHitVolumes(reader, root);
            config.Buffs = ParseBuffs(reader, root);
            config.Abilities = ParseAbilities(reader, root);
            config.Combo = ParseCombo(reader, root);
            config.Weapons = ParseWeapons(reader, root);
            config.Loadout = ParseLoadout(reader, root);
            config.EnemyAttack = ParseEnemyAttack(reader, root);

            config.BuildIndexes();
            Validate(config, result.Errors);

            result.Config = config;
            return result;
        }

        // ------------------------------------------------------------------
        // 解析
        // ------------------------------------------------------------------

        private static CombatRules ParseCombat(Reader reader, JObject root)
        {
            var rules = new CombatRules();
            JObject combat = reader.Object(root, "combat", "root");
            if (combat == null)
            {
                return rules;
            }
            rules.DefenseConstant = reader.Float(combat, "defenseConstant", "combat", 0);
            rules.MinDefense = reader.Float(combat, "minDefense", "combat", 0);
            rules.Rounding = reader.String(combat, "rounding", "combat", string.Empty);
            rules.HeavyHoldSeconds = reader.Float(combat, "heavyHoldSeconds", "combat", 0);
            rules.ComboWindowSeconds = reader.Float(combat, "comboWindowSeconds", "combat", 0);
            rules.InputBufferFrames = reader.Int(combat, "inputBufferFrames", "combat", 0);
            rules.DodgeInvulnFrames = reader.Int(combat, "dodgeInvulnFrames", "combat", 0);
            rules.DodgeCooldownFrames = reader.Int(combat, "dodgeCooldownFrames", "combat", 0);
            rules.DodgeDistance = reader.Float(combat, "dodgeDistance", "combat", 0);
            rules.BlockMoveMultiplier = reader.Float(combat, "blockMoveMultiplier", "combat", 0);
            return rules;
        }

        private static WorldConfig ParseWorld(Reader reader, JObject root)
        {
            var world = new WorldConfig();
            JObject node = reader.Object(root, "world", "root");
            if (node == null)
            {
                return world;
            }
            world.GroundSize = reader.Float(node, "groundSize", "world", 0);
            world.Gravity = reader.Float(node, "gravity", "world", 0);
            world.PlayerSpawn = reader.Vector3(node, "playerSpawn", "world");
            world.PlayerYaw = reader.Float(node, "playerYaw", "world", 0);
            world.EnemySpawn = reader.Vector3(node, "enemySpawn", "world");
            world.EnemyYaw = reader.Float(node, "enemyYaw", "world", 0);
            world.CameraPosition = reader.Vector3(node, "cameraPosition", "world");
            world.CameraTarget = reader.Vector3(node, "cameraTarget", "world");
            return world;
        }

        private static ActorsConfig ParseActors(Reader reader, JObject root)
        {
            var actors = new ActorsConfig();
            JObject node = reader.Object(root, "actors", "root");
            if (node == null)
            {
                return actors;
            }
            actors.Player = ParseActor(reader, reader.Object(node, "player", "actors"), "actors.player", Faction.Player);
            actors.Enemy = ParseActor(reader, reader.Object(node, "enemy", "actors"), "actors.enemy", Faction.Enemy);
            return actors;
        }

        private static ActorConfig ParseActor(Reader reader, JObject node, string path, Faction defaultFaction)
        {
            if (node == null)
            {
                return null;
            }
            var actor = new ActorConfig
            {
                DisplayName = reader.String(node, "displayName", path, path),
                MaxHealth = reader.Float(node, "maxHealth", path, 0),
                AttackPower = reader.Float(node, "attackPower", path, 0),
                Defense = reader.Float(node, "defense", path, 0),
                MoveSpeed = reader.Float(node, "moveSpeed", path, 0),
                BodyHitVolumeId = reader.String(node, "bodyHitVolumeId", path, string.Empty),
                Color = reader.String(node, "color", path, "#FFFFFF")
            };
            string factionText = reader.String(node, "faction", path, string.Empty);
            if (string.IsNullOrEmpty(factionText))
            {
                actor.Faction = defaultFaction;
            }
            else if (string.Equals(factionText, "player", StringComparison.OrdinalIgnoreCase))
            {
                actor.Faction = Faction.Player;
            }
            else if (string.Equals(factionText, "enemy", StringComparison.OrdinalIgnoreCase))
            {
                actor.Faction = Faction.Enemy;
            }
            else
            {
                reader.Errors.Add($"{path}.faction: expected 'player' or 'enemy' (got '{factionText}')");
                actor.Faction = defaultFaction;
            }
            return actor;
        }

        private static List<HitVolumeConfig> ParseHitVolumes(Reader reader, JObject root)
        {
            var list = new List<HitVolumeConfig>();
            JArray array = reader.Array(root, "hitVolumes", "root");
            if (array == null)
            {
                return list;
            }
            for (int i = 0; i < array.Count; i++)
            {
                JObject node = reader.ObjectAt(array, i, $"hitVolumes[{i}]");
                if (node == null)
                {
                    continue;
                }
                string path = $"hitVolumes[{i}]";
                var volume = new HitVolumeConfig
                {
                    Id = reader.String(node, "id", path, string.Empty),
                    Shape = reader.OptionalString(node, "shape", "Capsule"),
                    Radius = reader.OptionalFloat(node, "radius", 0),
                    Height = reader.OptionalFloat(node, "height", 0),
                    CenterY = reader.OptionalFloat(node, "centerY", 0),
                    Source = reader.OptionalString(node, "source", "body"),
                    RadiusScale = reader.OptionalFloat(node, "radiusScale", 1)
                };
                string owner = reader.String(node, "owner", path, string.Empty);
                if (string.Equals(owner, "player", StringComparison.OrdinalIgnoreCase))
                {
                    volume.Owner = Faction.Player;
                }
                else if (string.Equals(owner, "enemy", StringComparison.OrdinalIgnoreCase))
                {
                    volume.Owner = Faction.Enemy;
                }
                else
                {
                    reader.Errors.Add($"{path}.owner: expected 'player' or 'enemy' (got '{owner}')");
                }
                list.Add(volume);
            }
            return list;
        }

        private static List<BuffConfig> ParseBuffs(Reader reader, JObject root)
        {
            var list = new List<BuffConfig>();
            JArray array = reader.Array(root, "buffs", "root");
            if (array == null)
            {
                return list;
            }
            for (int i = 0; i < array.Count; i++)
            {
                JObject node = reader.ObjectAt(array, i, $"buffs[{i}]");
                if (node == null)
                {
                    continue;
                }
                string path = $"buffs[{i}]";
                list.Add(new BuffConfig
                {
                    Id = reader.String(node, "id", path, string.Empty),
                    DisplayName = reader.String(node, "displayName", path, string.Empty),
                    Stat = reader.String(node, "stat", path, string.Empty),
                    Add = reader.Float(node, "add", path, 0),
                    DurationFrames = reader.Int(node, "durationFrames", path, 0),
                    StackMode = reader.String(node, "stackMode", path, string.Empty)
                });
            }
            return list;
        }

        private static List<AbilityConfig> ParseAbilities(Reader reader, JObject root)
        {
            var list = new List<AbilityConfig>();
            JArray array = reader.Array(root, "abilities", "root");
            if (array == null)
            {
                return list;
            }
            for (int i = 0; i < array.Count; i++)
            {
                JObject node = reader.ObjectAt(array, i, $"abilities[{i}]");
                if (node == null)
                {
                    continue;
                }
                string path = $"abilities[{i}]";
                var ability = new AbilityConfig
                {
                    Id = reader.String(node, "id", path, string.Empty),
                    DisplayName = reader.String(node, "displayName", path, string.Empty),
                    Multiplier = reader.Float(node, "multiplier", path, 0),
                    CooldownFrames = reader.Int(node, "cooldownFrames", path, 0),
                    Hold = reader.Bool(node, "hold", path, false),
                    OnHitBuffId = reader.OptionalString(node, "onHitBuff", string.Empty),
                    HitVolumeId = reader.OptionalString(node, "hitVolume", string.Empty)
                };
                string slot = reader.String(node, "slot", path, string.Empty);
                if (!TryParseSlot(slot, out ability.Slot))
                {
                    reader.Errors.Add($"{path}.slot: unknown slot '{slot}'");
                }
                JArray phases = reader.Array(node, "phases", path);
                if (phases != null)
                {
                    for (int p = 0; p < phases.Count; p++)
                    {
                        JObject phaseNode = reader.ObjectAt(phases, p, $"{path}.phases[{p}]");
                        if (phaseNode == null)
                        {
                            continue;
                        }
                        string phasePath = $"{path}.phases[{p}]";
                        var phase = new PhaseConfig
                        {
                            Name = reader.String(phaseNode, "name", phasePath, string.Empty),
                            StartFrame = reader.Int(phaseNode, "startFrame", phasePath, 0),
                            EndFrame = reader.Int(phaseNode, "endFrame", phasePath, 0)
                        };
                        if (!TryParsePhase(phase.Name, out phase.Kind))
                        {
                            reader.Errors.Add($"{phasePath}.name: expected Windup/Active/Recovery (got '{phase.Name}')");
                        }
                        ability.Phases.Add(phase);
                    }
                }
                list.Add(ability);
            }
            return list;
        }

        private static ComboConfig ParseCombo(Reader reader, JObject root)
        {
            var combo = new ComboConfig();
            JObject node = reader.Object(root, "combo", "root");
            if (node == null)
            {
                return combo;
            }
            JArray steps = reader.Array(node, "steps", "combo");
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Type != JTokenType.String)
                    {
                        reader.Errors.Add($"combo.steps[{i}]: expected string");
                        continue;
                    }
                    combo.Steps.Add(steps[i].Value<string>());
                }
            }
            combo.WindowFrames = reader.Int(node, "windowFrames", "combo", 0);
            return combo;
        }

        private static List<WeaponConfig> ParseWeapons(Reader reader, JObject root)
        {
            var list = new List<WeaponConfig>();
            JArray array = reader.Array(root, "weapons", "root");
            if (array == null)
            {
                return list;
            }
            for (int i = 0; i < array.Count; i++)
            {
                JObject node = reader.ObjectAt(array, i, $"weapons[{i}]");
                if (node == null)
                {
                    continue;
                }
                string path = $"weapons[{i}]";
                list.Add(new WeaponConfig
                {
                    Id = reader.String(node, "id", path, string.Empty),
                    DisplayName = reader.String(node, "displayName", path, string.Empty),
                    Length = reader.Float(node, "length", path, 0),
                    Radius = reader.Float(node, "radius", path, 0),
                    DamageMultiplier = reader.Float(node, "damageMultiplier", path, 1),
                    Color = reader.String(node, "color", path, "#FFFFFF"),
                    HandOffset = reader.Vector3(node, "handOffset", path, new Vector3Data(0.4f, 1.1f, 0.25f))
                });
            }
            return list;
        }

        private static LoadoutConfig ParseLoadout(Reader reader, JObject root)
        {
            var loadout = new LoadoutConfig();
            JObject node = reader.Object(root, "loadout", "root");
            if (node == null)
            {
                return loadout;
            }
            loadout.WeaponSlots = reader.Int(node, "weaponSlots", "loadout", 0);
            loadout.EquipmentSlots = reader.Int(node, "equipmentSlots", "loadout", 0);
            loadout.InventorySlots = reader.Int(node, "inventorySlots", "loadout", 0);
            loadout.ItemBarSlots = reader.Int(node, "itemBarSlots", "loadout", 0);
            return loadout;
        }

        private static EnemyAttackConfig ParseEnemyAttack(Reader reader, JObject root)
        {
            var attack = new EnemyAttackConfig();
            JObject node = reader.Object(root, "enemyAttack", "root");
            if (node == null)
            {
                return attack;
            }
            attack.IntervalFrames = reader.Int(node, "intervalFrames", "enemyAttack", 0);
            attack.AbilityId = reader.String(node, "abilityId", "enemyAttack", string.Empty);
            attack.IdleFrames = reader.Int(node, "idleFrames", "enemyAttack", 0);
            return attack;
        }

        // ------------------------------------------------------------------
        // 语义校验
        // ------------------------------------------------------------------

        private static void Validate(CombatConfig config, List<string> errors)
        {
            CombatRules rules = config.Combat ?? new CombatRules();
            if (!(rules.DefenseConstant > 0))
            {
                errors.Add("combat.defenseConstant must be > 0");
            }
            if (rules.MinDefense < 0)
            {
                errors.Add("combat.minDefense must be >= 0");
            }
            if (!string.Equals(rules.Rounding, "AwayFromZero", StringComparison.Ordinal))
            {
                errors.Add("combat.rounding must be 'AwayFromZero' in this phase");
            }
            if (!(rules.HeavyHoldSeconds > 0))
            {
                errors.Add("combat.heavyHoldSeconds must be > 0");
            }
            if (!(rules.ComboWindowSeconds > 0))
            {
                errors.Add("combat.comboWindowSeconds must be > 0");
            }
            if (rules.InputBufferFrames <= 0)
            {
                errors.Add("combat.inputBufferFrames must be > 0");
            }
            if (rules.DodgeInvulnFrames < 0)
            {
                errors.Add("combat.dodgeInvulnFrames must be >= 0");
            }
            if (rules.DodgeCooldownFrames < 0)
            {
                errors.Add("combat.dodgeCooldownFrames must be >= 0");
            }
            if (!(rules.DodgeDistance > 0))
            {
                errors.Add("combat.dodgeDistance must be > 0");
            }
            if (rules.BlockMoveMultiplier <= 0 || rules.BlockMoveMultiplier > 1)
            {
                errors.Add("combat.blockMoveMultiplier must be in (0, 1]");
            }
            if (config.Combo != null && config.Combo.WindowFrames > 0 &&
                Math.Abs(rules.ComboWindowSeconds * config.TickRate - config.Combo.WindowFrames) > 0.5f)
            {
                errors.Add($"combat.comboWindowSeconds ({rules.ComboWindowSeconds}) does not match combo.windowFrames ({config.Combo.WindowFrames}) at {config.TickRate} Hz");
            }

            if (config.World == null || !(config.World.GroundSize > 0))
            {
                errors.Add("world.groundSize must be > 0");
            }
            if (config.World != null && config.World.Gravity == 0)
            {
                errors.Add("world.gravity must not be 0");
            }

            ValidateActor(config.Actors?.Player, "actors.player", errors);
            ValidateActor(config.Actors?.Enemy, "actors.enemy", errors);
            if (config.Actors?.Player != null && config.Actors.Enemy != null &&
                config.Actors.Player.Faction == config.Actors.Enemy.Faction)
            {
                errors.Add("actors.player and actors.enemy must be on different factions");
            }

            var volumeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (HitVolumeConfig volume in config.HitVolumes)
            {
                if (string.IsNullOrEmpty(volume.Id))
                {
                    errors.Add("hitVolumes: id must not be empty");
                    continue;
                }
                if (!volumeIds.Add(volume.Id))
                {
                    errors.Add($"hitVolumes: duplicate id '{volume.Id}'");
                }
                if (!string.Equals(volume.Shape, "Capsule", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"hitVolumes.{volume.Id}.shape: '{volume.Shape}' is reserved but not implemented in phase 1 (use Capsule)");
                }
                if (volume.IsWeapon)
                {
                    if (!(volume.RadiusScale > 0))
                    {
                        errors.Add($"hitVolumes.{volume.Id}.radiusScale must be > 0");
                    }
                }
                else
                {
                    if (!(volume.Radius > 0))
                    {
                        errors.Add($"hitVolumes.{volume.Id}.radius must be > 0");
                    }
                    if (volume.Height < volume.Radius * 2)
                    {
                        errors.Add($"hitVolumes.{volume.Id}.height must be >= 2*radius");
                    }
                    if (volume.CenterY < 0)
                    {
                        errors.Add($"hitVolumes.{volume.Id}.centerY must be >= 0");
                    }
                }
            }

            foreach (ActorConfig actor in new[] { config.Actors?.Player, config.Actors?.Enemy })
            {
                if (actor != null && !string.IsNullOrEmpty(actor.BodyHitVolumeId) &&
                    !volumeIds.Contains(actor.BodyHitVolumeId))
                {
                    errors.Add($"actor bodyHitVolumeId '{actor.BodyHitVolumeId}' does not exist in hitVolumes");
                }
            }

            var buffIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (BuffConfig buff in config.Buffs)
            {
                if (string.IsNullOrEmpty(buff.Id))
                {
                    errors.Add("buffs: id must not be empty");
                    continue;
                }
                if (!buffIds.Add(buff.Id))
                {
                    errors.Add($"buffs: duplicate id '{buff.Id}'");
                }
                if (!string.Equals(buff.Stat, "defense", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"buffs.{buff.Id}.stat: only 'defense' is supported");
                }
                if (buff.DurationFrames <= 0)
                {
                    errors.Add($"buffs.{buff.Id}.durationFrames must be > 0");
                }
                if (!string.Equals(buff.StackMode, "Refresh", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"buffs.{buff.Id}.stackMode: only 'Refresh' is supported");
                }
            }

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (AbilityConfig ability in config.Abilities)
            {
                string path = $"abilities[{ability.Id}]";
                if (string.IsNullOrEmpty(ability.Id))
                {
                    errors.Add("abilities: id must not be empty");
                    continue;
                }
                if (!abilityIds.Add(ability.Id))
                {
                    errors.Add($"abilities: duplicate id '{ability.Id}'");
                }
                if (ability.Multiplier < 0)
                {
                    errors.Add($"{path}.multiplier must be >= 0");
                }
                if (ability.CooldownFrames < 0)
                {
                    errors.Add($"{path}.cooldownFrames must be >= 0");
                }
                if (!string.IsNullOrEmpty(ability.OnHitBuffId) && !buffIds.Contains(ability.OnHitBuffId))
                {
                    errors.Add($"{path}.onHitBuff '{ability.OnHitBuffId}' does not exist");
                }
                if (!string.IsNullOrEmpty(ability.HitVolumeId) && !volumeIds.Contains(ability.HitVolumeId))
                {
                    errors.Add($"{path}.hitVolume '{ability.HitVolumeId}' does not exist");
                }
                if (ability.Multiplier > 0 && string.IsNullOrEmpty(ability.HitVolumeId))
                {
                    errors.Add($"{path} deals damage but has no hitVolume");
                }
                if (ability.HitVolumeId == null)
                {
                    ability.HitVolumeId = string.Empty;
                }

                ValidatePhases(ability, path, errors);
            }

            if (config.Combo == null || config.Combo.Steps.Count == 0)
            {
                errors.Add("combo.steps must not be empty");
            }
            else
            {
                config.Combo.AbilityIndices.Clear();
                for (int i = 0; i < config.Combo.Steps.Count; i++)
                {
                    int index = config.FindAbilityIndex(config.Combo.Steps[i]);
                    if (index < 0)
                    {
                        errors.Add($"combo.steps[{i}] references unknown ability '{config.Combo.Steps[i]}'");
                    }
                    else
                    {
                        config.Combo.AbilityIndices.Add(index);
                    }
                }
                if (config.Combo.WindowFrames <= 0)
                {
                    errors.Add("combo.windowFrames must be > 0");
                }
            }

            var weaponIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WeaponConfig weapon in config.Weapons)
            {
                if (string.IsNullOrEmpty(weapon.Id))
                {
                    errors.Add("weapons: id must not be empty");
                    continue;
                }
                if (!weaponIds.Add(weapon.Id))
                {
                    errors.Add($"weapons: duplicate id '{weapon.Id}'");
                }
                if (!(weapon.Length > 0))
                {
                    errors.Add($"weapons.{weapon.Id}.length must be > 0");
                }
                if (!(weapon.Radius > 0))
                {
                    errors.Add($"weapons.{weapon.Id}.radius must be > 0");
                }
                if (!(weapon.DamageMultiplier >= 0))
                {
                    errors.Add($"weapons.{weapon.Id}.damageMultiplier must be >= 0");
                }
                if (!ColorRegex.IsMatch(weapon.Color ?? string.Empty))
                {
                    errors.Add($"weapons.{weapon.Id}.color must be #RRGGBB");
                }
            }
            if (config.Weapons.Count == 0)
            {
                errors.Add("weapons must contain at least one entry");
            }

            LoadoutConfig loadout = config.Loadout ?? new LoadoutConfig();
            if (loadout.WeaponSlots <= 0 || loadout.EquipmentSlots <= 0 || loadout.InventorySlots <= 0 ||
                loadout.ItemBarSlots <= 0)
            {
                errors.Add("loadout slot counts must all be > 0");
            }

            if (config.EnemyAttack != null)
            {
                AbilityConfig enemyAbility = config.FindAbility(config.EnemyAttack.AbilityId);
                if (enemyAbility == null)
                {
                    errors.Add($"enemyAttack.abilityId '{config.EnemyAttack.AbilityId}' does not exist");
                }
                else
                {
                    if (enemyAbility.Slot != AbilitySlot.EnemyAttack)
                    {
                        errors.Add($"enemyAttack ability '{enemyAbility.Id}' must use slot EnemyAttack");
                    }
                    int expected = enemyAbility.TotalFrames + config.EnemyAttack.IdleFrames;
                    if (config.EnemyAttack.IntervalFrames != expected)
                    {
                        errors.Add($"enemyAttack.intervalFrames ({config.EnemyAttack.IntervalFrames}) must equal ability frames ({enemyAbility.TotalFrames}) + idleFrames ({config.EnemyAttack.IdleFrames}) = {expected}");
                    }
                }
            }

            // 可玩原型必须存在面向玩家的控制技能。
            if (config.FindAbilityBySlot(AbilitySlot.Primary) == null)
            {
                errors.Add("no ability uses slot Primary");
            }
            if (config.FindAbilityBySlot(AbilitySlot.Dodge) == null)
            {
                errors.Add("no ability uses slot Dodge");
            }
            if (config.FindAbilityBySlot(AbilitySlot.Block) == null)
            {
                errors.Add("no ability uses slot Block");
            }
            if (config.FindAbilityBySlot(AbilitySlot.Skill1) == null ||
                config.FindAbilityBySlot(AbilitySlot.Skill2) == null ||
                config.FindAbilityBySlot(AbilitySlot.Skill3) == null ||
                config.FindAbilityBySlot(AbilitySlot.Skill4) == null)
            {
                errors.Add("Q/E/R/F abilities (Skill1..Skill4) must all be defined");
            }
        }

        private static void ValidateActor(ActorConfig actor, string path, List<string> errors)
        {
            if (actor == null)
            {
                errors.Add($"{path} is missing");
                return;
            }
            if (!(actor.MaxHealth > 0))
            {
                errors.Add($"{path}.maxHealth must be > 0");
            }
            if (actor.AttackPower < 0)
            {
                errors.Add($"{path}.attackPower must be >= 0");
            }
            if (actor.Defense < 0)
            {
                errors.Add($"{path}.defense must be >= 0");
            }
            if (actor.MoveSpeed < 0)
            {
                errors.Add($"{path}.moveSpeed must be >= 0");
            }
        }

        private static void ValidatePhases(AbilityConfig ability, string path, List<string> errors)
        {
            List<PhaseConfig> phases = ability.Phases;
            if (phases.Count == 0)
            {
                if (!ability.Hold)
                {
                    errors.Add($"{path}.phases must not be empty for a non-hold ability");
                }
                return;
            }

            int activeCount = 0;
            int endFrame = 0;
            int activeStart = -1;
            int activeEnd = -1;
            var sorted = new List<PhaseConfig>(phases);
            sorted.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
            for (int i = 0; i < sorted.Count; i++)
            {
                PhaseConfig phase = sorted[i];
                if (phase.StartFrame < 0 || phase.EndFrame < phase.StartFrame)
                {
                    errors.Add($"{path}.phases[{phase.Name}]: invalid frame range {phase.StartFrame}..{phase.EndFrame}");
                }
                if (i > 0 && phase.StartFrame <= sorted[i - 1].EndFrame)
                {
                    errors.Add($"{path}.phases: '{sorted[i - 1].Name}' and '{phase.Name}' overlap");
                }
                endFrame = Math.Max(endFrame, phase.EndFrame);
                if (phase.Kind == PhaseKind.Active)
                {
                    activeCount++;
                    activeStart = phase.StartFrame;
                    activeEnd = phase.EndFrame;
                }
            }
            if (activeCount > 1)
            {
                errors.Add($"{path}.phases must contain at most one Active phase");
            }
            if (ability.Multiplier > 0 && activeCount == 0)
            {
                errors.Add($"{path} deals damage but has no Active phase");
            }
            if (!string.IsNullOrEmpty(ability.HitVolumeId) && activeCount == 0)
            {
                errors.Add($"{path} has a hitVolume but no Active phase");
            }
            ability.ActiveStartFrame = activeStart;
            ability.ActiveEndFrame = activeEnd;
            ability.EndFrame = endFrame;
        }

        // ------------------------------------------------------------------
        // 辅助函数
        // ------------------------------------------------------------------

        private static bool TryParseSlot(string text, out AbilitySlot slot)
        {
            slot = AbilitySlot.Primary;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            if (!Enum.TryParse(text, true, out slot))
            {
                return false;
            }
            return Enum.IsDefined(typeof(AbilitySlot), slot);
        }

        private static bool TryParsePhase(string text, out PhaseKind kind)
        {
            kind = PhaseKind.Windup;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            switch (text.ToLowerInvariant())
            {
                case "windup":
                    kind = PhaseKind.Windup;
                    return true;
                case "active":
                    kind = PhaseKind.Active;
                    return true;
                case "recovery":
                    kind = PhaseKind.Recovery;
                    return true;
                default:
                    return false;
            }
        }

        public static uint Fnv1a(string text)
        {
            uint hash = 2166136261u;
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 16777619u;
            }
            return hash;
        }

        private sealed class Reader
        {
            public readonly List<string> Errors;

            public Reader(List<string> errors)
            {
                Errors = errors;
            }

            public JObject Object(JObject owner, string name, string path)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing object");
                    return null;
                }
                if (token.Type != JTokenType.Object)
                {
                    Errors.Add($"{path}.{name}: expected object");
                    return null;
                }
                return (JObject)token;
            }

            public JObject ObjectAt(JArray array, int index, string path)
            {
                JToken token = array[index];
                if (token.Type != JTokenType.Object)
                {
                    Errors.Add($"{path}: expected object");
                    return null;
                }
                return (JObject)token;
            }

            public JArray Array(JObject owner, string name, string path)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing array");
                    return null;
                }
                if (token.Type != JTokenType.Array)
                {
                    Errors.Add($"{path}.{name}: expected array");
                    return null;
                }
                return (JArray)token;
            }

            public int Int(JObject owner, string name, string path, int fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing integer");
                    return fallback;
                }
                if (token.Type != JTokenType.Integer)
                {
                    Errors.Add($"{path}.{name}: expected integer");
                    return fallback;
                }
                return token.Value<int>();
            }

            public float Float(JObject owner, string name, string path, float fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing number");
                    return fallback;
                }
                if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                {
                    Errors.Add($"{path}.{name}: expected number");
                    return fallback;
                }
                return token.Value<float>();
            }

            public string String(JObject owner, string name, string path, string fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing string");
                    return fallback;
                }
                if (token.Type != JTokenType.String)
                {
                    Errors.Add($"{path}.{name}: expected string");
                    return fallback;
                }
                return token.Value<string>();
            }

            /// <summary>允许缺失；存在时必须是字符串。</summary>
            public string OptionalString(JObject owner, string name, string fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    return fallback;
                }
                if (token.Type != JTokenType.String)
                {
                    Errors.Add($"{name}: expected string");
                    return fallback;
                }
                return token.Value<string>();
            }

            /// <summary>允许缺失；存在时必须是数字。</summary>
            public float OptionalFloat(JObject owner, string name, float fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    return fallback;
                }
                if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                {
                    Errors.Add($"{name}: expected number");
                    return fallback;
                }
                return token.Value<float>();
            }

            public bool Bool(JObject owner, string name, string path, bool fallback)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    return fallback;
                }
                if (token.Type != JTokenType.Boolean)
                {
                    Errors.Add($"{path}.{name}: expected boolean");
                    return fallback;
                }
                return token.Value<bool>();
            }

            public Vector3Data Vector3(JObject owner, string name, string path, Vector3Data? fallback = null)
            {
                JToken token = owner?[name];
                if (token == null || token.Type == JTokenType.Null)
                {
                    Errors.Add($"{path}.{name}: missing [x, y, z] array");
                    return fallback ?? new Vector3Data();
                }
                if (token.Type != JTokenType.Array || ((JArray)token).Count != 3)
                {
                    Errors.Add($"{path}.{name}: expected [x, y, z]");
                    return fallback ?? new Vector3Data();
                }
                var array = (JArray)token;
                try
                {
                    return new Vector3Data(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                }
                catch (Exception)
                {
                    Errors.Add($"{path}.{name}: values must be numbers");
                    return fallback ?? new Vector3Data();
                }
            }
        }
    }
}
