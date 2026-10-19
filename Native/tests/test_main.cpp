// Native combat core tests (CTest).
// These tests exercise the authoritative rules: damage formula, phase windows,
// hit latch, combo chain, input buffering, block/dodge immunity, buffs and
// configuration validation.

#include "core/combat_core.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <functional>
#include <string>
#include <vector>

namespace {

int g_failures = 0;
int g_checks = 0;

void fail(const char* file, int line, const std::string& message) {
    std::printf("    FAILED %s:%d  %s\n", file, line, message.c_str());
    ++g_failures;
}

#define CHECK(condition)                                                                            \
    do {                                                                                            \
        ++g_checks;                                                                                 \
        if (!(condition)) {                                                                         \
            fail(__FILE__, __LINE__, #condition);                                                   \
            return;                                                                                 \
        }                                                                                           \
    } while (0)

#define CHECK_EQ(actual, expected)                                                                  \
    do {                                                                                            \
        ++g_checks;                                                                                 \
        const auto actualValue = (actual);                                                          \
        const auto expectedValue = (expected);                                                      \
        if (!(actualValue == expectedValue)) {                                                      \
            fail(__FILE__, __LINE__,                                                                \
                 std::string(#actual " == " #expected " (actual=") + std::to_string(actualValue) +   \
                     ", expected=" + std::to_string(expectedValue) + ")");                          \
            return;                                                                                 \
        }                                                                                           \
    } while (0)

#define CHECK_NEAR(actual, expected)                                                                \
    do {                                                                                            \
        ++g_checks;                                                                                 \
        const double actualValue = (actual);                                                        \
        const double expectedValue = (expected);                                                    \
        if (std::fabs(actualValue - expectedValue) > 1e-4) {                                        \
            fail(__FILE__, __LINE__, std::string(#actual " ~ " #expected));                         \
            return;                                                                                 \
        }                                                                                           \
    } while (0)

/* Local descriptor mirrors used only to keep the test setup readable; the
 * public ABI takes scalar arguments so it stays IL2CPP friendly. */
struct CkPhaseDesc {
    int32_t kind;
    int32_t startFrame;
    int32_t endFrame;
};

struct CkAbilityDesc {
    char id[CK_MAX_ID];
    int32_t slot;
    float multiplier;
    int32_t cooldownFrames;
    int32_t hold;
    int32_t onHitBuffIndex;
    int32_t hasHitVolume;
    int32_t phaseCount;
    CkPhaseDesc phases[CK_MAX_PHASES];
};

struct CkBuffDesc {
    char id[CK_MAX_ID];
    int32_t stat;
    float add;
    int32_t durationFrames;
    int32_t stackMode;
};

struct CkActorDesc {
    char id[CK_MAX_ID];
    int32_t faction;
    float maxHealth;
    float attackPower;
    float defense;
    float moveSpeed;
};

CkPhaseDesc phase(int kind, int start, int end) {
    CkPhaseDesc p{};
    p.kind = kind;
    p.startFrame = start;
    p.endFrame = end;
    return p;
}

CkAbilityDesc ability(const char* id, int slot, float multiplier, int cooldown, bool hitVolume,
                      int onHitBuff, bool hold, std::vector<CkPhaseDesc> phases) {
    CkAbilityDesc d{};
    std::snprintf(d.id, sizeof(d.id), "%s", id);
    d.slot = slot;
    d.multiplier = multiplier;
    d.cooldownFrames = cooldown;
    d.hasHitVolume = hitVolume ? 1 : 0;
    d.onHitBuffIndex = onHitBuff;
    d.hold = hold ? 1 : 0;
    d.phaseCount = static_cast<int>(phases.size());
    for (size_t i = 0; i < phases.size() && i < CK_MAX_PHASES; ++i) {
        d.phases[i] = phases[i];
    }
    return d;
}

CkBuffDesc buffDesc(const char* id, float add, int duration) {
    CkBuffDesc d{};
    std::snprintf(d.id, sizeof(d.id), "%s", id);
    d.stat = CK_STAT_DEFENSE;
    d.add = add;
    d.durationFrames = duration;
    d.stackMode = CK_STACK_REFRESH;
    return d;
}

CkActorDesc actorDesc(const char* id, int faction, float hp, float atk, float def, float speed) {
    CkActorDesc d{};
    std::snprintf(d.id, sizeof(d.id), "%s", id);
    d.faction = faction;
    d.maxHealth = hp;
    d.attackPower = atk;
    d.defense = def;
    d.moveSpeed = speed;
    return d;
}

CkGlobalConfig globalConfig() {
    CkGlobalConfig g{};
    g.tickRate = 60;
    g.defenseConstant = 100.0f;
    g.minDefense = 0.0f;
    g.inputBufferFrames = 10;
    g.dodgeInvulnFrames = 18;
    g.dodgeCooldownFrames = 30;
    g.dodgeDistance = 4.0f;
    g.blockMoveMultiplier = 0.3f;
    g.comboWindowFrames = 21;
    g.enemyIntervalFrames = 120;
    g.maxAbilities = CK_MAX_ABILITIES;
    g.maxBuffs = CK_MAX_BUFFS;
    g.maxActors = CK_MAX_ACTORS;
    return g;
}

/* Local descriptor mirrors used only to keep the test setup readable; the
 * public ABI takes scalar arguments so it stays IL2CPP friendly. */
int addAbilityCompat(ck::Combat& c, const CkAbilityDesc& desc) {
    int32_t triples[CK_MAX_PHASES * 3] = {};
    for (int i = 0; i < desc.phaseCount; ++i) {
        triples[i * 3] = desc.phases[i].kind;
        triples[i * 3 + 1] = desc.phases[i].startFrame;
        triples[i * 3 + 2] = desc.phases[i].endFrame;
    }
    return ck::addAbility(c, desc.id, desc.slot, desc.multiplier, desc.cooldownFrames,
                          desc.hold != 0, desc.onHitBuffIndex, desc.hasHitVolume != 0, triples,
                          desc.phaseCount);
}

int addBuffCompat(ck::Combat& c, const CkBuffDesc& desc) {
    return ck::addBuff(c, desc.id, desc.stat, desc.add, desc.durationFrames, desc.stackMode);
}

int addActorCompat(ck::Combat& c, const CkActorDesc& desc) {
    return ck::addActor(c, desc.id, desc.faction, desc.maxHealth, desc.attackPower, desc.defense,
                        desc.moveSpeed);
}

CkCapsule capsule(float ax, float ay, float az, float bx, float by, float bz, float radius) {
    CkCapsule c{};
    c.ax = ax;
    c.ay = ay;
    c.az = az;
    c.bx = bx;
    c.by = by;
    c.bz = bz;
    c.radius = radius;
    return c;
}

struct Harness {
    ck::Combat combat;
    uint32_t sequence = 0;
    std::vector<CkEvent> events;
    CkActorSnapshot snapshots[CK_MAX_ACTORS] = {};

    bool build(bool includeCombo = true, bool includeAlly = false, bool doFinalize = true) {
        combat.cfg = globalConfig();
        combat.hasConfig = true;
        if (addBuffCompat(combat, buffDesc("def_down_50", -50.0f, 600)) != 0) {
            return false;
        }
        const bool ok =
            addAbilityCompat(combat, ability("normal_1", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 9), phase(CK_PHASE_ACTIVE, 10, 15),
                                           phase(CK_PHASE_RECOVERY, 16, 30)})) == 0 &&
            addAbilityCompat(combat, ability("normal_2", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 8), phase(CK_PHASE_ACTIVE, 9, 14),
                                           phase(CK_PHASE_RECOVERY, 15, 28)})) == 1 &&
            addAbilityCompat(combat, ability("normal_3", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 11), phase(CK_PHASE_ACTIVE, 12, 18),
                                           phase(CK_PHASE_RECOVERY, 19, 36)})) == 2 &&
            addAbilityCompat(combat, ability("heavy", CK_SLOT_HEAVY, 1.5f, 0, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 20), phase(CK_PHASE_ACTIVE, 21, 28),
                                           phase(CK_PHASE_RECOVERY, 29, 55)})) == 3 &&
            addAbilityCompat(combat, ability("block", CK_SLOT_BLOCK, 0.0f, 0, false, -1, true, {})) == 4 &&
            addAbilityCompat(combat, ability("dodge", CK_SLOT_DODGE, 0.0f, 30, false, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 0), phase(CK_PHASE_ACTIVE, 1, 18),
                                           phase(CK_PHASE_RECOVERY, 19, 29)})) == 5 &&
            addAbilityCompat(combat, ability("q_break", CK_SLOT_SKILL1, 0.5f, 480, true, 0, false,
                                          {phase(CK_PHASE_WINDUP, 0, 12), phase(CK_PHASE_ACTIVE, 13, 19),
                                           phase(CK_PHASE_RECOVERY, 20, 42)})) == 6 &&
            addAbilityCompat(combat, ability("e_placeholder", CK_SLOT_SKILL2, 0.8f, 360, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 10), phase(CK_PHASE_ACTIVE, 11, 17),
                                           phase(CK_PHASE_RECOVERY, 18, 40)})) == 7 &&
            addAbilityCompat(combat, ability("r_placeholder", CK_SLOT_SKILL3, 0.6f, 600, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 14), phase(CK_PHASE_ACTIVE, 15, 24),
                                           phase(CK_PHASE_RECOVERY, 25, 50)})) == 8 &&
            addAbilityCompat(combat, ability("f_placeholder", CK_SLOT_SKILL4, 0.0f, 720, false, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 5), phase(CK_PHASE_ACTIVE, 6, 12),
                                           phase(CK_PHASE_RECOVERY, 13, 30)})) == 9 &&
            addAbilityCompat(combat, ability("enemy_swing", CK_SLOT_ENEMY_ATTACK, 1.0f, 0, true, -1, false,
                                          {phase(CK_PHASE_WINDUP, 0, 29), phase(CK_PHASE_ACTIVE, 30, 41),
                                           phase(CK_PHASE_RECOVERY, 42, 59)})) == 10;
        if (!ok) {
            std::printf("    build: ability add failed: %s\n", combat.lastError.c_str());
            return false;
        }
        if (addActorCompat(combat, actorDesc("player", 0, 1000.0f, 100.0f, 50.0f, 5.0f)) != 0) {
            return false;
        }
        if (addActorCompat(combat, actorDesc("enemy", 1, 1000.0f, 100.0f, 50.0f, 0.0f)) != 1) {
            return false;
        }
        if (includeCombo) {
            const int32_t combo[3] = {0, 1, 2};
            if (ck::setCombo(combat, combo, 3) != 3) {
                return false;
            }
        }
        if (includeAlly) {
            if (addActorCompat(combat, actorDesc("ally", 0, 500.0f, 10.0f, 0.0f, 0.0f)) != 2) {
                return false;
            }
        }
        if (!doFinalize) {
            return true;
        }
        if (!ck::finalize(combat)) {
            std::printf("    build: finalize failed: %s\n", combat.lastError.c_str());
            return false;
        }
        return true;
    }

    void clearEvents() { events.clear(); }

    void command(int actorId, int kind, int abilityIndex = -1) {
        CkCommand cmd{};
        cmd.actorId = actorId;
        cmd.kind = kind;
        cmd.abilityIndex = abilityIndex;
        cmd.issuedTick = combat.tickIndex;
        cmd.sequence = ++sequence;
        ck::submitCommand(combat, cmd);
    }

    void tickFrame(bool playerWeaponOverlaps = true, bool enemyWeaponOverlaps = true,
                   int extraPlayerActors = 0) {
        CkActorFrame frames[CK_MAX_ACTORS] = {};
        const int count = static_cast<int>(combat.actors.size());
        for (int i = 0; i < count; ++i) {
            const int id = combat.actors[static_cast<size_t>(i)].id;
            frames[i].actorId = id;
            float z = 0.0f;
            if (id == 1) {
                z = 2.0f;
            } else if (id >= 2) {
                z = 1.0f;
            }
            frames[i].body = capsule(0.0f, 0.5f, z, 0.0f, 1.5f, z, 0.5f);
            if (id == 0) {
                const float x = playerWeaponOverlaps ? 0.4f : 3.0f;
                frames[i].weapon = capsule(x, 1.1f, 0.25f, x, 1.1f, 1.85f, 0.08f);
            } else if (id == 1) {
                const float x = enemyWeaponOverlaps ? -0.4f : 3.0f;
                frames[i].weapon = capsule(x, 1.1f, 1.75f, x, 1.1f, 0.15f, 0.08f);
            } else {
                frames[i].weapon = capsule(3.0f, 1.1f, 0.25f, 3.0f, 1.1f, 1.85f, 0.08f);
            }
        }
        (void)extraPlayerActors;
        CkEvent buffer[64];
        ck::tick(combat, frames, count);
        const int eventCount = ck::drainEvents(combat, buffer, 64);
        for (int i = 0; i < eventCount; ++i) {
            events.push_back(buffer[i]);
        }
        for (int i = 0; i < count; ++i) {
            ck::writeSnapshot(combat, i, &snapshots[i]);
        }
    }

    void run(int frames, bool playerWeaponOverlaps = true, bool enemyWeaponOverlaps = true) {
        for (int i = 0; i < frames; ++i) {
            tickFrame(playerWeaponOverlaps, enemyWeaponOverlaps);
        }
    }

    int countEvents(int type) const {
        int count = 0;
        for (const CkEvent& event : events) {
            if (event.type == type) {
                ++count;
            }
        }
        return count;
    }

    const CkEvent* firstEvent(int type) const {
        for (const CkEvent& event : events) {
            if (event.type == type) {
                return &event;
            }
        }
        return nullptr;
    }

    const CkActorSnapshot& actor(int id) {
        ck::writeSnapshot(combat, id, &snapshots[id]);
        return snapshots[id];
    }

    const CkActorSnapshot& player() { return actor(0); }
    const CkActorSnapshot& enemy() { return actor(1); }
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

void test_abi_and_formula() {
    CHECK_EQ(ck_get_abi_version(), CK_ABI_VERSION);
    CHECK_EQ(ck::computeDamage(100.0f, 1.0f, 50, 100.0f), 67);
    CHECK_EQ(ck::computeDamage(100.0f, 1.5f, 50, 100.0f), 100);
    CHECK_EQ(ck::computeDamage(100.0f, 0.5f, 50, 100.0f), 33);
    CHECK_EQ(ck::computeDamage(100.0f, 1.0f, 0, 100.0f), 100);
    CHECK_EQ(ck::computeDamage(100.0f, 1.5f, 0, 100.0f), 150);
    CHECK_EQ(ck::computeDamage(100.0f, 0.5f, 0, 100.0f), 50);
}

void test_initial_snapshot() {
    Harness h;
    CHECK(h.build());
    CHECK_EQ(h.player().health, 1000.0f);
    CHECK_EQ(h.player().baseDefense, 50.0f);
    CHECK_EQ(h.player().effectiveDefense, 50.0f);
    CHECK_EQ(h.player().attackPower, 100.0f);
    CHECK_EQ(h.enemy().health, 1000.0f);
    CHECK_EQ(h.combat.tickIndex, 0u);
}

void test_primary_damage_and_active_window() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(10);
    CHECK_EQ(h.enemy().health, 1000.0f);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 0);
    h.run(1);
    CHECK_EQ(h.enemy().health, 933.0f);
    const CkEvent* hit = h.firstEvent(CK_EV_HIT);
    CHECK(hit != nullptr);
    CHECK_EQ(static_cast<int>(hit->amount), 67);
    CHECK_EQ(hit->sourceActor, 0);
    CHECK_EQ(hit->targetActor, 1);
    CHECK_EQ(hit->overlap, 1);
    CHECK_EQ(hit->healthAfter, 933);
    h.run(30);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 1);
}

void test_primary_miss_when_no_overlap() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(40, false, true);
    CHECK_EQ(h.enemy().health, 1000.0f);
    CHECK_EQ(h.countEvents(CK_EV_CONTACT), 0);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 0);
}

void test_heavy_damage() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_HEAVY);
    h.run(40);
    CHECK_EQ(h.enemy().health, 900.0f);
    const CkEvent* hit = h.firstEvent(CK_EV_HIT);
    CHECK(hit != nullptr);
    CHECK_EQ(static_cast<int>(hit->amount), 100);
}

void test_enemy_attack_damage_and_interval_source() {
    Harness h;
    CHECK(h.build());
    h.command(1, CK_CMD_ENEMY_ATTACK);
    h.run(30);
    CHECK_EQ(h.player().health, 1000.0f);
    h.run(1);
    CHECK_EQ(h.player().health, 933.0f);
    const CkEvent* hit = h.firstEvent(CK_EV_HIT);
    CHECK(hit != nullptr);
    CHECK_EQ(hit->sourceActor, 1);
    CHECK_EQ(static_cast<int>(hit->amount), 67);
}

void test_block_requires_contact() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_BLOCK_PRESS);
    h.command(1, CK_CMD_ENEMY_ATTACK);
    h.run(70);
    const CkEvent* blocked = h.firstEvent(CK_EV_BLOCKED);
    CHECK(blocked != nullptr);
    CHECK_EQ(blocked->overlap, 1);
    CHECK_EQ(static_cast<int>(blocked->amount), 0);
    CHECK_EQ(h.player().health, 1000.0f);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 0);
}

void test_block_without_overlap_is_miss() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_BLOCK_PRESS);
    h.command(1, CK_CMD_ENEMY_ATTACK);
    h.run(70, true, false);
    CHECK_EQ(h.countEvents(CK_EV_BLOCKED), 0);
    CHECK_EQ(h.countEvents(CK_EV_CONTACT), 0);
    CHECK_EQ(h.player().health, 1000.0f);
}

void test_dodge_requires_contact() {
    Harness h;
    CHECK(h.build());
    h.command(1, CK_CMD_ENEMY_ATTACK);
    h.run(28);
    h.command(0, CK_CMD_DODGE);
    h.run(20);
    const CkEvent* dodged = h.firstEvent(CK_EV_DODGED);
    CHECK(dodged != nullptr);
    CHECK_EQ(dodged->overlap, 1);
    CHECK_EQ(h.player().health, 1000.0f);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 0);
    const CkEvent* contact = h.firstEvent(CK_EV_CONTACT);
    CHECK(contact != nullptr);
    CHECK_EQ(contact->type, CK_EV_CONTACT);
}

void test_dodge_invulnerability_expires() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_DODGE);
    h.command(1, CK_CMD_ENEMY_ATTACK);
    h.run(35);
    CHECK_EQ(h.countEvents(CK_EV_DODGED), 0);
    CHECK_EQ(h.player().health, 933.0f);
}

void test_combo_chain_and_reset() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(40);
    h.command(0, CK_CMD_PRIMARY);
    h.run(40);
    h.command(0, CK_CMD_PRIMARY);
    h.run(40);
    CHECK_EQ(h.player().comboStep, 3);
    h.command(0, CK_CMD_PRIMARY);
    h.run(40);
    std::vector<int> started;
    for (const CkEvent& event : h.events) {
        if (event.type == CK_EV_ABILITY_STARTED && event.sourceActor == 0) {
            started.push_back(event.abilityIndex);
        }
    }
    CHECK_EQ(started.size(), static_cast<size_t>(4));
    CHECK_EQ(started[0], 0);
    CHECK_EQ(started[1], 1);
    CHECK_EQ(started[2], 2);
    CHECK_EQ(started[3], 0);
    CHECK(h.combat.abilities[0].id == "normal_1");
}

void test_combo_window_resets() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(80);
    CHECK_EQ(h.player().comboStep, 0);
    h.command(0, CK_CMD_PRIMARY);
    h.run(20);
    CHECK_EQ(h.player().comboStep, 1);
}

void test_input_buffer_accepts_within_window() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(25);
    h.command(0, CK_CMD_PRIMARY);
    h.run(10);
    bool secondStarted = false;
    for (const CkEvent& event : h.events) {
        if (event.type == CK_EV_ABILITY_STARTED && event.abilityIndex == 1) {
            secondStarted = true;
        }
    }
    CHECK(secondStarted);
    CHECK_EQ(h.countEvents(CK_EV_REJECTED), 0);
}

void test_input_buffer_expires() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_PRIMARY);
    h.run(20);
    h.command(0, CK_CMD_PRIMARY);
    h.run(20);
    bool expired = false;
    for (const CkEvent& event : h.events) {
        if (event.type == CK_EV_REJECTED && event.reason == CK_REJECT_EXPIRED) {
            expired = true;
        }
    }
    CHECK(expired);
}

void test_q_hit_and_defense_reduction() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL1);
    h.run(20);
    CHECK_EQ(h.enemy().health, 967.0f);
    CHECK_EQ(h.enemy().effectiveDefense, 0.0f);
    CHECK(h.enemy().buffRemainingFrames >= 590);
    const CkEvent* hit = h.firstEvent(CK_EV_HIT);
    CHECK(hit != nullptr);
    CHECK_EQ(static_cast<int>(hit->amount), 33);
    CHECK_EQ(hit->defenseBefore, 50);
    const CkEvent* buff = h.firstEvent(CK_EV_BUFF_APPLIED);
    CHECK(buff != nullptr);
    CHECK_EQ(buff->buffIndex, 0);
    CHECK_EQ(buff->defenseAfter, 0);
}

void test_damage_after_debuff() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL1);
    h.run(60);
    h.command(0, CK_CMD_PRIMARY);
    h.run(40);
    CHECK_EQ(h.enemy().health, 867.0f);
    h.command(0, CK_CMD_HEAVY);
    h.run(60);
    CHECK_EQ(h.enemy().health, 717.0f);
    h.run(340);
    h.command(0, CK_CMD_SKILL1);
    h.run(60);
    CHECK_EQ(h.enemy().health, 667.0f);
}

void test_buff_refresh_no_stack() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL1);
    h.run(60);
    CHECK_EQ(h.enemy().effectiveDefense, 0.0f);
    h.run(440);
    CHECK_EQ(h.enemy().effectiveDefense, 0.0f);
    const int before = h.enemy().buffRemainingFrames;
    h.command(0, CK_CMD_SKILL1);
    h.run(20);
    const int after = h.enemy().buffRemainingFrames;
    CHECK(after > before);
    CHECK(after >= 580);
    CHECK_EQ(h.enemy().buffCount, 1);
    CHECK_EQ(h.enemy().effectiveDefense, 0.0f);
}

void test_buff_expires() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL1);
    h.run(30);
    CHECK_EQ(h.enemy().effectiveDefense, 0.0f);
    h.run(650);
    CHECK_EQ(h.enemy().effectiveDefense, 50.0f);
    CHECK_EQ(h.enemy().buffCount, 0);
    CHECK(h.countEvents(CK_EV_BUFF_EXPIRED) >= 1);
}

void test_skill_cooldown_blocks_repeat() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL1);
    h.run(45);
    h.command(0, CK_CMD_SKILL1);
    h.run(10);
    bool cooldownRejected = false;
    for (const CkEvent& event : h.events) {
        if (event.type == CK_EV_REJECTED && event.reason == CK_REJECT_COOLDOWN) {
            cooldownRejected = true;
        }
    }
    CHECK(cooldownRejected);
    CHECK_EQ(h.countEvents(CK_EV_ABILITY_STARTED), 1);
    CHECK(h.player().cooldownBySlot[CK_SLOT_SKILL1] > 400);
}

void test_erf_skills() {
    Harness h;
    CHECK(h.build());
    h.command(0, CK_CMD_SKILL2);
    h.run(45);
    CHECK_EQ(h.enemy().health, 947.0f);
    h.command(0, CK_CMD_SKILL3);
    h.run(55);
    CHECK_EQ(h.enemy().health, 907.0f);
    const int before = static_cast<int>(h.enemy().health);
    h.command(0, CK_CMD_SKILL4);
    h.run(35);
    CHECK_EQ(static_cast<int>(h.enemy().health), before);
    CHECK(h.player().cooldownBySlot[CK_SLOT_SKILL4] > 650);
    CHECK_EQ(h.countEvents(CK_EV_HIT), 2);
}

void test_blocked_q_does_not_apply_buff() {
    Harness h;
    CHECK(h.build());
    h.command(1, CK_CMD_BLOCK_PRESS);
    h.command(0, CK_CMD_SKILL1);
    h.run(30);
    CHECK_EQ(h.countEvents(CK_EV_BLOCKED), 1);
    CHECK_EQ(h.countEvents(CK_EV_BUFF_APPLIED), 0);
    CHECK_EQ(h.enemy().effectiveDefense, 50.0f);
    CHECK_EQ(h.enemy().health, 1000.0f);
}

void test_friendly_fire_ignored() {
    Harness h;
    CHECK(h.build(true, true));
    h.command(0, CK_CMD_PRIMARY);
    h.run(40, true, true);
    CHECK_EQ(h.actor(2).health, 500.0f);
    CHECK_EQ(h.enemy().health, 933.0f);
}

void test_duplicate_command_ignored() {
    Harness h;
    CHECK(h.build());
    CkCommand cmd{};
    cmd.actorId = 0;
    cmd.kind = CK_CMD_PRIMARY;
    cmd.issuedTick = 0;
    cmd.sequence = 7;
    ck::submitCommand(h.combat, cmd);
    ck::submitCommand(h.combat, cmd);
    h.run(1);
    int started = 0;
    for (const CkEvent& event : h.events) {
        if (event.type == CK_EV_ABILITY_STARTED) {
            ++started;
        }
    }
    CHECK_EQ(started, 1);
}

void test_config_rejects_duplicate_ability_id() {
    ck::Combat combat;
    combat.cfg = globalConfig();
    combat.hasConfig = true;
    CHECK_EQ(addBuffCompat(combat, buffDesc("b", -50.0f, 600)), 0);
    CkAbilityDesc first = ability("dup", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                  {phase(CK_PHASE_ACTIVE, 0, 10)});
    CHECK_EQ(addAbilityCompat(combat, first), 0);
    CHECK_EQ(addAbilityCompat(combat, first), 1);
    CHECK_EQ(addActorCompat(combat, actorDesc("p", 0, 100.0f, 10.0f, 0.0f, 1.0f)), 0);
    const int32_t combo[1] = {0};
    CHECK_EQ(ck::setCombo(combat, combo, 1), 1);
    CHECK(!ck::finalize(combat));
    CHECK(combat.lastError.find("duplicate") != std::string::npos);
}

void test_config_rejects_overlapping_phases() {
    ck::Combat combat;
    combat.cfg = globalConfig();
    combat.hasConfig = true;
    CkAbilityDesc bad = ability("overlap", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                {phase(CK_PHASE_WINDUP, 0, 10), phase(CK_PHASE_ACTIVE, 5, 15)});
    CHECK_EQ(addAbilityCompat(combat, bad), 0);
    CHECK_EQ(addActorCompat(combat, actorDesc("p", 0, 100.0f, 10.0f, 0.0f, 1.0f)), 0);
    const int32_t combo[1] = {0};
    CHECK_EQ(ck::setCombo(combat, combo, 1), 1);
    CHECK(!ck::finalize(combat));
    CHECK(combat.lastError.find("overlapping") != std::string::npos);
}

void test_config_rejects_damage_without_hit_volume() {
    ck::Combat combat;
    combat.cfg = globalConfig();
    combat.hasConfig = true;
    CkAbilityDesc bad = ability("nohit", CK_SLOT_PRIMARY, 1.0f, 0, false, -1, false,
                                {phase(CK_PHASE_ACTIVE, 0, 10)});
    CHECK_EQ(addAbilityCompat(combat, bad), 0);
    CHECK_EQ(addActorCompat(combat, actorDesc("p", 0, 100.0f, 10.0f, 0.0f, 1.0f)), 0);
    const int32_t combo[1] = {0};
    CHECK_EQ(ck::setCombo(combat, combo, 1), 1);
    CHECK(!ck::finalize(combat));
    CHECK(combat.lastError.find("hit volume") != std::string::npos);
}

void test_config_rejects_bad_combo() {
    ck::Combat combat;
    combat.cfg = globalConfig();
    combat.hasConfig = true;
    CkAbilityDesc first = ability("a", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                   {phase(CK_PHASE_ACTIVE, 0, 10)});
    CHECK_EQ(addAbilityCompat(combat, first), 0);
    CHECK_EQ(addActorCompat(combat, actorDesc("p", 0, 100.0f, 10.0f, 0.0f, 1.0f)), 0);
    const int32_t badCombo[1] = {5};
    CHECK_EQ(ck::setCombo(combat, badCombo, 1), -1);
}

void test_config_rejects_invalid_actor_stats() {
    ck::Combat combat;
    combat.cfg = globalConfig();
    combat.hasConfig = true;
    CHECK_EQ(addActorCompat(combat, actorDesc("p", 0, -1.0f, 10.0f, 0.0f, 1.0f)), -1);
    CHECK(combat.lastError.find("maxHealth") != std::string::npos);
}

void test_config_requires_combo() {
    Harness h;
    CHECK(h.build(false, false, false));
    CHECK(!ck::finalize(h.combat));
    CHECK(h.combat.lastError.find("combo") != std::string::npos);
}

void test_c_api_lifecycle() {
    CkGlobalConfig gc = globalConfig();
    void* handle = ck_combat_create(&gc);
    CHECK(handle != nullptr);
    CHECK_EQ(ck_combat_add_buff_ex(handle, "def_down_50", CK_STAT_DEFENSE, -50.0f, 600,
                                   CK_STACK_REFRESH),
             0);
    auto addAbilityC = [](void* combatHandle, const CkAbilityDesc& desc) {
        int32_t triples[CK_MAX_PHASES * 3] = {};
        for (int i = 0; i < desc.phaseCount; ++i) {
            triples[i * 3] = desc.phases[i].kind;
            triples[i * 3 + 1] = desc.phases[i].startFrame;
            triples[i * 3 + 2] = desc.phases[i].endFrame;
        }
        return ck_combat_add_ability_ex(combatHandle, desc.id, desc.slot, desc.multiplier,
                                        desc.cooldownFrames, desc.hold, desc.onHitBuffIndex,
                                        desc.hasHitVolume, triples, desc.phaseCount);
    };
    CkAbilityDesc primary = ability("normal_1", CK_SLOT_PRIMARY, 1.0f, 0, true, -1, false,
                                    {phase(CK_PHASE_WINDUP, 0, 9), phase(CK_PHASE_ACTIVE, 10, 15),
                                     phase(CK_PHASE_RECOVERY, 16, 30)});
    CHECK_EQ(addAbilityC(handle, primary), 0);
    CkAbilityDesc enemySwing = ability("enemy_swing", CK_SLOT_ENEMY_ATTACK, 1.0f, 0, true, -1, false,
                                       {phase(CK_PHASE_WINDUP, 0, 29), phase(CK_PHASE_ACTIVE, 30, 41),
                                        phase(CK_PHASE_RECOVERY, 42, 59)});
    CHECK_EQ(addAbilityC(handle, enemySwing), 1);
    CHECK_EQ(ck_combat_add_actor_ex(handle, "player", 0, 1000.0f, 100.0f, 50.0f, 5.0f), 0);
    CHECK_EQ(ck_combat_add_actor_ex(handle, "enemy", 1, 1000.0f, 100.0f, 50.0f, 0.0f), 1);
    const int32_t combo[1] = {0};
    CHECK_EQ(ck_combat_set_combo(handle, combo, 1), 1);
    CHECK_EQ(ck_combat_finalize(handle), 1);
    CHECK_EQ(ck_combat_find_ability(handle, "normal_1"), 0);
    CHECK_EQ(ck_combat_find_ability(handle, "missing"), -1);
    CHECK(ck_combat_config_hash(handle) != 0u);
    CHECK_EQ(ck_combat_actor_count(handle), 2);

    CkCommand cmd{};
    cmd.actorId = 0;
    cmd.kind = CK_CMD_PRIMARY;
    cmd.issuedTick = 0;
    cmd.sequence = 1;
    ck_combat_submit_command(handle, &cmd);

    CkActorFrame frames[2] = {};
    frames[0].actorId = 0;
    frames[0].body = capsule(0.0f, 0.5f, 0.0f, 0.0f, 1.5f, 0.0f, 0.5f);
    frames[0].weapon = capsule(0.4f, 1.1f, 0.25f, 0.4f, 1.1f, 1.85f, 0.08f);
    frames[1].actorId = 1;
    frames[1].body = capsule(0.0f, 0.5f, 2.0f, 0.0f, 1.5f, 2.0f, 0.5f);
    frames[1].weapon = capsule(-0.4f, 1.1f, 1.75f, -0.4f, 1.1f, 0.15f, 0.08f);
    for (int tick = 0; tick < 20; ++tick) {
        ck_combat_tick(handle, frames, 2);
    }
    CkActorSnapshot snapshot{};
    ck_combat_get_actor_snapshot(handle, 1, &snapshot);
    CHECK_EQ(snapshot.health, 933.0f);
    CHECK_EQ(ck_combat_tick_index(handle), 20u);
    ck_combat_reset(handle);
    ck_combat_get_actor_snapshot(handle, 1, &snapshot);
    CHECK_EQ(snapshot.health, 1000.0f);
    CHECK_EQ(ck_combat_tick_index(handle), 0u);
    ck_combat_destroy(handle);
}

void test_c_api_rejects_bad_global_config() {
    CkGlobalConfig gc = globalConfig();
    gc.tickRate = 0;
    CHECK(ck_combat_create(&gc) == nullptr);
    CHECK(std::strlen(ck_get_last_error()) > 0);
}

void test_capsule_geometry() {
    CHECK(ck::capsuleOverlap(capsule(0.0f, 0.5f, 0.0f, 0.0f, 1.5f, 0.0f, 0.5f),
                             capsule(0.4f, 1.1f, 0.25f, 0.4f, 1.1f, 1.85f, 0.08f)));
    CHECK(!ck::capsuleOverlap(capsule(0.0f, 0.5f, 0.0f, 0.0f, 1.5f, 0.0f, 0.5f),
                              capsule(3.0f, 1.1f, 0.25f, 3.0f, 1.1f, 1.85f, 0.08f)));
    CHECK(!ck::capsuleOverlap(capsule(0.0f, 0.5f, 0.0f, 0.0f, 1.5f, 0.0f, 0.5f),
                              capsule(0.0f, 0.5f, 5.0f, 0.0f, 1.5f, 5.0f, 0.1f)));
}

struct TestCase {
    const char* name;
    void (*function)();
};

} // namespace

int main() {
    const TestCase tests[] = {
        {"abi_and_damage_formula", test_abi_and_formula},
        {"initial_snapshot", test_initial_snapshot},
        {"primary_damage_and_active_window", test_primary_damage_and_active_window},
        {"primary_miss_when_no_overlap", test_primary_miss_when_no_overlap},
        {"heavy_damage", test_heavy_damage},
        {"enemy_attack_damage", test_enemy_attack_damage_and_interval_source},
        {"block_requires_contact", test_block_requires_contact},
        {"block_without_overlap", test_block_without_overlap_is_miss},
        {"dodge_requires_contact", test_dodge_requires_contact},
        {"dodge_invulnerability_expires", test_dodge_invulnerability_expires},
        {"combo_chain_and_reset", test_combo_chain_and_reset},
        {"combo_window_resets", test_combo_window_resets},
        {"input_buffer_accepts", test_input_buffer_accepts_within_window},
        {"input_buffer_expires", test_input_buffer_expires},
        {"q_hit_and_debuff", test_q_hit_and_defense_reduction},
        {"damage_after_debuff", test_damage_after_debuff},
        {"buff_refresh_no_stack", test_buff_refresh_no_stack},
        {"buff_expires", test_buff_expires},
        {"skill_cooldown", test_skill_cooldown_blocks_repeat},
        {"erf_skills", test_erf_skills},
        {"blocked_q_no_buff", test_blocked_q_does_not_apply_buff},
        {"friendly_fire_ignored", test_friendly_fire_ignored},
        {"duplicate_command_ignored", test_duplicate_command_ignored},
        {"config_duplicate_id", test_config_rejects_duplicate_ability_id},
        {"config_overlapping_phases", test_config_rejects_overlapping_phases},
        {"config_damage_without_volume", test_config_rejects_damage_without_hit_volume},
        {"config_bad_combo", test_config_rejects_bad_combo},
        {"config_bad_actor_stats", test_config_rejects_invalid_actor_stats},
        {"config_requires_combo", test_config_requires_combo},
        {"c_api_lifecycle", test_c_api_lifecycle},
        {"c_api_bad_global_config", test_c_api_rejects_bad_global_config},
        {"capsule_geometry", test_capsule_geometry},
    };

    int executed = 0;
    for (const TestCase& test : tests) {
        const int failuresBefore = g_failures;
        std::printf("[ RUN  ] %s\n", test.name);
        test.function();
        ++executed;
        if (g_failures == failuresBefore) {
            std::printf("[  OK  ] %s\n", test.name);
        } else {
            std::printf("[ FAIL ] %s\n", test.name);
        }
    }

    std::printf("\n%d tests, %d checks, %d failures\n", executed, g_checks, g_failures);
    return g_failures == 0 ? 0 : 1;
}
