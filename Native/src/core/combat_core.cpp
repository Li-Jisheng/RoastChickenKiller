#include "core/combat_core.h"

#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>

namespace ck {
namespace {

constexpr double kEpsilon = 1e-12;

double dot3(const double a[3], const double b[3]) {
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}

double clamp01(double v) {
    return v < 0.0 ? 0.0 : (v > 1.0 ? 1.0 : v);
}

bool isFiniteFloat(float v) {
    return std::isfinite(static_cast<double>(v));
}

Actor* findActor(Combat& c, int actorId) {
    if (actorId < 0 || actorId >= static_cast<int>(c.actors.size())) {
        return nullptr;
    }
    Actor& candidate = c.actors[static_cast<size_t>(actorId)];
    if (candidate.id != actorId) {
        return nullptr;
    }
    return &candidate;
}

const Actor* findActor(const Combat& c, int actorId) {
    if (actorId < 0 || actorId >= static_cast<int>(c.actors.size())) {
        return nullptr;
    }
    const Actor& candidate = c.actors[static_cast<size_t>(actorId)];
    if (candidate.id != actorId) {
        return nullptr;
    }
    return &candidate;
}

int actorIndex(const Combat& c, int actorId) {
    for (size_t i = 0; i < c.actors.size(); ++i) {
        if (c.actors[i].id == actorId) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

void pushPending(Actor& actor, const PendingCommand& command) {
    if (static_cast<int>(actor.pending.size()) >= kMaxPendingCommands) {
        actor.pending.pop_front();
    }
    actor.pending.push_back(command);
}

void applyBuff(Combat& c, Actor& target, int buffIndex) {
    if (buffIndex < 0 || buffIndex >= static_cast<int>(c.buffs.size())) {
        return;
    }
    const BuffDef& definition = c.buffs[static_cast<size_t>(buffIndex)];
    const int before = effectiveDefense(c, target);

    for (BuffInstance& instance : target.buffs) {
        if (instance.buffIndex == buffIndex && definition.stackMode == CK_STACK_REFRESH) {
            instance.remainingFrames = definition.durationFrames;
            emitEvent(c, CK_EV_BUFF_APPLIED, -1, target.id, -1, buffIndex, 0, 1, before,
                      effectiveDefense(c, target), static_cast<int>(target.health),
                      static_cast<float>(definition.durationFrames));
            return;
        }
    }

    BuffInstance instance;
    instance.buffIndex = buffIndex;
    instance.remainingFrames = definition.durationFrames;
    target.buffs.push_back(instance);
    emitEvent(c, CK_EV_BUFF_APPLIED, -1, target.id, -1, buffIndex, 0, 0, before,
              effectiveDefense(c, target), static_cast<int>(target.health),
              static_cast<float>(definition.durationFrames));
}

void finishAbility(Combat& c, Actor& actor) {
    if (actor.currentAbility < 0 || actor.currentAbility >= static_cast<int>(c.abilities.size())) {
        actor.busy = false;
        actor.currentAbility = -1;
        actor.phase = -1;
        actor.invulnerable = false;
        return;
    }
    const Ability& ability = c.abilities[static_cast<size_t>(actor.currentAbility)];
    const int endedAbility = actor.currentAbility;
    emitEvent(c, CK_EV_ABILITY_ENDED, actor.id, -1, endedAbility, -1, 0, 0, 0, 0, 0, 0.0f);
    actor.busy = false;
    actor.currentAbility = -1;
    actor.phase = -1;
    actor.abilityFrame = 0;
    actor.invulnerable = false;
    if (ability.slot == CK_SLOT_PRIMARY) {
        actor.comboWindow = c.cfg.comboWindowFrames;
    }
}

bool activateAbility(Combat& c, Actor& actor, int abilityIndex, uint32_t tick, bool isPrimary,
                     int comboStepUsed) {
    if (abilityIndex < 0 || abilityIndex >= static_cast<int>(c.abilities.size())) {
        emitEvent(c, CK_EV_REJECTED, actor.id, -1, abilityIndex, -1, 0, CK_REJECT_NO_ABILITY, 0, 0,
                  0, 0.0f);
        return false;
    }
    const Ability& ability = c.abilities[static_cast<size_t>(abilityIndex)];
    if (actor.blocking) {
        emitEvent(c, CK_EV_REJECTED, actor.id, -1, abilityIndex, -1, 0, CK_REJECT_BLOCKING, 0, 0,
                  0, 0.0f);
        return false;
    }
    if (actor.cooldown[static_cast<size_t>(abilityIndex)] > 0) {
        emitEvent(c, CK_EV_REJECTED, actor.id, -1, abilityIndex, -1, 0, CK_REJECT_COOLDOWN, 0, 0,
                  0, static_cast<float>(actor.cooldown[static_cast<size_t>(abilityIndex)]));
        return false;
    }

    actor.currentAbility = abilityIndex;
    actor.abilityFrame = 0;
    actor.phase = phaseForFrame(ability, 0);
    actor.busy = true;
    actor.lastAbilityIndex = abilityIndex;
    actor.hitLatch.fill(false);
    actor.comboWindow = 0;
    actor.invulnerable =
        (ability.slot == CK_SLOT_DODGE) && (c.cfg.dodgeInvulnFrames > 0);
    if (ability.cooldownFrames > 0) {
        actor.cooldown[static_cast<size_t>(abilityIndex)] = ability.cooldownFrames;
    }
    emitEvent(c, CK_EV_ABILITY_STARTED, actor.id, -1, abilityIndex, -1, 0, 0, 0, 0, 0, 0.0f);
    if (ability.slot == CK_SLOT_DODGE) {
        emitEvent(c, CK_EV_DODGE_STARTED, actor.id, -1, abilityIndex, -1, 0, 0, 0, 0, 0, 0.0f);
    }
    if (isPrimary) {
        actor.comboStep = comboStepUsed + 1;
    }
    (void)tick;
    return true;
}

bool dispatchAction(Combat& c, Actor& actor, const PendingCommand& command, uint32_t tick,
                    bool fromPending) {
    if (command.kind == CK_CMD_PRIMARY) {
        int step = actor.comboStep;
        if (actor.comboWindow <= 0) {
            step = 0;
        }
        if (c.comboCount <= 0) {
            emitEvent(c, CK_EV_REJECTED, actor.id, -1, -1, -1, 0, CK_REJECT_NO_ABILITY, 0, 0, 0,
                      0.0f);
            return false;
        }
        const int wrapped = step % c.comboCount;
        const int abilityIndex = c.comboSteps[static_cast<size_t>(wrapped)];
        if (actor.busy) {
            if (fromPending) {
                emitEvent(c, CK_EV_REJECTED, actor.id, -1, abilityIndex, -1, 0, CK_REJECT_BUSY, 0, 0,
                          0, 0.0f);
                return false;
            }
            pushPending(actor, command);
            return false;
        }
        return activateAbility(c, actor, abilityIndex, tick, true, wrapped);
    }

    if (command.kind == CK_CMD_BLOCK_PRESS) {
        actor.blockHeld = true;
        if (!actor.busy && !actor.blocking) {
            actor.blocking = true;
            actor.blockHeld = true;
            emitEvent(c, CK_EV_BLOCK_STARTED, actor.id, -1, -1, -1, 0, 0, 0, 0, 0, 0.0f);
        }
        return false;
    }
    if (command.kind == CK_CMD_BLOCK_RELEASE) {
        actor.blockHeld = false;
        if (actor.blocking) {
            actor.blocking = false;
            emitEvent(c, CK_EV_BLOCK_ENDED, actor.id, -1, -1, -1, 0, 0, 0, 0, 0, 0.0f);
        }
        return false;
    }

    int abilityIndex = -1;
    switch (command.kind) {
    case CK_CMD_HEAVY: abilityIndex = c.slotAbility[CK_SLOT_HEAVY]; break;
    case CK_CMD_DODGE: abilityIndex = c.slotAbility[CK_SLOT_DODGE]; break;
    case CK_CMD_SKILL1: abilityIndex = c.slotAbility[CK_SLOT_SKILL1]; break;
    case CK_CMD_SKILL2: abilityIndex = c.slotAbility[CK_SLOT_SKILL2]; break;
    case CK_CMD_SKILL3: abilityIndex = c.slotAbility[CK_SLOT_SKILL3]; break;
    case CK_CMD_SKILL4: abilityIndex = c.slotAbility[CK_SLOT_SKILL4]; break;
    case CK_CMD_ENEMY_ATTACK: abilityIndex = c.slotAbility[CK_SLOT_ENEMY_ATTACK]; break;
    case CK_CMD_ACTIVATE_ABILITY: abilityIndex = command.abilityIndex; break;
    default: break;
    }

    if (abilityIndex < 0) {
        emitEvent(c, CK_EV_REJECTED, actor.id, -1, -1, -1, 0, CK_REJECT_NO_ABILITY, 0, 0, 0, 0.0f);
        return false;
    }
    if (actor.busy) {
        if (fromPending) {
            emitEvent(c, CK_EV_REJECTED, actor.id, -1, abilityIndex, -1, 0, CK_REJECT_BUSY, 0, 0, 0,
                      0.0f);
            return false;
        }
        pushPending(actor, command);
        return false;
    }
    return activateAbility(c, actor, abilityIndex, tick, false, 0);
}

void handleCommand(Combat& c, const PendingCommand& command, uint32_t tick) {
    Actor* actor = findActor(c, command.actorId);
    if (actor == nullptr) {
        return;
    }
    if (command.sequence != 0) {
        if (command.sequence <= actor->lastSequence) {
            return; /* duplicate or stale command */
        }
        actor->lastSequence = command.sequence;
    }
    if (c.cfg.inputBufferFrames >= 0 &&
        tick > command.issuedTick + static_cast<uint32_t>(c.cfg.inputBufferFrames)) {
        emitEvent(c, CK_EV_REJECTED, actor->id, -1, -1, -1, 0, CK_REJECT_EXPIRED, 0, 0, 0, 0.0f);
        return;
    }
    dispatchAction(c, *actor, command, tick, false);
}

void processPending(Combat& c, Actor& actor, uint32_t tick) {
    while (!actor.busy && !actor.pending.empty()) {
        PendingCommand command = actor.pending.front();
        actor.pending.pop_front();
        if (c.cfg.inputBufferFrames >= 0 &&
            tick > command.issuedTick + static_cast<uint32_t>(c.cfg.inputBufferFrames)) {
            emitEvent(c, CK_EV_REJECTED, actor.id, -1, -1, -1, 0, CK_REJECT_EXPIRED, 0, 0, 0, 0.0f);
            continue;
        }
        if (!dispatchAction(c, actor, command, tick, true)) {
            if (actor.busy) {
                break;
            }
        }
    }
}

void resolveHits(Combat& c, const CkActorFrame* frames, int frameCount, uint32_t tick) {
    if (frames == nullptr) {
        return;
    }
    for (size_t ai = 0; ai < c.actors.size(); ++ai) {
        Actor& attacker = c.actors[ai];
        if (!attacker.busy || attacker.currentAbility < 0) {
            continue;
        }
        const Ability& ability = c.abilities[static_cast<size_t>(attacker.currentAbility)];
        if (!ability.hasHitVolume || !isActiveFrame(ability, attacker.abilityFrame)) {
            continue;
        }

        const CkActorFrame* attackerFrame = nullptr;
        for (int fi = 0; fi < frameCount; ++fi) {
            if (frames[fi].actorId == attacker.id) {
                attackerFrame = &frames[fi];
                break;
            }
        }
        if (attackerFrame == nullptr || !capsuleIsFinite(attackerFrame->weapon)) {
            continue;
        }

        for (size_t di = 0; di < c.actors.size(); ++di) {
            Actor& defender = c.actors[di];
            if (defender.id == attacker.id || defender.faction == attacker.faction) {
                continue;
            }
            if (attacker.hitLatch[di]) {
                continue;
            }
            const CkActorFrame* defenderFrame = nullptr;
            for (int fi = 0; fi < frameCount; ++fi) {
                if (frames[fi].actorId == defender.id) {
                    defenderFrame = &frames[fi];
                    break;
                }
            }
            if (defenderFrame == nullptr || !capsuleIsFinite(defenderFrame->body)) {
                continue;
            }
            if (!capsuleOverlap(attackerFrame->weapon, defenderFrame->body)) {
                continue;
            }

            attacker.hitLatch[di] = true;
            const int defenseBefore = effectiveDefense(c, defender);
            emitEvent(c, CK_EV_CONTACT, attacker.id, defender.id, attacker.currentAbility, -1, 1, 0,
                      defenseBefore, defenseBefore, static_cast<int>(defender.health), 0.0f);

            if (defender.blocking) {
                emitEvent(c, CK_EV_BLOCKED, attacker.id, defender.id, attacker.currentAbility, -1, 1,
                          0, defenseBefore, defenseBefore, static_cast<int>(defender.health), 0.0f);
                attacker.lastEventType = CK_EV_BLOCKED;
                attacker.lastDamage = 0.0f;
                continue;
            }
            if (defender.invulnerable) {
                emitEvent(c, CK_EV_DODGED, attacker.id, defender.id, attacker.currentAbility, -1, 1,
                          0, defenseBefore, defenseBefore, static_cast<int>(defender.health), 0.0f);
                attacker.lastEventType = CK_EV_DODGED;
                attacker.lastDamage = 0.0f;
                continue;
            }

            const int damage = computeDamage(attacker.attackPower, ability.multiplier, defenseBefore,
                                             c.cfg.defenseConstant);
            defender.health = std::max(0.0f, defender.health - static_cast<float>(damage));
            attacker.totalHits += 1;
            attacker.lastEventType = CK_EV_HIT;
            attacker.lastDamage = static_cast<float>(damage);
            emitEvent(c, CK_EV_HIT, attacker.id, defender.id, attacker.currentAbility, -1, 1, 0,
                      defenseBefore, effectiveDefense(c, defender),
                      static_cast<int>(defender.health), static_cast<float>(damage));

            if (ability.onHitBuff >= 0) {
                applyBuff(c, defender, ability.onHitBuff);
            }
        }
    }
    (void)tick;
}

void advanceActor(Combat& c, Actor& actor, uint32_t tick) {
    if (actor.busy && actor.currentAbility >= 0) {
        const Ability& ability = c.abilities[static_cast<size_t>(actor.currentAbility)];
        actor.abilityFrame += 1;
        if (actor.abilityFrame > ability.endFrame) {
            finishAbility(c, actor);
        } else {
            actor.phase = phaseForFrame(ability, actor.abilityFrame);
            actor.invulnerable =
                (ability.slot == CK_SLOT_DODGE) && (actor.abilityFrame < c.cfg.dodgeInvulnFrames);
        }
    }

    if (actor.comboWindow > 0) {
        actor.comboWindow -= 1;
        if (actor.comboWindow == 0) {
            actor.comboStep = 0;
        }
    }

    for (int& cooldown : actor.cooldown) {
        if (cooldown > 0) {
            cooldown -= 1;
        }
    }

    for (auto it = actor.buffs.begin(); it != actor.buffs.end();) {
        it->remainingFrames -= 1;
        if (it->remainingFrames <= 0) {
            const int expiredBuff = it->buffIndex;
            it = actor.buffs.erase(it);
            emitEvent(c, CK_EV_BUFF_EXPIRED, -1, actor.id, -1, expiredBuff, 0, 0,
                      effectiveDefense(c, actor), effectiveDefense(c, actor),
                      static_cast<int>(actor.health), 0.0f);
        } else {
            ++it;
        }
    }
    (void)tick;
}

} // namespace

Combat::Combat() {
    slotAbility.fill(-1);
    comboSteps.fill(-1);
}

void setError(Combat& c, const std::string& message) {
    c.lastError = message;
}

int phaseForFrame(const Ability& ability, int frame) {
    for (int i = 0; i < ability.phaseCount; ++i) {
        if (frame >= ability.phases[i].startFrame && frame <= ability.phases[i].endFrame) {
            return ability.phases[i].kind;
        }
    }
    return -1;
}

bool isActiveFrame(const Ability& ability, int frame) {
    if (ability.activeStart < 0) {
        return false;
    }
    return frame >= ability.activeStart && frame <= ability.activeEnd;
}

int effectiveDefense(const Combat& c, const Actor& actor) {
    double value = static_cast<double>(actor.baseDefense);
    for (const BuffInstance& instance : actor.buffs) {
        if (instance.buffIndex < 0 || instance.buffIndex >= static_cast<int>(c.buffs.size())) {
            continue;
        }
        const BuffDef& definition = c.buffs[static_cast<size_t>(instance.buffIndex)];
        if (definition.stat == CK_STAT_DEFENSE) {
            value += static_cast<double>(definition.add);
        }
    }
    if (value < static_cast<double>(c.cfg.minDefense)) {
        value = static_cast<double>(c.cfg.minDefense);
    }
    return static_cast<int>(std::llround(value));
}

int computeDamage(float attackPower, float abilityMultiplier, int defense,
                  float defenseConstant) {
    const double denominator = static_cast<double>(defense) + static_cast<double>(defenseConstant);
    if (!(denominator > 0.0)) {
        return 0;
    }
    const double raw = static_cast<double>(attackPower) * static_cast<double>(abilityMultiplier);
    const double value = raw * static_cast<double>(defenseConstant) / denominator;
    if (!std::isfinite(value) || value <= 0.0) {
        return 0;
    }
    return static_cast<int>(std::llround(value));
}

bool capsuleIsFinite(const CkCapsule& capsule) {
    return isFiniteFloat(capsule.ax) && isFiniteFloat(capsule.ay) && isFiniteFloat(capsule.az) &&
           isFiniteFloat(capsule.bx) && isFiniteFloat(capsule.by) && isFiniteFloat(capsule.bz) &&
           isFiniteFloat(capsule.radius) && capsule.radius > 0.0f;
}

double segmentSegmentDistanceSquared(const double p1[3], const double q1[3], const double p2[3],
                                    const double q2[3]) {
    double d1[3] = {q1[0] - p1[0], q1[1] - p1[1], q1[2] - p1[2]};
    double d2[3] = {q2[0] - p2[0], q2[1] - p2[1], q2[2] - p2[2]};
    double r[3] = {p1[0] - p2[0], p1[1] - p2[1], p1[2] - p2[2]};

    const double a = dot3(d1, d1);
    const double e = dot3(d2, d2);
    const double f = dot3(d2, r);
    double s = 0.0;
    double t = 0.0;

    if (a <= kEpsilon && e <= kEpsilon) {
        return dot3(r, r);
    }
    if (a <= kEpsilon) {
        s = 0.0;
        t = clamp01(f / e);
    } else {
        const double c = dot3(d1, r);
        if (e <= kEpsilon) {
            t = 0.0;
            s = clamp01(-c / a);
        } else {
            const double b = dot3(d1, d2);
            const double denominator = a * e - b * b;
            if (std::fabs(denominator) > kEpsilon) {
                s = clamp01((b * f - c * e) / denominator);
            } else {
                s = 0.0;
            }
            t = (b * s + f) / e;
            if (t < 0.0) {
                t = 0.0;
                s = clamp01(-c / a);
            } else if (t > 1.0) {
                t = 1.0;
                s = clamp01((b - c) / a);
            }
        }
    }

    double closest1[3] = {p1[0] + d1[0] * s, p1[1] + d1[1] * s, p1[2] + d1[2] * s};
    double closest2[3] = {p2[0] + d2[0] * t, p2[1] + d2[1] * t, p2[2] + d2[2] * t};
    double diff[3] = {closest1[0] - closest2[0], closest1[1] - closest2[1],
                      closest1[2] - closest2[2]};
    return dot3(diff, diff);
}

bool capsuleOverlap(const CkCapsule& first, const CkCapsule& second) {
    if (!capsuleIsFinite(first) || !capsuleIsFinite(second)) {
        return false;
    }
    const double p1[3] = {first.ax, first.ay, first.az};
    const double q1[3] = {first.bx, first.by, first.bz};
    const double p2[3] = {second.ax, second.ay, second.az};
    const double q2[3] = {second.bx, second.by, second.bz};
    const double distanceSquared = segmentSegmentDistanceSquared(p1, q1, p2, q2);
    const double radiusSum = static_cast<double>(first.radius) + static_cast<double>(second.radius);
    return distanceSquared <= radiusSum * radiusSum;
}

int addAbility(Combat& c, const char* id, int slot, float multiplier, int cooldownFrames, bool hold,
               int onHitBuffIndex, bool hasHitVolume, const int32_t* phaseTriples, int phaseCount) {
    if (c.finalized) {
        setError(c, "cannot add ability after finalize");
        return -1;
    }
    if (id == nullptr || id[0] == '\0') {
        setError(c, "ability id must not be empty");
        return -1;
    }
    const int maxAbilities = (c.cfg.maxAbilities > 0 && c.cfg.maxAbilities <= CK_MAX_ABILITIES)
                                 ? c.cfg.maxAbilities
                                 : CK_MAX_ABILITIES;
    if (static_cast<int>(c.abilities.size()) >= maxAbilities) {
        setError(c, "ability capacity exceeded");
        return -1;
    }
    if (slot < 0 || slot >= CK_SLOT_COUNT) {
        setError(c, "ability slot out of range: " + std::to_string(slot));
        return -1;
    }
    if (phaseCount < 0 || phaseCount > CK_MAX_PHASES) {
        setError(c, "ability phaseCount out of range");
        return -1;
    }
    if (!(multiplier >= 0.0f) || !isFiniteFloat(multiplier)) {
        setError(c, "ability multiplier must be >= 0");
        return -1;
    }
    if (cooldownFrames < 0) {
        setError(c, "ability cooldownFrames must be >= 0");
        return -1;
    }
    if (onHitBuffIndex < -1 || onHitBuffIndex >= static_cast<int>(c.buffs.size())) {
        setError(c, "ability onHitBuffIndex is invalid");
        return -1;
    }
    if (phaseCount > 0 && phaseTriples == nullptr) {
        setError(c, "ability phase data must not be null");
        return -1;
    }
    Ability ability;
    ability.id = id;
    ability.slot = slot;
    ability.multiplier = multiplier;
    ability.cooldownFrames = cooldownFrames;
    ability.hold = hold;
    ability.onHitBuff = onHitBuffIndex;
    ability.hasHitVolume = hasHitVolume;
    ability.phaseCount = phaseCount;
    for (int i = 0; i < phaseCount; ++i) {
        ability.phases[i].kind = phaseTriples[i * 3];
        ability.phases[i].startFrame = phaseTriples[i * 3 + 1];
        ability.phases[i].endFrame = phaseTriples[i * 3 + 2];
    }
    c.abilities.push_back(ability);
    return static_cast<int>(c.abilities.size()) - 1;
}

int addBuff(Combat& c, const char* id, int stat, float add, int durationFrames, int stackMode) {
    if (c.finalized) {
        setError(c, "cannot add buff after finalize");
        return -1;
    }
    if (id == nullptr || id[0] == '\0') {
        setError(c, "buff id must not be empty");
        return -1;
    }
    const int maxBuffs =
        (c.cfg.maxBuffs > 0 && c.cfg.maxBuffs <= CK_MAX_BUFFS) ? c.cfg.maxBuffs : CK_MAX_BUFFS;
    if (static_cast<int>(c.buffs.size()) >= maxBuffs) {
        setError(c, "buff capacity exceeded");
        return -1;
    }
    if (durationFrames <= 0) {
        setError(c, "buff durationFrames must be > 0");
        return -1;
    }
    if (stat != CK_STAT_DEFENSE) {
        setError(c, "unsupported buff stat");
        return -1;
    }
    if (stackMode != CK_STACK_REFRESH) {
        setError(c, "unsupported buff stack mode");
        return -1;
    }
    if (!isFiniteFloat(add)) {
        setError(c, "buff add must be finite");
        return -1;
    }
    BuffDef buff;
    buff.id = id;
    buff.stat = stat;
    buff.add = add;
    buff.durationFrames = durationFrames;
    buff.stackMode = stackMode;
    c.buffs.push_back(buff);
    return static_cast<int>(c.buffs.size()) - 1;
}

int addActor(Combat& c, const char* id, int faction, float maxHealth, float attackPower,
             float defense, float moveSpeed) {
    if (c.finalized) {
        setError(c, "cannot add actor after finalize");
        return -1;
    }
    if (id == nullptr || id[0] == '\0') {
        setError(c, "actor id must not be empty");
        return -1;
    }
    const int maxActors =
        (c.cfg.maxActors > 0 && c.cfg.maxActors <= CK_MAX_ACTORS) ? c.cfg.maxActors : CK_MAX_ACTORS;
    if (static_cast<int>(c.actors.size()) >= maxActors) {
        setError(c, "actor capacity exceeded");
        return -1;
    }
    if (!(maxHealth > 0.0f) || !isFiniteFloat(maxHealth)) {
        setError(c, "actor maxHealth must be > 0");
        return -1;
    }
    if (!(attackPower >= 0.0f) || !isFiniteFloat(attackPower)) {
        setError(c, "actor attackPower must be >= 0");
        return -1;
    }
    if (!(defense >= 0.0f) || !isFiniteFloat(defense)) {
        setError(c, "actor defense must be >= 0");
        return -1;
    }
    if (!isFiniteFloat(moveSpeed)) {
        setError(c, "actor moveSpeed must be finite");
        return -1;
    }
    Actor actor;
    actor.id = static_cast<int>(c.actors.size());
    actor.faction = faction;
    actor.maxHealth = maxHealth;
    actor.health = maxHealth;
    actor.attackPower = attackPower;
    actor.baseDefense = defense;
    actor.moveSpeed = moveSpeed;
    c.actors.push_back(actor);
    return actor.id;
}

int setCombo(Combat& c, const int32_t* indices, int count) {
    if (c.finalized) {
        setError(c, "cannot set combo after finalize");
        return -1;
    }
    if (indices == nullptr || count <= 0 || count > CK_MAX_COMBO_STEPS) {
        setError(c, "combo steps out of range");
        return -1;
    }
    for (int i = 0; i < count; ++i) {
        if (indices[i] < 0 || indices[i] >= static_cast<int>(c.abilities.size())) {
            setError(c, "combo step references unknown ability index");
            return -1;
        }
        c.comboSteps[static_cast<size_t>(i)] = indices[i];
    }
    c.comboCount = count;
    return count;
}

bool finalize(Combat& c) {
    if (c.finalized) {
        return true;
    }
    if (c.actors.empty()) {
        setError(c, "at least one actor is required");
        return false;
    }
    if (c.comboCount <= 0) {
        setError(c, "combo steps are required");
        return false;
    }
    if (!(c.cfg.tickRate > 0) || c.cfg.tickRate > 1000) {
        setError(c, "tickRate must be in 1..1000");
        return false;
    }
    if (!(c.cfg.defenseConstant > 0.0f)) {
        setError(c, "defenseConstant must be > 0");
        return false;
    }
    if (c.cfg.minDefense < 0.0f) {
        setError(c, "minDefense must be >= 0");
        return false;
    }
    if (c.cfg.inputBufferFrames < 0 || c.cfg.comboWindowFrames < 0 ||
        c.cfg.dodgeInvulnFrames < 0 || c.cfg.dodgeCooldownFrames < 0 ||
        c.cfg.enemyIntervalFrames < 0) {
        setError(c, "frame windows must be >= 0");
        return false;
    }
    if (!(c.cfg.dodgeDistance >= 0.0f) || !(c.cfg.blockMoveMultiplier >= 0.0f) ||
        !(c.cfg.blockMoveMultiplier <= 1.0f)) {
        setError(c, "dodgeDistance/blockMoveMultiplier are invalid");
        return false;
    }

    c.slotAbility.fill(-1);
    for (size_t i = 0; i < c.abilities.size(); ++i) {
        Ability& ability = c.abilities[i];
        if (ability.id.empty()) {
            setError(c, "ability id must not be empty");
            return false;
        }
        for (size_t j = 0; j < i; ++j) {
            if (c.abilities[j].id == ability.id) {
                setError(c, "duplicate ability id: " + ability.id);
                return false;
            }
        }
        for (int p = 0; p < ability.phaseCount; ++p) {
            const Phase& phase = ability.phases[p];
            if (phase.kind < CK_PHASE_WINDUP || phase.kind > CK_PHASE_RECOVERY) {
                setError(c, "invalid phase kind in " + ability.id);
                return false;
            }
            if (phase.startFrame < 0 || phase.endFrame < phase.startFrame) {
                setError(c, "invalid phase range in " + ability.id);
                return false;
            }
            for (int q = 0; q < p; ++q) {
                const Phase& other = ability.phases[q];
                if (phase.startFrame <= other.endFrame && other.startFrame <= phase.endFrame) {
                    setError(c, "overlapping phases in " + ability.id);
                    return false;
                }
            }
            if (phase.kind == CK_PHASE_ACTIVE) {
                if (ability.activeStart >= 0) {
                    setError(c, "multiple active phases in " + ability.id);
                    return false;
                }
            }
        }
        int endFrame = 0;
        int activeStart = -1;
        int activeEnd = -1;
        for (int p = 0; p < ability.phaseCount; ++p) {
            const Phase& phase = ability.phases[p];
            endFrame = std::max(endFrame, phase.endFrame);
            if (phase.kind == CK_PHASE_ACTIVE) {
                activeStart = phase.startFrame;
                activeEnd = phase.endFrame;
            }
        }
        const_cast<Ability&>(ability).activeStart = activeStart;
        const_cast<Ability&>(ability).activeEnd = activeEnd;
        const_cast<Ability&>(ability).endFrame = endFrame;

        if (ability.multiplier > 0.0f && !ability.hasHitVolume) {
            setError(c, "ability " + ability.id + " deals damage but has no hit volume");
            return false;
        }
        if (ability.hasHitVolume && activeStart < 0) {
            setError(c, "ability " + ability.id + " has a hit volume but no active phase");
            return false;
        }
        if (ability.slot >= 0 && ability.slot < CK_SLOT_COUNT) {
            if (c.slotAbility[static_cast<size_t>(ability.slot)] < 0) {
                c.slotAbility[static_cast<size_t>(ability.slot)] = static_cast<int>(i);
            }
        }
        if (ability.onHitBuff >= static_cast<int>(c.buffs.size())) {
            setError(c, "ability onHitBuff references unknown buff");
            return false;
        }
    }

    for (size_t i = 0; i < c.buffs.size(); ++i) {
        if (c.buffs[i].id.empty()) {
            setError(c, "buff id must not be empty");
            return false;
        }
        for (size_t j = 0; j < i; ++j) {
            if (c.buffs[j].id == c.buffs[i].id) {
                setError(c, "duplicate buff id: " + c.buffs[i].id);
                return false;
            }
        }
    }

    for (int i = 0; i < c.comboCount; ++i) {
        const int index = c.comboSteps[static_cast<size_t>(i)];
        if (index < 0 || index >= static_cast<int>(c.abilities.size())) {
            setError(c, "combo step references unknown ability index");
            return false;
        }
    }

    uint32_t hash = 2166136261u;
    auto mix = [&hash](uint32_t value) {
        hash ^= value;
        hash *= 16777619u;
    };
    mix(static_cast<uint32_t>(c.cfg.tickRate));
    mix(static_cast<uint32_t>(std::llround(c.cfg.defenseConstant)));
    mix(static_cast<uint32_t>(std::llround(c.cfg.minDefense)));
    mix(static_cast<uint32_t>(c.cfg.inputBufferFrames));
    mix(static_cast<uint32_t>(c.cfg.dodgeInvulnFrames));
    mix(static_cast<uint32_t>(c.cfg.dodgeCooldownFrames));
    mix(static_cast<uint32_t>(std::llround(c.cfg.dodgeDistance * 1000.0f)));
    mix(static_cast<uint32_t>(c.cfg.comboWindowFrames));
    for (const Ability& ability : c.abilities) {
        for (char ch : ability.id) {
            mix(static_cast<uint32_t>(static_cast<unsigned char>(ch)));
        }
        mix(static_cast<uint32_t>(ability.slot));
        mix(static_cast<uint32_t>(std::llround(ability.multiplier * 1000.0f)));
        mix(static_cast<uint32_t>(ability.cooldownFrames));
        mix(static_cast<uint32_t>(ability.endFrame));
    }
    for (const BuffDef& buff : c.buffs) {
        for (char ch : buff.id) {
            mix(static_cast<uint32_t>(static_cast<unsigned char>(ch)));
        }
        mix(static_cast<uint32_t>(std::llround(buff.add * 1000.0f)));
        mix(static_cast<uint32_t>(buff.durationFrames));
    }
    for (const Actor& actor : c.actors) {
        mix(static_cast<uint32_t>(actor.faction));
        mix(static_cast<uint32_t>(std::llround(actor.maxHealth)));
        mix(static_cast<uint32_t>(std::llround(actor.attackPower)));
        mix(static_cast<uint32_t>(std::llround(actor.baseDefense)));
    }
    for (int i = 0; i < c.comboCount; ++i) {
        mix(static_cast<uint32_t>(c.comboSteps[static_cast<size_t>(i)]));
    }
    c.configHash = hash;
    c.finalized = true;
    c.lastError.clear();
    reset(c);
    return true;
}

int findAbility(const Combat& c, const char* id) {
    if (id == nullptr) {
        return -1;
    }
    for (size_t i = 0; i < c.abilities.size(); ++i) {
        if (c.abilities[i].id == id) {
            return static_cast<int>(i);
        }
    }
    return -1;
}

void submitCommand(Combat& c, const CkCommand& cmd) {
    PendingCommand command;
    command.kind = cmd.kind;
    command.abilityIndex = cmd.abilityIndex;
    command.issuedTick = cmd.issuedTick;
    command.sequence = cmd.sequence;
    command.actorId = cmd.actorId;
    c.commandQueue.push_back(command);
    if (c.commandQueue.size() > 256u) {
        c.commandQueue.pop_front();
    }
}

void tick(Combat& c, const CkActorFrame* frames, int frameCount) {
    if (!c.finalized) {
        return;
    }
    const uint32_t now = c.tickIndex;

    for (Actor& actor : c.actors) {
        if (!actor.busy && !actor.blocking && actor.blockHeld) {
            actor.blocking = true;
            emitEvent(c, CK_EV_BLOCK_STARTED, actor.id, -1, -1, -1, 0, 0, 0, 0, 0, 0.0f);
        }
        if (!actor.busy && !actor.pending.empty()) {
            processPending(c, actor, now);
        }
    }

    while (!c.commandQueue.empty()) {
        PendingCommand command = c.commandQueue.front();
        c.commandQueue.pop_front();
        handleCommand(c, command, now);
    }

    resolveHits(c, frames, frameCount, now);

    for (Actor& actor : c.actors) {
        advanceActor(c, actor, now);
    }

    c.tickIndex = now + 1;
}

void emitEvent(Combat& c, int type, int sourceActor, int targetActor, int abilityIndex,
               int buffIndex, int overlap, int reason, int defenseBefore, int defenseAfter,
               int healthAfter, float amount) {
    CkEvent event{};
    event.tick = c.tickIndex;
    event.type = type;
    event.sourceActor = sourceActor;
    event.targetActor = targetActor;
    event.abilityIndex = abilityIndex;
    event.buffIndex = buffIndex;
    event.overlap = overlap;
    event.reason = reason;
    event.defenseBefore = defenseBefore;
    event.defenseAfter = defenseAfter;
    event.healthAfter = healthAfter;
    event.sequence = ++c.eventSequence;
    event.amount = amount;

    if (c.eventCount < CK_MAX_EVENTS) {
        const int index = (c.eventHead + c.eventCount) % CK_MAX_EVENTS;
        c.events[static_cast<size_t>(index)] = event;
        c.eventCount += 1;
    } else {
        c.events[static_cast<size_t>(c.eventHead)] = event;
        c.eventHead = (c.eventHead + 1) % CK_MAX_EVENTS;
    }
}

int drainEvents(Combat& c, CkEvent* out, int capacity) {
    if (out == nullptr || capacity <= 0) {
        return 0;
    }
    const int count = std::min(capacity, c.eventCount);
    for (int i = 0; i < count; ++i) {
        out[i] = c.events[static_cast<size_t>((c.eventHead + i) % CK_MAX_EVENTS)];
    }
    c.eventHead = (c.eventHead + count) % CK_MAX_EVENTS;
    c.eventCount -= count;
    return count;
}

void writeSnapshot(const Combat& c, int actorId, CkActorSnapshot* out) {
    if (out == nullptr) {
        return;
    }
    std::memset(out, 0, sizeof(CkActorSnapshot));
    const Actor* found = findActor(c, actorId);
    if (found == nullptr) {
        return;
    }
    const Actor& actor = *found;
    CkActorSnapshot& snapshot = *out;
    snapshot.actorId = actor.id;
    snapshot.faction = actor.faction;
    snapshot.health = actor.health;
    snapshot.maxHealth = actor.maxHealth;
    snapshot.attackPower = actor.attackPower;
    snapshot.baseDefense = actor.baseDefense;
    snapshot.effectiveDefense = static_cast<float>(effectiveDefense(c, actor));
    snapshot.blocking = actor.blocking ? 1 : 0;
    snapshot.invulnerable = actor.invulnerable ? 1 : 0;
    snapshot.busy = actor.busy ? 1 : 0;
    snapshot.abilityIndex = actor.currentAbility;
    snapshot.phase = actor.phase;
    snapshot.abilityFrame = actor.abilityFrame;
    snapshot.comboStep = actor.comboStep;
    snapshot.comboCount = c.comboCount;
    snapshot.comboWindowRemaining = actor.comboWindow;
    for (int slot = 0; slot < CK_SLOT_COUNT; ++slot) {
        const int abilityIndex = c.slotAbility[static_cast<size_t>(slot)];
        snapshot.cooldownBySlot[slot] =
            abilityIndex >= 0 ? actor.cooldown[static_cast<size_t>(abilityIndex)] : 0;
    }
    snapshot.buffCount = static_cast<int>(actor.buffs.size());
    snapshot.buffActive = actor.buffs.empty() ? 0 : 1;
    snapshot.buffRemainingFrames = 0;
    for (const BuffInstance& instance : actor.buffs) {
        snapshot.buffRemainingFrames =
            std::max(snapshot.buffRemainingFrames, instance.remainingFrames);
    }
    snapshot.totalHits = actor.totalHits;
    snapshot.lastAbilityIndex = actor.lastAbilityIndex;
    snapshot.lastDamage = actor.lastDamage;
    snapshot.lastEventType = actor.lastEventType;
}

void reset(Combat& c) {
    c.tickIndex = 0;
    c.commandQueue.clear();
    c.eventHead = 0;
    c.eventCount = 0;
    c.eventSequence = 0;
    for (Actor& actor : c.actors) {
        actor.health = actor.maxHealth;
        actor.currentAbility = -1;
        actor.abilityFrame = 0;
        actor.phase = -1;
        actor.busy = false;
        actor.blocking = false;
        actor.blockHeld = false;
        actor.invulnerable = false;
        actor.cooldown.fill(0);
        actor.hitLatch.fill(false);
        actor.buffs.clear();
        actor.comboStep = 0;
        actor.comboWindow = 0;
        actor.pending.clear();
        actor.lastSequence = 0;
        actor.totalHits = 0;
        actor.lastAbilityIndex = -1;
        actor.lastDamage = 0.0f;
        actor.lastEventType = CK_EV_NONE;
    }
}

} // namespace ck
