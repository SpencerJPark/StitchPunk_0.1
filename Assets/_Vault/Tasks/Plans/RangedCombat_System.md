# Ranged / Projectile Combat System — Design Spec

> **Status:** ✅ spec ready · edit the inline **← DECISION** markers, then hand back to start the build.
> **Raw source:** [`../Claude/Code_Audit_2026-07.md`](../Claude/Code_Audit_2026-07.md) item #8 — last unbuilt phase of the behavior-recreation queue; currently **zero code**.
> **Prerequisites (hard):** [Despawn_System.md](Despawn_System.md) built (projectile pooling), [BehaviorCommandSplit_System.md](BehaviorCommandSplit_System.md) done (don't inflate the monolith), [MinionOrderRobustness_System.md](MinionOrderRobustness_System.md) (ordering ranged minions), BehaviorBakeValidation catalog (SpawnEntity gets un-flagged when its arm lands).

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

- [ ] §4 — projectile config in AttackLibrary (recommended) vs new ProjectileLibrary.
- [ ] §2/§5 — hit test: XZ proximity (recommended, ThrownItem precedent) vs Unity.Physics raycast.
- [ ] §5 — friendly fire for projectiles: inherit AOE's no-faction-filter stance vs faction-gate at the hit test (recommend matching AOE: no filter; threat stays faction-gated downstream anyway).
