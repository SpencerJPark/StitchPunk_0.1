---
title: Utility AI Refactor V3 — Unified Buffer Pipeline
status: active
created: 2026-06-08
area: code
---

## Goal

Merge the old awareness-based AI with the v2 SO-driven utility scoring into one clean pipeline.
Keep awareness systems writing to `UtilityActions` buffer. Replace hard-coded scoring with blob
consideration curves. Replace per-action execution systems with generic `BehaviorExecutionSystem`.
Player-controlled units write to the same buffer (high priority).

## Architecture

```
UtilityAISystemGroup
  ├── AIMotivationSystemGroup
  └── AIAwarenessSystemGroup  (awareness systems → UtilityActions buffer with actionDefIndex)

MinionActionSelectionSystemGroup  (after UtilityAI, before StateMachine — player writes buffer)

StateMachineSystemGroup
  ├── ActionSelectionSystemGroup
  │   ├── ConsiderationScoringSystem  (scores buffer entries via blob curves)
  │   └── WinnerSelectionSystem       (priority tier + highest utility → StateMachine)
  └── ActionExecutionSystemGroup
      ├── ActionInterruptSystem
      └── BehaviorExecutionSystem     (walks BehaviorConfigBlob → emits requests)
```

## Steps

### Phase 1 — Group restructure + data cleanup ✅ DONE
- [x] Rename AIActionSelectionSystemGroup → UtilityAISystemGroup
- [x] Rename ActionSystemGroup → StateMachineSystemGroup
- [x] Reorder MinionActionSelectionSystemGroup (after UtilityAI, before StateMachine)
- [x] Remove PerceptionSystemGroup + UtilityDecisionSystemGroup declarations
- [x] Delete ActionInstancingSystem, ContextAssemblySystem, WanderPerceptionSystem, UtilityBrainV2Authoring
- [x] Stub ConsiderationScoringSystem + WinnerSelectionSystem (correct group, empty bodies)
- [x] Move BehaviorExecutionSystem to ActionExecutionSystemGroup
- [x] Add isPlayerOrdered to UtilityActions; remove OwnedAction
- [x] Add logging to BehaviorExecutionSystem (behavior complete event)

### Phase 2 — Scoring pipeline ✅ DONE
- [x] Add NeedType + PersonalityTypes to ConsiderationBlob + baking
- [x] Add ConsiderationAuthoring fields (needType, traitType) to UtilityActionSO
- [x] BrainBlobUtils.GetActionDefIndex helper
- [x] Fix compile errors in awareness/motivation systems
- [x] Set UtilityBrain.unitType in UnitBakingUtil
- [x] Implement ConsiderationScoringSystem (inline context, blob curves, compensation factor)
- [x] Implement WinnerSelectionSystem (player-order override, priority tier, utility sort, logs)
- [x] Update awareness systems to set actionDefIndex (Enemy, Flee, Navigation, Interaction)

### Immediate Fix — EquipAction → PickupRequest ✅ DONE
- [x] Add PickupRequest + DropRequest to ItemComponents.cs
- [x] Update ItemEquipSystem, PlayerPickupSystem, ItemAuthoring to use PickupRequest

### Phase 3 — BehaviorSO migration (per domain)
- [x] Extend BehaviorCommandType: RequestAttack, RequestPickup, ModifyMotivation
- [x] Extend BehaviorType: MeleeSwing, Flee, Sit, Pickup, Talk
- [x] BehaviorExecutionSystem handles all new command types + full lookup set for pickup flow
- [ ] **IN UNITY EDITOR** — author BehaviorSO assets for each domain:
  - Wander: confirm existing WanderBehaviorSO asset + NavigationAwarenessSystem works end-to-end
  - MeleeSwing: Approach (stoppingDist=melee range, stance=Run) → RequestAttack
  - Flee: Approach (away from threat — needs custom approach direction logic, see notes)
  - Sit: Approach → WaitTime (Duration = sit length) → ModifyMotivation (Energy/Fun)
  - Pickup: Approach (stoppingDist=pickup range) → RequestPickup
  - Talk: Approach → WaitTime → ModifyMotivation (Social)
- [ ] Author UtilityActionSO assets + add to BrainSO for each unit type
- [ ] Wire each BrainSO into BrainLibrarySO
- [ ] Retire old execution systems once BehaviorSOs cover each domain

### Phase 4 — Player unit wiring ✅ DONE
- [x] MinionActionSelectionSystem — new system in MinionActionSelectionSystemGroup
- [x] Handles: OnMinionAttackCommand → MeleeSingle, OnMinionInteractCommand → Interact, OnMinionFollowCommand → Wander(player entity)
- [x] All entries written with isPlayerOrdered=true — WinnerSelectionSystem picks unconditionally
- [x] Uses ComponentLookup for each command so missing commands are silently skipped
- [ ] **TODO: OnMinionMoveCommand** (move-to-position) — needs float3 targetPosition added to StateMachine + UtilityActions; skipped for now
- [ ] Verify: enable OnMinionAttackCommand on a minion → StateMachine.action = MeleeSingle this frame

### Phase 5 — Cleanup ✅ DONE
- [x] Deleted: MotivationScoringSystem, ActionPrioritySystem, PersonalityContextSystem
- [x] Deleted: SelectionFunctions.cs, ActionSelectionSystem.cs, ActionInterruptSystem.cs
- [x] Deleted: ScoringLibraryBakingSystem, AIScoringLibraryAuthoring, AIScoringBlob.cs
- [x] Deleted: ConsiderationLibrarySO.cs, ConsiderationCurveSO.cs
- [x] Deleted: empty ScoringSystemGroup directory
- [x] Removed AIScoringSystemGroup from SystemGroups.cs + AIAwarenessSystemGroup UpdateBefore ref
- [x] Removed ScoringLibrary + ScoringLibraryReference from EntityLibraries.cs
- [x] Removed EvaluateScoringCurve from AIUtils.cs
- [ ] Profile: confirm zero per-frame structural changes in AI pipeline (verify in Unity Profiler)

## Key Files

| File | Role |
|---|---|
| `_Scripts/Systems/SystemGroups.cs` | Group declarations + order |
| `_Scripts/Components/AI/UtilityAiComponents.cs` | UtilityActions, StateMachine |
| `_Scripts/Data/Structs/AIConfigBlobs.cs` | ConsiderationBlob (needType, traitType) |
| `_Scripts/Data/SOs/UtilityActionSO.cs` | ConsiderationAuthoring SO |
| `_Scripts/Data/Enums/AiEnums.cs` | BehaviorCommandType, BehaviorType |
| `_Scripts/Systems/PostBakingSystemGroup/BrainLibraryBakingSystem.cs` | Brain blob baking |
| `_Scripts/Systems/PostBakingSystemGroup/BehaviorLibraryBakingSystem.cs` | Behavior blob baking |
| `_Scripts/Utils/BrainBlobUtils.cs` | GetActionDefIndex helper |
| `_Scripts/Systems/UtilityAISystemGroup/UtilityDecisionSystemGroup/ConsiderationScoringSystem.cs` | Scores buffer |
| `_Scripts/Systems/UtilityAISystemGroup/UtilityDecisionSystemGroup/WinnerSelectionSystem.cs` | Picks winner |
| `_Scripts/Systems/StateMachineSystemGroup/ActionExecutionSystemGroup/BehaviorExecutionSystem.cs` | Executes behaviors |
| `_Scripts/Data/SOs/BehaviorSO.cs` | Command sequence SO |

## Notes

- Requests (PathRequest, AttackRequest, PickupRequest) are the API boundary between StateMachine and downstream systems — StateMachine emits them, downstream systems respond
- SelfDefenceAwarenessSystem stubbed — Phase 3 should re-implement interrupt logic with UtilityBrain
- SocialAwarenessSystem stubbed — Phase 3 migration target
- PickupRequest lives on the item entity (not unit), enabled by callers, consumed by ItemEquipSystem
- UtilityBrain.unitType now populated from UnitSO.unitType in UnitBakingUtil
