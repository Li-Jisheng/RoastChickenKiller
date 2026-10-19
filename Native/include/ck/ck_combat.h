/*
 * Chicken Killer - native combat core C ABI (phase 1).
 *
 * Ownership: Architect freezes this header; Native implements it.
 * The C++ core is authoritative for ability state, cooldowns, buffs,
 * hit queries and the damage formula. C# only submits configuration,
 * per-tick hit volumes and commands, then reads events/snapshots.
 *
 * Interop note: every struct here is blittable and contains only scalars or
 * fixed arrays of scalars. Configuration is passed through scalar builder
 * calls (no structs containing strings) so the same ABI works in the Unity
 * Editor (Mono) and IL2CPP players.
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

/* Ability slots. Structural identifiers, not gameplay values. */
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

/* Ability phase kinds. */
enum {
    CK_PHASE_WINDUP = 0,
    CK_PHASE_ACTIVE = 1,
    CK_PHASE_RECOVERY = 2
};

/* Command kinds submitted by the Unity host. */
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

/* Event types. */
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

/* Rejection reasons carried by CK_EV_REJECTED. */
enum {
    CK_REJECT_NONE = 0,
    CK_REJECT_COOLDOWN = 1,
    CK_REJECT_BUSY = 2,
    CK_REJECT_BLOCKING = 3,
    CK_REJECT_EXPIRED = 4,
    CK_REJECT_NO_ABILITY = 5,
    CK_REJECT_STATE = 6
};

/* Buff stat / stack modes. */
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
 * Configuration builders. `phaseTriples` is an array of phaseCount*3 int32
 * values laid out as {kind, startFrame, endFrame}. All return the new index or
 * -1 on failure (ck_get_last_error() has the reason).
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

/* Runtime. */
CK_API void ck_combat_submit_command(void* handle, const CkCommand* cmd);
CK_API void ck_combat_tick(void* handle, const CkActorFrame* frames, int32_t frameCount);
CK_API int32_t ck_combat_drain_events(void* handle, CkEvent* out, int32_t capacity);
CK_API void ck_combat_get_actor_snapshot(void* handle, int32_t actorId, CkActorSnapshot* out);
CK_API uint32_t ck_combat_tick_index(void* handle);
CK_API void ck_combat_reset(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* CK_COMBAT_H */
