---
title: Verify — Despawn System
status: active
created: 2026-09-15
area: code
---

## Goal

Confirm the central `Despawn` funnel works in a scene: `LifetimeSystem` enables `Despawn` when a TTL runs out, and
`DespawnSystem` (after `UnitPoolReturnSystem` in `DespawnSystemGroup`) pools a `PoolOwner` unit under a per-`UnitType`
cap of 64 or destroys everything else. Built 2026-09-15 in the A102 / Despawn / Minion Orders parallel batch (spec:
[`Despawn_System.md`](Despawn_System.md), §12.4 log). Proven only by `StitchPunk.Tests.PlayMode.DespawnSystemTests`
(3 tests, revert-to-fail each); nobody has watched it in a scene.

**Before anything: rebake.** `UnitAuthoring.Baker` now bakes `Despawn` (disabled) on every unit, and `Lifetime` is new.
Until a rebake the baked subscenes lack `Despawn` and `DespawnSystem` finds nothing. Reopen the subscene or re-enter Play.

## Play checks (`DOTSTestScene`, Entities Hierarchy + Inspector open)

- [ ] **TTL destroy.** Add a cube with `LifetimeAuthoring` (`seconds = 3`) to the subscene. Play: it disappears about 3 s
  in and the Entities window shows it gone; the Inspector shows `Despawn` flip enabled the frame before.
- [ ] **Pool path.** Toggle `Despawn` on a spawned unit (it has `PoolOwner`) in the Inspector. It gains `Disabled` — not
  destroyed — with `Despawn` back off, and a `UnitSpawner` later reclaims it (`NewlySpawned` re-enabled, repositioned).
- [ ] **Cap.** Walk far from a crowd so units distance-pool: at most 64 dormant units per `UnitType` remain (Entities
  window count); the rest are destroyed.
- [ ] **Look (DS-D5).** Pooling disables the unit root only, like `UnitPoolReturnSystem`. Do a dormant unit's body parts
  still render or simulate? If yes, the follow-up is disabling the whole `LinkedEntityGroup` (`Gotchas.md` → Spawning).
- [ ] **Reclaim edge.** A unit distance-pooled on the same frame its `Despawn` was enabled keeps the bit while dormant and
  would vanish again the frame it is reclaimed. If you ever see a reclaimed unit blink out, add `Despawn` → off to
  `SpawnStateInitSystem` (recorded in `Gotchas.md`).

Success: entity counts move exactly as above, no entity keeps `Despawn` enabled past one frame, no dormant units beyond
the cap.
