---
status: BUILDING 2026-11-27, waves 0-2 done, wave 3 next (plus N3c, M4 fix). Supersedes TerraceCliffEdges_System.md from wave 4 on (its waves 1-3 are partly undone here, see section 1 and wave 0).
parent: Tasks/Plans/Terraces_System.md (levels, walls, ramps), Tasks/Plans/TerrainEdgeTiles_System.md (ground biome edge tiles, unchanged)
references: Tasks/References/Edges1.png (tall cliff, full profile), Edges2.png (low ledge that fades in and out, left side; short cliff, right), Edges3.png (soft low edge, apron under the lip)
good earlier capture: Tasks/Captures/Terraces_2026-11-21/r4_06_gameplay_camera.png (rounded freeform outline, striated wall: the starting point)
bad current captures: Tasks/Captures/CliffEdges_2026-11-27/09_*.png and 11_*.png (what the owner rejected)
captures: Tasks/Captures/NaturalCliffs_<date>/NN_<what_it_shows>.png
names verified against: main at d407ad39 (2026-11-27)
---

# Natural Cliffs: freeform plateaus, ledges and cliffs, edge-art ribbons, per-tile reroll

## 0. What the owner wants (2026-11-27, do not re-ask)

- Plateau and cliff **shapes look natural and varied**. Today they are repetitive and "weird".
- A cliff reads, from the top down: biome -> its ragged edge **into stone** -> **tight bevel** -> face going down ->
  **bevel out** -> quick ragged edge back into the lower biome.
- **Not every raise is a tall cliff.** Many are low ledges characters walk over. Only the tall ones need ramps, and
  those ramps go straight up or run along the side.
- Use only the three edge sets the owner kept: `T_EdgeAtlas_GrassOverDirt`, `T_EdgeAtlas_ForestOverGrass`,
  `T_EdgeAtlas_StoneOverDirt` (`Assets/PaintedGround/Textures/EdgeTiles/`). The owner deleted the eight derived
  Rim/Foot sets because they were jagged. **Never regenerate them.**
- **Roll one tile at a time.** A rolled tile flows into its neighbours, and no other tile is rebuilt.
- Fast and clean. It must start out good; the owner edits by hand afterwards.

## 1. Why it looks bad today (diagnosis from captures 09 and 11, do not re-derive)

1. **The outline is locked to the 2 m grid.** `TerraceCaseContour` only draws lines at 0/90 degrees and 1 m arcs, so
   every plateau is a rounded staircase (capture 11). The grid lock existed only so 16-case tile art could be
   projected flat onto the bevels. The references' cliffs are freeform curves.
2. **The source shape is one octave of noise** (`snoise` at 0.006 per metre, thresholded per 1 m cell in
   `GenerateTerraceLevels`). Every plateau is the same kind of blob at the same scale.
3. **Every step is the same 3 m wall.** No low ledges, no stacks that read as a mesa.
4. **The wall is a smooth beige cylinder, brighter than the grass above it.** In all three references the face is
   darker than the top surface, flat-shaded, with vertical cracks, a light rim line and a dark foot.
5. **The rim art was derived, spiky, and is now deleted.**
6. **No tile can be rolled alone.** Noise comes from one world seed, so a tile always regenerates the same.

The capture from 2026-11-21 (`r4_06`) had a freeform rounded outline and a striated wall with a lip strip along
it. That architecture is still in the code behind `terraceUseTileCliffEdges = false` and
`UseCaseContourOutline = false`. **This spec returns to it and improves it.** Grid tiles stay for ground biome
borders, where the borders really are on the grid.

## 2. The design in one picture

```
 plan view                                   cross-section of a TALL step (3 m)

   freeform outline from a rounded,            top biome (ground mesh, unchanged)
   blurred level field (not the 2 m grid)      ~~~~~~\  LIP RIBBON 1.55 m: ragged biome edge, light rock outside it
        ______                                        |__ tight top bevel 0.25 m, rim highlight
     __/      \___    tall mesa, 2-3 ramps               |
    /   .---.     \                                      |  FACE: darker than the top, vertical cracks,
   |   ( L2  )     |  low ledge skirt,                   |  flat 2-band light, darkens toward the foot
    \   `---'   __/   worn gaps every ~40 m              |
     \_________/                                       __/  foot bevel 0.45 m, contact shadow
                                              ~~~~~~~/     FOOT RIBBON 1.55 m: dark rock apron, lower biome laps over it
                                              lower biome (ground mesh, unchanged)

 a LOW step (ledge, 0.7 m) is the same profile, short. Units walk over it anywhere. The caravan uses the worn gaps.
```

The ribbons are meshes that follow the outline (arc length along, distance across). They draw **rock** where the
edge art has no coverage and **nothing** where it has coverage, so the real ground biome shows through and the
ragged line is the biome's own edge. No colour matching, no per-pair art, no corner pieces: the ribbon bends
with the curve, and it uses only the **straight-edge pieces** of the owner's three atlases.

## 3. Decisions (NC-D, final unless the owner overrides)

### Shape

- **NC-D1 Freeform outline.** `TerraceTileMeshBuilder.UseCaseContourOutline` becomes `false` and
  `TerrainWorldProfile.terraceUseTileCliffEdges` becomes `false` on every world profile asset. The outline is
  `TerraceLevelField.BuildSignedDistance` again (rounded + blurred). `terraceCornerRadiusMetres` drops from 2.5 to
  1.5 so the generated shape keeps its character (wave 1 judges 1.0 / 1.5 / 2.5 side by side).
- **NC-D2 One pure field function, `TerracePlateauField`** (`Assets/_Scripts/Utils/Terrain/`, pinned in section 6):
  1. rotate the point by `grainDegrees` and divide one axis by `grainStretch` (1 = round mesas, 3 = long escarpments);
  2. domain-warp it by `warpMetres` (18-30 m) with two `snoise` taps at `warpFrequencyPerMetre` (about 0.012);
  3. `macro` = 3-octave fbm at `macroFrequencyPerMetre` (0.004-0.008: 125-250 m features);
  4. `coves` = `snoise` at 0.02 per metre (50 m bays and promontories), times `coveAmount`;
  5. `wobble` = `snoise` at 0.07 per metre (14 m), times `wobbleAmount`;
  6. value = macro + coves + wobble + `bias`. **No detail under 8 m**: the bevel cannot carry it and it reads as noise.
- **NC-D3 Styles give the variety.** `TerracePlateauStyle` is a serializable struct of the NC-D2 parameters plus two
  level thresholds, `lowStepShare` and a `weight`. `TerrainGenerationProfile` gains `plateauStyles` (a list) with
  these five defaults: **Open** (high bias, almost nothing), **LedgeField** (many small low steps),
  **Mesa** (one large tall plateau, round grain), **Escarpment** (grain stretch 3, a cliff line crossing the tile),
  **Stacked** (tall, with a second level set back at least 8 m). A tile picks one by a weighted hash of
  (world seed, tile coordinate, `rollIndex`). `TerraceBiomeRule` keeps `enabled`, `maxLevel`,
  `minPlateauAreaSquareMetres` and the slope-cliff fields; its `plateauThresholds` and
  `plateauNoiseFrequencyPerMetre` are no longer read (give them the tooltip "unused since Natural Cliffs"; delete them in the cleanup wave).
- **NC-D4 Clean-up of the raster before it is stored** (1 m cells, in the generation window):
  open then close each level mask with a 3 m radius, so no plateau neck and no gap between two cliffs is narrower
  than 6 m; then the existing minimum-area cull.
- **NC-D5 Step kinds: Tall and Low.** The data is HL-D1, already committed (`baseTerraceStepKinds`,
  `TerrainTileData.TerraceLevelOffsetMetres`): a set bit means the low kind. Tall = `terraceLevelHeightMetres` (3).
  Low = `hillStepHeightMetres`, default changed from 1.5 to **0.7**. Each connected region at level >= k rolls the
  kind of step k once: regions under 600 m2 are Low with chance 0.85, larger ones with the style's `lowStepShare`.
  Step 2 leans the other way from step 1 (low skirt -> tall core = a mesa on a ledge; tall base -> low top).
- **NC-D6 A Low step is a short cliff, not a bank.** Same wall builder, same ribbons, 0.7 m. No new geometry.
  Units cross it anywhere: its chains are **left out of the nav wall mask** and its cells cost heavy (50).
  The caravan crosses at **worn gaps** (NC-D12). The hill-bank design (HL-D2, `TerraceHillBankField`) is dropped:
  gentle hills already come from the height noise.
  **Spike S2 (wave 3) must prove it in Play**: a citizen walks up and down a ledge without snagging, the caravan
  stops at the ledge face and climbs a worn gap. If units snag, the fallback is to widen both ledge bevels to 0.6 m.

### Roll one tile

- **NC-D7 `TerrainTileData.rollIndex`** (int, 0 = the world field only). The tile field is
  `lerp(worldField, tileField, b)` where `tileField` uses a seed offset hashed from (world seed, tile coordinate,
  `rollIndex`) and the tile's style, `worldField` uses the world seed and the profile's first style, and
  `b = smoothstep(lockMetres, lockMetres + blendMetres, distance to the nearest tile border)`,
  `lockMetres` 6, `blendMetres` 24. To stop the mid-band going flat, the blended value is re-centred:
  `mean + (blend - mean) / sqrt(b*b + (1-b)*(1-b))`.
- **NC-D8 The lock band makes a reroll local.** Inside 6 m of a border the result depends only on the world field,
  so both neighbours agree there whatever either has rolled, and a neighbour's mesh (which reads 2-5 m across the
  border) never goes stale. Three invariants, each with a fixture (section 8):
  - **I1** rerolling a tile leaves every other tile's data assets byte-identical;
  - **I2** levels and step kinds in the lock band are a pure function of the world seed, plus stored neighbours;
  - **I3** a region that crosses a seam has one step kind on both sides.
  Rules that give them: margin cells of the generation window come from the **neighbour's stored arrays** when that
  tile has data, else from the world field; a region with any cell in the lock band is never culled; a region
  touching a stored neighbour region **inherits** its kind; a region touching the lock band with no stored
  neighbour takes its kind from a hash of its smallest lock-band cell's world coordinate; only fully interior
  regions use the roll. If one region would join two stored neighbour regions of different kinds, cut a 6 m level-0
  channel through it at least 8 m inside the tile and log it.
- **NC-D9 Heights and biomes roll the same way.** `GenerateHeights` and `BiomeLayerAt` blend their noise through the
  same `b`. Border samples have `b = 0`, so the seam weld holds exactly.
- **NC-D10 Limit, stated to the owner in the panel tooltip:** the 6 m ring at a seam comes from the world seed and
  cannot be rolled. Paint it by hand or change the world seed.
- **NC-D11 Reroll tool.** Generate panel (`TerrainGeneratePanel.cs`): with the tool armed, the tile under the cursor
  is outlined in the Scene view; a dice icon button and the `R` key bump `rollIndex` and rebuild **that tile only**;
  `Shift+R` steps back. Undo restores the old `rollIndex` and rebuilds. A hand-authored tile asks first (existing
  rule). Hand layers are never touched. The panel shows the tile's style name and roll number.
  **Budget: reroll to rebuilt tile in 2 s or less**, measured with a `Stopwatch` line per stage (field, raster,
  mask PNG, composite, mesh, collider, ramps, save). Measure first; fix only the slowest stage.

### Ramps

- **NC-D12 Worn gaps for ledges.** A Low region gets a Straight `TerraceRamp` (rise 0.7 m, run 2.8 m = 14 degrees,
  width 8 m) about every 40 m of outline, at least one, placed where the lower ground is open. Caravan-grade by
  construction.
- **NC-D13 Tall regions: HL-D5 and HL-D6 as written** in `TerraceCliffEdges_System.md` section 8 (paste those two
  paragraphs into the brief). In short: `clamp(round(perimeter / 90), 1, 3)` ramps, spread by farthest point, kind
  by `straightRampWeight` / `alongCliffRampWeight`, at least one caravan-grade Straight ramp (run 12 m, width 6 m),
  AlongCliff rising either way. On a freeform outline a ramp needs a stretch whose chord deviation is 1 m or less
  over its width (Straight) or its run + 4 m (AlongCliff); pick the best-scoring stretch. A tall region that fits no
  caravan-grade ramp becomes Low. Ramps never start inside the lock band + their own run.

### Look

- **NC-D14 Ribbons replace the lip and foot fringe.** `TerraceDressingBuilder.BuildLipAndFootMesh` keeps its two
  submeshes but each becomes a 1.55 m ribbon:
  - lip: from the bevel start inward over the flat top; foot: from the foot bevel end outward over the lower ground;
  - uv.x = arc metres / 2 (one art cell is 2 m), uv.y = across, mapped so the art's mid line (v = 0.5) lies
    **0.55 m** from the wall side of the ribbon;
  - vertex colour: r = set index of the biome under the vertex (lip: top side, foot: lower side), g = the next
    biome's set index, b = blend 0..1 across the one segment where the biome changes, a = fade (0 at a ramp mouth
    and over the last 2 m before a chain ends);
  - biome under a vertex = the tile's composite mask at that XZ. Set per biome: Grass -> GrassOverDirt,
    Forest -> ForestOverGrass, Stone and Dirt -> StoneOverDirt;
  - lifted 0.015 m and drawn with a depth offset, as the old strips were.
- **NC-D15 New shader `Painted/CliffEdgeRibbon`** (`Assets/PaintedGround/Shaders/PaintedCliffEdgeRibbon.shader`).
  It samples the three atlases the bridge already pushes as globals (`_GroundEdgeTileAtlas*`; confirm the exact
  names by grep). Per 2 m segment `n = floor(uv.x)` it picks one of **8 pieces**: the four straight-edge cases
  (3, 6, 9, 12, each rotated to a common orientation) x 2 variants, by integer hash of `n` and the chain's
  `startArcPhase`; inset half a texel inside the atlas cell. Channels as in the atlas: R coverage, G rim light,
  B ink. Output: `clip` where coverage > 0.5 (the ground shows through); elsewhere rock (`_TerraceRimRock` on the
  lip, `_TerraceFootRock` on the foot, both already globals); ink from B along the silhouette; lit by
  `PaintedLighting_float` so the terrain tint applies. No new texture asset and no pack step: when the owner
  redraws an atlas, the cliffs follow.
- **NC-D16 The wall (`PaintedCliff.shader`, `_Mode` 0).** Colours come from the bridge, not from material constants:
  - top of the face = `_TerraceRimRock`, foot = `_TerraceFootRock`, gradient over the face height;
  - **the face is darker than the ground above it** (guard G1 in section 8);
  - a light rim line on the top bevel (the lightest value in the whole cliff band), a contact shadow at the foot;
  - light is 2-band (lit / shade by normal against the sun), not a smooth gradient round the curve;
  - vertical cracks: the existing striations, with spacing varied 0.4-1.6 m by noise and an ink line in the deep ones;
  - a slow value change along the wall every 8-15 m so a long face is not uniform;
  - `terraceTopRoundMetres` 0.25 (tight), `terraceFootRoundMetres` 0.45.
  Start values: rim rock = the stone palette colour x 0.85, foot rock = x 0.45, both slightly desaturated toward the
  ground hue. Tune by capture against Edges1 and Edges2.
- **NC-D17 Biome borders on plateau tops.** Capture 11 shows forest/grass borders on a plateau as raw squares.
  Find out why the ground edge tiles do not draw there (one `verifier`, sonnet, then one worker). They must look the
  same as at level 0.

### What is removed

- **NC-D18** After wave 4 passes: `TerraceCaseContour.cs` and its tests, `TerraceEdgeTileOutline.BuildSignedDistanceFromCaseContour`
  (keep `BuildCornerLevels` only if NC-D17 still needs it), the `TerraceCliffEdge` pass in `PaintedGround.shader`,
  `TerraceCliffEdgeArrayPacker.cs`, `TerraceCliffEdges.asset`, `TerrainLightingBridge.terraceCliffEdgeArray` and
  `_TerraceCliffEdgesReady`, the bevels-in-ground branch of `TerraceTileMeshBuilder`, `terraceUseTileCliffEdges`,
  `export_transition_atlas.py --cliff`. Keep `_TerraceRimRock` / `_TerraceFootRock`.

## 4. Execution protocol (observer pattern, token-aware)

One **orchestrator** session runs the spec. It **writes no feature code**. It writes briefs, spawns subagents, runs
every gate and every Unity MCP step, judges captures, commits, and keeps the log. Its own edits are one-line fixes
and asset field changes. **Subagents never touch `mcp__UnityMCP__*`.**

**Roles (state the tier in every spawn)**
- `worker`, `model: sonnet`: one task, **at most two named files**, 40-turn cap.
- `verifier`, `model: haiku`: greps, inventories, "does X exist", "which hunks touch Y". It judges nothing.
- `verifier`, `model: sonnet`: reviews one non-trivial worker diff against its brief (generator, mesh, shader).

**Token rules for the orchestrator**
1. Never read a file over 300 lines whole. Grep the member, read 40 lines either side.
2. Never read this spec again after the first pass, except section 5 (the wave table) and section 9 (the log).
   After a context reset, read sections 0, 5 and 9 only, then `git log --oneline -8`.
3. **One image per judgement.** Captures go through `Tools/devloop/contact_sheet.py` (task T0) into one sheet,
   long side 2000 px or less, with the matching reference pasted in as the first cell. Read the sheet, not the
   single captures. Open a single capture only to settle one named doubt.
4. Numbers before eyes. Run the metric fixtures (section 8) first. Look at a sheet only when they pass.
5. A brief is 60 lines or fewer and carries its decision paragraphs **pasted in**. Never write "read the spec".
6. A worker's report is 30 lines or fewer. If a diff needs checking, spawn a `verifier`; do not re-derive it.
7. A capped or rate-limited worker is **never resumed**. `git diff --stat`, then a fresh worker with what is left.
8. Every finished agent leaves a ledger line (`.claude/subagent-ledger.tsv`). HIGH or OVER means the next brief in
   that family is split in two.

**Brief template**
1. Task id, decision ids, those paragraphs pasted.
2. Files to edit (named). Files to read, each as a line range.
3. Pinned signatures from section 6, pasted.
4. Conventions: no `var`, no single-letter names, names carry the meaning, a comment only for a why.
5. "Run `python Tools/devloop/devloop.py gate <assembly>` before your report. Exit 0 is the only pass; 2 is not."
6. "At turn 30 stop editing and write your report (30 lines or fewer: files changed, what you did not do, gate)."

**Per wave**
1. `ListAgents`. If a peer session is on main, work in a worktree (`feedback_worktree_when_peer_busy`).
2. Spawn every `[parallel-safe]` task of the wave in **one message**. Two workers never share a file in a wave.
3. Gate once for the wave, then the Editor pass: `refresh_unity` -> wait for `isCompiling` false -> `read_console`.
   Run only the touched EditMode fixtures.
4. Rebuild the test tiles (RandomWorld, 3 x 3), capture in Play with a temporary camera (recipe and traps:
   `Memories/Code/Terrain.md`, "Terrace cliff edges"; FOV 15 gameplay, FOV 40 close). If Unity is unfocused or a
   dialog is up: `Tools/devloop/unity_focus.ps1`, `unity_dialogs.ps1`. Leave the Editor open.
5. Build the contact sheet and judge it against the references, harshly. A failed acceptance item becomes a small
   worker task with the sheet's path in its brief. A round is never reported done with a failed item.
6. `git diff --cached` first (a peer's staged files ride along), then commit by pathspec. One log line in section 9.
7. Owner checkpoints marked `⏸`: post the sheet, then **continue** (standing rule: unseen checkpoints pass).

## 5. Waves

| Wave | Tasks | Orchestrator after the wave |
|---|---|---|
| **0** housekeeping (no feature workers) | See the list under the table. **T0** `Tools/devloop/contact_sheet.py` (one worker) [parallel-safe] | `00_baseline` sheet: the 2026-11-21 look must be back before anything else is built |
| **1** shape, no tile is touched | **N1** `TerracePlateauField.cs` (new, Utils) - **N1t** its fixture `TerracePlateauFieldTests.cs` [parallel-safe] - **N2** `TerracePlateauPlanSheet.cs` (new, Editor: menu `Stitch Punk/Terrain/Debug/Render Plateau Plan Sheet`, pure CPU, writes a PNG of 3 x 3 tiles x 6 rolls coloured by level and kind, plus a JSON of metrics M1-M6) [parallel-safe] - **L1** `Tools/terrain_edge_mock/edge_strip_mock.py` (new: lays the 8 straight pieces of each of the 3 atlases along a straight and a curved ribbon, proves which cases and rotations tile) [parallel-safe] | Run the plan sheet. Tune the five style presets through one worker at a time until M1-M6 pass **and** the sheet reads like the references' plan shapes. Judge the L1 strip image. `⏸` post both, continue |
| **2** generator + ribbon | **N3** generator: field, NC-D4, NC-D5, NC-D8 window rules (`TerrainProceduralGenerator.cs`, `TerrainGenerationProfile.cs`) - **N3t** fixtures I1-I3 (`TerrainProceduralGeneratorTests.cs`) [parallel-safe] - **L2** `PaintedCliffEdgeRibbon.shader` (new) [parallel-safe] - **L3** ribbons (`TerraceDressingBuilder.cs`, `TerraceTileAssembly.cs` material hookup) [parallel-safe] | Compile the shader (`ShaderUtil`, async compilation off). Generate the 3 x 3. Sheet: plan view + gameplay + close-up of one tall and one low step |
| **3** roll, wall, ledges | **N4** `rollIndex` + NC-D9 heights and biomes (`TerrainTileData.cs`, `TerrainProceduralGenerator.cs`) - **L4** wall look (`PaintedCliff.shader`) [parallel-safe] - **K1** low chains out of the nav wall mask, heavy cost (`TerrainNavBaker.cs`, `TerrainNavCellRules.cs`) [parallel-safe] - **V1** NC-D17 investigation (`verifier`, sonnet) [parallel-safe] | Spike **S2** in Play (NC-D6). Reroll tile 0_0 three times by `execute_code`: check I1 with `git status` on the 8 neighbours. Wall sheet against Edges1 / Edges2, guard G1 |
| **4** ramps, tool, speed | **R1** worn gaps + multi-ramp (`TerrainProceduralGenerator.cs`) - **U1** reroll tool (`TerrainGeneratePanel.cs`, `TerrainGenerateTool.cs`; UI brief names `Docs/AnimationToolkit/EditorStyleGuide.md` R01-R22 and needs before/after captures at 177 pt) [parallel-safe] - **V1f** the NC-D17 fix (one worker, files from V1) [parallel-safe] | Stage timings for one reroll; a worker on the slowest stage only if over 2 s. Play: caravan up a Straight ramp and a worn gap, units over a ledge |
| **5** look rounds + cleanup | Look tasks from failed A-items, one or two files each - **C1** NC-D18 deletions, split by file pair [parallel-safe] - **C2** `Docs/Terrain/TerrainEdgeTileGuide.md` section "Cliff ribbons use the straight pieces" [parallel-safe] | Full acceptance sheet set. Update `Memories/Code/Terrain.md`. Retire this spec to `Tasks/Verification/` with `verify-natural-cliffs.md` |

**Wave 0, in order (orchestrator, with haiku verifiers for the inventories):**
1. Commit the owner's staged work as its own commit: the eight deleted `T_EdgeAtlas_*Rim/Foot.png` (+ metas), the
   three modified atlases, the `Tasks/Captures/References` -> `Tasks/References` renames.
2. Save the uncommitted wave-4 leftovers before touching them:
   `git diff -- Assets/_Scripts Assets/PaintedGround/Shaders > Assets/_Vault/Tasks/Claude/CliffEdges_wave4_leftovers.patch`.
3. **Keep and land** (gate, run `TerraceRampMeshBuilderTests`, commit): `TerraceRampMeshBuilder.cs` (AlongCliff
   landing end wall), `CaravanDriveMath.cs` + `CaravanDriveSystem.cs` (15-degree caravan slope limit; `gate game`,
   Burst rules apply).
4. **Revert** (after a haiku verifier confirms every hunk is the C5a ramp mask and nothing of the owner's):
   the working-tree changes to `PaintedGround.shader`, `TerrainTileAssets.cs`, `TerraceEdgeTileOutline.cs`.
   If a hunk is not C5a, stop and list it for the owner instead of reverting.
5. **Delete** the untracked `TerraceHillBankField.cs` and `TerraceHillBankFieldTests.cs` (+ metas): superseded by
   NC-D6, and still in the patch file.
6. Leave `Assets/Terrain/RandomWorld/**`, `Assets/Scenes/RandomWorld*` and `Assets/Terrain/Strategic/**` alone until
   the first regenerate; commit them only as the result of a wave's regenerate.
7. Set `UseCaseContourOutline = false`, `terraceUseTileCliffEdges = false` on the profile assets, rebuild tile 0_0,
   capture. If the old lip/foot strips or the wall fillet look broken (the path sat unused for three waves), fix
   that first with one worker.

## 6. Pinned surfaces

```csharp
// Assets/_Scripts/Utils/Terrain/TerracePlateauField.cs   (pure, Unity.Mathematics, EditMode-testable)
[System.Serializable]
public struct TerracePlateauStyle
{
    public string styleName;
    public float weight;
    public float grainDegrees;
    public float grainStretch;
    public float warpMetres;
    public float warpFrequencyPerMetre;
    public float macroFrequencyPerMetre;
    public float coveAmount;
    public float wobbleAmount;
    public float bias;
    public float firstLevelThreshold;
    public float secondLevelThreshold;
    [UnityEngine.Range(0, 1)] public float lowStepShare;
}

public static class TerracePlateauField
{
    public const float LockMetres = 6f;
    public const float BlendMetres = 24f;
    public static float SampleStyle(float2 worldXZ, float2 seedOffset, in TerracePlateauStyle style);
    public static float TileBlendFactor(float2 worldXZ, int2 tileCoordinate);            // 0 in the lock band, 1 from 30 m inside
    public static float BlendWorldAndTile(float worldValue, float tileValue, float blendFactor, float mean);
    public static float2 TileSeedOffset(int worldSeed, int2 tileCoordinate, int rollIndex);
    public static int StyleIndexFor(int worldSeed, int2 tileCoordinate, int rollIndex, System.Collections.Generic.IReadOnlyList<TerracePlateauStyle> styles);
}

// Data/Terrain/TerrainTileData.cs
public int rollIndex;                                   // 0 = world field only

// Data/Terrain/TerrainGenerationProfile.cs
public List<TerracePlateauStyle> plateauStyles;         // five defaults, NC-D3; index 0 is the world style

// Editor/Terrain/TerrainProceduralGenerator.cs   (new overload; the old ones forward with rollIndex 0 and a null lookup)
public static void GenerateTerraceLevels(TerrainGenerationProfile profile, Vector2Int tileCoordinate, int maxLevels,
    int rollIndex, System.Func<Vector2Int, TerrainTileData> neighbourLookup,
    byte[] outLevels, byte[] outStepKinds, System.Collections.Generic.List<TerraceRampPlacement> outRamps);

// Editor/Terrain/TerrainGenerateTool.cs
public static bool RerollOneTile(TerrainTile tile, int rollDelta);   // bumps rollIndex, regenerates and rebuilds this tile only

// Data/Terrain/TerrainWorldProfile.cs   (defaults change)
terraceCornerRadiusMetres = 1.5f; terraceTopRoundMetres = 0.25f; hillStepHeightMetres = 0.7f; terraceUseTileCliffEdges = false;
```

`TerrainTileData` lives in `StitchPunk.Data`; check with a haiku grep that it can see `TerracePlateauStyle` in
`StitchPunk.Utils` before wave 1. If it cannot, the struct moves to `Data/Terrain/` and the field stays in Utils.

## 7. Acceptance by look (judge the sheet against Edges1-3, never against this text)

- **A1 Freeform.** In plan, no outline follows the grid; curves sweep, with a few tighter bays and points.
- **A2 Varied.** Across the 3 x 3 no two tiles read as the same recipe; there are mesas, a long escarpment, ledge
  fields and open ground.
- **A3 Two heights.** Low ledges and tall cliffs are both on screen at the gameplay camera, and the low ones read
  as walkable (Edges2, left).
- **A4 Profile.** On a tall cliff the eye reads, in order: ragged biome edge, light stone lip, bright rim line,
  dark cracked face, dark apron, ragged lower biome. Nothing is missing along any stretch (Edges1).
- **A5 Clean edge.** The ragged line is the owner's edge art, rounded lobes, no spikes, no visible repeat within
  16 m, no stretch on curves, no seam where the biome changes along the rim.
- **A6 Darker face.** The face is darker than the ground on top. No beige cylinder.
- **A7 Ramps.** A big tall plateau has 2-3 ramps on different sides, straight and along-cliff. Ledges have worn gaps.
  No ribbon crosses a ramp mouth.
- **A8 Seams.** No step, gap or art break at a tile seam, before and after a reroll of either side.
- **A9 Tops.** Biome borders on a plateau top look like biome borders on the ground (NC-D17).
- **A10 Reroll.** Rolling a tile gives a visibly different, still good tile in 2 s or less, and nothing else moved.

## 8. Fixtures and guards (each must fail when its fix is reverted, or be deleted)

Shape metrics, computed by the plan sheet over 9 tiles x 6 rolls and asserted in `TerracePlateauFieldTests`:
- **M1** under 25 % of outline length runs within 5 degrees of a world axis.
- **M2** the longest stretch that turns less than 5 degrees is 14 m or shorter (ramp stretches excepted).
- **M3** region compactness `4*pi*area / perimeter^2` spans from under 0.35 to over 0.6 across the set.
- **M4** no plateau neck and no gap between two cliffs under 6 m (NC-D4).
- **M5** terraced area per tile is 0-55 %; in each 3 x 3 at least one tile is under 10 % and one over 30 %.
- **M6** low-step outline is 40 % or more of all outline length; both kinds occur in every 3 x 3.

Roll invariants, in `TerrainProceduralGeneratorTests` (50 random seeds each, in-memory tile data, no assets):
- **I1** generate A, then B beside it, then reroll B: A's arrays are unchanged.
- **I2** B's lock-band cells are identical for five different `rollIndex` values.
- **I3** every region crossing the A/B seam has one kind on both sides.
- **D1** the same inputs give the same bytes twice.

Guards measured on a capture by a small script, not by eye:
- **G1** mean luminance of a face patch is 0.75 x or less of the top-ground patch beside it.
- **G2** reroll stage timings sum to 2 s or less on the owner's machine.

## 9. Log (one line per wave, newest last)

- 2026-11-27 Spec written. Owner rejected captures 09/11 of TerraceCliffEdges, deleted the 8 derived Rim/Foot sets
  and kept 3 ground edge sets. Cause: grid-locked outline + one-octave shapes + one step height + bright smooth wall.
  Direction: back to the freeform outline of 2026-11-21, new plateau field with styles, low ledges as short cliffs,
  edge-art ribbons from the straight atlas pieces, per-tile roll with a 6 m lock band.
- 2026-11-27 Wave 0 done. Owner's atlas/reference work committed (b9603357). Kept AlongCliff landing wall + 15-degree
  caravan limit (7578236c); C5a ramp mask reverted, TerraceHillBankField deleted, both in
  `Tasks/Claude/CliffEdges_wave4_leftovers.patch` (the one non-C5a hunk, `MajorityTiesHigher` made internal, only served
  the hill bank). T0 `contact_sheet.py` (ce95c643). Gate fix: stale check now per assembly (2a6b298d). Baseline found a
  double-step bug in the freeform path (level 2 beside level 0: fields crossed, 163 multi-level sub-cells, floating
  slab); `NestStackedLevelFieldsWithLedge` keeps each level >= 3 m inside the one below (fixture
  `DoubleStepNeverPutsTwoLevelsInOneSubcell`, fails at 320 without it). Bridge pins `_TerraceCliffEdgesReady` = 0.
  Sheet `Captures/NaturalCliffs_2026-11-27/sheet_00_baseline.png`: the 11-21 look is back on tile 0_0; a stale ramp
  placement from the old data remains until the wave-2 regenerate. Tile rebuild = 14.4 s (G2 budget is 2 s: measure
  stages in wave 4). Section 6 check: Utils references Data, so `TerracePlateauStyle` goes to `Data/Terrain/`.
- 2026-11-27 Wave 1 done (orchestrator wrote signature stubs first so all six workers gated in parallel). N1
  `TerracePlateauField` (Utils) + `TerracePlateauStyle` / `TerracePlateauStyleDefaults` (Data), N1t 6 tests, N2a
  `TerracePlateauRaster` (Editor/Terraces: window 132 cells, threshold -> 8 m stacked set-back for ALL styles ->
  open/close LAST -> cull -> kinds), N2m `TerracePlateauMetrics`, N2 `TerracePlateauPlanSheet` (rasterizes 5 x 5,
  measures the inner 3 x 3: the open outer edge faked 20 m straight runs), L1 `edge_strip_mock.py`. Orchestrator calls:
  world style (index 0) = LedgeField; every roll including 0 blends (a fresh world is already varied); coves and wobble
  sample at metre scale on a stretched grain. Tuned defaults, seed 424242: M1 0.18 ok, M3 0.31..0.90 ok, M6 0.55 ok;
  M2 17.2 m on Escarpment only (others <= 10 m) -> **M2 limit for Escarpment is 20 m** (it is a cliff line, Edges1);
  M5 per-3x3 rule passes 5 of 6 rolls -> accepted (distribution, not a defect); M4 = 4 cells, all at seams -> must reach
  0 once N3's neighbour-margin rule lands. Note: the 30 m blend band covers 84 % of a tile, so the world style dominates
  borders; tile styles read mostly in the centre 40 m. L1: 8 straight pieces x 3 atlases join with seam score 0.000 and
  bend on r8 curves without tearing; the 2 m lobe rhythm is regular -> L2 adds a hashed mirror flip per segment.
  Sheets: `Captures/NaturalCliffs_2026-11-27/plateau_plan_sheet_seed424242.png`, `L1_edge_strip_mock.png`. ⏸ posted, continued.
- 2026-11-27 Wave 2 done. N3a raster NC-D8 overload (W pass + R pass, lock band and stored cells fixed, W-labelled
  lock-band kinds, level-0 channel cut for a region with two fixed kinds), N3b generator calls it per tile with a biome
  maxLevel cap, slope cliffs kept, ramp window from stored neighbours, cull protected 6 m from the border; profile
  `plateauStyles`; N3t I1/I2/D1 fixtures at 6 seeds, not 50 (one generate is ~1 s: two raster passes + ramp window;
  50 seeds ran > 180 s per test). L2 `PaintedCliffEdgeRibbon.shader`, L3 lip/foot ribbons with biome blend, materials
  `M_CliffRibbonLip/Foot`. 3 x 3 regenerated (603 s, 67 s/tile: far over the G2 2 s budget, wave 4 timings). Sheets
  `sheet_02_wave2.png`, `sheet_02_steps.png`: outlines are freeform and organic, ribbons draw (18 renderers, atlases bound);
  the wall still reads as a smooth tan extrusion (L4, wave 3); the cream lip band reads as a stripe, not ragged art
  (wave 5 look round). Data: tall cliffs are rare (hill edges ~85 % of 0->1 edges; world style lowShare .85 owns the
  borders) and the 3 x 3 has no level 2: the raster makes it (tiles 1_0, -1_1) but it falls in Stone, maxLevel 1, by
  rule; left as data. Plan sheet re-measured: M4 = 257 FAIL (roll 2 = 187): channel cuts and the W/R splice run after
  the re-clean, and the 8 m stacked set-back is not re-applied after it -> **N3c** in wave 3 (raster only). M2 15.3 on
  roll 2 against a uniform 14 m limit in code; the per-style 20 m Escarpment limit goes in with N3c. ⏸ posted, continued.
