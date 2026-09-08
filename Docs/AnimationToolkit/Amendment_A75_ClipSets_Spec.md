# Amendment A75 — Clip Sets tab: browse, create and edit clip sets inside the Clip Editor

> **Status:** 📋 specced 2026-09-08, not built.
> **Executor:** one orchestrating session (Editor-connected, runs the gate, commits) plus **small
> Sonnet `worker` subagents that only edit files** — a subagent never touches `mcp__UnityMCP__*`.
> Every task below is sized for one subagent **under ~100k tokens**: at most two files to edit,
> named line ranges to read, the snippets it needs pasted into its brief, no browsing. Tasks marked
> **[parallel-safe]** may run at the same time as every other task carrying the same marker in the
> same wave; the orchestrator runs **one** compile gate over a wave.
> **Protocol:** `Assets/_Vault/Tasks/NewPlans/Cutscene_Roadmap.md` §4 (binding) and
> `Docs/AnimationToolkit/HANDOFF.md` §2 (A69 comment rule, static-class suffixes, UI Toolkit only)
> and §3 (gate + discovered counts). Commit prefix `A75-Tn`.
> **Version:** the next unused minor. On 2026-09-08 the working tree already carried an
> uncommitted `0.21.0` ("New Rig source preview", `CHANGELOG.md:11`), so this lands as `0.22.0`
> unless the head of `CHANGELOG.md` has moved again — read it at T0. One entry under that version,
> headed "A75 — clip sets tab". (The New Rig preview session used `Library/A75Captures/` for its
> PNGs; this amendment's captures go to `Library/A75ClipSetsCaptures/` so neither overwrites the other.)

---

## 1. What the owner asked for (2026-09-08, verbatim where it matters)

> "I want to rename New Set tab to "Clip Sets" and give it a new window, here you will be able to
> either edit your existing clipsets by adding or removing clips and you will be able to create new
> ones where you can set its name, pick a save path (save paths will be remembered so you wont have
> to do this everytime), but also add existing clips to the clip set, so there will be a list I can
> scroll and search with check boxes next to the clips I can click them and choose to add those
> clips to the new clip set I am creating."

Today "New Set" is a `ToolbarButton` beside the Clip Set field (`ClipEditorWindow.uxml:7`,
bound at `ClipEditorWindow.cs:936-941`) that opens a save dialog and makes an empty set
(`CreateClipSet`, `ClipEditorWindow.cs:1256-1281`). Adding an existing clip to a set is possible
nowhere in the package except by hand in the `ClipSetAsset` inspector's list.

| # | Requirement | Where it lands |
|---|---|---|
| R1 | **"Clip Sets" is a tab** in the strip, like New Rig, drawn over the dock. The toolbar's "New Set" button goes. | §2 D1, §4.4, T6a/T6b/T6c |
| R2 | **Browse every clip set in the project** and pick one to edit. | §4.2 catalog column, T5 |
| R3 | **Edit a set: add and remove clips** from a scrollable, searchable, check-boxed list of every clip in the project. | §4.2 picker, T1, T2, T4, T5 |
| R4 | **Create a set: name it, choose a save folder, tick the clips it starts with.** | §4.2 create form, T3, T5 |
| R5 | **The save folder is remembered** across sessions. | §2 D4, T3 |

The vault's standing lesson applies (`feedback_visual_first_editor_tools`): the list, the ticks and
the live "will create …" path are the feature. A log line is not.

## 2. Decisions (recorded — do not re-ask)

- **A75-D1 — Clip Sets is a tab, and the toolbar button goes.** `ClipEditorTab` gains
  `ClipSets = 1`, shifting `ClipEditor`…`CutsceneEditor` up by one — the enum's own comment says
  values are display order, and the tab strip reads New Rig · **Clip Sets** · Clip Editor · VAT Bake ·
  Actor Editor · Cutscene Editor. `sessionTab` / `CarriedState.tab` are ints that live one session, so
  the first reload after this lands can restore one wrong tab once; nothing persists them further.
  `tabToggles` becomes `new ToolbarToggle[6]`. The `new-clip-set-button` element, its binding and
  the window's `CreateClipSet()` are deleted, not hidden.
- **A75-D2 — Two columns, built in C#, styled by the shared sheet.** Left: a **catalog** of every
  `ClipSetAsset` in the project (fixed 280 px). Right: the **editor** for the selected set or for a
  new one (`flexGrow = 1`). Same shape as `NewRigPanel` (form column + pane), same lazy build in
  `ShowClipSetsTab`, same `IDisposable`. Inline styles are layout only (A72); rows are
  `toolkit-box` boxes, headers are `toolkit-pane-header`, buttons come from `ToolkitIcons`.
- **A75-D3 — Edit mode applies each tick immediately; create mode accumulates until Create.** With a
  real set selected, ticking a clip calls `ClipAssetUtility.AddExistingClipToSet` and unticking calls
  `ClipAssetUtility.RemoveClipFromSet(set, clip)` — one undo step each, no Apply button to forget.
  With no set yet, ticks are held by the picker and written after the asset exists.
- **A75-D4 — The save folder is an `EditorPrefs` string, validated on read.** Key
  `DotsAnimationToolkit.ClipSets.SaveFolder`, written on every successful Create and every folder
  pick. Read through `AssetDatabase.IsValidFolder`; a stale or foreign-project value falls back to
  the open clip set's folder, else `Assets`. A folder picked outside this project is refused with a
  sentence in the result label, never silently mapped.
- **A75-D5 — The name is sanitized and the path uniquified, and the panel shows the resolved path
  live.** Invalid filename characters are stripped, an empty name becomes `NewClipSet`, and
  `AssetDatabase.GenerateUniqueAssetPath` picks the final path. The hint under the name field reads
  `Will create Assets/Animations/NewClipSet 1.asset` and updates on every keystroke and folder pick.
  A create never overwrites.
- **A75-D6 — The catalog and the picker are scanned on show, on Refresh and after Create. No
  `AssetPostprocessor`.** A postprocessor would fire on every import in the project for a tab most
  sessions never open. `ShowClipSetsTab(true)` calls `SetSource(clipSet)` which rescans; a Refresh
  action in the catalog header covers "I made a clip somewhere else meanwhile".
- **A75-D7 — One row per `ClipAsset` in the project: tick · name · dim folder.** The search matches
  name or asset path, case-insensitive, and a **Ticked only** toggle narrows to the set's members.
  The count label reads `3 ticked · 42 of 120 shown`. The list is a virtualized `ListView`
  (`fixedItemHeight = 22`), never a `ScrollView` of hand-built rows — a project can hold hundreds
  of clips.
- **A75-D8 — Duplicates.** `ClipSetAsset.clips` may legally hold one clip twice (deduplicated at
  bake). The picker shows it ticked once; unticking removes **every** entry; ticking a clip already
  present is refused (`AddExistingClipToSet` returns false) rather than appended.
- **A75-D9 — The panel reports, the window acts.** Like `NewRigPanel`, the panel never touches the
  window's `clipSet`. Three events: `SetClipsChanged(ClipSetAsset)` — the window refreshes its clip
  list, validation badge and preview when that is the open set; `ClipSetCreated(ClipSetAsset, bool
  loadIntoEditor)` — the window writes `clipSetField.value` when asked; `OpenInEditorRequested(ClipSetAsset)`
  — the window loads it and switches to the Clip Editor tab. After a create with **Load** ticked the
  panel also raises `Closed` (→ Clip Editor tab); with it unticked the panel stays open with the new
  set selected in the catalog, ready for another.
- **A75-D10 — Result colours are the palette's.** The result label's ok/error colour is set from
  `ToolkitPalette.Clean` / `ToolkitPalette.Error` inline with the one-line "data-driven colour"
  comment A72 allows — the same two values the USS tokens `--toolkit-color-clean` / `--toolkit-color-error`
  carry, so `ToolkitPaletteTests` keeps them honest. `NewRigPanel`'s hard-coded greens and reds are
  pre-A72 and are **not** touched here.
- **A75-D11 — Pure logic is a plain instance class, not a static one.** `ClipPickerModel` and
  `ClipSetSaveLocation` are `public sealed class`es so `Conformance_G`'s suffix vocabulary does not
  apply and no allowlist entry is needed. The only static class touched is `ClipAssetUtility`
  (already `Utility`, already in `Editor/ClipUtilities/`).
- **A75-D12 — The `ClipSetAsset` inspector is unchanged.** An "Open in Clip Sets" button there is a
  recorded follow-up (§6), not part of this amendment.

## 3. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`.
2. `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections **"Clips, sets and rigs are
   independent"** (a set names no rig — this panel must not grow a rig field), **"Shared editor
   chrome"** (inline styles are layout only; one root KeyDown; icon + word goes through
   `SetButtonIconAndText`) and **"Never rebuild a pane from a value-changed callback"**.
3. `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Authoring/NewRigPanel.cs:1-120, 320-377`
   — the panel shape being mirrored (two columns, `Closed` / `RigCreated` events, `ReportFailure`).
4. `Editor/ClipUtilities/ClipAssetUtility.cs` in full (≈260 lines) — `AppendClipToSet` (private,
   `:78-95`) and `RemoveClipEntry` (`:125-172`) are what T1 wraps.
5. `Editor/ClipEditor/ClipEditorWindow.cs:239-262` (pane fields, `tabToggles`), `:775-801`
   (disposal), `:926-941` (`BindToolbar`, the button being removed), `:1161-1171`
   (`RefreshClipList`), `:1256-1281` (`CreateClipSet`, being deleted), `:1587-1607` (`BindTabs`),
   `:1657-1698` (`ApplyActiveTab`), `:1750-1767` (`ShowNewRigTab`, the template), `:1800-1836`
   (`RefreshOpenPaneSource`, `CloseNewRigTab`, `OnNewRigCreated`).
6. `Editor/ClipEditor/ClipEditorWindow.uxml` (145 lines) and `ClipEditorTab.cs` (24 lines).
7. `Editor/ClipEditor/Shared/ToolkitIcons.cs:148-192` (`MakeIconButton`, `MakeIconTextButton`,
   `SetButtonIconAndText`), `Shared/ToolkitPalette.cs:14-27`.
8. `ClipEditorWindow.uss:239-400` — the `toolkit-pane-*` and `toolkit-box*` classes the panel uses.
9. `Editor/ClipUtilities/SharedClipBindingUtility.cs:134-148` — the `FindAssets("t:" + nameof(…))`
   scan to copy for both catalogs.
10. Tests to mirror: `Tests/EditMode/ActorEditorPanelTests.cs:1-60` (a panel is constructed with no
    window; assets via `ScriptableObject.CreateInstance`, destroyed in `TearDown`),
    `Tests/EditMode/ClipEditorLayoutTests.cs:21-85, 156-184` (the two arrays T6c edits).

## 4. Design

### 4.1 Asset surgery — `Editor/ClipUtilities/ClipAssetUtility.cs`

Two public additions, both routed through the existing private cores so a clip added here is
indistinguishable from one made by `CreateClipInSet`:

```csharp
/// Appends an existing clip to a set as one undo step. False when either is null or the clip is already in the set.
public static bool AddExistingClipToSet(ClipSetAsset clipSet, ClipAsset clip)

/// Removes every entry of clip from the set as one undo step. False when nothing was removed.
public static bool RemoveClipFromSet(ClipSetAsset clipSet, ClipAsset clip)
```

`AddExistingClipToSet`: null guards, `clipSet.clips.Contains(clip)` → false, else the existing
`AppendClipToSet(clipSet, clip)` and true. Give `AppendClipToSet` a second undo-name constant
(`AddExistingUndoActionName = "Add Clip To Set"`) by passing the name in — do not leave a user's
Ctrl+Z reading "Create Clip In Set" for an add. `RemoveClipFromSet(set, clip)`: walk
`clipSet.clips` **from the last index down** collecting indices equal to `clip`, then call the
existing `RemoveClipEntry(clipSet, index, true)` for each, inside one `Undo.IncrementCurrentGroup`
/ `CollapseUndoOperations` so several entries undo as one step. Return whether any index matched.

### 4.2 The panel — `Editor/ClipEditor/Authoring/ClipSetsPanel.cs`

`public sealed class ClipSetsPanel : VisualElement, IDisposable`. Public surface:

```csharp
public enum EditorMode { None, Create, Edit }

public event Action Closed;                                  // after a create with Load ticked
public event Action<ClipSetAsset, bool> ClipSetCreated;      // (set, loadIntoEditor)
public event Action<ClipSetAsset> OpenInEditorRequested;
public event Action<ClipSetAsset> SetClipsChanged;           // an add or remove landed on this set

public EditorMode Mode { get; }
public ClipSetAsset SelectedSet { get; }

public void SetSource(ClipSetAsset openClipSet);             // rescan; select openClipSet if non-null; folder fallback
public void LoadCatalog(IReadOnlyList<ClipSetAsset> clipSets, IReadOnlyList<ClipAsset> clips); // what the rescan feeds; public so tests feed in-memory assets
public void SelectSet(ClipSetAsset clipSet);                 // Edit mode, picker ticks = set.clips (deduplicated)
public void BeginCreate();                                   // Create mode, picker ticks cleared, name field "NewClipSet"
public void Dispose();
```

**Layout** (`style.flexGrow = 1; flexDirection = Row`, all inline styles layout-only):

- **Catalog column** — `VisualElement` named `clip-sets-catalog-column`, `width = 280`,
  `flexShrink = 0`, padding 8/10. Header: `toolkit-pane-header` with `Label` "Clip Sets"
  (`toolkit-pane-title`) and `toolkit-pane-actions` holding two `ToolkitIcons.MakeIconTextButton`s:
  **New** (`clip-sets-new-button`, icon `Toolbar Plus`, text "New") → `BeginCreate()`, and
  **Refresh** (`clip-sets-refresh-button`, icon `Refresh`, tooltip "Rescan the project for clip
  sets and clips") → `RescanProject()`. Below it a `ListView` named `clip-sets-list`,
  `fixedItemHeight = 44`, `selectionType = Single`, `flexGrow = 1`. `makeItem` returns a
  `VisualElement` with classes `toolkit-box` (and margin 2/4 inline) holding a
  `toolkit-box__header` row with a `Label` `toolkit-box__title` (set name) and, under it, a `Label`
  `toolkit-box__label` reading `12 clips · Assets/Animations` (`opacity` is a class concern — use
  `clip-editor__hint`). `bindItem` fills both. `selectionChanged` → `SelectSet(row)`; the selected
  row gets `toolkit-box--selected`. When the catalog is empty the list is hidden and a
  `clip-editor__hint` label says "No clip sets in this project yet. Press New."
- **Editor column** — `clip-sets-editor-column`, `flexGrow = 1`, `minWidth = 360`, padding 8/10.
  Header row: `Label` named `clip-set-editor-title` (`toolkit-pane-title`; "New Clip Set" in Create,
  the set's name in Edit, "Select a clip set or press New." in None) and, right-aligned, an
  icon-text button **Open in Clip Editor** (`clip-set-open-button`, icon `editicon.sml`) shown only
  in Edit mode → `OpenInEditorRequested(SelectedSet)`.
  - **Create form** (a `VisualElement` named `clip-set-create-form`, hidden outside Create):
    `TextField("Name")` named `clip-set-name-field`, `isDelayed = false`; a row with `Label` "Save
    Folder", a `Label` named `clip-set-folder-label` (`toolkit-box__label`, the current folder) and
    a `Button` "…" named `clip-set-folder-button` → `EditorUtility.OpenFolderPanel("Save Clip Set
    In", <absolute current folder>, "")` → `ClipSetSaveLocation.TryMakeProjectRelative` → on success
    `saveLocation.Remember(folder)`, else `ReportFailure(...)`; a `Label` named
    `clip-set-target-path-label` (`clip-editor__hint`) reading `Will create <path>` from
    `ClipSetSaveLocation.ResolveTargetAssetPath(folder, nameField.value)` — refreshed on every
    name-field `ChangeEvent<string>` and folder pick.
  - **Picker** — one `ClipPickerListElement` named `clip-picker`, `flexGrow = 1` (§4.3).
  - **Footer**: in Create mode a `Toggle("Load this set into the editor")` named
    `clip-set-load-toggle`, default true, and a `Button` "Create Clip Set" named
    `clip-set-create-button`, `height = 28`. In Edit mode a `Label` (`clip-editor__hint`) reading
    "Ticks apply to the set immediately. Ctrl+Z undoes." Then a `Label` named
    `clip-sets-result-label`, `whiteSpace = Normal`, bold, coloured per A75-D10.

**Behaviour:**

- `RescanProject()` (private): `AssetDatabase.FindAssets("t:" + nameof(ClipSetAsset))` and
  `"t:" + nameof(ClipAsset)`, load, sort both by `name` (`StringComparer.OrdinalIgnoreCase`), then
  `LoadCatalog`. `LoadCatalog` keeps the current selection when the selected set is still present,
  else drops to `EditorMode.None`; it always calls `picker.SetClips(clips)`.
- `SetSource(openClipSet)`: `RescanProject()`; `saveLocation.FallbackFolder` = the folder of
  `openClipSet`'s asset path when non-null, else `"Assets"`; if `openClipSet != null` and
  `Mode == None`, `SelectSet(openClipSet)`.
- `SelectSet(set)`: `Mode = Edit`, `SelectedSet = set`, title = `set.name`, catalog selection synced
  with `SetSelectionWithoutNotify`, `picker.SetCheckedClips(distinct non-null set.clips)`,
  create form hidden, Open button shown.
- `BeginCreate()`: `Mode = Create`, `SelectedSet = null`, catalog selection cleared, name field
  `"NewClipSet"`, `picker.SetCheckedClips(empty)`, create form shown, folder label =
  `saveLocation.Recall()`, path hint refreshed.
- `picker.ClipCheckedChanged += (clip, isChecked)`: in Edit mode call the T1 utility (`Add…` when
  ticked, `Remove…` when unticked); when it returns true raise `SetClipsChanged(SelectedSet)` and
  refresh the catalog row's clip count (`clipSetsList.RefreshItem(index)`). In Create mode do
  nothing — the picker holds the ticks.
- `Create()`: `folder = saveLocation.Recall()`; refuse (result label) when
  `!AssetDatabase.IsValidFolder(folder)`; `assetPath = ClipSetSaveLocation.ResolveTargetAssetPath(folder, nameField.value)`;
  `ClipSetAsset newSet = ClipAssetUtility.CreateClipSet(assetPath)`; null → `ReportFailure`. Then for
  each `picker.CheckedClips` (in picker order) `ClipAssetUtility.AddExistingClipToSet(newSet, clip)`;
  `AssetDatabase.SaveAssets()`; `saveLocation.Remember(folder)`; `EditorGUIUtility.PingObject(newSet)`;
  result label `Created "Walk" with 3 clip(s) at Assets/Animations/Walk.asset`; `RescanProject()`;
  `SelectSet(newSet)`; raise `ClipSetCreated(newSet, loadToggle.value)`; if `loadToggle.value` raise
  `Closed`.
- `ReportFailure(message)`: result label in `ToolkitPalette.Error`, plus
  `Debug.LogWarning("[DOTS Animation Toolkit] Clip Sets: " + message)` — the `NewRigPanel` shape.
- `Dispose()`: nothing native to free today; unsubscribe the picker event and clear the lists so
  a disposed panel holds no asset references. Keep the method — the window disposes every panel.

The panel must **not** call `AssetDatabase` from its constructor: `LoadCatalog` is what tests feed,
and the constructor runs in EditMode tests with no project scan wanted.

### 4.3 The picker — `Editor/ClipEditor/Authoring/ClipPickerListElement.cs` + `ClipPickerModel.cs`

**`ClipPickerModel`** (new, `public sealed class`, pure — no `UnityEditor`, no `AssetDatabase`;
takes its rows as data):

```csharp
public struct ClipPickerEntry { public ClipAsset Clip; public string Name; public string FolderPath; }

public IReadOnlyList<ClipPickerEntry> AllEntries { get; }
public IReadOnlyList<ClipPickerEntry> VisibleEntries { get; }     // recomputed by ApplyFilter
public string SearchText { get; set; }                             // setter calls ApplyFilter
public bool ShowCheckedOnly { get; set; }                          // setter calls ApplyFilter
public int CheckedCount { get; }
public IEnumerable<ClipAsset> CheckedClips { get; }                // in AllEntries order

public void SetEntries(IReadOnlyList<ClipPickerEntry> entries);   // keeps ticks for clips still present
public void SetChecked(IEnumerable<ClipAsset> clips);              // replaces the tick set
public bool IsChecked(ClipAsset clip);
public void SetCheckedState(ClipAsset clip, bool isChecked);
public string DescribeCounts();                                    // "3 ticked · 42 of 120 shown"
```

`ApplyFilter`: an entry is visible when (`SearchText` is empty or `Name` or `FolderPath` contains
it, `OrdinalIgnoreCase`) and (`!ShowCheckedOnly` or `IsChecked(Clip)`). Ticks live in a
`HashSet<ClipAsset>` keyed by instance — the same identity assumption `ClipValidation` and the set
inspector already make.

**`ClipPickerListElement`** (new, `public sealed class ClipPickerListElement : VisualElement`):

```csharp
public event Action<ClipAsset, bool> ClipCheckedChanged;
public IEnumerable<ClipAsset> CheckedClips { get; }
public void SetClips(IReadOnlyList<ClipAsset> clips);              // builds entries: Name = clip.name, FolderPath = folder of AssetDatabase.GetAssetPath(clip) or "" for unsaved
public void SetCheckedClips(IEnumerable<ClipAsset> clips);         // no event raised
```

Layout: a `toolkit-pane-header` with `Label` "Clips" (`toolkit-pane-title`) and, in
`toolkit-pane-actions`, a `ToolbarSearchField` named `clip-picker-search` (`width = 180`) and a
`Toggle("Ticked only")` named `clip-picker-checked-only`; a `Label` named `clip-picker-count`
(`clip-editor__hint`) bound to `model.DescribeCounts()`; a `ListView` named `clip-picker-list`,
`fixedItemHeight = 22`, `selectionType = None`, `flexGrow = 1`, `itemsSource = model.VisibleEntries`
(re-pointed and `Rebuild()` after every filter change). `makeItem`: a row (`flexDirection = Row`,
`alignItems = Center`) with a `Toggle` (`toolkit-pane-action`), a `Label` `toolkit-box__title`
for the name and a `Label` `clip-editor__hint` for the folder. `bindItem`: `SetValueWithoutNotify`
the toggle from `model.IsChecked`, fill both labels, and (re)register **one** change callback per
row element — store the bound entry index in the row's `userData` and read it inside the callback
rather than capturing the loop variable, or a recycled row writes the wrong clip. The callback:
`model.SetCheckedState(clip, newValue)`, refresh the count label, raise `ClipCheckedChanged(clip, newValue)`,
and if `ShowCheckedOnly` re-filter (an unticked row leaves the view). Search field
`RegisterValueChangedCallback` → `model.SearchText = …` → re-point + `Rebuild()` — a `ListView`
rebuild does not kill the search field's own focus, so this does not fall foul of the rebuild trap.

### 4.4 Save location — `Editor/ClipEditor/Authoring/ClipSetSaveLocation.cs`

`public sealed class ClipSetSaveLocation`:

```csharp
public const string PrefsKey = "DotsAnimationToolkit.ClipSets.SaveFolder";
public const string DefaultAssetName = "NewClipSet";
public string FallbackFolder { get; set; } = "Assets";

public string Recall();                                   // EditorPrefs value when AssetDatabase.IsValidFolder, else FallbackFolder
public void Remember(string projectRelativeFolder);       // EditorPrefs.SetString
public static string SanitizeAssetName(string requestedName);                 // strip Path.GetInvalidFileNameChars, trim, empty → DefaultAssetName
public static string ResolveTargetAssetPath(string folder, string requestedName); // AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Sanitize(name) + ".asset")
public static bool TryMakeProjectRelative(string absoluteFolder, string projectAssetsAbsolutePath, out string projectRelativeFolder);
```

`TryMakeProjectRelative`: normalise both to forward slashes, trim trailing slashes; the folder must
equal `projectAssetsAbsolutePath` or start with it plus `/` (ordinal, case-insensitive on Windows
— use `OrdinalIgnoreCase`); result is `"Assets"` + the remainder. Anything else → false. The caller
passes `Application.dataPath`; the test passes a literal.

### 4.5 Window integration — `ClipEditorTab.cs`, `ClipEditorWindow.uxml`, `ClipEditorWindow.cs`

- `ClipEditorTab`: insert `ClipSets = 1` after `NewRig` with the one-line summary "Browse, create
  and edit clip sets — which clips each one registers."; renumber the rest (`ClipEditor = 2`,
  `VatBake = 3`, `ActorEditor = 4`, `CutsceneEditor = 5`).
- UXML: delete line 7 (`new-clip-set-button`); after `tab-new-rig` add
  `<uie:ToolbarToggle name="tab-clip-sets" text="Clip Sets" class="clip-editor__tab"/>`; after
  `new-rig-pane` add `<ui:VisualElement name="clip-sets-pane" class="clip-editor__cover-pane clip-editor--hidden"/>`.
- Window: fields `private VisualElement clipSetsPane; private ClipSetsPanel clipSetsPanel;` beside
  the New Rig pair (`:243-245`); `tabToggles = new ToolbarToggle[6]` (`:262`); in `CreateGUI`'s
  pane resolution next to `newRigPane = rootVisualElement.Q<VisualElement>("new-rig-pane")`
  (`:995`) add `clipSetsPane = rootVisualElement.Q<VisualElement>("clip-sets-pane")`; delete the
  button binding (`:936-941`) and `CreateClipSet()` (`:1256-1281`); `BindTabs` gains
  `BindTab(ClipEditorTab.ClipSets, "tab-clip-sets", "Browse every clip set in the project, create one — name, folder, starting clips — or add and remove clips on an existing one.")`
  between the New Rig and Clip Editor lines; `ApplyActiveTab` gains
  `ShowClipSetsTab(activeTab == ClipEditorTab.ClipSets);` after `ShowNewRigTab`; a new
  `ShowClipSetsTab(bool isShown)` mirrors `ShowNewRigTab` (lazy `new ClipSetsPanel()`, subscribe
  `Closed += CloseClipSetsTab`, `ClipSetCreated += OnClipSetCreatedByPanel`,
  `OpenInEditorRequested += OnClipSetOpenRequested`, `SetClipsChanged += OnPanelChangedSetClips`,
  `clipSetsPane.Add`) and, **when shown**, calls `clipSetsPanel.SetSource(clipSet)` (the VAT tab's
  shape, `:1733-1745`); `RefreshOpenPaneSource` adds `if (clipSetsPanel != null) clipSetsPanel.SetSource(clipSet);`;
  disposal (`:792-796`) adds the same block for `clipSetsPanel`. Handlers:

```csharp
private void CloseClipSetsTab() { SetActiveTab(ClipEditorTab.ClipEditor); }

private void OnClipSetCreatedByPanel(ClipSetAsset createdSet, bool loadIntoEditor)
{
    if (loadIntoEditor && clipSetField != null) { clipSetField.value = createdSet; }   // the toolbar field, so OnClipSetChanged runs — the comment CreateClipSet carried
}

private void OnClipSetOpenRequested(ClipSetAsset requestedSet)
{
    if (clipSetField != null) { clipSetField.value = requestedSet; }
    SetActiveTab(ClipEditorTab.ClipEditor);
}

private void OnPanelChangedSetClips(ClipSetAsset changedSet)
{
    if (changedSet == null || changedSet != clipSet) { return; }
    RefreshClipList();
    RefreshClipActionButtons();
    MarkPreviewDirty();
    if (validationBadge != null) { validationBadge.Refresh(activeRig, clipSet); }
}
```

## 5. Tasks

Wave 1 (`[parallel-safe]` with each other): **T1, T2, T3**. Wave 2 (`[parallel-safe]`): **T4, T6a,
T6c**. Wave 3: **T5**. Wave 4: **T6b**. Then **T7** (orchestrator) and **T8** (⏸ checkpoint).

Each task's brief pastes: the spec path, the task text below, its "Read" line, the §4 block it
builds, and the three lines of CLAUDE.md's hard rules (no `var`, no single-letter names, explicit
types). Every brief ends: "at turn 30 stop editing and write your report; report ≤ 30 lines; never
call any `mcp__UnityMCP__*` tool."

### T0 — Baseline (orchestrator)
Gate per HANDOFF §3; record EditMode / PlayMode discovered totals in §7. `git status` first: on
2026-09-08 the tree carried ~40 uncommitted files from another session (the 0.21.0 New Rig source
preview across `ClipEditorWindow.cs`, `NewRigPanel.cs`, `ClipEditorWindow.uss`, the VAT baking
files, `PackagingConformanceTests.cs`, `HANDOFF.md`, plus the owner's asset edits). If they are
still uncommitted, they are not yours: leave them, stage only your own paths, and expect the line
numbers in §3/§5 to be the working tree's, not HEAD's. If `ClipEditorWindow.cs` has moved by more
than a few lines, re-grep the member names — they are what the tasks are keyed on.

### T1 — `ClipAssetUtility` add/remove by clip [parallel-safe]
Files: `Editor/ClipUtilities/ClipAssetUtility.cs` (edit `:14-21` constants, `:77-95`
`AppendClipToSet`, add the two public methods after `RemoveClipFromSet` at `:123`), **new**
`Tests/EditMode/ClipAssetUtilityTests.cs`. Read `ClipAssetUtility.cs` in full. Build §4.1.
- Tests use `ScriptableObject.CreateInstance<ClipSetAsset>()` / `<ClipAsset>()` (a
  `SerializedObject` over an unsaved SO works), destroyed in `TearDown`:
  - `AddExistingClipToSet_AppendsOnce_AndRefusesTheDuplicate`: add returns true and
    `clips.Count == 1`; a second add of the same clip returns false and the count stays 1.
    (Revert-to-fail: drop the `Contains` guard.)
  - `RemoveClipFromSet_ByClip_RemovesEveryEntry`: `clips = { a, b, a }`; remove `a` → true,
    `clips` equals `{ b }`. (Revert-to-fail: remove only the first match.)

### T2 — `ClipPickerModel` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/ClipPickerModel.cs`, **new**
`Tests/EditMode/ClipPickerModelTests.cs`. Read nothing beyond §4.3's model block and the two
`ActorEditorPanelTests.cs` lines that show `CreateInstance`/`DestroyImmediate`. Entries in tests
may carry a real `CreateInstance<ClipAsset>()` (identity is all the model needs).
- Tests:
  - `SearchText_FiltersByNameOrFolder_CaseInsensitive`: entries `Walk`/`Assets/A`, `Run`/`Assets/B`,
    `Idle`/`Assets/walkcycle`; `"WALK"` → visible names `{ Walk, Idle }`.
  - `SetEntries_KeepsTicksForClipsStillPresent`: tick `Walk`, `SetEntries` with `Walk` and a new
    `Jump` → `IsChecked(Walk)` true, `CheckedCount == 1`, and `ShowCheckedOnly = true` shows exactly
    `Walk`. (Revert-to-fail: clear the tick set in `SetEntries`.)

### T3 — `ClipSetSaveLocation` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/ClipSetSaveLocation.cs`, **new**
`Tests/EditMode/ClipSetSaveLocationTests.cs`. Read `RigAssetEditor.cs:700-745` (the package's
existing `EditorPrefs.GetString`/`SetString` shape) and §4.4. Tests are static-method only (no
`EditorPrefs` writes in a fixture):
  - `TryMakeProjectRelative_AcceptsAssetsSubfolders_AndRefusesOutsiders`:
    `("C:/Proj/Assets/Anim/Sets", "C:/Proj/Assets")` → true, `"Assets/Anim/Sets"`;
    `("C:\\Proj\\Assets", "C:/Proj/Assets")` → true, `"Assets"`; `("C:/Other/Assets/X", "C:/Proj/Assets")`
    → false; `("C:/Proj/AssetsBackup/X", …)` → false (the `/` boundary — revert-to-fail: drop it).
  - `SanitizeAssetName_StripsInvalidCharacters_AndFallsBackWhenEmpty`: `"Walk: v2?"` → `"Walk v2"`
    (no `:`/`?`, inner space kept, trimmed); `"   "` → `"NewClipSet"`.

### T4 — `ClipPickerListElement` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/ClipPickerListElement.cs`. Read `ClipPickerModel.cs`
(T2's output — it exists by this wave), `ClipEditorWindow.cs:1115-1126, 3502-3520` (`ListView`
setup and `MakeClipRow`/`BindClipRow`), `ToolkitIcons.cs:148-192`. Build §4.3's element block. No
fixture — UI wiring (HANDOFF §2); T7 drives it.

### T6a — Tab enum + UXML [parallel-safe]
Files: `Editor/ClipEditor/ClipEditorTab.cs`, `Editor/ClipEditor/ClipEditorWindow.uxml`. Read both
in full (24 + 145 lines). Apply §4.5's first two bullets exactly. Do not touch the window.

### T6c — Layout test names [parallel-safe]
Files: `Tests/EditMode/ClipEditorLayoutTests.cs` (`:30-85` `RequiredElementNames`, `:160-164`
`tabNames`). Remove `"new-clip-set-button"`; add `"tab-clip-sets"` to both arrays (after
`"tab-new-rig"`); add `"clip-sets-pane"` after `"new-rig-pane"` with a one-line comment in the
existing style. Nothing else.

### T5 — `ClipSetsPanel`
Files: **new** `Editor/ClipEditor/Authoring/ClipSetsPanel.cs`, **new**
`Tests/EditMode/ClipSetsPanelTests.cs`. Read `NewRigPanel.cs:1-120, 320-377`,
`ClipPickerListElement.cs` and `ClipSetSaveLocation.cs` (public surfaces only — the files exist),
`ClipAssetUtility.cs:97-123` (the three methods it calls), `SharedClipBindingUtility.cs:134-148`,
`ToolkitIcons.cs:148-192`, `ToolkitPalette.cs:14-27`. Build §4.2 in full.
- Test `SelectSet_EntersEditMode_WithTheSetsClipsTicked`: `new ClipSetsPanel()`; two in-memory
  clips `walk`, `run` and a set whose `clips = { walk, run, walk }`; `LoadCatalog(new[] { set }, new[] { walk, run, idle })`;
  `SelectSet(set)` → `Mode == Edit`, `SelectedSet == set`, and `Q<ClipPickerListElement>("clip-picker").CheckedClips`
  is exactly `{ walk, run }` (deduplicated). Then `BeginCreate()` → `Mode == Create`, no ticks,
  `Q<TextField>("clip-set-name-field").value == "NewClipSet"`. (Revert-to-fail: make `SelectSet`
  skip `SetCheckedClips`.) Destroy every asset in `TearDown`.

### T6b — Window wiring
Files: `Editor/ClipEditor/ClipEditorWindow.cs` only. Read `:239-262, :775-801, :926-941, :990-1000,
:1161-1171, :1256-1281, :1587-1607, :1657-1698, :1750-1767, :1800-1836` and
`ClipSetsPanel.cs`'s public surface (§4.2's code block — the file exists). Apply §4.5's third
bullet and the handlers verbatim. Afterwards grep `new-clip-set-button|CreateClipSet\(` across
`Editor/` — zero hits. No fixture.

### T7 — Gate, drive, capture, docs (orchestrator)
1. Full suites (HANDOFF §3 steps 3–4); discovered totals must not drop below T0's minus one
   (`ClipEditorLayoutTests` loses no test; the new fixtures add six).
2. Drive over `mcp__UnityMCP__execute_code` (CodeDom C# 6, no `using` lines, fully-qualified
   names, `resolvedStyle` read in a *second* call): open the Clip Editor, `FocusTab`-equivalent
   via reflection on `SetActiveTab(ClipEditorTab.ClipSets)`; in a second call confirm
   `clip-sets-list` has `itemsSource.Count >= 1` (the project has `NewClipSet.asset`,
   `VatSampleTentacleClips.asset`, `Walk.asset`); create `Assets/A75Scratch/` with
   `AssetDatabase.CreateFolder`, then through the panel's public surface: `BeginCreate()`, set the
   name field to `A75Probe`, reflect the private `saveLocation` and `Remember("Assets/A75Scratch")`,
   tick one clip through `clip-picker`'s model (`SetCheckedState`), reflect-call `Create()`. Third
   call: `AssetDatabase.LoadAssetAtPath<ClipSetAsset>("Assets/A75Scratch/A75Probe.asset").clips.Count == 1`
   **after** `AssetDatabase.Refresh()` — the write must persist (HANDOFF §3). Fourth: `SelectSet`
   that set, untick the clip → `clips.Count == 0` on a reloaded reference. Confirm
   `EditorPrefs.GetString("DotsAnimationToolkit.ClipSets.SaveFolder") == "Assets/A75Scratch"`.
3. Capture the tab to `Library/A75ClipSetsCaptures/clip-sets.png` (`GrabPixels`, scale by
   `pixelsPerPoint`) and **look at it**: three columns of text must be legible, the search field
   visible, at least one ticked row. A rail that collapsed to a sliver is the A74 lesson.
4. Delete `Assets/A75Scratch` (and its `.meta`); `EditorPrefs.DeleteKey` the probe folder or set
   it back to `Assets/ScriptableObjects/Animations`; `git status` must show only your files plus
   the owner's pre-existing edits from T0.
5. `CHANGELOG.md` (the version the header rule resolves to, "A75 — clip sets tab"), `package.json` version, HANDOFF §4 paragraph,
   and the vault note `AnimationToolkit.md`: one new short section "Clip Sets tab (A75)" naming the
   panel-reports/window-acts split and the immediate-apply rule — traps only, no inventory.

### T8 — ⏸ owner checkpoint
End the session with this message, verbatim in spirit:

> Open the Clip Editor and press the new **Clip Sets** tab (second from the left — the toolbar's
> "New Set" button is gone). The left column lists every clip set in the project; click **Walk** and
> the right side shows every clip in the project with the set's own clips ticked. Type in the
> search box, flip **Ticked only**, untick a clip and press Ctrl+Z — the Clip Editor tab's clip list
> should follow every change. Press **New**, name it, press **…** to pick a folder, tick a few clips,
> leave **Load this set into the editor** on, press **Create Clip Set** — you should land on the Clip
> Editor tab with the new set open. Close and reopen Unity: the folder should be remembered.
> Judge: is the two-column layout right, should the catalog get its own search, and does
> "ticks apply immediately" feel right or do you want an Apply button? The layout is yours to change.

## 6. Out of scope (recorded, with why)

- An "Open in Clip Sets" button on the `ClipSetAsset` inspector (A75-D12) — a follow-up once the
  tab's layout has passed the owner's eye.
- Search over the catalog column (sets are few; the checkpoint asks).
- Renaming or deleting a set from the tab — the project browser does both, and a delete here would
  need the "which actors reference it" sweep the package does not yet have.
- Drag-and-drop of clips from the project browser onto the picker.
- A `VatTextureSetAsset` field on the panel — the VAT Bake tab owns that pairing.
- Any change to `NewRigPanel`'s pre-A72 inline colours (A75-D10).

## 7. Build log

_(empty — filled by the orchestrating session)_
