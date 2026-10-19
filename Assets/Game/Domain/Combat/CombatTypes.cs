using System;
using System.Collections.Generic;

namespace CK.Domain.Combat
{
    /// <summary>Mirrors the native CK_EV_* enum. Values are ABI.</summary>
    public enum CombatEventType
    {
        None = 0,
        Hit = 1,
        Blocked = 2,
        Dodged = 3,
        BuffApplied = 4,
        AbilityStarted = 5,
        AbilityEnded = 6,
        Rejected = 7,
        BuffExpired = 8,
        Contact = 9,
        BlockStarted = 10,
        BlockEnded = 11,
        DodgeStarted = 12
    }

    /// <summary>Mirrors the native CK_REJECT_* enum.</summary>
    public enum CombatRejectReason
    {
        None = 0,
        Cooldown = 1,
        Busy = 2,
        Blocking = 3,
        Expired = 4,
        NoAbility = 5,
        State = 6
    }

    /// <summary>Mirrors the native CK_CMD_* enum.</summary>
    public enum CombatCommandKind
    {
        None = 0,
        Primary = 1,
        Heavy = 2,
        BlockPress = 3,
        BlockRelease = 4,
        Dodge = 5,
        Skill1 = 6,
        Skill2 = 7,
        Skill3 = 8,
        Skill4 = 9,
        EnemyAttack = 10,
        ActivateAbility = 11
    }

    public struct CombatEvent
    {
        public uint Tick;
        public CombatEventType Type;
        public int SourceActor;
        public int TargetActor;
        public int AbilityIndex;
        public int BuffIndex;
        public bool Overlap;
        public CombatRejectReason Reason;
        public int DefenseBefore;
        public int DefenseAfter;
        public int HealthAfter;
        public int Sequence;
        public float Amount;

        public string TypeName => Type.ToString();

        public override string ToString()
        {
            return $"t{Tick} {Type} src={SourceActor} tgt={TargetActor} ability={AbilityIndex} " +
                   $"dmg={Amount:0} overlap={(Overlap ? 1 : 0)} def={DefenseBefore}->{DefenseAfter} hp={HealthAfter}";
        }
    }

    /// <summary>Managed mirror of CkActorSnapshot. No native handles escape the bridge.</summary>
    public sealed class ActorSnapshot
    {
        public int ActorId;
        public int Faction;
        public float Health;
        public float MaxHealth;
        public float AttackPower;
        public float BaseDefense;
        public float EffectiveDefense;
        public bool Blocking;
        public bool Invulnerable;
        public bool Busy;
        public int AbilityIndex;
        public int Phase; // -1 none, 0 windup, 1 active, 2 recovery
        public int AbilityFrame;
        public int ComboStep;
        public int ComboCount;
        public int ComboWindowRemaining;
        public int[] CooldownBySlot = new int[9];
        public bool BuffActive;
        public int BuffRemainingFrames;
        public int BuffCount;
        public int TotalHits;
        public int LastAbilityIndex;
        public float LastDamage;
        public CombatEventType LastEventType;

        public string PhaseName
        {
            get
            {
                switch (Phase)
                {
                    case 0: return "Windup";
                    case 1: return "Active";
                    case 2: return "Recovery";
                    default: return "Idle";
                }
            }
        }

        public override string ToString()
        {
            return $"actor{ActorId} hp={Health:0}/{MaxHealth:0} def={EffectiveDefense:0} busy={Busy} block={Blocking} inv={Invulnerable} ability={AbilityIndex}@{AbilityFrame}({PhaseName}) combo={ComboStep}/{ComboCount}";
        }
    }

    /// <summary>Per-tick world volumes submitted to the native core.</summary>
    public struct CapsuleVolume
    {
        public float Ax, Ay, Az;
        public float Bx, By, Bz;
        public float Radius;
    }

    public struct ActorFrameVolumes
    {
        public int ActorId;
        public CapsuleVolume Body;
        public CapsuleVolume Weapon;
        public int WeaponActiveHint;
    }

    public sealed class CombatTickResult
    {
        public readonly List<CombatEvent> Events = new List<CombatEvent>(16);
        public uint Tick;
    }
}
