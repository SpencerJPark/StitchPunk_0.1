---
title: Verify Minion Revival & Life-State System (Alive deprecation + revive→PlayerZombie minion)
status: active
created: 2026-06-14
area: code
---

## Goal

Confirm in `Assets/Scenes/TestArea/DOTSTestScene.unity` that (1) `Alive` is fully deprecated — `Dead` is the
sole life-state — and (2) reviving a corpse produces a controllable `PlayerZombie` minion via the
`SwapBrainRequest` hook. **Phases 1–4 (all C#) are committed.** The conversion is **inert until the
Editor-only assets below exist** (a `PlayerZombie` `UnitSO` + per-human `becomesUnitType` + revivable
prefabs wired for player control) — none of which can be created outside Unity. Phase 5 (persistence
round-trip) is the last gate and may surface a restore-hook follow-up.

Spec: [`MinionRevival_System.md`](MinionRevival_System.md).

## Steps

### Compile + import (first)
- [ ] Re-enter the Unity Editor; confirm **no compile errors**. (`Alive` is deleted — any lingering
      reference would fail here.)
- [ ] Confirm **no duplicate-GUID warnings** — `SwapBrainSystem.cs.meta` (`988b7df8c9af45718829832405b85f92`)
      and `verify-minion-revival.md.meta` GUIDs were hand-generated. On a collision, delete the `.meta`,
      let Unity regenerate, re-commit.
- [ ] Systems window: `SwapBrainSystem` is in `HealthSystemGroup`, ordered **after** `ReviveRequestSystem`.

### Editor assets (one-time setup — code can't create these)
- [ ] Author a `PlayerZombie` `UnitSO` (Units/Unit): faction = Player/Undead form, `attackFactions` = the
      human factions, `attacks` = the bite, motivations as desired. Add it to `_UnitLibrary`.
- [ ] On each revivable human `UnitSO` (e.g. `MaleCitizen`/`FemaleCitizen`): set
      `becomesUnitType = PlayerZombie`. **Confirm none meant to convert are left at `None`.**
- [ ] On each revivable human **prefab**: add `MinionAuthoring` + `UndeadAuthoring`, and set the
      `UnitSO.canBePlayerControlled = true` (so `Minion`/`Selected`/`PlayerUnitBrain`/`OnMinion*Command`
      bake **disabled**, ready to enable on revive).

### Phase 1 — Alive deprecation
- [ ] Kill a unit (ragdoll plays). In the Entities window: `Dead` **enabled**, and **no `Alive`
      component** exists anywhere.
- [ ] Revive with the existing reviver: `Dead` **disabled**, the unit animates again.
- [ ] No console errors; no double-processing of death — the `UnitAction.current == ActionType.Death`
      latch holds (DamageApplication still enables `Dead` at health ≤ 0).

### Phase 3 — SwapBrainSystem in isolation
- [ ] On a **live** citizen, set `SwapBrainRequest.newUnit = PlayerZombie` and **enable** it in the
      inspector. Next frame: `SwapBrainRequest` **disabled**;
      `UnitData.unitType` / `UtilityBrain.unitType = PlayerZombie`; `Faction` = the zombie faction;
      `AttackFaction` lists humans; `AvailableAttack` lists the bite; `Motivation` refilled
      (zero-decay is expected).

### Phase 4 — Revive → conversion
- [ ] Equip the reviver, target a corpse, revive. The corpse rises as a `PlayerZombie`:
      `Minion` **enabled** (box-select highlights it), `Undead` **enabled**, autonomous until commanded.
- [ ] Right-click a human → the zombie paths in and bites it (faction targeting flipped — the
      `FactionRegistry` is rebuilt per-frame, so the swapped `Faction` takes effect next frame).
- [ ] Right-click ground → it moves (existing minion pipeline, unchanged).
- [ ] **Re-kill** the reanimated zombie → it ragdolls again (the death latch was re-armed by revive's
      `UnitAction.current = Idle` reset).

### Phase 5 — Persistence round-trip (last gate)
- [ ] Auto/manual save with a revived zombie minion alive → reload.
- [ ] **Caveat to watch:** restore re-instantiates a body prefab keyed on `record.unitType` (=
      `PlayerZombie`). If **no `PlayerZombie` body prefab is registered in the spawn pool**, the zombie
      is **dropped on load** (`GetBodyPrefabForType → Entity.Null → continue`). If that happens, build the
      reserved restore hook: either register a `PlayerZombie` prefab in the pool, **or** re-stamp
      `SwapBrainRequest` on restore so combat data re-derives. (Reserved §4/§7 hook — build only if this step fails.)
- [ ] If it loads: confirm it returns as a zombie minion with correct `Faction`/`AttackFaction`/
      `AvailableAttack` (not stale citizen data).

## Notes

Code committed this round:
- **New:** `Assets/_Scripts/Systems/HealthSystemGroup/SwapBrainSystem.cs` (+ `.meta`).
- **Deleted:** `struct Alive` (`Components/Units/UnitComponents.cs`).
- **Edited:** `HealthAuthoring.cs` (no `Alive`), `DeathSystem.cs` (latch → `UnitAction.current == Death`),
  `ReviveRequestSystem.cs` (`[WithAll(Dead)]`; stamp/enable `SwapBrainRequest`; enable `Minion`; reset
  `UnitAction`), `AttackRequestSystem.cs` (`Dead` lookup), `SpawnStateInitSystem.cs` (no `Alive`),
  `UnitSelectionManager.cs` (`isHostile` via `Dead`), `UnitSO.cs` + `UnitBlob.cs` +
  `UnitLibraryBakingSystem.cs` (`becomesUnitType`), comment-only updates in `Ragdoll2DReviveSystem.cs` /
  `BillboardSystem.cs` + commented-`Alive` cleanup in the four minion/player systems.

Gotchas to watch:
- `SwapBrainSystem` is `ScheduleParallel`; it consumes `SwapBrainRequest` via an
  `EndSimulation` ECB (deferred) but enables `ActionInterruptRequest` immediately. Revive enables the
  request **immediately** (ComponentLookup), so the same-frame `[UpdateAfter]` swap sees it.
- A swap only runs while `UtilityBrain` is **enabled** (revive re-enables it first). Stamping
  `SwapBrainRequest` on a unit with a disabled brain is a no-op.
- Rebuilt motivations are **zero-decay** by design (the blob carries no `decayRate`).

When everything passes: move this file to `Assets/_Vault/Tasks/Done/` and flip the spec status to ✔️ done.
