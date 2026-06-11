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
- [x] BehaviorSO assets authored for all domains:
  - Wander/MeleeContinuous/MeleeSingle/Flee/Talk existed already
  - SitBehaviour: Approach(1.35) → PlayAnimation(Sit, loop) → WaitTime(8s) → ModifyMotivation(Energy +50) → ReleaseInteraction(60) → StopAnimation
  - PickupBehaviour (shared by EquipWeapon/UseHealingItem/Eat/Drink): Approach(1.5) → PlayActionAnimation → WaitTime(1s) → RequestPickup → StopAnimation
  - Note: WaitTime in Sit/Pickup uses Qualifier=None — TargetDead would exit instantly on
    chairs/items (missing-Dead-data evaluates true)
- [x] UtilityActionSO assets: SitAction, EquipWeaponAction, UseHealingItemAction, EatAction, DrinkAction — added to CitizenBrain (Rotter intentionally skipped)
- [x] _BehaviorLibrary wired with SitBehaviour + PickupBehaviour; _BrainLibrary unchanged
- [x] ItemAwarenessSystem now sets actionDefIndex on all four emissions (was defaulting to slot 0) and skips when the brain lacks the action
- [x] New ItemConsumeSystem (before ItemEquipSystem): consumables → HealRequest or MotivationChangeRequest + destroy; weapons fall through to equip. RunRequestPickup re-validates the item wasn't claimed mid-approach
- [x] Retire old execution systems once BehaviorSOs cover each domain — all 8 commented legacy systems deleted
- [x] Sit interaction source (close-out 2026-06-10): Chair.asset configured (Sit, Energy/50, range 1.5, duration 8, maxOccupants 1) + added to _InteractionLibrary; a Sit chair entity already existed in DOTSTestScene (actionType 16)
- [x] **Bug fix** — InteractionAuthoring.Baker never baked `authoring.actionType` into `Interaction` (every interaction entity baked as Idle → spatial hash never registered it → InteractionAwareness could never emit Sit/Bathroom). Now bakes `action = authoring.actionType`
- [x] Item asset setup (close-out 2026-06-10): Bandage/MedKit/Bread/Water ItemSOs created (Consumable; Healing/Healing/Feed/Hydrate); Feed + Hydrate EffectSOs created (both restore Hunger — no Thirst NeedType exists and DrinkAction already scores Hunger); _EffectLibrary updated; _ItemLibrary fixed (listed None twice, Rock missing) → now None/Rock/Bandage/MedKit/Bread/Water
  - Note: EffectLibrary is enum-indexed, so Bandage and MedKit share Healing's value (50); differentiating needs a second healing EffectType
- [ ] **IN UNITY EDITOR** — place consumable item GameObjects (Bandage/MedKit/Bread/Water, itemType 6/7/8/9) in DOTSTestScene with ItemAuthoring + visuals (the Rock object is the model); add handSocket child to citizen prefab + assign on CitizenBrainAuthoring (caution: CitizenBrain.prefab carries stale serialized fields `body`/`awarenessRange` and no unitLibrary — verify which object holds the live authoring)
- [ ] **IN UNITY EDITOR** — suspicious scene data: an InteractionAuthoring in DOTSTestScene has actionType 3 (= Death); likely meant Bathroom (15)

### Phase 4 — Player unit wiring ✅ DONE
- [x] MinionActionSelectionSystem — new system in MinionActionSelectionSystemGroup
- [x] Handles: OnMinionAttackCommand → MeleeSingle, OnMinionInteractCommand → Interact, OnMinionFollowCommand → Wander(player entity)
- [x] All entries written with isPlayerOrdered=true — WinnerSelectionSystem picks unconditionally
- [x] Uses ComponentLookup for each command so missing commands are silently skipped
- [x] OnMinionMoveCommand (move-to-position): float3 targetPosition + hasTargetPosition added to UtilityActions + StateMachine (incl. pending* variants). Move order = Wander with targetEntity=Null; RunApproach paths to the raw position. Detection is the explicit hasTargetPosition bool (float3.zero is a legal click)
- [x] Command delivery fixed — was a dead end (UnitSelectionManager wrote commands to the player entity; the job read them off minions). Now: UnitBakingUtil.AddPlayerControlled bakes all five OnMinion*Command slots disabled; UnitSelectionManager fans commands out to each selected minion + enables PlayerUnitBrain; MinionActionSelectionSystem consumes commands one-shot (disables after emitting)
- [x] WinnerSelection guard relaxed: a player order to a different target/position preempts a live same-action behavior (one-shot consumption prevents per-frame re-preempt). Interrupt/Complete/SocialResponse paths clear/transfer the position fields
- [ ] Verify in play mode: right-click ground → [MinionOrder] Move log → unit paths there → returns to AI autonomy; second order mid-move re-targets; OnMinionAttackCommand → MeleeSingle
- [ ] **DEFERRED: OnMinionDefendCommand** (decision 2026-06-10) — baked + fanned out (Shift+right-click in UnitSelectionManager) but not read by MinionActionSelectionSystem; needs a Defend/Guard ActionType + BehaviorType + DefendBehaviour asset (move to position, hold, attack enemies entering radius). Out of scope for V3 close-out

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
- PickupRequest lives on the item entity (not unit), enabled by callers; ItemConsumeSystem intercepts consumables before ItemEquipSystem links weapons
- UtilityBrain.unitType now populated from UnitSO.unitType in UnitBakingUtil
- SitBehaviour hardcodes Energy +50 (legacy read InteractionBlob.satisfiedNeed/restorationAmount per asset) — a future ModifyMotivationFromInteraction command would restore data-driven values
- Heal/Eat/Drink share priority tier 1 with Wander — consideration curves decide; may need tuning
- RotterBrain has no Interact/Sit/Pickup actions — minion Interact orders on rotters resolve defIndex −1 → behavior None (pre-existing)
- Close-out 2026-06-10: BehaviorExecutionSystem.cs physically moved into ActionExecutionSystemGroup/ (its UpdateInGroup was already correct); the now-empty StateMachineSystemGroup/ActionSelectionSystemGroup/ folder was deleted (ConsiderationScoring/WinnerSelection live in UtilityAISystemGroup/UtilityDecisionSystemGroup/ and update in ActionSelectionSystemGroup via attribute)
