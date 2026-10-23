/*
 * Chicken Killer - 原生战斗核心 C ABI（阶段一）。
 *
 * 所有权：架构师冻结本头文件，Native 侧实现。
 * C++ 核心是技能状态、冷却、Buff、命中查询与伤害公式的权威；
 * C# 只提交配置、每 tick 的命中体积和命令，然后读取事件/快照。
 *
 * 互操作说明：这里所有结构体都是 blittable 的，只包含标量或标量定长数组。
 * 配置通过标量 builder 调用传入（不使用含字符串的结构体），因此同一套 ABI
 * 可同时用于 Unity Editor（Mono）与 IL2CPP Player。
 */
#ifndef CK_COMBAT_H
#define CK_COMBAT_H

#include <stdint.h>

#if defined(_WIN32)
#  if defined(CK_STATIC)
#    define CK_API
#  elif defined(CK_EXPORTS)
#    define CK_API __declspec(dllexport)
#  else
#    define CK_API __declspec(dllimport)
#  endif
#else
#  define CK_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define CK_ABI_VERSION 1
#define CK_MAX_ABILITIES 64
#define CK_MAX_BUFFS 32
#define CK_MAX_ACTORS 8
#define CK_MAX_PHASES 4
#define CK_MAX_COMBO_STEPS 16
#define CK_MAX_ID 64
#define CK_MAX_EVENTS 512

/* 技能槽位。结构性标识，不是玩法数值。 */
enum {
    CK_SLOT_PRIMARY = 0,
    CK_SLOT_HEAVY = 1,
    CK_SLOT_BLOCK = 2,
    CK_SLOT_DODGE = 3,
    CK_SLOT_SKILL1 = 4,
    CK_SLOT_SKILL2 = 5,
    CK_SLOT_SKILL3 = 6,
    CK_SLOT_SKILL4 = 7,
    CK_SLOT_ENEMY_ATTACK = 8,
    CK_SLOT_COUNT = 9
};

/* 技能阶段类型。 */
enum {
    CK_PHASE_WINDUP = 0,
    CK_PHASE_ACTIVE = 1,
    CK_PHASE_RECOVERY = 2
};

/* Unity 主机提交的命令类型。 */
enum {
    CK_CMD_NONE = 0,
    CK_CMD_PRIMARY = 1,
    CK_CMD_HEAVY = 2,
    CK_CMD_BLOCK_PRESS = 3,
    CK_CMD_BLOCK_RELEASE = 4,
    CK_CMD_DODGE = 5,
    CK_CMD_SKILL1 = 6,
    CK_CMD_SKILL2 = 7,
    CK_CMD_SKILL3 = 8,
    CK_CMD_SKILL4 = 9,
    CK_CMD_ENEMY_ATTACK = 10,
    CK_CMD_ACTIVATE_ABILITY = 11
};

/* 事件类型。 */
enum {
    CK_EV_NONE = 0,
    CK_EV_HIT = 1,
    CK_EV_BLOCKED = 2,
    CK_EV_DODGED = 3,
    CK_EV_BUFF_APPLIED = 4,
    CK_EV_ABILITY_STARTED = 5,
    CK_EV_ABILITY_ENDED = 6,
    CK_EV_REJECTED = 7,
    CK_EV_BUFF_EXPIRED = 8,
    CK_EV_CONTACT = 9,
    CK_EV_BLOCK_STARTED = 10,
    CK_EV_BLOCK_ENDED = 11,
    CK_EV_DODGE_STARTED = 12
};

/* CK_EV_REJECTED 携带的拒绝原因。 */
enum {
    CK_REJECT_NONE = 0,
    CK_REJECT_COOLDOWN = 1,
    CK_REJECT_BUSY = 2,
    CK_REJECT_BLOCKING = 3,
    CK_REJECT_EXPIRED = 4,
    CK_REJECT_NO_ABILITY = 5,
    CK_REJECT_STATE = 6
};

/* Buff 属性/叠加模式。 */
enum { CK_STAT_DEFENSE = 0 };
enum { CK_STACK_REFRESH = 0 };

typedef struct CkGlobalConfig {
    int32_t tickRate;
    float defenseConstant;
    float minDefense;
    int32_t inputBufferFrames;
    int32_t dodgeInvulnFrames;
    int32_t dodgeCooldownFrames;
    float dodgeDistance;
    float blockMoveMultiplier;
    int32_t comboWindowFrames;
    int32_t enemyIntervalFrames;
    int32_t maxAbilities;
    int32_t maxBuffs;
    int32_t maxActors;
} CkGlobalConfig;

typedef struct CkCommand {
    int32_t actorId;
    int32_t kind;
    int32_t abilityIndex;
    uint32_t issuedTick;
    uint32_t sequence;
} CkCommand;

typedef struct CkCapsule {
    float ax, ay, az;
    float bx, by, bz;
    float radius;
} CkCapsule;

typedef struct CkActorFrame {
    int32_t actorId;
    CkCapsule body;
    CkCapsule weapon;
    int32_t weaponActiveHint;
} CkActorFrame;

typedef struct CkEvent {
    uint32_t tick;
    int32_t type;
    int32_t sourceActor;
    int32_t targetActor;
    int32_t abilityIndex;
    int32_t buffIndex;
    int32_t overlap;
    int32_t reason;
    int32_t defenseBefore;
    int32_t defenseAfter;
    int32_t healthAfter;
    int32_t sequence;
    float amount;
} CkEvent;

typedef struct CkActorSnapshot {
    int32_t actorId;
    int32_t faction;
    float health;
    float maxHealth;
    float attackPower;
    float baseDefense;
    float effectiveDefense;
    int32_t blocking;
    int32_t invulnerable;
    int32_t busy;
    int32_t abilityIndex;
    int32_t phase;
    int32_t abilityFrame;
    int32_t comboStep;
    int32_t comboCount;
    int32_t comboWindowRemaining;
    int32_t cooldownBySlot[CK_SLOT_COUNT];
    int32_t buffActive;
    int32_t buffRemainingFrames;
    int32_t buffCount;
    int32_t totalHits;
    int32_t lastAbilityIndex;
    float lastDamage;
    int32_t lastEventType;
} CkActorSnapshot;

CK_API int32_t ck_get_abi_version(void);
CK_API const char* ck_get_build_info(void);
CK_API const char* ck_get_last_error(void);

CK_API void* ck_combat_create(const CkGlobalConfig* cfg);
CK_API void ck_combat_destroy(void* handle);

/*
 * 配置构建函数。`phaseTriples` 是一个 phaseCount*3 的 int32 数组，
 * 按 {kind, startFrame, endFrame} 排列。成功返回新索引，失败返回 -1
 * （原因见 ck_get_last_error()）。
 */
CK_API int32_t ck_combat_add_ability_ex(void* handle, const char* id, int32_t slot,
                                        float multiplier, int32_t cooldownFrames, int32_t hold,
                                        int32_t onHitBuffIndex, int32_t hasHitVolume,
                                        const int32_t* phaseTriples, int32_t phaseCount);
CK_API int32_t ck_combat_add_buff_ex(void* handle, const char* id, int32_t stat, float add,
                                     int32_t durationFrames, int32_t stackMode);
CK_API int32_t ck_combat_add_actor_ex(void* handle, const char* id, int32_t faction,
                                      float maxHealth, float attackPower, float defense,
                                      float moveSpeed);
CK_API int32_t ck_combat_set_combo(void* handle, const int32_t* abilityIndices, int32_t count);
CK_API int32_t ck_combat_finalize(void* handle);
CK_API int32_t ck_combat_find_ability(void* handle, const char* id);
CK_API uint32_t ck_combat_config_hash(void* handle);
CK_API int32_t ck_combat_actor_count(void* handle);

/* 运行时。 */
CK_API void ck_combat_submit_command(void* handle, const CkCommand* cmd);
CK_API void ck_combat_tick(void* handle, const CkActorFrame* frames, int32_t frameCount);
CK_API int32_t ck_combat_drain_events(void* handle, CkEvent* out, int32_t capacity);
CK_API void ck_combat_get_actor_snapshot(void* handle, int32_t actorId, CkActorSnapshot* out);
CK_API uint32_t ck_combat_tick_index(void* handle);
CK_API void ck_combat_reset(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* CK_COMBAT_H 头文件保护 */
