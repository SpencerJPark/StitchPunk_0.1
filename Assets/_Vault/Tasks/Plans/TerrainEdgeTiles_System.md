---
status: SPEC 2026-11-25, not started. Phase T (terraces) waits for Terraces round 4 to be accepted and merged.
reference: Tasks/Captures/References/edgesSample.png (owner's tile sheet: turf fill, transparent outside, ink flicks along the edge)
captures: Tasks/Captures/EdgeTiles_<date>/NN_<what_it_shows>.png
guide: Docs/Terrain/TerrainEdgeTileGuide.md (how to draw a sheet, where pieces line up, how to add a biome pair)
templates: Docs/Terrain/EdgeTile_Template_5Pieces.png, Docs/Terrain/EdgeTile_Template_16Case.png
extends: Tasks/Verification/Terrain_System.md (tile data, compositor, palette), Tasks/Verification/InkLook_System.md (R3 tile transitions experiment, replaced by this)
names verified against: main at c4f0ba31 (2026-11-25). If a name has drifted, grep and note it in the log.
---

# Terrain Edge Tiles: biome borders drawn from a tile set

## 0. What the owner wants

Cleaner biome edges and better-looking generated transitions. Edges come from a hand-drawn tile set like
`References/edgesSample.png`: the world sits on a grid, each grid tile shows part top texture and part bottom
texture, and the edge art (silhouette + ink flicks) is drawn into the tile, so it never stretches along a curve.
Every biome pair can have its own edge ("grass meeting stone" is not "grass meeting dirt"). There must be a guide
so the owner can draw more sets and they always line up.

## 1. Owner answers (2026-11-25, do not re-ask)

- **Scheme: 16-case corner ("dual grid").** Biome is stored at grid corners; each drawn tile sits between 4 corners
  and picks one of 16 cases. Five drawn pieces per edge set (outer corner, edge, inner corner, diagonal, full);
  the packer rotates them into all 16 cases. Any single case can be hand-overridden. (The reference sheet is the
  47-tile blob scheme; it is a look reference, not the format.)
- **Cell size: 2 m.** 50 cells per 100 m world tile, 51 x 51 corners. Each cell is 2 x 2 terrace cells.
- **Terraces: Phase T, specced now, built after Terraces r4 lands.** Phase 1-5 touch biome edges only.

## 2. How it works (one picture)

```
 corner data (biome id per corner, 2 m apart)        one drawn tile per cell, case from its 4 corners
   G-----G-----D                                       bits: 1 = top-left, 2 = top-right,
   |  15 |  6  |    G grass, D dirt                          4 = bottom-right, 8 = bottom-left; top = +Z (north)
   G-----G-----D    cell (0,0): all G  -> case 15 (full grass, no edge art)
   |  3  |  2  |    cell (1,0): G G / D G -> TL+TR+BR... read each cell's own 4 corners
   D-----D-----D    cell (0,1): G G / D D -> case 3 (edge piece, grass on top half)
```

Layering per cell (at most 4 distinct biomes in a cell):
1. Sort the cell's distinct biomes by `drawOrder` (low first). Paint the lowest one's fill.
2. For each higher biome U in order: a corner is "inside U" when its biome's drawOrder >= U's (higher biomes
   continue under each other, so a 3-way junction never leaves a hole). L = the highest biome below U in this
   cell. Edge set = pair table [U over L], else U's default set. Case = inside bits. Variant = hash(world cell, U).
3. Sample that set's coverage + detail for (case, variant): `col = lerp(col, fill(U), coverage)`, then
   `col = lerp(col, detail.rgb, detail.a)`.

## 3. Decisions (ET-D, final unless the owner overrides)

- **ET-D1 The corner grid is a compositor OUTPUT, not a new paint source.** `TerrainCompositor` already builds the
  composite layer texels (base mask, paint overlay, water, road). It now also writes `edgeCornerIds` by majority vote
  over the composite texels within +-1 m of each corner (ties go to the higher drawOrder). Every existing tool
  (generate, area recipes, paint brush, water) keeps working with no change, and the composite SDF, nav cost,
  footsteps, decals and the strategic stand-in still read the composite as they do today.
- **ET-D2 Storage.** `TerrainTileData.edgeCornerIds` (`byte[51 * 51]`, index `z * 51 + x`, corner 0 = tile min corner,
  value = biome id, 255 = unset) and `edgeCornerIdTexture` (sub-asset, R8, 51 x 51, point filter, clamp, no mips,
  uncompressed). Biome id = `TerrainSplatLayer` value for now (Dirt 0, Grass 1, Forest 2, Stone 3); ids up to 15 are
  reserved so more biomes need no format change.
- **ET-D3 Seams: both copies always equal.** A seam corner exists in 2 tiles (corner: 4). When a tile composites,
  it writes its seam corners into every neighbour's array too (open tile: in memory + texture re-upload; closed
  tile: its data asset, as the height weld already does). Last writer wins, both copies match after every write.
  The shader only ever reads its own tile's texture.
- **ET-D4 Cleanup pass on the corner grid** (in the compositor, after the vote, `TerrainEdgeCornerCleanup`, pure,
  Utils): removes islands smaller than `minIslandCorners` (default 3), fills holes smaller than `minHoleCorners`
  (default 3), and applies adjacency rules (ET-D7). It only changes corners whose winning texels came from the
  procedural base mask; painted corners (overlay alpha > 0) are never cleaned, so a painted dot stays a dot.
  This pass is what stops noise thresholds turning into speckle and rings, the main way tile edges look bad.
  **Generation must sample rotated, domain-warped noise** (mock-up finding): noise upsampled on a grid aligned with
  the corner grid gives long straight axis-aligned runs that no edge art can hide. Warped noise gives diagonal
  staircases, which the rounded pieces turn into organic outlines. Feature size about 4-6 corners (8-12 m).
- **ET-D5 Edge set asset: `TerrainEdgeTileSet` (Data SO).** Fields: `List<TerrainEdgeTileVariant> variants`
  (each: `Texture2D source`, `TerrainEdgeTileLayout layout` = FivePieces | SixteenCases), `Color fillKeyColour`
  (alpha 0 = auto: median colour of the full piece), `float detailKeyTolerance` (default 0.12). Sources are
  authored at 256 px per cell. Layouts match the two templates in `Docs/Terrain/`. Menu `Create > Stitch Punk >
  Terrain > Edge Tile Set`; sources live in `Assets/PaintedGround/Textures/EdgeTiles/`, named
  `T_Edge_<Top>Over<Bottom>_v<n>.png`.
- **ET-D6 Pair table: `TerrainBiomeEdgeTable` (Data SO).** `List<TerrainEdgeBiome> biomes` (id, displayName,
  drawOrder, defaultEdgeSet), `List<TerrainEdgePairOverride> pairOverrides` (upperId, lowerId, edgeSet),
  `List<TerrainEdgeAdjacencyRule> adjacency` (biomeA, biomeB, bufferBiomeId, bufferWidthCorners default 1),
  `minIslandCorners`, `minHoleCorners`. Default drawOrder keeps today's paint order: Dirt 0, Grass 1, Forest 2,
  Stone 3. `TerrainPalette` gains one field `TerrainBiomeEdgeTable edgeTable` (added after Terraces r4 merges, see
  section 6); nothing else in the palette changes.
- **ET-D7 Adjacency rules make transitions believable.** "Forest never touches Stone directly; put 1 corner of
  Grass between" is a table row, applied by the cleanup pass to procedural corners. This is the generation-side
  half of "specific biomes meet each other in a specific way"; the pair override is the art-side half.
- **ET-D8 Packer: `TerrainEdgeTilePacker` (Editor).** Menu `Stitch Punk/Terrain/Edge Tiles/Pack Edge Tiles` and a
  Pack button on the table's inspector. Reads every set in the table and writes to
  `Assets/PaintedGround/Generated/EdgeTiles/`:
  - `EdgeCoverage.asset`: Texture2DArray, R8/BC4, one slice per (set, variant, piece) = source alpha.
  - `EdgeDetail.asset`: Texture2DArray, RGBA/BC7, same slices: pixels whose colour is further than
    `detailKeyTolerance` from `fillKeyColour` keep their drawn colour (ink flicks, rims, wet edges), alpha = that
    difference smoothed; pixels close to the key become alpha 0 so the world-tiled fill shows through. This is
    why a case-15 cell never shows a square: the inside of every piece is the real fill, not the drawn fill.
    Coverage = alpha of the non-ink pixels; where ink sits inside the silhouette (3 of 4 neighbours at 3 px are
    covered), coverage is closed underneath it. Flicks that hang OUTSIDE the silhouette stay detail only, so they
    lie over the lower biome without dragging fill with them (proven in the mock-up).
  - `EdgeCaseLookup` (RGBA8, 16 x (sets x 4) texels): per (set, variant, case): slice index lo/hi, rotation
    0-3. FivePieces maps case to (piece, rotation) by the table in the guide; SixteenCases maps each case to its
    own slice, rotation 0. Variants are padded to 4 by repeating.
  - `EdgePairSlice` (R8, 16 x 16): (upper, lower) -> set index.
  One slice per piece (not one atlas cell per case), so mips never bleed between cases. Import max size per
  platform from `TerrainPlatformProfile` (Switch: 128).
  The packer also validates every source against the guide's rules (crossing points, solid sides, safe margin) and
  reports the failing piece and side by name. A failed check still packs, with a console warning.
- **ET-D9 Shader.** New local keyword `_GROUND_EDGE_TILES` on `Painted/Ground`, branch beside the existing
  `_GROUND_TRANSITION_TILES` one, same output contract as `PaintedFringe_float` (col, ink, rim). New function
  `PaintedEdgeTiles_float` in `PaintedGroundCore.hlsl`. Corner ids via `Load` (integer, no filtering). Arrays
  sampled with `SampleGrad` from the continuous cell coordinate (the R3 trick: frac() jumps would pick the
  smallest mip in a one-pixel line); rotation applied to the cell-local uv before sampling. Cells whose 4 corners
  are one biome sample nothing (one fill tap). Without `_SPLAT_WORLDTEXTURE` it falls back to the fringe.
  Per-tile material properties: `_EdgeCornerIds`, `_EdgeCellMetres` (2), plus the four packed globals set by
  `TerrainLightingBridge` (`_EdgeCoverage`, `_EdgeDetail`, `_EdgeCaseLookup`, `_EdgePairSlice`), so the strategic
  stand-in copies pick them up with no rebake.
- **ET-D10 Old experiment.** `_GROUND_TRANSITION_TILES` + `T_TransitionAtlas.png` stay until E1-E9 pass, then are
  deleted in one commit (InkLook D4 closes as "replaced by Edge Tiles"). The fringe stays as the keyword-off path.
- **ET-D11 Snap brush (Phase 5).** `TerrainTileData.handEdgeCornerIds` (`byte[51*51]`, 255 = no override, null =
  none). Final corner = hand if set, else derived. Brush mode "Tile" in the Paint panel: Scene-view stroke paints
  whole corners of the selected biome, with the grid and the cell cases previewed under the cursor live; Shift
  erases the override. Writes all copies of seam corners (as `TerrainHeightBrush` does). The soft paint brush is
  unchanged.
- **ET-D12 What does not change.** Nav, footsteps, decals, scatter and roads read the composite as today. A visual
  edge can sit up to 1 m from the nav edge; that is accepted. Roads stay ribbons on top.

## 4. Pinned surfaces (workers do not change these signatures)

```csharp
// Data/Terrain/TerrainEdgeTiles.cs
public enum TerrainEdgeTileLayout { FivePieces, SixteenCases }
[Serializable] public struct TerrainEdgeTileVariant { public Texture2D source; public TerrainEdgeTileLayout layout; }
[Serializable] public struct TerrainEdgeBiome { public byte biomeId; public string displayName; public int drawOrder; public TerrainEdgeTileSet defaultEdgeSet; }
[Serializable] public struct TerrainEdgePairOverride { public byte upperBiomeId; public byte lowerBiomeId; public TerrainEdgeTileSet edgeSet; }
[Serializable] public struct TerrainEdgeAdjacencyRule { public byte biomeA; public byte biomeB; public byte bufferBiomeId; public int bufferWidthCorners; }
// Data/Terrain/TerrainEdgeTileSet.cs, TerrainBiomeEdgeTable.cs  (ScriptableObjects, fields per ET-D5 / ET-D6)

// TerrainTileData additions
public const int EdgeCornersPerSide = 51;          // 2 m cells, 50 per tile
public const float EdgeCellMetres = 2f;
public const byte NoEdgeCornerId = 255;
public byte[] edgeCornerIds;                       // derived, written by the compositor
public byte[] handEdgeCornerIds;                   // Phase 5
public Texture2D edgeCornerIdTexture;

// Utils (pure, unit-testable)
public static class TerrainEdgeCornerVote   { public static void VoteCorners(Color32[] compositeTexels, int resolution, byte[] outCornerIds, bool[] outCornerIsPainted); }
public static class TerrainEdgeCornerCleanup{ public static void Clean(byte[] cornerIds, bool[] cornerIsPainted, TerrainBiomeEdgeTable table); }
public static class TerrainEdgeCases        { public static int CaseFor(byte topLeft, byte topRight, byte bottomRight, byte bottomLeft, byte upperBiome, TerrainBiomeEdgeTable table);
                                              public static void PieceAndRotationFor(int caseIndex, out int pieceIndex, out int clockwiseQuarterTurns); }
```

`TerrainEdgeCases` is the C# mirror of the shader's case logic; the tests and the packer validation use it.

## 5. Phases and tasks

| Phase | Task | Files | Check |
|---|---|---|---|
| **P0 spike** | Hand-draw (or generate) one Grass-over-Dirt FivePieces sheet; hardcode corner ids from tile 0_0's composite in a probe; render with a throwaway branch of the shader. Capture next to the fringe and the old R3 path. Owner looks before P1. | probe + shader branch | captures 01-04 |
| **P1 data** | ET-D2 fields, `TerrainEdgeCornerVote`, seam write (ET-D3), call from `TerrainCompositor.Composite` | `TerrainTileData.cs`, `TerrainCompositor.cs`, new Utils file | EditMode: vote ties, seam copies equal after compositing either side |
| **P2 sets + packer** | ET-D5, ET-D6, ET-D8 (with validation) + `TerrainEdgeTilesTemplateWriter` menu that writes the two template PNGs | new Data + Editor files | EditMode: case -> piece/rotation table (16 rows), validation catches a crossing off the midpoint |
| **P3 shader** | ET-D9, bridge globals | `PaintedGroundCore.hlsl`, `PaintedGround.shader`, `TerrainLightingBridge.cs` | variant compiles (`ShaderUtil.CompilePass`), captures E1-E4 |
| **P4 generation** | `TerrainEdgeCornerCleanup` + adjacency (ET-D4, ET-D7); default table asset with Forest/Stone buffered by Grass | new Utils file, compositor call | EditMode: island removed, painted island kept, forbidden pair gets a buffer; capture E7 on a generated 3 x 3 |
| **P5 snap brush** | ET-D11 | Paint panel + brush | drive by hand once; capture of the live grid preview |
| **P6 zoom + perf** | Strategic handover check; ms cost vs fringe on the gameplay camera | none expected | E6, E9 |
| **Phase T** | see section 8 | | |

P1 and P2 are parallel-safe. P3 needs both. P4 needs P1. P5 needs P3.

## 6. Shared files and other sessions

- Terraces r4 is live on another session and owns `TerrainPalette.cs`, `PaintedCliff.shader`, the Terraces
  folders and `TerrainArtSwapGuide.md`. This spec adds ONE field to `TerrainPalette` (`edgeTable`) and must do it
  after r4 merges, or in a worktree merged after it. Everything else here is new files.
- `PaintedGround.shader` / `PaintedGroundCore.hlsl`: r4 TX-D19 only changes material values, not code. Still,
  ListAgents before P3 and rebase on main.
- `TerrainCompositor.cs` is also called by the paint brush: keep the 6-argument `CompositeTexels` overload working.

## 7. Acceptance (every item passes in a capture; judge against `edgesSample.png`, not this text)

- E1 A Grass/Dirt border at gameplay camera (FOV 15) and close-up reads as drawn turf edge with ink flicks; no
  square visible where a full cell meets an edge cell.
- E2 Tile seam 0_0 to -1_0: the edge continues across with no step, gap or doubled piece.
- E3 A 3-biome junction (Dirt, Grass, Stone in one cell): no hole, no hard square, each upper biome has its edge.
- E4 Pair override shows: Grass over Stone uses a visibly different set from Grass over Dirt.
- E5 A 40 m straight border uses at least 2 variants; no obvious repeat at gameplay zoom.
- E6 Zoomed out: no grid lines, no grey mip smear, and no ghost shapes at the strategic dither handover.
- E7 A generated 3 x 3 tile area: no single-corner speckle, no ring shapes from noise, Forest never touches Stone.
- E8 Swap test: the owner (or orchestrator) draws or edits one piece PNG, presses Pack, and the scene changes as
  the guide says it will.
- E9 Ground pass cost with `_GROUND_EDGE_TILES` on is within 0.3 ms of the fringe at 1080p on the dev machine.

## 8. Phase T: terraces on the same grid (spec now, build after r4)

Goal: a cliff's grass lip is the same art as a biome edge, and cliff outlines get the same clean drawn look.
Recommendation (to confirm with a spike):
- Terrace level moves to the same dual grid as a corner value (`terraceCornerLevels`, derived from the 1 m levels
  by vote, as ET-D1). The outline is no longer a smoothed SDF contour (TX-D2) but the case pieces.
- Walls become a **modular kit per case piece** (straight, outer corner, inner corner, diagonal), one mesh per
  piece per level height, authored or generated with the TX-D11 profile (round top, steep face, round foot). The
  tile mesh places pieces by case instead of marching-squares cutting (TX-D3).
- The lip uses the upper biome's edge set at the rim, the foot uses the lower biome's (replaces TX-D17 strips).
- Ramps stay objects (TX-D6) and occupy whole cells.

Mock-up findings (2026-11-25, `Tools/terrain_edge_mock/`, captures 08-10):
- Lip and foot are two more 16-case sets on the cliff grid, drawn like biome edges but pushed outward by a fixed
  offset (lip +11 px over the rim, foot +12 px into the wall base, using the complement case). An offset set
  crosses each side at 128 + offset, consistently, so its pieces still tile; validation takes the offset into account.
- The cliff face reads as rock only with large irregular slabs stretched vertically (Voronoi, about 0.5 x 0.9 m),
  each lit on its upper face, plus a jagged light cap for the top bevel. Vertical columns read as a wooden fence.
- Top a few percent lighter than the ground below, a soft shadow south of the foot, and an ink line round the
  plateau top are what make the step readable from the gameplay angle.

Open for the Phase T spike (defaults apply if the owner does not answer):
- T-Q1 **Answered 2026-11-27: 2 m, the same grid as biome edges.** Owner: "the top edges and bottom edges need to align
  with normal tile transition... it should be the same tiling", even if the edge shapes change.
- T-Q2 Kit pieces hand-modelled or generated from the TX-D11 profile. Default: generated, swappable per piece.

### 8.1 Build plan (2026-11-27, started now; Terraces session is stopped, owner directs this)

Audit of today's pipeline: levels are 1 m cells (`baseTerraceLevels`/`handTerraceLevels`, -3..+3);
`TerraceLevelField.BuildSignedDistance` upsamples to 4/m, rounds by open/close at `terraceCornerRadiusMetres` and blurs;
`TerraceTileMeshBuilder` marches 0.25 m subcells, links `TerraceOutlineChain`s, and everything downstream (wall profile
strips, `TerraceDressingBuilder` lip/foot, nav wall mask via `LastBuildOutlineChains`) follows those chains.

Least invasive path, keeping the whole wall/chain/dressing/nav pipeline and replacing only the contoured field:
- **PT-D1 Shared integer variant hash.** CPU and GPU must pick the same variant per cell, so the shader's
  `frac(sin(...))` hash becomes an integer hash in both (`TerrainEdgeTileCases.VariantFor(cellX, cellZ, seed)` in C#,
  the same in `PaintedTransitionTile_float`):
  `h = (uint)(cellX * 73856093) ^ (uint)(cellZ * 19349663) ^ (uint)(seed * 83492791); h ^= h >> 13; h *= 0x5bd1e995u;
  h ^= h >> 15; variant = h & 1`. Seeds: R 0, G 17, B 31, plateau 47.
- **PT-D2 Plateau outline = edge-tile silhouette.** For each level threshold, corner levels on the 2 m dual grid
  (corner (i,j) at world `tileMin + 2i, 2j`; corner level = the higher of the majority of the four 1 m cells touching
  it, ties up; cells outside the tile read the neighbour tile's data when loaded, else the generator, else clamp). The
  case per 2 m cell picks the atlas cell (same indexing as `PaintedTransitionTile_float`: column c % 8, row c / 8 + 2v,
  row 0 at the PNG top, cell-local +Z up), and the signed field at each 0.25 m sample = (coverage - 0.5) scaled to
  metres, no open/close/blur. Marching squares, chains, walls, dressing and nav run unchanged on it.
- **PT-D3 Silhouette source.** The plateau uses the grass edge atlas (`T_EdgeAtlas_GrassOverDirt`, read on the CPU from
  the PNG file, not the import, so it needs no Read/Write flag) with seed 47 until a dedicated plateau set exists.
- **PT-D4 Lip and foot use the tile art.** The ground shader learns the plateau case: a per-tile `_TerraceCornerLevels`
  texture (R8, corner grid, point sampled) and the vertex's own level in uv0.z. On the top surface it draws the atlas ink
  (outline + flicks) of the plateau case at the rim; at the foot it draws the lower edge of the same case. The
  procedural lip blades and the black foot strip (`TerraceDressingBuilder`, `PaintedCliff` modes 1-2) are turned off
  once PT-D4 reads right.

| Phase | Work | Files |
|---|---|---|
| PT1 | PT-D1 hash in the shader + C# helper | `PaintedGroundCore.hlsl`; `Utils/Terrain/TerrainEdgeTileCases.cs` (new) |
| PT2 | PT-D2/D3 field from the silhouette | `TerraceLevelField.cs`, `TerraceTileMeshBuilder.cs`, new `Terraces/TerraceEdgeTileOutline.cs` |
| PT3 | PT-D4 lip/foot ink in the ground shader, dressing off | `PaintedGround.shader`, tile assets writer, `TerraceDressingBuilder` |

Acceptance: a plateau rim and a grass/dirt border next to it show the same lobes and crossing points at the same 2 m
cells (capture from the gameplay angle); no gaps where a wall meets a tile seam; nav wall mask follows the new outline.

## 9. Open (defaults apply)

- O1 Variant count per set: default 2, max 4.
- O2 Detail ink colour: drawn colour (default) or forced to the global `_StitchInkColor`. Default drawn.
- O3 More than 4 biomes needs a fill Texture2DArray instead of the four layer slots; out of scope until a 5th
  biome exists.

## 10. Log (one line per wave, newest last)

- 2026-11-25 Offline mock-up (`Tools/terrain_edge_mock/edge_tile_mock.py`, numpy + Pillow): 4 example sets in
  `Docs/Terrain/EdgeTileExamples/`, all pass section-3 validation; biome map + cliff captures in
  `Tasks/Captures/EdgeTiles_2026-11-25/`. Not in Unity; P0 spike still owed.
- 2026-11-25 P0 shortcut: the existing ink-look path (`PaintedTransitionTile_float`, `_StitchTransitionAtlas`, 8x4 cells of
  128 px, R coverage / G rim / B ink, corners = splat channel > 0.5) already IS a 16-case dual grid, so the spike needs no
  shader edit. `Tools/terrain_edge_mock/export_transition_atlas.py` packs GrassOverDirt v0/v1 into
  `Assets/PaintedGround/Textures/EdgeTiles/T_EdgeSpike_GrassOverDirt_Atlas.png` (import settings copied from
  T_TransitionAtlas). To look: on `TerrainWorld` > Atmosphere > TerrainLightingBridge set Ink Look on, Transition Atlas =
  the spike atlas, Transition Cell Metres = 2. Capture blocked: an off-screen `Camera.Render` (new camera or the Scene
  view camera) in Edit mode draws no ground at all, so owner looks in the Scene view by hand. Limits: per splat channel,
  not biome draw order; layer colour replaces the fill; only ink (B) kept from the art.
- 2026-11-26 live on the normal look: global keyword `_GROUND_EDGE_TILES` (PaintedGround.shader, forward pass, takes
  priority over STITCH_INK_LOOK's transition branch) with one atlas per splat layer, `_GroundEdgeTileAtlasR/G/B` +
  `_GroundEdgeTileCellMetres`, pushed by TerrainLightingBridge (`edgeTilesEnabled`, `grass/forest/stoneEdgeTileAtlas`,
  missing layers borrow grass). On in TerrainWorld at 2 m: R GrassOverDirt, G ForestOverGrass, B StoneOverDirt
  (`T_EdgeAtlas_<Set>.png`; `export_transition_atlas.py [Set ...]`). Layers map to draw order already (palette: base
  Dirt, R Grass, G Forest, B Stone). Not yet seen in the Editor. Known gap: ForestOverGrass ink is near zero (mean B
  0.0007): checked, its example art has no detail pixels at all past the 0.12 fill tolerance, so the fix is in the art
  (darker needle flicks), not the exporter.
  Pair overrides (StoneOverGrass) need the lower biome per cell: still P3 proper.
- 2026-11-25 Spec written. Owner chose 16-case corner, 2 m cells, Phase T after r4. Templates generated by a
  scratch script into `Docs/Terrain/` (P2 replaces it with an in-Editor menu).
- 2026-11-26 generated world: `TerrainGenerationProfile.biomeNoiseWarpMetres / WarpFrequency / RotationDegrees` (0 = old
  behaviour) in `TerrainProceduralGenerator.BiomeLayerAt`; Create Random World sets 11 / 0.035 / 27 and
  `region.paletteOverride = TP_EdgeMock` (mock-up fills, `export_ground_fills.py`, 8 m tiles). Builders now copy
  TerrainWorld (CampLoop deleted). Captures 13-15. Verdict: biome shapes organic, but forest borders read as stepped
  octagons (art wobble too small for 2 m cells) and the ink barely shows; terrace walls are still the Terraces look.
  Note: open RandomWorld fresh and the keyword is off until the bridge's Update runs (move the Scene view once).
