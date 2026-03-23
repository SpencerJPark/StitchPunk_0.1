# Systems — Context

All game logic lives here. Read the CONTEXT.md in each sub-group before working inside it.

---

## System Group Execution Order

```
PostBakingSystemGroup        — SOs → BlobAssets (runs once at bake time)
GameManagerSystemGroup       — world-level: floating origin, player input, horde state
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
HealthSystemGroup            — death, heal, revive, health bar updates
LateSimulationSystemGroup
  ├── SpawnSystemGroup       — spawn new units, rebuild animator targets
  └── (loose systems)        — pool return, event resets
PresentationSystemGroup      — selection outlines (runs after transforms settled)
```

System group definitions live in `SystemGroups.cs`.
`SystemGroups.cs` orchestrates the order systems execute and is organized in that same order (top down) for easy to read logic.

---

## All Systems — File Map

### PostBakingSystemGroup (`Systems/PostBakingSystemGroup/`)
Runs once at bake time. Converts ScriptableObject data into BlobAssets.

| System | File | What it bakes |
|---|---|---|
| `AnimationLibraryBakingSystem` | `AnimationLibraryBakingSystem.cs` | AnimationLibrarySO → AnimationLibraryBlob |
| `UnitLibraryBakingSystem` | `UnitLibraryBakingSystem.cs` | UnitLibrarySO → UnitLibraryBlob + UnitPrefabEntry |
| `ScoringLibraryBakingSystem` | `ScoringLibraryBakingSystem.cs` | AIScoringLibrarySO → AIScoringLibraryBlob |
| `AttackLibraryBakingSystem` | `AttackLibraryBakingSystem.cs` | AttackLibrarySO → AttackLibraryBlob |

---

### GameManagerSystemGroup (`Systems/GameManagerSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `FloatingWorldOriginSystem` | `FloatingWorldOriginSystem.cs` | Recenters world origin to prevent float precision loss |
| `HordeSystem` | `HordeSystem.cs` | Creates/destroys horde entities, manages membership |
| `PlayerInputSystem` | `PlayerInputSystem.cs` | Reads `PlayerInputData` singleton, enables input tag components |

---

### AISystemGroup — see [Systems_AI.md](Systems_AI.md)

---

### AnimationSystemGroup — see [Systems_Animation.md](Systems_Animation.md)

---

### MovementSystemGroup — see [Systems_Movement.md](Systems_Movement.md)

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
| `HealSystem` | `HealSystem.cs` | Applies `Heal` component when enabled |
| `ReviveSystem` | `ReviveSystem.cs` | Handles `Revive` — re-enables unit from dead state |
| `HealthBarSystem` | `HealthBarSystem.cs` | Syncs `HealthBar` visual entity scale to `Health` values |

---

### LateSimulationSystemGroup (`Systems/LateSimulationSystemGroup/`)

Runs at end of frame. Safe zone for spawn/despawn and event cleanup.

| System | File | Purpose |
|---|---|---|
| `UnitSpawnerSystem` | `SpawnSystemGroup/UnitSpawnerSystem.cs` | Spawns units from pool or instantiates new ones |
| `AnimatorTargetInitSystem` | `SpawnSystemGroup/AnimatorTargetInitSystem.cs` | Rebuilds `AnimatorTarget` buffer on newly spawned bodies |
| `UnitPoolReturnSystem` | `UnitPoolReturnSystem.cs` | Disables dead units and returns them to pool |
| `ResetEventsSystem` | `ResetEventsSystem.cs` | Clears one-frame event flags (onSelected, onDeselected, etc.) |

> `SpawnSystemGroup` is a sub-group nested inside `LateSimulationSystemGroup`.

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
