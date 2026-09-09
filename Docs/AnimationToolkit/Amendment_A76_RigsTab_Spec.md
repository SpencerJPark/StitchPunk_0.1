# Amendment A76 — Rigs tab: a rig catalog, an editable target list, and the preview beside them

> **Status:** ✅ built 2026-09-08, shipped as 0.23.0. One ⏸ owner checkpoint open (§5 T8).
> **Prompt:** [`Amendment_A76_RigsTab_Prompt.md`](Amendment_A76_RigsTab_Prompt.md).
> **Predecessor:** [`Amendment_A75_ClipSets_Spec.md`](Amendment_A75_ClipSets_Spec.md) — this amendment
> gives the Rigs tab the shape A75 gave the Clip Sets tab. Where the two disagree, A75's shipped code
> is the reference, not this document's prose.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> five waves and never touch MCP. Every task is at most two files, with named line ranges to read.

---

## 1. What the owner asked for (2026-09-08, verbatim where it matters)

> "I want to add similar style of clip sets to the rigs tab, here is the idea, first collumn shows
> rigs, it it pretty much the same as the clip sets picker including the add new and refresh button,
> then the next row is the heiarchy like it has right now for its left collumn, so that is now the
> middle collum, together those two start off taking up half the screen, and then the rest of the
> screen is the preview window like what already there."

Asked as a follow-up ("what should the middle column let you do to a selected rig?"), the owner chose
**full edit, like Clip Sets**: the middle column lists every renderer-bearing node in the selected
rig's prefab with that rig's current targets ticked; tick adds a target, untick removes one, the Tag
button retags — each an immediate, undoable write to the `.asset`. The option text he accepted also
carried the orphan guard ("unticking a target that a clip has keyed tracks on … a warning that names
the clips") and this layout sketch, which is the acceptance target:

```
┌ Rigs ──────┬ Targets ─────────────┬ Preview ────────┐
│ 🔍 search   │ Source Prefab [Male…]│                 │
│ ┌────────┐ │ ☑ Root/Torso  [Tag:…]│      ▄▄▄        │
│ │NewRig  │ │ ☑ Root/Head   [Tag:…]│     ▐███▌       │
│ │ 12 tgt │ │ ☐ Root/CapeAlt[Tag:…]│      ███        │
│ └────────┘ │ ⚠ Root/OldArm (missing)                │
│ ┌────────┐ │                      │                 │
│ │PlayerR │ │ [Use in Clip Editor] │                 │
│ └────────┘ │                      │                 │
│ [+New][⟳]  │                      │                 │
└────────────┴──────────────────────┴─────────────────┘
```

---

## 2. Decisions (recorded — do not re-ask)

- **A76-D1 — Three columns, two nested `TwoPaneSplitView`s.** Outer horizontal split, fixed pane
  index 0, initial dimension **640px**, holding [inner split | preview]. Inner horizontal split,
  fixed pane index 0, initial dimension **280px** (the same number A75's catalog uses), holding
  [catalog | targets]. That makes the two left columns start at half of a ~1280px window and hands
  the remainder to a preview that grows, which is what the owner described. Both dividers are
  draggable; neither position is persisted (§6).
- **A76-D2 — The catalog column is a mirror of A75's, not an extraction.** `ClipSetsPanel`'s catalog
  shipped three commits ago carrying live-verified layout constants (`fixedItemHeight = 64`, the
  zeroed `ToolbarSearchField` margins, the `Color.clear` item slot). Re-homing those into a shared
  generic element would put a tab the owner has already visually approved back at risk for no
  visible gain. A76 writes `RigCatalogColumn.cs` as a close mirror; extracting the pair into one
  control is a follow-up once both have passed the owner's eye (§6).
- **A76-D3 — Selecting a rig in the catalog does *not* change the window's Rig field.** An explicit
  **Use in Clip Editor** button in the targets column header does, raising an event the window acts
  on — the same panel-reports / window-acts split A75 used for `OpenInEditorRequested`. Browsing
  rigs must not silently repoint the Clip Editor's binding.
- **A76-D4 — Edit-mode ticks and tags apply immediately, one undo step each.** No Apply button, no
  dirty state to lose. Create mode is unchanged: nothing is written until **Create Rig**.
- **A76-D5 — Edit-mode writes go through `Undo.RecordObject` + direct list mutation, not the
  `SerializedProperty` route `ClipAssetUtility` uses.** `RigTargetDefinition.stableId` is `internal`
  to the Authoring assembly, so only `RigAsset.EnsureStableIds()` can mint an id for a target added
  from the Editor assembly — and a `SerializedObject` built before that call would write the
  freshly-minted id back to 0 on its next `ApplyModifiedProperties`. Record, mutate, `EnsureStableIds`,
  `SetDirty`, `SaveAssetIfDirty`, in that order. See §4.1.
- **A76-D6 — Unticking a target that clips are bound to asks first.** A pure sweep finds every
  `ClipAsset` with a `TransformTrack` or `SpriteTrack` whose `tagId` matches the target's tag (when
  non-zero) or whose `targetId` matches the target's stable id. If any hit, a confirm dialog names
  the count and up to three clip names; Cancel restores the tick and writes nothing. Proceeding is
  legal and not an error — a tag-bound track missing from a rig is already the *lenient* T2 case
  (HANDOFF §5), so this is a "you are about to make these clips skip on this rig" warning, not a
  block.
- **A76-D7 — The row list in edit mode is the union of the prefab's renderer-bearing nodes and the
  rig's existing targets**, in that order, with a target whose `sourceNodePath` is empty or absent
  from the prefab shown last as a ⚠ *missing* row: ticked, unticka­ble, never pre-focused. Dropping
  such a target from the list would make an unticked box the only evidence that a target still
  exists, which is how a rig loses a part silently.
- **A76-D8 — No delete in the rig catalog.** A76's context menu offers nothing. A rig is referenced
  by actor profiles and by every clip track's `targetId`/`tagId`, and the package has no reference
  sweep broad enough to tell an owner what a delete would cost. Recorded in §6; the owner can ask
  for it once he has seen the tab.
- **A76-D9 — Changing a selected rig's Source Prefab is allowed, and only repoints
  `RigAsset.sourcePrefab`.** Targets are left exactly as they are; nodes that no longer exist become
  ⚠ missing rows by D7. No migration, no re-derivation — HANDOFF §5's "new rigs are created fresh"
  applies here too.
- **A76-D10 — `NewRigPanel` becomes `RigsPanel` and `ClipEditorTab.NewRig` becomes
  `ClipEditorTab.Rigs`, in T7, by the orchestrator.** A panel that browses every rig in the project
  is not "the New Rig panel", and CLAUDE.md's "names are the documentation" rule outranks the churn.
  The **UXML element names stay** (`tab-new-rig`, `new-rig-pane`): they are asserted by
  `ClipEditorLayoutTests`' two arrays, nothing user-visible reads them, and the tab's display text
  has said "Rigs" since A72.
- **A76-D11 — Catalog rows carry two lines, like A75's.** Title = rig name; info =
  `"<n> targets · <folder>"`; tooltip = the source prefab's name, or `"(no source prefab)"`. Same
  `fixedItemHeight = 64f`. Do not add a third line — the height constant is tuned to two.
- **A76-D12 — No new UXML.** The panel builds all three columns in C#, inside the existing
  `new-rig-pane`. `ClipEditorWindow.uxml` and `ClipEditorLayoutTests` are untouched by every wave
  except T7's rename, which touches neither.

---

## 3. Read first (the executor and every subagent — only what your task names)

1. Repo root `CLAUDE.md`; `Docs/AnimationToolkit/HANDOFF.md` §2, §3, §5 (skip §4 history);
   `Assets/_Vault/Memories/Code/RULES.md`.
2. `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections **"Shared editor chrome"** (inline
   styles are layout only; icon + word goes through `ToolkitIcons.SetButtonIconAndText`) and
   **"Never rebuild a pane from a value-changed callback"**.
3. `Packages/com.dotsanimationtoolkit/Editor/ClipEditor/Authoring/ClipSetsPanel.cs` — the shape being
   mirrored: `:59-74` (the split view), `:76-153` (`BuildCatalogColumn` — header, New/Refresh,
   search field, `ListView`), `:155-206` (`MakeClipSetRow` and its three live-verified layout
   comments), `:255-290` (`BindClipSetRow`, selection), `:475-579` (`RescanProject`, `LoadCatalog`,
   `ApplyCatalogFilter`, `RefreshCatalogEmptyState`), `:581-625` (`SelectSet`, `BeginCreate`).
4. `Editor/ClipEditor/Authoring/NewRigPanel.cs` in full (386 lines) — the file A76 restructures.
   `:50-131` is the current two-column layout, `:141-182` the tag picker, `:196-291`
   `RescanHierarchy` (the row builder), `:293-310` `FocusRow`, `:312-377` `Create`.
5. `Editor/ClipUtilities/RigAssetUtility.cs` in full (62 lines) and
   `Editor/ClipUtilities/ClipAssetUtility.cs:120-165` (the undo-group idiom A76 copies the *shape*
   of, not the `SerializedProperty` mechanics — see A76-D5).
6. `Authoring/Assets/RigAsset.cs:19-52` (fields), `:60-100` (`EnsureStableIds`), `:201-241`
   (`RigTargetDefinition`); `Runtime/Identity/TargetId.cs:12-22`.
7. `Authoring/Assets/ClipAsset.cs:205-215` (`TransformTrack.targetId`/`tagId`) and `:269-275`
   (`SpriteTrack`) — the two track kinds A76's sweep reads.
8. `Editor/ClipEditor/Authoring/RigSourcePreviewElement.cs:124-180` — `ShowPrefab`,
   `SetNodeIncluded`, `FocusNode`, `ClearFocus`. The whole preview column is reused unchanged.
9. `Editor/ClipEditor/ClipEditorWindow.cs:239-253` (pane fields), `:796-805` (disposal),
   `:1730-1773` (`ShowNewRigTab` and `ShowClipSetsTab` — the second is the template for `SetSource`),
   `:1805-1846` (`RefreshOpenPaneSource`, `CloseNewRigTab`, `OnNewRigCreated`).
10. `Editor/ClipEditor/Shared/ToolkitIcons.cs:148-192` (`MakeIconButton`, `MakeIconTextButton`,
    `SetButtonIconAndText`); `ClipEditorWindow.uss:239-400` (`toolkit-pane-*`, `toolkit-box*`).
11. Tests to mirror: `Tests/EditMode/ClipSetsPanelTests.cs` (a panel constructed with no window;
    assets via `ScriptableObject.CreateInstance`, destroyed in `TearDown`) and
    `Tests/EditMode/ClipPickerModelTests.cs` (a pure model fixture).

---

## 4. Design

### 4.1 Asset surgery — `Editor/ClipUtilities/RigAssetUtility.cs`

Four public additions beside `CreateRig`. Every one: null-guard, `Undo.RecordObject(rig, name)`,
mutate `rig.targets` / `rig.sourcePrefab` directly, then `EditorUtility.SetDirty(rig)` and
`AssetDatabase.SaveAssetIfDirty(rig)`. `Undo.RecordObject` on a `ScriptableObject` snapshots the
whole serialized state, so a list mutation after it is one undoable step.

```csharp
/// Appends a target for sourceNodePath as one undo step, minting its stable id. Returns the new target, or null when the rig is null or already has one for that path.
public static RigTargetDefinition AddTargetToRig(RigAsset rig, string sourceNodePath, string displayName)

/// Removes the target carrying targetStableId as one undo step. False when the rig is null or no target matches.
public static bool RemoveTargetFromRig(RigAsset rig, uint targetStableId)

/// Writes a target's tag as one undo step. False when the rig is null or no target matches. tagId 0 means untagged, which is legal.
public static bool SetTargetTag(RigAsset rig, uint targetStableId, uint tagId)

/// Repoints the rig at a different source prefab as one undo step, leaving every target as it is. False when the rig is null or the prefab is already assigned.
public static bool SetRigSourcePrefab(RigAsset rig, GameObject sourcePrefab)
```

`AddTargetToRig` order matters and is the whole reason for A76-D5:

1. `Undo.RecordObject(rig, "Add Rig Target")`
2. `rig.targets.Add(new RigTargetDefinition { displayName = …, sourceNodePath = … })` — leave
   `kind`, `boundsExtents`, `framesPerVariant`, `facesDirection` at their field defaults; that is
   exactly what `NewRigPanel.Create` already hands `CreateRig`.
3. `rig.EnsureStableIds()` — mints the id. Nothing else can.
4. `EditorUtility.SetDirty(rig)`; `AssetDatabase.SaveAssetIfDirty(rig)`.
5. Return the added definition; its `Id.Value` is now non-zero and is what the panel keys rows on.

Do **not** call `rig.MarkStableIdPersisted()` here — that method discharges the *asset's* own
unpersisted-id report, and `CreateRig` already owns that call for a rig it just wrote. A target id
minted into an already-saved rig is persisted by the `SaveAssetIfDirty` above.

### 4.2 The reference sweep — new `Editor/ClipEditor/Authoring/RigTargetReferenceResolver.cs`

`public static class RigTargetReferenceResolver`. Pure, no `AssetDatabase` — the caller passes the
clips it already scanned, so the fixture needs no assets on disk. The `Resolver` suffix is required
by `Conformance_G` (see §4.3); a `…Sweep` or `…Helper` name fails the gate.

```csharp
/// Every clip carrying a transform or sprite track bound to this target, by tag when tagId is non-zero or by target id otherwise. Never null; ordered as clips was, each clip at most once.
public static List<ClipAsset> FindClipsBoundToTarget(IReadOnlyList<ClipAsset> clips, uint targetStableId, uint tagId)

/// "Walk, Run and 2 more" — the clip names for the confirm dialog, at most three named. Empty string for an empty list.
public static string DescribeClips(IReadOnlyList<ClipAsset> clips)
```

Matching rule, per `ClipAsset`'s own sentinel convention (`:210-215`): a track binds **by tag** when
its `tagId` is non-zero and **by target id** otherwise. So a track matches when
`track.tagId != 0 && track.tagId == tagId` (only when the target's own `tagId` is non-zero), or
`track.tagId == 0 && track.targetId == targetStableId`. A target with `tagId == 0` therefore matches
only by target id — do not let a `tagId == 0` target sweep up every untagged track in the project.
Guard nulls at every level (`clips` entries, `clip.transformTracks`, `clip.spriteTracks`, and the
entries inside them); a fixture builds these lists by hand and half-populated shapes are ordinary.

### 4.3 The row builder — new `Editor/ClipEditor/Authoring/RigTargetRowBuilder.cs`

Pure. Merges "what the prefab has" with "what the rig claims" into the ordered row list A76-D7
describes, so the panel builds rows from data instead of two interleaved loops.
`public static class RigTargetRowBuilder` — the `Builder` suffix is required by `Conformance_G`,
which allows only Api/Builder/Sampler/Resolver/Math/Validation/Utility/Editing on a static class;
`…Model` would fail the gate. The row type beside it is an ordinary instance class.

```csharp
public sealed class RigTargetRow
{
    public string SourceNodePath;      // empty only on a missing-node row for an unbound target
    public string DisplayName;
    public uint TargetStableId;        // 0 when this node is not (yet) a target
    public uint TagId;
    public bool IsTarget;              // the tick state
    public bool IsMissingNode;         // a target whose path is absent from the prefab
    public bool PreTicked;             // create mode only: the renderer looked "wanted"
}

/// Rows for create mode: one per renderer-bearing node, in hierarchy order, pre-ticked per the existing rule.
public static List<RigTargetRow> BuildForNewRig(GameObject sourcePrefab)

/// Rows for edit mode: every renderer-bearing node of the rig's prefab (ticked when the rig has a target for it), then one missing row per target the prefab cannot account for.
public static List<RigTargetRow> BuildForRig(RigAsset rig)
```

`BuildForNewRig` is `NewRigPanel.RescanHierarchy`'s existing scan lifted out verbatim, including its
two rules: a renderer whose `PrefabAuthoringBridge.GetHierarchyPath` comes back empty is **skipped**
(an empty path is `RigTargetDefinition`'s own "unbound" sentinel), and `PreTicked` is
`renderer.enabled && transform.gameObject.activeSelf`. Both comments at `NewRigPanel.cs:220-233`
move with the code — they are the reasoning, not decoration.

`BuildForRig` walks the same renderers, looks each path up in `rig.targets`, and carries the found
target's `Id.Value`, `tagId` and `displayName` onto the row; unmatched nodes get
`TargetStableId == 0`, `IsTarget == false`. Then every target whose `sourceNodePath` is empty or
matched nothing becomes a trailing row with `IsMissingNode = true`, `IsTarget = true`. A null
`rig`, a null `rig.sourcePrefab`, or a null entry inside `rig.targets` returns/skips rather than
throwing.

### 4.4 The catalog column — new `Editor/ClipEditor/Authoring/RigCatalogColumn.cs`

`public sealed class RigCatalogColumn : VisualElement`. A close mirror of
`ClipSetsPanel.BuildCatalogColumn` + `MakeClipSetRow` + `BindClipSetRow` + `ApplyCatalogFilter` +
`RefreshCatalogEmptyState`, self-contained so the panel holds one child rather than five fields.

```csharp
public event Action NewRequested;        // the header's + button
public event Action RefreshRequested;    // the header's ⟳ button
public event Action<RigAsset> RigSelected;

public RigAsset SelectedRig { get; }
public void SetRigs(IReadOnlyList<RigAsset> rigs);   // replaces the catalog, re-applies the search filter
public void SetSelectedRig(RigAsset rig);            // selection without raising RigSelected
public void ClearSelection();
```

Element names: `rig-catalog-column`, `rigs-search`, `rigs-list`, `rig-row-box`, `rig-row-title`,
`rig-row-info`. Copy A75's numbers and its three layout comments exactly — `fixedItemHeight = 64f`,
`SelectionType.Single`, the search field's `width = 100%` / `minWidth = 0` / zeroed left+right
margins, the item slot's `backgroundColor = Color.clear`, the row's `marginTop/Bottom = 4f` and
`marginLeft/Right = 0f`. Header title "Rigs"; buttons via
`ToolkitIcons.MakeIconTextButton(…, "Toolbar Plus", null, "New")` and
`(…, "Refresh", "Rescan the project for rigs", "Refresh")`. Empty label text: `"No rigs in this
project yet. Press New."` / `"No rigs match your search."`. No context menu (A76-D8).

### 4.5 The panel — `Editor/ClipEditor/Authoring/NewRigPanel.cs` (→ `RigsPanel.cs` in T7)

Public surface after A76:

```csharp
public enum EditorMode { Create, Edit }

public event Action Closed;                        // after a create with Load ticked
public event Action<RigAsset, bool> RigCreated;    // (rig, loadIntoEditor) — unchanged
public event Action<RigAsset> UseInEditorRequested; // the targets header button (A76-D3)
public event Action<RigAsset> RigTargetsChanged;    // an add/remove/retag landed on this rig

public EditorMode Mode { get; }
public RigAsset SelectedRig { get; }

public void SetSource(RigAsset activeRig);   // called by the window every time the tab is shown
public void SelectRig(RigAsset rig);
public void BeginCreate();
```

**Layout.** The constructor builds the two nested split views of A76-D1 and adds
`RigCatalogColumn`, the targets column, and the existing preview pane. Column minimums:
catalog `minWidth = 200f`, targets `minWidth = 360f`, preview `minWidth = 320f` — the same floors
A75 used, so a drag cannot squeeze a column to an unusable sliver.

> **Trap.** A `TwoPaneSplitView` that is laid out while hidden comes back collapsed with no handle
> (`ClipEditorWindow.cs:1698-1702`). The panel is already built lazily on first show and the pane's
> `clip-editor--hidden` class is applied *after* the `Add` — keep that order in T6, and never
> construct the panel from `OnEnable`.

**Targets column** (`rig-targets-column`), top to bottom:

- Header (`toolkit-pane-header`): title label `rig-targets-title` — `"New Rig"` in create mode,
  the rig's name in edit mode — and, in edit mode only, `ToolkitIcons.MakeIconTextButton(…,
  "editicon.sml", null, "Use in Clip Editor")` named `rig-use-in-editor-button`, which raises
  `UseInEditorRequested`. Hidden (`display = None`) in create mode.
- `ObjectField` `rig-source-prefab-field` ("Source Prefab", `GameObject`, no scene objects) — the
  existing one, with its existing tooltip. In create mode a change rescans; in edit mode a change
  goes through `RigAssetUtility.SetRigSourcePrefab` first, then rescans (A76-D9).
- The summary label and the `ScrollView` of boxed rows, both unchanged in structure. Rows are built
  from `RigTargetRowBuilder`, not from a raw renderer walk.
- Create-mode-only footer: the "Load this rig into the editor" toggle and the **Create Rig** button,
  both unchanged. `display = None` in edit mode.
- `resultLabel`, unchanged, in both modes.

**Rows.** Keep every ellipsis/shrink style from `NewRigPanel.cs:235-263` verbatim — a deep node path
is longer than the column is wide, and those five `labelElement` lines are what stop the tag button
being pushed out of the row. Per row: the `Toggle`, the tag `Button` (disabled when unticked), the
`toolkit-box` / `toolkit-box__header` wrapper, the `PointerDownEvent` focus handler with
`TrickleDown.TrickleDown`, and `preview.SetNodeIncluded(path, ticked)`.

A ⚠ missing row (A76-D7) differs: title text is `"⚠ " + path + " (missing from prefab)"` or
`"⚠ " + displayName + " (no node)"` when the path is empty, the toggle is ticked, the row is not
click-to-focus (guard `FocusNode` — never call it with a path the preview has no copy of), and the
tag button still works. Unticking it removes the target through the ordinary edit path.

**Mode behaviour.**

| | Create | Edit |
|---|---|---|
| Entered by | `BeginCreate()`, the catalog's **New** | `SelectRig(rig)`, a catalog click |
| Rows from | `BuildForNewRig(prefabField.value)` | `BuildForRig(rig)` |
| Tick | in-memory only | `AddTargetToRig` / `RemoveTargetFromRig`, immediately |
| Tag | in-memory only | `SetTargetTag`, immediately |
| Prefab field | rescans | `SetRigSourcePrefab`, then rescans |
| Writes on | **Create Rig** only | every interaction |

**The untick guard (A76-D6).** Before `RemoveTargetFromRig`, call
`RigTargetReferenceResolver.FindClipsBoundToTarget(catalogClips, row.TargetStableId, row.TagId)`. On a
non-empty result, `EditorUtility.DisplayDialog("Remove Rig Target", "\"" + row.DisplayName + "\" is
animated by " + DescribeClips(hits) + ". Those tracks will be skipped when a clip plays on this rig.
Remove it anyway?", "Remove", "Cancel")`. On Cancel: `toggle.SetValueWithoutNotify(true)` and return
— **`SetValueWithoutNotify`, or restoring the tick re-enters this handler and asks again.**

**After a successful edit write:** update the row's `TargetStableId`/`TagId` in place, raise
`RigTargetsChanged(SelectedRig)`, and call the catalog's `SetRigs` refresh only for the row's info
line — never rebuild the row list from inside a `ChangeEvent` callback (vault: "Never rebuild a pane
from a value-changed callback"); the row that raised it is mid-dispatch.

**Scanning.** `RescanProject()` mirrors `ClipSetsPanel.cs:475-505`: `AssetDatabase.FindAssets("t:" +
nameof(RigAsset))` and the same for `ClipAsset` (the sweep needs them), each loaded, both sorted
`StringComparer.OrdinalIgnoreCase` by name, then `catalog.SetRigs(rigs)`. Called from `SetSource`,
from the catalog's **Refresh**, and after a successful `Create`. No `AssetPostprocessor` — A75-D
precedent.

`SetSource(RigAsset activeRig)` rescans, and if `Mode` has never been set by the user this session
and `activeRig` is in the catalog, selects it — so opening the tab with a rig in the toolbar lands
on that rig.

### 4.6 Window integration — `Editor/ClipEditor/ClipEditorWindow.cs`

Three edits, all mirroring what `ShowClipSetsTab` already does for clip sets:

- `ShowNewRigTab` (`:1730-1747`): subscribe `UseInEditorRequested += OnRigUseInEditorRequested` and
  `RigTargetsChanged += OnPanelChangedRigTargets` alongside the existing two, and add
  `if (isShown) { newRigPanel.SetSource(activeRig); }` before the `EnableInClassList` line.
- `RefreshOpenPaneSource` (`:1805-1820`): add `if (newRigPanel != null) { newRigPanel.SetSource(activeRig); }`.
- Two handlers, beside `OnNewRigCreated` (`:1840`):
  - `OnRigUseInEditorRequested(RigAsset rig)` — `if (rig != null && skinnedSourceField != null) { skinnedSourceField.value = rig; }` then `SetActiveTab(ClipEditorTab.ClipEditor)`, matching
    `OnClipSetOpenRequested`'s shape.
  - `OnPanelChangedRigTargets(RigAsset rig)` — if `rig == activeRig`, refresh the hierarchy/rig
    binding the same way the window already answers an external rig change. Find the existing path
    by grepping for what `skinnedSourceField`'s own value-changed callback calls; reuse it, do not
    write a second refresh.

Disposal at `:796-805` already nulls `newRigPanel` through `Dispose()`; the catalog column holds no
unmanaged resource, so nothing is added there.

---

## 5. Tasks

Wave 1 (`[parallel-safe]` with each other): **T1, T2, T3, T4**. Wave 2: **T5a**. Wave 3: **T5b**.
Wave 4: **T6**. Then **T7** (orchestrator) and **T8** (⏸ checkpoint).

Each task's brief pastes: the spec path, the task text below, its "Read" line, the §4 block it
builds, and the three lines of CLAUDE.md's hard rules (no `var`, no single-letter names, explicit
types; `.Schedule()` never `.Run()` — not that this amendment has a job in it). Every brief ends:
"at turn 30 stop editing and write your report; report ≤ 30 lines; never call any `mcp__UnityMCP__*`
tool."

### T0 — Baseline (orchestrator)
Gate per HANDOFF §3; record EditMode / PlayMode discovered totals in §7. `git status` first — on
2026-09-08 the tree carried four modified `ClipEditor/` files from the A75 follow-up plus a
screenshot in `Assets/_Vault/Tasks/Claude/`. If they are still uncommitted they are the owner's:
leave them, stage only your own paths, and expect §3/§5's line numbers to be the working tree's.
If `ClipEditorWindow.cs` or `NewRigPanel.cs` has moved by more than a few lines, re-grep the member
names — the tasks are keyed on those, not on the numbers.

### T1 — `RigAssetUtility` write methods [parallel-safe]
Files: `Editor/ClipUtilities/RigAssetUtility.cs` (append after `CreateRig`, `:49`), **new**
`Tests/EditMode/RigAssetUtilityTests.cs`. Read `RigAssetUtility.cs` in full, `RigAsset.cs:60-100,
201-241`, `TargetId.cs:12-22`, and §4.1. Build §4.1.
- Tests use `ScriptableObject.CreateInstance<RigAsset>()`, destroyed in `TearDown` (an unsaved SO is
  enough — `SaveAssetIfDirty` on an asset with no path is a no-op, not a throw; if it does log,
  assert with `LogAssert.ignoreFailingMessages` rather than skipping the assertion):
  - `AddTargetToRig_MintsAStableId_AndRefusesADuplicatePath`: add `"Root/Torso"` → non-null, its
    `Id.Value != 0`, `rig.targets.Count == 1`; adding `"Root/Torso"` again → null, count still 1.
    (Revert-to-fail: drop the `EnsureStableIds()` call — `Id.Value` comes back 0.)
  - `RemoveTargetFromRig_RemovesOnlyTheMatchingId`: two targets; remove the second's id → true,
    the survivor is the first; removing an id no target carries → false.
  - `SetTargetTag_WritesTheTag_AndZeroIsLegal`: set `0xABCDu` → true and readable; set `0u` → true
    and reads 0. (Revert-to-fail: early-return on `tagId == 0`.)

### T2 — `RigTargetReferenceResolver` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/RigTargetReferenceResolver.cs`, **new**
`Tests/EditMode/RigTargetReferenceResolverTests.cs`. Read `ClipAsset.cs:205-215, 269-275` and §4.2 —
nothing else. Fixtures build `ClipAsset`s with `CreateInstance` and populate their track lists by
hand; destroy in `TearDown`.
- Tests:
  - `FindClipsBoundToTarget_MatchesByTag_ThenByTargetId`: clip `walk` has a transform track with
    `tagId = 7`; clip `run` has one with `tagId = 0, targetId = 42`; clip `idle` has neither.
    Sweeping `(targetStableId: 42, tagId: 7)` returns exactly `{ walk, run }`, `walk` first.
  - `FindClipsBoundToTarget_UntaggedTarget_DoesNotSweepUpEveryUntaggedTrack`: sweeping
    `(targetStableId: 42, tagId: 0)` over a clip whose only track is `tagId = 0, targetId = 99`
    returns empty. (Revert-to-fail: match any `tagId == 0` track.)
  - `DescribeClips_NamesAtMostThree`: five clips → `"A, B, C and 2 more"`; one → `"A"`; none →
    `string.Empty`.

### T3 — `RigTargetRowBuilder` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/RigTargetRowBuilder.cs`, **new**
`Tests/EditMode/RigTargetRowBuilderTests.cs`. Read `NewRigPanel.cs:196-291` (the scan being lifted),
`Authoring/.../PrefabAuthoringBridge.cs`'s `GetHierarchyPath` signature only, `RigAsset.cs:201-241`,
and §4.3. Build §4.3.
- Fixtures build a real `GameObject` hierarchy in memory (`new GameObject`, `AddComponent<MeshRenderer>`,
  parented) and `DestroyImmediate` the root in `TearDown` — no prefab asset on disk.
  - `BuildForNewRig_SkipsTheRootRenderer_AndPreTicksOnlyEnabledActiveNodes`: root with a renderer +
    two children, one with its renderer disabled → two rows, the disabled one `PreTicked == false`.
    (Revert-to-fail: drop the empty-path skip — a third row appears.)
  - `BuildForRig_TicksExistingTargets_AndTrailsMissingOnesLast`: a rig over that hierarchy with
    targets for `"Child"` and for `"Ghost"` (absent) → three rows; `"Child"` `IsTarget == true` with
    the target's tag carried; the other child `IsTarget == false`; the last row `IsMissingNode ==
    true`, `IsTarget == true`. (Revert-to-fail: drop the missing-target pass — a target vanishes
    from the list.)

### T4 — `RigCatalogColumn` [parallel-safe]
Files: **new** `Editor/ClipEditor/Authoring/RigCatalogColumn.cs`. Read
`ClipSetsPanel.cs:76-153, 155-206, 255-290, 533-579` (the five methods being mirrored, comments and
all), `ToolkitIcons.cs:148-192`, `ClipEditorWindow.uss:239-400`, and §4.4. No fixture — UI wiring
(HANDOFF §2); T5b's fixture and T7's drive cover it. Do not touch `ClipSetsPanel.cs`.

### T5a — Three-column layout, create mode preserved
Files: `Editor/ClipEditor/Authoring/NewRigPanel.cs` only. Read the file in full, `RigCatalogColumn.cs`
and `RigTargetRowBuilder.cs` public surfaces (both exist by now), `ClipSetsPanel.cs:59-74, 475-531`,
and §4.5 — everything except the "Edit" column of the mode table and the untick guard. Deliver: the
nested split views, the hosted catalog, `RescanProject`, `SetSource`, `BeginCreate`, and rows built
through `RigTargetRowBuilder.BuildForNewRig`. **Create mode must behave exactly as it does today** —
same pre-ticks, same tag picker, same `Create()`, same `RigCreated`/`Closed` events. Edit mode may
be a stub that only sets `Mode`/`SelectedRig` and shows the header. No fixture in this task.

### T5b — Edit mode
Files: `Editor/ClipEditor/Authoring/NewRigPanel.cs`, **new** `Tests/EditMode/RigsPanelTests.cs`. Read
the file as T5a left it, the public surfaces of `RigAssetUtility` (§4.1's code block),
`RigTargetReferenceResolver` (§4.2's) and `RigTargetRowBuilder` (§4.3's), plus
`ClipSetsPanelTests.cs` in full as the fixture shape. Build §4.5's Edit column, the untick guard,
and the `UseInEditorRequested` / `RigTargetsChanged` events.
- Test `SelectRig_EntersEditMode_WithTheRigsTargetsTicked`: a real in-memory prefab hierarchy (root
  + `Torso` + `Head`), a `CreateInstance<RigAsset>()` pointing at it with one target for `"Torso"`;
  `new NewRigPanel()`; `LoadCatalog`-equivalent via `SetSource(rig)` is not usable in a fixture (it
  hits `AssetDatabase`) — call `SelectRig(rig)` directly. Assert `Mode == Edit`, `SelectedRig == rig`,
  and that the targets scroll holds two toggles with exactly the `"Torso"` one ticked.
  (Revert-to-fail: build edit rows with `BuildForNewRig`.) Then `BeginCreate()` → `Mode == Create`,
  `SelectedRig == null`. Destroy every asset and GameObject in `TearDown`.

### T6 — Window wiring
Files: `Editor/ClipEditor/ClipEditorWindow.cs` only. Read `:239-253, :796-805, :1730-1773,
:1805-1846`, the panel's public surface (§4.5's code block — the file exists), and §4.6. Apply
§4.6's three edits. Grep for `skinnedSourceField.RegisterValueChangedCallback` first and reuse
whatever it calls for `OnPanelChangedRigTargets` — do not write a second refresh path. No fixture.

### T7 — Rename, gate, drive, docs (orchestrator)
1. **Rename (A76-D10), by grep, not by a worker:** `NewRigPanel` → `RigsPanel` (rename the file and
   its `.meta` with `git mv`), `ClipEditorTab.NewRig` → `ClipEditorTab.Rigs`, `newRigPanel` →
   `rigsPanel`, `ShowNewRigTab` → `ShowRigsTab`, `CloseNewRigTab` → `CloseRigsTab`. Leave
   `newRigPane`, `"tab-new-rig"` and `"new-rig-pane"` alone (A76-D10). `grep -rn "NewRigPanel\|ClipEditorTab\.NewRig"`
   across `Packages/` and `Assets/` must come back empty except in this spec and the changelog.
2. Full suites (HANDOFF §3 steps 3–4); discovered totals must not drop below T0's — the new fixtures
   add nine tests and nothing is deleted.
3. Drive over `mcp__UnityMCP__execute_code` (CodeDom C# 6, no `using` lines, fully-qualified names,
   `resolvedStyle` read in a **second** call — vault: "resolvedStyle is stale in the call that
   rebuilt it"). Open the Clip Editor, `SetActiveTab(ClipEditorTab.Rigs)`. Second call: assert
   `rigs-list`'s `itemsSource.Count >= 1`, and that `rig-catalog-column`, `rig-targets-column` and
   the preview pane all have non-zero `layout.width` in roughly `280 : 360 : rest` proportion.
   Third: `SelectRig` the project's `NewRig` asset, reflect the targets scroll's toggle count, tick
   a previously-unticked node, `AssetDatabase.Refresh()`, then **reload the rig from disk** and
   assert its `targets.Count` grew by one and the new target's `Id.Value != 0` — HANDOFF §3's
   "prove the write persists". Fourth: untick it again and assert the count returns, then
   `Undo.PerformUndo()` twice and assert the rig is back where it started. Do this against a scratch
   copy (`AssetDatabase.CopyAsset` into `Assets/A76Scratch/`), never the owner's real rig.
4. Capture the tab to `Library/A76RigsCaptures/rigs-tab.png` (`GrabPixels`, scale by
   `pixelsPerPoint`, check `EditorApplication.isFocused` **first**) and **look at it**: three columns
   legible, the search field visible, at least one ticked row, the preview showing geometry. A75's
   capture step failed on a stale frame with the Editor unfocused — if that repeats, say so and
   record the `resolvedStyle` numbers instead of claiming a capture happened.
5. Delete `Assets/A76Scratch` and its `.meta`; `git status` must show only your files plus whatever
   T0 recorded as the owner's.
6. `CHANGELOG.md` (the version the header rule resolves to, "A76 — rigs tab"), `package.json`
   `0.22.0` → `0.23.0`, HANDOFF §4 paragraph, and the vault note `AnimationToolkit.md`: one short
   section "Rigs tab (A76)" naming the immediate-apply rule, A76-D5's mint-order trap, and the
   `SetValueWithoutNotify` restore in the untick guard. Traps only, no inventory.

### T8 — ⏸ owner checkpoint
End the session with this message, verbatim in spirit:

> Open the Clip Editor and press **Rigs**. The first column lists every rig in the project with New
> and Refresh above it, exactly like Clip Sets; the middle column is the hierarchy that used to be
> on the left; the preview has the right half. Click a rig — the middle column fills with every
> renderer-bearing node in its prefab, its own targets ticked. Tick a node to add it as a target,
> untick one to remove it, press a Tag button to retag; each writes to the `.asset` immediately and
> Ctrl+Z undoes it. Untick something a clip animates and it should ask first, naming the clips. Press
> **Use in Clip Editor** to put the rig in the toolbar. Press **New** and the middle column goes back
> to the create flow you already know. Drag both dividers.
> Judge: are the two starting widths right, should the divider positions be remembered between
> sessions, and does the ⚠ missing-node row read clearly enough? Deleting a rig from this tab is
> deliberately absent — say if you want it.

---

## 6. Out of scope (recorded, with why)

- **Deleting a rig from the catalog** (A76-D8) — needs a reference sweep across actor profiles and
  clip tracks that the package does not have. T8 asks.
- **Extracting `RigCatalogColumn` and `ClipSetsPanel`'s catalog into one shared control** (A76-D2) —
  a follow-up once both have passed the owner's eye.
- **Persisting the two divider positions** in `EditorPrefs` — T8 asks; `TwoPaneSplitView` does not
  do it on its own and every other dock in this window is equally forgetful.
- **Editing a target's `displayName`, `kind`, `boundsExtents`, `framesPerVariant` or
  `facesDirection` from this tab** — the `RigAsset` inspector owns those, and the owner asked for
  the Clip Sets *shape*, which is membership plus one attribute.
- **Sockets, billboard roots, mirror pairs and ragdoll bodies** — same reason; the tab is about
  targets.
- **A "reveal in Rigs tab" button on the `RigAsset` inspector** — the A75 twin of this is still
  unbuilt; both land together or not at all.
- **Any change to `RigSourcePreviewElement`** — A74 (`Amendment_A74_PreviewViewports_Spec.md`) owns
  the preview-viewport work and does not touch this file; A76 must not either, or the two amendments
  collide.

## 7. Build log

- **T0 baseline:** EditMode 784/784 (one standing `Conformance_A` asmdef drift, pre-existing),
  PlayMode 283/283 — exactly HANDOFF's numbers. The four uncommitted `Editor/ClipEditor/` files
  predicted by T0 were present and were left alone; `NewRigPanel.cs`'s pending change was a
  one-line heading rename that T5a subsumed.
- **Waves 1–4:** T1–T4 landed in parallel with no rework. **T5b hit its 40-turn cap** after
  finishing the panel and the fixture but before self-reviewing; per CLAUDE.md it was not resumed —
  its diff was read, found complete, and checked by a `verifier` against a 13-point list (all
  passed, including every create-mode preservation check). T6 landed clean, and made one judgement
  call worth recording: for `OnPanelChangedRigTargets` it reuses `RebuildHierarchy`/`RebuildTimeline`/
  `RebuildInspector` rather than the whole rig-changed handler, because the rig object has not
  changed — only its targets — so reassigning `activeRig` would be wrong and clearing the hierarchy
  selection would be collateral.
- **Subagent `Read` was broken for the whole of wave 1.** The read-guard hook resolves
  `.claude/hooks/` relative to the shell's cwd, which had drifted into the package folder when the
  wave was spawned, so `Read` (and `Bash`) errored for all four workers. Each fell back to
  `PowerShell`/`Grep` over the same named line ranges and none lost fidelity, but the orchestrator
  must keep cwd at the repo root when spawning.
- **Test job filtering:** `run_tests` with `group_names` failed to initialize, and a subsequent
  unfiltered run silently inherited the filter (9 tests, no result payload, status "failed").
  Re-running clean fixed it. Do not trust a run whose completed count looks too small.
- **T7 rename:** done by grep, not by a worker. `git mv` of the `.cs`/`.meta` pair produced one
  transient `Internal error - unexpected guid mismatch`; a forced refresh cleared it. Prose the
  rename made false was corrected with it, including two user-facing hints that told the reader to
  "use New Rig".
- **T7 drive — full functional pass, all through the real UI toggles.** Tab opens with
  `rig-catalog-column` 280 × 562, `rig-targets-column` 360 × 562, preview 328 × 562 (the 640 fixed
  pair, preview taking the remainder); `rigs-list` itemsSource 2, matching the project's two rigs.
  Against a scratch copy of `NewRig` (20 targets, 34 renderer-bearing nodes): `SelectRig` gave
  34 rows with exactly 20 ticked and revealed the Use-in-Clip-Editor button; ticking a row grew a
  **reloaded-from-disk** reference to 21 with a minted non-zero stable id and zero zero-ids
  anywhere; unticking returned it to 20. Undo verified on the in-memory instance: tick → 21 →
  Ctrl+Z → 20. The untick guard is live — 19 of the real rig's 20 targets have clips bound, and
  `DescribeClips` renders `Idle, Idle_EastFacing, Walk and 1 more`.
- **One defect found and fixed during the drive.** The capture showed a row still ticked after its
  add had been undone: nothing listened for `Undo.undoRedoPerformed`, so Ctrl+Z changed the asset
  while the panel's ticks stayed put — precisely what T8 asks the owner to try. `RigsPanel` now
  subscribes in its constructor and unsubscribes in `Dispose`; re-verified live (tick → True,
  Ctrl+Z → asset 20 and the row reads False). Recorded in the vault note.
- **A false alarm worth recording:** an earlier undo check appeared to fail because it ran
  `SaveAssets`/`Refresh` between the edit and the undo, which reloads the object out from under the
  undo stack. The code was correct; the check was not.
- **T7 capture — succeeded**, unlike A75's. `EditorApplication.isFocused` was true. The first
  attempt came back solid black because the window sits at x = −701 on a monitor left of the
  primary and the scaled coordinate is off-screen for `ReadScreenPixel`; moving the window to a
  positive position, capturing, and restoring it produced a legible frame at
  `Library/A76RigsCaptures/rigs-tab.png`, which was looked at. `EditorWindow.RepaintImmediately`
  is non-public and needs reflection.
- **Cleanup:** `Assets/A76Scratch` and its probe deleted (project back to 2 rigs), the Clip Editor
  window restored to its original position, `git status` clean apart from the pre-existing files
  T0 recorded.
- **Final gate:** EditMode 794/794 (784 baseline + 10 new: T1×3, T2×4, T3×2, T5b×1), PlayMode
  283/283. The only failure throughout is the standing `Conformance_A` drift.
