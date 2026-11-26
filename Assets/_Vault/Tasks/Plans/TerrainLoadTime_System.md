# Terrain load time (Editor) — spec

Status 2026-11-26: specced and being built the same day (owner: "make it as fast as possible").

## Problem

Opening a 3 x 3 terrain world (RandomWorld) took 5+ minutes, looped, and once lost an import worker. Read-only audit
(2026-11-26) of the load path, with measured file sizes:

| # | Cause | Evidence |
|---|---|---|
| C1 | `TerrainMesh` is 181,134 vertices per 100 m tile (~1.6M for 9 tiles), stored as ~16.6 MB of hex text inside `Tile_x_z_Data.asset` (whole asset ~20 MB, 188 MB for 9 tiles). Every open, import and `DependsOn(tileData)` bake parses it. | `TerraceTileMeshBuilder` `SamplesPerSide = CellsPerSide * SamplesPerMetre + 1`; TestWorld tile asset 14 MB |
| C2 | `TerrainOpenTilesSessionRestore` re-records the open-tile list every second and live-bakes (`SubSceneEditorUtility.EditScene`) every recorded tile on each reload, scene open and Play exit: once all 9 were open, all 9 re-bake forever. | `TerrainTilesPanel.cs:631-704` |
| C3 | Event storm: `[ExecuteAlways]` roads/areas/water/terraces raise a rebake from `OnDisable` (close, reload) and `OnValidate` (load); each baker composites and rebuilds the same tile separately (3-4 times per open), some with `ImportAsset(ForceUpdate)` / `SaveAssets`. | `TerrainRoad.cs:104-126`, `TerrainArea.cs:171-194`, `TerrainSplatCodec.cs:55` |
| C4 | `TerrainClosedTilePreview` calls `SceneSystem.LoadSceneAsync` for every closed tile every 0.5 s until loaded; with a stale entity-scene cache that is an import per tile at once. | `TerrainClosedTilePreview.cs:15-105` |
| C5 | `TerrainFeatureBaker` opens closed tiles when an area/path is selected. | `TerrainFeatureBaker.cs:82-85, 281-283` |

## Decisions (LT-D*)

- **LT-D1 Measure in place.** One `TerrainLoadTimer` (Editor) writes `[TerrainLoad] <stage> <ms>` lines: scene open to
  first frame, each tile reopen, each closed-tile preview stream-in, each rebake (by baker and tile). Off by default
  behind `EditorPrefs` `TerrainEditor.LogLoadTimes`; menu `Stitch Punk/Terrain/Log Load Times`.
- **LT-D2 Restore only what the user opened.** The session list changes only when the user opens or closes a tile
  (Tiles panel, tile map, `OpenTileForEdit` from a user action), never from a once-a-second scan of what happens to be
  open. A build or bake that opens tiles closes them again and never records them. Scene open clears the list for tiles
  not in that scene. Restore opens at most one tile per editor frame.
- **LT-D3 No rebakes from loading.** A static `TerrainRebakeGate` suppresses rebake events while a scene is opening, a
  tile subscene is opening/closing, during domain reload (`AssemblyReloadEvents`) and for 1 s after. `OnDisable` never
  raises a rebake (a disabled feature that really changed is caught by its own edit path). Bakers coalesce: one queue of
  dirty tiles, one composite + rebuild per tile per flush.
- **LT-D4 Closed preview one at a time.** At most one `LoadSceneAsync` in flight; next tile only after the previous is
  loaded or 10 s timed out; no repeat requests for a tile already requested this session.
- **LT-D5 Lighter ground mesh.** `TerrainMesh` keeps full density only where height changes (terrace edges, slopes,
  rivers, roads); flat runs merge into larger quads (greedy quad merge on the sample grid, no T-junction cracks: merged
  quads are split at every neighbour vertex on their border). Target <= 30k vertices per tile. Owned by the Terraces
  code (`TerraceTileMeshBuilder`): landed with a peer note, visual check against the r4 captures.
- **LT-D6 Do not change project serialization.** Binary assets would shrink C1 ~2x but rewrites every asset diff in a
  public repo; LT-D5 fixes the cause.
- **LT-D7 Dropped.** The audit read C5 as "selection opens tiles"; the code only opens closed tiles when the selected
  feature is EDITED, which must show its result. Scene loads no longer raise those events (LT-D3), so C5 is covered.

## Phases

| Phase | Work | Files |
|---|---|---|
| L1 | LT-D1 timer + LT-D2 restore | `TerrainLoadTimer.cs` (new), `Window/TerrainTilesPanel.cs` |
| L2 | LT-D3 gate + coalescing | `TerrainRebakeGate.cs` (new), `TerrainRoad.cs`, `TerrainArea.cs` (+ other raisers), bakers |
| L3 | LT-D4 preview + LT-D7 | `TerrainClosedTilePreview.cs`, `Features/TerrainFeatureBaker.cs` |
| L4 | LT-D5 mesh | `Terraces/TerraceTileMeshBuilder.cs` |

## Acceptance

- A1 Opening RandomWorld (3 x 3) with no tiles recorded open reaches an interactive editor in < 30 s on the dev machine
  (log lines prove it); with one tile recorded open, < 45 s.
- A2 A recompile with one tile open re-bakes exactly one tile once (log lines).
- A3 Play exit re-bakes nothing that did not change.
- A4 `TerrainMesh` <= 30k vertices per RandomWorld tile, no visible cracks or terrace-edge change at the r4 capture
  angles; `Tile_x_z_Data.asset` <= 5 MB.
- A5 EditMode terrain fixtures still green.

## Log

- 2026-11-26 spec written from the audit; L1-L4 started.
