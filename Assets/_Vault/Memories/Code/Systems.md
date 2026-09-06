---
tags: [memory, code, systems, ecs, execution-order]
related: "[[Systems_AI]], [[Systems_Animation]], [[Systems_Movement]], [[Components]], [[RULES]]"
---

# Systems — Context

All game logic lives here. Read the context file for each sub-group before working inside it.

---

## System Group Execution Order

```
PostBakingSystemGroup        — SOs → BlobAssets + cross-entity component distribution (runs once at bake time)
GameManagerSystemGroup       — WORLD SERVICES (not scene-gated): registries, spatial hashes, DamageBus reset, floating origin, horde state
PlayerSystemGroup            — all player-driven logic
  ├── PlayerInputSystemGroup (OrderFirst)  — input events → ECS components, targeting, attacks, aim, equipment dispatch
  ├── NarrativeSystemGroup   — proximity trigger detection and dialogue→narrative bridge (before DialogueSystemGroup)
  ├── DialogueSystemGroup    — dialogue start detection and event relay (after NarrativeSystemGroup)
  └── PlayerEquipmentSystemGroup (OrderLast) — equipment actions (reviver)
CutsceneSystemGroup          — wires com.dotsanimationtoolkit's cutscene player into the game: see [[Systems_AI]]'s brain-control split and Tasks/NewPlans/CutsceneIntegration_System.md (G1)
UtilityAISystemGroup         — decision inputs: see [[Systems_AI]]
  ├── UtilityMotivationSystemGroup — motivation change requests + decay
  └── UtilityAwarenessSystemGroup  — awareness systems populate the UtilityActions buffer
MinionActionSelectionSystemGroup — player-guided: consumes minion orders → writes StateMachine directly
StateMachineSystemGroup      — decision resolution + behavior execution: see [[Systems_AI]]
  ├── ActionSelectionSystemGroup — ConsiderationScoringSystem + WinnerSelectionSystem → StateMachine
  └── ActionExecutionSystemGroup — BehaviorInterruptSystem (OrderFirst) + BehaviorExecutionSystem (the interpreter)
ItemSystemGroup
  ├── ItemEquipSystemGroup (OrderFirst) — equip, consume, pickup, attach, unequip
  └── ThrownItemSystemGroup            — thrown item movement, proximity hit detection
MovementSystemGroup          — PACKAGE-OWNED (com.dotsmovementtoolkit, ns DotsMovementToolkit); see [[Systems_Movement]]
  ├── MovementCoordinatorSystemGroup — grid, path requests, stuck checks, formation offsets, horde lifecycle
  ├── MovementRoutingSystemGroup     — flowfield / D* Lite path calculation
  ├── MovementFollowerSystemGroup    — smooth path following (+ game's PlayerMoveSystem)
  ├── MovementSteeringSystemGroup    — empty, declared slot for future arrival easing/avoidance
  └── MovementExecutionSystemGroup   — writes final position/rotation to transforms (+ game's LocomotionStanceSystem)
BuildingsSystemGroup         — factory production loop (PARKED — ProductionSystem + FactoryLibraryBakingSystem live commented-out in Core/Unused/)
CombatSystemGroup
  ├── CombatExecutionSystemGroup — AttackRequestSystem, HazardZoneSystem (DamageBus producers)
  └── CombatReactionSystemGroup  — DamageResolutionSystem + DamageEventSystem (bus drain + apply)
HealthSystemGroup            — death, fake ragdoll init, heal, revive, brain swap, zombify, health bar updates
DesignSystemGroup            — DesignChangeSystem: runtime body-part re-skin (after Health, before Animation)
AnimationSystemGroup         — see [[Systems_Animation]]
  ├── AnimationAssignmentSystemGroup — decides which clip to play per layer
  └── AnimationExecutionSystemGroup  — advances time, samples keyframes, applies pose
LateSimulationSystemGroup
  ├── SpawnSystemGroup       — UnitSpawnerSystem: instantiate/reclaim, enable NewlySpawned
  ├── SpawnInitSystemGroup   — all spawn-frame init systems filter on [WithAll<NewlySpawned>]
  ├── RagdollSystemGroup     — Ragdoll2DSystem: corpse flight/flail/settle (after all transform writes, before Sound for future landing SFX)
  ├── SoundSystemGroup       — gather/cull requested sounds → ResolvedVoices/WorldMood/MusicState (AudioManager reads them in LateUpdate)
  ├── DespawnSystemGroup     — UnitPoolReturnSystem
  ├── (raw members)          — OrderMarkerSystem, SelectedVisualSystem (documented conformance exemptions)
  └── SaveSystemGroup        — play time tracking, auto-save timer, save/load (OrderLast)
PresentationSystemGroup      — InteractionHighlightSystem (outline systems parked in Core/Unused/)
```

### Structural conformance (2026-07 pass)

`SystemGroups.cs` is the **single manifest**: every group is declared there, top-down in execution order. Three rules are now *enforced* by `Assets/_Scripts/Tests/SystemPlacementConformanceTests.cs` + `SystemGroupOrderTests.cs` (EditMode — run via Test Runner):

1. Every system carries `[UpdateInGroup]` (a forgotten attribute silently auto-creates in `SimulationSystemGroup`).
2. A system file lives in the folder named after its group — the folder tree IS the group tree (3 documented exemptions in the test).
3. Every adjacent pair in the pipelines above has an explicit `UpdateBefore`/`UpdateAfter` edge, and no ordering edge crosses a parent-group boundary (Unity ignores those silently).

**Scene gating is group-level:** top-level feature groups derive from `GameSceneSystemGroup` (declared at the top of `SystemGroups.cs`), which does `RequireForUpdate<GameSceneTag>()` once for the whole feature. New systems only declare their own DATA requirements (`RequireForUpdate<SomeLibrary>`); the per-system `GameSceneTag` require is legacy boilerplate (harmless, being phased out). `FeatureConfigAuthoring` (Authoring/Tags/) bakes per-feature plug tags (`CombatFeature` etc.) for scene-level feature toggles — the group-side `RequireForUpdate<XFeature>` wiring is a planned follow-up (see Tasks/Plans).

The cross-feature request/event API surface is indexed in [[Contracts]] — read it before making one feature touch another.

---

## All Systems — File Map

### PostBakingSystemGroup (`Systems/PostBakingSystemGroup/`)
Runs once at bake time. Converts ScriptableObject data into BlobAssets, and distributes components that bakers cannot add cross-entity. See [[Data]] for the SO → Blob pipeline and [[Authoring]] for the cross-entity baking pattern.

| System | File | What it bakes |
|---|---|---|
| `AnimationLibraryBakingSystem` | `AnimationLibraryBakingSystem.cs` | AnimationLibrarySO → AnimationLibraryBlob |
| `UnitLibraryBakingSystem` | `UnitLibraryBakingSystem.cs` | UnitLibrarySO → UnitLibraryBlob + UnitPrefabEntry |
| `AttackLibraryBakingSystem` | `AttackLibraryBakingSystem.cs` | AttackLibrarySO → AttackLibraryBlob |
| `FactoryLibraryBakingSystem` | `Core/Unused/FactoryLibraryBakingSystem.cs` (**parked** with ProductionSystem) | FactoryLibrarySO → FactoryLibraryBlob (recipes blob for ProductionSystem) |
| `PartLibraryBakingSystem` | `PartLibraryBakingSystem.cs` | PartLibrarySO → PartLibraryBlob (enum-indexed per-part `PartDesignDef` list: tagged texture span + 3 `PartPaletteSlot` colour windows per design; DESIGN only — no ragdoll data; CharacterRig) |
| `ColorPaletteLibraryBakingSystem` | `ColorPaletteLibraryBakingSystem.cs` | ColorPaletteLibrarySO → ColorPaletteLibraryBlob (enum-indexed palettes of `ColorBlob { color, alternative }` pairs — alternative = the entry's zombie/converted variant; **sRGB → linear at bake**; unauthored slots fall back to a 1-entry white palette; holder: `ColorPaletteLibrary`) |
| `CharacterRigBakingSystem` | `CharacterRigBakingSystem.cs` | Builds the root `BodyPart` buffer from `BodyPartInfo`+`BaseParent`; stamps `Ragdoll2D`/`Ragdoll2DJoint` (disabled) with settle/segment/weight from the joint's `RagdollJointBakeData` (written by `RagdollJointAuthoring` — ragdoll fully separate from the design blob). Replaces `CharacterBodyPartBakingSystem` + `Ragdoll2DBakingSystem` (both deleted) |

---

### GameManagerSystemGroup (`Systems/GameManagerSystemGroup/`)

**Charter: world services.** Frame-setup infrastructure only — registries, spatial hashes, event buses, floating origin. Runs `OrderFirst` (before every feature) and is deliberately **not** scene-gated at group level. Features may read its singletons; it never reads feature state. Gameplay logic does not belong here.

| System | File | Purpose |
|---|---|---|
| `DamageBusSystem` | `DamageBusSystem.cs` | Owns + resets the recycled `DamageBus` NativeQueues, carries the producer JobHandle (see CombatSystemGroup section) |
| `CorpseCellSystem` | `CorpseCellSystem.cs` | **Managed** (`SystemBase`) owner of the `CorpseCells` corpse-stacking hash (`NativeParallelMultiHashMap<int2,float>`, Persistent). Rebuilt from scratch each frame from SETTLED corpses (`Ragdoll2DLaunch` enabled + sleeping) — revive/despawn bookkeeping is free. Carries the reader JobHandle (`AddJobHandleForReader`, completed before the clear) because the map bypasses ECS dependency tracking; `Ragdoll2DSystem` is the reader (landing pile height) |
| `FactionRegistrySystem` | `FactionRegistrySystem.cs` | Maintains the per-faction entity registry singleton |
| `InteractionSpatialHashSystem` | `InteractionSpatialHashSystem.cs` | Rebuilds the interaction/waypoint spatial hash (`SpatialHashRegistry`) |
| `WaypointRegistrationSystem` | `WaypointRegistrationSystem.cs` | Registers `NavigationWaypoint` entities into the registry |
| `FloatingWorldOriginSystem` | `FloatingWorldOriginSystem.cs` | Recenters world origin to prevent float precision loss |
| `CameraVisibilitySystem` | `CameraVisibilitySystem.cs` | Flips the `CameraVisible` enableable tag on rig roots + propagates to `BodyPart` children, from the `CameraView` singleton (XZ distance vs viewRadius, +5/+10 hysteresis paddings; `IgnoreComponentEnabledState` so off-screen roots re-enable). **Presentation-only gate** — animation sampling/apply/image-index/billboard chunk-filter on it; simulation systems must never gate on it. Prefab-lookup guard: spawn-frame `BodyPart` buffers still hold prefab entity refs — never written through (would corrupt the prefab's enable state); part sync self-heals via a drift check instead |

---

### PlayerSystemGroup (`Systems/PlayerSystemGroup/`)

Runs inside `SimulationSystemGroup`, before `MinionActionSelectionSystemGroup`. Contains two sub-groups:

#### PlayerInputSystemGroup (OrderFirst)

| System | File | Purpose |
|---|---|---|
| `PlayerRollInputSystem` | `PlayerInputSystemGroup/PlayerRollInputSystem.cs` | Ticks down `OnRollPlayerInput.rollTime`, disables when expired |
| `PlayerTargetingSystem` | `PlayerInputSystemGroup/PlayerTargetingSystem.cs` | Finds nearest `PlayerInteractable` in range; enables/disables `Target` on the player (interaction targeting) |
| `PlayerCombatTargetingSystem` | `PlayerInputSystemGroup/PlayerCombatTargetingSystem.cs` | Finds nearest **damageable** entity (`Health`, alive, `WithNone<Player, PlayerImmune, Dead>`) within `COMBAT_TARGET_RANGE` (5f); enables/disables `CombatTarget` on the player. Combat targeting is kept separate from interaction `Target`. |
| `PlayerAttackCooldownSystem` | `PlayerInputSystemGroup/PlayerAttackCooldownSystem.cs` | Ticks down `AttackCooldown.remaining`, disables when expired (mirrors `PlayerRollInputSystem`) |
| `PlayerAttackSystem` | `PlayerInputSystemGroup/PlayerAttackSystem.cs` | **Player melee (v1).** On `OnAttackPlayerInput` + off `AttackCooldown` + `CombatTarget` in `AttackBlob.range`: snap-faces the target, writes `AttackRequest { damageSource }` (consumed same-frame by `AttackRequestSystem`), pushes the swing anim (Action layer via `AIUtils.GetAnimationByAction`), starts `AttackCooldown = max(cooldown, hitTime+0.05)`. The player is the one combatant that bypasses the AI decision/execution split — it writes `AttackRequest` directly. Retaliation is left entirely to the existing AI threat systems. |
| `PlayerAimSystem` | `PlayerInputSystemGroup/PlayerAimSystem.cs` | Reads `LookPlayerInput` while aiming; updates `AimDirection`, rotates player, shows/hides aim indicator |
| `PlayerEquipmentInputSystem` | `PlayerInputSystemGroup/PlayerEquipmentInputSystem.cs` | Resolves `OnEquipmentSlotPlayerInput.slot` → `ItemType` via `PlayerEquipmentSlots`; fires the matching equipment event (`OnPlayerReviverEquipt` etc.) |

#### NarrativeSystemGroup (after PlayerInputSystemGroup, before DialogueSystemGroup)

| System | File | Purpose |
|---|---|---|
| `NarrativeDialogueBridgeSystem` | `NarrativeSystemGroup/NarrativeDialogueBridgeSystem.cs` | `[OrderFirst]` — reads `OnDialogueEvent` and maps it to `OnNarrativeEvent` via `NarrativeIds.DialogueBridge.Pairs`. Runs before proximity system so bridge events take priority. |
| `NarrativeProximitySystem` | `NarrativeSystemGroup/NarrativeProximitySystem.cs` | Checks player distance to all enabled `NarrativeTrigger` entities. When player enters range: enables `OnNarrativeEvent`, disables the trigger. No-ops when `ActiveNarrativeEvent` is enabled. |

`NarrativeEventManager` (MonoBehaviour) reads `OnNarrativeEvent` each `Update()`, looks up the `NarrativeEventSO` by ID, and runs action groups via UniTask (`async/await`). Groups execute sequentially; actions within a group run in parallel via `UniTask.WhenAll`. Re-enables repeatable triggers on event completion.

---

#### DialogueSystemGroup (after NarrativeSystemGroup, before PlayerEquipmentSystemGroup)

| System | File | Purpose |
|---|---|---|
| `DialogueStartSystem` | `DialogueSystemGroup/DialogueStartSystem.cs` | Detects `OnInteractPlayerInput` while player targets a `DialogueProvider` NPC. Picks primary or refresher sequence (checks `PlayedDialogue` buffer). Enables `ActiveDialogue` on the manager entity. Consumes interact input so no other system fires same frame. |
| `DialogueEventSystem` | `DialogueSystemGroup/DialogueEventSystem.cs` | `[OrderLast]` — disables `OnDialogueEvent` after one frame so downstream systems had exactly one frame to react. |

`DialogueUIManager` (MonoBehaviour) drives line progression and choice display. It detects `ActiveDialogue` each `Update()`, looks up the `DialogueSequenceSO` by ID, advances on `OnInteractPlayerInput`, and at sequence end writes to `PlayedDialogue` and disables `ActiveDialogue`.

---

#### PlayerEquipmentSystemGroup (OrderLast)

| System | File | Purpose |
|---|---|---|
| `PlayerReviverSystem` | `PlayerEquipmentSystemGroup/PlayerReviverSystem.cs` | When `OnPlayerReviverEquipt` is enabled and `Target` is valid: enables `Revive` on the target entity |

> `MinionCommandSystem` is **parked** in `Core/Unused/` — minion orders now flow `UnitSelectionManager` (Mono) → `OnMinion*Command` → `MinionActionSelectionSystem`.

---

### CutsceneSystemGroup (`Systems/CutsceneSystemGroup/`)

Between `PlayerSystemGroup` and `UtilityAISystemGroup` — a cutscene starting this frame must gate
AI selection this same frame, and `NarrativeEventManager`'s signal (a MonoBehaviour `Update`, before
`SimulationSystemGroup`) is consumed the same frame it's created. Wires
`com.dotsanimationtoolkit`'s cutscene player into the game: clip blocks, root keys, camera and
events (G1, `Tasks/NewPlans/CutsceneIntegration_System.md`), plus the four host contracts the
toolkit raises during playback — marks, the dialogue cue, facing and detach (G2,
`Tasks/NewPlans/CutsceneInteractions_System.md`).

| System | File | Purpose |
|---|---|---|
| `CutsceneStartSystem` | `CutsceneStartSystem.cs` | `[OrderFirst]`, not Bursted (managed `CutscenePlaybackApi` + structural changes). Consumes every `CutsceneRequest` signal: `TryFindStage` → `CreatePlayRequestFromStage`, applies `CutsceneRequestBindingOverride` entries, enables `CutsceneActor` + `ActionInterruptRequest` on every bound entity, halts any `PathRequest` (`MovementAPI.HaltPathing` + snaps `Movement.targetPosition`), writes `ActiveCutscene` on the `NarrativeEventTag` singleton. A second request while one is active, or a scene with no narrative singleton, is dropped with a warning. **Gotcha:** every `ComponentLookup` used here must be refreshed (`.Update(ref state)`) *after* `CreatePlayRequestFromStage`'s structural change — see `Gotchas.md` |
| `CutsceneEndSystem` | `CutsceneEndSystem.cs` | Not Bursted (`DestroyEntity`). On `CutscenePlaybackState.isComplete`: disables `CutsceneActor` on every bound entity, re-arms the brain (enables `ActionInterruptRequest` + `ActionRequest` — the same re-arm shape as `ReviveRequestSystem`/`SwapBrainSystem`), destroys the toolkit's request entity (the toolkit leaves that lifecycle step to the host), disables `ActiveCutscene` |
| `CutscenePlayerControlSystem` | `CutscenePlayerControlSystem.cs` | Drives `CutsceneActiveTag = ActiveCutscene enabled`, OR'd against `NarrativeEventManager`'s own writes for `blockPlayerInput` events — only *enables*, and only *disables* when `ActiveNarrativeEvent` is also disabled, so it never stomps a narrative-event-driven enable. **The rendezvous exception (G2):** while the clock is paused on a hold and the `Player` still has an enabled `CutsceneMoveToMark`, the tag goes *off* so the player can walk to their own mark — deliberately overriding a `blockPlayerInput` event, since an author who gave the player a mark asked for them to walk it |
| `CutsceneMoveToMarkSystem` | `CutsceneMoveToMarkSystem.cs` | Bursted, two `ScheduleParallel` jobs. Issue: a newly enabled `CutsceneMoveToMark` (A64) becomes one `MovementAPI.BeginPathRequest` at half the mark's tolerance, flagged by a game-side `CutsceneMarkIssued`. Resolve: when the toolkit disables the order (arrived, timed out, skipped, cutscene over), `HaltPathing` + `Movement.targetPosition = own position` + clear the flag. `[WithNone(typeof(Player))]` — the player is never pathed |
| `CutsceneDialogueCueSystem` | `CutsceneDialogueCueSystem.cs` | Not Bursted (managed reads + `CutscenePlaybackApi`). Reads the request entity's `AnimEventOutput` for `AnimEvents.Dialogue`, resolves `floatParam`'s slot **index** through `CutsceneBlob.slots` to a bound speaker, and makes the same `ActiveDialogue` write `NarrativeEventManager` does. When the UI manager closes the dialogue it enables `CutsceneHoldRelease` with the hold id `TryGetCurrentHoldId` reports — which for a holding event is the event's own registry name, `"Dialogue"` |
| `CutsceneDetachSystem` | `CutsceneDetachSystem.cs` | Bursted, one `.Schedule()`d job. A `CutsceneDetachSignal` (A63) on an entity that has `ThrownItemRequest` becomes a throw carrying the toolkit's world impulse, previous host and release position; a unit is placed and nothing else. Always consumes the signal. Takes the signal by `ref`, not `in` — `in` beside `EnabledRefRW` of the same type is a run-time aliasing throw |

**Gates elsewhere** (not in this folder): `[WithDisabled(typeof(CutsceneActor))]` on `WinnerSelectionSystem`, `MinionActionSelectionSystem`, and every `UtilityAwarenessSystemGroup` job except `ThreatDecaySystem` (deliberate — see the spec's §7 drift log); `UnitFacingSystem` reads `CutsceneFacing` on a `CutsceneActor`-enabled unit and keeps its previous facing when the cutscene has no answer (G2); `PlayerMoveSystem`/`PlayerAttackSystem`/`PlayerRollInputSystem`/`PlayerEquipmentInputSystem`/`PlayerPickupSystem`/`DialogueStartSystem` early-out on `CutsceneActiveTag` (read via `SystemAPI.TryGetSingletonEntity<NarrativeEventTag>` — a scene without the singleton behaves as "no cutscene"); `PersistentSaveSystem` skips a `SaveRequest` while `ActiveCutscene` is enabled (warns once). Camera: `CutsceneCameraBridge` (Mono, `MonoBehaviours/Managers/`) drives a dedicated `CameraManager` vcam from the `CutsceneCameraPose` singleton. Sound: `AnimEventSoundSystem` splits its `AnimEventsPending` pass in two — actors (unchanged, now `[WithNone(typeof(CutscenePlay))]`) and cutscene request entities (new, non-positional at `ListenerPosition`). Triggers: `PlayCutsceneAction` (`NarrativeEventSO.cs`) and `CutsceneDebugTrigger` (Mono, F9 debug key) both spawn a `CutsceneRequest` signal.

---

### MinionActionSelectionSystemGroup (`Systems/MinionActionSelectionSystemGroup/`)

Runs before `StateMachineSystemGroup`. Handles player-commanded units and writes `UtilityActions` buffer entries from the player's command. See [[Systems_AI]].

---

### StateMachineSystemGroup — see [[Systems_AI]]

Contains the nested `ActionSelectionSystemGroup` and `ActionExecutionSystemGroup` (see `SystemGroups.cs`).

---

### AnimationSystemGroup — see [[Systems_Animation]]

---

### MovementSystemGroup — package-owned, see [[Systems_Movement]]

Declared inside `Packages/com.dotsmovementtoolkit`, not `SystemGroups.cs` — the game's `ItemSystemGroup`/
`BuildingsSystemGroup` keep their `UpdateBefore`/`UpdateAfter` edges onto it via `using DotsMovementToolkit;`.

---

### BuildingsSystemGroup (`Systems/BuildingsSystemGroup/`)

Runs after `MovementSystemGroup`, before `CombatSystemGroup`. Handles factory production. Requires a `FactoryLibrary` singleton entity (baked by `FactoryLibraryAuthoring`) to be present in the scene.

> ⚠ **PARKED (2026-07):** `ProductionSystem` and `FactoryLibraryBakingSystem` are fully commented out and moved to `Core/Unused/`. The ECS data layer (grid, station components, authoring, library SO types) still exists, but the production loop does not run. Re-enabling = restore both files to their group folders, uncomment, and complete the setup steps below.

| System | File | Purpose |
|---|---|---|
| `ProductionSystem` | `Core/Unused/ProductionSystem.cs` (parked) | Checks idle stations for inputs+workers (`StartProductionJob`); ticks active cycles and writes outputs (`TickProductionJob`) |

**Key components:** `FactoryStation`, `StationInputSlot` buffer, `StationOutputSlot` buffer, `ProductionProgress` (enableable), `StationWorkerSlot` buffer — see [[Components]].

**Setup per scene with a factory floor:**
1. Add a GO with `FactoryGridAuthoring` (defines grid size + origin) — creates the grid singleton entity
2. Add a GO with `FactoryLibraryAuthoring` pointing to `_FactoryLibrary` SO — creates the library singleton
3. Add station GOs with `FactoryStationAuthoring` (stationType, gridX/Z, workerSlots)

---

### ItemSystemGroup (`Systems/ItemSystemGroup/`)

#### ItemEquipSystemGroup (OrderFirst) (`ItemEquipSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `ItemEquipSystem` | `ItemEquipSystem.cs` | Equips items from pickup or command |
| `PlayerPickupSystem` | `PlayerPickupSystem.cs` | Handles player pickup input |
| `ItemAttachSystem` | `ItemAttachSystem.cs` | Attaches equipped item visually to socket |
| `PlayerUnequipSystem` | `PlayerUnequipSystem.cs` | Handles player unequip input |

#### ThrownItemSystemGroup (after ItemEquipSystemGroup) (`ThrownItemSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `ThrownItemSystem` | `ThrownItemSystem.cs` | Applies horizontal (X/Z) throw velocity each frame; stops and re-enables `PlayerInteractable` when grounded |
| `ThrownItemHitSystem` | `ThrownItemHitSystem.cs` | Proximity-based hit detection for thrown items (no physics collider required). XZ-only distance check; "hittable" == has `Health`. On hit **Enqueues** a `DamageEvent` value (`sourceEntity = Null`, `damageSource = Throw`) into the `DamageBus.raw` queue (synchronous main-thread write — runs before any parallel `raw` writer, so no producer-handle registration), disables `ThrownItem`, enables `PlayerInteractable` |

> Thrown items skip the `thrower` entity and ignore all hits until the item has traveled **1.2 units** from `ThrownItem.throwOrigin` — prevents a body right next to the player from immediately blocking the throw. Walls are unaffected (no `Health` component).

---

### CombatSystemGroup (`Systems/CombatSystemGroup/`)

Combat runs on a **recycled `NativeQueue<DamageEvent>` bus (v2)** — no per-unit `Hurt` buffer and no per-hit entity create/destroy. Producers `Enqueue` source-agnostic `DamageEvent` values into `DamageBus.raw`; `DamageResolutionSystem` expands AOE into single-target events in `DamageBus.resolved`; one consumer (`DamageEventSystem`) drains `resolved`. `DamageEvent` is a **plain value struct, not an `IComponentData`** (not inspectable in the Entities window — a per-frame `[DamageBus] Applied N` count is logged instead). See the built spec [`../../Tasks/Verification/DamageEvent_v2_System.md`](../../Tasks/Verification/DamageEvent_v2_System.md).

**Manual job-dependency wiring (critical):** a `NativeQueue` handed through a singleton bypasses ECS automatic dependency tracking. `DamageBusSystem` owns the queues + a combined producer `JobHandle`; every parallel producer calls `DamageBusSystem.AddJobHandleForProducer(state.Dependency)`; `DamageResolutionSystem` combines `ProducerHandle` and `Complete()`s before draining `raw` on the main thread — the `EntityCommandBufferSystem` pattern. Get it wrong → a race / disposed-container crash, not a compile error.

| System | File | Purpose |
|---|---|---|
| `DamageBusSystem` | `DamageBusSystem.cs` | **Managed** (`SystemBase`) queue owner, `GameManagerSystemGroup` OrderFirst (resets before *any* producer — thrown items live earlier in the frame). Creates `raw`/`resolved` `NativeQueue`s (`Allocator.Persistent`) + the `DamageBus` singleton in `OnCreate`, clears them each frame, exposes `AddJobHandleForProducer`/`ProducerHandle`, disposes in `OnDestroy`. All hot work stays in the Burst producer/resolution/consumer jobs. |
| `HazardZoneSystem` | `CombatExecutionSystemGroup/HazardZoneSystem.cs` | Environmental producer — `[UpdateBefore(AttackRequestSystem)]` so its synchronous main-thread `raw` write never overlaps the parallel melee enqueue. For each `HazardZone` past its whole-zone `retriggerInterval` gate, Enqueues a `Hazard` `DamageEvent` (`sourceEntity = Null` → no threat) for every alive unit in `radius` (XZ), stamps `lastTriggerTime`. |
| `AttackRequestSystem` | `CombatExecutionSystemGroup/AttackRequestSystem.cs` | Per-attacker swing-windup timer (`AttackRequest` enableable). On hit (range/alive checks pass) **Enqueues** a `DamageEvent` value into `DamageBus.raw` via `ScheduleParallel` (`NativeQueue.ParallelWriter`), then registers the write handle with `DamageBusSystem.AddJobHandleForProducer`. `sourceEntity = attacker`, `damageSource = attackBlob.damageSource`. |
| `DamageResolutionSystem` | `CombatReactionSystemGroup/DamageResolutionSystem.cs` | AOE expand pre-pass, `[UpdateBefore(DamageEventSystem)]`. Combines `ProducerHandle` + `Complete()`s, drains `raw` to an array (main thread), and if any `AreaOfEffect` event is present gathers a target snapshot. `DamageExpandJob` (`ScheduleParallel`): SingleTarget (and, until dedicated resolvers exist, Cone/Line/Chain) copies straight through; `AreaOfEffect` emits one single-target event per in-range, **non-source, alive** victim (friendly fire — no faction filter, XZ radius). Completes the job so the consumer can drain safely. (The plan's producer-written `aoeCount` was dropped — a parallel `NativeReference` increment is a race; AOE presence is detected by scanning `raw` on the main thread instead.) |
| `DamageEventSystem` | `CombatReactionSystemGroup/DamageEventSystem.cs` | Single reaction consumer. Burst main-thread `while (resolved.TryDequeue(...))`: skips already-`Dead` victims; **threat is faction-gated (v2)** — a `ThreatEntry` is added only when the source is *hostile* to the target (`source.Faction.factionType` ∈ target's `AttackFaction` buffer, mirroring `EnemyAwarenessSystem`), so friendly-fire and environmental damage never provoke fight-back (`REACTION_TIME 0.3s` / `THREAT_TTL 4s`); applies `damageAmount` to `Health`; on the lethal event captures killing-blow knockback into `Health.kill*` + enables `Dead`. Cost is O(hits-that-happened). |

> Player attack input lives in `PlayerAttackSystem` (`PlayerSystemGroup/PlayerInputSystemGroup/`), not here. Death-only knockback semantics are preserved — see [[project_combat_knockback]]. The enum `AttackType` was renamed **`DamageSource`** (adds `Fall`/`Hazard`/`Burn`/`Drown`); serialized SO fields named `attackType` became `damageSource` with `[FormerlySerializedAs]`.

---

### HealthSystemGroup (`Systems/HealthSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `DeathSystem` | `DeathSystem.cs` | First-death-frame work (`Dead` is enabled upstream in `DamageEventSystem`); latches on `UnitAction.current == ActionType.Death` (set here) so it runs once per death. Halts pathing, disables the package's `Movement`/`Gravity` (both enableable — Ragdoll2DSystem drives the corpse from here), fires `ActionInterruptRequest`, cancels in-flight `AttackRequest`. **Enables `PlayerInteractable` (if present) so a revivable corpse becomes targetable by the player reviver** (`PlayerTargetingSystem` scans `PlayerInteractable`; only `UndeadAuthoring` units carry it, baked disabled). `Alive` deprecated — `Dead` is the sole life-state. |
| `Ragdoll2DInitSystem` | `Ragdoll2DInitSystem.cs` | Runs after `DeathSystem`. Detects freshly dead units, reads `Health.kill*` (full `killSourcePosition` float3): derives `fallSideSign` (X) + a real 3D launch velocity (horizontal away from source × `killLaunchForceX` + up × `killLaunchForceY`), seeds `Ragdoll2DLaunch` (restitution = per-attack or `RagdollSimConfig` default, `airborne=1`), copies flail/spin onto `Ragdoll2D`, resets each `RagdollJoint`-flagged joint (zone target from its `RagdollLandingZone` buffer, launch-proportional trail kick on `angularVelocity`, baked settle/segment/weight preserved). Fully independent of the design `PartLibrary` blob |
| `HealRequestSystem` | `HealRequestSystem.cs` | Applies a heal request when enabled |
| `ReviveRequestSystem` | `ReviveRequestSystem.cs` | Consumes `ReviveRequest` on a corpse (`[WithAll(Dead)]`): heal, `Dead`→off, `Undead`→on, re-enables the package's `Movement`/`Gravity`, `UnitAction`→Idle (re-arms death latch), disables `PlayerInteractable` (alive again → no longer a reviver target). If the unit's `UnitDataBlob.becomesUnitType != None`, stamps + enables `SwapBrainRequest{newUnit}` and enables `Minion` (→ selectable). Re-enables `UtilityBrain`, fires `ActionInterruptRequest`. |
| `SwapBrainSystem` | `SwapBrainSystem.cs` | `[UpdateAfter(ReviveRequestSystem)]`. Consumes an enabled `SwapBrainRequest`: re-keys `UtilityBrain.unitType`/`UnitData.unitType`, `Faction`, and rebuilds the `AttackFaction`/`AvailableAttack`/`Motivation` buffers from `UnitDataLibrary[newUnit]`; fires `ActionInterruptRequest`; consumes the request via ECB. Generic brain-swap hook (revive, future feral turn, debug). Rebuilt motivations are zero-decay (blob has no decay data). |
| `ZombifySystem` | `ZombifySystem.cs` | `[UpdateAfter(ReviveRequestSystem)]` + `[UpdateBefore(SwapBrainSystem)]`. Consumes `ZombifyRequest` on a LIVING unit (`[WithDisabled(Dead)]`): counts down `delaySeconds`, resolves the target type (`None` → `UnitDataBlob.becomesUnitType`), then stamps + enables `SwapBrainRequest` (consumed the same frame, and it fires the `ActionInterruptRequest`) and `ChangeDesignRequest` (skin group → "Zombie" tag + `AlternateColorMode.Enable`, applied the same frame by `DesignSystemGroup`), and enables `Undead` if present. Defers a frame when a `SwapBrainRequest` is already in flight (a revive that frame) rather than clobbering it. Corpses convert through `ReviveRequestSystem`, not this. |
| `Ragdoll2DReviveSystem` | `Ragdoll2DReviveSystem.cs` | Runs after `ReviveRequestSystem`. Resets visual child + joint rotations to their pre-death pose and disables ragdoll components |
| `HealthBarSystem` | `HealthBarSystem.cs` | Syncs `HealthBar` visual entity scale to `Health` values |

> ⚠ **`Ragdoll2DSystem` does NOT run in HealthSystemGroup** — it lives in the declared `RagdollSystemGroup` (`LateSimulationSystemGroup`, SpawnInit → Ragdoll → Sound) so it runs *after* `ApplyAnimatedPoseSystem`, which stomps every part `LocalTransform` unconditionally each frame — this is also why sleeping corpses still re-write their settled rotations.

---

### LateSimulationSystemGroup (`Systems/LateSimulationSystemGroup/`)

Runs at end of frame. Safe zone for spawn/despawn and event cleanup.

Sub-group execution order:
```
SpawnSystemGroup → SpawnInitSystemGroup → RagdollSystemGroup → SoundSystemGroup → DespawnSystemGroup → SaveSystemGroup (OrderLast)
```

#### SpawnSystemGroup (`SpawnSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `UnitSpawnerSystem` | `UnitSpawnerSystem.cs` | Instantiates new or reclaims pooled body+brain entities; enables `NewlySpawned` on every body; cross-links `BrainLink`/`BodyLink` |

#### SpawnInitSystemGroup (`SpawnInitSystemGroup/`)

Runs after `SpawnSystemGroup` each frame. All systems filter on `[WithAll<NewlySpawned>]` — no-op on frames with no spawning.

| System | File | Purpose |
|---|---|---|
| `SpawnStateInitSystem` | `SpawnStateInitSystem.cs` | Resets root-entity enableable states: `Dead`/`Ragdoll2DLaunch`/`Undead`/`Minion`/`Revive`/`Selected`/pathfinding→off (units start alive = `Dead` disabled), `UtilityBrain`→on, the package's `Movement`/`Gravity`→on (a reclaimed pool unit must be able to move/fall again) |
| `BodyPartInitSystem` | `BodyPartInitSystem.cs` | Rebuilds the root `BodyPart` buffer on `NewlySpawned` units from `BodyPartInfo`+`BaseParent` (carries `partDef`+`flags`) — ECB.Instantiate does not reliably remap refs inside dynamic buffers. Replaces `AnimatorTargetInitSystem` |
| `Ragdoll2DSpawnInitSystem` | `Ragdoll2DSpawnInitSystem.cs` | Scans `LinkedEntityGroup` to force-disable `Ragdoll2D`/`RagdollJoint` on all child entities, and zeroes + disables the root's `Ragdoll2DLaunch` (airborne/sleeping flags) — fixes ECB.Instantiate enabled-bit copy and stale state on pool reclaims |
| `DesignRandomizeSystem` | `DesignRandomizeSystem.cs` | `[UpdateAfter(BodyPartInitSystem)] [UpdateBefore(MinionRestoreApplySystem)]` Rolls a per-character `CharacterPalette`: one shape tag per group from the AUTHORED `RandomTagOption` buffer (CharacterRigAuthoring.randomTags — authoring decides randomness; unlisted tags like "Zombie" never roll) + one colour index per referenced `ColorPaletteType` (full palette length; slot [min,max] windows narrow it at apply) + a random shape per `DesignSlot`-flagged `BodyPart` into `PersistedDesign`; resets alternate-colour mode, disables `RandomizeDesign`. `IJobEntity.ScheduleParallel`, requires both library singletons |
| `DesignApplySystem` | `DesignApplySystem.cs` | `[UpdateAfter(MinionRestoreApplySystem)] [UpdateBefore(SpawnInitCleanupSystem)]` Re-derives every `DesignSlot` part's slice + colours from `PersistedDesign` shapes + `CharacterPalette` through the `PartLibrary` + `ColorPaletteLibrary` blobs; writes `baseImageIndex` + `ImageIndex` + the 3 tint components (`BodyPartTint`/`Secondary`/`Tertiary`) on the main thread. Restored shapes/colours win. Shares `DesignApplyUtil.ApplyDesign` with `DesignChangeSystem` |
| `UnitHealthInitSystem` | `UnitHealthInitSystem.cs` | Stamps `Health` from `UnitDataBlob.maxHealth` (keyed by `UnitData.unitType`) so health is authored once per unit type on `UnitSO`, not per prefab. `[UpdateBefore(MinionRestoreApplySystem)]` — a restored minion's saved health must overwrite this, not the reverse. A `maxHealth` of 0 is skipped, leaving whatever `HealthAuthoring` baked, so an un-filled `UnitSO` cannot spawn units dead |
| `SpawnInitCleanupSystem` | `SpawnInitCleanupSystem.cs` | `[OrderLast]` Disables `NewlySpawned`; component persists on entity for re-enablement on next pool reclaim |

### DesignSystemGroup (`Systems/DesignSystemGroup/`)

Runs in `SimulationSystemGroup`, `[UpdateAfter(HealthSystemGroup)] [UpdateBefore(AnimationSystemGroup)]` — so a conversion-fired re-skin lands after revive/swap logic and before `UpdateImageIndexSystem` pushes `_ImageIndex`.

| System | File | Purpose |
|---|---|---|
| `DesignChangeSystem` | `DesignChangeSystem.cs` | Consumes enabled `ChangeDesignRequest`: applies `paletteChanges` (shape tags) + `alternateColorMode` (Enable/Disable → `CharacterPalette.useAlternateColors`) + `shapeOverrides` to `PersistedDesign`, then re-derives EVERY `DesignSlot` part's slice + colours (`DesignApplyUtil.ApplyDesign`) and disables the request. Zombify = `paletteChanges("Skin"→"Zombie")` + `alternateColorMode = Enable` (every palette entry shows its zombie `alternative`, rolled identity kept). Main-thread writes, requires `PartLibrary` + `ColorPaletteLibrary` |

#### SoundSystemGroup (`SoundSystemGroup/`)

"ECS decides, MonoBehaviour plays." Runs late (after SpawnInit, before Despawn) so it sees every sound emitted during gameplay + spawn-init. `AudioManager` (`MonoBehaviours/Managers/AudioManager.cs`, `PersistentSingleton`) reads the singletons each LateUpdate and drives a 32-AudioSource pool through an AudioMixer.

| System | File | Purpose |
|---|---|---|
| `VoiceSelectionSystem` | `VoiceSelectionSystem.cs` | Owns the `ResolvedVoices` singleton (`NativeList`, Persistent). Gathers one-shot `PlaySound` + enabled `LoopingSound`, scores by priority+distance to `ListenerPosition`, applies per-type `maxConcurrent`, writes top ≤32, then `DestroyEntity` the `PlaySound` query (loops persist). Not Burst (structural change). |
| `WorldMoodSystem` | `WorldMoodSystem.cs` | Sets the `WorldMood` singleton from camera-visible state (`CameraView`): `AttackRequest` in view → Combat, non-empty `ThreatEntry` in view → Tension, else Explore. Idempotent via `WorldMoodUtil`. |
| `MusicStateSystem` | `MusicStateSystem.cs` | Maps `WorldMood` → `MusicState` layer target weights (AudioManager crossfades to them). |

One-shot SFX are emitted via `SoundUtil.Play/PlayOn` (ECB; the LogMessage pattern). Animation-locked SFX fire from `AnimationSoundMarkerSystem` (`AnimationExecutionSystemGroup`, after `AnimationTimeSystem`) on clip marker crossings. Behaviour cues use the `PlaySound` `BehaviorCommandType` (fire-and-advance in `BehaviorExecutionSystem`). Blob: `SoundLibraryBakingSystem` (PostBakingSystemGroup) builds `SoundLibraryBlob` enum-indexed (clips stay managed on `AudioManager`).

#### DespawnSystemGroup (`Systems/DespawnSystemGroup/` — top-level folder; Spawn/SpawnInit/Despawn are LateSimulation siblings, not nested groups)

| System | File | Purpose |
|---|---|---|
| `UnitPoolReturnSystem` | `UnitPoolReturnSystem.cs` | Adds `Disabled` to units > 200 units from the player; returns them to the pool |

#### RagdollSystemGroup (`Systems/RagdollSystemGroup/`)

Declared in `SystemGroups.cs` (SpawnInit → Ragdoll → Sound). Everything that moves a corpse after death lives here; init/revive stay in `HealthSystemGroup`.

| System | File | Purpose |
|---|---|---|
| `Ragdoll2DSystem` | `Ragdoll2DSystem.cs` | One `ScheduleParallel` job over ragdolling roots (children written via `[NativeDisableParallelForRestriction]` lookups — disjoint per root). ① FLIGHT: integrates the float3 launch velocity, ground height from a `CollisionWorld` raycast (GROUND/STRUCTURES/OBJECTS + corpse-pile offset from `CorpseCells`), wall bounces (STRUCTURES/WALLS) via restitution. ② FLAIL: each joint = 1-segment pendulum (angle + angular velocity) driven by gravity minus anchor acceleration (weightless in freefall); impacts kick it. ③ SETTLE: grounded joints exp-lerp to their authored zone angle (v1 formula) + flail rings out; body tilt steps to ±88° with airborne spin settling to the nearest full turn. Quiet corpses set `sleeping=1` — dynamics skip but settled rotations are still re-written (ApplyPoseJob stomps). Registers its read with `CorpseCellSystem.AddJobHandleForReader`. Falls back to built-in `RagdollSimConfig` defaults when the authoring isn't baked |

#### Raw LateSimulationSystemGroup members (documented conformance exemptions)

| System | File | Purpose |
|---|---|---|
| `OrderMarkerSystem` / `SelectedVisualSystem` | `PresentationSystemGroup/` | Presentation visuals that run after simulation settles |

---

### SaveSystemGroup (`Systems/SaveSystemGroup/`)

Runs `OrderLast` inside `LateSimulationSystemGroup` — after all spawns, despawns, and events have settled. Systems that need to trigger a save or load enable `SaveRequest` / `LoadRequest` (both `IEnableableComponent`) on the GameData entity (identified by `GameDataTag`). The MonoBehaviour `SaveLoadBridge` (`MonoBehaviours/SaveLoadBridge.cs`) is the UI/manual seam — its `RequestSave/RequestLoad` flip those requests and it draws a debug 2-button OnGUI. See [[Components]] for the component definitions and [[Data]] for save file DTOs.

**Generic, marker-driven serializer (v1).** Saving is no longer hand-written per-field. Any unmanaged value component marked with the `IPersist` interface (`Components/Save/PersistComponents.cs`) — plus an explicit externals list for unmodifiable Unity types like `LocalTransform` — is snapshotted automatically. `PersistRegistry` builds the saveable `ComponentType` set once (assembly scan ∪ externals, excluding any type with an `Entity`/`BlobAssetReference` field). `SaveSerialization` is the encoder seam: it copies each component's **raw bytes → Base64** (reflection `GetComponentData<T>` + `Marshal`/`GCHandle` pin) and back. To persist a new component, just add `, IPersist` to it — no system changes.

**v1 scope:** persists the **Player** and **GameData** singletons only (located by tag — no `PersistId`/minion/remap yet). Currently marked `IPersist`: `Health`, `PlayerEquipmentSlots`, `GameSettings`, `PlayTimeTracker` (+ `LocalTransform` via externals). Deferred to later `Save_System.md` phases: `PersistId` + multi-entity iteration, minion respawn, `EntityRemapSystem` (Entity-field relinking), travel autosave, design buffers, slot UI.

| System | File | Purpose |
|---|---|---|
| `PlayTimeTrackerSystem` | `PlayTimeTrackerSystem.cs` | Accumulates `DeltaTime` into `PlayTimeTracker.totalSeconds` each frame |
| `AutoSaveTimerSystem` | `AutoSaveTimerSystem.cs` | Ticks `AutoSaveTimer`; enables `SaveRequest { slot = 0 }` when interval elapses |
| `PersistentSaveSystem` | `PersistentSaveSystem.cs` | Consumes `SaveRequest`; `SaveSerialization.WriteEntity` over Player+GameData singletons → `SaveFile` DTO → JSON on disk |
| `PersistentLoadSystem` | `PersistentLoadSystem.cs` | Consumes `LoadRequest`; reads JSON → `SaveSerialization.ApplyEntity` restores each record onto its singleton (by `role`) |

`SaveSerialization.cs` (non-system helper) holds the encoder; `SaveSystem.cs`/`LoadSystem.cs` were **deleted** (superseded).

**Slot convention:** slot `0` = auto-save, slots `1–3` = manual slots.  
**Save path:** `Application.persistentDataPath/save_slot_{N}.json` (see `SavePaths.cs`).  
**No `[BurstCompile]`** on `PersistentSaveSystem` / `PersistentLoadSystem` / `SaveSerialization` — reflection, `JsonUtility`, and `System.IO` are managed. `PlayTimeTrackerSystem` and `AutoSaveTimerSystem` are fully Burst-compiled.

---

### PresentationSystemGroup (`Systems/PresentationSystemGroup/`)
Runs after all transforms have settled.

| System | File | Purpose |
|---|---|---|
| `InteractionHighlightSystem` | `InteractionHighlightSystem.cs` | Highlights the player's current interaction target |
| `OrderMarkerSystem` | `OrderMarkerSystem.cs` | Move-order marker visual (updates in raw `LateSimulationSystemGroup` — exempted) |
| `SelectedVisualSystem` | `SelectedVisualSystem.cs` | Shows/hides selection circle visual under units (raw `LateSimulationSystemGroup` — exempted) |

> `OutlineSystem` / `OutlineLayerUpdateSystem` are **parked** in `Core/Unused/`.

---

## Spawn Init Pattern

Every body entity has `NewlySpawned : IComponentData, IEnableableComponent` baked **disabled** by `UnitAuthoring.Baker`. `UnitSpawnerSystem` enables it at spawn time (new instantiation and pool reclaim). `SpawnInitCleanupSystem` disables it `[OrderLast]` so it persists across pool cycles.

### How to add a new component with a spawn-frame default state

Root-entity enableable component (e.g., a new state flag):
1. Add a `ComponentLookup<T>` field to `SpawnStateInitSystem`
2. Update it in `OnUpdate` with `.Update(ref state)`
3. Add `if (_lookup.HasComponent(entity)) _lookup.SetComponentEnabled(entity, true/false);` to the `foreach` body
4. **No edit to `UnitSpawnerSystem` needed.**

### How to add a new spawn-frame init system

Any logic that must run once on a freshly spawned entity:
```csharp
[UpdateInGroup(typeof(SpawnInitSystemGroup))]
public partial struct MySpawnInitSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (...) in
            SystemAPI.Query<...>()
                .WithAll<NewlySpawned>()  // ← required filter
                .WithAll<MyComponent>()   // ← filter for entities relevant to this system
                .WithEntityAccess())
        {
            // init logic
            // do NOT disable NewlySpawned here — SpawnInitCleanupSystem handles it
        }
    }
}
```

### Rules

- **Never add per-system signal components** (e.g., `NeedsMyInit`). `NewlySpawned` is the single shared signal.
- **Never remove `NewlySpawned`** — only disable it (done by `SpawnInitCleanupSystem`). Removal would break pool reclaim.
- **Child entity component resets** (anything not on the root body entity) must use `DynamicBuffer<LinkedEntityGroup>` scanning, not stored entity refs from baked components. `LinkedEntityGroup` is always correctly remapped by `ECB.Instantiate`; baked entity refs in `IComponentData` / buffers may not be.
- **Structural changes** (AddComponent, RemoveComponent, Instantiate) cannot happen in `SpawnInitSystemGroup` — these require ECB and belong in `UnitSpawnerSystem`.

---

DOTS/ECS coding conventions: [[RULES]].
