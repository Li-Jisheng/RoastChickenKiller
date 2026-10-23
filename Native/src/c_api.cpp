#include "ck/ck_combat.h"

#include "core/combat_core.h"

#include <cstring>
#include <new>
#include <string>

namespace {

thread_local std::string g_lastError;

ck::Combat* toCombat(void* handle) {
    return static_cast<ck::Combat*>(handle);
}

void setGlobalError(const std::string& message) {
    g_lastError = message;
}

bool checkHandle(void* handle) {
    if (handle == nullptr) {
        setGlobalError("null combat handle");
        return false;
    }
    return true;
}

} // 命名空间

extern "C" {

int32_t ck_get_abi_version(void) {
    return CK_ABI_VERSION;
}

const char* ck_get_build_info(void) {
    return "ck_combat phase1 (C++20, ABI 1)";
}

const char* ck_get_last_error(void) {
    return g_lastError.c_str();
}

void* ck_combat_create(const CkGlobalConfig* cfg) {
    if (cfg == nullptr) {
        setGlobalError("config must not be null");
        return nullptr;
    }
    if (cfg->tickRate <= 0) {
        setGlobalError("tickRate must be > 0");
        return nullptr;
    }
    if (!(cfg->defenseConstant > 0.0f)) {
        setGlobalError("defenseConstant must be > 0");
        return nullptr;
    }
    if (cfg->maxActors > CK_MAX_ACTORS || cfg->maxAbilities > CK_MAX_ABILITIES ||
        cfg->maxBuffs > CK_MAX_BUFFS) {
        setGlobalError("requested capacity exceeds native limits");
        return nullptr;
    }
    ck::Combat* combat = new (std::nothrow) ck::Combat();
    if (combat == nullptr) {
        setGlobalError("out of memory creating combat");
        return nullptr;
    }
    combat->cfg = *cfg;
    combat->hasConfig = true;
    g_lastError.clear();
    return combat;
}

void ck_combat_destroy(void* handle) {
    delete toCombat(handle);
}

int32_t ck_combat_add_ability_ex(void* handle, const char* id, int32_t slot, float multiplier,
                                 int32_t cooldownFrames, int32_t hold, int32_t onHitBuffIndex,
                                 int32_t hasHitVolume, const int32_t* phaseTriples,
                                 int32_t phaseCount) {
    if (!checkHandle(handle)) {
        return -1;
    }
    ck::Combat* combat = toCombat(handle);
    const int index =
        ck::addAbility(*combat, id, slot, multiplier, cooldownFrames, hold != 0, onHitBuffIndex,
                       hasHitVolume != 0, phaseTriples, phaseCount);
    if (index < 0) {
        setGlobalError(combat->lastError);
    }
    return index;
}

int32_t ck_combat_add_buff_ex(void* handle, const char* id, int32_t stat, float add,
                              int32_t durationFrames, int32_t stackMode) {
    if (!checkHandle(handle)) {
        return -1;
    }
    ck::Combat* combat = toCombat(handle);
    const int index = ck::addBuff(*combat, id, stat, add, durationFrames, stackMode);
    if (index < 0) {
        setGlobalError(combat->lastError);
    }
    return index;
}

int32_t ck_combat_add_actor_ex(void* handle, const char* id, int32_t faction, float maxHealth,
                               float attackPower, float defense, float moveSpeed) {
    if (!checkHandle(handle)) {
        return -1;
    }
    ck::Combat* combat = toCombat(handle);
    const int index =
        ck::addActor(*combat, id, faction, maxHealth, attackPower, defense, moveSpeed);
    if (index < 0) {
        setGlobalError(combat->lastError);
    }
    return index;
}

int32_t ck_combat_set_combo(void* handle, const int32_t* abilityIndices, int32_t count) {
    if (!checkHandle(handle)) {
        return -1;
    }
    ck::Combat* combat = toCombat(handle);
    const int result = ck::setCombo(*combat, abilityIndices, count);
    if (result < 0) {
        setGlobalError(combat->lastError);
    }
    return result;
}

int32_t ck_combat_finalize(void* handle) {
    if (!checkHandle(handle)) {
        return 0;
    }
    ck::Combat* combat = toCombat(handle);
    if (!ck::finalize(*combat)) {
        setGlobalError(combat->lastError);
        return 0;
    }
    return 1;
}

int32_t ck_combat_find_ability(void* handle, const char* id) {
    if (!checkHandle(handle)) {
        return -1;
    }
    return ck::findAbility(*toCombat(handle), id);
}

uint32_t ck_combat_config_hash(void* handle) {
    if (!checkHandle(handle)) {
        return 0;
    }
    return toCombat(handle)->configHash;
}

int32_t ck_combat_actor_count(void* handle) {
    if (!checkHandle(handle)) {
        return 0;
    }
    return static_cast<int32_t>(toCombat(handle)->actors.size());
}

void ck_combat_submit_command(void* handle, const CkCommand* cmd) {
    if (!checkHandle(handle) || cmd == nullptr) {
        return;
    }
    ck::submitCommand(*toCombat(handle), *cmd);
}

void ck_combat_tick(void* handle, const CkActorFrame* frames, int32_t frameCount) {
    if (!checkHandle(handle)) {
        return;
    }
    ck::tick(*toCombat(handle), frames, frameCount);
}

int32_t ck_combat_drain_events(void* handle, CkEvent* out, int32_t capacity) {
    if (!checkHandle(handle)) {
        return 0;
    }
    return ck::drainEvents(*toCombat(handle), out, capacity);
}

void ck_combat_get_actor_snapshot(void* handle, int32_t actorId, CkActorSnapshot* out) {
    if (!checkHandle(handle) || out == nullptr) {
        return;
    }
    ck::writeSnapshot(*toCombat(handle), actorId, out);
}

uint32_t ck_combat_tick_index(void* handle) {
    if (!checkHandle(handle)) {
        return 0;
    }
    return toCombat(handle)->tickIndex;
}

void ck_combat_reset(void* handle) {
    if (!checkHandle(handle)) {
        return;
    }
    ck::reset(*toCombat(handle));
}

} // extern "C" 导出
