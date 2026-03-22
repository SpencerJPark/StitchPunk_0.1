# MovementSystemGroup — Context

Movement is split into four sub-groups that run in order each frame.

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
  UnitMoverSystem               — integrates velocity → position
  UnitGravitySystem             — applies gravity on Y axis
  StairTransitionSystem         — handles stair/level transition triggers
  SetupUnitMoverDefaultPositionSystem — initialises position on first frame
```

---

## Horde vs Individual Movement

- **Horde movement** (most units): FlowField. A shared vector grid points every cell toward the target. Units sample the cell they occupy. Cheap for large groups.
- **Individual movement** (player, special units): D* Lite. Incremental A* variant that replans efficiently when obstacles change.

Units are assigned to a `Horde` entity via `HordeAuthoring`. The horde holds the target destination; individual members hold a formation offset added on top.

---

## Key Components

| Component | Purpose |
|---|---|
| `UnitMover` | Current velocity, move speed, is-grounded flag |
| `PathfindingData` | D* Lite state (open list, path buffer) |
| `HordeAuthoring` / horde entity | Shared target + flowfield reference |
| `UnitGravity` | Gravity scale, vertical velocity |

---

## Adding Movement to a New Unit

1. Add `UnitMoverAuthoring` to the body prefab.
2. Add `HordeAuthoring` if the unit should join a horde, or `PathfindingAuthoring` for individual pathfinding.
3. Add `UnitGravityAuthoring` if the unit is subject to gravity.
4. The follower and execution systems will pick it up automatically via query.
