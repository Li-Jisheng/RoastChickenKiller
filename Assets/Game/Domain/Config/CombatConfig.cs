using System;
using System.Collections.Generic;

namespace CK.Domain.Config
{
    public enum Faction
    {
        Player = 0,
        Enemy = 1
    }

    /// <summary>镜像原生 CK_SLOT_* 枚举；顺序属于 ABI。</summary>
    public enum AbilitySlot
    {
        Primary = 0,
        Heavy = 1,
        Block = 2,
        Dodge = 3,
        Skill1 = 4,
        Skill2 = 5,
        Skill3 = 6,
        Skill4 = 7,
        EnemyAttack = 8
    }

    /// <summary>镜像原生 CK_PHASE_* 枚举。</summary>
    public enum PhaseKind
    {
        Windup = 0,
        Active = 1,
        Recovery = 2
    }

    [Serializable]
    public struct Vector3Data
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Data(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return $"({X:0.##}, {Y:0.##}, {Z:0.##})";
        }
    }

    public sealed class CombatRules
    {
        public float DefenseConstant;
        public float MinDefense;
        public string Rounding;
        public float HeavyHoldSeconds;
        public float ComboWindowSeconds;
        public int InputBufferFrames;
        public int DodgeInvulnFrames;
        public int DodgeCooldownFrames;
        public float DodgeDistance;
        public float BlockMoveMultiplier;
    }

    public sealed class WorldConfig
    {
        public float GroundSize;
        public float Gravity;
        public Vector3Data PlayerSpawn;
        public float PlayerYaw;
        public Vector3Data EnemySpawn;
        public float EnemyYaw;
        public Vector3Data CameraPosition;
        public Vector3Data CameraTarget;
    }

    public sealed class ActorConfig
    {
        public string DisplayName;
        public Faction Faction;
        public float MaxHealth;
        public float AttackPower;
        public float Defense;
        public float MoveSpeed;
        public string BodyHitVolumeId;
        public string Color;
    }

    public sealed class ActorsConfig
    {
        public ActorConfig Player;
        public ActorConfig Enemy;
    }

    public sealed class HitVolumeConfig
    {
        public string Id;
        public Faction Owner;
        public string Shape;
        public float Radius;
        public float Height;
        public float CenterY;
        public string Source;
        public float RadiusScale;

        public bool IsWeapon => string.Equals(Source, "weapon", StringComparison.OrdinalIgnoreCase);

        /// <summary>类圆柱身体体积；半径用于 X/Z，高度用于 Y。</summary>
        public override string ToString()
        {
            return IsWeapon ? $"{Id}(weapon, x{RadiusScale:0.##})" : $"{Id}({Shape} r{Radius:0.##} h{Height:0.##})";
        }
    }

    public sealed class BuffConfig
    {
        public string Id;
        public string DisplayName;
        public string Stat;
        public float Add;
        public int DurationFrames;
        public string StackMode;
    }

    public sealed class PhaseConfig
    {
        public PhaseKind Kind;
        public string Name;
        public int StartFrame;
        public int EndFrame;
    }

    public sealed class AbilityConfig
    {
        public string Id;
        public string DisplayName;
        public AbilitySlot Slot;
        public float Multiplier;
        public int CooldownFrames;
        public bool Hold;
        public string OnHitBuffId;
        public string HitVolumeId;
        public List<PhaseConfig> Phases = new List<PhaseConfig>();

        // 由加载器计算。
        public int ActiveStartFrame = -1;
        public int ActiveEndFrame = -1;
        public int EndFrame;

        public bool HasActiveWindow => ActiveStartFrame >= 0;
        public int TotalFrames => EndFrame + 1;

        public PhaseKind PhaseAt(int frame)
        {
            for (int i = 0; i < Phases.Count; i++)
            {
                if (frame >= Phases[i].StartFrame && frame <= Phases[i].EndFrame)
                {
                    return Phases[i].Kind;
                }
            }
            return PhaseKind.Windup;
        }

        public override string ToString()
        {
            return $"{Id}[{Slot}] x{Multiplier:0.##}";
        }
    }

    public sealed class ComboConfig
    {
        public List<string> Steps = new List<string>();
        public int WindowFrames;
        public List<int> AbilityIndices = new List<int>();
    }

    public sealed class WeaponConfig
    {
        public string Id;
        public string DisplayName;
        public float Length;
        public float Radius;
        public float DamageMultiplier;
        public string Color;
        public Vector3Data HandOffset;
    }

    public sealed class LoadoutConfig
    {
        public int WeaponSlots;
        public int EquipmentSlots;
        public int InventorySlots;
        public int ItemBarSlots;
    }

    public sealed class EnemyAttackConfig
    {
        public int IntervalFrames;
        public string AbilityId;
        public int IdleFrames;
    }

    /// <summary>
    /// 按约定不可变的 Config/CombatPrototype.json 快照。
    /// 加载器执行全部校验；运行时代码不再解释 JSON。
    /// </summary>
    public sealed class CombatConfig
    {
        public int Version;
        public int TickRate;
        public CombatRules Combat;
        public WorldConfig World;
        public ActorsConfig Actors;
        public List<HitVolumeConfig> HitVolumes = new List<HitVolumeConfig>();
        public List<BuffConfig> Buffs = new List<BuffConfig>();
        public List<AbilityConfig> Abilities = new List<AbilityConfig>();
        public ComboConfig Combo;
        public List<WeaponConfig> Weapons = new List<WeaponConfig>();
        public LoadoutConfig Loadout;
        public EnemyAttackConfig EnemyAttack;

        public string SourcePath;
        public string SourceName;
        /// <summary>原始 JSON 的 FNV-1a 哈希，显示在 HUD 上，便于截图对应数据。</summary>
        public uint RawHash;

        private readonly Dictionary<string, int> _abilityIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _buffIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, HitVolumeConfig> _hitVolumes = new Dictionary<string, HitVolumeConfig>(StringComparer.Ordinal);

        public void BuildIndexes()
        {
            _abilityIndex.Clear();
            _buffIndex.Clear();
            _hitVolumes.Clear();
            for (int i = 0; i < Abilities.Count; i++)
            {
                _abilityIndex[Abilities[i].Id] = i;
            }
            for (int i = 0; i < Buffs.Count; i++)
            {
                _buffIndex[Buffs[i].Id] = i;
            }
            for (int i = 0; i < HitVolumes.Count; i++)
            {
                _hitVolumes[HitVolumes[i].Id] = HitVolumes[i];
            }
        }

        public int FindAbilityIndex(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }
            return _abilityIndex.TryGetValue(id, out int index) ? index : -1;
        }

        public AbilityConfig FindAbility(string id)
        {
            int index = FindAbilityIndex(id);
            return index >= 0 ? Abilities[index] : null;
        }

        public AbilityConfig FindAbilityBySlot(AbilitySlot slot)
        {
            for (int i = 0; i < Abilities.Count; i++)
            {
                if (Abilities[i].Slot == slot)
                {
                    return Abilities[i];
                }
            }
            return null;
        }

        public bool TryGetAbilityBySlot(AbilitySlot slot, out AbilityConfig ability)
        {
            ability = FindAbilityBySlot(slot);
            return ability != null;
        }

        public int FindBuffIndex(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return -1;
            }
            return _buffIndex.TryGetValue(id, out int index) ? index : -1;
        }

        public HitVolumeConfig FindHitVolume(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }
            return _hitVolumes.TryGetValue(id, out HitVolumeConfig volume) ? volume : null;
        }

        public HitVolumeConfig GetActorHitVolume(Faction faction)
        {
            return FindHitVolume(faction == Faction.Player ? Actors.Player.BodyHitVolumeId : Actors.Enemy.BodyHitVolumeId);
        }

        public string GetComboStepId(int step)
        {
            if (Combo == null || Combo.Steps.Count == 0)
            {
                return null;
            }
            return Combo.Steps[((step % Combo.Steps.Count) + Combo.Steps.Count) % Combo.Steps.Count];
        }

        public override string ToString()
        {
            return $"CombatPrototype v{Version} tick={TickRate} abilities={Abilities.Count} weapons={Weapons.Count} hash={RawHash:X8}";
        }
    }
}
