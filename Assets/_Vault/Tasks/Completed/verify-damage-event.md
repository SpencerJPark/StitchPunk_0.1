---
title: Verify DamageEvent System (Hurt-buffer → signal-entity refactor)
status: active
created: 2026-07-01
area: code
---

## Goal

Confirm the attack/damage refactor works end-to-end with **exact parity** to the old
`Hurt`-buffer path in `Assets/Scenes/TestArea/DOTSTestScene.unity`. All code is committed: the
per-unit `Hurt` buffer is gone; melee (`AttackRequestSystem`) and thrown items
(`ThrownItemHitSystem`) now spawn one-frame `DamageEvent` entities into
`CombatEventCommandBufferSystem`'s ECB; a single `DamageEventSystem` drains them
(damage + threat + death + killing-blow knockback), then `DestroyEntity(query)`.

**This is a baking change** (`HealthAuthoring` no longer adds `AddBuffer<Hurt>`) — the scene
**must be re-baked** before any of the checks below are meaningful.

Spec: [`../Completed/DamageEvent_System.md`](../Completed/DamageEvent_System.md).

## Steps

### Compile + rebake (first)
- [x] Re-enter the Unity Editor; confirm **no compile errors** (`error CS####` / Burst `BC####`). *(user confirmed compiled + working)*
- [ ] Re-bake `DOTSTestScene` (re-open subscene or re-enter Play) so baked units drop the `Hurt` buffer.
- [ ] Systems window: `DamageEventSystem` sits in `CombatReactionSystemGroup`; `CombatEventCommandBufferSystem` sits in `CombatSystemGroup` after `CombatExecutionSystemGroup` / before `CombatReactionSystemGroup`; `ThreatUpdateSystem` and `DamageApplicationSystem` are **gone**.

### Phase 3 — melee parity
- [ ] Two combatants fight → damage ticks, health reaches 0, unit dies.
- [ ] Ragdoll launches in the correct direction with the same knockback feel as before (death-only knockback, `Health.kill*` captured on the lethal event — see [[project_combat_knockback]]).
- [ ] A damaged citizen still fights back / flees: threat-driven `SelfDefenceAwarenessSystem` fires after the 0.3s flinch (`ThreatEntry` populated with identical `REACTION_TIME` / `THREAT_TTL` constants — see [[project_bravery_system]]).

### Phase 4 — thrown parity
- [ ] Throw a Rock at a unit → it damages and can kill (ragdoll plays).
- [ ] Thrown kill adds **no** `ThreatEntry` (`attackerEntity == Entity.Null` is skipped) — matches old behavior.

### Decoupling proof
- [ ] Entities inspector: `DamageEvent` entities appear only on hit frames and vanish the same frame — none leak.
- [ ] Profiler: `DamageEventSystem` only does work on hit frames (no per-unit scan); `AttackRequestSystem` now runs as a **parallel** job.
- [ ] No-Hurt proof: no `Hurt` buffer on any baked unit; project compiles with the struct deleted.

## Notes

Code files (committed this round):
- **New:** `Assets/_Scripts/Components/Combat/DamageEvent.cs`; `Systems/CombatSystemGroup/CombatEventCommandBufferSystem.cs`; `Systems/CombatSystemGroup/CombatReactionSystemGroup/DamageEventSystem.cs`.
- **Edited:** `AttackRequestSystem.cs` (spawn `DamageEvent`, `ScheduleParallel`, drop `Hurt` lookup); `ThrownItemHitSystem.cs` (spawn `DamageEvent`, filter targets by `Health`); `UnitComponents.cs` (delete `Hurt` struct); `HealthAuthoring.cs` (remove `AddBuffer<Hurt>`).
- **Deleted:** `ThreatUpdateSystem.cs`, `DamageApplicationSystem.cs` (logic folded into `DamageEventSystem`).

Gotchas to watch:
- `DamageEventSystem` is a Burst **main-thread** loop (`SystemAPI.Query<RefRO<DamageEvent>>()`) — the lookups write arbitrary victims, so it's single-threaded by nature (small N, LoggingSystem precedent). Do **not** jobify it into a `ScheduleParallel` write.
- Same-frame timing is preserved by the dedicated `CombatEventCommandBufferSystem` ECB playing back **between** attack execution and damage reaction. Thrown items record earlier in the frame (ItemSystemGroup) into the same ECB — fine; playback is at the combat ECB system.
- Already-`Dead` victims are skipped inside the loop to avoid double-kill when two events land the same frame.
- v1 resolves `DamageBehaviour.SinlgeTarget` only; `damageBehaviour` / `sourcePosition` / `range` are carried but unused (AOE-ready, no future schema change).

When everything passes: flip the spec status to ✔️ done and move this file to `Assets/_Vault/Tasks/Done/` (or leave alongside the completed spec per current convention).
