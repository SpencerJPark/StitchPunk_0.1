---
tags: [memory, code, contracts, architecture]
related: "[[Systems]], [[Components]], [[Systems_AI]], [[RULES]]"
---

# Contracts — the cross-feature API surface

Every request/event component that carries intent **between** system groups. This is the plug-and-play boundary: a feature is interacted with by enabling one of these on an entity (or enqueueing on a bus) — never by writing another feature's internal state. If a new system needs 3+ foreign-domain lookups, the fix is a new entry in this table, not more lookups.

Keep this file current: add a row when you add a request component; delete the row *and the struct* when the last consumer dies. Dead contract entries cost every reader a rule-out.

## Entity-carried requests (enableable components / buffers)

| Contract | Produced by | Consumed by (owner feature) |
|---|---|---|
| `PathRequest` | BehaviorExecution/Interrupt, DeathSystem, HordeSystem, SpawnStateInit | `MovementSystemGroup` — PathRequestSystem → routing → followers |
| `AttackRequest` | BehaviorExecutionSystem (RequestAttack), PlayerAttackSystem | `CombatSystemGroup` — AttackRequestSystem (AnimationTimeSystem reads swing timing; WorldMoodSystem reads for mood; BehaviorInterruptSystem cancels) |
| `HealRequest` | ItemConsumeSystem | `HealthSystemGroup` — HealRequestSystem |
| `ReviveRequest` | PlayerReviverSystem, SpawnStateInitSystem | `HealthSystemGroup` — ReviveRequestSystem |
| `SwapBrainRequest` | ReviveRequestSystem | `HealthSystemGroup` — SwapBrainSystem |
| `ActionInterruptRequest` | DeathSystem, ReviveRequestSystem, SwapBrainSystem, PathStuckCheckSystem, SelfDefenceAwarenessSystem | `StateMachineSystemGroup` — BehaviorInterruptSystem (single teardown path) |
| `ActionRequest` | MotivationDecaySystem, UnitSpawnerSystem, PersistentLoadSystem | `UtilityAISystemGroup` — gate tag that triggers the awareness/scoring pass |
| `MotivationChangeRequest` (buffer) | ItemConsumeSystem, BehaviorExecution (ModifyMotivation), DeathSystem, BehaviorInterrupt | `UtilityAISystemGroup` — MotivationChangeRequestSystem |
| `SocialInvite` | BehaviorExecutionSystem (RequestSocialResponse) | `UtilityAISystemGroup` — SocialResponseSystem |
| `PickupRequest` | BehaviorExecutionSystem (RequestPickup), PlayerPickupSystem | `ItemSystemGroup` — ItemConsumeSystem (consumables) / ItemEquipSystem (weapons) |
| `AttachItemRequest` | BehaviorExecution, PlayerPickupSystem, ItemConsumeSystem | `ItemSystemGroup` — ItemAttachSystem |
| `ThrownItemRequest` | PlayerUnequipSystem | `ItemSystemGroup` — ThrownItemSystem + ThrownItemHitSystem |
| `AnimationRequest` | BehaviorExecution/Interrupt, PlayerAttackSystem | `AnimationSystemGroup` — AnimationRequestSystem |
| `ChangeDesignRequest` | (re-skin callers; currently only consumed) | `DesignSystemGroup` — DesignChangeSystem. Payload: shape `paletteChanges`/`shapeOverrides` + `alternateColorMode` (Enable = zombify: every palette entry shows its `alternative` colour, rolled identity kept) |
| `SaveRequest` / `LoadRequest` | AutoSaveTimerSystem; SaveLoadBridge + DebugSaveMenu (Mono) | `SaveSystemGroup` — PersistentSaveSystem / PersistentLoadSystem |
| `OnDialogueEvent` | DialogueUIManager (Mono) | `DialogueSystemGroup` — DialogueEventSystem; NarrativeDialogueBridgeSystem bridges to narrative |
| `OnNarrativeEvent` | NarrativeProximitySystem, NarrativeDialogueBridgeSystem | NarrativeEventManager (Mono, async via UniTask) |

## Bus contracts (NativeQueue singletons)

| Contract | Owner | Producers | Resolution / consumer |
|---|---|---|---|
| `DamageEvent` via `DamageBus` | DamageBusSystem (GameManagerSystemGroup, OrderFirst — resets queues, carries producer JobHandle) | AttackRequestSystem, HazardZoneSystem, ThrownItemHitSystem, PlayerAttackSystem | DamageResolutionSystem (raw → resolved), DamageEventSystem (applies). ⚠ 2026-07 ragdoll rework: `hitSourceX` deleted — every producer sets `sourcePosition` (float3, drives AOE + ragdoll launch direction); events carry `flailIntensity`/`spin`/`restitution`; the lethal event's values land in `Health.kill*` (incl. `killSourcePosition`) |
| `CorpseCells` map | CorpseCellSystem (GameManagerSystemGroup — rebuilds each frame from settled corpses, carries reader JobHandle via `AddJobHandleForReader`) | (rebuild is the producer) | Ragdoll2DSystem (RagdollSystemGroup) reads pile height at landing |

## Removed (2026-07-02 structural pass)

`SocialValidationRequest`, `SpawnItemRequest`, `DespawnItemRequest`, `UseItemRequest`, `DropRequest`, `ReleaseRequest` — defined but produced/consumed by nothing. Deleted; git remembers if a future feature wants the name back.
