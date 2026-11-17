# Save and Title: exact-moment saves, title menu, instant Continue

> **Status:** 🔨 code built 2026-11-17 (Phases 1-5); open: run SL-2-6 + SL-5-4 fixtures, SL-3-6 Game.unity wiring, play checks SL-2-7 + SL-4-7. Then retire to Verification.
> **Supersedes:** [`Save_System.md`](Save_System.md) phases 4 to 6 (roadmap M2-6), and covers roadmap M7-7 (menus). Phases 1 to 3 of that spec stay as they are: they become the progress layer below.
> **Execution:** the orchestrator (Opus) briefs, gates and observes. **Sonnet** workers and verifiers do the editing and reviewing, and **Haiku** does the greps and inventories. Section 3 says who does what.

## 0. What the owner asked for

1. Save the whole game state. If another character is mid-attack when you save, they are still mid-attack when you load.
2. Pressing Play in `Game.unity` opens a title menu in the style of Breath of the Wild, with New Game and Continue among the options.
3. Continue starts almost instantly, because the save was already loaded in the background while the menu was showing.
4. Eventually the menu becomes **Rive over a 3D background scene**. It should have all the usual menu features.

## 1. Owner answers (2026-11-13, do not re-ask)

| # | Answer |
|---|---|
| SM-A1 | **Two save layers.** A version-tolerant *progress* layer (today's `IPersist` JSON) plus an exact *moment* snapshot of every live dynamic entity, made with Entities' own world serializer. A save loaded on the same component layout resumes the exact moment. After a code change alters a saved component's layout, the moment layer is rejected and the save falls back to the progress layer, with units reset to Idle. Nothing is ever lost. |
| SM-A2 | The title menu opens in **`Game.unity`**. |
| SM-A3 | The menu appears in Editor Play too, with a **"Skip title in Editor"** toggle that drops straight into the scene as it is authored. |
| SM-A4 | **Rive behind a seam.** v1 is a plain UI Toolkit view behind view interfaces. The Rive view and 3D backdrop replace it later without touching the save or flow logic. This answers roadmap Q3 for menus. |

## 2. Orchestrator decisions (final unless the owner overrides)

- **SM-D1 What goes in the moment set.** Every entity tagged `MomentPersist`, together with its `LinkedEntityGroup` (body parts, attached items). The tag is opt-in and baked on the runtime-spawned prefabs: units, projectiles, hazard zones, dropped items, corpses, the caravan. It is not inferred from `SceneSection`, because instantiated subscene prefabs keep their `SceneSection` (SP-2 confirms this). Static subscene content such as terrain, buildings and features is never in the snapshot; it re-streams.
- **SM-D2 How the snapshot is written.** Copy the moment set into a staging `World` with `EntityManager.CopyEntitiesFrom`, call `SerializeUtility.SerializeWorld(staging, writer, out object[] referencedObjects)`, then dispose the staging world. If `referencedObjects` is not empty, a managed or UnityObject reference got in. That is a bug, and it is logged and listed in the save's diagnostics rather than silently dropped.
- **SM-D3 How the layout is checked.** Each snapshot stores a *layout manifest*: a sorted list of `(type full name, TypeManager StableTypeHash)` for every type in the staging world. On load, any mismatch rejects the moment layer *before* `DeserializeWorld` is called, because a mismatch inside `DeserializeWorld` throws and can leave the world half-written. The Load list shows a rejected save with an "Older version: units reset" badge.
- **SM-D4 How a snapshot is restored.** `DeserializeWorld` runs into a staging world, which the preload can do while the title is up. *Commit* then destroys every live `MomentPersist` entity, calls `MoveEntitiesFrom(staging)`, and restores the saved `World.Time.ElapsedTime` (SP-3), in that order and all in one frame inside `SaveSystemGroup`.
- **SM-D5 What the progress layer does.** It is unchanged in kind: player singleton, GameData, resources, trade, harvested nodes, WorldClock, camera pose, and the minion roster. When the moment layer commits, the minion respawn path (`PersistentLoadSystem.RestoreMinions` → `MinionRestoreApplySystem`) is skipped, because the units already came back exactly as they were. When the moment layer is rejected, that path runs as it does today.
- **SM-D6 Save files.** Each save gets its own folder under `persistentDataPath/saves/<saveId>/`, containing `header.json` (small and read by the menu), `progress.json`, `moment.bin.gz` and `thumb.png`. Writes go to `*.tmp`, then `File.Replace` swaps them in and keeps a `.bak`. If the main file is corrupt, the `.bak` is loaded. Capture happens on the main thread; compression and the disk write run on a `Task` thread, so the hitch is the capture only.
- **SM-D7 Save slots, modelled on Breath of the Wild.** A ring of **5 autosaves** that overwrites the oldest, **1 quicksave**, and **unlimited manual saves**. Continue always loads the most recent save of any kind.
- **SM-D8 When saving is allowed.** Saving is allowed mid-combat; that is the point of this spec. It stays blocked during cutscenes, which is today's rule in `PersistentSaveSystem`, and while a save or load is already in progress. Autosave additionally waits until no unit has been in combat for 3 s, so an autosave never lands mid-swing. Manual saves and quicksaves don't wait.
- **SM-D9 Holding the world during the title.** A new `SimulationHold` singleton stops every `GameSceneSystemGroup`, plus the physics step (SP-4 shows how). Rendering, subscene streaming and `GameManagerSystemGroup` world services keep running. The title therefore shows a world that is loaded and frozen.
- **SM-D10 Making Continue fast.** While the title is up, `BootFlow`:
  - reads every save header;
  - picks the newest save;
  - deserializes its moment layer into a staging world;
  - parses its progress JSON;
  - points terrain streaming at the saved player position;
  - warms the `ShaderVariantCollection`.

  Pressing Continue then commits, releases the hold and fades the title out. **Target:** under 0.5 s from the button press to player control on the dev PC, measured. If the player picks New Game or another save, the staging world is thrown away.
- **SM-D11 New Game** is the scene as authored: drop the staging world and release the hold. If saves exist, it doesn't overwrite them. It starts a new autosave ring with a new `saveId` prefix.
- **SM-D12 Return to Title** reloads `Game.unity` with `SceneManager.LoadScene`, which re-runs the boot path. That is the cleanest way to get back to the authored world state, and it reuses SM-D10, so a second Continue is instant too. Before leaving, the player is warned about unsaved progress if the last save is more than 60 s old.
- **SM-D13 The 3D background, v1.** A Cinemachine `TitleCamera` with the highest priority looks at the already-loaded world, so the backdrop costs nothing extra. Later, the Rive pass can add an additive `TitleBackdrop.unity` behind the `ITitleBackdrop` seam (section 6.4).
- **SM-D14 Editor skip.** A `StitchPunk/Skip Title in Editor` menu toggle, stored in `EditorPrefs` and read through `#if UNITY_EDITOR` in `BootFlow`. When it is on, `BootFlow` releases the hold on the first frame, preloads nothing and shows no menu. In builds, the title always shows.

## Skills Needed

- `dots-system-scaffold`: `MomentSnapshotCommitSystem`, `SimulationHoldPhysicsSystem`, `AutoSaveCombatQuietSystem`.
- `dots-authoring-baker`: the `MomentPersistAuthoring` baker.
- `dots-test`: the PlayMode round-trip fixture (SL-2-6) and the EditMode slot-store and menu-controller fixtures.
- `unity:ui-uitk`: the v1 views.

## 3. Execution protocol (token budget)

| Role | Model | Does | Never |
|---|---|---|---|
| Orchestrator | Opus | briefs (pasted snippets, at most 2 named files, named line ranges), reads diffs, runs `devloop gate` and `lint`, all Unity MCP work (spikes through `execute_code`, refresh/console, tests, asset and prefab edits, captures), commits | writes code, except a one-line fix found while reading a diff |
| Worker | **Sonnet** (`worker`) | one task row; runs `python Tools/devloop/devloop.py gate <assembly>` before its report; stops editing at turn 30; reports in 30 lines or fewer | touches Unity MCP, or files not named in its brief |
| Verifier | **Sonnet** (`verifier`) | checks a finished wave against the brief, and audits UI captures against EditorStyleGuide-independent rules (game UI: section 6.3) | edits |
| Scout | **Haiku** (`Explore`, `model: haiku`) | greps and inventories, returning a list with `file:line`, at most 40 lines | judges or designs |

**Tags.** `[parallel-safe]` tasks share one spawn message per wave. A `[shared]` task touches a file in section 9 and runs alone. An `[orchestrator]` task is MCP or Editor work only. `[haiku]` is a scout task. The compile gate runs once per wave (from feedback memory "Parallel Small Subagent Tasks"), plus each worker's own gate.

## 4. Read first (orchestrator)

`Memories/Code/RULES.md`, `Gotchas.md` (the "generic save only copies IComponentData" entry and the terrain streaming entries), `Systems.md` (the save section), `Terrain.md` (streaming), and this spec's sections 5 and 6.

## 5. Existing surface (verified 2026-11-13)

- `Systems/SaveSystemGroup/`: `PersistentSaveSystem` (writes Player, GameData and minion records as Base64 raw bytes, skips saving during a cutscene, writes one JSON file with `File.WriteAllText`), `PersistentLoadSystem` (patches the singletons; `RestoreMinions` instantiates body prefabs with `RestorePending`), `MinionRestoreQueue`, `PersistRegistry` (`IPersist` scan, plus `LocalTransform` as an external type, which filters out Entity and Blob fields), `SaveSerialization`, `SavePaths` (`save_slot_<n>.json`), the Resource/Trade/HarvestedNode save-and-load sync pairs, `AutoSaveTimerSystem`, `PlayTimeTrackerSystem`.
- `Components/Save/`: `SaveRequest` and `LoadRequest` (enableable components on `GameDataTag`, where slot 0 is autosave and 1 to 3 are manual), `PersistId`, `RestorePending`, `GameSettings` (`IPersist`).
- `Data/Structs/SaveFile.cs`: `SaveFile { version, header, entities[] }`, `SaveHeader { timestampUnix, totalPlaySeconds, sceneLabel }`.
- `MonoBehaviours/SaveLoadBridge.cs`: `RequestSave(int)` and `RequestLoad(int)`. `GameMenuManager.cs`: the pause panel, which sets `Time.timeScale` to 0 or 1 and has `OnSaveButton` and `OnLoadButton`. `DebugSaveMenu.cs`.
- `UI/MainMenuUI.cs`: a 33-line stub that calls `SceneManager.LoadScene(1)`. `UI/SettingsMenuUI.cs` is 617 lines and has `OpenFromMainMenu()`, so it is reused for Options.
- `SystemGroups.cs:20`: `GameSceneSystemGroup.OnCreate` → `RequireForUpdate<GameSceneTag>()`, the single gate point.
- Entities: `SerializeUtility.SerializeWorld(EntityManager, BinaryWriter, out object[])` (line 758) and `DeserializeWorld(ExclusiveEntityTransaction, BinaryReader, object[])` (line 1210) in `Library/PackageCache/com.unity.entities@e30ad8d00609/Unity.Entities/Serialization/SerializeUtility.cs`.
- `ProjectSettings/EditorBuildSettings.asset` lists `Assets/Scenes/Main.unity`, which does not exist. SL-3-4 fixes it.

## 6. Pinned surfaces (workers do not change these signatures)

### 6.1 Save store (`_Scripts/Core/Saving/`, pure C#, no Entities)

```csharp
public enum SaveKind { Autosave, Quicksave, Manual }

[Serializable] public class SaveHeaderV2 {
    public int formatVersion;          // 2
    public string saveId;              // folder name
    public SaveKind kind;
    public long timestampUnix;
    public double totalPlaySeconds;
    public string locationLabel;       // region/tile name, "" if unknown
    public string buildVersion;        // Application.version
    public bool hasMomentLayer;
    public string momentLayoutHash;    // hash of the layout manifest, for the "units reset" badge
}

public static class SaveSlotStore {
    public static IReadOnlyList<SaveHeaderV2> ListHeadersNewestFirst();          // header.json only
    public static bool TryGetNewest(out SaveHeaderV2 header);
    public static string CreateSaveFolder(SaveKind kind);                         // applies the autosave ring (5)
    public static void WriteFileAtomically(string saveId, string fileName, byte[] bytes); // tmp → File.Replace(.bak)
    public static bool TryReadFile(string saveId, string fileName, out byte[] bytes);     // falls back to .bak
    public static void DeleteSave(string saveId);
}
```

### 6.2 Moment layer (`_Scripts/Systems/SaveSystemGroup/`)

```csharp
public struct MomentPersist : IComponentData { }                       // Components/Save/PersistComponents.cs
public struct SimulationHold : IComponentData { }                      // Components/Save/GameDataComponents.cs

public static class MomentSnapshot {
    public static byte[] Capture(EntityManager liveEntityManager, out string layoutHash, out int referencedObjectCount);
    public static bool LayoutMatches(byte[] snapshotBytes, out string mismatchReason);
    public static World DeserializeIntoStagingWorld(byte[] snapshotBytes);  // caller owns and disposes the world
    public static void Commit(EntityManager liveEntityManager, World stagingWorld, double savedElapsedTime);
}

// Set by BootFlow or the load path; MomentSnapshotCommitSystem commits it at the next SaveSystemGroup update.
public static class PendingMomentCommit {
    public static World StagingWorld;
    public static double SavedElapsedTime;
}
```

`Capture` writes `[manifest length][manifest JSON][SerializeWorld bytes]`.

### 6.3 Menu seam (`_Scripts/UI/Menus/`)

```csharp
public enum TitleScreen { Splash, Main, LoadList, ConfirmNewGame, Options, Credits }
public interface ITitleMenuView {
    event Action<TitleCommand> CommandIssued;   // Continue, NewGame, OpenLoadList, LoadSave(id), DeleteSave(id), Options, Credits, Quit, Back
    void Show(TitleScreen screen, TitleMenuModel model);
    void Hide(float fadeSeconds);
}
public interface IPauseMenuView { /* same shape: Resume, Save, Load, Options, ReturnToTitle, Quit */ }
public interface ITitleBackdrop { void Enter(); void Exit(float fadeSeconds); }
public sealed class TitleMenuController { /* pure C# state machine; EditMode-testable */ }
```

Game UI rules for these views: they work with pad, keyboard and mouse; the first button has focus on open; Back/B always goes up one screen; and every destructive action (delete, New Game over existing saves, Return to Title with unsaved progress) gets a confirm screen. These are game screens, not editor windows, so `EditorStyleGuide.md` does not apply.

### 6.4 Boot flow (`_Scripts/MonoBehaviours/BootFlow.cs`)

`Awake`: create `SimulationHold`. If the Editor skip is on, remove it and stop. Otherwise show `ITitleMenuView` on Splash, start `PreloadNewestAsync()` (UniTask), and point `ITitleBackdrop.Enter()` at the title camera. The Continue button is enabled once the preload finishes. Until then it shows a small spinner, but it can be pressed, and a press waits for the preload to finish.

## 7. Spikes (orchestrator via `execute_code`, results appended here under section 12)

| # | Question | Pass condition |
|---|---|---|
| SP-1 | Enter Play in `Game.unity` with Skip on, spawn 20 units, start a fight, then run `CopyEntitiesFrom` → `SerializeWorld` on a staging world | `referencedObjects.Length == 0` (or a list of the offenders); serialize time and byte size recorded; a deserialize into a second staging world gives the same entity count and the same `LocalTransform` and combat state on 3 sampled units |
| SP-2 | Does a unit instantiated from a subscene prefab keep `SceneSection`? Does a unit moved in through `MoveEntitiesFrom` survive a terrain tile unload? | Recorded yes/no; decides whether commit must strip `SceneSection` |
| SP-3 | Do saved timers use absolute `ElapsedTime`? Can `World.SetTime` restore it, and does `UpdateWorldTimeSystem` keep accumulating from it? | Elapsed time continues from the restored value over 60 frames |
| SP-4 | Can `SimulationHold` gate the `GameSceneSystemGroup` subclasses and the physics step without anything moving? | A unit in mid-air does not fall during a 30 s hold; frame time while held is recorded |
| SP-5 **[haiku]** | List (a) runtime `Instantiate` call sites and the prefabs they spawn, (b) `static` non-readonly fields in `Systems/` holding runtime state, (c) `class` `IComponentData` and companion GameObjects on unit prefabs, (d) fields storing `ElapsedTime`-based absolute times | Four lists with `file:line`; this feeds SL-2-1 and SL-2-5 |

If SP-1 fails because snapshot entities carry managed references that cannot be removed, stop and bring it to the owner, because SM-A1 depends on it. The fallback is to mark the remaining combat and AI state `IPersist` and extend the progress layer to cover it.

## 8. Phases

### Phase 1: save store (independent of the spikes, can run in parallel with them)

| ✓ | Task | Files | Fixture fails if |
|---|---|---|---|
| [x] | SL-1-1 **[parallel-safe]** | `Core/Saving/SaveSlotStore.cs`, `Tests/SaveSlotStoreTests.cs` | a sixth autosave does not delete the oldest; a corrupt `header.json` hides the save instead of falling back to `.bak`; a write interrupted after the `.tmp` (simulated) leaves the old file unreadable |
| [x] | SL-1-2 **[parallel-safe]** | `Core/Saving/SaveHeaderV2.cs` (with `SaveKind`) | gate only |
| [x] | SL-1-3 **[parallel-safe]** | `MonoBehaviours/SaveThumbnailCapture.cs` | gate only. 320×180 PNG from `ScreenCapture.CaptureScreenshotAsTexture` at end of frame, UI layer excluded if possible |
| [x] | SL-1-4 **[shared]** (after 1-1/1-2) | `PersistentSaveSystem.cs`, `PersistentLoadSystem.cs` | gate only. Write and read `progress.json` through `SaveSlotStore`. `SaveRequest.slot` becomes a `SaveKind` plus a target `saveId`; keep the field name `slot` and add `int kind`, where -1 means load the newest. Delete `SavePaths.cs` once there are no callers (a Haiku grep confirms) |

### Phase 2: moment layer (after SP-1 to SP-3 and SP-5)

| ✓ | Task | Files | Fixture fails if |
|---|---|---|---|
| [x] | SL-2-1 **[parallel-safe]** | `Components/Save/PersistComponents.cs` (+`MomentPersist`), `Authoring/Save/MomentPersistAuthoring.cs` | gate only |
| [x] | SL-2-1b **[orchestrator]** | prefabs from SP-5(a) | `MomentPersistAuthoring` added to each spawned prefab through MCP, then rebaked |
| [x] | SL-2-2 **[parallel-safe]** | `SaveSystemGroup/MomentSnapshot.cs` | gate only. Capture: query `MomentPersist`, expand through `LinkedEntityGroup`, stage, write the manifest, serialize. If SP-2 says yes, the staging copy removes `SceneSection`/`SceneTag` |
| [x] | SL-2-3 **[parallel-safe]** | `SaveSystemGroup/MomentSnapshotCommitSystem.cs` | gate only. Runs `[UpdateAfter(PersistentLoadSystem)]`; commits `PendingMomentCommit` per SM-D4, then sets a `MomentCommitted` flag for this frame so the minion restore path is skipped |
| [x] | SL-2-4 **[shared]** | `PersistentSaveSystem.cs`, `PersistentLoadSystem.cs` | gate only. Save also writes `moment.bin.gz` and `savedElapsedTime`. On load: if the layout matches, deserialize into staging and set `PendingMomentCommit`, then skip `RestoreMinions`; otherwise use today's path |
| [x] | SL-2-5 **[parallel-safe]** (one worker per two files) | files from SP-5(b) and SP-5(d) | per item: a static runtime cache is cleared or rebuilt on commit; a stored absolute time either stays valid through SP-3 or becomes a relative "remaining seconds" value |
| [ ] | SL-2-6 **[parallel-safe]** | `Tests/PlayMode/Save/MomentSnapshotTests.cs` | a unit mid-attack (attack state plus timer) does not return to the same state and timer after Capture → Deserialize → Commit; an altered manifest hash is accepted instead of rejected; a body-part child is missing after commit |
| [ ] | SL-2-7 **[orchestrator]** | Play check | In `Game.unity`, with Skip on: start a fight, quicksave, wait 5 s, load. The same swings land at the same moment and the projectiles are still in flight. Record the capture time in ms (the hitch) |

### Phase 3: holding the world and booting

| ✓ | Task | Files | Fixture fails if |
|---|---|---|---|
| [x] | SL-3-1 **[shared]** | `Systems/SystemGroups.cs` (the `GameSceneSystemGroup.OnUpdate` override skips while `SimulationHold` exists), `Components/Save/GameDataComponents.cs` (+`SimulationHold`) | gate only |
| [x] | SL-3-2 **[parallel-safe]** | `SaveSystemGroup/SimulationHoldPhysicsSystem.cs` | gate only. Uses whatever mechanism SP-4 found (for example, toggling `PhysicsSystemGroup.Enabled`) |
| [x] | SL-3-3 **[parallel-safe]** | `MonoBehaviours/BootFlow.cs` | gate only. Implements 6.4 and SM-D10; logs `[BootFlow] preload ms`, then `continue→control ms` |
| [x] | SL-3-4 **[parallel-safe]** | `Editor/Boot/SkipTitleInEditorMenu.cs` | gate only. Menu toggle plus checkmark, stored in `EditorPrefs` |
| [x] | SL-3-5 **[shared, terrain peer]** | the terrain streaming focus (file named by a Haiku grep for the streaming focus source) | gate only. Adds an override focus position that `BootFlow` sets before the hold is released. **Tell the terrain session first** |
| [ ] | SL-3-6 **[orchestrator]** | `Game.unity`, build settings | Add `BootFlow` and the title camera to `Game.unity`; replace the missing `Main.unity` entry with `Game.unity`; retire `MainMenuUI.cs` once nothing references it. Play: the title appears in under 2 s, the world is frozen behind it, and Continue reaches control in under 0.5 s (measured from the log) |

### Phase 4: menus (UI Toolkit v1 behind the seam)

| ✓ | Task | Files | Fixture fails if |
|---|---|---|---|
| [x] | SL-4-1 **[parallel-safe]** | `UI/Menus/TitleMenuController.cs` (+ the 6.3 interfaces), `Tests/TitleMenuControllerTests.cs` | Back from LoadList does not return to Main; NewGame with existing saves skips ConfirmNewGame; Continue is offered when there are no saves |
| [x] | SL-4-2 **[parallel-safe]** | `UI/Menus/UITKTitleMenuView.cs`, `UI/Menus/TitleMenu.uxml` (+`.uss`) | gate only. Splash ("Press any button"), Main (Continue with thumbnail, location, play time and date; New Game; Load Game; Options → `SettingsMenuUI`; Credits; Quit, hidden on consoles), focus rules from 6.3 |
| [x] | SL-4-3 **[parallel-safe]** | `UI/Menus/SaveListView.cs`, `UI/Menus/SaveList.uxml` | gate only. Shared by title Load, pause Save and pause Load: rows with thumbnail, kind icon, location, play time and date, the "units reset" badge, Delete with confirm, and "New save" at the top in Save mode |
| [x] | SL-4-4 **[shared]** | `MonoBehaviours/GameMenuManager.cs` → `PauseMenuController` + `IPauseMenuView` | gate only. Resume, Save, Load, Options, Return to Title (SM-D12 warning), Quit |
| [x] | SL-4-5 **[parallel-safe]** | `UI/SaveIndicatorUI.cs` | gate only. A corner icon while a save is being written; a "Saved" toast; a "Can't save during a cutscene" toast |
| [x] | SL-4-6 **[parallel-safe]** | `MonoBehaviours/QuicksaveInput.cs` + an Input System action | gate only. F5 quicksave and F9 quickload by default (pad: none); rebindable through `InputRebinding` |
| [ ] | SL-4-7 **[orchestrator]** | captures of Splash, Main, LoadList, Confirm, the pause menu and the save list (Sonnet verifier audits them against the 6.3 rules) | Pad-only walk-through: New Game → play → pause → save → Return to Title → Continue lands at the saved moment |

### Phase 5: triggers and robustness

| ✓ | Task | Files | Fixture fails if |
|---|---|---|---|
| [x] | SL-5-1 **[parallel-safe]** | `SaveSystemGroup/AutoSaveCombatQuietSystem.cs`, `AutoSaveTimerSystem.cs` | gate only. The SM-D8 3 s combat-quiet gate; autosaves become `SaveKind.Autosave` |
| [x] | SL-5-2 **[parallel-safe]** | `SaveSystemGroup/TravelAutoSaveSystem.cs` | gate only. The old Save_System phase 5 (50 m threshold), feeding the autosave ring |
| [x] | SL-5-3 **[parallel-safe]** | `PersistentSaveSystem.cs` write path → `Task.Run` compress + `WriteFileAtomically` | gate only. Capture stays on the main thread; logs the main-thread ms |
| [ ] | SL-5-4 **[parallel-safe]** | `Tests/PlayMode/Save/SaveFallbackTests.cs` | an old progress JSON missing a newly added field fails to load; a rejected moment layer still restores the player position |

### Phase 6: Rive and backdrop (not scheduled; becomes its own spec when the art exists)

`RiveTitleMenuView : ITitleMenuView`, `RivePauseMenuView`, `TitleBackdrop.unity` behind `ITitleBackdrop`. Flow and save code are not touched.

## 9. Shared files and other sessions

Files that need a solo Sonnet worker: `SystemGroups.cs`, `PersistentSaveSystem.cs`, `PersistentLoadSystem.cs`, `GameMenuManager.cs`, the terrain streaming focus file, `Game.unity` and the unit prefabs (orchestrator only). **A terrain session is active (2026-11-13).** Run `ListAgents` before SL-3-5 and SL-3-6. If it is working on `main`, use a worktree (feedback memory "Worktree When Peer Busy").

## 10. Effects on other docs

- `Save_System.md`: its status line points here, and phases 4 to 6 are marked superseded. For moment entities, the Entity remap is no longer needed, because `SerializeWorld` remaps internally. It is only still needed for any `Entity` field on a progress-layer singleton, and none exists today. `TradeOrders` already avoids them.
- `Roadmap.md`: M2-6 and M7-7 point here; Q3 is answered "Rive behind a seam for menus (SM-A4)".

## 11. Verification (becomes `Tasks/Verification/verify-save-and-title.md`)

- [ ] Play `Game.unity`: the title appears with the world frozen behind it; nothing moves for 30 s.
- [ ] Continue reaches control in under 0.5 s (BootFlow log) and lands on the newest save.
- [ ] Mid-fight quicksave, then quickload: the same attack is mid-swing and projectiles are in flight.
- [ ] Change a saved component's layout (add a field), recompile, load an old save: the "units reset" badge shows, the player position and resources are kept, and the units are Idle.
- [ ] Six autosaves leave five; deleting a save asks for confirmation first.
- [ ] Return to Title, then Continue: still instant.
- [ ] With Skip title in Editor on, Play goes straight into the authored scene.
- [ ] The whole menu works with only a pad.

## Open decisions (defaults apply until the owner answers; none blocks Phase 1)

- [ ] SM-Q1: Autosave ring size 5 and unlimited manual saves (SM-D7). Default as written.
- [ ] SM-Q2: Should Continue wait for the 3 s combat-quiet window before an autosave counts as "newest"? Default no: the newest save is the newest save.
- [ ] SM-Q3: Quicksave keys F5 and F9. Default as written.
- [ ] SM-Q4: Credits content. Default is a placeholder screen.
- [ ] SM-Q5: Should the title background be the live world (SM-D13) or a dedicated backdrop scene from day one? Default: live world until Phase 6.

## 12. Log (one line per wave, newest last)

- 2026-11-13 spec written; owner answers SM-A1 to SM-A4 recorded.
- 2026-11-13 Phase 1 wave: SaveSlotStore + SaveHeaderV2 (Core), SaveSlotStoreTests 3/3 green, SaveThumbnailCapture. Tests asmdef now references Core + UI.
- 2026-11-13 spikes (CampLoop.unity, 14 units incl. the player):
  - **SP-1 pass, with two corrections to SM-D2.** 14 unit roots expand through `LinkedEntityGroup` to 547 entities. Copy takes 3 ms, serialize 29 ms, deserialize 3 ms. Raw size is 574 KB, 37 KB gzipped (gzip 60 ms, so it runs off-thread). Round-trip gives 547→547 entities with matching positions. Correction (a): `SerializeWorld` throws on `BlobAssetOwner` (a pointer field), so the staging copy must strip it. `DeserializeWorld` re-adds its own. Correction (b): `referencedObjects` is never empty, because all 103 entries are render Materials/Meshes from `RenderMeshArray`. So they are not a bug. They are written as an object table `(type, name)` in the manifest and resolved on load with `Resources.FindObjectsOfTypeAll`. An unresolved entry rejects the moment layer, which falls back to progress.
  - **SP-2 yes.** Instantiated units and the player all carry `SceneSection`/`SceneTag`. The staging copy strips both, so committed entities belong to no section and survive tile unloads by construction. Known limit: `CopyEntitiesFrom` nulls Entity fields that point outside the moment set (for example a target on a static building), so the AI re-picks. The **player is a unit** and is in the moment set, so unit→player targets survive.
  - **SP-3 yes, plus one fix.** `UpdateWorldTimeSystem` accumulates from the current `ElapsedTime`, so `World.SetTime` sticks. The 15+ AI/combat `timestamp`/`*ReadyAtSeconds` fields therefore stay valid without conversion, and SL-2-5 shrinks to the two statics (`MinionRestoreQueue.nextId` is harmless; `PersistRegistry.initialized` is a type cache that stays valid). But `FixedRateCatchUpManager.m_LastFixedUpdateTime` does not follow, so physics would stall after a backward jump or fast-forward after a forward one. Commit realigns it by reflection and logs a warning if the field is missing.
  - **SP-4 pass.** Zero entities moved over 297 held frames at 16.6 ms per frame, but only when the hold covers every `GameSceneSystemGroup` except `TerrainStreamingSystemGroup` and `SaveSystemGroup`, **plus** `PhysicsSystemGroup`, `DotsMovementToolkit.MovementSystemGroup` and `DotsAnimationToolkit.AnimationToolkitSystemGroup`. Those last three are not scene-gated. `TerrainSystemGroup` must be held as well, because it runs the abstract unit tier, which walks units. So SL-3-2 becomes `GameManagerSystemGroup/SimulationHoldSystem.cs`, which toggles the three foreign groups the same frame.
  - **SP-5** inventory: unit bodies come from `UnitSpawnerSystem`, `WorkstationSummonSpawnSystem` and `PersistentLoadSystem`; projectiles from `AiShootSystem`; there are also `WorkstationDeploySystem`, `PigeonSpawnSystem` and `StartingWeaponSpawnSystem`. There are no class IComponentData and no `AddComponentObject`. Absolute times are listed in the agent output and covered by SP-3.
  - **Assembly correction to section 6.3/6.4.** UI references MonoBehaviours, so `BootFlow` (MonoBehaviours) cannot see `UI/Menus`. The interfaces and `TitleMenuController` therefore move to `Core/Menus/` (pure C#, referenced by both). The views stay in `UI/Menus/`.
  - `Game.unity` has 25 entities and no gameplay subscene. The title flow is built for it as SM-A2 says, and the play checks run in CampLoop until Game.unity has content.
- 2026-11-17 audit + wave (Opus orchestrator, Sonnet verifiers/workers, Haiku scouts). Found Phases 1-5 already coded but uncommitted; two verifiers audited it against this spec.
  - **Fixes:** `MomentSnapshot.Commit` now disposes the staging world in `finally` (a throw after the destroy used to leak it); F5/F9 are ignored while `SimulationHold` exists; Continue/LoadSave guarded against double presses; confirm screens focus "No"; `preload ms` is also logged with no saves.
  - **SL-2-3 deviation:** no `MomentCommitted` flag; `PersistentLoadSystem` decides the minion-restore skip inline when it hands off `PendingMomentCommit`. **SL-3-2 lives at** `GameManagerSystemGroup/SimulationHoldSystem.cs` (per SP-4).
  - **SL-4-3:** `UI/Menus/SaveListView.cs` (plain C#, no uxml) now serves title Load, pause Save and pause Load; Save mode can overwrite only Manual saves.
  - **SL-4-6 limit:** F5/F9 are not rebindable, because `InputRebinding` takes a single asset.
  - **SL-2-1b:** `MomentPersistAuthoring` is on PlayerUnit, MaleCitizen (Bandit and the three PlayerZombie prefabs are variants and inherit it), Bullet, Pistol, Rifle, Workstation, CarrierPigeon. Corpses are the same unit entity. No hazard zone, dropped item or caravan is spawned at runtime today.
  - **Risk for the play check:** PlayerUnit is *placed* in the CampLoop/TerrainWorld subscenes, so after a commit the authored player could come back as a duplicate if its subscene ever unloads and reloads mid-session.
  - **Build settings:** Game.unity only, at index 0 (`Main.unity` removed).
  - **Tests:** `Tests/PlayMode/Save/MomentSnapshotTests.cs` (3) and `SaveFallbackTests.cs` (1; a size mismatch skips only that component) pass the gate but have **not run**: a peer's dirty TerrainWorld.unity blocks the scene switch PlayMode needs.
