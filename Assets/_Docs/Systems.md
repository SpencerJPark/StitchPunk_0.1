# Systems — Context

All game logic lives here. Read the CONTEXT.md in each sub-group before working inside it.

---

## System Group Execution Order

```
PostBakingSystemGroup        — SOs → BlobAssets + cross-entity component distribution (runs once at bake time)
GameManagerSystemGroup       — world-level: floating origin, player input, aim, horde state
ItemSystemGroup              — thrown item movement, proximity hit detection
AISystemGroup
  ├── AIAwarenessSystemGroup — motivation decay, spatial hashing (perception)
  ├── AIScoringSystemGroup   — 9 systems write scores to ActionOption buffer
  ├── AISelectionSystemGroup — picks action, assigns interaction targets
  └── AIExecutionSystemGroup — executes the chosen action
AnimationSystemGroup
  ├── AnimationAssignmentSystemGroup — decides which clip to play per layer
  └── AnimationExecutionSystemGroup — advances time, samples keyframes, applies pose
MovementSystemGroup
  ├── MovementRoutingSystemGroup     — flowfield / D* Lite path calculation
  ├── MovementCoordinatorSystemGroup — horde formation offsets
  ├── MovementFollowerSystemGroup    — smooth path following
  └── MovementExecutionSystemGroup  — writes final position/rotation to transforms
BuildingsSystemGroup         — construction, harvesting, destruction
CombatSystemGroup
  ├── CombatResolutionSystemGroup — attack resolution, player attacks
  └── CombatReactionSystemGroup  — damage application
HealthSystemGroup            — death, fake ragdoll init, heal, revive, ragdoll revive cleanup, health bar updates
LateSimulationSystemGroup
  ├── SpawnSystemGroup       — spawn new units, rebuild animator targets
  ├── (loose systems)        — pool return, event resets, Ragdoll2DSystem (runs here so it fires AFTER ApplyAnimatedPoseSystem)
  └── SaveSystemGroup        — play time tracking, auto-save timer, save/load (OrderLast)
PresentationSystemGroup      — selection outlines (runs after transforms settled)
```

System group definitions live in `SystemGroups.cs`.
`SystemGroups.cs` orchestrates the order systems execute and is organized in that same order (top down) for easy to read logic.

---

## All Systems — File Map

### PostBakingSystemGroup (`Systems/PostBakingSystemGroup/`)
Runs once at bake time. Converts ScriptableObject data into BlobAssets, and distributes components that bakers cannot add cross-entity.

| System | File | What it bakes |
|---|---|---|
| `AnimationLibraryBakingSystem` | `AnimationLibraryBakingSystem.cs` | AnimationLibrarySO → AnimationLibraryBlob |
| `UnitLibraryBakingSystem` | `UnitLibraryBakingSystem.cs` | UnitLibrarySO → UnitLibraryBlob + UnitPrefabEntry |
| `ScoringLibraryBakingSystem` | `ScoringLibraryBakingSystem.cs` | AIScoringLibrarySO → AIScoringLibraryBlob |
| `AttackLibraryBakingSystem` | `AttackLibraryBakingSystem.cs` | AttackLibrarySO → AttackLibraryBlob |
| `Ragdoll2DBakingSystem` | `Ragdoll2DBakingSystem.cs` | Adds `Ragdoll2D` (disabled) to visual root child, `Ragdoll2DJoint` (disabled) to each joint pivot — cannot be done in baker because they are other GOs' entities |

---

### GameManagerSystemGroup (`Systems/GameManagerSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `FloatingWorldOriginSystem` | `FloatingWorldOriginSystem.cs` | Recenters world origin to prevent float precision loss |
| `HordeSystem` | `HordeSystem.cs` | Creates/destroys horde entities, manages membership |
| `PlayerInputSystem` | `PlayerInputSystem.cs` | Reads `PlayerInputData` singleton, enables input tag components |
| `PlayerAimSystem` | `PlayerAimSystem.cs` | While `AimPlayerInput` is enabled: reads `LookPlayerInput`, updates `AimDirection`, rotates player, shows/hides aim indicator |

---

### AISystemGroup — see [Systems_AI.md](Systems_AI.md)

---

### AnimationSystemGroup — see [Systems_Animation.md](Systems_Animation.md)

---

### MovementSystemGroup — see [Systems_Movement.md](Systems_Movement.md)

---

### ItemSystemGroup (`Systems/ItemSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `ThrownItemHitSystem` | `ThrownItemHitSystem.cs` | Proximity-based hit detection for thrown items (no physics collider required). XZ-only distance check; on hit writes to `Hurt` buffer, disables `ThrownItem`, enables `PlayerInteractable` |

> Thrown items skip the `thrower` entity and ignore all hits until the item has traveled **1.2 units** from `ThrownItem.throwOrigin` — prevents a body right next to the player from immediately blocking the throw. Walls are unaffected (no `Health` component).

---

### CombatSystemGroup (`Systems/CombatSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `AttackResolutionSystem` | `CombatResolutionSystemGroup/AttackResolutionSystem.cs` | Resolves unit-vs-unit attack hits, writes to `Hurt` buffer |
| `PlayerAttackSystem` | `CombatResolutionSystemGroup/PlayerAttackSystem.cs` | Handles player attack input, finds targets |
| `DamageApplicationSystem` | `CombatReactionSystemGroup/DamageApplicationSystem.cs` | Reads `Hurt` buffer, applies damage to `Health` |

---

### HealthSystemGroup (`Systems/HealthSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `DeathSystem` | `DeathSystem.cs` | Detects zero health, enables `Dead`, disables `Alive` |
| `Ragdoll2DInitSystem` | `Ragdoll2DInitSystem.cs` | Runs after `DeathSystem`. Detects freshly dead units, reads `Hurt` buffer to determine fall direction (away from attacker), enables and resets `Ragdoll2D` + `Ragdoll2DJoint` components with randomised flail velocity |
| `HealSystem` | `HealSystem.cs` | Applies `Heal` component when enabled |
| `ReviveSystem` | `ReviveSystem.cs` | Handles `Revive` — re-enables unit from dead state |
| `Ragdoll2DReviveSystem` | `Ragdoll2DReviveSystem.cs` | Runs after `ReviveSystem`. Resets visual child + joint rotations to their pre-death pose and disables ragdoll components |
| `HealthBarSystem` | `HealthBarSystem.cs` | Syncs `HealthBar` visual entity scale to `Health` values |

> ⚠ **`Ragdoll2DSystem` does NOT run in HealthSystemGroup** — it is in `LateSimulationSystemGroup` so it runs *after* `ApplyAnimatedPoseSystem`, which would otherwise overwrite the ragdoll transforms every frame.

---

### LateSimulationSystemGroup (`Systems/LateSimulationSystemGroup/`)

Runs at end of frame. Safe zone for spawn/despawn and event cleanup.

| System | File | Purpose |
|---|---|---|
| `UnitSpawnerSystem` | `SpawnSystemGroup/UnitSpawnerSystem.cs` | Spawns units from pool or instantiates new ones |
| `AnimatorTargetInitSystem` | `SpawnSystemGroup/AnimatorTargetInitSystem.cs` | Rebuilds `AnimatorTarget` buffer on newly spawned bodies |
| `UnitPoolReturnSystem` | `UnitPoolReturnSystem.cs` | Disables dead units and returns them to pool |
| `ResetEventsSystem` | `ResetEventsSystem.cs` | Clears one-frame event flags (onSelected, onDeselected, etc.) |
| `Ragdoll2DSystem` | `HealthSystemGroup/Ragdoll2DSystem.cs` | Drives fake ragdoll each frame: lerps body Z tilt toward ±88° (direction from `fallSideSign`), decays joint angular velocity, clamps joints to ±75°, ground-clamps joint world Y against root Y + buffer |

> `SpawnSystemGroup` is a sub-group nested inside `LateSimulationSystemGroup`.

---

### SaveSystemGroup (`Systems/SaveSystemGroup/`)

Runs `OrderLast` inside `LateSimulationSystemGroup` — after all spawns, despawns, and events have settled. Systems that need to trigger a save or load enable `SaveRequest` / `LoadRequest` (both `IEnableableComponent`) on the GameData entity (identified by `GameDataTag`).

| System | File | Purpose |
|---|---|---|
| `PlayTimeTrackerSystem` | `PlayTimeTrackerSystem.cs` | Accumulates `DeltaTime` into `PlayTimeTracker.totalSeconds` each frame |
| `AutoSaveTimerSystem` | `AutoSaveTimerSystem.cs` | Ticks `AutoSaveTimer`; enables `SaveRequest { slot = 0 }` when interval elapses |
| `SaveSystem` | `SaveSystem.cs` | Consumes `SaveRequest`; snapshots player components → `SaveFile` DTO → JSON on disk |
| `LoadSystem` | `LoadSystem.cs` | Consumes `LoadRequest`; reads JSON → restores player transform, health, item slots |

**Slot convention:** slot `0` = auto-save, slots `1–3` = manual slots.  
**Save path:** `Application.persistentDataPath/save_slot_{N}.json` (see `SavePaths.cs`).  
**No `[BurstCompile]`** on `SaveSystem` / `LoadSystem` — `JsonUtility` and `System.IO` are managed. `PlayTimeTrackerSystem` and `AutoSaveTimerSystem` are fully Burst-compiled.

---

### PresentationSystemGroup (`Systems/PresentationSystemGroup/`)
Runs after all transforms have settled.

| System | File | Purpose |
|---|---|---|
| `OutlineSystem` | `OutlineSystem.cs` | Drives outline render feature for selected units |
| `OutlineLayerUpdateSystem` | `OutlineLayerUpdateSystem.cs` | Updates per-layer outline state |
| `SelectedVisualSystem` | `SelectedVisualSystem.cs` | Shows/hides selection circle visual under units |

---

## ISystem Rules

- Prefer `ISystem` (struct) over `SystemBase` — it is Burst-compatible.
- Always annotate the struct with `[BurstCompile]`.
- `OnCreate`, `OnUpdate`, `OnDestroy` must all be annotated with `[BurstCompile]` individually.
- Use `SystemAPI.Query<>()` for component iteration — it is the Burst-friendly query API.
- Use `SystemAPI.GetSingleton<>()` / `SetSingleton<>()` for singleton components (library blobs, world state).
- Schedule jobs with `state.Dependency` — never ignore the dependency chain.
