---
status: BUILDING 2026-11-27, wave 1 done. Replaces TerrainEdgeTiles_System.md section 8.1 PT2 (outline from the art) and finishes PT3 (rim/foot ink).
parent: Tasks/Plans/TerrainEdgeTiles_System.md (16-case dual grid, 2 m cells), Tasks/Plans/Terraces_System.md (levels, ramps, walls)
references: Tasks/Captures/References/4_dse_wendywillowforestfire_a.png, 3_dse_04_wilsongettingknockedoffcliff.png, edgesSample.png;
            Tasks/Captures/EdgeTiles_2026-11-25/10_cliff_profile_labelled.png (the target profile)
captures: Tasks/Captures/CliffEdges_<date>/NN_<what_it_shows>.png
names verified against: main at babe6b8d (2026-11-27)
---

# Terrace Cliff Edges: biome -> cliff -> biome on the edge-tile grid

## 0. What the owner wants (2026-11-27)

A cliff reads top to bottom like this, using the same tile system as ground transitions:

```
 plateau top biome (grass / forest / stone / dirt)
   | TRANSITION TILE: the top biome's ragged edge, drawn on the 2 m grid, into cliff rock
   | TOP BEVEL: rock rounds over
   | FACE: steep rock
   | FOOT BEVEL: rock flares out
   | TRANSITION TILE: the lower biome's ragged edge, laid over the cliff rock
 lower ground biome
```

- The cliff outline is **rounded** in plan. It is not jaggy. Capture 18/20 shows today's sawtooth walls, and that is the thing to kill.
- The cliff must **read the biome** at every point along it. Suppose a grass-to-forest border runs into a cliff. The rim is grass-over-cliff on one side, forest-over-cliff on the other, and the switch happens exactly where the biome border meets the rim. The same holds at the foot.
- Adding biome-over-cliff transition textures is expected and fine.

## 1. Why the current attempt is stuck (diagnosis, do not re-derive)

1. **The outline is traced from the art silhouette.** PT-D2 makes the art's lobes into the wall's plan shape, so every lobe becomes a wall corner and the walls come out sawtooth (capture 18). The lobes belong in the texture. The geometry should be a smooth curve.
2. **The ink is painted where no ground mesh draws.** PT3 paints rim/foot ink on the ground material within about 0.1-0.3 m of the contour. The wall strip owns that band (top fillet + foot batter, drawn by `PaintedCliff` on the `TerraceWalls` child), so the art lands under the wall and never shows (log 2026-11-27).
3. **There is one art set, with no biome awareness.** The terrace ink always samples `_GroundEdgeTileAtlasR` (grass) with seed 47, whatever biome is on top or below.
4. **There is no "cliff" in the transition.** The art draws biome over a lower biome. Nothing says "biome over rock", so even a visible rim would show grass-over-dirt colours.

## 2. The idea in one picture

Geometry and art are split. The geometry follows a **smooth case curve** on the 2 m corner grid. The art is the **16-case tile art**, laid over the bevels with top-down (XZ) projection. Both use the same grid and the same crossing points, so they always line up.

```
 plan view, one 2 m cell per box, corner levels from _TerraceCornerLevels

   1-----1-----0          geometry: per case, straight runs + 1 m-radius quarter arcs
   |  .--'     |                    through the side midpoints (G1-continuous, seam-safe)
   | /   case  |          art:      the same cell's case piece from the TOP biome's RIM set
   1|    7     0                    (top bevel + flat top) and the LOWER biome's FOOT set
   | \         |                    (foot bevel + lower ground), chosen per pixel by biome
   1--'--0-----0

 cross-section across the rim (plan distance from the case curve C)

   top flat  |<-- r_top -->|  face  |<-- r_foot -->|  lower ground
   ----------.__                                      
     ground  rim art   `-.  (bevels are GROUND mesh,  
     mesh    straddles   |   planar XZ, ground shader)
             C - r_top   |                            
                         |  FACE = TerraceWalls child, PaintedCliff
                          `-.__                       
                               `---- foot art straddles C + r_foot
```

## 3. Decisions (CE-D, final unless the owner overrides)

- **CE-D1 Smooth case contour for the geometry** (replaces PT-D2's silhouette field). New pure util
  `TerraceCaseContour` (`Assets/_Scripts/Utils/Terrain/`) returns, per 2 m cell and level threshold, the signed
  distance to an analytic curve:
  - edge cases (3, 6, 9, 12): a straight line through the two side midpoints;
  - one-corner and three-corner cases (1, 2, 4, 8, 7, 11, 13, 14): a quarter circle of radius 1 m centred on the
    odd corner, through both side midpoints, so it meets the neighbouring piece's tangent at the midpoint;
  - saddles (5, 10): two arcs, with the **inside connected** (the higher level wins). It must match the guide's
    diagonal piece.
  `TerraceEdgeTileOutline.BuildSignedDistanceFromEdgeTiles` is replaced by
  `BuildSignedDistanceFromCaseContour(data, threshold, neighbourLookup, samplesPerMetre)`, which has the same output
  layout. Marching squares, chains, nav wall mask and ramps run unchanged on it. `BuildCornerLevels` stays as the
  single source of corner levels (shader texture and CPU).
  **Risk:** a 45-degree staircase becomes a gentle S-wave (about 0.29 m amplitude). The C0 mock judges it. If
  it reads scalloped, the fallback is one Chaikin pass on the chains with the side-midpoint vertices pinned.
- **CE-D2 Bevels move to the ground mesh; only the face stays a wall.** `TerraceTileMeshBuilder` emits the top
  bevel (from C to C + r_top in plan, rounding down) and the foot bevel (from the face base out to C + r_top +
  r_foot) **into the ground mesh**, with world XZ as the shading position. uv0.z carries the level: the upper level
  on the top bevel, the lower level on the foot bevel. The ground shader then draws rim and foot art across the
  bevels with no extra mesh. The steep face stays on the `TerraceWalls` child with `PaintedCliff`. Defaults:
  `terraceTopRoundMetres` = `terraceFootRoundMetres` = 0.45 (today 0.2, too narrow to hold a 0.3 m lobe).
  `TerraceDressingBuilder` lip/foot strips and `PaintedCliff` fillet grass are switched off by a bool on
  `TerrainWorldProfile` (`terraceUseTileCliffEdges`, default true once C4 passes), then deleted in the C6 cleanup
  commit.
- **CE-D3 Two art sets per biome, not per pair.** "Biome over cliff" needs a **Rim** set (the biome's edge, rock
  outside) and a **Foot** set (the lower biome's edge, rock inside, used with the complement case). That makes 4
  biomes x 2 = 8 sets. Junctions such as grass/forest/cliff need **no** special art: CE-D5 composes them per pixel.
  Format: the existing 8 x 4 x 128 px atlas (R coverage, G rim light, B ink, case column c % 8, row c / 8 + 2v).
  Names: `T_EdgeAtlas_<Biome>Rim.png` and `T_EdgeAtlas_<Biome>Foot.png` in
  `Assets/PaintedGround/Textures/EdgeTiles/`.
- **CE-D4 Offset art, generated first, hand-drawn later.** The rim silhouette's mean line sits at C - r_top/2
  and the foot's at C + r_top + r_foot/2. So the sets are drawn **offset** from the side midpoint: they cross each
  side at 128 +- offset px, and validation allows for the offset (the mock-up already did this, section 8 of the
  parent). `Tools/terrain_edge_mock/export_transition_atlas.py --cliff <Biome>` derives both sets from that biome's
  ground edge art by distance-field offset of the coverage plus re-stamping its ink and flicks along the new
  silhouette. The owner can replace any derived PNG with a drawn one; the guide gets a section on it.
- **CE-D5 The cliff reads the biome per pixel** (the junction answer). The ground shader already paints Base(Dirt) ->
  R(Grass) -> G(Forest) -> B(Stone) with coverage `a` per pass. It now also keeps the **final weight of each layer**
  (`w_prev *= 1 - a; w_layer = a`, summing to 1). For a cell whose plateau case is 1-14:
  `rimCoverage = sum_i w_i * RimSet_i(case, cellLocal).r`, sampled only for layers with `w_i > 0.02`
  (at most 4 taps, usually 1-2), then cut at 0.5 with `fwidth` anti-aliasing.
  `col = lerp(rimRockColour, col, rimCut)`, and ink = the weighted rim ink. At a grass/forest border the silhouette
  morphs from grass lobes to forest needles inside the biome edge's own soft band, and the biome edge's ink line runs
  straight on across the bevel to the face. The foot uses the same rule with the Foot sets, the complement case at
  threshold myLevel + 1, and `footRockColour`.
  Every set crosses the cell sides at the same offset point (guide rule), so where neighbouring cells use different
  biomes their pieces still join.
- **CE-D6 One texture array, pushed globally.** An Editor menu `Stitch Punk/Terrain/Edge Tiles/Pack Cliff Edge
  Sets` packs the 8 PNGs into `Assets/PaintedGround/Generated/EdgeTiles/TerraceCliffEdges.asset` (Texture2DArray,
  slices Rim Dirt/Grass/Forest/Stone, then Foot in the same order). A missing PNG takes the Grass slice and logs a
  warning. `TerrainLightingBridge` gets `terraceCliffEdgeArray`, `rimRockColour`, `footRockColour` and pushes
  `_TerraceCliffEdges`, `_TerraceRimRock`, `_TerraceFootRock` as globals, beside the `_GroundEdgeTileAtlas*` ones.
  The strategic stand-in picks them up with no rebake. `TerraceEdgeTileInk` in `PaintedGround.shader` is replaced
  by `TerraceCliffEdge(...)`, which returns (col, ink, rim).
- **CE-D7 Variants stay on the shared integer hash** (`TerrainEdgeTileCases.VariantFor`). The seed per set is the
  biome's ground seed + 100 (rim) / + 200 (foot). The CPU no longer needs the variant, because geometry no longer
  depends on the art. The hash only has to agree between neighbouring pixels.
- **CE-D8 Ramps.** The builder treats corners inside a `TerraceRamp` footprint as the ramp's upper level for the rim
  threshold and the lower level for the foot. This means there is no cliff art across a ramp mouth. Ramp side walls
  keep their current dressing in v1.
- **CE-D9 Multi-level drops.** A 0 -> +2 drop is one taller face. The rim uses threshold myLevel and the foot uses
  myLevel + 1, so both fire correctly, and no new case is needed.
- **CE-D10 What does not change.** Level data, brushes, generation, nav (the wall mask follows the new chains),
  ground biome edge tiles, the wall face texture and uvs (TX-D18).

## 4. Execution protocol (observer pattern; token efficiency comes first)

One **orchestrator** session (Opus) runs the whole spec. It **does not write feature code**. It writes briefs, spawns
subagents, runs the gates and every Unity MCP step, judges the captures, commits, and keeps the log. Its own edits
are one-line fixes only. Subagents never touch Unity MCP.

**Roles**
- **`worker`, `model: sonnet`**: one task, **at most two named files**, hard cap 40 turns.
- **`verifier`, `model: haiku`**: greps, inventories and "does name X exist" checks. Nothing is judged here.
- **`verifier`, `model: sonnet`**: behaviour review of a worker diff against its brief, only when the diff is
  non-trivial (shader, mesh builder, generation).

**Brief template (every worker, kept under about 60 lines)**
1. The task id and the decision ids it builds (e.g. "C1: CE-D1"), with those decision paragraphs **pasted in**.
   Never say "read the spec".
2. The files to edit, named. The files to read, each given as a line range (grep the member, then read 40 lines
   either side). No whole-file reads over 300 lines.
3. The pinned signatures from section 5 that it must match, pasted in.
4. Conventions: no `var`, no single-letter names, comments only for a why, `[UpdateInGroup]` placement if a system
   is touched (none expected).
5. "Run `python Tools/devloop/devloop.py gate <assembly>` before writing your report. Exit 0 is the only pass; 2 is
   not a pass." Assemblies: `StitchPunk.Editor`, `StitchPunk.Utils`, `StitchPunk.Data`, `StitchPunk.MonoBehaviours`
   (check the real asmdef name with a haiku grep before the first wave).
6. "At turn 30 stop editing and write your report." The report is 30 lines or fewer: files changed, what it did not
   do, and the gate result.

**Per wave (orchestrator)**
1. `ListAgents` first. If a peer session is working on main, run the spec in a worktree
   (`feedback_worktree_when_peer_busy`).
2. Spawn all `[parallel-safe]` tasks of the wave in **one message**.
3. When they finish, run `git diff --stat`. A worker that hit its cap or a 429 is **never resumed**: read its diff and
   spawn a fresh worker with only what is left.
4. Run the gate once for the wave. Then do the Editor pass: `refresh_unity` -> wait for `isCompiling` false ->
   `read_console`. Run the touched EditMode fixtures only, never the full suite.
5. Rebake the touched tiles, then capture. Edit-mode `Camera.Render` draws no ground, so capture in Play mode with a
   temporary camera (`CopyFrom(Camera.main)`, FOV 15 for gameplay and 40 for close-ups, a RenderTexture). If Unity
   is unfocused or a dialog is up, use `Tools/devloop/unity_focus.ps1` / `unity_dialogs.ps1`. Save to
   `Tasks/Captures/CliffEdges_<date>/NN_<what_it_shows>.png`.
6. **Read every capture yourself against the references** (section 0). A worker's or verifier's "done" is a
   hypothesis. Any acceptance item that fails means another worker task, not a pass.
7. Commit by pathspec (never `git add -A`; check `git diff --cached` first, because a peer's staged files ride
   along). Add one log line to section 10.

**Wave plan**

| Wave | Tasks (each one worker unless noted) | Orchestrator after the wave |
|---|---|---|
| 1 | **C0** mock (`edge_tile_mock.py`) [parallel-safe] - **C1** contour (`TerraceCaseContour.cs` new, `TerraceEdgeTileOutline.cs`) + its EditMode fixture as a second worker [parallel-safe] - **C3a** rim/foot derivation (`export_transition_atlas.py`) [parallel-safe] | Run the exporter, look at the 8 PNGs and the C0 mock captures, post the mock captures to the owner, and **continue without waiting** (standing rule: unseen checkpoints pass). Run the C1 fixture. |
| 2 | **C2** bevels into the ground mesh (`TerraceTileMeshBuilder.cs`, `TerrainWorldProfile.cs`) - **C3b** pack menu (new Editor file) + bridge fields (`TerrainLightingBridge.cs`) [parallel-safe with C2] - **H3** along-cliff ramp (`TerraceRamp.cs`, `TerraceRampMeshBuilder.cs`) [parallel-safe] | Run Pack, rebake tile 0_0, capture plain rock bevels + an along-cliff ramp each way |
| 3 | **C4** shader (`PaintedGround.shader`, `PaintedGroundCore.hlsl`) - **H1** step kinds + heights (`TerrainTileData.cs`, the terrace height path) [parallel-safe] | Compile the shader variant (`ShaderUtil`), captures A1-A6 |
| 4 | **C5** ramp corners + seams (`TerraceEdgeTileOutline.cs`) - **H2** hill banks (`TerraceTileMeshBuilder.cs`; runs after C2 is committed) - **H6** caravan slope limit [parallel-safe] | Captures A7, H-A1..A3 (hills at 1.0 / 1.5 / 2.0 m side by side for HL-Q1); nav bake check H-A6; Play mode: drive the caravan at a hill, a Straight ramp and an AlongCliff ramp (H-A7) |
| 5 | **H4** multi-ramp generation (`TerrainGenerationProfile.cs`, the reachability pass) - **H5** brush kind toggle + ramp drag (terrace panel + ramp tool; UI: brief names `Docs/AnimationToolkit/EditorStyleGuide.md` R01-R22 and needs before/after captures at 177 pt) | Generate a 3 x 3, capture H-A4, H-A5; judge the panel capture |
| 6 | **C6a** delete the dressing lip/foot + fillet grass (`TerraceDressingBuilder.cs`, `PaintedCliff.shader`) - **C6b** guide section (`Docs/Terrain/TerrainEdgeTileGuide.md`) [parallel-safe] | Swap test A8, a full acceptance capture set, retire the spec to `Tasks/Verification/` with a `verify-terrace-cliff-edges.md` play checklist |

Two workers never edit the same file in one wave. `TerraceTileMeshBuilder.cs` is touched by C2 then H2, in
different waves.

**Look rounds.** Judge harshly against refs `4_dse_wendy`, `3_dse_04` and capture 10
(`feedback_terrain_vector_art_direction`). A failed item becomes a small worker task with the failing capture's
path in its brief. Never present a round as done while any item fails.

## 5. Pinned surfaces

```csharp
// Utils/Terrain/TerraceCaseContour.cs  (pure, EditMode-testable)
public static class TerraceCaseContour
{
    public const float CornerArcRadiusMetres = 1f;      // half a 2 m cell: the only radius that meets both side midpoints at right angles
    public static int CaseFor(int topLeftLevel, int topRightLevel, int bottomRightLevel, int bottomLeftLevel, int levelThreshold);
    public static float SignedDistanceInCell(int caseIndex, Vector2 cellLocalMetres);   // + inside (upper level), metres
}

// Editor/Terrain/Terraces/TerraceEdgeTileOutline.cs
public static float[] BuildSignedDistanceFromCaseContour(TerrainTileData data, int threshold,
    Func<Vector2Int, TerrainTileData> neighbourLookup, int samplesPerMetre);   // same layout as BuildSignedDistanceFromEdgeTiles

// Data/Terrain/TerrainWorldProfile.cs
public bool terraceUseTileCliffEdges = true;

// MonoBehaviours/Terrain/TerrainLightingBridge.cs
public Texture2DArray terraceCliffEdgeArray;
public Color rimRockColour;
public Color footRockColour;
```

## 6. Phases

| Phase | Work | Files | Check |
|---|---|---|---|
| **C0 mock** [parallel-safe] | Extend `Tools/terrain_edge_mock` to render the plan + gameplay-angle mock of one plateau: case-arc outline, derived Grass/Forest Rim + Foot sets, a grass/forest border running into the rim and on past the foot. Owner looks **before C1**. | `edge_tile_mock.py`, `export_transition_atlas.py` | captures 01-04: straight rim, outer + inner corner, 45-degree staircase, junction |
| **C1 contour** [parallel-safe] | CE-D1 util + new field builder, wired in place of the art field | `TerraceCaseContour.cs` (new), `TerraceEdgeTileOutline.cs` | EditMode: crossings at side midpoints for all 16 cases; both copies of a seam cell give equal distance |
| **C2 profile** | CE-D2 bevels into the ground mesh, level in uv0.z, profile flag, defaults 0.45 | `TerraceTileMeshBuilder.cs`, `TerrainWorldProfile.cs` | gate; capture of the plain rock bevels (no art yet) at gameplay FOV 15 |
| **C3 art + pack** [parallel-safe with C1] | CE-D4 derivation for the 4 biomes, CE-D6 pack menu + bridge fields | exporter, new Editor packer, `TerrainLightingBridge.cs` | 8 PNGs pass offset validation; array has 8 slices |
| **C4 shader** | CE-D5 weights + rim/foot passes, CE-D7 seeds | `PaintedGround.shader` (+ `PaintedGroundCore.hlsl` for a slice-aware tile sample) | `ShaderUtil` compile; captures A1-A6 |
| **C5 ramps + seams** | CE-D8, seam check at 0_0 / -1_0 | `TerraceEdgeTileOutline.cs` corner override | A7 |
| **C6 cleanup + guide** | Delete the dressing lip/foot + fillet grass paths; guide section "Cliff rim and foot sets" | `TerraceDressingBuilder.cs`, `PaintedCliff.shader`, `Docs/Terrain/TerrainEdgeTileGuide.md` | A8 swap test |

C0, C1 and C3 run in parallel. C2 needs C1. C4 needs C2 and C3.

## 7. Acceptance (judge captures against refs 4_dse_wendy, 3_dse_04 and capture 10, never against this text)

- A1 **Rounded in plan.** No sawtooth, and no wall corner sharper than the 1 m arc, along a generated plateau at FOV 15.
- A2 **Top transition shows.** Along every rim, the top biome's ragged edge sits over lighter rock on the bevel, then the face. No stretch of rim is bare.
- A3 **Foot transition shows.** The lower biome's ragged edge laps over darker rock at the foot.
- A4 **Biome aware.** Grass top gives a grass rim, forest gives a forest rim, stone gives a stone rim, and the same holds at the foot.
- A5 **Junction.** Where a grass/forest border meets the rim, the edge switches cleanly where the biome line meets the rim. There is no hole, no square, and no doubled ink.
- A6 **Aligned with the ground tiles.** A rim and a plain grass/dirt border beside it share the 2 m crossing points.
- A7 **Seams and ramps.** No step or gap at a tile seam. No cliff art across a ramp mouth.
- A8 **Swap test.** Edit one Rim PNG, Pack, and the scene changes as the guide says.

## 8. Hills and ramp kinds (owner 2026-11-27, do not re-ask)

Owner: not every plateau should be a tall cliff. **Short steps should be small hills you can walk or drive up.** The
game is built around driving the caravan, so their edge spreads outward into a gentle bank instead of a wall. Hills
stack. Tall plateaus get **more ramp kinds**: the straight one stays, and a new one runs **along the cliff side,
rising either way**. A **big** plateau gets **two or three ramps** spread around it, so units have several ways in
and out.

- **HL-D1 Every level step has a kind: Cliff or Hill.** `TerrainTileData` gains `baseTerraceStepKinds` and
  `handTerraceStepKinds` (`byte[100*100]`, 255 = no hand override). They work the same way as the level arrays.
  Bit k-1 is step +k and bit k+2 is step -k (k = 1..3); a set bit means Hill. Cell height = height layers +
  the sum of the step heights from level 0 to the cell's level. A Cliff step is `terraceLevelHeightMetres` (3) and a
  Hill step is `hillStepHeightMetres` (default 1.5). Within one connected region at one level a step's kind must be
  uniform, or a flat top would split in height: the builder takes the majority per region and logs any fix it makes.
- **HL-D2 A hill edge is a bank in the ground mesh, not a cut.** Hill steps get no wall, no face, no marching-squares
  cut and no nav wall mask. Each vertex adds `hillStepHeight * smoothstep(0, 1, (sd + hillBankMetres) / hillBankMetres)`.
  Here `sd` is the CE-D1 case-contour signed distance for that step, so the bank starts at the contour and runs
  **outward** with rounded top and foot. The bank width is **derived from the caravan limit** (HL-D9):
  `hillBankMetres = 1.5 * hillStepHeight / tan(CaravanMaxSlopeDegrees)`, because smoothstep peaks at 1.5x the mean
  slope. A 1.5 m hill gets an 8.4 m bank at 15 degrees, so every hill is caravan-drivable by construction. Bank cells subdivide to
  0.5 m. Stacked hills add their banks. If two banks overlap past 25 degrees, the builder warns and names the cell.
- **HL-D3 Hill banks are ground, not rock.** The top biome continues down the bank under normal lighting. No Rim or
  Foot set and no cliff rock. Biome edge tiles run over the bank like flat ground.
- **HL-D4 Mixed stacks.** A cliff may sit on a hill top and a hill on a cliff top. A cliff whose foot lands on a bank
  keeps a vertical face over sloped ground (TX-D12 already allows it). A single connected outline can be part cliff,
  part hill: per edge, the kind of the upper side's step decides. Where a cliff run meets a hill run along one
  outline, the face tapers to zero height over the last 2 m cell.
- **HL-D5 Ramp kinds.** `TerraceRamp` gains `TerraceRampKind kind` (`Straight`, `AlongCliff`) and
  `bool risesToTheRight` (seen from the lower ground, facing the cliff).
  - **Straight** is today's ramp.
  - **AlongCliff** lies parallel to the face, built out onto the lower ground. Its inner long side is the cliff face,
    so it needs no wall there. Its outer long side gets a ramp side wall with the CE rim/foot art. Its top end
    notches into the plateau; corners in the notch follow CE-D8.
  - **Placement:** v1 needs a straight-ish stretch of outline (chord deviation <= 1 m) of length run + 4 m. Curved
    along-cliff ramps are v2.
  - **Size and who can use it** (HL-D9):
    - Straight ramps are caravan ramps: run 12 m for a 3 m step (about 14 degrees), width 6 m.
    - AlongCliff ramps are foot-only by default: run 9 m (about 18 degrees), width 4 m. A designer can make one
      caravan-grade by lengthening its run until the slope is at or under the limit.
- **HL-D6 Ramp count from size.** The TX-D14 reachability pass places
  `clamp(round(outlinePerimeterMetres / rampSpacingMetres), 1, maxRampsPerRegion)` ramps per cliff region
  (`rampSpacingMetres` 90, `maxRampsPerRegion` 3, both on `TerraceBiomeRule`). It spreads them by greedy
  farthest-point along the outline, so they land on different sides. It picks each kind by the rule's
  `straightRampWeight` / `alongCliffRampWeight` and the side by open lower ground (no water, no other cliff).
  Hand-placed ramps count toward the total. **At least one ramp per cliff region is caravan-grade** (a Straight
  ramp at or under `CaravanMaxSlopeDegrees`). If no stretch fits one, the region is culled to a Hill step instead.
  A region with a Hill edge to its surroundings is already reachable, so it needs no ramp.
- **HL-D7 Generation per biome.** `TerraceBiomeRule` gains `[Range(0,1)] hillChance` (chance a generated step is a
  Hill), plus the ramp fields above. Grassland leans to hills and stone to cliffs; the defaults go in the spec log
  once played.
- **HL-D9 Caravan slope limit** (owner 2026-11-27: "caravan can only go up not so steep ones"). Today nothing in
  `CaravanDriveSystem` limits slope; only the bumper wall cast stops it. Add `CaravanMaxSlopeDegrees` (default 15,
  one constant beside the caravan drive tuning). The caravan refuses to drive forward onto ground whose slope along
  its heading exceeds the limit: it stops as at a wall, with no slide. Units on foot keep the 25-degree
  `HeavySlopeDegrees` rule. Ramps get a derived `isCaravanGrade` (`atan(rise / run) <= CaravanMaxSlopeDegrees`),
  and caravan pathing (if a route planner reads nav) treats a non-caravan-grade ramp as blocked.
- **HL-D8 Tools.** The Plateau and Cliff brushes get a kind toggle, Cliff or Hill (icon buttons, still 177 pt wide).
  Ramp placement:
  - drag across an edge for Straight (as today);
  - drag along the cliff foot for AlongCliff, where the drag direction is uphill;
  - previews live while dragging.

| Phase | Work | Files |
|---|---|---|
| H1 data + heights | HL-D1 arrays, step heights, region uniformity | `TerrainTileData.cs`, `TerrainWorldProfile.cs`, terrace height path |
| H2 hill banks | HL-D2/D3/D4 in the ground mesh, nav slope only | `TerraceTileMeshBuilder.cs`, `TerraceEdgeTileOutline.cs` |
| H3 along-cliff ramp | HL-D5 mesh + footprint + notch | `TerraceRamp.cs`, `TerraceRampMeshBuilder.cs` |
| H4 multi-ramp generation | HL-D6/D7 | `TerrainGenerationProfile.cs`, the reachability pass |
| H5 tools | HL-D8 | terrace brush panel + ramp tool |
| H6 caravan slope limit | HL-D9 | `CaravanDriveSystem.cs` + the caravan tuning file (one worker; Burst rules apply) |

H1 comes after C1, because it uses the case contour. H3 is parallel-safe with C2-C4. H4 needs H3.

Acceptance:
- H-A1 A 1.5 m hill reads as a soft rounded bank with no rock, and the caravan drives up it anywhere.
- H-A7 The caravan stops at the foot of a foot-only AlongCliff ramp and at any slope over 15 degrees, and climbs every Straight ramp.
- H-A2 A hill on a hill, and a cliff on a hill top, both build with no gaps and no spikes.
- H-A3 An outline that changes from cliff to hill tapers cleanly.
- H-A4 An along-cliff ramp rises each way, its outer side wall carries the rim art, and units and the caravan use it.
- H-A5 A generated plateau with a perimeter over 200 m gets 2-3 ramps on different sides; a small one gets 1.
- H-A6 The nav bake shows hill banks as normal slope, not wall.

## 9. Open questions (answered 2026-11-27)

- CE-Q1 Bevel widths: default 0.45 m top and foot.
- CE-Q2 Rock showing in the bays between rim lobes on the flat top: default yes (it reads as the lip).
- CE-Q3 **Answered 2026-11-27: no drapes for now.** Turf hanging down the face is out of scope; do not build it.
- CE-Q4 Derived sets first, drawn sets later: default yes.
- HL-Q1 **Owner unsure (2026-11-27).** Start at a 1.5 m step. The bank width is derived, not chosen (HL-D2). Both are profile fields; the wave 4 capture shows 1.0 / 1.5 / 2.0 m side by side so the owner can pick by eye.
- HL-Q2 **Answered 2026-11-27: no.** The caravan only climbs gentle slopes. See HL-D9.

## 10. Log (one line per wave, newest last)

- 2026-11-27 Owner added hills, ramp kinds and multiple ramps (section 8); drapes asked for then removed; hill height unsure (HL-Q1 compares by capture); caravan only climbs gentle slopes (HL-D9); observer protocol added (section 4).
- 2026-11-27 Spec written after the PT3 session stalled. Diagnosis in section 1. Uncommitted work from that session
  (edge atlas PNGs staged, RandomWorld tiles modified) was left as is.
- 2026-11-27 Wave 1: C1 case contour + fixture (proved to fail without the +-1 m clamp; Editor asmdef now refs Utils); C3a 8 Rim/Foot sets by analytic-normal transfer (rim crossings exact, foot single-corner cases 1/2/4/8 cross ~0.12 m short, fix rides wave 2); C0 mock captures 01-04 in Captures/CliffEdges_2026-11-27 posted to owner, judged: rounded outline, lip over light rim, foot lapping, junction clean.
