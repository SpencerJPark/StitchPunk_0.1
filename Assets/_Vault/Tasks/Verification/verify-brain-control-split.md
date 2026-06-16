---
title: Verify Brain Control Split (UtilityBrain=decision / StateMachine=execution; death blank-slate, player-controlled revive, minion self-defence)
status: active
created: 2026-06-15
area: code
---

## Goal

Confirm in `Assets/Scenes/TestArea/DOTSTestScene.unity` that the AI is split into two independently
gated halves — **UtilityBrain = decision/awareness ("utility AI")**, **StateMachine = execution** —
and that:
1. Disabling `UtilityBrain` turns off **only** autonomous decisions; the StateMachine half keeps
   running (death still ragdolls/tears down).
2. **Death** is a blank slate: `UtilityBrain` disabled, `ThreatEntry` + `MotivationChangeRequest` +
   `RecentInteraction` cleared, `AttackRequest` reset.
3. **Revive** hands the corpse to the player: `PlayerUnitBrain` + `Minion` enabled, `UtilityBrain`
   left disabled. A corpse with `becomesUnitType == None` is **not revivable**.
4. A revived minion **self-defends only when uncommanded** (new `MinionSelfDefenceAwarenessSystem`).

Spec: [`BrainControlSplit_System.md`](BrainControlSplit_System.md). Builds on
[`MinionRevival_System.md`](MinionRevival_System.md) / [`verify-minion-revival.md`](verify-minion-revival.md).

## Steps

### Compile + import (first)
- [ ] Re-enter the Unity Editor; confirm **no compile errors**. (Touched: the four regated systems,
      `MinionActionSelectionSystem`, `DeathSystem`, `ReviveRequestSystem`, `SwapBrainSystem`, and the
      new `MinionSelfDefenceAwarenessSystem`.)
- [ ] Confirm **no duplicate-GUID warnings**. The new script's `.meta` GUID was hand-generated:
      `MinionSelfDefenceAwarenessSystem.cs.meta` (`ee266e5edfea4b8696dda70df68ceec7`). On a
      collision, delete the `.meta`, let Unity regenerate, re-commit. (The two vault `.md` metas
      were Unity-generated — no collision risk.)
- [ ] Systems window: `MinionSelfDefenceAwarenessSystem` is in `MinionActionSelectionSystemGroup`;
      `BehaviorExecutionSystem` / `BehaviorInterruptSystem` / `WinnerSelectionSystem` /
      `ClearOptionsSystem` show `WithPresent(UtilityBrain)` queries (no longer `WithAll`).

### Phase 1 — execution decoupled (no regression on living units)
- [ ] Spawn ordinary citizens (UtilityBrain **enabled**). Confirm behavior is **identical to before**:
      they wander, talk, fight back, flee — the regate is a no-op while `UtilityBrain` is enabled.

### Phase 2 — death blank-slate
- [ ] Get a unit into combat so it has `ThreatEntry` entries, then kill it. In the Entities window on
      the corpse: `UtilityBrain` **disabled**; `ThreatEntry`, `MotivationChangeRequest`,
      `RecentInteraction` buffers **empty**; `AttackRequest` **disabled** with zeroed fields.
- [ ] The corpse still **ragdolls / plays its death animation** — proves the StateMachine/execution
      half runs with `UtilityBrain` off (the death interrupt + death behavior execute).
- [ ] No console errors; the `UnitAction.current == Death` latch still holds (no per-frame re-trigger).

### Phase 3 — player-controlled revive
- [ ] Editor setup (code can't create these): a `PlayerZombie` `UnitSO` in `_UnitLibrary`; each
      revivable human `UnitSO` has `becomesUnitType = PlayerZombie`; revivable prefabs have
      `MinionAuthoring` + `UndeadAuthoring` and `canBePlayerControlled = true`.
- [ ] Revive a convertible corpse → it rises as a `PlayerZombie`: `Minion` **enabled** (box-select
      highlights it), `PlayerUnitBrain` **enabled**, `UtilityBrain` **disabled**;
      `Faction`/`AttackFaction`/`AvailableAttack` rebuilt to the zombie form (SwapBrain ran while the
      brain was disabled).
- [ ] Right-click a human → the minion paths in and bites. Right-click ground → it moves.
- [ ] **Re-kill** the minion → it ragdolls again (death latch re-armed by revive's `UnitAction = Idle`).
- [ ] Author a corpse whose `UnitSO.becomesUnitType == None`, kill it, attempt revive → **nothing
      happens** (the `ReviveRequest` is consumed, the unit stays `Dead`).

### Phase 4 — minion self-defence (only when uncommanded)
- [ ] Leave a revived minion **uncommanded** next to a hostile that attacks it → after the ~0.3s
      flinch it fights back (look for `[MinionSelfDefence]` AI logs; emits at priority 3).
- [ ] While it is self-defending, **issue a move order** to empty ground → it breaks off and obeys
      (player order at `int.MaxValue` preempts the priority-3 self-defence).
- [ ] While it is executing a player **move** order through a hostile, confirm it does **not** stop to
      fight (the order outranks self-defence until it completes), then self-defends once idle again.

### Phase 5 — persistence round-trip
- [ ] Save with a revived zombie minion alive → reload. Confirm it returns as a `PlayerZombie` minion
      with `PlayerUnitBrain` enabled, `UtilityBrain` disabled, and correct combat data (see the
      `verify-minion-revival.md` Phase 5 caveat about a registered `PlayerZombie` body prefab).

## Notes

This environment cannot compile, Burst-compile, or run Unity — every step above is an Editor/Play-mode
check the build could not perform. Verified statically against the in-repo patterns only.

Code landed this round:
- **New:** `Assets/_Scripts/Systems/MinionActionSelectionSystemGroup/MinionSelfDefenceAwarenessSystem.cs` (+ `.meta`).
- **Regated `WithAll(UtilityBrain)` → `WithPresent(UtilityBrain)`:** `BehaviorExecutionSystem`,
  `BehaviorInterruptSystem`, `WinnerSelectionSystem`, `ClearOptionsSystem` (execution + shared
  selection/clear must run with the brain disabled).
- **Edited:** `MinionActionSelectionSystem` (gate `PlayerUnitBrain` + `WithPresent(UtilityBrain)`),
  `DeathSystem` (disable `UtilityBrain`, clear `ThreatEntry`/`MotivationChangeRequest`/`RecentInteraction`,
  reset `AttackRequest`), `ReviveRequestSystem` (bail on `becomes == None`; enable
  `PlayerUnitBrain`+`Minion`, never re-enable `UtilityBrain`), `SwapBrainSystem` (`WithPresent(UtilityBrain)`).

Gotchas to watch:
- `WithPresent(UtilityBrain)` is what lets execution/selection run while the brain is disabled **and**
  keeps `in/ref UtilityBrain` readable. If a regated system stops processing corpses/minions, check
  that attribute survived.
- The kept-`WithAll(UtilityBrain)` decision systems (`ConsiderationScoring`, `SelfDefence`/`Flee`
  awareness, `MotivationChangeRequest`) deliberately **do not** run on minions — that is the point.
- `MotivationChangeRequest` is not drained for minions (system stays `UtilityBrain`-gated); benign for
  zero-decay zombies and cleared on death. See the spec's Known limitations.

When everything passes: move this file + `BrainControlSplit_System.md` to `Assets/_Vault/Tasks/Done/`
and flip the spec status to ✔️ done.
