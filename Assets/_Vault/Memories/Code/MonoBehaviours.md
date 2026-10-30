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

## Camera world lean

`CameraWorldLean` (on `Prefabs/SetUp/Main Camera 1.prefab`, the camera CampLoop and TestArea share) leans the rendered world away from the camera by `leanStrength` × camera pitch, pivoting on `groundHeight`, so 3D meshes read on the same plane as sprite billboards. It is view-only: it writes `worldToCameraMatrix` in `RenderPipelineManager.beginCameraRendering` (after Cinemachine), so transforms, physics, lighting and shadow maps stay upright and stock materials work. Billboards must stay upright (`VisualBillboardSystem`) or they lean twice. Culling trap in [[Gotchas]]. Replaced the per-material `SkewToCamera` shader experiment, which leaned per vertex and broke outlines.

---

## Bridging ECS and MonoBehaviour

Use `SystemAPI.GetSingleton<T>()` / `SystemAPI.SetSingleton<T>()` from inside a [[Systems]] system to read/write shared state that MonoBehaviours also need. For the other direction (MonoBehaviour → ECS), write to a singleton component through the `World.DefaultGameObjectInjectionWorld.EntityManager`.

Do not use `DOTSEventsManager` as a general message bus — it is specifically for input-driven world events.

---

## Debug Utilities

`FlowFieldSystemDebug`, `FlowFieldSystemDebugSingle`, `InteractionRuntimeDebugDrawer` — Gizmo-based visualisers for pathfinding (see [[Systems_Movement]]) and interaction slots (see [[Systems_AI]]). Safe to leave in scenes during development; strip before shipping.

OnGUI debug menus only see the OS pointer, never the virtual cursor (`CursorScreenPosition`). `DebugMenuVirtualCursorInput` is the fix (2026-10-30): it registers the menu's area with `VirtualCursorUiRouter`, records the click, and lets the button under it claim the click in that frame's Repaint pass. While `ControlUnits` is active it uses up real mouse events, because the hidden OS pointer is somewhere else on screen. `DebugMenuVisibility` gives every debug menu one shared show/hide key, backquote (F1 is a media key on laptops). `DebugResourceMenu` and `DebugTradeMenu` use both; the save and zombify menus do not yet. Key input sent from `execute_code` (`QueueStateEvent`) goes to the Editor, not to Play mode, so it can't test these menus. Only a real key press can.
