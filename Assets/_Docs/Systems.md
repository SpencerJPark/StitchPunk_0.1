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
LateSimulationSystemGroup    — spawn/despawn, event resets, pool return
PresentationSystemGroup      — selection outlines (runs after transforms settled)
```

System group definitions live in `SystemGroups.cs`.

---

## ISystem Rules

- Prefer `ISystem` (struct) over `SystemBase` — it is Burst-compatible.
- Always annotate the struct with `[BurstCompile]`.
- `OnCreate`, `OnUpdate`, `OnDestroy` must all be annotated with `[BurstCompile]` individually.
- Use `SystemAPI.Query<>()` for component iteration — it is the Burst-friendly query API.
- Use `SystemAPI.GetSingleton<>()` / `SetSingleton<>()` for singleton components (library blobs, world state).
- Schedule jobs with `state.Dependency` — never ignore the dependency chain.

---

## Sub-Group Context Files

| Group | Context |
|---|---|
| `AISystemGroup/` | [CONTEXT.md](AISystemGroup/CONTEXT.md) |
| `AnimationSystemGroup/` | [CONTEXT.md](AnimationSystemGroup/CONTEXT.md) |
| `MovementSystemGroup/` | [CONTEXT.md](MovementSystemGroup/CONTEXT.md) |
