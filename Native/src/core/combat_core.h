#pragma once

#include "ck/ck_combat.h"

#include <array>
#include <cstdint>
#include <deque>
#include <string>
#include <vector>

namespace ck {

constexpr int kMaxPendingCommands = 4;
constexpr int kMaxSlotCount = CK_SLOT_COUNT;

struct Phase {
    int kind = CK_PHASE_WINDUP;
    int startFrame = 0;
    int endFrame = 0;
};

struct Ability {
    std::string id;
    int slot = CK_SLOT_PRIMARY;
    float multiplier = 0.0f;
    int cooldownFrames = 0;
    bool hold = false;
    int onHitBuff = -1;
    bool hasHitVolume = false;
    int phaseCount = 0;
    Phase phases[CK_MAX_PHASES] = {};
    int activeStart = -1;
    int activeEnd = -1;
    int endFrame = 0;
};

struct BuffDef {
    std::string id;
    int stat = CK_STAT_DEFENSE;
    float add = 0.0f;
    int durationFrames = 0;
    int stackMode = CK_STACK_REFRESH;
};

struct BuffInstance {
    int buffIndex = -1;
    int remainingFrames = 0;
};

struct PendingCommand {
    int kind = CK_CMD_NONE;
    int abilityIndex = -1;
    int actorId = 0;
    uint32_t issuedTick = 0;
    uint32_t sequence = 0;
};

struct Actor {
    int id = 0;
    int faction = 0;
    float maxHealth = 0.0f;
    float health = 0.0f;
    float attackPower = 0.0f;
    float baseDefense = 0.0f;
    float moveSpeed = 0.0f;

    int currentAbility = -1;
    int abilityFrame = 0;
    int phase = -1;
    bool busy = false;
    bool blocking = false;
    bool blockHeld = false;
    bool invulnerable = false;

    std::array<int, CK_MAX_ABILITIES> cooldown{};
    std::array<bool, CK_MAX_ACTORS> hitLatch{};
    std::vector<BuffInstance> buffs;

    int comboStep = 0;      /* number of completed primary attacks in the chain */
    int comboWindow = 0;    /* frames left to continue the chain */
    std::deque<PendingCommand> pending;

    uint32_t lastSequence = 0;
    int totalHits = 0;
    int lastAbilityIndex = -1;
    float lastDamage = 0.0f;
    int lastEventType = CK_EV_NONE;
};

struct Combat {
    CkGlobalConfig cfg{};
    bool hasConfig = false;
    bool finalized = false;

    std::vector<Ability> abilities;
    std::vector<BuffDef> buffs;
    std::vector<Actor> actors;
    std::array<int, kMaxSlotCount> slotAbility{};
    std::array<int, CK_MAX_COMBO_STEPS> comboSteps{};
    int comboCount = 0;

    uint32_t tickIndex = 0;
    uint32_t configHash = 0;

    std::deque<PendingCommand> commandQueue;
    std::array<CkEvent, CK_MAX_EVENTS> events{};
    int eventHead = 0;
    int eventCount = 0;
    int eventSequence = 0;

    std::string lastError;

    Combat();
};

/* Configuration building. Each returns an index or -1 with lastError set. */
int addAbility(Combat& c, const char* id, int slot, float multiplier, int cooldownFrames, bool hold,
               int onHitBuffIndex, bool hasHitVolume, const int32_t* phaseTriples, int phaseCount);
int addBuff(Combat& c, const char* id, int stat, float add, int durationFrames, int stackMode);
int addActor(Combat& c, const char* id, int faction, float maxHealth, float attackPower,
             float defense, float moveSpeed);
int setCombo(Combat& c, const int32_t* indices, int count);
bool finalize(Combat& c);
int findAbility(const Combat& c, const char* id);

/* Runtime. */
void submitCommand(Combat& c, const CkCommand& cmd);
void tick(Combat& c, const CkActorFrame* frames, int frameCount);
int drainEvents(Combat& c, CkEvent* out, int capacity);
void writeSnapshot(const Combat& c, int actorId, CkActorSnapshot* out);
void reset(Combat& c);
int effectiveDefense(const Combat& c, const Actor& a);
int computeDamage(float attackPower, float abilityMultiplier, int defense, float defenseConstant);

/* Geometry helpers (exposed for tests). */
double segmentSegmentDistanceSquared(const double p1[3], const double q1[3],
                                    const double p2[3], const double q2[3]);
bool capsuleOverlap(const CkCapsule& first, const CkCapsule& second);
bool capsuleIsFinite(const CkCapsule& capsule);

/* Internal helpers used by the API layer. */
void setError(Combat& c, const std::string& message);
void emitEvent(Combat& c, int type, int sourceActor, int targetActor, int abilityIndex,
               int buffIndex, int overlap, int reason, int defenseBefore, int defenseAfter,
               int healthAfter, float amount);
int phaseForFrame(const Ability& ability, int frame);
bool isActiveFrame(const Ability& ability, int frame);
int slotToCommandAbility(const Combat& c, int slot);

} // namespace ck
