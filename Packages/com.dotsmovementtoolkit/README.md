# DOTS Movement Toolkit

Grid-based pathfinding and movement for Unity Entities: a shared grid/cost-map, D* Lite for
individual agents, flow fields for hordes/groups, formation offsets, stair/layer transitions,
and a `PathRequest`-driven API. Bake one `NavGridAuthoring` and issue `PathRequest`s — no
other project coupling.

Extracted from Stitch Punk. This is an **internal-quality extraction** — decoupled, documented,
compiling, play-tested — not yet a polished sellable toolkit. See Known Issues below.

## The contract

- **In:** add `PathRequest` (enableable) + `PathfindingAgent` to an entity, then drive it via
  `MovementAPI.BeginPathRequest` / `MovementAPI.HaltPathing`. Any system may also write
  `Movement.targetPosition` directly instead of going through `PathRequest` — that's how a
  player-controlled character typically works, and it stays fully supported.
- **Out:** `Movement` (position target + `isMoving`/`isRunning`, both enableable) is what the
  rest of your game reads — animation, camera, anything that cares whether a unit is walking.
  `MovementStuck` is an enableable tag set when an entity has an active `PathRequest` but hasn't
  made progress in ~4 seconds; map it onto whatever "cancel the current action" concept your
  game uses (see `MovementStuckBridgeSystem` in the consuming game for a worked example — it
  disables `MovementStuck` and enables the game's own interrupt tag).
- **Death/despawn:** `Movement` and `Gravity` are `IEnableableComponent`. Disable both when an
  entity dies or otherwise shouldn't move under its own power (a ragdoll, a cutscene actor);
  re-enable both on revive/respawn. A pooled entity being reclaimed must have every enableable
  component's bit reset explicitly — Entities does not reliably preserve or reset enabled bits
  across `Instantiate`/pool-reclaim.
- **Gating:** every package system requires the baked `NavGridSettings` singleton
  (see Quickstart) — no config in the world means the whole `MovementSystemGroup` idles. This
  is the generic, game-agnostic replacement for a scene-gate tag.

## Quickstart (nav grid config)

Add `NavGridAuthoring` to one GameObject in your subscene (or any baked scene content) and
set:

| Field | Meaning |
|---|---|
| `width` / `height` / `layerCount` | Grid extent. `layerCount` > 1 needs stairs (`StairUtils.AddStairConnection`) to connect layers — see Known Issues, layer support is half-plumbed. |
| `cellSize` / `layerHeight` | World-unit size of one cell / vertical distance between layers. |
| `wallLayerMask` / `heavyLayerMask` / `groundLayerMask` | Physics `LayerMask`s the grid samples against — walls block pathing entirely, "heavy" cells cost more, `groundLayerMask` is what `UnitGravitySystem` raycasts down onto. |
| `wallCost` / `heavyCost` / `defaultCost` | Byte cost-map values. `wallCost` doubles as the "impassable" sentinel every pathfinding/flow-field comparison in the package checks against — pick a value nothing else would realistically produce (`byte.MaxValue` by default). |

**The grid is anchored at world origin**, not at the authoring GameObject's transform: cell (0,0)
is world (0,0,0) and the grid extends into +X/+Z. Moving the GameObject changes nothing. The
Scene-view gizmo is drawn in world space for exactly this reason — check that the footprint
actually covers your level before wondering why units path into walls at the far edge.

Then add `MovementAuthoring` (+ `GravityAuthoring` if the entity should fall) to any unit, and
either `PathfindingAuthoring` (individual D* Lite / flow-field agent) or `HordeAuthoring`
(shared group target) depending on whether it moves alone or in a formation.

## Debug view

`NavGridAuthoring`'s **Debug View** block turns on `NavGridDebugRenderSystem`, which draws the
live cost map as a single vertex-coloured mesh through `Graphics.RenderMesh` — visible in the
Game view *and* the Scene view, one draw call for the fills and one for the outlines. The whole
system is compiled out by `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

| Field | Meaning |
|---|---|
| `debugDisplayMode` | `Off`, `ObstaclesOnly` (only cells whose cost differs from `defaultCost` — walls, heavy terrain, and cells still fading from a recent change), or `FullGrid` (every cell). |
| `debugLayerToDraw` | Layer index, or `-1` for every layer at its own world height. |
| `debugHeightOffset` / `debugCellPadding` | Lift above the layer floor, and the gap shrunk from each cell edge so individual tiles read as tiles. |
| `debugChangeHighlightSeconds` | How long a cell flashes `debugRecentlyChangedColor` after its cost changes — this is what makes a live obstacle visible as it lands, and a freed cell visible as it clears. `0` disables change tracking entirely (and its two per-cell arrays). |
| `debugMaxDrawnCells` | Hard budget. A `FullGrid` request over it downgrades to `ObstaclesOnly` with one console warning rather than stalling the editor. |
| colour fields | Resting colours for walkable / discouraged / blocked, plus the change flash. Costs between `defaultCost` and `wallCost` ramp from walkable toward discouraged, so a multi-tier cost map stays readable. |

Outside Play mode there is no cost map to draw — `NavGridSystem` builds it from the physics
world at runtime — so the authoring falls back to a Scene-view gizmo of the grid footprint
(`drawBoundsGizmo`), plus the cell lattice while the object is selected
(`drawLatticeGizmoWhenSelected`).

Two things to know before you file a bug against it:

- The settings are **baked**. With the subscene open for editing, live baking pushes a change
  through each frame; with it closed, reopen it or re-enter Play mode.
- The view is only as fresh as the cost map, and the cost map only rebuilds when physics
  `NumBodies` changes (see Known Issues). An obstacle that *moves* without a body being added or
  removed will not show up until something else triggers a rebuild.

In a player build, `Hidden/DotsMovementToolkit/NavGridDebug` must be listed under Project
Settings > Graphics > Always Included Shaders — otherwise `Shader.Find` returns null, the system
logs one warning and draws nothing. In the Editor it always resolves.

## Adding a pathfinding strategy

One strategy = one `PathfindingMode` enum member + one routing system (consumes `PathRequest`,
lives in `MovementRoutingSystemGroup`) + one enableable follower component + one follower system
(lives in `MovementFollowerSystemGroup`, writes `Movement.targetPosition`). `PathRequestSystem`
is the dispatcher — it reads `PathRequest.requestedMode` and flips on the matching follower
component; add your new mode there too. New strategies ship inside the package, so extending
the enum is the intended way to grow it — this isn't a plug-in system for external assemblies.

## Known Issues

Not fixed in this pass — extraction and improvement are deliberately separate, so a play-test
regression stays attributable to one or the other:

- **D* Lite replan is main-thread and full-grid per request** (`DStarLiteSystem.ComputeDStarLitePath`)
  — fine at today's scale, will not scale to many simultaneous individual pathfinders.
- **`NavGridSystem` uses physics `NumBodies` as a change proxy** plus a `CompleteDependency()` sync
  each time it changes (`NavGridSystem.cs`) — correct but coarse (any body add/remove anywhere
  rebuilds the whole cost map) and a hard sync point.
- **Flow-field ring-buffer slot reuse can clobber a field still in use**
  (`FlowFieldSystem.FLOW_FIELD_MAP_COUNT` wraps without checking the old slot is done).
- **Layer support is half-plumbed**: the cost map is layered end-to-end, but flow-field and
  D* Lite indexing assume a single layer. Multi-layer buildings need stairs wired by hand via
  `StairUtils` and haven't been stress-tested.
- **Per-entity line-of-sight raycasts every frame** in both follower systems — fine at current
  unit counts, a first target for batching/throttling if it shows up in a profile.
- **`PathfindingUtils.GetFlowDirectionSmooth`** (bilinear flow sampling for smoother movement)
  and several `NavGridSystem`/`PathfindingUtils` static helpers (`IsWall`, `IsWalkable`,
  `GetMovementCost`, `GetNeighbors`, `GetCardinalNeighbors`, `HasLineOfSight`, `ManhattanDistance`)
  are written but not called from anywhere in the package — candidates for the still-empty
  `MovementSteeringSystemGroup`, or for deletion if a future pass confirms nothing needs them.
