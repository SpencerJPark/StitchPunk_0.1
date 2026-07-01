---
tags: [memory, code, components, ecs]
related: "[[Systems]], [[Authoring]], [[Data]]"
---

# Components — Context

Component files are **pure data structs**. No methods, no logic, no Unity API calls. See [[RULES]] for the broader coding conventions and [[Authoring]] for how components get baked onto entities.

---

## Conventions

- `IComponentData` — single value or small fixed set of values on an entity.
- `IBufferElementData` — variable-length list on an entity (e.g. `ActionOption` buffer for scored actions).
- `IEnableableComponent` — tag or component that can be toggled on/off without structural change. Prefer this over adding/removing when the component will toggle frequently.
- Group related components in one file (e.g. `AIComponents.cs`, `UnitComponents.cs`).
- Never put helper methods or logic inside a component. If you need a helper, put it in the matching `Utils/` file.

---

## Component Files

| File | Path | Contains |
|---|---|---|
| `AIComponents.cs` | `Components/AI/` | `Brain` (BrainType enum), `ActiveBrain` (enableable), `Awareness`, `SelectedAction`, `NeedsAction` (enableable), `ActionOption` buffer, `Behaviour` buffer, `PlayerControlled` (enableable), `PlayerOrder` |
| `Brains.cs` | `Components/AI/` | Brain-type tags: `CitizenBrain`, `ZombieBrain` |
| `Motivations.cs` | `Components/AI/` | One struct per motivation (9 core + personality traits) |
| `Interactions.cs` | `Components/AI/` | `Interaction`, `InteractionTimer`, `InteractionOccupant` buffer, interaction-type components |
| `SpatialHashRegistry.cs` | `Components/AI/` | `SpatialHashRegistry` singleton (two NativeParallelMultiHashMaps) |
| `CombatAI.cs` | `Components/AI/` | `Faction`, `ThreatEntry` buffer, `CombatTarget` (enableable), `ChaseConfig`, `MeleeAttackConfig`, `FactionRegistry` singleton |
| `AnimationComponents.cs` | `Components/Animation/` | `AnimationLayer` buffer, `AnimatorTarget` buffer, `AnimationTargetTag`, `AnimationTargetRestPose`, `AnimationTargetPose`, `ImageIndex`, `ImageIndexOverride`, `Billboard`, `BaseParent` |
| `DamageEvent.cs` | `Components/Combat/` | `DamageEvent` — **plain value struct** (NOT `IComponentData`), queued in the `DamageBus`. `sourceEntity` (Null = environmental) + `damageSource` + damage/knockback/AOE fields |
| `DamageBus.cs` | `Components/Combat/` | `DamageBus` singleton — recycled `NativeQueue<DamageEvent>` transport (`raw` + `resolved`). Owned/disposed by `DamageBusSystem` |
| `Hazard.cs` | `Components/Combat/` | `HazardZone` — proximity damage zone (spikes): `damageAmount`, `damageSource`, `radius`, `retriggerInterval`, `lastTriggerTime` (whole-zone gate), kill-knockback fields |
| `UnitComponents.cs` | `Components/Units/` | `Unit`, `UnitData`, `UnitStateData`, `UnitAction`, `Dead`, `Health`, `HealthBar`, `Attack`, `AttackData`, `AttackCooldown`, `Selected`, `Undead`, `Revive`, `Minion`, `PlayerImmune`, `Heal` |
| `UnitDesignComponents.cs` | `Components/Units/` | `UnitSkinColor`, `UnitHairColor`, `UnitHeadShape`, `UnitNoseShape`, `RandomizeDesign`, design tags |
| `DesignComponents.cs` | `Components/Units/` | Unit Design System: `DesignPart` buffer + `DesignRange` buffer (baked config), `DesignSlot` (blittable entry), `PersistedDesign` (`IPersist`, chosen indices — auto-saved), `ChangeDesignRequest` (enableable, runtime re-skin batch). All on the root body entity |
| `UnitVisualComponents.cs` | `Components/Units/` | `Outline`, `OutlineChild`, `OutlinedTag` |
| `MovementComponents.cs` | `Components/Movement/` | `UnitMover`, `UnitGravity`, `HordeMembership`, `Horde`, `HordeMemberBuffer`, `SetupUnitMoverDefaultPosition` |
| `PathfindingComponents.cs` | `Components/Movement/` | `PathfindingAgent`, `PathRequest`, `DStarLiteFollower`, `FlowFieldFollower` |
| `SpawnerComponents.cs` | `Components/Spawners/` | `UnitSpawner`, `PoolOwner`, `NeedsAnimatorInit` |
| `PlayerComponents.cs` | `Components/Player/` | `Player`, `PlayerData`, `PlayerInputData`, input enable-tag components, `AimDirection`, `AimIndicatorRef` |
| `PlayerEquipmentComponents.cs` | `Components/Player/` | `OnPlayerReviverEquipt` (enableable) — fired by `PlayerEquipmentInputSystem` when Reviver slot is activated |
| `PlayerMinionCommandComponents.cs` | `Components/Player/` | `OnMinionMoveCommand` (enableable, float3 destination), `OnMinionInteractCommand` (enableable, Entity targetEntity) — written by `UnitSelectionManager`, consumed by `MinionCommandSystem` |
| `Ragdoll2DComponents.cs` | `Components/Units/` | `Ragdoll2D` (enableable, on visual root child), `Ragdoll2DJoint` (enableable, on joint pivot entities), `Ragdoll2DConfig` (static config on root body), `Ragdoll2DJointRef` buffer |
| `ItemComponents.cs` | `Components/Items/` | `Item`, `UnitEquipt`, `EquiptSocket`, `EquiptBy`, `AttachedTo`, `EquipAction`, `AttachItemRequest`, `SpawnItemRequest`, `DespawnItemRequest`, `ThrownItemRequest`. A **loose** (pickable) item has `EquiptBy.owner == Entity.Null` |
| `ItemLibraryComponents.cs` | `Components/Items/` | `ItemLibrary` (singleton blob holder), `ItemLibraryReference` (bake-time `UnityObjectRef<ItemLibrarySO>`) — item `ItemCategory` + effect data for AI item awareness. `PickupItemAction` tag itself lives in `AiComponents.cs` |
| `EntityLibraries.cs` | `Components/EntityLibraries/` | Singleton blob holders: `ScoringLibrary`, `AnimationLibrary`, `UnitDataLibrary`, `AttackLibrary`, `FactoryLibrary`, `UnitPrefabEntry` |
| `FactoryComponents.cs` | `Components/Structures/` | `FactoryStation`, `StationInputSlot` buffer, `StationOutputSlot` buffer, `ProductionProgress` (enableable), `StationWorkerSlot` buffer, `FactoryGridConfig` singleton, `FactoryGridCell` buffer |
| `RegistryComponents.cs` | `Components/Registry/` | `HordeRegistry` |
| `SceneTags.cs` | `Components/Tags/` | `MainMenuTag`, `GameSceneTag` |
| `GameDataComponents.cs` | `Components/Save/` | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` (now incl. master/music/sfx/ambient volume floats — persisted via IPersist) |
| `Dialogue.cs` | `Components/AI/` | `DialogueProvider` (enableable, on NPC), `DialogueManagerTag`, `ActiveDialogue` (enableable, singleton), `OnDialogueEvent` (enableable, singleton), `PlayedDialogue` (buffer on GameData), `DialogueFlag` (buffer on GameData) |
| `SoundComponents.cs` | `Components/Audio/` | Sound System: `PlaySound` (one-frame one-shot signal — LogMessage lifecycle), `LoopingSound` (enableable, on emitter), `ListenerPosition` + `CameraView` (singletons written by AudioManager), `ResolvedVoice` + `ResolvedVoices` (singleton `NativeList`, owned by VoiceSelectionSystem), `MusicState` (singleton layer weights) |
| `SoundLibraryComponents.cs` | `Components/Audio/` | `SoundLibrary` (singleton blob holder), `SoundLibraryReference` (bake-time `UnityObjectRef<SoundLibrarySO>`) |
| `WorldStateComponents.cs` | `Components/World/` | `WorldMoodState` enum (Explore/Tension/Combat) + `WorldMood` singleton — global mood written idempotently via `WorldMoodUtil`; read by music now + NPC behaviours later |

---

## AI Components (`Components/AI/`)

Used by [[Systems_AI]].

### `AIComponents.cs` — AI state (all on the single unit entity)

> No separate brain entity. No BodyLink/BrainLink. Everything is on the same entity.

```
Brain                       BrainType activeBrain
ActiveBrain (enableable)    tag — disabled on dead/inactive units
Awareness                   float range
SelectedAction              ActionCategory category, Entity targetEntity, float3 targetPosition
NeedsAction (enableable)    tag — enable to trigger AI scoring/selection pipeline
ActionOption (buffer)       float score, ActionCategory category, Entity targetEntity, float3 targetPosition
Behaviour (buffer)          BehaviourType behaviourType, MotivationType targetMotivation,
                            ActionType actionType, int value, DamageSource damageSource,
                            FactionType hostileFaction, float range
PlayerControlled (enableable) tag — enabled when minion is under player command
PlayerOrder                 float3 destination, Entity targetEntity, CommandType commandType

PlayerControlled (enableable)   tag — on brain entity. When enabled, ActionSelectionSystem is bypassed.
                                MinionAutoCounterSystem disables this when the minion takes damage.
                                ⚠ Must be baked (disabled) on ALL brain types via BrainBakeHelper.AddRequirements.
                                  ActionSelectionJob uses [WithDisabled], which requires the component to be present —
                                  brains that lack it entirely are silently excluded from the AI pipeline.
PlayerOrder                     float3 destination, Entity targetEntity — on brain entity.
                                Written by MinionCommandSystem. Drives SelectedAction / PathRequest
                                while PlayerControlled is enabled.
```

### `Brains.cs` — Brain personality type tags

```
CitizenBrain    tag — marks this brain as a citizen AI
ZombieBrain     tag — marks this brain as a zombie AI
```

### `Motivations.cs` — One component per motivation need

Each has a single `int value` in range `[-100, 100]`. Negative = depleted, positive = satisfied.

**Core (9 — drive the scoring pipeline):**
```
HungerMotivation
EnergyMotivation
FunMotivation
SocialMotivation
ComfortMotivation
BladderMotivation
SafetyMotivation
MovementMotivation
SelfPreservationMotivation
```

**Personality traits (randomly assigned, modify scoring weights):**
```
BookwormMotivation
WorkMotivation
NightOwlMotivation
EarlyBirdMotivation
GluttonMotivation
GrumpyMotivation
DepressedMotivation
LazyMotivation
NervousMotivation
```

> ⚠ **Each motivation is a separate `IComponentData` struct.** There is no single "Motivations" component. Querying a specific motivation requires naming its struct.

### `Interactions.cs` — Interaction point data (lives on waypoint/building entities)

```
InteractionProvider (enableable)    tag — marks entity as an active interaction source
Interaction                         float interactionRange, ActionType actionType, int maxOccupants
InteractionTimer (enableable)       float maxTime, float duration, float elapsed
InteractionOccupant (buffer)        Entity entity, MotivationType motivationType, float score
InteractionHandled (enableable)     tag — interaction is currently being served
```

Interaction-type components (mark what a waypoint satisfies — one per object):
```
HungerInteraction / EnergyInteraction / FunInteraction / SocialInteraction
ComfortInteraction / BladderInteraction / SafetyInteraction / MovementInteraction
```
Each has `int value` — the satisfaction amount granted per use.

### `SpatialHashRegistry.cs` — Spatial lookup singleton

```
SpatialHashRegistry (singleton on one entity)
    NativeParallelMultiHashMap<int2, Entity>                waypointCells       — general waypoint lookup
    NativeParallelMultiHashMap<SpatialInteractionKey, Entity> interactionCells   — filtered by (cell, MotivationType)
```

---

## Animation Components (`Components/Animation/`)

Used by [[Systems_Animation]].

### `AnimationComponents.cs`

```
AnimationLayer (buffer, capacity 8)
    AnimationLayerType  layer
    AnimationType       animation
    float               time
    float               speed
    bool                active
    bool                looping

AnimatorTarget (buffer, capacity 32)
    Entity              entity      — the quad entity for this body part
    AnimationTarget     target      — which body part (enum)
    ⚠ Entity refs here are NOT remapped by ECB.Instantiate.
      AnimatorTargetInitSystem rebuilds this buffer for newly spawned units via NeedsAnimatorInit.

AnimationTargetTag          AnimationTarget target  — on each quad entity, names which part it is
BaseParent                  Entity baseParentEntity — on each quad, points to the root body entity (IS remapped by Instantiate)
AnimationTargetRestPose     float3 localPosition, float rotation, float2 scale, int baseImageIndex
AnimationTargetPose         float3 localPosition, float rotation, float2 scale, int imageIndex
ImageIndex                  int index, bool onUpdate
ImageIndexOverride          float Value  — MaterialProperty("_ImageIndex"), drives GPU texture index
Billboard                   Entity parentEntity
```

---

## Unit Components (`Components/Units/`)

### `UnitComponents.cs`

```
Unit                    tag — marks this as a unit body entity
UnitData                UnitType unitType
UnitStateData           UnitState state
UnitAction              ActionType current

Dead (enableable)       tag — SOLE life-state: enabled = dead, disabled = alive (Alive deprecated)

DamageEvent             PLAIN VALUE STRUCT (not IComponentData) queued in the DamageBus (v2).
                        targetEntity, sourceEntity (Null = environmental/sourceless), damageSource,
                        damageAmount, distance, kill-knockback fields, + AOE (damageBehaviour,
                        sourcePosition, range). Producers Enqueue → DamageResolutionSystem expands AOE →
                        DamageEventSystem drains. No entity create/destroy. See
                        Tasks/Verification/DamageEvent_v2_System.md
DamageBus (singleton)   recycled NativeQueue<DamageEvent> raw + resolved (Components/Combat/DamageBus.cs).
                        Owned/created/disposed by DamageBusSystem (Persistent). Manual job-dep wiring —
                        AddJobHandleForProducer / ProducerHandle (ECB-owner pattern).
HazardZone              proximity damage zone (Components/Combat/Hazard.cs) — spike-trap example.
                        damageAmount, damageSource, radius, retriggerInterval, lastTriggerTime.
Health                  int healthAmount, int healthAmountMax
HealthBar               Entity barVisualEntity, Entity healthEntity
CombatTarget            Entity entity
AttackCooldown          float timer
Attack (enableable)     tag
AttackData              DamageSource damageSource
PlayerImmune            tag — prevents player attack targeting

Heal (enableable)       int healAmount
Undead (enableable)     tag — unit is player-controlled undead
Revive (enableable)     tag
Minion (enableable)     tag — unit is a player minion

Selected (enableable)
    Entity  visualEntity
    float   showScale
    bool    onSelected      — true on the frame selection starts
    bool    onDeselected    — true on the frame selection ends
```

---

## Movement Components (`Components/Movement/`)

Used by [[Systems_Movement]].

### `MovementComponents.cs`

```
UnitMover
    float   moveSpeed
    float   rotationSpeed
    float3  targetPosition
    bool    isMoving

UnitGravity
    float   fallSpeed
    float   verticalVelocity

SetupUnitMoverDefaultPosition   tag — triggers SetupUnitMoverDefaultPositionSystem on first frame

HordeMembership (enableable)
    int     hordeId
    Entity  hordeEntity
    float2  formationOffset
    int     priority

Horde
    int     hordeId
    float3  targetPosition
    Entity  targetEntity
    int     flowFieldIndex
    int     memberCount
    bool    isActive
    bool    needsPathUpdate
    int     behaviorFlags

HordeMemberBuffer (buffer, capacity 16)
    Entity  memberEntity
```

### `PathfindingComponents.cs`

```
PathfindingAgent
    PathfindingMode     preferredMode       — DStarLite / FlowField / None
    PathfindingMode     currentMode
    float               repathInterval
    float               timeSinceLastRepath
    int                 hordeFormationThreshold
    bool                needsRepath
    float3              targetPosition
    bool                isActive

PathRequest (enableable)
    float3          targetPosition
    PathfindingMode requestedMode

DStarLiteFollower (enableable)
    int     currentNodeIndex
    int     goalNodeIndex
    float3  nextWaypoint
    float3  targetPosition
    int     pathDataIndex
    float3  lastMoveDirection
    int     currentLayer

FlowFieldFollower (enableable)
    float3  targetPosition
    float3  lastMoveVector
    int     flowFieldIndex
    int     currentLayer
```

---

## Spawner Components (`Components/Spawners/`)

```
UnitSpawner (enableable)
    UnitType    unitType
    int         spawnCount
    float       range

PoolOwner                   UnitType unitType — on every pooled entity (body + brain), active or dormant
NeedsAnimatorInit           tag — added by UnitSpawnerSystem; consumed and removed by AnimatorTargetInitSystem same frame
```

---

## Entity Library Components (`Components/EntityLibraries/`)

These are singletons — one entity in the world holds them. Access via `SystemAPI.GetSingleton<T>()`. See [[Gotchas]] for the `UnitPrefabEntry` singleton trap.

```
ScoringLibrary          BlobAssetReference<AIScoringLibraryBlob>  library
AnimationLibrary        BlobAssetReference<AnimationLibraryBlob>  library
UnitDataLibrary         BlobAssetReference<UnitLibraryBlob>       library
AttackLibrary           BlobAssetReference<AttackLibraryBlob>     library

UnitPrefabEntry         Entity maleCitizenPrefab, Entity maleCitizenBrainPrefab
    ⚠ Lives on the library entity AND on baked prefab entities.
      Use GetSingletonEntity<UnitDataLibrary>() then GetComponent<UnitPrefabEntry>(libraryEntity).
      Do NOT use GetSingleton<UnitPrefabEntry>() — it will throw because multiple entities match.
```

---

## Player Components (`Components/Player/`)

```
Player              Entity interactableEntity
PlayerSettings      (reserved)
PlayerActionMap     ActionMaps activeActionMap

-- Input events (all IEnableableComponent, baked disabled, written each frame by PlayerInputManager) --
MovePlayerInput         float2 moveInput
LookPlayerInput         float2 lookInput
CursorPlayerInput       float2 cursorInput
ZoomPlayerInput         float zoomInput
OnAttackPlayerInput     tag
OnInteractPlayerInput   tag
OnRollPlayerInput       float rollTime
OnSneakPlayerInput      tag
OnEquipmentSlotPlayerInput  int slot
OnDropPlayerInput       tag
AimPlayerInput          float aimValue (0–1 trigger axis)

-- Equipment --
PlayerEquipmentSlots    ItemType itemSlot1/2/3/4
OnPlayerReviverEquipt (enableable)  ItemType itemType — fired by PlayerEquipmentInputSystem

-- Minion commands (written by UnitSelectionManager, consumed by MinionCommandSystem) --
OnMinionMoveCommand (enableable)    float3 destination
OnMinionInteractCommand (enableable) Entity targetEntity

-- Aim --
AimDirection            float3 direction — current XZ aim direction, updated by PlayerAimSystem
AimIndicatorRef         Entity visualEntity — arrow indicator (scale 1 while aiming, 0 while not)
```

---

## Save / Game Data Components (`Components/Save/GameDataComponents.cs`)

All live on the single **GameData entity** (identified by `GameDataTag`). Access via `SystemAPI.GetSingleton<T>()`. See [[Data]] for save file DTOs.

```
GameDataTag             singleton identity — use GetSingletonEntity<GameDataTag>() to find the entity

SaveRequest (enableable)    int slot    — enable to trigger a save (slot 0 = auto, 1–3 = manual)
LoadRequest (enableable)    int slot    — enable to trigger a load

AutoSaveTimer
    float elapsedSeconds    — time since last auto-save
    float intervalSeconds   — authored interval (default 300s); set in GameDataAuthoring inspector

PlayTimeTracker
    double totalSeconds     — accumulated play time; double for long-session precision

GameSettings
    int animationFrameRate  — flipbook playback rate shared by all animated units; saved/loaded per slot
```

---

## Narrative Components (`Components/Narrative/NarrativeComponents.cs`)

All components live on three entity types: the **NarrativeEvent singleton** (baked by `NarrativeEventAuthoring`), **trigger zone entities** (baked by `NarrativeEventTriggerAuthoring`), and **addressable NPC/waypoint entities** (baked by `NarrativeEntityIdAuthoring`).

```
-- NarrativeEvent singleton entity --
NarrativeEventTag               singleton identity — GetSingletonEntity<NarrativeEventTag>()

OnNarrativeEvent (enableable)
    int eventId                 — which narrative event to run; maps to NarrativeIds.Events
    ⚠ Enabled by ECS systems (NarrativeProximitySystem or NarrativeDialogueBridgeSystem).
      Consumed and disabled by NarrativeEventManager (MonoBehaviour) in the same Update() frame.

ActiveNarrativeEvent (enableable)
    tag — enabled by NarrativeEventManager while an event is executing.
    NarrativeProximitySystem checks this to block new triggers until the event finishes.

-- Trigger zone entities --
NarrativeTrigger (enableable)
    int   eventId              — which event to fire; maps to NarrativeIds.Events
    float range                — player proximity radius in world units
    bool  repeatable           — when true, NarrativeEventManager re-enables after event ends

-- Addressable NPC / waypoint entities --
NarrativeEntityId
    int id                     — unique entity ID; maps to NarrativeIds.Entities
    Used by NarrativeEventManager to build a Dictionary<int, Entity> at startup for O(1) lookup.
```

**ID registry:** All event IDs, entity IDs, and dialogue bridge pairs live in `NarrativeIds.cs` (`NarrativeIds.Events`, `NarrativeIds.Entities`, `NarrativeIds.DialogueBridge.Pairs`).
**SO data:** `NarrativeEventSO` defines the action groups. `NarrativeToggleType` enum lists known toggleable component types. Both live in `Data/SOs/NarrativeEventSO.cs`.

---

## Dialogue Components (`Components/AI/Dialogue.cs`)

All components live on two entities: the **DialogueManager singleton** (baked by `DialogueManagerAuthoring`) and each **NPC entity** (baked by `DialogueProviderAuthoring`). Persistent state (`PlayedDialogue`, `DialogueFlag`) lives on the **GameData entity**.

```
-- NPC entity --
DialogueProvider (enableable)
    int sequenceId          — primary sequence ID, maps to DialogueIds.Sequences
    int refresherSequenceId — refresher ID (-1 if none); plays after primary has been seen

-- DialogueManager singleton entity --
DialogueManagerTag          singleton identity — GetSingletonEntity<DialogueManagerTag>()

ActiveDialogue (enableable)
    int    sequenceId       — which sequence is currently playing
    Entity speakerEntity    — the NPC that triggered it

OnDialogueEvent (enableable)
    int    eventId          — event constant from DialogueIds.Events
    ⚠ Enabled by DialogueUIManager (MonoBehaviour) when a choice fires an event.
      DialogueEventSystem disables it after one ECS frame so downstream systems get exactly one frame to react.

-- GameData entity (persistent, serialized) --
PlayedDialogue (buffer)     int sequenceId — tracks which primary sequences have been seen
DialogueFlag (buffer)       int flagId     — story flags set by player choices
```

**ID registry:** All sequence, flag, and event IDs are constants in `DialogueIds.cs` (`DialogueIds.Sequences`, `DialogueIds.Flags`, `DialogueIds.Events`). Add a constant there whenever you create a new `DialogueSequenceSO` or define a new flag/event.

---

## Fake Ragdoll Components (`Components/Units/Ragdoll2DComponents.cs`)

Visual-only death ragdoll for quad-based characters. All components are `IEnableableComponent` — they stay on the entity permanently and are toggled to support revive. See [[Gotchas]] for known ragdoll bugs.

```
Ragdoll2D (enableable, on visual root child entity)
    float       fallSpeed           — authored, used as fall speed reference
    float       bodyZAngle          — current Z tilt (simulation state, reset each death)
    quaternion  initialRotation     — captured from LocalTransform at death (used by revive to restore)
    float       fallSideSign        — +1 fall right / -1 fall left (set from Health.killSourceX, captured by DamageEventSystem)

Ragdoll2DJoint (enableable, on joint pivot entities)
    float       groundBuffer        — how far above root.Y to stop the joint (prevents ground clip)
    float       zAngularVelocity    — current angular speed in deg/s (set randomly 120–360 on death)
    float       currentZAngle       — current Z rotation offset from rest (simulation state)
    quaternion  initialLocalRotation — captured at death (used by revive to restore)

Ragdoll2DConfig (static, on root body entity — never toggled)
    Entity      visualRoot          — the visual child entity that tilts on Z
    float       groundBuffer        — authored default, propagated to joints at death
    float       fallSpeed           — authored, passed through to Ragdoll2D

Ragdoll2DJointRef (buffer, capacity 8, on root body entity)
    Entity      joint               — each joint pivot entity
```

**Entity placement:**
- `Ragdoll2DConfig` + `Ragdoll2DJointRef` buffer → **root body entity** (baked by `Ragdoll2DRootAuthoring`)
- `Ragdoll2D` → **visual root child** (added disabled by `Ragdoll2DBakingSystem`)
- `Ragdoll2DJoint` → **each joint pivot entity** (added disabled by `Ragdoll2DBakingSystem`)
