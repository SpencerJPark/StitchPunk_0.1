# MonoBehaviours — Context

This folder contains the **non-ECS layer** of the game: managers that bridge Unity's MonoBehaviour world with the DOTS world, plus camera and input handling.

---

## Managers (`Managers/`)

These are `RegulatorSingleton<T>` or `PersistentSingleton<T>` instances that live in the scene.

| Manager | Responsibility |
|---|---|
| `PlayerInputManager` | Reads Unity Input System actions; fires events to ECS via `DOTSEventsManager` |
| `DOTSEventsManager` | Bridge — writes player intent (click targets, selections) into ECS singleton components |
| `UnitSelectionManager` | Tracks which units are selected; updates `Selected` component on body entities |
| `CameraManager` | Controls cinemachine camera state, target switching |
| `BuildingPlacementManager` | Ghost preview and placement confirmation for buildings |
| `ResourceManager` | Tracks global resource counts (non-ECS; will likely migrate to DOTS later) |
| `RagDollManager` | Spawns and manages physics ragdoll objects on unit death |

---

## Bridging ECS and MonoBehaviour

Use `SystemAPI.GetSingleton<T>()` / `SystemAPI.SetSingleton<T>()` from inside a system to read/write shared state that MonoBehaviours also need. For the other direction (MonoBehaviour → ECS), write to a singleton component through the `World.DefaultGameObjectInjectionWorld.EntityManager`.

Do not use `DOTSEventsManager` as a general message bus — it is specifically for input-driven world events.

---

## Debug Utilities

`FlowFieldSystemDebug`, `FlowFieldSystemDebugSingle`, `InteractionRuntimeDebugDrawer` — Gizmo-based visualisers for pathfinding and interaction slots. Safe to leave in scenes during development; strip before shipping.
