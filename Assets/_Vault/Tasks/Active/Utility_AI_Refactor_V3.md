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

See full plan: `~/.claude/plans/we-need-to-come-humble-biscuit.md`

## Target Architecture

```
UtilityAISystemGroup
  ├── AIMotivationSystemGroup
  └── AIAwarenessSystemGroup (awareness systems → UtilityActions buffer with actionDefIndex)

MinionActionSelectionSystemGroup (after UtilityAI, before StateMachine — player writes buffer)

StateMachineSystemGroup
  ├── ActionSelectionSystemGroup
  │   ├── ConsiderationScoringSystem (scores buffer entries via blob curves)
  │   └── WinnerSelectionSystem     (picks winner → writes StateMachine)
  └── ActionExecutionSystemGroup
      ├── ActionInterruptSystem
      └── BehaviorExecutionSystem   (walks BehaviorConfigBlob → emits requests)
```

## Steps

### Phase 1 — Group restructure + data cleanup ✅ DONE
- [x] Rename AIActionSelectionSystemGroup → UtilityAISystemGroup in SystemGroups.cs
- [x] Rename ActionSystemGroup → StateMachineSystemGroup in SystemGroups.cs
- [x] Reorder MinionActionSelectionSystemGroup (after UtilityAI, before StateMachine)
- [x] Remove PerceptionSystemGroup + UtilityDecisionSystemGroup declarations
- [x] Delete ActionInstancingSystem, ContextAssemblySystem, WanderPerceptionSystem, UtilityBrainV2Authoring
- [x] Stub ConsiderationScoringSystem + WinnerSelectionSystem (correct group, empty bodies)
- [x] Move BehaviorExecutionSystem to ActionExecutionSystemGroup
- [x] Add isPlayerOrdered to UtilityActions struct
- [x] Remove OwnedAction from EntityLibraries
- [x] Add logging to BehaviorExecutionSystem (behavior complete event)

### Phase 2 — Scoring pipeline refactored to read buffer ✅ DONE
- [x] Add NeedType needType + PersonalityTypes traitType to ConsiderationBlob (AIConfigBlobs.cs)
- [x] Add needType + traitType to ConsiderationAuthoring (UtilityActionSO.cs)
- [x] Update BrainLibraryBakingSystem to bake new fields
- [x] Add BrainBlobUtils.GetActionDefIndex helper (Assets/_Scripts/Utils/BrainBlobUtils.cs)
- [x] Fix compile errors: InteractionAwarenessSystem, FleeAwarenessSystem, NavigationAwarenessSystem, SelfDefenceAwarenessSystem (stub), SocialAwarenessSystem (stub), MotivationScoringSystem (stub), PersonalityContextSystem (stub), MotivationChangeRequestSystem
- [x] Implement ConsiderationScoringSystem (iterate UtilityActions buffer, inline context, blob curves)
- [x] Implement WinnerSelectionSystem (priority tier + highest utility → write StateMachine + activeBehavior; logs winner)
- [x] Update EnemyAwarenessSystem, FleeAwarenessSystem, NavigationAwarenessSystem, InteractionAwarenessSystem: set actionDefIndex via BrainBlobUtils
- [x] Set UtilityBrain.unitType in UnitBakingUtil (was never populated before)
- [ ] Verify: unit with UtilityBrain shows non-zero totalUtility in Entities Inspector after play

### Phase 3 — BehaviorSO migration (per domain)
- [ ] Extend BehaviorCommandType: RequestPath, RequestAttack, RequestPickup (one per API boundary)
- [ ] Update BehaviorExecutionSystem switch-case for new command types + logging
- [ ] Author WanderBehaviorSO + confirm Wander end-to-end
- [ ] Author MeleeSwingBehaviorSO (Approach → RequestAttack); retire MeleeSingleActionSystem
- [ ] Author FleeBehaviorSO; retire FleeActionSystem
- [ ] Author SitBehaviorSO; retire SitActionSystem
- [ ] Author PickupBehaviorSO; retire PickupItemActionSystem
- [ ] Migrate SocialAwarenessSystem to UtilityBrain + UtilityActions; author TalkBehaviorSO

### Phase 4 — Player unit wiring
- [ ] MinionActionSelectionSystemGroup: write UtilityActions with isPlayerOrdered=true, priority=100
- [ ] WinnerSelectionSystem: player-ordered entries skip scoring, always win
- [ ] Verify: PlayerOrder → StateMachine.action updates immediately

### Phase 5 — Cleanup
- [ ] Delete MotivationScoringSystem, ActionPrioritySystem, PersonalityContextSystem
- [ ] Delete SelectionFunctions.cs, ActionSelectionSystem.cs (commented out)
- [ ] Remove AIScoringSystemGroup declaration from SystemGroups.cs
- [ ] Profile: zero per-frame structural changes in AI pipeline
- [ ] Optionally re-enable DecideCadence for decision throttling

## Key Files

| File | Role |
|---|---|
| `Assets/_Scripts/Systems/SystemGroups.cs` | Group declarations + order |
| `Assets/_Scripts/Components/AI/UtilityAiComponents.cs` | UtilityActions, StateMachine, etc. |
| `Assets/_Scripts/Data/Structs/AIConfigBlobs.cs` | ConsiderationBlob (add needType/traitType) |
| `Assets/_Scripts/Data/SOs/UtilityActionSO.cs` | ConsiderationAuthoring SO fields |
| `Assets/_Scripts/Systems/PostBakingSystemGroup/BrainLibraryBakingSystem.cs` | Bakes brain blobs |
| `Assets/_Scripts/Systems/UtilityAISystemGroup/UtilityDecisionSystemGroup/ConsiderationScoringSystem.cs` | Phase 2 implement |
| `Assets/_Scripts/Systems/UtilityAISystemGroup/UtilityDecisionSystemGroup/WinnerSelectionSystem.cs` | Phase 2 implement |
| `Assets/_Scripts/Systems/StateMachineSystemGroup/ActionSelectionSystemGroup/BehaviorExecutionSystem.cs` | Phase 3 extend |
| `Assets/_Scripts/Systems/UtilityAISystemGroup/ActionAwarenessSystemGroup/` | All awareness systems |

## Notes

- `SelfDefenceAwarenessSystem` still on old AIBrain pipeline — it writes ActionInterruptRequest directly, keep separate
- `SocialAwarenessSystem` still on old AIBrain pipeline — Phase 3 migration target
- `Personality` component does not exist — FleeAwarenessSystem needs to use PersonalityAttributes buffer instead
- `WanderAction` component (on NavigationAwarenessSystem filter) may not exist — remove that filter
- `isPlayerOrdered` field added to UtilityActions for Phase 4 player override
- Requests (PathRequest, AttackRequest etc.) are the intentional decoupling seam — StateMachine emits them, downstream systems respond
