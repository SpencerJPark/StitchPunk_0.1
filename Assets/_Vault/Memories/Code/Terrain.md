---
tags: [memory, code, terrain]
related: "[[Editor]], [[Data]], [[Gotchas]]"
---

# Terrain (plan: `Tasks/Plans/Terrain_System.md`, spikes: `Terrain_Spikes.md`)

## What exists (P1 + P2)
- Data (`Data/Terrain/`): `TerrainWorldProfile` (30 x 30 tiles from -15), `TerrainPalette` (`ApplyToMaterial` names match `Painted/Ground`), `TerrainTileData` (one SO per tile, every source layer a separate array; final height = sum of base + hand + river + road, 101 x 101 samples, index `z * 101 + x`, sample 0 = min corner).
- `TerrainTile` (MonoBehaviours) now carries `tileData` + `palette`.
- Editor: see the `Terrain/` row in `Editor.md`. Tile (0,0) lives in its own subscene `Assets/Terrain/Tiles/Tile_0_0/Tile_0_0.unity` (AutoLoad true until P9 streaming), under a `TerrainWorld` object in `CampLoop`.
- Collider is a legacy `MeshCollider` on the tile (S1: a heightfield is 10x smaller, 76 KB vs 807 KB, but needs a custom baker; deferred).

## Paint (P3)
- Sources per tile: base mask PNG (hard RGB, opaque), `PaintOverlay.png` (RGB layer colour, A = painted), optional `RoadMask.png`. `TerrainCompositor` (base, then overlay, then road wins; river layer not built) writes the SDF PNG; it never writes a source layer or a height array.
- Live painting (`Brushes/TerrainPaintBrush`): per-stroke session per tile with its own uncompressed RGBA32 SDF texture on the material (the imported SDF is BC7 and cannot be edited), dirty-rect encode `EncodeSignedDistanceFieldRegion`, PNG + recomposite only on mouse-up. Undo = revision match + saved overlay rect.
- Mask resolution is per tile (`TerrainTileAssets.MaskResolutionFor`; tile (0,0) stays 512); SDF PNGs are stamped BC7 on first creation, `Compress Selected Tile SDF (BC7)` fixes an old one.

## Terrain Editor window (P4, `Editor/Terrain/Window/`)
- `Stitch Punk > Terrain > Terrain Editor`: left rail (Tiles, Paint, Height live; Roads/Decals/Scatter/Nav/Stream disabled until their phase), panel host, footer (active tile, brush, panel status). Panels derive `TerrainToolPanel`; every visual lives in `TerrainEditor.uss` (shared class vocabulary, `--unity-colors-*` only), none in C#.
- Panels arm and disarm `BrushState.activeTool` in `OnShown`/`OnHidden`; the Brush menu still works and the panels re-sync every 250 ms.
- `TerrainTilesPanel`: Painter2D tile map of the world rect (click open, plus cell creates, right click close / select / delete). `TerrainTileCreator.CreateTileScene` makes a tile scene (AutoLoad false) and welds its seam heights to existing neighbours (read from their data assets, so unloaded ones count). Open tiles are remembered in `SessionState` `TerrainEditor.OpenTiles` and reopened after a reload.
- A clicked rail button can't be driven by a synthetic `ClickEvent`; send `NavigationSubmitEvent` to the Button.

## Procedural base + regions (P3c)
- `TerrainGenerationProfile` (Data): fbm height layers + `heightRemap`, one mountain (centre, radius, height, profile), `flattenBelowMetres`, ordered biome rules (a passing later rule wins; passes when `noise01 * weight >= threshold` and height and slope are in range). `TerrainSplatLayer` now lives in Data.
- `TerrainProceduralGenerator` (Editor, pure): noise is sampled in WORLD coordinates (adjacent tiles share edges exactly) and mask slope comes from the analytic height at +-1 m, never from a neighbour tile.
- `TerrainRegion` (persistent scene): bounds are WORLD coordinates (moving the transform does not move the box). `TerrainGenerateTool.Generate` writes only `baseHeights` and the base mask PNG (+ region palette), then composites and rebuilds; it never touches hand, river, road or paint layers, so Hand/Procedural/Mixed behave the same for now. A hand-authored tile is skipped unless confirmed. Generate rail tool + panel in the window.
- Measured: 9 tiles generate in about 20 s (BC7 reimport, mesh and collider cook each); creating 9 tile scenes takes about 18 s.

## Roads (P5)
- `TerrainRoad` (SplineContainer + `TerrainRoadProfile` + priority) raises `TerrainRoadEvents.RoadChanged` on spline edit, move, validate and disable. `TerrainRoadBaker` debounces 0.25 s, then per overlapped OPEN tile: rasterise the coverage mask (`TerrainRoadRasterizer`, pure, stamp-based), write `Tile_x_z_RoadMask.png`, carve `roadHeightDelta` (bed = terrain WITHOUT road, smoothed along the road by `heightSmoothingMetres`), Composite, rebuild mesh when heights changed, save the tile scene. Unopened tiles go to `StaleTiles`; `RebakeAllRoads(true)` opens them. Roads panel is the rail tab.
- `SplineContainer.EvaluatePosition` already returns WORLD space; do not TransformPoint it again.
- Road graph: `TerrainRoadGraphBuilder` (Utils, pure) merges junctions within 2 m, `TerrainRoadGraphAuthoring` (baker, drop one in the persistent subscene) emits `RoadGraphSingleton`, `RoadGraphQuery.TryFindRoute` (Burst) snaps both ends within 30 m and A*s over nodes. The blob parameter is `ref RoadGraphBlob`: the DOTS source generator rejects `in` (EA0009). The authoring component has NOT been added to CampLoop_Sub yet.
- Not built yet: decal rules on the road profile (P6), `TerrainRoad` crossing missing tiles only warns.

## Water (P5b)
- `TerrainRiver` (spline + `TerrainWaterProfile`), `TerrainWaterBody` (fixed level, WORLD-coordinate bounds box, fills wherever ground < level) and `TerrainCrossing` (Bridge/Ford oriented rectangle) raise `TerrainWaterEvents`; `TerrainWaterBaker` debounces like the road baker. Per open tile it writes `riverHeightDelta` (channel carve, never raises ground), `Tile_x_z_SurfaceOverlay.png` (R shallow, G deep, deep wins), water meshes as children `Water/Water_<n>` with sub-assets `Water_<n>` of the tile data asset (no collider; deep cells become walls through the nav bake in P8, not physics), then Composite and mesh rebuild.
- River levels never rise downstream (`level[i] = min(level[i-1], terrain[i] - 0.3)`, so a rise is dug through, not climbed). The compositor paints water-covered texels as Dirt (river bed) above the paint overlay and below roads. A Ford turns deep texels in its footprint shallow; a Bridge clears both flags under the deck. Nav and speed consumption of the overlay is P8.
- Road and river deltas read each other (the road bed ignores the road delta but includes the river delta and vice versa), so bake both after a big change to settle.
- The live paint brush still composites with the old 6-argument `CompositeTexels`, so water texels show their painted colour until mouse-up.

## Traps
- `CopySerialized(newMesh, existingMesh)` overwrites the mesh name; the tile builder re-names the sub-asset right after, or the next Rebuild no longer recognises it and tries to destroy an asset.
- The Scene view in authoring mode drew a flat plane for tiles whose heights are right (render bounds in Play show the real 81 m mountain); judge height in Play or by data, not by the authoring Scene view.
- A domain reload (any recompile) clears the Undo stack and closes tile subscenes.
- Sculpt only ever writes `handHeightDelta`. An edge sample exists in 2 (corner: 4) tiles and every copy must hold the same value; `TerrainHeightBrush` computes it once and writes all copies (test: `TerrainHeightBrushTests`).
- `ConvertToTileScene` closes the new scene with `removeScene`, which destroys the tile GameObject in the editor session; reopen with `SubSceneUtility.EditScene(subScene)`.
- A hand-built `Unity.Physics.PhysicsWorld` needs `RigidBody.Scale = 1` or every ray misses.
- DXT1/DXT5 break the SDF (1.3 m error); the SDF import format is BC7 (P3-0).
- Full SDF encode is 236 ms at 512 px (62 ms at 256), so live painting needs the dirty-rect encode (P3-1).
- Nothing has been done in the Scene view with the mouse yet (brush overlay and sculptor are compile + logic checked only); drive them once by hand.
