---
status: BUILDING 2026-11-21. T0 accepted; T1, T1b, T2, T3, T4, T6 built and gated on spec/terraces. Next: T5 + T7 (wave 3).
reference: Tasks/Captures/References/ (owner's reference shots: flat plateaus, vertical cliffs, rounded outlines, clean biome ends)
captures: Tasks/Captures/Terraces_<date>/NN_<what_it_shows>.png, one folder per capture pass
extends: Tasks/Verification/Terrain_System.md (tile data, bakers, nav, streaming, strategic stand-in)
names verified against: main at 1f80cd03 (2026-11-21). If a name has drifted, grep and note it in the log.
---

# Terraces: flat levels with rounded cliff walls

## 0. What the owner wants

Most ground is flat; height changes come as **plateaus** separated by **vertical cliff walls** whose outlines have broad rounded corners, with a grass overhang on the top edge and a dark foot at the base. Biome edges stay as they are (SDF splat + fringe strip already gives clean ends).

## 1. Owner answers (2026-11-21, do not re-ask)

- **Alongside, not instead.** Terraces add a stepped offset on top of the existing height layers. The height brush, procedural fbm and the one mountain keep working, so a plateau top can still roll gently.
- **Ramps only.** Units, the player and the caravan change level only on a ramp. No jumping, no falling, no climbing.
- **Spike first,** captures saved under `Tasks/Captures/Terraces_<date>/` with titles that say what each shot shows.
- **Way of working:** the orchestrator observes; small Sonnet workers edit; Haiku verifiers grep. Token-efficient briefs.

### Owner answers (2026-11-23, do not re-ask)

- **Rounded top AND bottom, grass on both.** A raised part rounds over at the top and rounds out at the bottom, and both edges carry grass, as in `References/4_dse_wendywillowforestfire_a.png`. The face between stays steep rock ("steep face, rounded ends", not a soft S-curve).
- **Cliffs on slopes, not only flat plateaus.** "The plateau should be part of the terrain." Separate brushes: one makes a plateau; another works on a hill: painting one side lowers it, so the hill slopes up gradually and ends in a terrace edge (cliff).
- **Generation makes them too, per biome.** These formations must also form naturally when new areas are generated, with different rules per biome. "Being able to handle this is what I need." The orchestrator may modify the spec to achieve it (done: TX-D11 to TX-D14, T12 to T15).

## 2. Decisions (TX-D, final unless the owner overrides)

- **TX-D1 Levels are integers per 1 m cell.** `byte` level 0..3 (`maxTerraceLevels` = 3), height per level `terraceLevelHeightMetres` = 3 on `TerrainWorldProfile`. Source layers stay separate, like height: `baseTerraceLevels` (procedural) and `handTerraceLevels` (255 = no override, else the level). Final level = hand if set, else base.
- **TX-D2 Outlines come from a smoothed field, not from polylines.** Per level threshold, a signed distance from the cell grid, blurred to `terraceCornerRadiusMetres` (default 2.5), computed in WORLD coordinates with a one-tile margin read from neighbour data assets (as the seam weld already does). Both sides of a tile seam compute identical values, so walls meet exactly. The T0 spike used polyline Chaikin smoothing only to judge the look; it does not cut a heightmap and does not survive seams.
- **TX-D3 The tile mesh is cut along the outline.** Cells wholly on one level stay ordinary 1 m quads (height = existing layer sum + level offset). Cells the outline crosses are subdivided 4 x 4 (0.25 m) and split by marching squares into a lower and an upper polygon, with a vertical wall quad along each iso segment. A tile with no level above 0 keeps the current `TerrainMeshBuilder.BuildTileMesh` path byte for byte.
- **TX-D4 Wall texture runs along the edge.** Wall uv = (metres down from the top edge, arc length along the chained outline). Chains break at tile edges; each chain's arc length starts at a phase hashed from its world start point. If the seam shows in a capture, revisit.
- **TX-D5 Meshes.** The tile render mesh gets a second submesh (walls, cliff material) and stays the MeshCollider, so walls block physics. Lip and foot strips are a visual-only child mesh `TerraceDressing` (no collider, no shadow casting for the foot).
- **TX-D6 Ramps are objects, not paint.** `TerraceRamp` (tile scene, WORLD-coordinate oriented rectangle: centre, yaw, width default 6 m, run default 9 m so 3 m rise is ~18 degrees, under the 25 degree heavy-slope rule). The tile mesh leaves the ramp footprint as a hole and emits no wall inside it; `TerraceRampMeshBuilder` fills it with the sloped surface plus two side walls. A ramp spans exactly one level step.
- **TX-D7 Change flow mirrors roads and water.** `TerraceRamp` edits and brush strokes raise `TerrainTerraceEvents.TerraceChanged(Vector2Int tile)`; `TerrainTerraceBaker` debounces 0.25 s and rebuilds only the touched open tiles plus the neighbour whose seam a stroke touched.
- **TX-D8 Nav.** A nav cell crossed by a wall is `WallCost`; ramp cells go through the normal slope rules. No new surface type.
- **TX-D9 Rivers and roads at an edge.** v1 has no waterfalls: a river crossing a terrace edge keeps the wall and the water baker warns. A road crossing an edge outside a ramp gets a warning from the road baker; place a ramp under it.
- **TX-D10 Tool.** Scene-view direct manipulation: a stroke paints the level under the cursor at stroke start **+1** (Shift: -1, Ctrl: clear the hand override), so one stroke always makes one flat plateau. The rounded outline previews live while dragging (contour lines only); mesh rebuild happens at mouse-up. A ramp is placed by dragging from lower ground across an edge. Panel holds brush size and corner radius only; it must work at the owner's docked 177 pt width.

- **TX-D11 Wall profile: steep face, rounded ends, grass both ends** (amends the vertical-quad wall of TX-D3 and the dark foot of TX-D5). Cross-section from top to bottom: a convex top fillet (`terraceTopRoundMetres`, default 0.75) carrying a grass overhang, a steep rock face, a concave bottom fillet (`terraceFootRoundMetres`, default 0.75) carrying a grass foot. At least 3 segments per fillet; fillets stay inside the wall strip so the plateau top and the ground below keep their outline (TX-D2). Lip and foot are GRASS in the biome ground colour, not dark. The rock face uses a rock texture with vertical striations; never the Ribbon/fringe stripe texture (capture 07 shows horizontal grass bands tiled down the wall). The MeshCollider keeps using the render mesh.
- **TX-D12 Signed levels: cliffs on slopes** (amends TX-D1). A level is a step from the ground's own height: -3..+3. Stored as `byte` with bias `TerraceLevelBias` = 3 (stored 0..6), 255 still means no hand override, null base array = all level 0. Final height = height-layer sum + level x `terraceLevelHeightMetres`. A wall exists wherever neighbouring levels differ, whatever the slope underneath, so a hill with its downhill side at level -1 rises gradually and ends in a cliff. The terraced path runs when any level != 0. Nothing has shipped data with the old encoding; no migration.
- **TX-D13 Two brushes** (amends TX-D10). **Plateau**: as TX-D10, start level +1, flat top over the existing slope. **Cliff**: paints the cells under the stroke to (level at stroke start) - 1, so the painted side of a hill drops one step and the stroke edge becomes the cliff; the unpainted side keeps its slope. Shift inverts either brush; Ctrl clears the hand override. Panel: two mode buttons (icons first, R-rules), brush size, corner radius; still fits 177 pt.
- **TX-D14 Generation, per biome.** `TerrainGenerationProfile` gains a list of `TerraceBiomeRule` keyed by biome: `enabled`, plateau noise threshold + scale, `maxLevel`, `minPlateauAreaSquareMetres`, `cliffOnSlope` (where the height-layer slope exceeds `cliffSlopeDegrees` and noise passes, the downhill side of a contour gets level -1, making slope-then-cliff formations), `cliffChance`. A biome with no rule gets no terraces. Writes `baseTerraceLevels` only. **Reachability (ramps-only rule):** after levels are set, every connected region above or below its surroundings gets at least one procedural `TerraceRamp` on its longest straight edge stretch (>= ramp width + 2 m); a region with no such stretch is culled to level 0.

## 3. Execution protocol (observer pattern)

- **Orchestrator (Opus) observes.** It writes briefs, runs the gate once per wave, does every Unity MCP step (refresh, rebake, Play, captures) and judges captures against `Tasks/Captures/References/`. It edits only one-line fixes.
- **Workers: `worker`, `model: sonnet`.** One task per worker, at most two named files, the pinned signatures from section 4 pasted into the brief, reading limited to named line ranges, no MCP, `python Tools/devloop/devloop.py gate StitchPunk.Editor` (or the named assembly) before the report, stop at turn 30, report of 30 lines or fewer.
- **Greps and inventories: `verifier`, `model: haiku`.** Behaviour reviews: `verifier`, `model: sonnet`.
- **Tasks marked [parallel-safe] in one wave spawn together.** Gate once after the wave, not per worker.
- **Every capture pass** writes `Tasks/Captures/Terraces_<yyyy-mm-dd>/NN_<what_it_shows>.png` and is read by the orchestrator before a phase closes. No capture, not done.
- After a rate-limit kill, `git diff` before respawning; edits are usually on disk.

## 4. Pinned surfaces (workers do not change these signatures)

```csharp
// Data/Terrain/TerrainTileData.cs  (T1)
public const int TerraceCellsPerSide = 100;            // 1 m cells, index z * 100 + x, cell 0 = min corner
public const byte NoHandTerraceLevel = 255;
public byte[] baseTerraceLevels;                        // length 10000, null = all 0
public byte[] handTerraceLevels;                        // length 10000, null = no overrides

// Data/Terrain/TerrainWorldProfile.cs  (T1)
public float terraceLevelHeightMetres = 3f;
public int maxTerraceLevels = 3;
public float terraceCornerRadiusMetres = 2.5f;

// MonoBehaviours/Terrain/TerraceRamp.cs  (T1)   ExecuteAlways, raises TerraceChanged on validate/move/disable
public float widthMetres = 6f; public float runMetres = 9f;   // pose = transform position + yaw, WORLD space
public int lowerLevel;                                         // upper = lowerLevel + 1

// MonoBehaviours/Terrain/TerrainTerraceEvents.cs  (T1)
public static event Action<Vector2Int> TerraceChanged; public static void RaiseTerraceChanged(Vector2Int tile);

// Editor/Terrain/Terraces/TerraceLevelField.cs  (T2, pure)
public static byte ResolvedLevel(TerrainTileData data, int cellX, int cellZ);
public static float[] BuildSignedDistance(TerrainTileData data, int threshold, float cornerRadiusMetres,
    Func<Vector2Int, TerrainTileData> neighbourLookup, int samplesPerMetre);   // positive = on/above threshold

// Editor/Terrain/Terraces/TerraceTileMeshBuilder.cs  (T3, pure)
public static Mesh BuildTerracedTileMesh(TerrainTileData data, TerrainWorldProfile world,
    Func<Vector2Int, TerrainTileData> neighbourLookup, IReadOnlyList<TerraceRampFootprint> ramps,
    float verticalOffsetMetres);                         // submesh 0 ground, submesh 1 walls
public static List<TerraceOutlineChain> LastBuildOutlineChains;   // consumed by dressing + nav, rebuilt each call

// Editor/Terrain/Terraces/TerraceRampMeshBuilder.cs  (T4, pure)
public readonly struct TerraceRampFootprint { public readonly Vector2 centre; public readonly float yawDegrees, widthMetres, runMetres, lowerHeight, upperHeight; }
public static Mesh BuildRampMesh(in TerraceRampFootprint ramp, Vector3 tileOrigin);  // submesh 0 surface, 1 side walls

// Editor/Terrain/Terraces/TerraceDressingBuilder.cs  (T6, pure)
public static Mesh BuildLipAndFootMesh(IReadOnlyList<TerraceOutlineChain> chains, float fringeSizeMetres);  // submesh 0 lip, 1 foot
```

`TerraceOutlineChain` = `{ Vector3[] points (tile-local, at wall top), float[] bottomHeights, float startArcPhase }`, declared in `TerraceTileMeshBuilder.cs`.

## 5. Phases and tasks

| Id | Wave | Files (at most two) | Work | Check |
|---|---|---|---|---|
| T0 | 0 | `Editor/Terrain/Terraces/TerraceSpikeGeometry.cs`, `TerraceSpikeScene.cs` | Look spike: two levels, convex + concave corners, wall/lip/foot, five captures | Orchestrator reads `Terraces_2026-11-21/01..05`; owner accepts the look |
| T1 | 1 [parallel-safe] | `TerrainTileData.cs`, `TerrainWorldProfile.cs` | Fields and constants from section 4 | gate `StitchPunk.Data` |
| T1b | 1 [parallel-safe] | `TerraceRamp.cs`, `TerrainTerraceEvents.cs` | Ramp authoring + event | gate `StitchPunk.MonoBehaviours` |
| T2 | 1 [parallel-safe] | `TerraceLevelField.cs`, `Tests/Terrain/TerraceLevelFieldTests.cs` | Smoothed SDF, neighbour margin | test: a 90-degree corner's iso-line sits within 0.2 m of a 2.5 m arc; both sides of a seam give identical samples |
| T3 | 2 | `TerraceTileMeshBuilder.cs`, `Tests/Terrain/TerraceTileMeshBuilderTests.cs` | Cut mesh + walls + outline chains | test: no boundary edges except the tile rim and ramp holes (watertight); flat tile output equals `BuildTileMesh` |
| T4 | 2 [parallel-safe] | `TerraceRampMeshBuilder.cs`, its test | Ramp surface + side walls | test: ramp rim vertices match the hole rim within 1 mm |
| T6 | 2 [parallel-safe] | `TerraceDressingBuilder.cs`, `PaintedGround/Shaders/PaintedCliff.shader` only if T0 found Painted/Ribbon unfit | Lip + foot strips; cliff material | capture |
| T5 | 3 | `TerrainTerraceBaker.cs`, `TerrainTileBuilder.cs` | Debounced bake, terraced path when any level > 0, collider, dressing child, ramp children | Orchestrator: rebake TestWorld, Play, unit walks up a ramp and is blocked by a wall |
| T7 | 3 [parallel-safe] | `TerrainNavCellRules.cs`, nav tile baker | Wall cells from outline chains | extend `TerrainNavCellRulesTests` with one wall-crossing case |
| T8 | 4 | `Brushes/TerrainTerraceBrush.cs`, `Window/TerrainTerracesPanel.cs` (+ uss rows) | TX-D10 tool | EditorStyleGuide R01-R22 audit + before/after captures of the panel at 177 pt and the Scene view mid-stroke |
| T9 | 4 [parallel-safe] | `TerrainProceduralGenerator.cs`, `TerrainGenerationProfile.cs` | Terrace rules (noise threshold → level, cull regions under `minPlateauAreaSquareMetres` 150) writing only `baseTerraceLevels` | test: generator never touches `handTerraceLevels` |
| T10 | 4 [parallel-safe] | `Strategic/TerrainStrategicBaker.cs` | Stand-in uses the terraced builder at its step, walls kept | capture at full strategic zoom |
| T11 | 5 | `TerrainTestWorldContent.cs` | Terraces + two ramps in the explorable test world | capture pass matching the four reference angles; owner play pass |

| T12 | 5 | `TerraceTileMeshBuilder.cs`, `TerraceDressingBuilder.cs` (+ `PaintedCliff.shader` rock face) | TX-D11 rounded profile, grass lip + grass foot, rock face texture | capture `09_wall_profile_rounded_top_and_foot` judged against `4_dse_wendywillowforestfire_a` |
| T12b | 5 [parallel-safe] | `TerraceRampMeshBuilder.cs`, its test | Fix 08: black ramp side walls; a wall segment floating in the air across the ramp opening | capture `10_ramp_fixed` |
| T13 | 5 [parallel-safe] | `TerrainTileData.cs`, `TerraceLevelField.cs` (+ `TerraceTileAssembly.TileNeedsTerracedPath`) | TX-D12 signed levels | test: a hill with level -1 on its lower half yields one wall chain with constant 3 m drop |
| T14 | 6 | `Brushes/TerrainTerraceBrush.cs`, `Window/TerrainTerracesPanel.cs` | TX-D13 Plateau + Cliff brushes | R01-R22 audit + panel capture at 177 pt + Scene view mid-stroke on a hill |
| T15 | 6 [parallel-safe] | `TerrainProceduralGenerator.cs`, `TerrainGenerationProfile.cs` | TX-D14 per-biome rules, cliff-on-slope, reachability ramps | test: every non-zero region has a ramp or is culled; generator never touches `handTerraceLevels` |

T11 runs after T12-T15 so the test world shows plateaus, slope-then-cliff formations and generated areas.

Spec retires to `Tasks/Verification/Terraces_System.md` with `verify-terraces.md` after T11.

## 6. Shared files and other sessions

`TerrainTileData.cs`, `TerrainTileBuilder.cs` and `TerrainNavCellRules.cs` are shared with live terrain work. Run `ListAgents` before each wave; if a peer is on main touching `Terrain/`, build in a worktree (`[[feedback-worktree-when-peer-busy]]`).

## 7. Open (defaults apply)

- **TX-Q1 Ink look on walls:** default, walls get the same ink treatment as other world geometry (`Tasks/Verification/InkLook_System.md`).
- **TX-Q2 Waterfalls:** default none in v1 (TX-D9).
- **TX-Q3 Cliff art:** default, reuse `T_Cliff.png` and `T_EdgeFringe.png`; a dedicated painted cliff strip and overhang strip will lift the look more than any code change.

## 8. Log (one line per wave, newest last)

- 2026-11-21 Spec written. T0 spike code written (gate + lint pass, Painted/Ribbon walls opaque, T_EdgeFringe alphaIsTransparency off). Not yet run: Editor closed. Next: menu Build Terrace Spike, then Capture Terrace Spike, read 01-05.
- 2026-11-21 T0 run: captures `Terraces_2026-11-21/01..05` judged by the orchestrator against References. Owner accepted: "continue, the edges do look weird, but I figure you'll fix them down the line". Carry forward:
  - **T6 lip is wrong:** renders as a solid black band with jagged red/yellow teeth (02, 04). Fringe texture/material is wrong, not a grass overhang. Fix in T6.
  - **Walls read as a short plain grey-blue curb.** Reference `3_dse_04` wants tall dark walls with vertical striations. Check the 3 m height actually lands (T3/T5 capture) and treat Painted/Ribbon as unfit: T6 takes the `PaintedCliff.shader` branch.
  - **No dark foot fringe** at the wall base. T6 foot strip must read dark.
  - **Spike-only artefacts, do not carry over:** red debug outline on plateau tops; kinks where Chaikin chains join (the circle has 4 bumps); a fan of thin lines on the ground inside the concave notch (04). T3 test/capture must show the real cut mesh has no fan.
- 2026-11-21 Wave 1 (T1, T1b, T2): gate PASS at 5d6826b0, TerraceLevelFieldTests 3/3. TerraceRamp tile coordinate is floor((world + 50) / 100) because tiles are centred on coordinate*100 (spec said floor(world/100)). TerraceLevelField clamps its output to +-4 m and pads ceil(3r)+ceil(clamp)+2 cells; both are needed for seam identity.
- 2026-11-21 Wave 2 (T3, T4, T6): gate PASS at a31fb676, compile clean, TerraceTileMeshBuilderTests 4/4, TerraceRampMeshBuilderTests 3/3. Outline chains travel with the LOWER side on the RIGHT (TerraceDressingBuilder relies on it). PaintedCliff (`Painted/Cliff`) has one shader and three materials via `_Mode` (0 Wall, 1 Lip, 2 Foot) plus blend/ZWrite/Cull properties. Wall material: Src 1, Dst 0, ZWrite On. Lip: Cull Off, `_SurfaceTex` = T_EdgeFringe. Foot: SrcAlpha/OneMinusSrcAlpha, ZWrite Off, queue Transparent. The C# gate does not check shader compilation, so T5's Editor pass must read the console for it. Known v1 limits: a subcell crossed by two thresholds is clamped (`LastBuildMultiCrossingSubcells` counts these); a T-junction can occur across a tile seam where only one side subdivides; ramp holes are cut at 0.25 m subcells and the ramp mesh overlaps the hole edge. Gates that add a new shader or asset leave the broker stage stuck ("failed to restore trunk"), so the orchestrator restores trunk by hand.
- 2026-11-21 Wave 3 (T5, T7): orchestrator gate at cf344fab. Editor compile clean, no Painted/Cliff shader errors, TerrainNavCellRulesTests + TerraceTileMeshBuilderTests + TerraceRampMeshBuilderTests + TerraceLevelFieldTests 19/19. **Blocker found:** the project has NO `TerrainWorldProfile` asset, so `FindInProject()` returns null and the terraced path never runs on any tile. The orchestrator used a temporary profile to capture 06-08. T5 must create the asset (or fall back to defaults with a warning); never silently skip terraces. Captures (hand block 30x30 m with a 10x10 notch at tile 0_0, ramp at (5,-10)): 06 plateau reads, grass lip present; 07 wall face is horizontal grass/dirt bands (fringe texture tiled down the wall); 08 ramp side walls solid black, and a wall strip floats in the air across the ramp opening. Magenta (error-shader) cubes visible in TestWorld in 06; check whether a terrace material is involved.
- 2026-11-23 Owner amendments: rounded top + bottom with grass on both; cliff brush for slope-then-cliff on hills; per-biome generation. Added TX-D11 to TX-D14 and T12 to T15 (orchestrator).
- 2026-11-23 Wave 5 partial (profile fix 24b6cc2a, T13 + T12b 42591440, plus T8/T9/T10 never compiled before): orchestrator gate PASS, Editor compile clean, 22/22 across the four terrace fixtures. Profile asset, `M_Terrace{Wall,Lip,Foot}` materials and the T8 metas committed from the gate. Captures 10/10b: the floating wall over the ramp opening is gone, BUT (a) ramp side walls and plateau walls still render near-black: the mesh is fine (normals and uv checked); the cause is `M_TerraceWall` colours (_TopColor 0.22/0.24/0.30, _BottomColor 0.08/0.08/0.11) plus `_Cull` 0, so back faces draw lit with their front normal; T12 must use Cull Back and a lighter rock palette; (b) the lip/foot dressing appears as horizontal striped bars (T_EdgeFringe tiled) sticking out at mid-wall height beside the ramp, not draping over the edge, and dressing is not skipped over the ramp opening; T12 replaces the dressing with the TX-D11 grass fillets and must skip ramp openings like T12b does for walls. The Terraces tab capture at 177 pt is still owed (T14).
- 2026-11-23 Waves 6-8 (T12, T14, T15, T11) on spec/terraces-c3, offline gate `game` PASS, Editor/tests/captures owed. T12: the profile runs OUTWARD from the top outline (top fillet + foot fillet in the GROUND submesh so they take the biome grass, rock face in submesh 1); the lower ground still ends at the outline under the skirt, so the two watertight tests now accept only those hidden seams. Wall Cull Back, rock palette lightened, lip/foot are procedural grass blades (`_BladeDensity`), no T_EdgeFringe. T14: segmented control is text-only (`TerrainUi.MakeSegmented` has no icon overload). T15: `TerraceBiomeRule` defaults (Grass, Stone) are ENABLED, so regenerating any existing region now makes terraces; regions touching the 200 m window border get neither ramp nor cull. Worktree gate trick: junction `Library` to main's and gate `game`, not one assembly (a single assembly links main's stale DLLs).
- 2026-11-23 Gate b7d10fa6 (waves 6-8): Editor compile clean, no PaintedCliff shader errors, fixtures 32/33. FAIL `TerraceTileMeshBuilderTests.CliffProfileFacesOutwardAndUp`: "ground triangle faces down at (-6.57, 2.83, -20.31), expected > 0, was -0.0064" (a near-vertical fillet triangle; loosen to > -0.01 only if it is the corner case, else fix the winding). Captures 09/09c/10c/11 (capture lesson: TerrainWorld's Main Camera has FOV 15 + CinemachineBrain; render with a temp camera at FOV 40). Look verdict, NOT accepted: (1) the top "round-over" renders as a flat horizontal grass sheet sticking ~2-3 m out from the plateau edge at top height, with blades metres long hanging from it like a curtain (09, 10c, 11); it must hug the edge and curve DOWN into the face within `terraceTopRoundMetres` (0.75); (2) blades are far too long: target 0.15-0.35 m tufts; (3) the rock face is mostly hidden behind the blade curtain; where visible (09 right, beside the ramp) it is dark brown, not the light striated rock; (4) the foot is a flat grass sheet lying on the ground ahead of the wall rather than a concave round-out; (5) from the gameplay camera (09c) the plateau barely reads: no visible wall band or shadow line, so the height step is lost. Ramp (10c): side walls no longer black, ramp surface fine, but the lip sheets cross the view either side of it. Owner capture review pending.
- 2026-11-24 Gate 1638cac9 (walls as their own `TerraceWalls` child): compile clean, TerraceTileMeshBuilderTests + TerraceRampMeshBuilderTests 10/10. Root cause of the invisible walls: the tile mesh's second submesh never draws, even on a plain GameObject, though its CPU data is valid. Walls had not been visible since wave 3. Captures 09/09c/10c/11/16 (16 = gameplay FOV 15) show the plateau with a rounded grass top, a rock face and a grass foot, readable from the game camera. Capture lesson: in a subscene, rebuild in one execute_code call and render in a LATER call, so live baking catches up. Open look notes for the owner: the rock face texture smears horizontally instead of showing crisp vertical striations; the top grass reads as a thick flat band; tufts zigzag at the concave notch; ramp side walls still use the plain rock path; roads and scatter do not react to plateaus yet (TX-D9).
- 2026-11-24 Merged to main (821a372e) so the owner can judge in the Editor. A test plateau (30x30 m with a 10x10 notch) plus a ramp is saved in TestWorld tile 0_0. Owner look notes, round 2 (do not re-ask):
  1. **Grass stops before the curve.** The top grass edge and the foot grass edge end where the flat ground meets the bevel. The curved bevel itself is not grass-covered, at top or bottom.
  2. **Tighter bevel.** The curves are too big; make the rounding a tight bevel, not a 0.75 m fillet.
  3. **Rounded corners in plan view.** Where the outline turns, the corners don't read as rounded; they look faceted or angular.
  4. **Ramps follow the same rules.** Ramp bottoms and side walls get the same corner rounding, bevel and grass-edge rules as the plateau walls.
  5. **Edges take the ground's material.** The top and foot edge fringes must match the ground material they border where it meets the stone wall: a grass fringe on grass, a stone or dirt fringe on stone or dirt, sampled from the tile's biome splat and painted ground texture. The current fringes are pure flat green and look nothing like the surface they end. This applies to ramps too.
  6. **The plateau top is not raised on screen.** In the Scene/Game view the walls stand at full height, but the ground inside them renders at the old height, so the plateau centre doesn't match its edges. Orchestrator evidence: the collider and `MeshFilter.sharedMesh` ARE raised (raycast 2.97 m, max vertex y 5.02), and the tile mesh is a sub-asset of `Tile_0_0_Data.asset` that is rewritten in place. The new `TerraceWalls` child bakes fresh, but the subscene's baked ground entity seems to keep the old mesh. Suspect live baking does not see an in-place sub-asset mesh change. Fix it so a rebuild always shows the raised ground, without the owner having to close and reopen the subscene.
  6. **Plateau top must actually render raised.** In the Editor scene the ground inside the walls stays at the old height: the walls stand at the edge but the middle does not match. The collider and the tile mesh vertices ARE raised (raycast 2.97 m, mesh max y 5.03), so suspect the rendered or baked ground: the subscene's baked entity or a mesh sub-asset updated in place and never rebaked, or a TerrainTileBuilder path that renders a separate flat ground mesh. The fix must hold after a save + subscene rebake + Play, not just in a capture.
  7. **Ramps: seamless ends, rule-following sides (clarifies note 4).** A ramp's top and bottom ends are seamless: the slope surface flows straight into the plateau top and into the ground below with no bevel, wall, grass edge or step. The bevel, corner-rounding and edge rules apply only to the ramp's two SIDE walls and to the corners where those side walls meet the plateau wall. Those corners must line up and round into the plateau's wall profile.
- 2026-11-24 Gate 5ff6c7a4 (polish r2, branch spec/terraces-polish2): compile clean, 13/13 across the three terrace fixtures, ramp child slot 0 = M_Ground_Tile_0_0. Captures `*_r3`. Note 6 FIXED: the plateau top renders raised; the Mesh API copy fixed the stale GPU mesh. Note 3 FIXED: plan corners are smooth rounded curves (11, 16). Note 1: no grass on the bevel. Still open: (a) a see-through rectangular hole at the TOP end of the ramp where it meets the plateau top; you see the ground below through it (10c, 16); (b) the foot fringe is a regular sawtooth, not ragged tufts, and on the stone edge (18) it is green over grey stone, so note 5 is not met there; (c) tiny green triangle specks dot the top edge; (d) the rock face is still a smeared vertical-blur texture, not crisp striated rock; (e) the wall is bare sand-brown everywhere, which reads more 'clay' than the reference's dark rock.

## 9. Round 4: vector look + material edges (owner 2026-11-25, do not re-ask)

Owner verdict on r3: the bevel SIZE is right, but there is literally no edge. The ground's ragged edge silhouette must show FIRST, then the bevel. The edge changes with the material. Switch the art from painterly to flat vector ("think Paper Mario"), matching the owner's own art (Assets/Art/CutoutSheets/NatureAtlas.png: flat 2-3 tone posterised fills, muted greens #3e4d36 to #6b7d5c, warm brown #6e5a48, thin dark ink outlines, no noise or brush streaks). Walls need real detail, not a stretch. Everything must be swappable for the owner's own textures, with a guide. Bar: match references 3_dse_04 (cliff) and 4_dse_wendy (edges). Judged harshly, round after round, until it does.

### Decisions (TX-D15..D20)
- **TX-D15 One swap point: `TerrainPalette`.** Extend `TerrainPaletteLayer` with `Texture2D edgeTexture`, `float edgeDepthMetres` (default 0.6, how far the edge reaches onto the top) and `float edgeOverhangMetres` (default 0.25, how far it hangs out over the bevel). `TerrainPalette` gains `Texture2D terraceWallTexture`, `float terraceWallTileWidthMetres = 3`, `float terraceWallTileHeightMetres = 3`, and `public void ApplyToTerraceWallMaterial(Material wall)`. That method sets `_EdgeTex0`..`_EdgeTex3` (index order dirt, grass, forest, stone, matching `GetLayer`), `_EdgeDepths` and `_EdgeOverhangs` (Vector4 per layer), `_WallTex`, `_WallTileSize` (Vector4 x = width, y = height), and the splat (`_SplatMap`, `_SplatBounds` copied from the ground material). The terrace builder calls both Apply methods whenever it builds a tile.
- **TX-D16 Vector textures are generated, then replaceable.** `Editor/Terrain/Art/TerrainVectorTextureGenerator.cs` has a menu item, `Stitch Punk/Terrain/Art/Generate Vector Mock Textures`, that writes tileable PNGs to `Assets/PaintedGround/Textures/Vector/`:
  - `T_V_Dirt`, `T_V_Grass`, `T_V_Forest`, `T_V_Stone`: 1024², flat posterised shapes with ink outlines.
  - `T_V_Edge_<layer>`: 1024 x 256 strips, alpha-cut silhouettes with a 2 px ink outline. Grass = irregular tuft clumps, stone = chipped slab rim, dirt = crumbly clods, forest = mossy tufts plus needles. Never a regular sawtooth.
  - `T_V_Wall`: 1024², vertical rock columns with ink crack lines, like ref 3.

  It then creates `TerrainPalette_Vector.asset` and makes it the active palette. The painterly textures stay on disk, untouched.
- **TX-D17 Edge geometry.** Each outline chain gets an edge strip: from `edgeDepthMetres` inside the top rim to `edgeOverhangMetres` past it, draped over the start of the bevel. A second strip at the foot uses the LOWER ground's layer, from the foot outward, overlapping the wall base. Material choice: sample `_SplatMap` at the strip's world XZ, blend the four `_EdgeTex*` by splat weight, then alpha-clip, so stone tops get stone edges and grass gets grass. The edge is drawn by PaintedCliff (Edge mode) on the TerraceDressing child. Ramp side walls get edges; ramp top and bottom ends get none (TX note 7).
- **TX-D18 Wall face uv in metres.** u = arc length / `_WallTileSize.x`, v = height / `_WallTileSize.y`, so no stretch. A darker band toward the foot (AO) and a lighter top bevel. No vertical blur smear.
- **TX-D19 Ground goes flat.** The ground material's painterly extras (macro noise, brush or ink detail, transition atlas strength) are turned down to near zero in `TerrainPalette_Vector` via material properties, so the vector textures read clean. No shader rewrite unless a property is missing.
- **TX-D20 Guide.** `Docs/Terrain/TerrainArtSwapGuide.md` covers:
  - every texture slot, its size, tiling, alpha and edge-strip layout (which rows are the silhouette, ink width, top and bottom orientation);
  - how to swap a texture and press Apply;
  - how to add a new edge;
  - a diagram of top surface, edge, bevel, face, foot bevel, foot edge, lower ground.

### Acceptance (every item must pass in a capture; any fail = another round)
- A1: Along every plateau edge, in the gameplay camera (FOV 15) and in the close-up, a ragged silhouette of the TOP material visibly overhangs the rim, then the bevel, then the face. No stretch of rim without an edge.
- A2: The edge colour and shape match the surface it ends: a grass top gives a grass edge, and a stone top (notch side, near the grey patch) gives a stone edge.
- A3: The lower ground's edge overlaps the wall foot the same way.
- A4: The face shows crisp vertical rock columns with ink lines at the same texel scale as the ground (within 2x). Darker toward the foot. No stretch or smear.
- A5: Ground, edges and walls read as flat vector in the NatureAtlas palette. No painterly noise left.
- A6: Ramps: surface continuous with the ground, no hole at the top or bottom, side walls with the same face and edges, and the side-wall corners blend into the plateau wall.
- A7: No specks, no regular sawtooth, no z-fighting, no seam at tile borders (tile 0_0 to -1_0).
- A8: Swap test: the orchestrator swaps `T_V_Grass` in the palette for another PNG, presses Apply, and the scene changes. The guide's steps work as written.

### Round 4 verdict (orchestrator, 2026-11-25): FAIL. Branch spec/terraces-r4-edges at 9143b8e7, which already merges r4-art.
Gate: compile clean, 15/17. `RampTopEndLeavesNoHoleInThePlateauSurface` fails in both cases ("pit at (-3.50,-19.50), expected 3.0, was -6.0"), so the ramp-top hole fix does not work. Captures: `r4_01`..`r4_07`.
- **A1 FAIL: the edge reads as a separate curb.** On stone, the top edge is a lighter uniform ribbon along the rim with a tiny comb of teeth (r4_07). In the references the edge IS the ground: the same texture runs to the rim and breaks into big irregular lumps, 0.2-0.6 m, with wispy ink strokes; see the new owner reference `References/edgesSample.png` (turf-edge atlas). The edge body must reuse the ground's own texture (or be indistinguishable from it), and only the silhouette differs. The lumps must be 10x bigger.
- **A2 PASS (partly):** stone edges on stone and grass edges on grass (r4_02). Too fine to read at gameplay zoom.
- **A3 FAIL:** the foot edge has the same curb look (r4_01).
- **A4 PARTLY:**
  - Crisp, no smear, good scale.
  - But the columns are evenly spaced stripes that read as bark or wood planks. Needs chunky irregular columns, horizontal ledges and breaks, and darker AO toward the foot.
- **A5 FAIL:**
  - Straight dark grid lines cross the ground at regular intervals: across the road (r4_06, r4_02) and on grass (r4_03). Also faint dotted rectangles on grass. A leftover painterly/ink overlay or a tile-cell artefact; find and kill it.
  - Grass tufts are about 1 m wide and read as stamps; they should be half that size and less uniform.
- **A6 FAIL:**
  - The ramp-top hole is still there.
  - The ramp surface shows a straight seam line down its centre (r4_03).
  - The ramp top junction shows a ragged band.
- **A7 FAIL:** the grid lines above.
- **A8 untested:** the palette was not applied by the terrace build (TerraceDressing's `_EdgeTex1` = none). Only a manual `ApplyToTerraceWallMaterial` call wired it, so the reflection hook in `TerraceTileAssembly.ApplyPaletteToTerraceMaterial` does not reach `TerrainActivePalette`.
- **Tooling FAIL:** in the Editor, `Generate Vector Mock Textures` ran for more than 8 minutes on the main thread and froze Unity; the Editor had to be killed. It must finish in under 20 s (Burst/jobs, or Parallel.For over rows, or a smaller canvas with supersampling only on outlines).
