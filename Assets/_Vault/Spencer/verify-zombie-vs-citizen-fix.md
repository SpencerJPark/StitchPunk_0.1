---
title: Verify Zombie vs Citizen Fight After AI Ordering Fix
status: active
created: 2026-04-17
area: code
---

## Goal

Confirm the zombie-vs-citizen fight in `Assets/Scenes/TestArea/DOTSTestScene.unity` works end-to-end after the three iterative AI fixes:

1. `BrainUtil.BakeRequirements` now bakes `AggressiveState` (so awareness systems' `[WithPresent]` filters match).
2. `[WithDisabled(PlayerControlled)]` → `[WithAll(ActiveBrain)]` in `CombatMoveJob`, `CombatAttackJob`, `CombatAbandonJob`, `MoveToTargetJob`.
3. Dropped the contradictory `[UpdateAfter(typeof(MoveToTargetSystem))]` from `CombatAttackExecutionSystem`, and added `lastKnownTargetPos = float3.zero` reset in `CombatMoveJob`.

The third fix is the one being verified here. Diagnostic plan that produced it: `C:/Users/spenc/.claude/plans/right-now-the-npcs-velvet-volcano.md`.

## Steps

### Smoke test the fix
- [ ] Re-enter the Unity Editor so systems recompile, open `Assets/Scenes/TestArea/DOTSTestScene.unity`, hit Play
- [ ] Select the zombie (`TestRotter`) in the Entities Hierarchy
  - [ ] `CombatTarget` enabled, `targetEntity` = citizen
  - [ ] `MoveToTargetRequest` enabled, `arrivalRange ≈ 1.5`
  - [ ] `PathRequest.enabled` pulses true on the same frame `MoveToTargetRequest` enables, then back to false next frame (was never pulsing before this fix)
  - [ ] `Movement.targetPosition` is no longer the zombie's spawn position — first the citizen position, then a grid waypoint
  - [ ] `LocalTransform.Position.x` advances from `1.65` toward `5.17`
- [ ] On arrival (`~1.5` units away), `ArrivedAtTarget` enables, `Attack` enables, citizen's Health drops

### Verify retaliation chain
- [ ] Select the citizen entity once it's been hit
  - [ ] `ThreatEntry` buffer has an entry with `attackerEntity` = zombie
  - [ ] `FightOrFlightSystem` flips `SelfDefence.contextMultiplier` to `3.0`
  - [ ] `CombatTarget` enables on citizen pointing at zombie
  - [ ] Same `MoveToTargetRequest` → `PathRequest` → walk → `Attack` chain runs in reverse on the citizen
- [ ] Confirm both units engage and damage trades back and forth

### Regression — make sure player-controlled units still work
- [ ] Take direct control of a citizen (`SwapBrainRequest`) and right-click somewhere
  - [ ] Citizen still receives `MoveToTargetRequest`-driven motion via the player path
  - [ ] No console warnings about system ordering cycles in `AIExecutionSystemGroup`

## Notes

Key files touched in this round:
- `Assets/_Scripts/Systems/AISystemGroup/AIExecutionSystemGroup/CombatAttackExecutionSystem.cs`
  - Removed `[UpdateAfter(typeof(MoveToTargetSystem))]` from class attribute
  - Added `moveRequest.lastKnownTargetPos = float3.zero;` inside `CombatMoveJob.Execute`
- `Assets/_Scripts/Utils/BrainUtil.cs` — `AggressiveState` bake (prior fix, leave alone)
- `Assets/_Scripts/Systems/AISystemGroup/AIExecutionSystemGroup/MoveToTargetSystem.cs` — `[WithAll(ActiveBrain)]` filter (prior fix)

If `PathRequest` still never pulses after this:
- Re-check that `MoveToTargetSystem` is actually scheduled (Systems window)
- Confirm `ActiveBrain` is enabled on the zombie at runtime (it must be — `CombatMoveJob` requires it and ran)
- Check for any other `UpdateAfter(MoveToTargetSystem)` lingering on systems in `AIExecutionSystemGroup` (`InteractionExecutionSystem`, `MinionAttackExecutionSystem`, etc.) — none currently, but if added later they'll re-create the cycle

If everything works:
- Move this file to `Assets/_Vault/Tasks/Done/`
- Consider the broader "should `MoveToTargetSystem` move out of `AIExecutionSystemGroup` into its own movement-prep group?" design question — it's currently the only system in the AI group whose output feeds the movement pipeline rather than other AI systems
