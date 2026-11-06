# Next session: build the terrain system (tools first)

Paste everything below the line into a fresh session. Unity must be open for the MCP steps.

---

We are building the open-world terrain system. The whole design is settled and written down; do not re-derive it or re-ask the owner's answered questions.

**Read first, in this order:**
1. `CLAUDE.md` and `Assets/CLAUDE.md` (conventions, the `dots-*` skills, the compile gate, subagent rules).
2. `Assets/_Vault/Tasks/Verification/Terrain_System.md`, the master plan. Section 0 is what the owner decided (settled), section 2 the decisions TT-D6 to TT-D33, section 4 the spikes, section 5 the task list, section 6 the order of work, section 7 how to run it.
3. `Assets/_Vault/Tasks/Plans/TerrainTiles_System.md` (history of T0/T1 and TT-D1 to TT-D5).
4. The memory note `project_painted_ground_tiles.md`, and `_Vault/Memories/Code/Editor.md`, `RULES.md`, `Gotchas.md` before editing a folder.
5. The owner's look reference: `Assets/_Vault/Tasks/Claude/ExampleZoomOutFasterTravel.png` (only matters from P11).

**State of the repo.** T0 (painted ground on the CampLoop `Ground` tile, 500 decals under `GroundDecals`, package patch to `DecalScatter.cs`) is committed as `45ad433f`. T1 (`TerrainTile`, `TerrainSplatCodec`, `TerrainTileAssets`, `TerrainTileCreator`, `Stitch Punk > Terrain` menu, `Ground` adopted as tile (0,0)) is committed as `a4c2a275`. Leave these untracked files alone: `Assets/InitTestScene*`, `Assets/Screenshots`, `Assets/_Vault/Spencer/next-session-trade-prompt.md`. `Assets/Scenes/CampLoop.unity` shows only camera-transform drift: do not commit it unless you changed the scene on purpose.

**This session's goal (tools first, TT-D30):** run the spikes the authoring phases depend on, then build P1 (data foundation) and P2 (height).
1. Spikes, run by you through the Unity MCP (workers never touch the MCP): **S1** (height collider and unit gravity on a hill), **S5** (SDF encode speed), **S6** (painted shader on a slope and cliff), **S7** (Unity Splines 2.8.4 API), **S11** (real data size per tile). Append a 5-line finding for each to `Assets/_Vault/Tasks/Plans/Terrain_Spikes.md` and flip any TT-D a spike contradicts before building on it.
2. **P1** then **P2** from the plan, with Sonnet `worker` subagents (never `general-purpose`), at most two files each, using the standard footer in section 7. Parallel groups are marked `Par`. Compile-gate every wave (`python Tools/devloop/devloop.py gate <package>`, then `refresh_unity` and `read_console` for anything without an asmdef), lint Editor files, and verify each worker's diff yourself or with a `verifier`.
3. Stop at the P2 checkpoint (sculpt a hill, walk a unit up it in Play) and report. Commit once per wave with explicit paths, never `git add -A`.

**Standing traps (all already bitten):**
- A domain reload closes `CampLoop_Sub`: reopen it with `SubSceneUtility.EditScene` before touching `Ground`. `manage_scene save` only saves the active scene, so save the subscene with `EditorSceneManager.SaveScene(scene)` and check `git diff` shows it.
- `execute_code` is CodeDom C# 6 and blocks `AssetDatabase.DeleteAsset`; use `manage_asset delete`.
- Game-view screenshots can be blank for a second after reopening the subscene: retry before debugging.
- The custom `Painted/Post` full-screen pass rendered black on this Unity version; do not revisit it (the plan uses built-in URP Volume overrides).
- No `var`, no single-letter names, explicit types, `.Schedule()` never `.Run()`, one or two lines of comment *why* only.
- A worker that hits its turn cap is never resumed: read its diff and spawn a fresh worker for the remainder.

**Stop and report instead of pushing on if:** a spike contradicts a decision three or more later tasks depend on; a task needs an edit outside its named files; the Editor will not respond for several minutes; or a commit would include files you did not intend.

When you finish, update `Terrain_System.md` status, the memory note, `_Vault/Memories/Code/Terrain.md` (create it) and the matching `Editor.md` rows, and give me a short report: what was built, what the spikes found, what is next.
