# Components — Context

Component files are **pure data structs**. No methods, no logic, no Unity API calls.

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
| `AIComponents.cs` | `Components/AI/` | `IsBrain`, `HasBrain`, `BodyLink`, `BrainLink`, `Awareness`, `SelectedAction`, `NeedsAction`, `ActionOption` buffer |
| `Brains.cs` | `Components/AI/` | Brain-type tags: `CitizenBrain`, `ZombieBrain` |
| `Motivations.cs` | `Components/AI/` | One struct per motivation (9 core + personality traits) |
| `Interactions.cs` | `Components/AI/` | `Interaction`, `InteractionTimer`, `InteractionOccupant` buffer, interaction-type components |
| `SpatialHashRegistry.cs` | `Components/AI/` | `SpatialHashRegistry` singleton (two NativeParallelMultiHashMaps) |
| `AnimationComponents.cs` | `Components/Animation/` | `AnimationLayer` buffer, `AnimatorTarget` buffer, `AnimationTargetTag`, `AnimationTargetRestPose`, `AnimationTargetPose`, `ImageIndex`, `ImageIndexOverride`, `Billboard`, `BaseParent` |
| `UnitComponents.cs` | `Components/Units/` | `Unit`, `UnitData`, `UnitStateData`, `UnitAction`, `Alive`, `Dead`, `Hurt` buffer, `Health`, `HealthBar`, `Attack`, `AttackData`, `AttackCooldown`, `CombatTarget`, `Selected`, `Undead`, `Revive`, `Minion`, `PlayerImmune`, `Heal` |
| `UnitDesignComponents.cs` | `Components/Units/` | `UnitSkinColor`, `UnitHairColor`, `UnitHeadShape`, `UnitNoseShape`, `RandomizeDesign`, design tags |
| `UnitVisualComponents.cs` | `Components/Units/` | `Outline`, `OutlineChild`, `OutlinedTag` |
| `MovementComponents.cs` | `Components/Movement/` | `UnitMover`, `UnitGravity`, `HordeMembership`, `Horde`, `HordeMemberBuffer`, `SetupUnitMoverDefaultPosition` |
| `PathfindingComponents.cs` | `Components/Movement/` | `PathfindingAgent`, `PathRequest`, `DStarLiteFollower`, `FlowFieldFollower` |
| `SpawnerComponents.cs` | `Components/Spawners/` | `UnitSpawner`, `PoolOwner`, `NeedsAnimatorInit` |
| `PlayerComponents.cs` | `Components/Player/` | `Player`, `PlayerData`, `PlayerInputData`, input enable-tag components |
| `ItemComponents.cs` | `Components/Items/` | `Item`, `UnitEquipt`, `EquiptSocket`, `EquiptBy`, `AttachedTo`, `EquipRequest` |
| `EntityLibraries.cs` | `Components/EntityLibraries/` | Singleton blob holders: `ScoringLibrary`, `AnimationLibrary`, `UnitDataLibrary`, `AttackLibrary`, `UnitPrefabEntry` |
| `RegistryComponents.cs` | `Components/Registry/` | `HordeRegistry` |
| `SceneTags.cs` | `Components/Tags/` | `MainMenuTag`, `GameSceneTag` |

---

## AI Components (`Components/AI/`)

### `AIComponents.cs` — Brain / Body identity and AI state

```
IsBrain                     tag — present on brain entities
HasBrain                    tag — present on body entities
BodyLink                    brain → body:  Entity body
BrainLink                   body → brain:  Entity brain
                            ⚠ BrainLink is NOT baked. Added by UnitSpawnerSystem via ECB.
Awareness                   float range
SelectedAction              Entity current, Entity previous
NeedsAction (enableable)    tag — set enabled to trigger the AI scoring/selection pipeline
ActionOption (buffer)       Entity interactableEntity, float score
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

Alive (enableable)      tag
Dead (enableable)       tag

Hurt (buffer)           Entity attackerEntity, float distance, int damageAmount
Health                  int healthAmount, int healthAmountMax
HealthBar               Entity barVisualEntity, Entity healthEntity
CombatTarget            Entity entity
AttackCooldown          float timer
Attack (enableable)     tag
AttackData              AttackType attackType
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

These are singletons — one entity in the world holds them. Access via `SystemAPI.GetSingleton<T>()`.

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
PlayerData          float rollTime, float rollTimeMax, float rollSpeed
PlayerInputData     float2 moveInput, float2 lookInput, float2 cursorInput, float zoomInput,
                    ActionMaps activeActionMap, bool sneakToggle,
                    bool onAttackInput, bool onInteractInput, bool onRollInput
PlayerAttackInput (enableable)
PlayerInteractInput (enableable)
PlayerRollInput (enableable)
PlayerSneakToggle (enableable)
```
