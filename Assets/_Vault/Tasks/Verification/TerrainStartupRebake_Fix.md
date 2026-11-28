# Terrain Startup Rebake Fix

Status: built 2026-11-28 (gate + lint green); T5 owner open + fixture run owed, Editor was closed. Blocks `Spencer/NaturalCliffs_Resume_Wave3.md` (Unity is unusable for
~10 min after every open or recompile, so the MCP bridge never connects).

## 1. What is happening (read from code and `Logs/Editor.log`, no Editor needed)

The scene is not "rebuilt from scene data" on purpose. Nothing has changed; the bakers are told it has.

1. Opening the project, a recompile, or a scene open makes Unity call `OnValidate` on every terrain feature
   component (`TerraceRamp`, `TerrainRiver`, `TerrainWaterBody`, `TerrainCrossing`, `TerrainRoad`, `TerrainArea`,
   `TerrainPath`).
2. Each `OnValidate` raises a "changed" event through `EditorApplication.delayCall`. It has no idea whether a
   value changed; it raises every time.
3. `TerrainRebakeGate.SuppressRebakeEvents` is meant to swallow those load-time events, but its settle window is
   1.0 s of wall-clock time. The domain reload alone is 22 s (`[TerrainLoad] domain reload 22042 ms`) and the
   first frames after it are long, so late `OnValidate` calls land after the window and pass.
4. Each baker then does its whole job on the main thread inside `EditorApplication.update`:
   - `TerrainTerraceBaker.TickDebounce` -> `TerrainTileBuilder.Rebuild` for every tile a ramp overlaps. The 3x3
     holds 16 ramps on 6 tile scenes; a full tile rebuild was measured at 67 s. No progress bar, so Unity shows
     "Waiting for user code in StitchPunk.Editor.dll". This is the hang in the report.
   - `TerrainWaterBaker.TickDebounce` -> `BakeTile` -> `EditorSceneManager.SaveScene` -> `TerrainNavAutoBaker
     .OnSceneSaving` -> a nav bake per tile (stack in `Logs/Editor.log`). This is also why the
     `Assets/Terrain/RandomWorld/Tile_*` files show as modified with nobody editing them.
5. The static caches (`lastBounds`, the gate's flags) reset on every domain reload, so it repeats every time.

It is not an infinite loop: it should finish after roughly 7-10 minutes, then run again on the next recompile.
The ramps arrived with Natural Cliffs wave 2, which is why this started now.

Not yet proven: whether `Spline.Changed` (the `HandleSplineChanged` path) also fires at load. T3's log line
answers that on the first open after the build; the fix below does not depend on the answer.

## 2. Decisions

- **SR-D1 An event means a change.** `OnValidate` raises only when the component's serialized state differs from
  the state last seen. First sight after a load records the state and raises nothing. This is the answer to
  "why rebake if already baked": after this, it does not.
- **SR-D2 The gate settles on idle ticks, not only on a clock.** Suppression lasts until 1.0 s has passed AND 8
  `EditorApplication.update` ticks have run since the last load event. One long startup frame is one tick.
- **SR-D3 Kill switch on disk.** If `UserSettings/TerrainAutoRebake.off` exists, `SuppressRebakeEvents` is always
  true. Menu `Stitch Punk/Terrain/Auto Rebake On Edit` creates or deletes the file. It works with the Editor
  closed, which is the point: a hung Editor can be rescued from a shell. Manual menu bakes, the generate tool
  and the nav auto-baker are untouched.
- **SR-D4 No anonymous hang.** `TerrainTerraceBaker.TickDebounce` shows a cancelable progress bar per tile.
  Cancel keeps the remaining tiles pending (they rebuild on the next terrace event or from the menu).
- **SR-D5 Startup bakes name their cause.** A change handler that fires within 30 s of a load event logs one
  `[TerrainRebake] startup event` line with the object name. `Logs/Editor.log` keeps the stack, which shows
  whether it came from `OnValidate`, `HandleSplineChanged` or `Update`.
- **SR-D6 Not done here:** persisted input hashes on the bake outputs (skip a bake when inputs match the last
  bake across sessions), and moving the 67 s rebuild off the main thread. SR-D1 removes the false triggers,
  which is the whole bug; reopen these only if T5 still shows startup bakes.

## 3. API (exact names, so the tasks below are parallel-safe)

Added to `Assets/_Scripts/MonoBehaviours/Terrain/TerrainRebakeGate.cs`. Editor branch has the logic; the `#else`
branch has no-ops (`ShouldRaiseForValidate` returns false, `RememberSerializedState` does nothing,
`SecondsSinceLastLoadEvent` returns `double.MaxValue`).

```csharp
// Records the component's serialized state. True only when it differs from the state last seen
// and rebake events are not suppressed. First sight returns false.
public static bool ShouldRaiseForValidate(UnityEngine.MonoBehaviour component)

// Records the state without raising. Called from OnEnable so a component Unity never validated is still known.
public static void RememberSerializedState(UnityEngine.MonoBehaviour component)

// Seconds since the last domain reload, scene open or play-mode flip.
public static double SecondsSinceLastLoadEvent { get; }
```

- State = `Hash128.Compute(EditorJsonUtility.ToJson(component))`, kept in a
  `static Dictionary<UnityEngine.Object, Hash128>` keyed by reference (not `GetInstanceID`, it is gone in 6.5).
  Drop destroyed keys when the count passes 256. The transform is not part of it; each component's `Update`
  already watches the pose.
- `ShouldRaiseForValidate` records the state **before** it checks suppression, so a value changed under
  `TerrainRebakeGate.Suppress()` is not re-raised later.
- Kill switch: read `File.Exists("UserSettings/TerrainAutoRebake.off")` once in `RegisterEditorHooks` and again
  in the menu toggle; never per call.

## 4. Tasks

Token rules for this spec: the orchestrator applies the pasted one-line edits itself (cheaper than a spawn);
workers are `worker` / sonnet with the files named here and nothing else to read; greps go to a haiku
`verifier`, which observes and never edits; no subagent calls `mcp__UnityMCP__*`; one gate for the whole wave.

### Wave 1 (all [parallel-safe])

**T1 gate + fixture** — worker, 2 files.
- `Assets/_Scripts/MonoBehaviours/Terrain/TerrainRebakeGate.cs` (122 lines, read whole): section 3 API, SR-D2
  (`settleUpdatesRemaining`, set to 8 wherever `ExtendSuppression` runs and in `RegisterEditorHooks`,
  decremented by an `EditorApplication.update` hook), SR-D3 file + menu.
- New `Assets/_Scripts/Tests/Terrain/TerrainRebakeGateTests.cs` (EditMode, use the `dots-test` skill): on a
  temp `GameObject` with `TerraceRamp`, inside nothing that suppresses: first `ShouldRaiseForValidate` is false;
  unchanged is false; after `widthMetres += 1f` it is true; then false again. One more: a change made inside
  `using (TerrainRebakeGate.Suppress())` returns false and stays false after the scope. The fixture must get
  the gate past its settle (expose `internal static void EndSettleForTests()`; the test asmdef needs
  `InternalsVisibleTo` or make it public with that name).
- Revert-check: make first sight return true, watch test 1 fail, restore.

**T2 component edits** — orchestrator, 7 files, same two edits in each of `TerraceRamp.cs`, `TerrainRiver.cs`,
`TerrainWaterBody.cs`, `TerrainCrossing.cs`, `TerrainRoad.cs`, `TerrainArea.cs`, `TerrainPath.cs`
(`Assets/_Scripts/MonoBehaviours/Terrain/`):
1. In `OnValidate` only, the outer check
   `if (TerrainRebakeGate.SuppressRebakeEvents)` becomes `if (!TerrainRebakeGate.ShouldRaiseForValidate(this))`.
   Leave the check inside the `delayCall` lambda and the ones in `HandleSplineChanged` as they are. In
   `TerrainArea` and `TerrainPath` the call stays after `AssignFeatureIdIfEmpty()` / `SynchroniseSplineClosedFlag()`.
2. First line of `OnEnable`: `TerrainRebakeGate.RememberSerializedState(this);`

**T3 terrace baker** — worker, 1 file: `Assets/_Scripts/Editor/Terrain/Terraces/TerrainTerraceBaker.cs` (75
lines). SR-D4 in `TickDebounce` (`EditorUtility.DisplayCancelableProgressBar`, `ClearProgressBar` in `finally`,
cancel puts the unbuilt coordinates back in `pendingTiles` without re-hooking). SR-D5 in `OnTerraceChanged`.

**T4 cause log in the other bakers** — orchestrator, one statement each at the top of `RegisterChange` in
`Water/TerrainWaterBaker.cs` and of the matching change handler in `Roads/TerrainRoadBaker.cs` and
`Features/TerrainFeatureBaker.cs`:
```csharp
if (TerrainRebakeGate.SecondsSinceLastLoadEvent < 30.0) Debug.Log("[TerrainRebake] startup event: water " + (changedObject != null ? changedObject.name : "destroyed"));
```

**V1 creation check** — haiku verifier, greps only, 15 lines back: for each of the 7 components, what triggers
the first bake when a tool or the generator creates one (`AddComponent<...>` call sites under
`Assets/_Scripts/Editor/Terrain/`)? Under SR-D1 a freshly added component's first `OnValidate` raises nothing.
`TerrainTerraceBrush.cs:430` already calls `RebuildTiles` itself. Any creator that relied on `OnValidate` gets
an explicit raise after it finishes setting fields (one line, orchestrator).

### Wave 2 (needs wave 1)

**T5 verify, no MCP needed.**
1. `python Tools/devloop/devloop.py gate game` and `lint`.
2. Before the owner opens Unity: `git status --porcelain Assets/Terrain > <scratchpad>/before.txt`.
3. Owner opens Unity on RandomWorld and waits for the title to settle.
4. Pass when all hold: the Editor is responsive in under a minute after `[TerrainLoad] domain reload`;
   `grep -c "TerrainNavBaker: tile" Logs/Editor.log` is 0 for this session; `git status` under
   `Assets/Terrain` equals `before.txt`; any `[TerrainRebake] startup event` lines are followed by no bake.
5. If a bake still runs: the `[TerrainRebake]` stack names the path. `touch UserSettings/TerrainAutoRebake.off`
   to keep working, then fix that one path (likely `HandleSplineChanged`; same first-sight rule on the spline).
6. Once MCP is up: run fixture `TerrainRebakeGateTests`; in the Editor change a ramp's `widthMetres` and confirm
   exactly that ramp's tiles rebuild with a progress bar; move a river knot and confirm water still rebakes.

**T6 notes.** Add to `Memories/Code/Terrain.md`: OnValidate fires on every load, events must be change-gated
through `ShouldRaiseForValidate`; the kill-switch file; the `[TerrainRebake]` grep. Add the kill switch to
section 4 of `Spencer/NaturalCliffs_Resume_Wave3.md`. Retire this spec to `Tasks/Verification/`.

## 5. Risks

- **A real edit is swallowed.** Only if Unity never validated or enabled the component before the edit;
  `RememberSerializedState` in `OnEnable` covers it. V1 covers creation.
- **Wave 3 files.** T2 and T4 touch none of the 12 uncommitted wave-3 files. Commit this fix by pathspec on its
  own, before wave 3.
- **The modified `Tile_*` files** in the working tree come from these startup bakes; the wave-3 regenerate
  replaces them, so leave them.
