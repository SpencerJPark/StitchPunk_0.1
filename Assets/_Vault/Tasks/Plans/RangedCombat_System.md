# Ranged / Projectile Combat System — Design Spec

> **Status:** ✅ decisions settled 2026-10-31 (RC-D1–D10 below, owner + recorded calls). They **supersede** §2/§4 where they disagree; the build is SmartHumanAI H5.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #8 — last unbuilt phase of the behavior-recreation queue; currently **zero code**.
> **Prerequisites (hard):** [Despawn_System.md](Despawn_System.md) built (projectile pooling), [`../Verification/BehaviorCommandSplit_System.md`](../Verification/BehaviorCommandSplit_System.md) — **built** 2026-08-29 (don't inflate the monolith; new arms land in `Utils/BehaviorCommands/`), [MinionOrderRobustness_System.md](MinionOrderRobustness_System.md) (ordering ranged minions), BehaviorBakeValidation catalog (SpawnEntity gets un-flagged when its arm lands).

---

## 0. Decisions (2026-10-31 — supersede §2–§5 where they differ)

| Id | Decision | Source |
|---|---|---|
| RC-D1 | **Friendly fire is on for every projectile, bullets AND thrown rocks.** A projectile hits the **first live unit along its path**, whatever its faction; only the shooter itself is skipped (skip-until-cleared). Diving into a shot takes it, and a body in the way blocks it. This **reverses H3's same-faction skip** in `ThrownItemHitSystem`. | Owner |
| RC-D2 | **Rocks and bullets are one projectile family.** Both are their own entities with one shared swept hit test. They differ only at the end of flight: a rock lands and becomes a loose pickable item again (today's ThrownItem path); a bullet that hits or reaches its TTL returns to the Despawn pool (`PoolOwner`). | Owner |
| RC-D3 | **Bullets are faster than rocks but still dodgeable.** Speed is per gun, with ~25 m/s as the default to tune from (rock 14). No hitscan. The player's roll (`DodgeInvulnerable`) already makes a dodged shot pass through. | Owner |
| RC-D4 | **Multiple guns, each with its own stats.** A gun is an `ItemSO` (category Weapon) carrying its own damage source, projectile speed, fire cooldown, AI min/max range and projectile prefab. This follows Rock's `throwSpeed`/`aiThrow*` precedent. **This replaces the old §4 choice**: config lives on the item, not on `AttackLibraryBlob`, and there is no ProjectileLibrary. | Owner → recorded |
| RC-D5 | **How humans get guns:** they spawn holding one (a new `UnitSO` starting-weapon field, equipped at spawn) or pick one up from the world. v1 places guns in the CampLoop camp for a few bandits. | Owner |
| RC-D6 | **A gun is a weapon option in the existing grab.** ItemAwareness' EquipWeapon (pre- and mid-fight) considers guns alongside rocks. When several are in reach, the unit takes the one with the highest `ItemSO.aiWeaponValue` (gun > rock), with distance breaking ties. | Owner → recorded |
| RC-D7 | **The hit test is a swept XZ segment against unit radii**, not a point-distance check and not Unity.Physics. At 25 m/s a bullet moves ~0.4 m per frame, so a point test would tunnel through units. The rock path moves onto the same test. | Recorded |
| RC-D8 | **Friendly hits make no `ThreatEntry`** (unchanged rule), so allies don't start infighting over a stray shot; the damage still lands. | Recorded |
| RC-D9 | **Firing is a `Shoot` module:** a ShootAwareness option at `Critical` (like Throw) when holding a gun with the target in [min, max]. ShootBehaviour runs `RequestShoot` → Wait(gun cooldown) (no face-target command yet, same gap as Throw). Keep Range reuses the gun's AI min/max range unchanged. | Recorded |
| RC-D10 | **Out of v1:** ammo, reload animation, arcs, piercing, homing, the player gun. | SH §8 |

---

**Skills Needed:**
- `dots-system-scaffold` — `ProjectileSystem` (§5)
- `dots-authoring-baker` — projectile prefab authoring (§4)
- `dots-unit-ai` — ranged attack options / awareness range curves (§7)
- `dots-test` — projectile math EditMode fixture

---

## 1. Purpose & v1 scope

Projectile-based attacks for units (and later the player): a `SpawnEntity` behavior command emits a pooled projectile entity; the projectile flies, hits, and feeds the **existing DamageBus** (source-agnostic `DamageEvent` — the hard half of ranged combat already shipped in DamageEvent v2). `ActionType`/`BehaviorType` already reserve `ProjectileSingle/Continuous` values.

**v1 handles:** straight-line projectile, single-target hit via proximity (the `ThrownItemHitSystem` XZ-distance pattern — no physics collider), one ranged unit archetype end-to-end.
**Out of v1:** arcs/ballistics, piercing, homing, player ranged weapon (player path exists via `PlayerAttackSystem` later). Reserve: `ProjectileBlob.arcHeight` field, unused in v1.

## 2. Architecture

```
BehaviorSO (ProjectileSingle) ── SpawnEntity command arm (new, in the split interpreter)
    └─ ECB-instantiates pooled projectile prefab + Projectile{ velocity, sourceEntity, damageSource, ttl }
         └─ ProjectileSystem (CombatExecutionSystemGroup) — moves, XZ-proximity hit test
              ├─ hit → Enqueue DamageEvent into DamageBus.raw (AddJobHandleForProducer — RULES in Systems.md §Combat)
              └─ hit or Lifetime expiry → Despawn funnel (Despawn_System's pool-vs-destroy)
```

- **Group placement:** `ProjectileSystem` in `CombatExecutionSystemGroup` (it is a DamageBus producer; the bus-reset ordering in `DamageBusSystem` already accounts for producers at this point in the frame). File lives in `CombatSystemGroup/CombatExecutionSystemGroup/` per the conformance rule.
- **Spawning:** the `SpawnEntity` arm resolves the projectile prefab from the attacker's resolved `AttackType` entry — extend `AttackLibraryBlob` entries with `projectileId` rather than a new library. ← DECISION below.

## 3. Entry points

- **Persistent:** `Projectile : IComponentData` on the projectile entity (not enableable — presence defines the archetype; pooling handles reuse via the Despawn system's `PoolOwner`).
- The *attack* enters through the existing spine: awareness → `StateMachine.action = ProjectileSingle` → behavior runs `SpawnEntity`.

## 4. Data model

**← DECISION:** projectile config home — (a) extend `AttackSO`/`AttackLibraryBlob` with projectile fields (speed, ttl, prefab id) — one library, attack-centric; or (b) new `ProjectileLibrary` via `dots-blob-library` — cleaner if projectiles get reused across attacks. *Recommendation: (a) — v1 has one projectile; a second library earns its five files only when projectiles decouple from attacks.*
Prefab: quad + `ProjectileAuthoring` (TransformUsageFlags.Dynamic) baked into the unit-prefab registry the spawner already uses (`UnitLibraryBlob`/`UnitPrefabEntry` pattern — reuse, don't invent).

## 5. Systems

- **New:** `CombatSystemGroup/CombatExecutionSystemGroup/ProjectileSystem.cs` — `IJobEntity.ScheduleParallel` over `Projectile` + `LocalTransform`: integrate velocity, XZ hit test against alive units (skip `sourceEntity`, skip-until-cleared like `ThrownItem.throwOrigin`'s 1.2-unit rule), enqueue `DamageEvent`, request despawn. Registers with `DamageBusSystem.AddJobHandleForProducer`.
- **Edited:** `Utils/BehaviorCommands/` combat family — the `SpawnEntity` arm (also un-flag it in `BehaviorCommandCatalog`).
- **New behaviors/assets:** `ProjectileAttackBehaviour.asset` (`Approach(to range) → PlayActionAnimation → SpawnEntity → WaitTime(cooldown) → LoopUntil(TargetDead|TargetLost|TargetOutOfRange)`), ranged `AttackSO` entries, awareness range consideration curves on the ranged action SO.

## 7. Integration points

DamageBus (producer #4 — the manual JobHandle wiring is the critical detail, copy `AttackRequestSystem`), Despawn funnel (`Lifetime` TTL), `AvailableAttack` resolution (order-time via MinionOrderRobustness, execution-time via `RequestAttack`), `AnimationSoundMarkerSystem` for fire SFX, [[Contracts]] row for `Projectile`.

## 9. Build phases

1. Projectile entity + `ProjectileSystem` flight/hit/despawn with a debug-spawned projectile (no behavior yet) → DamageBus applies damage.
2. `SpawnEntity` command arm + catalog un-flag → behavior-driven firing.
3. Ranged unit archetype: `AttackSO` + behavior asset + brain wiring → awareness picks ranged at range.
4. Minion attack orders on the ranged unit (proves the MinionOrderRobustness prerequisite).
5. Polish: fire SFX marker, muzzle offset, `arcHeight` reservation documented.

## 10. Verification

Phase 1: debug-spawn 50 projectiles → all hit or TTL-expire into the pool (Entities window: pooled count stable, no entity leak). Phase 3: ranged unit kites a melee zombie — fires at range, DamageBus log shows `Applied N`, victim threat-reacts (faction-gated). Frame check: producer-handle race would manifest as a safety exception on `raw` — one long soak in a 50-unit fight.

## Open decisions (collected)

None. All are settled in §0 (RC-D1–D10).
