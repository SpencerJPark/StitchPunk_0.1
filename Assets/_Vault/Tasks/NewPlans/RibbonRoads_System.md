---
status: spec, not built; owner asked for it 2026-11-06 (wants ruts, wear and richer detail, not only sharper edges); open questions below carry defaults
parent: Terrain_System.md P10-5 (Tasks/Verification/Terrain_System.md)
---

# Ribbon Roads: a draped mesh road with ruts, wear and detail

## 0. Why and what stays the same
Roads today are painted into each tile's splat mask and signed-distance field (about 0.39 m per texel at 256 px), plus decals. Edge crispness is capped by mask resolution and there is no place for per-road detail. A ribbon is a real mesh strip laid on the ground along the road spline, drawn with its own shader, so detail is limited only by the texture and mesh, not the mask.

Unchanged: the `TerrainRoad` spline authoring, the height carve, the splat road mask (stays underneath as the soft fringe), the road graph and nav surface speed (all read the spline / mask, never the ribbon), `TerrainCrossing` markers, tile streaming. The ribbon is visual only.

## 1. Architecture in one page
- **Source:** the same `TerrainRoad` splines. A ribbon is baked per tile and lives in that tile's SubScene, so it streams with the tile.
- **Mesh:** a strip with a cross-section of 7 vertices (left fade, left edge, left rut, centre, right rut, right edge, right fade), a ring every 1 m along the spline (denser where the curve bends past 5 degrees). Heights come from the tile's final composited heights (`TerrainTileData.SampleBilinear`) plus a small lift. The strip is cut at tile borders so each piece belongs to exactly one tile.
- **Vertex data:** UV.x across the road 0..1; UV.y distance along the road in metres (so textures tile at a fixed world scale); vertex colour R = edge wear, G = rut depth, B = shoulder fade, A = reserved.
- **Shader `Painted/Ribbon`:** URP unlit, reuses `PaintedLighting_float` from `PaintedGroundCore.hlsl` (so ambient tint and lightning flash match the ground). Draws a tinted surface texture, darkens the two ruts, breaks the edge with a noise-driven wear mask, optional puddle tint.
- **Style data:** `TerrainRoadRibbonStyle` ScriptableObject referenced from `TerrainRoadProfile` (a null style means no ribbon, painted road only).
- **Sits on the ground:** opaque, writes depth, small slope-scaled depth offset so it wins the z-fight with the ground; edge fade uses alpha-to-coverage or a 1 m fade-in strip so there is no hard line. Decals (footprints, scatter) still project onto it.

## 2. Decisions (RB-D1 onward; defaults apply unless the owner says otherwise)
- **RB-D1 The painted road mask stays as an underlay** and the ribbon is 0.4 m narrower than the painted width each side, so the painted dirt shows as a natural fringe and a missing ribbon still looks like a road. *Why:* no change to nav, SDF or carve, and a safe fallback.
- **RB-D2 One mesh per road per tile**, not per spline segment. Baked into the tile folder as a mesh asset (`Ribbon_<road>.asset`) with a `MeshFilter`/`MeshRenderer` under the tile root.
- **RB-D3 Ribbons never touch heights.** The carve already shaped the bed; the ribbon only drapes.
- **RB-D4 Crossings:** a `Bridge` footprint cuts the ribbon (the hand-placed bridge model carries the road); a `Ford` keeps the ribbon with a wet tint and ruts faded out.
- **RB-D5 Junctions:** overlapping ribbons resolve by `TerrainRoad.priority`; the lower-priority ribbon fades its centre vertices to alpha 0 inside a 3 m radius of the junction node (a small blend, no boolean mesh ops).
- **RB-D6 Budget:** at most 4,000 ribbon vertices per tile, shown by `TerrainBudgetCheck` and counted in the `Report Data Size` meshes column.
- **RB-D7 Detail look (default):** two wheel ruts darkened toward the road tint, grass tufts and stones as edge wear (not decals), puddle tint off by default and driven by a global `_TerrainWetness` later if a weather system exists. Detail is texture + vertex colour, not geometry.
- **RB-D8 Authoring workflow:** rebake follows `TerrainRoadEvents` like the painted road; live preview while dragging a knot (ribbon mesh updated in memory), disk write on mouse-up.

## 3. Open questions
- **RB-Q1 Ruts only, or a worn centre strip too?** Default: two ruts plus a lighter centre ridge.
- **RB-Q2 Edge wear: textured fade, or scattered 3D stones/tufts along the edge?** Default: textured fade; scatter stays a separate P7 rule.
- **RB-Q3 Puddles/wetness:** wanted now, or only once weather exists? Default: shader supports it, no driver.
- **RB-Q4 Different road classes (dirt track vs paved)?** Default: yes, one `TerrainRoadRibbonStyle` asset per class.
- **RB-Q5 Camera zoom:** the miniature/strategic view (P11) draws roads from the road graph, not the ribbon. Default: leave as is.

## 4. Spikes (orchestrator, Unity MCP)
| ID | Question | Answer by |
|---|---|---|
| RB-S1 | Depth offset vs ground at the real 2.5D camera angle: does a 0.02 m lift plus a slope-scaled bias remove the z-fight on slopes up to 30 degrees without the ribbon floating at the grazing angle? | Place a test strip on the camp tile, orbit the game camera |
| RB-S2 | Does a custom unlit shader with `PaintedLighting_float` bake and render through Entities Graphics in a tile SubScene (DOTS-instanced properties for tint)? Copy the exact pattern from `PaintedGround.shader` (`UNITY_DOTS_INSTANCED_PROP`). | Bake a one-quad subscene |
| RB-S3 | Does the soft edge look right: alpha-to-coverage vs a 1 m alpha fade strip (the project uses MSAA?) | Compare both on the test strip |

## 5. Phases and tasks
All names final. Verify real Unity API signatures in `Library/PackageCache` before calling. Workers never use the Unity MCP; one worker per row.

### R1. Data
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| R1-1 | `Data/Terrain/TerrainRoadRibbonStyle.cs` | SO: `Material material`, `float ribbonInsetMetres = 0.4f`, `float rutSpacingMetres`, `float rutWidthMetres`, `float rutDarkness`, `float edgeWearWidthMetres`, `float edgeWearNoiseScale`, `float textureMetresPerTile`, `float verticalLiftMetres = 0.02f`, `float ringSpacingMetres = 1f`, `bool fadeRutsOnFord` | `StitchPunk.Data` | A |
| R1-2 | `Data/Terrain/TerrainRoadProfile.cs` | add `TerrainRoadRibbonStyle ribbonStyle` (null = none) | `StitchPunk.Data` | A |

### R2. Mesh builder (pure)
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| R2-1 | `Editor/Terrain/Roads/TerrainRoadRibbonMeshBuilder.cs` | Pure static: `Build(IReadOnlyList<float3> centreline, ..., Func<float,float,float> heightAt, TerrainRoadRibbonStyle style, Rect tileWorldRect, out Mesh or arrays)`: rings every `ringSpacingMetres`, 7 vertices per ring, heights from `heightAt` + lift, clipped to `tileWorldRect` (cut the strip exactly at the border so neighbours meet at the same vertices), vertex colour and UV as in section 1, returns triangle and vertex counts | `StitchPunk.Editor` | B |
| R2-2 | `Tests/Terrain/TerrainRoadRibbonMeshBuilderTests.cs` | EditMode: a straight road gives symmetric cross-sections; a strip across a tile border produces identical border vertices on both tiles; a closed-loop spline does not duplicate the seam ring; counts stay under the vertex budget for a 100 m road | touched fixture | B |

### R3. Shader
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| R3-1 | `Assets/PaintedGround/Shaders/PaintedRibbon.shader` | Use the `shader-edit` skill. Unlit URP, includes `PaintedGroundCore.hlsl`, lighting through `PaintedLighting_float`, ruts and edge wear from vertex colour and a noise texture, surface tint and optional puddle tint as DOTS-instanced properties following `PaintedGround.shader`, depth offset, soft edge per RB-S3 | none (spike checked) | C |

### R4. Baker and panel
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| R4-1 | `Editor/Terrain/Roads/TerrainRoadRibbonBaker.cs` | On `TerrainRoadEvents` (and from the panel's rebake): for each tile overlapped by the road, build the ribbon mesh, write `Ribbon_<road>.asset` into the tile folder, keep one `MeshFilter`/`MeshRenderer` child per road under the tile root, remove stale ones; apply RB-D4 (cut on bridges, wet tint on fords) and RB-D5 (junction fade). Live in-memory preview while a knot drags, disk write on mouse-up | `StitchPunk.Editor` | - |
| R4-2 | `Editor/Terrain/Window/TerrainRoadsPanel.cs` | Style picker on the selected road's profile, a "Rebake ribbons" button, a ribbon vertex count per tile (use `TerrainBudgetCheck`) | `StitchPunk.Editor` | - |
| R4-3 | `Editor/Terrain/TerrainBudgetCheck.cs`, `Editor/Terrain/TerrainDataReport.cs` | count ribbon vertices against RB-D6; add ribbon meshes to the meshes column | `StitchPunk.Editor` | D |

### R5. Content and verification
| ID | Files | Brief | Gate | Par |
|---|---|---|---|---|
| R5-1 | `_Vault/Tasks/Verification/verify-terrain.md` | add the ribbon owner passes: draw a road across a border, drag a knot, see the ruts follow live; cross a ford and a bridge; walk it in Play, check for z-fight shimmer at the camera angles you use; `Verify Seams` and `Report Data Size` still pass | none | - |
| R5-2 | `_Vault/Memories/Code/Terrain.md` | ribbon section: layering rule, RB-D decisions, traps found | none | - |

Checkpoint R (owner visual): one dirt road with ruts across a tile border and over a ford, in Play at your normal camera; judge edges, ruts, wear and shimmer.

## 6. Order of work
Spikes RB-S1 to RB-S3 first (they decide the shader and edge approach). R1 and R2 are independent and can run in parallel; R3 follows the spike; R4 needs R1 to R3; R5 last. Rough size: 8 worker tasks plus the spikes.

## 7. Known gaps by design (v1)
No geometry (3D stones, tufts) on the ribbon; no weather driver for puddles; no player-visible deformation (tracks are decals, a separate system); the strategic map keeps drawing roads from the road graph.
