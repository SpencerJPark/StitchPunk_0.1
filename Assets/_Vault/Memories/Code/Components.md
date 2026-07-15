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
| `AnimationComponents.cs` | `Components/Animation/` | `AnimationLayer` buffer, `AnimationTargetRestPose`, `AnimationTargetPose`, `ImageIndex`, `ImageIndexOverride`, `Billboard`, `BaseParent`, per-instance tints `BodyPartTint` (`_BaseColor`) + `BodyPartSecondaryTint` (`_SecondaryColor`) + `BodyPartTertiaryTint` (`_TertiaryColor`) — packed-recolor layer colours, baked white by `BodyPartAuthoring`, written by `DesignApplyUtil.ApplyDesign` (alpha = layer blend strength on G/B). ⚠ `AnimatorTarget` buffer + `AnimationTargetTag` were **unified into `BodyPart` / `BodyPartInfo`** (CharacterRig refactor) — see `BodyPartComponents.cs` |
| `BodyPartComponents.cs` | `Components/Units/` | **CharacterRig registry:** `BodyPart` buffer (root, replaces `AnimatorTarget` — `entity`+`target`+`partDef`+`flags`), `BodyPartInfo` (each part child, replaces `AnimationTargetTag`), `CharacterPalette` (`IPersist`: `groups` shape tags + `colors` rolled `ColorChoice` per `ColorPaletteType` + `useAlternateColors` conversion mode), `CharacterRigConfig` + `RagdollJointBakeData` (both `[BakingType]`; the latter now carries the joint's RESOLVED settle/segment/weight from `RagdollJointAuthoring`) |
| `PartLibraryComponents.cs` | `Components/Units/` | `PartLibrary` (singleton blob holder), `PartLibraryReference` (bake-time `UnityObjectRef<PartLibrarySO>`) — enum-indexed per-part static config (design grid + ragdoll zones) |
| `DamageEvent.cs` | `Components/Combat/` | `DamageEvent` — **plain value struct** (NOT `IComponentData`), queued in the `DamageBus`. `sourceEntity` (Null = environmental) + `damageSource` + damage/knockback/AOE fields |
| `DamageBus.cs` | `Components/Combat/` | `DamageBus` singleton — recycled `NativeQueue<DamageEvent>` transport (`raw` + `resolved`). Owned/disposed by `DamageBusSystem` |
| `Hazard.cs` | `Components/Combat/` | `HazardZone` — proximity damage zone (spikes): `damageAmount`, `damageSource`, `radius`, `retriggerInterval`, `lastTriggerTime` (whole-zone gate), kill-knockback fields |
| `UnitComponents.cs` | `Components/Units/` | `Unit`, `UnitData`, `UnitStateData`, `UnitAction`, `Dead`, `Health`, `HealthBar`, `Attack`, `AttackData`, `AttackCooldown`, `Selected`, `Undead`, `Revive`, `Minion`, `PlayerImmune`, `Heal` |
| `UnitDesignComponents.cs` | `Components/Units/` | `RandomizeDesign` (enableable), design tags, `Outline`/`OutlineChild`/`OutlinedTag` (the `UnitVisualComponents.cs` file never existed — they live here). ⚠ `UnitSkinColor`/`UnitHairColor`/`UnitHeadShape`/`UnitNoseShape` removed — `CharacterPalette` is their successor |
| `DesignComponents.cs` | `Components/Units/` | **Semantic** Unit Design: `DesignSlot` (`target`+`shapeIndex`), `PersistedDesign` (`IPersist`, rolled shapes — auto-saved), `ShapeOverride`, `ChangeDesignRequest` (enableable, semantic re-skin: shape-tag `paletteChanges` + `shapeOverrides` + `alternateColorMode` Enable/Disable), `RandomTagOption` buffer (authored roll pool from `CharacterRigAuthoring.randomTags` — what a random spawn may look like), `DesignReloadOnBake` (`[BakingType]`). Colours live in `CharacterPalette`; slices re-derived through the `PartLibrary` blob, colours through the `ColorPaletteLibrary` blob. ⚠ `DesignPart`/`DesignRange` buffers removed |
| `CameraVisibilityComponents.cs` | `Components/Units/` | `CameraVisible` (enableable tag) — camera-visibility gate, flipped by `CameraVisibilitySystem` (GameManagerSystemGroup) from `CameraView`. Baked ENABLED on rig roots (`CharacterRigAuthoring`), parts (`BodyPartAuthoring`), standalone quads (`ImageIndexAuthoring`). ⚠ **PRESENTATION-ONLY gate** — see [[RULES]] |
| `MovementComponents.cs` | `Components/Movement/` | `UnitMover`, `UnitGravity`, `HordeMembership`, `Horde`, `HordeMemberBuffer`, `SetupUnitMoverDefaultPosition` |
| `PathfindingComponents.cs` | `Components/Movement/` | `PathfindingAgent`, `PathRequest`, `DStarLiteFollower`, `FlowFieldFollower` |
| `SpawnerComponents.cs` | `Components/Spawners/` | `UnitSpawner`, `PoolOwner`, `NeedsAnimatorInit` |
| `PlayerComponents.cs` | `Components/Player/` | `Player`, `PlayerData`, `PlayerInputData`, input enable-tag components, `AimDirection`, `AimIndicatorRef`, `CombatTarget` (enableable — player combat target, distinct from interaction `Target`), `AttackCooldown` (enableable — player per-swing cadence gate; replaces the deleted `ActionTimer`) |
| `PlayerEquipmentComponents.cs` | `Components/Player/` | `OnPlayerReviverEquipt` (enableable) — fired by `PlayerEquipmentInputSystem` when Reviver slot is activated |
| `PlayerMinionCommandComponents.cs` | `Components/Player/` | `OnMinionMoveCommand` (enableable, float3 destination), `OnMinionInteractCommand` (enableable, Entity targetEntity) — written by `UnitSelectionManager`, consumed by `MinionCommandSystem` |
| `Ragdoll2DComponents.cs` | `Components/Units/` | `Ragdoll2D` (enableable, visual root child — tilt/spin/flail), `Ragdoll2DJoint` (enableable, joint pivots — baked settle/segment/weight + pendulum flail state), `RagdollLandingZone` buffer (authored zones on each joint, from `RagdollJointSO` via `RagdollJointAuthoring`), `Ragdoll2DConfig` (static config on root body), `Ragdoll2DLaunch` (enableable — float3 flight velocity, restitution, airborne/sleeping), `RagdollSimConfig` (flat singleton, global tuning), `CorpseCells` (singleton corpse-stacking hash). ⚠ joints come from the `BodyPart` buffer (`RagdollJoint` flag); ragdoll config is fully separate from the design `PartLibrary` blob |
| `ItemComponents.cs` | `Components/Items/` | `Item`, `UnitEquipt`, `EquiptSocket`, `EquiptBy`, `AttachedTo`, `EquipAction`, `AttachItemRequest`, `SpawnItemRequest`, `DespawnItemRequest`, `ThrownItemRequest`. A **loose** (pickable) item has `EquiptBy.owner == Entity.Null` |
| `ItemLibraryComponents.cs` | `Components/Items/` | `ItemLibrary` (singleton blob holder), `ItemLibraryReference` (bake-time `UnityObjectRef<ItemLibrarySO>`) — item `ItemCategory` + effect data for AI item awareness. `PickupItemAction` tag itself lives in `AiComponents.cs` |
| `EntityLibraries.cs` | `Components/EntityLibraries/` | Singleton blob holders: `ScoringLibrary`, `AnimationLibrary`, `UnitDataLibrary`, `AttackLibrary`, `FactoryLibrary`, `ColorPaletteLibrary` (+`ColorPaletteLibraryReference`), `UnitPrefabEntry` |
| `FactoryComponents.cs` | `Components/Structures/` | `FactoryStation`, `StationInputSlot` buffer, `StationOutputSlot` buffer, `ProductionProgress` (enableable), `StationWorkerSlot` buffer, `FactoryGridConfig` singleton, `FactoryGridCell` buffer |
| `RegistryComponents.cs` | `Components/Registry/` | `HordeRegistry` |
| `SceneTags.cs` | `Components/Tags/` | `MainMenuTag`, `GameSceneTag` |
| `GameDataComponents.cs` | `Components/Save/` | `GameDataTag`, `SaveRequest`, `LoadRequest`, `AutoSaveTimer`, `PlayTimeTracker`, `GameSettings` (now incl. master/music/sfx/ambient volume floats — persisted via IPersist) |
| `Dialogue.cs` | `Components/AI/` | `DialogueProvider` (enableable, on NPC), `DialogueManagerTag`, `ActiveDialogue` (enableable, singleton), `OnDialogueEvent` (enableable, singleton), `PlayedDialogue` (buffer on GameData), `DialogueFlag` (buffer on GameData) |
| `SoundComponents.cs` | `Components/Audio/` | Sound System: `PlaySound` (one-frame one-shot signal — LogMessage lifecycle), `LoopingSound` (enableable, on emitter), `ListenerPosition` + `CameraView` (singletons written by AudioManager; `CameraView.viewRadius` is now DYNAMIC — computed each LateUpdate from the camera's ground-projected frustum corners, clamped `[cameraViewRadius, maxCameraViewRadius]`, so zoom/map cam adapt; read by WorldMood + CameraVisibilitySystem + UnitSpawnerSystem), `ResolvedVoice` + `ResolvedVoices` (singleton `NativeList`, owned by VoiceSelectionSystem), `MusicState` (singleton layer weights) |
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

BaseParent                  Entity baseParentEntity — on each part, points to the root body entity (IS remapped by Instantiate)
    ⚠ AnimatorTarget buffer + AnimationTargetTag were unified into BodyPart / BodyPartInfo
      (see Unit Components → CharacterRig registry below). Animation now reads the root's BodyPart buffer.
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

## CharacterRig registry (`Components/Units/BodyPartComponents.cs`)

The unified per-part registry (CharacterRig refactor) — replaces `AnimatorTarget`, `DesignPart`/`DesignRange`, `Ragdoll2DJointRef`/`Zone`, and the equip-socket registry. Assembled at bake by `CharacterRigBakingSystem` and at spawn by `BodyPartInitSystem` (both from `BodyPartInfo` + `BaseParent`). Static per-part config lives in the `PartLibrary` blob; entities carry only a `PartDefId` index.

```
BodyPart (buffer, capacity 32, on the root)   -- replaces AnimatorTarget
    Entity          entity      — the child part entity
    AnimationTarget target      — which part (single identity key everywhere)
    PartDefId       partDef     — index into the PartLibrary blob (design grid + ragdoll zones)
    BodyPartFlags   flags       — HasQuad | DesignSlot | RagdollJoint | ItemSocket
    ⚠ Entity refs NOT remapped by Instantiate — BodyPartInitSystem rebuilds on NewlySpawned.

BodyPartInfo (on each part child)             -- replaces AnimationTargetTag
    AnimationTarget target; PartDefId partDef; BodyPartFlags flags

CharacterPalette (IPersist, on the root)
    FixedList512Bytes<PaletteEntry> groups   — active SHAPE tag per free-text group (e.g. "Skin"→"Tan")
    FixedList64Bytes<ColorChoice>   colors   — rolled COLOUR index per ColorPaletteType (palette = sharing group)
    byte useAlternateColors                  — 1 = every palette entry shows its ALTERNATIVE (zombie) variant
    Zombify = ChangeDesignRequest{ paletteChanges("Skin"→"Zombie") + alternateColorMode = Enable };
    parts re-derive shape slices + palette colours through DesignApplyUtil.ApplyDesign — rolled
    identity is kept (pale skin → its corresponding pale-zombie alternative).

CharacterRigConfig      [BakingType] marker on the root (added by CharacterRigAuthoring)
RagdollJointBakeData    [BakingType] per-joint override carrier (settleSpeedOverride, groundBufferOverride)
```

Enums: `UnitPartId : short` (`Data/Enums/PartEnums.cs`, one per interchangeable part KIND, L/R share a kind), `[Flags] BodyPartFlags : byte` (same file), `ColorPaletteType : byte` (`Data/Enums/ColorEnums.cs`, palette identity = colour sharing group). ⚠ shape groups are free-text strings now (no `PaletteGroup` enum); the old `GridMode` design grid is gone (tag ranges).

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

## Ragdoll Components (`Components/Units/Ragdoll2DComponents.cs`)

Procedural death ragdoll for quad-based characters (2026-07 rework: real 3D flight + plane-space
flail; see `Tasks/Verification/Ragdoll2D_System.md`). All ragdoll components are
`IEnableableComponent` — they stay on the entity permanently and are toggled to support revive.

```
Ragdoll2D (enableable, on visual root child entity)
    float       fallSpeed           — tip-over speed (config.fallSpeed × killRagdollForce at init)
    float       groundBuffer/tiltOffset — copied from Ragdoll2DConfig per fall direction at init
    float       flailIntensity      — per-attack joint flail scale (Health.killFlailIntensity, 0 → 1)
    float       spin                — deg/s airborne tumble (killSpin × fallSideSign); damps on ground
    float       bodyZAngle          — current Z tilt (can wind past 360° under spin; settles to nearest turn)
    quaternion  initialRotation     — captured at death (used by revive to restore)
    float       fallSideSign        — +1 fall right / -1 fall left (from Health.killSourcePosition.x)

Ragdoll2DJoint (enableable, on joint pivot entities)
    float       settleSpeed/segmentLength/weight — BAKED (override-or-PartDef blob); preserved across deaths
    float       targetAngle         — zone-picked at death (PartLibrary blob via PartDefId)
    float       currentZAngle       — simulation state
    float       angularVelocity     — deg/s flail pendulum state (seeded with a launch trail kick)
    quaternion  initialLocalRotation — captured at death (used by revive to restore)

Ragdoll2DConfig (static, on root body entity — never toggled)
    Entity      visualRoot          — the visual child entity that tilts on Z
    float       groundBufferForward/Backward, tiltOffsetForward/Backward, fallSpeed

Ragdoll2DLaunch (enableable, on root body entity)
    float3      velocity            — real 3D flight velocity (direction from kill source)
    float       restitution         — bounce energy kept (per-attack, or RagdollSimConfig default)
    byte        airborne / sleeping — flight phase flag / all-quiet flag (sleeping = zero dynamics cost)

RagdollSimConfig (singleton, baked by RagdollSimConfigAuthoring — flat floats, NOT a blob)
    gravity, horizontalDrag, defaultRestitution, bounceMinSpeed, groundRaycastDistance,
    landingImpulseScale, flailDamping, sleepAngularSpeedDeg, corpseCellSize/StackOffset/StackMax
    (systems fall back to identical built-in defaults when the authoring isn't baked)

CorpseCells (singleton, owned by CorpseCellSystem)
    NativeParallelMultiHashMap<int2, float> map — settled corpses per XZ cell, rebuilt each frame
```

**Entity placement (CharacterRig refactor):**
- `Ragdoll2DConfig` + `Ragdoll2DLaunch` → **root body entity** (baked by `CharacterRigAuthoring`)
- `Ragdoll2D` → **visual root child** (added disabled by `CharacterRigBakingSystem`)
- `Ragdoll2DJoint` → **each joint pivot entity** (added disabled by `CharacterRigBakingSystem`; `settleSpeed` from override-or-blob, `segmentLength`/`weight` from the `PartDef`)
- Joints are discovered at death/revive by walking the root's **`BodyPart` buffer** for `RagdollJoint`-flagged entries; **landing zones** come from the `PartLibrary` blob via each joint's `PartDefId` (no per-root zone buffer anymore). `Ragdoll2DJointRef`/`Ragdoll2DJointZone` are gone.
