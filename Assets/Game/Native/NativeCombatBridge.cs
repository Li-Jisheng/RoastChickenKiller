using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CK.Domain.Combat;
using CK.Domain.Config;

namespace CK.Native
{
    /// <summary>
    /// Thin, allocation-light P/Invoke layer over ck_combat.dll.
    /// All gameplay decisions stay in C++; this class only maps DTOs.
    /// </summary>
    public sealed class NativeCombatBridge : IDisposable
    {
        public const int AbiVersion = 1;
        public const int MaxAbilities = 64;
        public const int MaxBuffs = 32;
        public const int MaxActors = 8;

        private const string Library = "ck_combat";
        private const int SlotCount = 9;

        private IntPtr _handle;
        private CkEvent[] _eventBuffer;
        private CombatEvent[] _managedEvents;

        public bool IsLoaded => _handle != IntPtr.Zero;
        public CombatConfig Config { get; private set; }
        public uint NativeConfigHash { get; private set; }
        public int ActorCount { get; private set; }

        public static string BuildInfo
        {
            get
            {
                try
                {
                    return Marshal.PtrToStringAnsi(ck_get_build_info()) ?? "unknown";
                }
                catch (Exception ex)
                {
                    return $"unavailable ({ex.GetType().Name})";
                }
            }
        }

        public static int GetAbiVersion()
        {
            try
            {
                return ck_get_abi_version();
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static string GetLastError()
        {
            try
            {
                return Marshal.PtrToStringAnsi(ck_get_last_error()) ?? string.Empty;
            }
            catch (Exception)
            {
                return "native library unavailable";
            }
        }

        /// <summary>Checks that the native plugin loads and reports ABI 1.</summary>
        public static bool Probe(out string message)
        {
            try
            {
                int abi = ck_get_abi_version();
                if (abi != AbiVersion)
                {
                    message = $"ck_combat ABI mismatch: expected {AbiVersion}, got {abi}";
                    return false;
                }
                message = $"ck_combat ABI {abi}: {BuildInfo}";
                return true;
            }
            catch (DllNotFoundException)
            {
                message = "ck_combat.dll not found. Run Tools/build_native.ps1 to build and install the plugin.";
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                message = $"ck_combat.dll is stale (missing export): {ex.Message}";
                return false;
            }
            catch (BadImageFormatException ex)
            {
                message = $"ck_combat.dll has the wrong architecture: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                message = $"ck_combat load failed: {ex}";
                return false;
            }
        }

        public bool Initialize(CombatConfig config, out string error)
        {
            Dispose();
            Config = config;
            error = string.Empty;
            if (config == null)
            {
                error = "config is null";
                return false;
            }
            if (config.Abilities.Count > MaxAbilities || config.Buffs.Count > MaxBuffs)
            {
                error = $"config exceeds native capacity (abilities {config.Abilities.Count}/{MaxAbilities}, buffs {config.Buffs.Count}/{MaxBuffs})";
                return false;
            }

            try
            {
                var global = new CkGlobalConfig
                {
                    TickRate = config.TickRate,
                    DefenseConstant = config.Combat.DefenseConstant,
                    MinDefense = config.Combat.MinDefense,
                    InputBufferFrames = config.Combat.InputBufferFrames,
                    DodgeInvulnFrames = config.Combat.DodgeInvulnFrames,
                    DodgeCooldownFrames = config.Combat.DodgeCooldownFrames,
                    DodgeDistance = config.Combat.DodgeDistance,
                    BlockMoveMultiplier = config.Combat.BlockMoveMultiplier,
                    ComboWindowFrames = config.Combo.WindowFrames,
                    EnemyIntervalFrames = config.EnemyAttack.IntervalFrames,
                    MaxAbilities = MaxAbilities,
                    MaxBuffs = MaxBuffs,
                    MaxActors = MaxActors
                };
                _handle = ck_combat_create(ref global);
                if (_handle == IntPtr.Zero)
                {
                    error = "ck_combat_create failed: " + GetLastError();
                    return false;
                }

                // Buffs must exist before abilities so onHitBuff indices resolve.
                for (int i = 0; i < config.Buffs.Count; i++)
                {
                    BuffConfig buff = config.Buffs[i];
                    int buffStat = string.Equals(buff.Stat, "defense", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
                    int index = ck_combat_add_buff_ex(_handle, buff.Id, buffStat, buff.Add, buff.DurationFrames, 0);
                    if (index != i)
                    {
                        error = $"native add_buff '{buff.Id}' failed (expected {i}, got {index}): {GetLastError()}";
                        Dispose();
                        return false;
                    }
                }

                for (int i = 0; i < config.Abilities.Count; i++)
                {
                    AbilityConfig ability = config.Abilities[i];
                    var phaseTriples = new int[ability.Phases.Count * 3];
                    for (int p = 0; p < ability.Phases.Count; p++)
                    {
                        phaseTriples[p * 3] = (int)ability.Phases[p].Kind;
                        phaseTriples[p * 3 + 1] = ability.Phases[p].StartFrame;
                        phaseTriples[p * 3 + 2] = ability.Phases[p].EndFrame;
                    }
                    int onHitBuff = config.FindBuffIndex(ability.OnHitBuffId);
                    int index = ck_combat_add_ability_ex(
                        _handle,
                        ability.Id,
                        (int)ability.Slot,
                        ability.Multiplier,
                        ability.CooldownFrames,
                        ability.Hold ? 1 : 0,
                        onHitBuff,
                        string.IsNullOrEmpty(ability.HitVolumeId) ? 0 : 1,
                        phaseTriples,
                        ability.Phases.Count);
                    if (index != i)
                    {
                        error = $"native add_ability '{ability.Id}' failed (expected {i}, got {index}): {GetLastError()}";
                        Dispose();
                        return false;
                    }
                }

                ActorConfig player = config.Actors.Player;
                ActorConfig enemy = config.Actors.Enemy;
                int playerIndex = ck_combat_add_actor_ex(_handle, "player", (int)player.Faction, player.MaxHealth,
                    player.AttackPower, player.Defense, player.MoveSpeed);
                int enemyIndex = ck_combat_add_actor_ex(_handle, "enemy", (int)enemy.Faction, enemy.MaxHealth,
                    enemy.AttackPower, enemy.Defense, enemy.MoveSpeed);
                if (playerIndex != 0 || enemyIndex != 1)
                {
                    error = $"native add_actor failed (player={playerIndex}, enemy={enemyIndex}): {GetLastError()}";
                    Dispose();
                    return false;
                }

                var comboIndices = new int[config.Combo.AbilityIndices.Count];
                for (int i = 0; i < comboIndices.Length; i++)
                {
                    comboIndices[i] = config.Combo.AbilityIndices[i];
                }
                int comboResult = ck_combat_set_combo(_handle, comboIndices, comboIndices.Length);
                if (comboResult != comboIndices.Length)
                {
                    error = $"native set_combo failed: {GetLastError()}";
                    Dispose();
                    return false;
                }

                if (ck_combat_finalize(_handle) == 0)
                {
                    error = "native finalize rejected config: " + GetLastError();
                    Dispose();
                    return false;
                }

                NativeConfigHash = ck_combat_config_hash(_handle);
                ActorCount = ck_combat_actor_count(_handle);
                _eventBuffer = new CkEvent[64];
                _managedEvents = new CombatEvent[64];
                return true;
            }
            catch (DllNotFoundException)
            {
                error = "ck_combat.dll not found. Run Tools/build_native.ps1.";
                Dispose();
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                error = "ck_combat.dll is stale: " + ex.Message;
                Dispose();
                return false;
            }
            catch (BadImageFormatException ex)
            {
                error = "ck_combat.dll architecture mismatch: " + ex.Message;
                Dispose();
                return false;
            }
        }

        public void SubmitCommand(int actorId, CombatCommandKind kind, uint issuedTick, uint sequence, int abilityIndex = -1)
        {
            if (!IsLoaded)
            {
                return;
            }
            var command = new CkCommand
            {
                ActorId = actorId,
                Kind = (int)kind,
                AbilityIndex = abilityIndex,
                IssuedTick = issuedTick,
                Sequence = sequence
            };
            ck_combat_submit_command(_handle, ref command);
        }

        public void Tick(IReadOnlyList<ActorFrameVolumes> frames)
        {
            if (!IsLoaded)
            {
                return;
            }
            var nativeFrames = new CkActorFrame[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                nativeFrames[i] = ToNative(frames[i]);
            }
            ck_combat_tick(_handle, nativeFrames, nativeFrames.Length);
        }

        public int DrainEvents(List<CombatEvent> into)
        {
            if (!IsLoaded || into == null)
            {
                return 0;
            }
            int count = ck_combat_drain_events(_handle, _eventBuffer, _eventBuffer.Length);
            for (int i = 0; i < count; i++)
            {
                into.Add(ToManaged(_eventBuffer[i]));
            }
            return count;
        }

        public ActorSnapshot GetSnapshot(int actorId)
        {
            if (!IsLoaded)
            {
                return null;
            }
            var native = new CkActorSnapshot { CooldownBySlot = new int[SlotCount] };
            ck_combat_get_actor_snapshot(_handle, actorId, ref native);
            var snapshot = new ActorSnapshot
            {
                ActorId = native.ActorId,
                Faction = native.Faction,
                Health = native.Health,
                MaxHealth = native.MaxHealth,
                AttackPower = native.AttackPower,
                BaseDefense = native.BaseDefense,
                EffectiveDefense = native.EffectiveDefense,
                Blocking = native.Blocking != 0,
                Invulnerable = native.Invulnerable != 0,
                Busy = native.Busy != 0,
                AbilityIndex = native.AbilityIndex,
                Phase = native.Phase,
                AbilityFrame = native.AbilityFrame,
                ComboStep = native.ComboStep,
                ComboCount = native.ComboCount,
                ComboWindowRemaining = native.ComboWindowRemaining,
                CooldownBySlot = native.CooldownBySlot ?? new int[SlotCount],
                BuffActive = native.BuffActive != 0,
                BuffRemainingFrames = native.BuffRemainingFrames,
                BuffCount = native.BuffCount,
                TotalHits = native.TotalHits,
                LastAbilityIndex = native.LastAbilityIndex,
                LastDamage = native.LastDamage,
                LastEventType = (CombatEventType)native.LastEventType
            };
            return snapshot;
        }

        public uint TickIndex => IsLoaded ? ck_combat_tick_index(_handle) : 0u;

        public void Reset()
        {
            if (IsLoaded)
            {
                ck_combat_reset(_handle);
            }
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                try
                {
                    ck_combat_destroy(_handle);
                }
                catch (Exception)
                {
                    // Native teardown must never mask shutdown.
                }
                _handle = IntPtr.Zero;
            }
        }

        private static CkActorFrame ToNative(ActorFrameVolumes frame)
        {
            return new CkActorFrame
            {
                ActorId = frame.ActorId,
                Body = ToNative(frame.Body),
                Weapon = ToNative(frame.Weapon),
                WeaponActiveHint = frame.WeaponActiveHint
            };
        }

        private static CkCapsule ToNative(CapsuleVolume capsule)
        {
            return new CkCapsule
            {
                Ax = capsule.Ax,
                Ay = capsule.Ay,
                Az = capsule.Az,
                Bx = capsule.Bx,
                By = capsule.By,
                Bz = capsule.Bz,
                Radius = capsule.Radius
            };
        }

        private static CombatEvent ToManaged(CkEvent native)
        {
            return new CombatEvent
            {
                Tick = native.Tick,
                Type = (CombatEventType)native.Type,
                SourceActor = native.SourceActor,
                TargetActor = native.TargetActor,
                AbilityIndex = native.AbilityIndex,
                BuffIndex = native.BuffIndex,
                Overlap = native.Overlap != 0,
                Reason = (CombatRejectReason)native.Reason,
                DefenseBefore = native.DefenseBefore,
                DefenseAfter = native.DefenseAfter,
                HealthAfter = native.HealthAfter,
                Sequence = native.Sequence,
                Amount = native.Amount
            };
        }

        // ------------------------------------------------------------------
        // Native layout mirrors. Keep in sync with Native/include/ck/ck_combat.h
        // ------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        private struct CkGlobalConfig
        {
            public int TickRate;
            public float DefenseConstant;
            public float MinDefense;
            public int InputBufferFrames;
            public int DodgeInvulnFrames;
            public int DodgeCooldownFrames;
            public float DodgeDistance;
            public float BlockMoveMultiplier;
            public int ComboWindowFrames;
            public int EnemyIntervalFrames;
            public int MaxAbilities;
            public int MaxBuffs;
            public int MaxActors;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CkCommand
        {
            public int ActorId;
            public int Kind;
            public int AbilityIndex;
            public uint IssuedTick;
            public uint Sequence;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CkCapsule
        {
            public float Ax, Ay, Az;
            public float Bx, By, Bz;
            public float Radius;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CkActorFrame
        {
            public int ActorId;
            public CkCapsule Body;
            public CkCapsule Weapon;
            public int WeaponActiveHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CkEvent
        {
            public uint Tick;
            public int Type;
            public int SourceActor;
            public int TargetActor;
            public int AbilityIndex;
            public int BuffIndex;
            public int Overlap;
            public int Reason;
            public int DefenseBefore;
            public int DefenseAfter;
            public int HealthAfter;
            public int Sequence;
            public float Amount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CkActorSnapshot
        {
            public int ActorId;
            public int Faction;
            public float Health;
            public float MaxHealth;
            public float AttackPower;
            public float BaseDefense;
            public float EffectiveDefense;
            public int Blocking;
            public int Invulnerable;
            public int Busy;
            public int AbilityIndex;
            public int Phase;
            public int AbilityFrame;
            public int ComboStep;
            public int ComboCount;
            public int ComboWindowRemaining;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = SlotCount)]
            public int[] CooldownBySlot;
            public int BuffActive;
            public int BuffRemainingFrames;
            public int BuffCount;
            public int TotalHits;
            public int LastAbilityIndex;
            public float LastDamage;
            public int LastEventType;
        }

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ck_get_abi_version();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ck_get_build_info();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ck_get_last_error();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ck_combat_create(ref CkGlobalConfig config);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ck_combat_destroy(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int ck_combat_add_ability_ex(IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string id, int slot, float multiplier, int cooldownFrames,
            int hold, int onHitBuffIndex, int hasHitVolume, [In] int[] phaseTriples, int phaseCount);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int ck_combat_add_buff_ex(IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string id, int stat, float add, int durationFrames, int stackMode);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int ck_combat_add_actor_ex(IntPtr handle,
            [MarshalAs(UnmanagedType.LPStr)] string id, int faction, float maxHealth, float attackPower,
            float defense, float moveSpeed);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ck_combat_set_combo(IntPtr handle, [In] int[] abilityIndices, int count);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ck_combat_finalize(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int ck_combat_find_ability(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string id);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint ck_combat_config_hash(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ck_combat_actor_count(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ck_combat_submit_command(IntPtr handle, ref CkCommand command);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ck_combat_tick(IntPtr handle, [In] CkActorFrame[] frames, int frameCount);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ck_combat_drain_events(IntPtr handle, [Out] CkEvent[] events, int capacity);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ck_combat_get_actor_snapshot(IntPtr handle, int actorId, ref CkActorSnapshot snapshot);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint ck_combat_tick_index(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ck_combat_reset(IntPtr handle);
    }
}
