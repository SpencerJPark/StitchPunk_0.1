# Resume prompt: Natural Cliffs, finish wave 3 and run waves 4-5

Paste everything below the line into a fresh Claude Code session (Opus, this repo, Unity open).

---

Continue `Assets/_Vault/Tasks/Plans/NaturalCliffs_System.md` as the orchestrator under its section 4 protocol.
Waves 0-2 are done and committed (last commit `72b5fc35`). Wave 3 is **coded and gated but not verified or
committed**: the previous session lost the Unity MCP bridge (server kept dropping Unity's websocket). Read the spec
once (sections 0, 3, 4, 5, 7, 8, 9); after that only re-read sections 0, 5 and 9. Follow the protocol strictly:
- The orchestrator writes no feature code. Its own edits are one-liners and asset fields only.
- Workers are `worker` / `model: sonnet`, at most 2 named files, 40-turn cap, and get briefs of 60 lines or fewer
  with the decision paragraphs pasted in.
- Verifiers are haiku (inventories) or sonnet (diff review). Subagents never call `mcp__UnityMCP__*`.
- Use one contact sheet per judgement (`Tools/devloop/contact_sheet.py`).
- Commit by pathspec after `git diff --cached`. Write one log line per wave in section 9.
- At ⏸ checkpoints, post the sheet and continue (standing rule: unseen checkpoints pass).
The owner is remote. Tell them when Unity is busy for a long time and when it is responsive again.

## 0. First: confirm the bridge
`ReadMcpResourceTool mcpforunity://instances`, then `execute_code` returning `"alive"`.
- If calls return `success:false` with null data, or "ping not answered", run `Tools/devloop/unity_focus.ps1` and
  `unity_dialogs.ps1`, then retry.
- If it still fails, the MCP server needs a restart from Unity's MCP for Unity window (Stop Server / Start Server /
  Connect). Ask the owner; do not kill the server process (a peer session shares it).

## 1. Wave 3 state (uncommitted working tree)
| Task | Files | What landed |
|---|---|---|
| N3c M4 fix | `Editor/Terrain/Terraces/TerracePlateauRaster.cs`, `TerracePlateauPlanSheet.cs` | Channel cut half-width 3.5 m; opening (erode then dilate 3 m) on non-fixed cells after `AssignWindowStepKinds`; `ClearBelowStackedSetback` re-applies the 8 m set-back after each re-clean; plan sheet M2 limit is per tile: 20 m when the rolled style's `grainStretch > 1.5` (Escarpment), else 14 m |
| N4 roll | `Data/Terrain/TerrainTileData.cs` (`public int rollIndex`), `Editor/Terrain/TerrainProceduralGenerator.cs` | New overloads `AnalyticHeightAt(profile, x, z, tile, roll)`, `GenerateHeights(profile, tile, roll, out)`, `GenerateBaseMask(profile, tile, roll, res, out)`. Roll 0 = exactly the old output (orchestrator call: a roll-0 regenerate never moves authored mountains, flats or rivers). Roll != 0 blends only the `heightLayers` fbm octaves and the biome rule noise via `TerracePlateauField.BlendWorldAndTile(world, tile, b, 0)`. The terrace generator's biome cap and slope cliffs use the rolled height and biome |
| N4t | `Tests/Terrain/TerrainProceduralGeneratorTests.cs` | `RerollKeepsTheHeightBorderRing`, `RerollChangesTheTileInterior`, `RerollKeepsTheBiomeMaskBorderRing` (4.5 m ring) |
| L4 wall | `PaintedGround/Shaders/PaintedCliff.shader` | Wall (`_Mode` 0) only: rim->foot rock gradient from the `_TerraceRimRock`/`_TerraceFootRock` globals, 2-band main light (`_WallShadeLevel` 0.6), cracks every 0.4-1.6 m with ink (`_InkColour`), ±17 % drift along ~11 m, rim line on the top 0.12 m. **Compiles clean** (checked) |
| K1 nav | `Editor/Terrain/Nav/TerrainNavCellRules.cs`, `TerrainNavBaker.cs` | `crossesLowLedge` input, heavy cost 50. Chains whose drop is under `(hillStep + levelHeight)/2` go into a low-ledge mask, not the wall mask |
| Orchestrator | `Data/Terrain/TerrainWorldProfile.cs`, `Editor/Terrain/TerrainGenerateTool.cs` | `hillStepHeightMetres` 1.5 -> **0.7** (NC-D5); `terraceTopRoundMetres` 0.45 -> **0.25** (NC-D16). The asset does not serialize these fields, so the code defaults apply. The generate tool now passes `tileData.rollIndex` to the terraces, heights and base mask |

- `python Tools/devloop/devloop.py gate game` = **GATE PASS** (11 assemblies); lint passes.
- The modified `Assets/Terrain/RandomWorld/Tile_*` files come from the nav auto-baker re-baking on a scene save
  during a test run. The wave-3 regenerate replaces them.
- The `02_*.png.meta` files under `Captures/NaturalCliffs_2026-11-27/` are untracked; commit them with wave 3.

## 2. Finish wave 3, in order
1. **Tests.** Run the EditMode fixtures `TerrainProceduralGeneratorTests|TerracePlateauFieldTests|`
   `TerrainNavCellRulesTests|TerraceTileMeshBuilderTests`.
   - `run_tests` over MCP dropped the connection on long runs. If it does again, use `execute_code` with
     `TestRunnerApi` plus an `ICallbacks` class that writes a result file to the scratchpad. Declare the class after
     closing the method body: end the snippet with `static void Placeholder() {`.
   - A domain reload discards those callbacks.
   - Revert-check one new N4t test: break the roll-0 short-circuit or the `b` blend, watch it fail, restore it.
2. **Plan sheet.** Run it through `EditorApplication.delayCall` with a result file (~60 s):
   `TerracePlateauPlanSheet.RenderPlanSheet(424242, "Assets/_Vault/Tasks/Captures/NaturalCliffs_2026-11-27",`
   `profile.plateauStyles, 2f, 2, 150f)`, with the profile `Assets/Terrain/RandomWorld/RandomWorldGeneration.asset`.
   - Wave-2 numbers: M1 0.17 ok, M2 15.3 FAIL (roll 2), M3 ok, **M4 257 FAIL** (roll 2 = 187), M5 0.04..0.53
     (5/6 rolls accepted), M6 0.82 ok.
   - Target: M4 = 0 and M2 ok. If M4 stays above 0, find where the cells are (lock-band edge or channel) and give
     one worker the raster file only.
3. **Regenerate the 3x3** (`TerrainGenerateTool`; about 10 min, Unity shows "Hold on"). Tell the owner before and
   after. Wait on the window title (`(Get-Process Unity).MainWindowTitle` back to `Stitch_Punk - RandomWorld`),
   not on the log. Then enter Play; the subscene bake is slow.
4. **Spike S2 (NC-D6)** in Play: a citizen walks up and down a 0.7 m ledge without snagging, and the caravan stops
   at the ledge face. If units snag, widen both ledge bevels to 0.6 m.
5. **Reroll check (I1).** Set `rollIndex` 1, 2, 3 on tile 0_0 and regenerate only that tile each time;
   `git status` must show none of the 8 neighbours' `_Data.asset` changed.
6. **Wall sheet + guard G1** against `Assets/_Vault/Tasks/References/Edges1.png` and `Edges2.png`. G1 passes when
   the mean luminance of a wall-face patch is at most 0.75 x the top-ground patch beside it, measured by a small
   script on the capture.
   - Capture recipe (Play, `execute_code`, CodeDom C# 6, write `UnityEngine.Object.DestroyImmediate`):
     1. Make a temp `Camera` with `CopyFrom(Camera.main)`, then `ResetWorldToCameraMatrix`,
        `ResetProjectionMatrix` and `ResetCullingMatrix`.
     2. Set the culling mask to Default|Ground|Water and render to a 1920x1080 RT.
     3. Call `ReadPixels`, then write the PNG into `Captures/NaturalCliffs_2026-11-27/03_*.png`.
     4. Place the camera at `target - Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * distance`.
   - Shots:
     - gameplay: target (-25,3,14), pitch 41.6, FOV 15, distance 230
     - plan: (0,0,0), pitch 89.9, FOV 40, distance 210
     - close-ups: pitch 35, FOV 40, distance 26; tall steps were at (-60,·,-20.5) and (-32,·,67.5). Find new
       tall and low steps from the tile data after the regenerate: a level 0->1 edge; the upper cell's kinds
       byte bit 0 set means Low.
   - Ground y: raycast on the Ground layer.
7. **NC-D17 (biome borders on plateau tops).** V1 (sonnet verifier) found **no code cause**. Ground and tops share the
   material, `_GROUND_EDGE_TILES` is global, and the edge lookup is world-XZ only. The keyword and atlases are ON in
   Edit mode.
   - The wave-2 captures show staircase biome borders at level 0 too, so the edge art may not draw at all in Play.
   - Probe in Play: `Shader.IsKeywordEnabled("_GROUND_EDGE_TILES")`, the `_GroundEdgeTileAtlasR/G/B` globals, the
     tile material's `_SPLAT_WORLDTEXTURE` / `_SplatMap`, and `_TerraceCornerOrigin.z` == 2.
   - The result decides the V1f worker in wave 4.
8. **Commit wave 3 by pathspec:**
   - the 12 files above, the shader, the RandomWorld + Strategic tile data from the regenerate (spec wave 0,
     step 6), `Assets/Scenes/RandomWorld*` if changed, and the captures and sheets;
   - not `Captures/EdgeTiles_*` and not `__pycache__`.
   Then write the section 9 log line, update the status line, and post the ⏸ wall sheet.

## 3. Then waves 4 and 5 (spec section 5 table)
- **W4:**
  - R1 worn gaps and multi-ramp (`TerrainProceduralGenerator.cs`). One wave-2 test tile got 0 ramps.
  - U1 reroll tool (`TerrainGeneratePanel.cs`, `TerrainGenerateTool.cs`). The brief names
    `Docs/AnimationToolkit/EditorStyleGuide.md` R01-R22 and needs before/after captures at 177 pt.
  - V1f, the NC-D17 fix.
  - Stage timings for one reroll against the 2 s budget (G2). Measured so far: one generate is 1.8 s (raster
    0.4 s; the rest is per-cell biome lookups plus `AnalyticHeightAt` in the slope-cliff pass), and a full tile
    rebuild takes 67 s.
- **W5:** look rounds from failed A-items. Open items already known:
  - the lip ribbon reads as a flat cream stripe, not ragged art;
  - tall cliffs are rare (about 85 % of 0->1 edges are Low, because the world style's lowShare .85 owns the
    borders);
  - there is no level 2 in this 3x3 (the raster makes it, but those spots are Stone, `maxLevel` 1, by rule).
  Also in W5: C1 NC-D18 deletions, C2 guide doc, a `Memories/Code/Terrain.md` update, and retiring the spec to
  `Tasks/Verification/` with `verify-natural-cliffs.md`.

## 4. Traps learned in the previous session
- **Unity busy states:** an unfocused Unity stops ticking, so run `unity_focus.ps1`. "Hold on (busy …)" in the title
  means the main thread is blocked; wait on the title. `isPlaying` can be true while MCP times out.
- **Test runs re-bake every tile:** `run_tests` saves the open scenes first, and `TerrainNavAutoBaker.OnSceneSaving`
  then re-bakes every tile, which takes minutes.
- **Editor log:** the real log is `$LOCALAPPDATA/Unity/Editor/Editor.log` (100 MB+). Read it with bash `tail -c`,
  never PowerShell `Get-Content -Tail`.
  - It is flooded with `IsNormalized(dir)` assertions; filter them out.
  - MCP reconnects are not logged, so never wait for a "Connected" line.
- **Assemblies:**
  - After a refresh, confirm `Library/ScriptAssemblies/StitchPunk.Editor.dll` is newer (`-nt`) than the edited .cs;
    the first refresh can leave the old assembly loaded.
  - Gate with `game`, not a single assembly, when sibling assemblies changed.
- **Startup rebake hang (fixed 2026-11-28):** if Unity again sits on "Waiting for user code in StitchPunk.Editor.dll"
  after a reload, `touch UserSettings/TerrainAutoRebake.off` from a shell (or menu `Stitch Punk/Terrain/Auto Rebake
  On Edit`) and grep `[TerrainRebake]` in `Logs/Editor.log` for the path that fired.
- **Never `cd` in Bash.** Use `(cd x && cmd)` subshells.
