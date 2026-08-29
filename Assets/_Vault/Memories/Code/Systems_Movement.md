---
tags: [memory, code, systems, movement, pathfinding, package]
related: "[[Systems]], [[Components]], [[Systems_AI]], [[Contracts]]"
---

# Movement — Context

**The grid/pathfinding/movement/horde stack now lives in `Packages/com.dotsmovementtoolkit`**
(namespace `DotsMovementToolkit`), extracted 2026-08 per
`_Vault/Tasks/Verification/verify-movement-toolkit.md` (was `Tasks/Plans/Movement_Toolkit_Extraction.md`).
This file covers the **game-side seam** — what stays in `Assets/_Scripts` and how it talks to
the package. For the package's own systems, components, and pipeline, **read the package
README** (`Packages/com.dotsmovementtoolkit/README.md`) — a transcription here would rot.

---

## What moved vs. what stayed

Moved into the package: `NavGridSystem` (named `GridSystem` before 2026-08-29), `FlowFieldSystem`, `DStarLiteSystem`,
`PathfindingCoordinatorSystem`, `PathRequestSystem`, `PathStuckCheckSystem`,
`FormationOffsetSystem`, `HordeSystem`, `DStarLiteFollowerSystem`, `FlowFieldFollowerSystem`,
`UnitMoverSystem`, `UnitGravitySystem`, `StairTransitionSystem`,
`SetupUnitMoverDefaultPositionSystem`, `PathfindingUtils`, `HordeUtils`, the `Movement`/
`Gravity`/`Horde`/`HordeMembership`/`HordeMemberBuffer`/`PathfindingAgent`/`PathRequest`/
`StuckDetector`/`DStarLiteFollower`/`FlowFieldFollower`/`HordeRegistry`/`FormationType`/
`NavGridSettings`/`MovementStuck` components, and `MovementAuthoring`/
`PathfindingAuthoring`/`HordeAuthoring`/`GravityAuthoring`/`HordeRegistryAuthoring`/
`NavGridAuthoring`. `MovementSystemGroup` itself (root + `MovementCoordinatorSystemGroup`/
`MovementRoutingSystemGroup`/`MovementFollowerSystemGroup`/`MovementSteeringSystemGroup`
(empty, declared slot)/`MovementExecutionSystemGroup`) is declared inside the package, not in
the game's `SystemGroups.cs`.

Stayed in game code (`Assets/_Scripts`), all under `using DotsMovementToolkit;`:

| System | File | Why it stays |
|---|---|---|
| `PlayerMoveSystem` | `Systems/MovementSystemGroup/MovementFollowerSystemGroup/PlayerFollowerSystem.cs` | Writes `Movement.targetPosition` directly from player input — game-specific, not `PathRequest`-driven |
| `LocomotionStanceSystem` | `Systems/MovementSystemGroup/MovementExecutionSystemGroup/LocomotionStanceSystem.cs` | Bridges `StateMachine.currentStance` (AI-spine) → `Movement.isRunning` |
| `MovementStuckBridgeSystem` | `Systems/MovementSystemGroup/MovementCoordinatorSystemGroup/MovementStuckBridgeSystem.cs` | Maps the package's generic `MovementStuck` → this game's `ActionInterruptRequest`. `[UpdateAfter(typeof(PathStuckCheckSystem))]`, same package group, so the mapping happens same-frame |
| `UnitSpeedBakingSystem` | `Systems/PostBakingSystemGroup/UnitSpeedBakingSystem.cs` | Copies `UnitSO` (game data) speeds into the package's `Movement` component post-bake |
| `OrderMarkerSystem` | `Systems/PresentationSystemGroup/OrderMarkerSystem.cs` | Reads the game-only `HordeOrderMarker` (see below) alongside the package's `Horde` |

Note these three game files legitimately declare `[UpdateInGroup(typeof(MovementCoordinatorSystemGroup))]`
/ `MovementFollowerSystemGroup` / `MovementExecutionSystemGroup` (a **package** group) — that's
allowed (game code plugging into a package group), and their folder still matches per
`SystemPlacementConformanceTests`'s regex **only because they use an unqualified `typeof(...)`
via `using DotsMovementToolkit;`** — a fully-qualified `typeof(DotsMovementToolkit.Foo)` would
not match the folder-name regex and would fail the conformance test. Keep the `using`.

## Horde.markerEntity → HordeOrderMarker

The order-destination marker GameObject reference used to live on the package's `Horde`
component (`markerEntity`). The extraction split it into a new game-only component,
`HordeOrderMarker` (`Components/Units/HordeOrderMarker.cs`), added alongside `Horde` on the
same horde entity by `PlayerControllerAuthoring`'s baker. `OrderMarkerSystem` now queries
`(RefRO<Horde>, RefRO<HordeOrderMarker>)` together — a horde entity with no `HordeOrderMarker`
(e.g. one created via `HordeUtils.CreateHorde` or `HordeAuthoring`, neither of which add it)
simply doesn't match, same effective behavior as the old `markerEntity == Entity.Null` check.

`Horde.behaviorFlags` was dropped entirely during the extraction (confirmed dead: always
written `0`, never read anywhere).

## Death / revive wiring

`Movement` and `Gravity` are `IEnableableComponent`. `DeathSystem.DeathJob` disables both
(`[WithPresent(typeof(Movement))]`/`Gravity` — same pattern as its existing `PathRequest`/
`DStarLiteFollower`/`FlowFieldFollower`/`HordeMembership` handling); `ReviveRequestSystem.ReviveJob`
re-enables both. `UnitMoverJob`/`UnitGravityJob` inside the package have **no** `Dead` filter at
all — the enabled bit on `Movement`/`Gravity` themselves is the gate now. `SpawnStateInitSystem`
resets both to enabled on every `NewlySpawned` entity (spawn AND pool reclaim) — required per
the [[Gotchas]] "enableable bits aren't reliably copied on reclaim" trap; skip this and a
reclaimed corpse-turned-fresh-unit silently never moves again.

`UnitAnimationAssignmentJob` (`AnimationAssignmentSystemGroup`) carries an explicit
`[WithPresent(typeof(Movement))]` — the one place where letting the new enabled-gate exclude
dead units would have been a real regression (it's what assigns the Death animation clip via
`unitAction.current == ActionType.Death`), not a harmless no-op like the other movement/
pathfinding jobs (which already excluded dead units via their own follower/request enable
flags, so gaining the Movement gate too changed nothing observable for them).

## Nav grid config

`NavGridAuthoring` is baked in `Assets/Scenes/SubScenes/DOTSTestScene.unity` (the real DOTS
sandbox — `Game.unity` itself holds no baked content and is not wired to any subscene; it's
loaded via `Assets/Scenes/TestArea.unity`'s `SubScene` component instead). Today's values match
the pre-extraction hardcoded constants: 100×100×1 grid, cellSize 2, layerHeight 3,
wallLayerMask = Walls (8), heavyLayerMask = PathfindingHeavy (9), groundLayerMask = Ground (3)
+ Structures (7), wallCost 255 / heavyCost 50 / defaultCost 1.

**Grid placement (fixed 2026-08-29, second pass).** The grid used to be hard-anchored at world
origin extending only into +X/+Z, so with a 100×100 grid at cellSize 2 it covered X/Z 0..200 while
the level spans roughly -50..+50 — three quarters of the level was off-grid and three quarters of
the grid was empty space. `NavGridSettings`/`NavGridConfig` now carry `gridOrigin` (world position
of cell (0,0)'s corner), baked from the authoring transform and centred on it by default
(`centerGridOnThisTransform`). Every world↔cell conversion in the package takes it — the loose
`cellSize` overloads were kept for jobs, but each also takes `gridOrigin`, so a missed call site is
a compile error rather than a silent off-by-a-hundred-metres.

Current baked value for `MovementGridConfig` (at world origin): `gridOrigin = (-100, 0, -100)`,
footprint X -100..100, Z -100..100.

## Nav grid rename (2026-08-29)

`Grid*` → `NavGrid*` across the package, and the three runtime components were promoted out of
`GridSystem`'s nested scope into `Runtime/Components/NavGridComponents.cs`:

| Was | Now |
|---|---|
| `GridSystem` | `NavGridSystem` |
| `GridSystem.GridConfig` | `NavGridConfig` |
| `GridSystem.GridCostMap` | `NavGridCostMap` |
| `GridSystem.StairConnection` | `NavGridStairConnection` |
| `MovementGridSettings` | `NavGridSettings` |
| `GridConfigAuthoring` | `NavGridAuthoring` |
| `UpdateCostMapJob` | `UpdateNavGridCostMapJob` |

The `.cs.meta` GUIDs were carried across with `git mv`, so the `MovementGridConfig` GameObject in
`DOTSTestScene.unity` kept its component and its serialized values — no re-authoring needed. The
type renames do invalidate the baked entity-scene cache, so the subscene re-bakes on first open.

## Nav grid debug view

`NavGridDebugRenderSystem` (package, `#if UNITY_EDITOR || DEVELOPMENT_BUILD`) draws the live cost
map as one vertex-coloured mesh via `Graphics.RenderMesh` — Game **and** Scene view, Play mode
only, since the cost map is built from the physics world at runtime. Turn it on with **Debug View
> Debug Display Mode** on `NavGridAuthoring`.

Traps worth knowing:

- Settings are **baked**, so a change only reaches the world on a re-bake. With the subscene open
  for editing, live baking pushes it each frame; with it closed, reopen or re-enter Play mode.
- The mesh only rebuilds when `NavGridCostMap.costMapVersion` bumps (or a geometry-shaping setting
  changes). `NavGridSystem` bumps that only when it actually reruns `UpdateNavGridCostMapJob`,
  which it gates on physics `NumBodies` changing — so an obstacle that *moves* without adding or
  removing a body will not refresh the cost map or the view. That's a pre-existing limitation of
  the change proxy (package README, Known Issues), not a bug in the debug view.
- Blocked cells are extruded into blocks (`debugObstacleExtrusionHeight`, discouraged cells get
  half) — flat tiles are near-invisible under this game's shallow camera angle.
- On each cost-map rebuild it logs a census (`N blocked + M discouraged of C cells` + footprint),
  only when the numbers change. An all-zero census escalates to a warning listing the three causes
  of an empty-looking view. **Read that line first** — as of this pass exactly one object in the
  project sits on the `PathfindingWalls` layer (a 1-unit sphere at ~(37.6, 0, 15.8) in
  `DOTSTestScene`), so "no tiles fill in" was mostly "there is nothing to fill in", not a renderer
  bug. The census is what makes that distinguishable without reading code.
- `FullGrid` over `maxDrawnCells` silently downgrades to `ObstaclesOnly` with one console warning.
  There is also a hard internal ceiling of 60k quads, since an extruded cell costs five.
- In a player build `Hidden/DotsMovementToolkit/NavGridDebug` must be in Project Settings >
  Graphics > Always Included Shaders, or the system logs one warning and draws nothing.
