---
tags: [memory, code, systems, movement, pathfinding]
related: "[[Systems]], [[Components]], [[Systems_AI]]"
---

# MovementSystemGroup — Context

Movement is split into four sub-groups that run in order each frame. Part of the larger [[Systems]] execution pipeline. Units' pathfinding decisions are initiated by [[Systems_AI]] (via `PathRequest`).

---

## Sub-Group Responsibilities

```
MovementRoutingSystemGroup      — calculates paths (does the expensive work)
  FlowFieldSystem               — builds a flowfield grid for horde movement
  DStarLiteSystem               — incremental pathfinding for individual units

MovementCoordinatorSystemGroup  — assigns formation offsets within a horde
  PathfindingCoordinatorSystem  — distributes destination targets per horde member
  GridSystem                    — maintains the spatial grid used by the flowfield

MovementFollowerSystemGroup     — reads path data and produces a desired velocity
  FlowFieldFollowerSystem       — units in a horde sample the flowfield
  DStarLiteFollowerSystem       — individual units follow their D* path
  PlayerFollowerSystem          — player character movement

MovementExecutionSystemGroup    — applies final velocity to transforms
  LocomotionStanceSystem        — syncs StateMachine.currentStance → Movement.isRunning + LocomotionStance.stance (runs before UnitMoverSystem)
  UnitMoverSystem               — integrates velocity → position (picks runSpeed when isRunning)
  UnitGravitySystem             — applies gravity on Y axis
  StairTransitionSystem         — handles stair/level transition triggers
  SetupUnitMoverDefaultPositionSystem — initialises position on first frame
```

### File Paths (relative to `_Scripts/Systems/MovementSystemGroup/`)

| System | File |
|---|---|
| `FlowFieldSystem` | `MovementRoutingSystemGroup/FlowFieldSystem.cs` |
| `DStarLiteSystem` | `MovementRoutingSystemGroup/DStarLiteSystem.cs` |
| `PathfindingCoordinatorSystem` | `MovementCoordinatorSystemGroup/PathfindingCoordinatorSystem.cs` |
| `GridSystem` | `MovementCoordinatorSystemGroup/GridSystem.cs` |
| `FlowFieldFollowerSystem` | `MovementFollowerSystemGroup/FlowFieldFollowerSystem.cs` |
| `DStarLiteFollowerSystem` | `MovementFollowerSystemGroup/DStarLiteFollowerSystem.cs` |
| `PlayerFollowerSystem` | `MovementFollowerSystemGroup/PlayerFollowerSystem.cs` |
| `LocomotionStanceSystem` | `MovementExecutionSystemGroup/LocomotionStanceSystem.cs` |
| `UnitMoverSystem` | `MovementExecutionSystemGroup/UnitMoverSystem.cs` |
| `UnitGravitySystem` | `MovementExecutionSystemGroup/UnitGravitySystem.cs` |
| `StairTransitionSystem` | `MovementExecutionSystemGroup/StairTransitionSystem.cs` |
| `SetupUnitMoverDefaultPositionSystem` | `MovementExecutionSystemGroup/SetupUnitMoverDefaultPositionSystem.cs` |

---

## Horde vs Individual Movement

- **Horde movement** (most units): FlowField. A shared vector grid points every cell toward the target. Units sample the cell they occupy. Cheap for large groups.
- **Individual movement** (player, special units): D* Lite. Incremental A* variant that replans efficiently when obstacles change.

Units are assigned to a `Horde` entity. The horde holds the target destination; individual members hold a `formationOffset` (in `HordeMembership`) added on top.

The system switches a unit between `FlowFieldFollower` and `DStarLiteFollower` (both `IEnableableComponent`) based on `PathfindingAgent.currentMode`. Full component definitions in [[Components]].

---

## Key Components

| Component | File | Purpose |
|---|---|---|
| `Movement` | `MovementComponents.cs` | moveSpeed, runSpeed, rotationSpeed, targetPosition, isMoving, isRunning. Speeds for brain units come from `UnitSO` (overridden post-bake by `UnitSpeedBakingSystem`); `MovementAuthoring` values are authoritative only for non-brain units (player) |
| `UnitGravity` | `MovementComponents.cs` | fallSpeed, verticalVelocity |
| `HordeMembership` | `MovementComponents.cs` | hordeId, hordeEntity, formationOffset, priority |
| `Horde` | `MovementComponents.cs` | Shared target + flowfield index + member count |
| `HordeMemberBuffer` | `MovementComponents.cs` | Buffer of member entities on the horde entity |
| `PathfindingAgent` | `PathfindingComponents.cs` | Mode, repath interval, target, active flag |
| `DStarLiteFollower` | `PathfindingComponents.cs` | Per-unit D* Lite state (enableable) |
| `FlowFieldFollower` | `PathfindingComponents.cs` | Per-unit flowfield state (enableable) |

---

## Adding Movement to a New Unit

1. Add `UnitMoverAuthoring` to the body prefab — see [[Authoring]] for baking conventions.
2. Add `HordeAuthoring` if the unit should join a horde, or `PathfindingAuthoring` for individual pathfinding.
3. Add `UnitGravityAuthoring` if the unit is subject to gravity.
4. The follower and execution systems will pick it up automatically via query.
