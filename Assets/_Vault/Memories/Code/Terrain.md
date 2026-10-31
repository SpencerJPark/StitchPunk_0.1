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

## Decals and scatter (P6, P7)
- One pure `TerrainScatterPlanner` (Editor) serves both: biome-weighted rules, slope and height ranges, spacing grid shared across rules, cluster noise, obstacles (locked instances), deterministic per `seed` + tile (`TerrainDecalScatterer.SeedFor`). `TerrainTileSampler` feeds it a tile's composite mask (water texels and off-tile are blocked).
- Decals: `TerrainDecalSet` (default rows copied from the old `DecalScatter`, density about 5 per 100 m^2) -> `PaintedDecal` children under the tile's `Decals`, each with a `TerrainDecalMarker` (locked flag). Props: `TerrainScatterSet` rules -> prefab instances under `Scatter` with `TerrainScatterMarker`; `blocksNavigation` rules put the instance on the `PathfindingWalls` layer (layer 8, the nav wall layer). `Regenerate` keeps locked ones and steers around them, but locked ones are EXTRA: density is not reduced to count them.
- Brush tools (`TerrainBrushTool.Decal*`, `Scatter*`), panels Decals and Scatter, `BrushState.activeDecalSet/decalEntryIndex/activeScatterSet/scatterRuleIndex`.
- S3: `PAINTED_ENTITIES` is now defined (`PaintedEntitiesDefine`, saved in ProjectSettings) so tile decals bake with per-instance `_Tile`/`_Tint`. `PaintedGround.Runtime.asmdef` was added so `StitchPunk.Editor` can reference `PaintedDecal` (Assembly-CSharp types are invisible to asmdef assemblies); tests reference it too. Adding the define the first time recompiles everything and the Editor stalled about 10 minutes.
- Legacy: the 500 camp decals are still plain GameObjects in `CampLoop`.

## Nav window (P8, built 2026-10-31, play-checked once)
- Toolkit: `NavGridSettings.externalCostRegions` (grid starts all wall, no physics rebuild) + `NavGridCostMap.windowVersion`. Tile data: `TileNavCost` blob + `TerrainTileNavAuthoring` (per-tile `costs`/`surface` bytes, `[layer*n*n + z*n + x]`, `TerrainSurface` Normal/Road/Shallow/Deep). Rules: `TerrainNavCellRules` (deep water and 45 degrees = wall, 25 degrees = heavy 50, shallow beats road, bridge/ford overrides). Baker: `TerrainNavBaker` (menu `Stitch Punk/Terrain/Bake Nav (Open Tiles)`, Nav rail tab) samples slope, SurfaceOverlay, RoadMask, composite palette cost, `Physics.CheckSphere` on layer 8 (GameObject colliders only), then crossings.
- Runtime: `NavWindowSystem` (`Systems/MovementCoordinatorSystemGroup/`, after `NavGridSystem`) re-centres a (2R+3)^2-tile window on the first `TerrainNavFocus` entity and blits loaded tiles' blobs, bumping both versions; window min tile = focus - (R+1), origin = min tile centre*100 - 50. `NavFootprint` entities (authoring `NavFootprintAuthoring`) are the only way structures reach the map: `NavFootprintChangeDetectSystem` sets `TerrainNavWindowSettings.rebuildRequested` when footprints are added, removed or moved, `NavWindowSystem` rebuilds and clears it, `NavFootprintStampSystem` re-stamps after every `windowVersion` change. Surface speed: `TerrainSurfaceSpeedSampleSystem` attaches `SurfaceSpeedFactor` to every `Movement` entity and reads the tile blob; Apply scales `Movement` before `MovementExecutionSystemGroup` and Revert divides back after `UnitMoverSystem` (other systems capture `moveSpeed` as a base). Cell index is `floor(offset / (tileSize / cellsPerTile))`; sampling is every frame, not every 5th.
- Test scene (CampLoop): 3 x 3 tiles auto-load (P9 will stream), `MovementGridConfig` is 500 x 500 at 1 m with `externalCostRegions` on, `PlayerUnit` carries `TerrainNavFocus`. Live check in Play: origin (-250, -250), 90,000 open cells, versions bumped as tiles loaded. The camp has no layer-8 colliders, so nothing was lost by switching off the physics rebuild.
- Not done: baked flow-field map count (S4: 50 maps = 150 MB at 500 x 500; `FLOW_FIELD_MAP_COUNT` is a const in the package), flow fields and D* Lite never read `costMapVersion` so they keep stale cells after a window move, the Nav panel and debug overlay were never looked at, the caravan's own speed seam, `NavFootprintAuthoring` is on no prefab yet, bake does not run automatically on tile creation.
- Asmdef trap hit: `StitchPunk.Editor` needed `StitchPunk.Authoring`, `DotsMovementToolkit.Runtime` and `DotsMovementToolkit.Authoring`; a duplicated reference made compilation fail silently (EditMode tests still ran against old assemblies, the PlayMode runner just timed out). Check `EditorUtility.scriptCompilationFailed` and the console for "duplicate references" when a test run will not start.

## Traps
- A test that creates GameObjects in the ACTIVE scene can leak them, and any later `SaveScene(active)` (tile creation does this) writes them into CampLoop.unity (it grew by 100k lines). Scatter tests run in `EditorSceneManager.NewPreviewScene()` (an additive `NewScene` throws "untitled scene unsaved" because the test runner leaves an untitled scene open); check `git diff --stat Assets/Scenes/CampLoop.unity` after any test run that touches scene objects.
- `Assets/_Scripts/Tests/StitchPunk.Tests.asmdef` edits: apply one change at a time and re-validate the JSON; two overlapping edits once corrupted it and the test list silently became empty (0 tests, run "succeeded").
- `CopySerialized(newMesh, existingMesh)` overwrites the mesh name; the tile builder re-names the sub-asset right after, or the next Rebuild no longer recognises it and tries to destroy an asset.
- The Scene view in authoring mode drew a flat plane for tiles whose heights are right (render bounds in Play show the real 81 m mountain); judge height in Play or by data, not by the authoring Scene view.
- A domain reload (any recompile) clears the Undo stack and closes tile subscenes.
- Sculpt only ever writes `handHeightDelta`. An edge sample exists in 2 (corner: 4) tiles and every copy must hold the same value; `TerrainHeightBrush` computes it once and writes all copies (test: `TerrainHeightBrushTests`).
- `ConvertToTileScene` closes the new scene with `removeScene`, which destroys the tile GameObject in the editor session; reopen with `SubSceneUtility.EditScene(subScene)`.
- A hand-built `Unity.Physics.PhysicsWorld` needs `RigidBody.Scale = 1` or every ray misses.
- DXT1/DXT5 break the SDF (1.3 m error); the SDF import format is BC7 (P3-0).
- Full SDF encode is 236 ms at 512 px (62 ms at 256), so live painting needs the dirty-rect encode (P3-1).
- Nothing has been done in the Scene view with the mouse yet (brush overlay and sculptor are compile + logic checked only); drive them once by hand.
- Burst can keep serving a stale compiled copy of an `ISystem` after you revert a mutation (same source text, old code): a forced refresh does not fix it. Edit something that changes the IL (reorder two independent assignments) and recompile; `EnableBurstCompilation = false` via `execute_code` shows the source's true behaviour.
