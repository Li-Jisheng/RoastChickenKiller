using UnityEngine;
using CK.Domain.Combat;
using CK.Domain.Config;
using CK.Native;

namespace CK.Runtime.Combat
{
    /// <summary>
    /// 脚本敌人：没有 AI、不移动、不做决策。它只
    /// 每 Config.enemyAttack.intervalFrames tick 提交一次 EnemyAttack 命令。
    /// </summary>
    public sealed class ScriptedEnemyDriver : MonoBehaviour
    {
        private CombatDirector _director;
        private NativeCombatBridge _bridge;
        private AbilityConfig _ability;
        private int _abilityIndex = -1;
        private int _intervalFrames;
        private uint _nextAttackTick;
        private uint _sequence;
        private bool _initialized;

        public int AttacksIssued { get; private set; }
        public uint NextAttackTick => _nextAttackTick;
        public int IntervalFrames => _intervalFrames;
        public string AbilityId => _ability != null ? _ability.Id : "<none>";

        public bool Initialize(CombatDirector director, CombatConfig config, out string error)
        {
            _director = director;
            _bridge = director != null ? director.Bridge : null;
            error = string.Empty;
            if (_bridge == null || !_bridge.IsLoaded)
            {
                error = "enemy driver requires a loaded native bridge";
                return false;
            }
            if (!config.TryGetAbilityBySlot(AbilitySlot.EnemyAttack, out _ability))
            {
                error = "config has no EnemyAttack ability";
                return false;
            }
            _abilityIndex = config.FindAbilityIndex(_ability.Id);
            _intervalFrames = config.EnemyAttack.IntervalFrames;
            if (_intervalFrames <= 0)
            {
                error = "enemyAttack.intervalFrames must be > 0";
                return false;
            }
            _nextAttackTick = (uint)_intervalFrames;
            _sequence = 0;
            AttacksIssued = 0;
            _initialized = true;
            Debug.Log($"[CK] enemy driver ready: ability={_ability.Id} interval={_intervalFrames} firstTick={_nextAttackTick}");
            return true;
        }

        /// <summary>由 CombatDirector 在原生 tick 之前调用。</summary>
        public void SubmitIfDue(uint tick)
        {
            if (!_initialized || _bridge == null)
            {
                return;
            }
            if (tick < _nextAttackTick)
            {
                return;
            }
            _sequence++;
            _bridge.SubmitCommand(CombatDirector.EnemyActorId, CombatCommandKind.EnemyAttack, tick, _sequence);
            AttacksIssued++;
            Debug.Log($"[CK] enemy_swing command tick={tick} (#{AttacksIssued}, interval={_intervalFrames})");
            _nextAttackTick = tick + (uint)_intervalFrames;
        }

        public void ResetSchedule()
        {
            _nextAttackTick = (uint)_intervalFrames;
            _sequence = 0;
            AttacksIssued = 0;
            Debug.Log($"[CK] enemy driver schedule reset: nextTick={_nextAttackTick}");
        }
    }
}
