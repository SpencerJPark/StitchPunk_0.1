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

OnGUI debug menus only see the OS pointer, never the virtual cursor (`CursorScreenPosition`). `DebugResourceMenu` shows the fix (2026-10-30): it registers with `VirtualCursorUiRouter`, records the click, and lets the button under it claim the click in that frame's Repaint pass. While `ControlUnits` is active it uses up real mouse events, because the hidden OS pointer is somewhere else on screen. The backquote key (`) shows and hides it; F1 was dropped because laptop F-keys send media keys. Copy this pattern if another debug menu needs the in-game cursor.

## Settings menu (2026-10-30)
`SettingsMenuUI` (UI) is a placeholder view for the future Rive menu: all logic lives in `UserSettings` (per-machine `user_settings.json`: volumes, video, rebind overrides — NOT in save slots; `AudioManager` reads volumes from it, the `GameSettings` volume fields are now unused) and `InputRebinding` (registered by `PlayerInputManager.Awake`; main menu uses the view's `fallbackInputActions`). `GameMenuManager.VisibilityChanged` opens/closes the view. The menu key is polled in `PlayerInputManager` (`Player|ControlUnits|Vehicle/ToggleMenu`, `UI/Cancel`) — do NOT also wire the PlayerInput `OnToggleMenu` event or it double-toggles. Escape cancels a rebind, so the toggle ignores presses within a frame of a rebind ending.
