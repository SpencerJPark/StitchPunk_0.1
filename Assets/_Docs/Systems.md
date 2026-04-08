# Systems — Context

All game logic lives here. Read the CONTEXT.md in each sub-group before working inside it.

---

## System Group Execution Order

```
PostBakingSystemGroup        — SOs → BlobAssets + cross-entity component distribution (runs once at bake time)
GameManagerSystemGroup       — world-level: floating origin, player input, aim, horde state
PlayerSystemGroup            — all player-driven logic
  ├── PlayerInputSystemGroup (OrderFirst) — input events → ECS components, targeting, equipment dispatch
  └── PlayerEquipmentSystemGroup (OrderLast) — equipment actions, minion commands
AISystemGroup
  ├── AIAwarenessSystemGroup — motivation decay, spatial hashing (perception)
  ├── AIScoringSystemGroup   — 9 systems write scores to ActionOption buffer
  ├── AISelectionSystemGroup — picks action; skips PlayerControlled brains
  └── AIExecutionSystemGroup — executes the chosen action
ItemSystemGroup              — thrown item movement, proximity hit detection
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
  └── CombatReactionSystemGroup  — damage application; MinionAutoCounterSystem releases PlayerControlled on hit
HealthSystemGroup            — death, fake ragdoll init, heal, revive, ragdoll revive cleanup, health bar updates
LateSimulationSystemGroup
  ├── SpawnSystemGroup       — UnitSpawnerSystem only: instantiate/reclaim, enable NewlySpawned
  ├── SpawnInitSystemGroup   — all spawn-frame init systems filter on [WithAll<NewlySpawned>]
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

### PlayerSystemGroup (`Systems/PlayerSystemGroup/`)

Runs inside `SimulationSystemGroup`, before `AISystemGroup`. Contains two sub-groups:

#### PlayerInputSystemGroup (OrderFirst)

| System | File | Purpose |
|---|---|---|
| `PlayerRollInputSystem` | `PlayerInputSystemGroup/PlayerRollInputSystem.cs` | Ticks down `OnRollPlayerInput.rollTime`, disables when expired |
| `PlayerTargetingSystem` | `PlayerInputSystemGroup/PlayerTargetingSystem.cs` | Finds nearest `PlayerInteractable` in range; enables/disables `Target` on the player |
| `PlayerAimSystem` | `PlayerInputSystemGroup/PlayerAimSystem.cs` | Reads `LookPlayerInput` while aiming; updates `AimDirection`, rotates player, shows/hides aim indicator |
| `PlayerEquipmentInputSystem` | `PlayerInputSystemGroup/PlayerEquipmentInputSystem.cs` | Resolves `OnEquipmentSlotPlayerInput.slot` → `ItemType` via `PlayerEquipmentSlots`; fires the matching equipment event (`OnPlayerReviverEquipt` etc.) |

#### PlayerEquipmentSystemGroup (OrderLast)

| System | File | Purpose |
|---|---|---|
| `PlayerReviverSystem` | `PlayerEquipmentSystemGroup/PlayerReviverSystem.cs` | When `OnPlayerReviverEquipt` is enabled and `Target` is valid: enables `Revive` on the target entity |
| `MinionCommandSystem` | `PlayerEquipmentSystemGroup/MinionCommandSystem.cs` | Reads `OnMinionMoveCommand`/`OnMinionInteractCommand` from player; writes `PlayerOrder` + enables `PlayerControlled` on each Selected+Minion brain; writes `PathRequest` on bodies for move commands |

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
| `MinionAutoCounterSystem` | `CombatReactionSystemGroup/MinionAutoCounterSystem.cs` | Detects when a `PlayerControlled` minion body takes damage; disables `PlayerControlled` on the linked brain so the AI (SelfPreservation) takes over and counter-attacks |

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

Sub-group execution order:
```
SpawnSystemGroup       → SpawnInitSystemGroup → DespawnSystemGroup → SaveSystemGroup (OrderLast)
```

#### SpawnSystemGroup (`SpawnSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `UnitSpawnerSystem` | `UnitSpawnerSystem.cs` | Instantiates new or reclaims pooled body+brain entities; enables `NewlySpawned` on every body; cross-links `BrainLink`/`BodyLink` |

#### SpawnInitSystemGroup (`SpawnInitSystemGroup/`)

Runs after `SpawnSystemGroup` each frame. All systems filter on `[WithAll<NewlySpawned>]` — no-op on frames with no spawning.

| System | File | Purpose |
|---|---|---|
| `SpawnStateInitSystem` | `SpawnStateInitSystem.cs` | Resets root-entity enableable states: `Alive`→on, `Dead`/`Ragdoll2DLaunch`/`Undead`/`Minion`/`Revive`/`Selected`/pathfinding→off |
| `AnimatorTargetInitSystem` | `AnimatorTargetInitSystem.cs` | Rebuilds `AnimatorTarget` buffer — ECB.Instantiate does not reliably remap refs inside dynamic buffers |
| `Ragdoll2DSpawnInitSystem` | `Ragdoll2DSpawnInitSystem.cs` | Scans `LinkedEntityGroup` to force-disable `Ragdoll2D`/`RagdollJoint` on all child entities — fixes ECB.Instantiate enabled-bit copy and stale state on pool reclaims |
| `SpawnInitCleanupSystem` | `SpawnInitCleanupSystem.cs` | `[OrderLast]` Disables `NewlySpawned`; component persists on entity for re-enablement on next pool reclaim |

#### DespawnSystemGroup (`DespawnSystemGroup/`)

| System | File | Purpose |
|---|---|---|
| `UnitPoolReturnSystem` | `../UnitPoolReturnSystem.cs` | Adds `Disabled` to units > 200 units from the player; returns them to the pool |

#### Loose systems (LateSimulationSystemGroup)

| System | File | Purpose |
|---|---|---|
| `ResetEventsSystem` | `ResetEventsSystem.cs` | Clears one-frame event flags (onSelected, onDeselected, etc.) |
| `Ragdoll2DSystem` | `../HealthSystemGroup/Ragdoll2DSystem.cs` | Drives fake ragdoll each frame: lerps body Z tilt toward ±88°, decays joint angular velocity, ground-clamps joints. Runs here (not HealthSystemGroup) so it fires AFTER `ApplyAnimatedPoseSystem` |

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

## ISystem Rules

- Prefer `ISystem` (struct) over `SystemBase` — it is Burst-compatible.
- Always annotate the struct with `[BurstCompile]`.
- `OnCreate`, `OnUpdate`, `OnDestroy` must all be annotated with `[BurstCompile]` individually.
- Use `SystemAPI.Query<>()` for component iteration — it is the Burst-friendly query API.
- Use `SystemAPI.GetSingleton<>()` / `SetSingleton<>()` for singleton components (library blobs, world state).
- Schedule jobs with `state.Dependency` — never ignore the dependency chain.
