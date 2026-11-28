# Resume prompt: Natural Cliffs, wave 4b (ramp notches) then wave 5

Paste everything below the line into a fresh Claude Code session (Opus, this repo, Unity open).

---

Continue `Assets/_Vault/Tasks/Plans/NaturalCliffs_System.md` as the orchestrator under its section 4 protocol.
Waves 0-4 are committed (wave 4 = `6260ce9e`, pushed). Read the spec once (sections 0, 3, 4, 5, 7, 8, 9; the
2026-11-28 wave 4 log line is the state of the world); after that re-read only sections 0, 5 and 9. Protocol:
- The orchestrator writes no feature code. Its own edits are one-liners and asset fields only.
- Workers are `worker` / `model: sonnet`, at most 2 named files, 40-turn cap, briefs of 60 lines or fewer with
  the decision paragraphs and line ranges pasted in. A capped worker is never resumed; spawn a fresh one.
- Verifiers are haiku (inventories) or sonnet (diff review). Subagents never call `mcp__UnityMCP__*`.
- One contact sheet per judgement (`Tools/devloop/contact_sheet.py`).
- Commit by pathspec after `git diff --cached`; never `git add -A` (public repo). Never commit
  `Captures/EdgeTiles_*` or `__pycache__`. One log line per wave in section 9, plus the status line.
- ⏸ checkpoints: post the sheet and continue (unseen checkpoints pass).
- The owner is remote: say when Unity goes busy for long and when it is responsive again.
- Never regenerate the derived Rim/Foot atlas sets. Never `cd` in Bash; use `(cd x && cmd)` subshells.

## 0. First: confirm the bridge
`ReadMcpResourceTool mcpforunity://instances`, then `execute_code` returning `"alive"`. On null data or "ping not
answered": run `Tools/devloop/unity_focus.ps1` and `unity_dialogs.ps1`, retry, and retry reflection snippets with
`safety_checks:false`. Still failing: ask the owner to restart the server from Unity's MCP window. Never kill the
MCP server process (a peer session shares it).

## 1. State at `6260ce9e`
- Tile rebuild 6.5 s (G2 target 2 s not met: biome mask 3.9 s, terraces 3.2 s, both serial on purpose,
  `AnimationCurve.Evaluate` is not thread-safe; see `ForEachRowInOrder` in `TerrainProceduralGenerator.cs`).
- Low steps are 0.70 m in the mesh (M1, `TerraceTileMeshBuilder.LevelOffsetMetresAt`).
- Ramp planner rewritten; **0 generated ramps on the committed 3x3**, plan render has 0 hole pixels.
  - Worn gaps are planned but not spawned: `TerrainGenerateTool.SpawnGeneratedWornGapRamps = false`
    (`Editor/Terrain/TerrainGenerateTool.cs:102`). When spawned, 14 of 18 holed where the 2.8 m notch meets a
    freeform wall. Snapping ramp yaw to 90 degrees made it worse (reverted).
  - AlongCliff candidates are parked in the planner (landing notch also holes).
  - No tall Straight ramp survives the 1 m base-rise rule (`MaxBaseRiseAcrossRampMetres`), so A7 fails.
- U1 reroll tool done; Scene-view hover outline is an owner checkpoint (not capturable in background).
- `TerraceRampMeshBuilderTests.TerracedTileEmitsNoWallVertexInsideARampFootprint` is stale (walls moved to the
  `TerraceWalls` child mesh in wave 0) and fails at HEAD; it goes to C1.
- S2 Play spike (citizen walks a 0.7 m ledge, caravan stops at the face) never run; owed to the owner, and now
  meaningful because ledges really are 0.7 m.

## 2. Wave 4b: ramp notch vs freeform wall
Goal: worn gaps, AlongCliff and tall ramps spawn with **0 holes** on the 3x3.
1. **Diagnose before briefing.** Spawn the parked gaps (flip the const, regenerate one tile that had holes),
   then run the hole check below in a *separate* call. Capture 2-3 of the worst ramps close up (pitch 40, FOV 40,
   distance 14, magenta clear) into `Captures/NaturalCliffs_2026-11-27/04b_*.png` and one sheet. Decide from the
   pixels which side leaks: the ground notch cut wider than the ramp mesh, the wall clip (`TerraceTileAssembly.cs`
   / `TerraceTileMeshBuilder.cs` wall pass) removing wall the ramp doesn't cover, or the ramp sides not reaching a
   curved wall. Read `TerraceRampMeshBuilder.cs` around the notch/footprint code yourself (grep, 40 lines).
2. **One worker** on the guilty file(s) (at most 2) with the diagnosis, the sheet path and the fix rule pasted in.
   Likely shape: the clip/notch follows the actual wall outline within the ramp width, or the ramp gets side
   skirts down to the lower level, instead of a rectangle against a curve.
3. **Gate:** compile (`python Tools/devloop/devloop.py gate game`), refresh Unity, regenerate the 3x3 (tell the
   owner, ~1 min now), separate-call hole check: `holes(>60px)=0` and plan `planHolePixelsInside3x3=0`.
4. Then re-enable in order, re-running the gate each time: worn gaps (`SpawnGeneratedWornGapRamps = true`),
   AlongCliff in the planner, then re-check the 1 m base-rise rule for tall Straight ramps (measure the base rise
   of the rejected candidates first; loosen only if the ramp mesh now follows the base terrain without holing).
   A7: every tall region gets its NC-D13 ramp count.
5. Fixtures: `TerrainProceduralGeneratorTests|TerracePlateauFieldTests|TerraceTileMeshBuilderTests|`
   `TerraceRampMeshBuilderTests|TerraceLevelFieldTests` via the reflection runner on `EditorApplication.delayCall`
   with a result file (avoids `run_tests` saving scenes, which fires the nav auto-rebake). Revert-check any new test.
6. Commit wave 4b by pathspec (code, regenerated `Assets/Terrain/RandomWorld/Tile_*`, captures), log line, ⏸ sheet.

### Hole check (execute_code, after the regenerate, in its own call)
```csharp
System.Text.StringBuilder report = new System.Text.StringBuilder();
GameObject holder = new GameObject("TempHoleCamera"); Camera holeCamera = holder.AddComponent<Camera>();
holeCamera.clearFlags = CameraClearFlags.SolidColor; holeCamera.backgroundColor = Color.magenta;
holeCamera.cullingMask = LayerMask.GetMask("Default", "Ground", "Water"); holeCamera.farClipPlane = 400f;
RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32); holeCamera.targetTexture = target;
Texture2D image = new Texture2D(128, 128, TextureFormat.RGB24, false);
int ramps = 0, holed = 0, pinholes = 0, clean = 0;
foreach (TerraceRamp ramp in UnityEngine.Object.FindObjectsByType<TerraceRamp>(FindObjectsSortMode.None)) {
  if (ramp.name.IndexOf("Generated", System.StringComparison.OrdinalIgnoreCase) < 0) continue; ramps++;
  int worst = 0;
  foreach (float yawOffset in new float[] { 180f, 90f, 270f, 0f }) foreach (float pitch in new float[] { 90f, 40f }) {
    holeCamera.orthographic = pitch > 80f; holeCamera.orthographicSize = 8f; holeCamera.fieldOfView = 40f;
    Quaternion rotation = Quaternion.Euler(pitch, ramp.YawDegrees + yawOffset, 0f);
    holeCamera.transform.rotation = rotation;
    holeCamera.transform.position = ramp.transform.position - rotation * Vector3.forward * (pitch > 80f ? 150f : 14f);
    holeCamera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply();
    int magenta = 0; foreach (Color32 pixel in image.GetPixels32()) if (pixel.r > 240 && pixel.g < 20 && pixel.b > 240) magenta++;
    worst = Mathf.Max(worst, magenta);
  }
  if (worst > 60) { holed++; report.Append("HOLE " + worst + "px " + ramp.transform.position.ToString("F0") + " yaw" + ramp.YawDegrees.ToString("F0") + " L" + ramp.lowerLevel + "; "); }
  else if (worst > 0) pinholes++; else clean++;
}
RenderTexture.active = null; holeCamera.targetTexture = null;
UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(holder);
return "ramps=" + ramps + " clean=" + clean + " pinholes=" + pinholes + " holes(>60px)=" + holed + " " + report;
```
Plan-wide check: ortho camera at (0,200,0) looking down, `orthographicSize` 140, 1920x1080, count magenta pixels
with |world x|,|world z| <= 148 (0.259 m per pixel). Gameplay/close-up capture recipe: spec section 9 wave 3 line and
`Memories/Code/Terrain.md`.

## 3. Optional in 4b if cheap: G2 toward 2 s
A deterministic speedup of the biome mask and terraces: bake each `AnimationCurve` the passes evaluate into a
float LUT on the main thread, then the row loops can go `Parallel.For` again. Gate: the mask PNG and terrace bytes
are byte-identical to serial output for tiles 0_0 and 1_1 (the `mask_0_0_preG2c.png` comparison method), and the
I1/I2/I3/D1 fixtures pass. One worker, `TerrainProceduralGenerator.cs` only.

## 4. Wave 5 (spec section 5 table)
- Look rounds against `Tasks/References/Edges1.png`, `Edges2.png` (Read them first), one sheet per round:
  - the lip ribbon reads as a flat cream stripe, not ragged edge art;
  - tall cliffs are rare (~85 % of 0->1 edges Low: world style lowShare .85) and there is no level 2 on the 3x3;
  - ghost wall slivers above one ledge;
  - the foot apron looks heavy against a 0.7 m step.
- C1 NC-D18 deletions, including the stale ramp test above.
- C2 guide doc `Docs/Terrain/TerrainEdgeTileGuide.md`.
- Update `Memories/Code/Terrain.md` and the memory `project_natural_cliffs_spec.md`.
- Retire the spec to `Tasks/Verification/` with `verify-natural-cliffs.md` (owner items: S2 Play spike, U1
  Scene-view outline, Edge Tiles 2 m biome-border blockiness call).

## 5. Traps
- Run hole checks in a separate call after any regenerate: deferred rebuilds give false holes in the same call.
- Full regenerates leave neighbour assets dirty; save the scenes before an I1 neighbour-unchanged check.
- `run_tests` saves scenes and fires the nav auto-rebake on every tile; use the reflection runner.
- Never call `TerrainTileCreator.FindTileAt` per cell; memoise per call (the 95 s cause).
- Lock-band cells (6 m) always use roll 0 (I2).
- Startup rebake hang: `touch UserSettings/TerrainAutoRebake.off`, grep `[TerrainRebake]` in `Logs/Editor.log`.
- After a refresh confirm `Library/ScriptAssemblies/StitchPunk.Editor.dll` is newer than the edited .cs.
