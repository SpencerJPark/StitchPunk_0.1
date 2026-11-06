---
status: spec, not built, grounded in code 2026-11-06; owner wants ruts, edge wear and richer detail; open questions below carry defaults
parent: Terrain_System.md P10-5 (Tasks/Verification/Terrain_System.md)
---

# Ribbon Roads: a draped mesh road with ruts, wear and detail

## 0. Why and what stays the same
Roads are painted into each tile's splat mask and SDF (about 0.39 m per texel at 256 px). Edge crispness is capped by mask resolution and there is no place for per-road detail. A ribbon is a real mesh strip on the ground along the spline with its own shader.

Unchanged: `TerrainRoad` spline authoring, the height carve, the splat road mask (stays underneath as the fringe), road graph and nav speed (read the spline / mask, never the ribbon), `TerrainCrossing`, tile streaming. The ribbon is visual only.

## 1. Architecture and data layout
Precedent to copy: `TerrainWaterBaker.BakeTile` (Editor/Terrain/Water/TerrainWaterBaker.cs ~l.435-508) already puts per-tile child meshes under the tile root as sub-assets of the tile's `TerrainTileData` named `Water_<n>` (`AssetDatabase.AddObjectToAsset(mesh, tileData)`, `EditorUtility.CopySerialized` into an existing sub-asset by name, `Mesh.Clear()` on unused ones, `DestroyImmediate` on obsolete child objects). The ribbon baker mirrors it: root child `Ribbons`, one child `Ribbon_<road.name>` per road with `MeshFilter`+`MeshRenderer`, mesh sub-asset named `Ribbon_<road.name>` inside `TerrainTileAssets.TileDataPath(coordinate)`. The mesh must be a sub-asset (not a loose .asset) or the subscene cannot bake it (comment in `TerrainTileBuilder.Rebuild`).

**Coordinates.** Tile (cx,cz) covers world x,z in `[c*100-50, c*100+50)` (`TerrainTileData.WorldMinCorner`, `TerrainMeshBuilder` uses `localX = sample - 50`). Tile root position is `(c*100, GroundTopHeight - 0.5, c*100)` (`TerrainTileCreator.cs` l.53, `GroundTopHeight = -0.03f`). Ground surface world y = `SampleBilinear(x,z) + TerrainTileCreator.GroundTopHeight` (proof: `TerrainBrushOverlay.cs` l.79). Builder works in world space and the baker converts each vertex with `tile.transform.InverseTransformPoint`. RB-S4 confirms the y arithmetic.

**Ownership rule.** A ring belongs to the tile containing its centre point by half-open floor (`Mathf.FloorToInt((x + 50f) / 100f)`). Never use `TerrainTile.CoordinateContaining`: it uses `Mathf.RoundToInt`, which rounds exact tile borders (x = 50, 150) inconsistently. Where the strip crosses a border, each tile gets an extra ring exactly on the border, built from the same centre point and tangent, so both tiles hold identical border vertices. Rings overhang the tile sideways by up to 2.1 m; that is accepted. Heights of every vertex come from `heightAt` below, which picks the tile that owns that vertex's xz, so overhang vertices agree in both tiles.

**Centreline and ring spacing.** The baker samples each spline of the road at 0.5 m with `container.CalculateLength(splineIndex)` and `container.EvaluatePosition(splineIndex, t)` (world space; same calls as `TerrainRoadBaker.StampFromRoad`) and passes xz polylines. The builder resamples by arc length every `ringSpacingMetres` (default 1 m), starting at the spline start so rings are the same in every tile. No adaptive bend density in v1 (1 m is enough for a 4 m road; revisit only if the checkpoint shows faceting). `UV.y` is arc length from the spline start, so it is continuous across tiles.

**Cross-section: 7 vertices per ring.** Half ribbon width `h = profile.widthMetres * 0.5 - style.ribbonInsetMetres`, fade band `f = style.fadeBandMetres`. `right = (t.z, -t.x)` for tangent t (xz), so a road running +z has right = +x. Worked values use width 4, inset 0.4, fade 0.5, rut spacing 1.4 (`h` = 1.6).

| # | Name | Offset (m) | As fraction of width W=4 | UV.x (signed metres, + = right) | Colour R wear | G rut | B shoulder | A opacity |
|---|---|---|---|---|---|---|---|---|
| 0 | left fade | -(h+f) = -2.1 | -0.525 | -2.1 | 255 | 0 | 255 | 255 |
| 1 | left edge | -h = -1.6 | -0.40 | -1.6 | 255 | 0 | 0 | 255 |
| 2 | left rut | -rutSpacing/2 = -0.7 | -0.175 | -0.7 | 0 | 255 | 0 | 255 |
| 3 | centre | 0 | 0 | 0 | 0 | 0 | 0 | 255 |
| 4 | right rut | +0.7 | +0.175 | 0.7 | 0 | 255 | 0 | 255 |
| 5 | right edge | +h | +0.40 | 1.6 | 255 | 0 | 0 | 255 |
| 6 | right fade | +(h+f) | +0.525 | 2.1 | 255 | 0 | 255 | 255 |

`UV.y` = arc metres. Second UV channel (`mesh.SetUVs(1, ...)`): `x` = ford wetness 0..1 (1 inside a Ford footprint), `y` = rut fade multiplier (1 normally, 0 inside a Ford when `fadeRutsOnFord`). Vertex colour is `Color32`. Normal = `normalize(-dh/dx, 1, -dh/dz)` from `heightAt` central differences at 0.5 m (deterministic across tiles). Vertex y = `heightAt + GroundTopHeight + style.verticalLiftMetres`.

**Triangles.** Ring i has vertices `base = i*7 .. i*7+6`. For each ring pair (i, i+1) and quad j in 0..5: `a = i*7+j`, `b = a+1`, `c = a+7`, `d = a+8`; emit `(a, c, b)` and `(b, c, d)`. This is the same clockwise-from-above winding as `TerrainMeshBuilder` (`corner, corner+columns, corner+1`). Per ring pair: 12 triangles. A straight 80 m road = 81 rings, 567 vertices, 960 triangles. Closed loops add a duplicated seam ring (UV.y = total length) so the texture does not jump.

**Border clipping.** For each polyline segment crossing a tile rect edge, insert the exact crossing point (linear interpolation of xz and arc length) as a ring in both tiles; the strip for a tile is the contiguous run(s) of rings inside the half-open rect plus its border rings. Bridge cuts and a tile re-entry make multiple runs; no triangles span between runs.

**Crossings (RB-D4).** `crossingAt(x,z)` returns `TerrainRibbonCrossingState.None / Ford / Bridge` (baker implements it from `TerrainCrossing.ContainsXZ(x,z)` and `.kind`). A ring whose centre is in a Bridge is dropped (cut). A ring in a Ford keeps its vertices with uv2 = (1, ruts?0:1).

**Junction fade (RB-D5).** The baker passes `junctionPointsXZ`: knots of a lower-priority road whose xz lies within the higher-priority road's half width of its centreline (`TerrainRoad.priority`). For every ring within 3 m of a junction point all 7 vertices get `A = saturate(distance / 3)` (ring at the junction = 0, 1 m away = 0.333, 3 m or more = 1). The higher-priority ribbon is untouched.

**Fragment channel meaning.** R = edge wear weight (adds the worn colour, broken up by noise); G = rut weight (darkens toward the rut tint, times uv2.y); B = shoulder, outermost fade verts (1 = outside the road, coverage 0); A = opacity from junction fades. Coverage = `A * (1 - B)`; the edge softens by dithered clip (RB-S3). uv2.x = wetness.

## 2. Decisions (RB-D1 to RB-D8; defaults apply unless the owner says otherwise)
- **RB-D1** Painted road mask stays as an underlay; the ribbon is `ribbonInsetMetres` (0.4) narrower each side so painted dirt shows as a fringe and a missing ribbon still looks like a road.
- **RB-D2** One mesh per road per tile, stored as a sub-asset of the tile's `TerrainTileData` (not a loose `Ribbon_<road>.asset`; see Changes).
- **RB-D3** Ribbons never touch heights; the carve already shaped the bed.
- **RB-D4** Bridge cuts the ribbon; Ford keeps it with a wet tint and ruts faded.
- **RB-D5** Junctions resolve by `TerrainRoad.priority`; lower-priority ribbon fades out within 3 m of the junction.
- **RB-D6** At most 4,000 ribbon vertices per tile, shown by `TerrainBudgetCheck`.
- **RB-D7** Detail look: ruts, centre ridge, edge wear as texture plus vertex colour, no geometry; puddle tint supported, no driver.
- **RB-D8** Rebake follows the existing road path: `TerrainRoadEvents.RoadChanged` then `TerrainRoadBaker.TickDebounce` (0.25 s) then `BakeTiles`. No separate live-preview path (see Changes).

## 3. Open questions
- **RB-Q1** Ruts only, or a worn centre strip too? Default: two ruts plus a lighter centre ridge (`_RidgeLighten`).
- **RB-Q2** Edge wear: textured fade or scattered 3D stones/tufts? Default: textured fade; scatter stays a separate P7 rule.
- **RB-Q3** Puddles/wetness now, or once weather exists? Default: shader supports `_WetTint`, no driver.
- **RB-Q4** Road classes? Default: yes, one `TerrainRoadRibbonStyle` asset per class.
- **RB-Q5** Strategic map (P11) keeps drawing roads from the road graph, not the ribbon.

## 4. Spikes (orchestrator, Unity MCP)
| ID | Question | Answer by |
|---|---|---|
| RB-S1 | Does `Offset -1,-1` plus 0.02 m lift remove ribbon z-fight on slopes up to 30 degrees at the real 2.5D camera angle, without floating at grazing angles? | Test strip on the camp tile, orbit the game camera |
| RB-S2 | ANSWERED by reading `PaintedGround.shader`: the ground already renders through Entities Graphics with `#pragma multi_compile _ DOTS_INSTANCING_ON`, `#pragma multi_compile_instancing`, one `UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)` block declaring `_AmbientTint`, and `#define _AmbientTint UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _AmbientTint)` (l.127-135, 159-160); `PaintedLighting_float` is plain HLSL from `Include/PaintedGroundCore.hlsl`, so reusing it needs no extra setup. Remaining check is folded into Checkpoint R (the ribbon appears in a baked subscene). | n/a |
| RB-S3 | Edge softness: dithered `clip` on coverage vs alpha-to-coverage vs a pure alpha fade (does the URP asset use MSAA?). Shader ships dithered clip (`_EdgeDitherSoftness`). | Compare on the test strip |
| RB-S4 | Does ribbon vertex world y = `SampleBilinear + GroundTopHeight + lift` sit on the rendered ground mesh (tile root y is `GroundTopHeight - 0.5`, mesh y is `HeightAt + GroundTopHeight`; the arithmetic looks like it double-counts)? | `execute_code`: raycast down at one tile point, compare to the formula |

## 5. Phases and tasks
Names are final. Workers never use Unity MCP. Verify real Unity API signatures in `Library/PackageCache`. Gate = `python Tools/devloop/devloop.py gate <assembly>` before the report. One worker per row. Assemblies: Data = `StitchPunk.Data`, Editor = `StitchPunk.Editor`, Tests = `StitchPunk.Tests` (all under `Assets/_Scripts/*/`).

### R1. Data
| ID | Files | Brief | Gate | Marker |
|---|---|---|---|---|
| R1-1 | `Data/Terrain/TerrainRoadRibbonStyle.cs` (new), `Data/Terrain/TerrainRoadProfile.cs` | New `public class TerrainRoadRibbonStyle : ScriptableObject` with `[CreateAssetMenu(menuName = "Stitch Punk/Terrain/Road Ribbon Style")]` and fields: `Material material`, `float ribbonInsetMetres = 0.4f`, `float fadeBandMetres = 0.5f`, `float rutSpacingMetres = 1.4f`, `float rutWidthMetres = 0.5f`, `float rutDarkness = 0.5f`, `float edgeWearWidthMetres = 0.6f`, `float edgeWearNoiseScale = 0.35f`, `float textureMetresPerTile = 4f`, `float verticalLiftMetres = 0.02f`, `float ringSpacingMetres = 1f`, `bool fadeRutsOnFord = true`. Add `public TerrainRoadRibbonStyle ribbonStyle;` (null = painted road only) to `TerrainRoadProfile` (existing class, `widthMetres`, `HalfWidthMetres` already there). | `StitchPunk.Data` | [parallel-safe] |

### R2. Mesh builder (pure, no scene access)
| ID | Files | Brief | Gate | Marker |
|---|---|---|---|---|
| R2-1 | `Editor/Terrain/Roads/TerrainRoadRibbonMeshBuilder.cs` (new) | Public API (final): `public enum TerrainRibbonCrossingState { None, Ford, Bridge }`; `public struct TerrainRoadRibbonInput { IReadOnlyList<Vector2> centrelineXZ; bool closedLoop; float widthMetres; TerrainRoadRibbonStyle style; Rect tileRectXZ; Func<float,float,float> groundHeightAt; Func<float,float,TerrainRibbonCrossingState> crossingAt; IReadOnlyList<Vector2> junctionPointsXZ; }`; `public static class TerrainRoadRibbonMeshBuilder { public const int RingVertexCount = 7; public const int MaxVerticesPerTile = 4000; public static Mesh Build(in TerrainRoadRibbonInput input, Vector3 meshOriginWorld, out int vertexCount, out int triangleCount); }` returning null when no ring lies in the tile. `groundHeightAt` returns the raw `SampleBilinear` height; the builder adds `TerrainTileCreator.GroundTopHeight` (public const) itself. Implements section 1 exactly: resample, ownership, border rings, 7-vertex layout, colours, uv, uv2, normals, triangles, bridge cut, ford state, junction alpha, closed-loop seam ring, `UInt16` indices. Vertices stored relative to `meshOriginWorld` (the baker passes `tile.transform.position`). | `StitchPunk.Editor` | [after R1-1] |
| R2-2 | `Tests/Terrain/TerrainRoadRibbonMeshBuilderTests.cs` (new) | EditMode fixture `StitchPunk.Tests.TerrainRoadRibbonMeshBuilderTests`, fixture style of `Tests/Terrain/TerrainRoadRasterizerTests.cs` (`[TestFixture] public sealed class`, private `Create...` helper that builds `TerrainRoadRibbonInput`; style via `ScriptableObject.CreateInstance<TerrainRoadRibbonStyle>()` destroyed in `[TearDown]`). Defaults: width 4, flat `groundHeightAt = (x,z) => 0`, tile (0,0) rect x,z in [-50,50), origin `Vector3.zero`. Cases in section 6. | touched fixture | [after R2-1] |

### R3. Shader
| ID | Files | Brief | Gate | Marker |
|---|---|---|---|---|
| R3-1 | `Assets/PaintedGround/Shaders/PaintedRibbon.shader` (new; shader name `Painted/Ribbon`) | Use the `shader-edit` skill. Copy structure from `PaintedGround.shader`: tags (`Opaque`, `Geometry`, `UniversalPipeline`), one shared `CBUFFER_START(UnityPerMaterial)` in `HLSLINCLUDE` (l.97-125), the DOTS block pattern (l.127-135, instance `_SurfaceTint`), ForwardLit pragmas (l.145-160: `target 4.5`, `_MAIN_LIGHT_SHADOWS*`, `_ADDITIONAL_LIGHTS*`, `_CLUSTER_LIGHT_LOOP`, `_ADDITIONAL_LIGHT_SHADOWS`, `_SHADOWS_SOFT*`, `multi_compile_fog`, `multi_compile_instancing`, `DOTS_INSTANCING_ON`), `#include ".../Lighting.hlsl"` then `#include "Include/PaintedGroundCore.hlsl"`. Lighting: `PaintedLighting_float(posWS, normalWS, GetNormalizedScreenSpaceUV(IN.positionCS), _AmbientTint.rgb, _ShadowStrength, _NdotLInfluence, _PointLightBoost, lighting)` then `colour *= lighting` (that function already applies the `_TerrainAmbientColor` and `_TerrainLightningFlash` globals, defined in `PaintedGroundCore.hlsl` l.125-126). Passes: ForwardLit, DepthOnly, DepthNormals (copy ground's, l.361+, adding the same coverage clip); no ShadowCaster. `Offset -1,-1` on ForwardLit and DepthOnly (RB-S1 tunes). Vertex input: position, normal, `TEXCOORD0` uv, `TEXCOORD1` uv2, `COLOR`. | none (RB-S3 judged at the checkpoint) | [parallel-safe] |

Properties (exact): `_SurfaceTex` (2D, NoScaleOffset), `_SurfaceTint` (Color, DOTS-instanced), `_NoiseTex` (2D tileable), `_NoiseMetres` (Float 6), `_MetresPerTile` (Float 4), `_RutTint` (Color), `_RutDarkness` (Range 0..1), `_RutSharpness` (Range 1..8), `_RidgeLighten` (Range 0..0.5), `_EdgeWearColor` (Color), `_EdgeWearAmount` (Range 0..1), `_EdgeWearNoiseScale` (Range 0..1), `_WetTint` (Color), `_WetDarkness` (Range 0..1), `_EdgeDitherSoftness` (Range 0..1), plus ground's `_AmbientTint`, `_ShadowStrength`, `_NdotLInfluence`, `_PointLightBoost`. Fragment: `base = tex(_SurfaceTex, uv / _MetresPerTile) * _SurfaceTint`; `rut = saturate(G * _RutSharpness) * uv2.y`, `base = lerp(base, base * _RutTint, rut * _RutDarkness)`; `ridge = (1-G) * (1-R)`, `base += _RidgeLighten * ridge`; `wear = smoothstep(1 - _EdgeWearAmount, 1, R + (noise - 0.5) * _EdgeWearNoiseScale)`, `base = lerp(base, _EdgeWearColor, wear)`; `base = lerp(base, base * _WetTint, uv2.x * _WetDarkness)`; `coverage = A * (1 - B)`, `clip(coverage - lerp(0.5, noise, _EdgeDitherSoftness))`.

### R4. Baker, budget and panel
| ID | Files | Brief | Gate | Marker |
|---|---|---|---|---|
| R4-1 | `Editor/Terrain/Roads/TerrainRoadRibbonBaker.cs` (new), `Editor/Terrain/Roads/TerrainRoadBaker.cs` (one hook line) | New `public static class TerrainRoadRibbonBaker { public const string RibbonRootName = "Ribbons"; public const string RibbonMeshPrefix = "Ribbon_"; public static int BakeTileRibbons(TerrainTile tile); }` (returns total vertices). Algorithm: `Object.FindObjectsByType<TerrainRoad>(FindObjectsInactive.Exclude)`; keep roads with `profile != null && profile.ribbonStyle != null` whose `WorldBounds()` overlaps the tile; per spline build a `TerrainRoadRibbonInput` (`heightAt` = owning-tile `TerrainTileData.SampleBilinear(float,float)` via floor ownership, tile data from `TerrainTileCreator.FindTileAt(coordinate).tileData` else `AssetDatabase.LoadAssetAtPath<TerrainTileData>(TerrainTileAssets.TileDataPath(coordinate))`, cached per call; `crossingAt` from `FindObjectsByType<TerrainCrossing>` using `ContainsXZ` and `kind`); call `TerrainRoadRibbonMeshBuilder.Build`; persist mesh as sub-asset of `tile.tileData` exactly like `TerrainWaterBaker.BakeTile` l.435-508 (existing-by-name `CopySerialized`, `AddObjectToAsset`, `Clear()` unused, `DestroyImmediate` obsolete children, assign `style.material` as `sharedMaterial`); `EditorUtility.SetDirty(tileData)`, `EditorSceneManager.MarkSceneDirty` and `SaveScene(tile.gameObject.scene)`. Hook: in `TerrainRoadBaker.BakeTiles`, after the `for` loop over `tilesToBake` and still inside the `try` (l.305-319), run a second pass `TerrainRoadRibbonBaker.BakeTileRibbons(tile)` for every baked tile. A second pass, not a call inside `BakeTile` (l.177-263), because a neighbour tile's `roadHeightDelta` is stale until it has been baked, which would put different border heights in the two tiles. Deleted roads: tiles come from `pendingAffectedBounds` (previous bounds) so a tile whose roads vanished runs the same pass and its ribbon children are removed. | `StitchPunk.Editor` | [after R2-1] |
| R4-2 | `Editor/Terrain/Window/TerrainRoadsPanel.cs` | The existing Rebake button (l.84, `TerrainRoadBaker.RebakeAllRoads(true)`) already rebakes ribbons through R4-1, so no new button. Add a per-tile ribbon vertex label next to the missing/stale-tile counts (l.137-138), refreshed on `TerrainRoadBaker.BakeFinished` (l.92), using `TerrainBudgetCheck.TryEvaluateByCoordinate`. The style picker is the `ribbonStyle` field on the profile asset; no extra UI. | `StitchPunk.Editor` | [after R4-3] |
| R4-3 | `Editor/Terrain/TerrainBudgetCheck.cs`, `Editor/Terrain/TerrainDataReport.cs` | `TerrainTileBudgetResult` gets `public int ribbonVertexCount;`. In `Evaluate`: sum `sharedMesh.vertexCount` over `MeshFilter`s under `tile.transform.Find("Ribbons")` (ground is found by `GetComponentInChildren<MeshFilter>(true)`, which returns the tile's own filter first, so ribbons never pollute `meshVertexCount`); new const `MaxRibbonVerticesPerTile = 4000`; `isOverBudget` includes it; summary gains `"<n> ribbon verts"` plus `" OVER"`. `TerrainDataReport` needs no byte change: ribbon meshes are sub-assets of the data asset, so they already land in the `data` column of `BuildTileReport`; add a `ribbonVertexCount` text line per tile only if `BuildTileReport` can read it from `TerrainBudgetCheck` without opening scenes, otherwise leave it unchanged and say so. Estimated cost: 44 B/vertex, 4,000 vertices about 0.18 MB against the 1.2 MB tile disk budget. | `StitchPunk.Editor` | [parallel-safe] |

### R5. Content and verification
| ID | Files | Brief | Gate | Marker |
|---|---|---|---|---|
| R5-1 | `_Vault/Tasks/Verification/verify-terrain.md` | Owner passes: road across a border, drag a knot (ribbon follows after the 0.25 s debounce), ford and bridge, walk in Play and watch z-fight shimmer at the normal camera angles, `Verify Seams` and `Report Data Size` still pass. | none | [after R4-1] |
| R5-2 | `_Vault/Memories/Code/Terrain.md` | Ribbon section: layering rule, RB-D decisions, owning-tile floor rule (`RoundToInt` trap), second-pass trap, sub-asset rule. | none | [after R4-1] |

Checkpoint R (owner visual): one dirt road with ruts across a tile border and over a ford, in Play at the normal camera; judge edges, ruts, wear, shimmer.

## 6. R2-2 test cases (numeric)
Style defaults: inset 0.4, fade 0.5, rut spacing 1.4, ring spacing 1, lift 0.02; `GroundTopHeight` = -0.03 so flat vertex y = -0.01.
1. **Straight road symmetric.** Centreline (-40,0) to (40,0): 81 rings, `vertexCount` 567, `triangleCount` 960. For each ring, vertex j and 6-j have z summing to 0 (within 1e-4), equal y, and UV.x in {-2.1,-1.6,-0.7,0,0.7,1.6,2.1} (right of +x travel is -z).
2. **Colours.** Ring 0 vertex colours R = [255,255,0,0,0,255,255], G = [0,0,255,0,255,0,0], B = [255,0,0,0,0,0,255], A all 255.
3. **Border, integer start.** Centreline (-30,0) to (130,0), tiles (0,0) and (1,0) (rect x in [50,150)): tile 0 has 81 rings (x -30..50), tile 1 has 81 rings (x 50..130); the x = 50 ring positions are equal within 1e-5 once both use `meshOriginWorld = Vector3.zero`.
4. **Border, non-integer start.** Start at x = -30.5: tile 0 gets 82 rings (81 + the inserted border ring at 50), border rings of both tiles are equal within 1e-5.
5. **Closed loop.** Circle radius 20, `closedLoop = true`: rings at s = 0..125 (126) + 1 duplicated seam ring = 127 rings, 889 vertices, 1,512 triangles; first and last ring equal positions, UV.y differ by the loop length 125.66 (+-0.05).
6. **Budget.** 100 m straight road in one tile: 101 rings = 707 vertices, `<= MaxVerticesPerTile`; the 141 m diagonal stays under 1,100.
7. **Bridge cut.** Straight -40..40 with `crossingAt` Bridge for |x| <= 4.5: rings x -4..4 dropped (72 rings left), triangles 840, no triangle references both a ring with x <= -5 and one with x >= 5.
8. **Ford.** `crossingAt` Ford for |x| <= 4.5: those rings have uv2.x = 1 and uv2.y = 0 when `fadeRutsOnFord`, 1 when not; rings outside have uv2 = (0,1).
9. **Junction.** Junction point (0,0), road along x: ring x=0 has A = 0, x = +-1 has A = 85 +-1 (0.333), |x| >= 3 has A = 255.
10. **Slope.** `groundHeightAt = 0.2 * x`: vertex y = 0.2x - 0.01; normal.x = -0.196 +-0.002, normal.y = 0.981 +-0.002.
11. **Empty.** Road entirely outside the tile returns null with 0 counts.
Revert check: break the ownership rule (use rounding) and case 3/4 must fail; otherwise delete case 4.

## 7. Order of work
Wave 1 in parallel: R1-1, R3-1, R4-3. Wave 2: R2-1 (needs R1-1). Wave 3 in parallel: R2-2, R4-1, R4-2. Gate each assembly once per wave (`StitchPunk.Data`, `StitchPunk.Editor`, `StitchPunk.Tests`). Then spikes RB-S1, S3, S4 (orchestrator), R5, Checkpoint R. About 8 worker tasks plus spikes.

## 8. Known gaps by design (v1)
No 3D stones or tufts; no weather driver; no player-visible deformation (tracks are decals); strategic map reads the road graph; ribbons overhang into the neighbour tile by up to 2.1 m; no adaptive ring density.

## 9. Changes from the first draft
- **Mesh storage:** sub-asset of `TerrainTileData` named `Ribbon_<road>` instead of a loose `Ribbon_<road>.asset`, because `TerrainWaterBaker` and `TerrainTileBuilder.Rebuild` prove only sub-assets of the data asset bake into the subscene reliably.
- **RB-D8 live preview dropped:** the road baker has no in-memory preview; `TerrainRoadBaker` debounces 0.25 s and writes (and saves the scene) every time. The ribbon follows that path.
- **Ring density:** fixed spacing, not denser past 5 degrees bends.
- **UV.x is signed metres,** not 0..1, so both texture axes tile at world scale; edge masks come from vertex colour.
- **Added** `fadeBandMetres` and a second UV channel (wetness, rut fade), `closedLoop` seam ring, second-pass hook in `BakeTiles`, spike RB-S4, and RB-S2 answered by reading.
