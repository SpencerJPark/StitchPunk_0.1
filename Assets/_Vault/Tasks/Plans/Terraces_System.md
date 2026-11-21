---
status: SPECCED 2026-11-21. T0 look spike in progress; T1 onward waits for the T0 captures to be accepted.
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

Spec retires to `Tasks/Verification/Terraces_System.md` with `verify-terraces.md` after T11.

## 6. Shared files and other sessions

`TerrainTileData.cs`, `TerrainTileBuilder.cs` and `TerrainNavCellRules.cs` are shared with live terrain work. Run `ListAgents` before each wave; if a peer is on main touching `Terrain/`, build in a worktree (`[[feedback-worktree-when-peer-busy]]`).

## 7. Open (defaults apply)

- **TX-Q1 Ink look on walls:** default, walls get the same ink treatment as other world geometry (`Tasks/Verification/InkLook_System.md`).
- **TX-Q2 Waterfalls:** default none in v1 (TX-D9).
- **TX-Q3 Cliff art:** default, reuse `T_Cliff.png` and `T_EdgeFringe.png`; a dedicated painted cliff strip and overhang strip will lift the look more than any code change.

## 8. Log (one line per wave, newest last)

- 2026-11-21 Spec written. T0 spike code written (gate + lint pass, Painted/Ribbon walls opaque, T_EdgeFringe alphaIsTransparency off). Not yet run: Editor closed. Next: menu Build Terrace Spike, then Capture Terrace Spike, read 01-05.
