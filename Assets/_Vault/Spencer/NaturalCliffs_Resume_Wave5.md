# Resume prompt: Natural Cliffs, wave 5 look rounds, then retire

Paste everything below the line into a fresh Claude Code session (Opus, this repo, Unity open).

---

Continue `Assets/_Vault/Tasks/Plans/NaturalCliffs_System.md` as the orchestrator under its section 4 protocol.
Waves 0-4b and wave 5 C1/C2 + round 1 are committed. Read sections 0, 5, 7 and 9 once (the 2026-11-28 log lines are the
state of the world); after that re-read only 0, 5 and 9. Same protocol as before: no feature code from the orchestrator
(one-liners and asset fields only); `worker` / sonnet, at most 2 named files, 40 turns, briefs of 60 lines or fewer
with decision text and line ranges pasted in; verifiers haiku (inventories) or sonnet (diff review); subagents never
call `mcp__UnityMCP__*`; one contact sheet per judgement; commit by pathspec after `git diff --cached` (never
`Captures/EdgeTiles_*`, never `__pycache__`); ⏸ = post the sheet and continue; tell the owner when Unity is busy and
when it is responsive again; never regenerate Rim/Foot sets; never `cd` in Bash.

## 0. Bridge
`ReadMcpResourceTool mcpforunity://instances`, then `execute_code` returning "alive". On null data: run
`Tools/devloop/unity_focus.ps1` and `unity_dialogs.ps1`, retry, use `safety_checks:false` for reflection. Anything that
runs longer than ~20 s (regenerate, fixtures, sweeps) goes on `EditorApplication.delayCall` and writes a `Temp/*.txt`
result file; wait for it with a Bash `until [ -f ... ]` loop after `unity_focus.ps1`.

## 1. State
- 3x3 RandomWorld: 10 plateau worn gaps, holes 0, plan 0 hole pixels; tall plateau cells 1471 (world style lowStepShare
  0.6 on `RandomWorldGeneration.asset`), no level 2 (biome maxLevel cap, left as data).
- Parked: sunken worn gaps (planner places the lower end on level 0; the ramp floats over the pit),
  AlongCliff (collection skipped in the planner), tall Straight ramps (0 on this 3x3: tall regions touch tile borders
  or fail the 1 m base-rise rule; loosening admits only sunken slope cuts that break caravan grade). A7 stays open.
- Round 1 sheet `Captures/NaturalCliffs_2026-11-27/sheet_05_round1.png` failed A4/A5:
  1. the lip ribbon reads as a flat cream stripe, not ragged edge art;
  2. the face is barely darker than a Stone top (G1 was measured on grass);
  3. the 0.7 m ledge's dark foot apron is heavy;
  4. not yet checked: ghost wall slivers above one ledge.

## 2. Look rounds (one or two files per worker, one sheet per round, reference Edges1/Edges2 Read first)
- Lip stripe: `PaintedCliffEdgeRibbon.shader` (coverage clip / rock colour on the lip) or `TerraceDressingBuilder.cs`
  (ribbon uv/width). Find out from pixels which one first: capture the lip with the ribbon renderer disabled vs enabled.
- Face vs stone top: bridge rim/foot rock colours are asset/scene fields (orchestrator); G1 on a Stone-top cliff too.
- Foot apron on Low steps: scale the foot ribbon/contact shadow by step height (0.7 vs 3 m).
- Capture recipe used in round 1: edit-mode `Camera.Render`, pitch 50, FOV 15 at 130 m (gameplay) and FOV 40 at
  14-22 m (close), focus = the densest tall / low outline cell found from the tile data (script in the 2026-11-28 log).

## 3. Then
- Sunken worn gap planner fix (one worker, `TerrainProceduralGenerator.cs` `CreatePlacement` / candidate direction),
  re-enable `SpawnSunkenWornGapRamps`, hole check. AlongCliff re-enable + landing notch if a tall plateau away from the
  tile border exists (else record and leave).
- Update `Memories/Code/Terrain.md` and memory `project_natural_cliffs_spec.md`; retire the spec to
  `Tasks/Verification/` with `verify-natural-cliffs.md` (owner items: S2 Play spike, U1 Scene-view outline, Edge Tiles
  2 m biome-border blockiness, caravan up a worn gap).

## Hole check (filtered; run in its own call after a regenerate)
```csharp
GameObject holder = new GameObject("TempHoleCamera"); Camera holeCamera = holder.AddComponent<Camera>();
holeCamera.clearFlags = CameraClearFlags.SolidColor; holeCamera.backgroundColor = Color.magenta;
holeCamera.cullingMask = LayerMask.GetMask("Default", "Ground", "Water"); holeCamera.farClipPlane = 400f;
RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32); holeCamera.targetTexture = target;
Texture2D image = new Texture2D(128, 128, TextureFormat.RGB24, false);
int groundMask = LayerMask.GetMask("Default", "Ground");
int ramps = 0, holed = 0, pinholes = 0, clean = 0, worstPin = 0; string report = "";
foreach (TerraceRamp ramp in UnityEngine.Object.FindObjectsByType<TerraceRamp>(FindObjectsSortMode.None)) {
  if (ramp.name.IndexOf("Generated", System.StringComparison.OrdinalIgnoreCase) < 0) continue; ramps++;
  int worst = 0; Vector3 centre = ramp.transform.position;
  foreach (float yawOffset in new float[] { 180f, 90f, 270f, 0f }) foreach (float pitch in new float[] { 90f, 40f }) {
    holeCamera.orthographic = pitch > 80f; holeCamera.orthographicSize = 8f; holeCamera.fieldOfView = 40f;
    Quaternion rotation = Quaternion.Euler(pitch, ramp.YawDegrees + yawOffset, 0f);
    holeCamera.transform.rotation = rotation; holeCamera.transform.position = centre - rotation * Vector3.forward * (pitch > 80f ? 150f : 14f);
    RaycastHit hit;
    if (pitch < 80f && Physics.Raycast(holeCamera.transform.position + Vector3.up * 300f, Vector3.down, out hit, 600f, groundMask) && hit.point.y > holeCamera.transform.position.y) continue;
    holeCamera.Render(); RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); image.Apply();
    Color32[] pixels = image.GetPixels32(); int magenta = 0;
    for (int index = 0; index < pixels.Length; index++) {
      Color32 pixel = pixels[index]; if (!(pixel.r > 240 && pixel.g < 20 && pixel.b > 240)) continue;
      Ray ray = holeCamera.ScreenPointToRay(new Vector3(index % 128 + 0.5f, index / 128 + 0.5f, 0f));
      if (Mathf.Abs(ray.direction.y) < 1e-4f) continue;
      float distance = (centre.y - ray.origin.y) / ray.direction.y; if (distance < 0f) continue;
      Vector3 onPlane = ray.origin + ray.direction * distance;
      if (Mathf.Abs(onPlane.x) > 148f || Mathf.Abs(onPlane.z) > 148f) continue;
      if (new Vector2(onPlane.x - centre.x, onPlane.z - centre.z).magnitude > 10f) continue;
      magenta++;
    }
    worst = Mathf.Max(worst, magenta);
  }
  if (worst > 60) { holed++; report += "HOLE " + worst + " " + centre.ToString("F0") + " L" + ramp.lowerLevel + " " + ramp.kind + "; "; } else if (worst > 0) { pinholes++; worstPin = Mathf.Max(worstPin, worst); } else clean++;
}
RenderTexture.active = null; holeCamera.targetTexture = null;
UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(holder);
return "ramps=" + ramps + " clean=" + clean + " pinholes=" + pinholes + " (worst " + worstPin + ") holes(>60px)=" + holed + " " + report;
```
Plan-wide check: the same camera, ortho at (0,200,0) looking down, size 140, 1920x1080, magenta pixels with
|world x|,|world z| <= 148 via `ScreenToWorldPoint`. Fixtures: reflection runner on `delayCall` over
`TerrainProceduralGeneratorTests|TerracePlateauFieldTests|TerraceTileMeshBuilderTests|TerraceRampMeshBuilderTests|TerraceLevelFieldTests`
(36 pass + 1 known Inconclusive at the end of 2026-11-28).
