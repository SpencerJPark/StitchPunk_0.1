---
tags: [memory, code, monobehaviour, bridge]
related: "[[Core]], [[Systems]]"
---

# MonoBehaviours — Context

This folder contains the **non-ECS layer** of the game: managers that bridge Unity's MonoBehaviour world with the DOTS world, plus camera and input handling. Singleton base classes for these managers live in [[Core]].

---

## Managers (`Managers/`)

These are `RegulatorSingleton<T>` or `PersistentSingleton<T>` instances that live in the scene.

| Manager | Responsibility |
|---|---|
| `PlayerInputManager` | Reads Unity Input System actions; fires events to ECS via `DOTSEventsManager`. Also handles mouse aim direction each update: while `AimPlayerInput` is enabled and scheme is Keyboard&Mouse, converts mouse world position to XZ aim direction and writes `LookPlayerInput` directly to ECS |
| `DOTSEventsManager` | Bridge — writes player intent (click targets, selections) into ECS singleton components |
| `UnitSelectionManager` | Drag/click selects `Minion`-enabled bodies; writes `OnMinionMoveCommand` / `OnMinionInteractCommand` to the player entity on right-click command. Reads `PlayerActionMap` to check for `ControlUnits` mode. ⚠ Needs revamp — currently has dead references to `BuildingPlacementManager`, `BuildingBarracks`, and old `PlayerInputData` singleton. |
| `CameraManager` | Controls cinemachine camera state, target switching |
| `BuildingPlacementManager` | Ghost preview and placement confirmation for buildings |
| `ResourceManager` | Tracks global resource counts (non-ECS; will likely migrate to DOTS later) |

---

## Bridging ECS and MonoBehaviour

Use `SystemAPI.GetSingleton<T>()` / `SystemAPI.SetSingleton<T>()` from inside a [[Systems]] system to read/write shared state that MonoBehaviours also need. For the other direction (MonoBehaviour → ECS), write to a singleton component through the `World.DefaultGameObjectInjectionWorld.EntityManager`.

Do not use `DOTSEventsManager` as a general message bus — it is specifically for input-driven world events.

---

## Debug Utilities

`FlowFieldSystemDebug`, `FlowFieldSystemDebugSingle`, `InteractionRuntimeDebugDrawer` — Gizmo-based visualisers for pathfinding (see [[Systems_Movement]]) and interaction slots (see [[Systems_AI]]). Safe to leave in scenes during development; strip before shipping.
