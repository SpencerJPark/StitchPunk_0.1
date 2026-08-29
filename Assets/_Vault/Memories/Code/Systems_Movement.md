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

Moved into the package: `GridSystem`, `FlowFieldSystem`, `DStarLiteSystem`,
`PathfindingCoordinatorSystem`, `PathRequestSystem`, `PathStuckCheckSystem`,
`FormationOffsetSystem`, `HordeSystem`, `DStarLiteFollowerSystem`, `FlowFieldFollowerSystem`,
`UnitMoverSystem`, `UnitGravitySystem`, `StairTransitionSystem`,
`SetupUnitMoverDefaultPositionSystem`, `PathfindingUtils`, `HordeUtils`, the `Movement`/
`Gravity`/`Horde`/`HordeMembership`/`HordeMemberBuffer`/`PathfindingAgent`/`PathRequest`/
`StuckDetector`/`DStarLiteFollower`/`FlowFieldFollower`/`HordeRegistry`/`FormationType`/
`MovementGridSettings`/`MovementStuck` components, and `MovementAuthoring`/
`PathfindingAuthoring`/`HordeAuthoring`/`GravityAuthoring`/`HordeRegistryAuthoring`/
`GridConfigAuthoring`. `MovementSystemGroup` itself (root + `MovementCoordinatorSystemGroup`/
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

## Grid config

`GridConfigAuthoring` is baked in `Assets/Scenes/SubScenes/DOTSTestScene.unity` (the real DOTS
sandbox — `Game.unity` itself holds no baked content and is not wired to any subscene; it's
loaded via `Assets/Scenes/TestArea.unity`'s `SubScene` component instead). Today's values match
the pre-extraction hardcoded constants: 100×100×1 grid, cellSize 2, layerHeight 3,
wallLayerMask = Walls (8), heavyLayerMask = PathfindingHeavy (9), groundLayerMask = Ground (3)
+ Structures (7), wallCost 255 / heavyCost 50 / defaultCost 1.
