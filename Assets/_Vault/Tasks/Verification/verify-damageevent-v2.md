---
title: Verify DamageEvent v2 (NativeQueue bus / source-agnostic DamageSource / AOE friendly-fire / spike hazard)
status: active
created: 2026-07-01
area: code
---

## Goal

Confirm the DamageEvent v2 refactor works end-to-end in `Assets/Scenes/TestArea/DOTSTestScene.unity`. All **code** is committed: the `AttackType → DamageSource` rename, the recycled `NativeQueue<DamageEvent>` bus (`DamageBusSystem` + `DamageBus`), the queue-based producers/consumer (`AttackRequestSystem`, `ThrownItemHitSystem`, `DamageEventSystem`), the AOE expand pass (`DamageResolutionSystem`), and the spike-hazard example (`HazardZone` + `HazardAuthoring` + `HazardZoneSystem`). `CombatEventCommandBufferSystem` was deleted.

The **compile gate was NOT run this session** (Unity MCP not connected) — the first step below is the real compile check. Spec: [`DamageEvent_v2_System.md`](DamageEvent_v2_System.md).

## Steps

### Compile + import (first — gate everything on this)
- [ ] Re-enter the Unity Editor; confirm **no compile errors** (`error CS####`) and **no Burst errors** (`BC####`). The `AttackType → DamageSource` rename touched ~23 files; a stray reference will surface here.
- [ ] Confirm **no duplicate-GUID warnings** — the new `.cs`/folder `.meta` GUIDs were hand-generated (`DamageBus.cs`, `DamageBusSystem.cs`, `DamageResolutionSystem.cs`, `Hazard.cs`, `HazardAuthoring.cs`, `HazardZoneSystem.cs`, `Authoring/Hazards` folder, this doc). If a collision is reported, delete that `.meta` and let Unity regenerate it, then re-commit.
- [ ] Systems window: `DamageBusSystem` is in `GameManagerSystemGroup` (OrderFirst); `HazardZoneSystem` + `AttackRequestSystem` in `CombatExecutionSystemGroup` (hazard before attack); `DamageResolutionSystem` before `DamageEventSystem` in `CombatReactionSystemGroup`. `CombatEventCommandBufferSystem` is **gone**.

### SO data preserved by the rename (no re-authoring)
- [ ] Open an `AttackSO` asset — the `damageSource` field still shows its old value (Punch/Slash/etc.), preserved by `[FormerlySerializedAs("attackType")]`. Same for `ItemSO.weaponAttack` / `UnitSO.attack` / `PlayerControllerAuthoring.defaultAttack` (field names unchanged; type-only).
- [ ] Re-bake (re-open the subscene or re-enter Play) — `AttackLibraryBakingSystem` builds the enum-indexed blob over the new `DamageSource` count (added `Fall`/`Hazard`/`Burn`/`Drown`); attacks still map correctly.

### Parity — melee + thrown (must match v1)
- [ ] Melee: two hostile units fight → damage ticks, death, ragdoll direction/feel unchanged; a hit citizen still fights back after the 0.3s flinch (`ThreatEntry` still produced for a hostile attacker).
- [ ] Throw a Rock at a unit → it damages / can kill, ragdoll plays, but **no** `ThreatEntry` is created (source is `Null`). Inspect the victim's `ThreatEntry` buffer.
- [ ] Confirm the `[DamageBus] Applied N damage event(s) this frame.` debug log appears on hit frames (Combat log category) — the recycled bus has no inspectable event entities.

### Friendly fire (AOE) — needs an AreaOfEffect source
- [ ] Give an attack (or a test hazard) `DamageBehaviour.AreaOfEffect` with a radius, trigger it near mixed allies + enemies → **everyone in radius except the source** takes damage (XZ radius; already-`Dead` skipped).
- [ ] Allies caught in the AOE are damaged but **do not** gain a `ThreatEntry` and do **not** turn on the caster (faction gate). Only units hostile to the source get threat.

### Spike hazard (environmental / sourceless)
- [ ] Add a GameObject to `DOTSTestScene` with `HazardAuthoring` (damageAmount / radius / retriggerInterval). Re-bake.
- [ ] Walk a unit onto the zone → periodic `Hazard` damage, throttled to once per `retriggerInterval` (whole-zone gate). It can kill; ragdoll plays tipping **away from the hazard** (hitSourceX = zone X); **no** `ThreatEntry`; the lethal capture shows `DamageSource.Hazard` on `Health.killDamageSource`.

### Dependency safety (highest-risk — do this deliberately)
- [ ] Run with the **Jobs Debugger / safety checks ON**. Focus-fire a target with several attackers + have a hazard active + throw an item in the same frames → confirm **no race / disposed-container / "container passed to job not registered" errors**. This validates the manual `AddJobHandleForProducer` / `ProducerHandle.Complete()` wiring — the one place a mistake is a runtime crash, not a compile error.
- [ ] Exit Play mode → confirm **no `NativeQueue`/`Persistent` leak warnings** (validates `DamageBusSystem.OnDestroy` disposal).

### Scale / churn proof (optional profiling)
- [ ] Profiler: confirm **no per-frame entity create/destroy** in combat anymore. `DamageEventSystem` only does work on hit frames; the `DamageExpandJob` shows as a parallel job only when events exist.
- [ ] ~50 attackers focus-firing → the consumer is the only main-thread combat cost; the melee producer stays parallel.

## Notes

Code files (this build):
- **New:** `Components/Combat/DamageBus.cs`, `Components/Combat/Hazard.cs`; `Systems/CombatSystemGroup/DamageBusSystem.cs`, `Systems/CombatSystemGroup/CombatReactionSystemGroup/DamageResolutionSystem.cs`, `Systems/CombatSystemGroup/CombatExecutionSystemGroup/HazardZoneSystem.cs`; `Authoring/Hazards/HazardAuthoring.cs`.
- **Deleted:** `Systems/CombatSystemGroup/CombatEventCommandBufferSystem.cs`.
- **Rewritten:** `Components/Combat/DamageEvent.cs` (plain value struct), `CombatReactionSystemGroup/DamageEventSystem.cs`, `CombatExecutionSystemGroup/AttackRequestSystem.cs`, `ThrownItemSystemGroup/ThrownItemHitSystem.cs`.
- **Rename ripple (`AttackType → DamageSource`):** `Data/Enums/AttackEnums.cs` + `AttackSO`/`ItemSO`/`UnitSO`/`AttackLibrarySO`, `AttackBlobs`/`ItemBlobs`/`UnitBlob`, `AttackLibraryBakingSystem`/`ItemLibraryBakingSystem`, `AttackComponents`/`PlayerEquipmentComponents`/`UnitComponents` (`killDamageSource`), `AIUtils`/`UnitBakingUtil`, `EnemyAwarenessSystem`/`BehaviorExecutionSystem`/`SwapBrainSystem`, `PlayerControllerAuthoring`.

Gotchas to watch:
- **Manual dependency is the whole ballgame.** `DamageBus`'s `NativeQueue`s bypass ECS auto-tracking. Producers register with `DamageBusSystem.AddJobHandleForProducer`; `DamageResolutionSystem` combines `ProducerHandle` and `Complete()`s before the main-thread drain, then completes its own expand job for the consumer. If a "container not registered / not disposed" error appears, this wiring is where to look.
- `DamageBusSystem` is **managed** (`SystemBase`, no `[BurstCompile]`) because it owns native containers + the handle. `DamageResolutionSystem.OnUpdate` and `AttackRequestSystem.OnUpdate` are **not** `[BurstCompile]` (they call `GetExistingSystemManaged<DamageBusSystem>()`); the heavy work is in the Burst jobs.
- Plan's `aoeCount` `NativeReference` was intentionally **dropped** (parallel increment is a race) — AOE presence is scanned from the drained `raw` array on the main thread. Don't re-add it as a producer-written field.
- `.meta` GUIDs hand-generated; a Rock/hazard model re-import with a new GUID is expected (that's your asset).

When everything passes: move this file to `Assets/_Vault/Tasks/Done/` and flip the spec status to ✔️ done.
