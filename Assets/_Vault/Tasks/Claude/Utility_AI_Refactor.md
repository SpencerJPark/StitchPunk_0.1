---
status: in-progress
created: 2026-05-30
tags: [ai, dots, refactor, performance]
---

# Utility AI Refactor — Separate Action Entities

# Goal

Convert the hand-coded AI into a data-driven, editor-authorable utility AI built on separate
action entities, scaling to 2000+ mixed-complexity deciders on low-end devices, while keeping
the priority filter and the existing enableable-request downstream systems.

# Context

Full design + rationale lives in the approved plan: `~/.claude/plans/i-want-you-to-binary-stearns.md`.
Design conversation source: `_Vault/Memories/Design/UtilityAI_Dots.md`.

Locked decisions:
- **Separate action entities** (buffer-less IComponentData; considerations + command sequences in blobs).
- Authoring: `BehaviorSO` (executed sequence) + `UtilityActionSO` (ActionType/priority/targeting +
  **inline** `AnimationCurve` considerations + one BehaviorSO) + `BrainConfigSO` (unit's action set).
- Selection: hashmap winner-selection (priority tier + top-3 random), replaces old `ActionSelectionSystem`.
- Execution: generic `BehaviorExecutionSystem` walks `ActionConfigBlob` via `StateMachine`, emits the
  existing enableable requests. `SelectionFunctions` + per-action tags are **abandoned** (legacy only).
- Rollout: incremental, gated by a `UtilityBrainV2` tag; old AI runs until each domain is migrated.
- Curves authored **inline**; legacy `ConsiderationCurveSO`/`ConsiderationLibrarySO` deleted in Phase 4.

Performance rules: lookup-free scoring (ContextAssembly pre-flattens), pooled action entities recycled
via enableable `ActionActive` (no per-frame structural changes), decision throttling via `DecideCadence`,
Burst + ScheduleParallel everywhere, never `.Run()`.

# Subtasks

## Phase 0 — Confirm build compiles (likely already done)
- [x] Confirm Unity compiles, no console errors, legacy AI runs (rename + library already in place)

## Phase 1 — Authoring + blob pipeline (skill: dots-blob-library)
- [x] `BehaviorSO.cs` (targetRange, executionSequence, interruptionCleanup)
- [x] `UtilityActionSO.cs` (ActionType, priority, TargetingMode, BehaviorSO, inline considerations)
- [x] `BrainConfigSO.cs` (UnitType, List<UtilityActionSO>)
- [x] `EntityLibraries.cs`: `AIConfigBlob` (brains/actionDefs/behaviors/considerations) + `AIConfig` singleton
- [x] `AIConfigAuthoring.cs` + `AIConfigBakingSystem.cs` (PostBakingSystemGroup, sample inline curves, IsCreated guard)
- [ ] Verify: baked `AIConfig` blob populated in Entities Inspector

## Phase 2 — Runtime skeleton + Wander/Idle end-to-end
- [x] Components: `UtilityAction` (+ actionDefIndex), `ActionActive`, `DecideCadence`, `UtilityBrainV2`, `AwarenessTarget`; `StateMachine` (+ activeBehavior)
- [x] Action-entity pooling at unit spawn (`ActionEntitySpawnSystem` in SpawnInitSystemGroup)
- [x] PerceptionSystemGroup + `WanderPerceptionSystem` (writes AwarenessTarget; v2 units only)
- [x] `ActionInstancingSystem` (enables Self-targeting action entities; SingleTarget disabled until Phase 3)
- [x] `ContextAssemblySystem` (health ratio + max-need ratio + zero distance for Self)
- [x] `ConsiderationScoringSystem` (blob curve eval + compensation factor → totalUtility)
- [x] `WinnerSelectionSystem` (NativeParallelMultiHashMap two-pass → priority tier + score → StateMachine)
- [x] `BehaviorExecutionSystem` (Approach/Execute/Complete phase machine → PathRequest)
- [x] Gate all new systems with `UtilityBrainV2`
- [ ] Verify: Wander test unit scores/decides/moves; inline-curve edits change score

## Phase 3 — Migrate combat (Melee, SingleTarget reference)
- [ ] Refit `EnemyAwarenessSystem`/`SelfDefenceAwarenessSystem` → `AwarenessTarget`
- [ ] Author `MeleeSwing` BehaviorSO + `MeleeAttack` UtilityActionSO + BrainConfig
- [ ] Confirm interpreter emits `AttackRequest`; damage lands via unchanged `AttackRequestSystem`
- [ ] Verify: 2 enemies → 2 action entities, correct winner, clean re-target

## Phase 4 — Migrate remaining domains + retire legacy
- [ ] Flee, Pickup, Social/Talk (+ reservation handshake), Interactions/Sit (+ ReservationStatus), waypoints
- [ ] Flip each domain's prefabs to `UtilityBrainV2`; delete its hand-written `*ActionSystem`
- [ ] Retire: old `ActionSelectionSystem` selection, `MotivationScoringSystem`, `ActionPrioritySystem`,
      `ClearOptionsSystem`, `ActionOption`, `AIScoringLibraryAuthoring`, `ScoringLibraryBakingSystem`,
      `ConsiderationCurveSO` + `ConsiderationLibrarySO`, `SelectionFunctions` + per-action tags

## Phase 5 — Performance hardening (2000+ / low-end)
- [ ] Tune `DecideCadence` bucketing; profile on low-end target
- [ ] Cap SingleTarget fan-out (nearest-N targets per unit)
- [ ] Confirm zero per-frame structural changes; dense action-entity chunks
- [ ] Optional: distinct zombie vs human BrainConfigs

# Notes

- Reuse: `AIScoringBlob.Evaluate` + curve sampling loop (`ScoringLibraryBakingSystem`,
  `ConstGameData.SCORING_CURVE_RESOLUTION`); `FilterToHighestPriority` + top-3 from
  `ActionSelectionSystem.cs`; blob baking from `ItemLibraryBakingSystem`/`FactoryLibraryBakingSystem`.
- Open items: SingleTarget fan-out cap (decide Phase 3); DecideCadence scheme (decide Phase 5).
