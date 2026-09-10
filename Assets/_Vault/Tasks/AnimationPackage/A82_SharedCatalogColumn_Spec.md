# Amendment A82 — One catalog column, one cover-pane split, remembered dividers

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.29.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 0, first.
> **Predecessors:** A76 (`RigCatalogColumn`), A80 (`ActorProfileCatalogColumn`), A81
> (`ImageCatalogColumn`, `RecipeCatalogColumn`), A75/A77 (`ClipSetsPanel`'s inline catalog). Where
> this document and shipped code disagree on how a catalog looks, the shipped code wins.
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents (Sonnet) in
> **two waves** (2 then 6), each at most two files, never touching MCP.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A82** on the DOTS Animation Toolkit package
(`Packages/com.dotsanimationtoolkit`, head `0.28.0`). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A82_SharedCatalogColumn_Spec.md`. Read it in full, then the
roadmap's §3 protocol (binding), then only the files §3 of this spec names. §2 decisions are
settled. You are the orchestrator: you alone compile, test, drive and commit; `worker` subagents
edit files, at most two each, with named line ranges, and never call `mcp__UnityMCP__*`. Run T0
yourself, then wave 1 (T1, T2), gate, then wave 2 (T3–T8), gate, then T9–T10 yourself. Stop at T11.

---

## 1. Goal

Five catalogs exist that are the same control written five times: search field, New, Refresh,
boxed two-line rows, right-click Rename/Delete, `row.userData` recycling, the same five
live-verified layout constants. Every cover-pane split view (eight raw `TwoPaneSplitView`s across
Rigs, Clip Sets, Actor Profiles and Texture Packer) settles at its `minWidth` floor after a tab
hide/show, because nothing re-applies the dragged dimension. A75, A76, A80 and A81 each
re-recorded that defect.

After this amendment: one `ToolkitCatalogColumn<TAsset>` element hosts all five catalogs, one
`CoverPaneSplitView` replaces the raw split views, and a divider dragged on any tab is where it was
when the tab comes back, across domain reloads.

---

## 2. Decisions (recorded — do not re-ask). ⚠ marks an interpretation the owner has not confirmed.

- **A82-D1 — Generic over the asset type, not over the row.** `ToolkitCatalogColumn<TAsset> where
  TAsset : UnityEngine.Object`. The host supplies three delegates: `Func<TAsset, string>
  secondLine` (the grey second row), `Func<IReadOnlyList<TAsset>> scan`, and an optional
  `Func<TAsset, Texture2D> thumbnail` (null for every catalog except A81's Images). Rows stay two
  lines; a thumbnail, when supplied, sits left of them at 48px as A81 draws it today.
- **A82-D2 — New / Rename / Delete are host callbacks, not column behaviour.** The column raises
  `Action NewRequested`, `Action<TAsset, string> RenameRequested`, `Action<TAsset> DeleteRequested`,
  `Action<TAsset> Selected`. The `…AssetUtility` classes keep owning the writes. A host that passes
  no delete callback gets no Delete menu item.
- **A82-D3 — Inline rename reuses `InlineRenameEditing`** exactly as A77 wired it; the column owns
  the wiring once.
- **A82-D4 — The Images catalog keeps its ✓ and eye toggle** as host-supplied row decorators:
  `Func<TAsset, bool> isMarked` and an optional `Toggle` slot. If that generality makes the column
  grow past ~350 lines, keep `ImageCatalogColumn` on its own and re-home only the other four — log
  the call in §7. ⚠
- **A82-D5 — `CoverPaneSplitView` wraps, not subclasses, `TwoPaneSplitView`.** It owns a
  `TwoPaneSplitView`, a `string prefsKey`, and re-applies `fixedPaneInitialDimension` from
  `EditorPrefs` on every `AttachToPanelEvent` and every `GeometryChangedEvent` where the fixed
  pane's dimension has fallen to its `minWidth` while the stored value is larger. It writes the
  stored value when the user releases the dragline (a `PointerUpEvent` on the dragline, not per
  move).
- **A82-D6 — Prefs key convention:** `DotsAnimationToolkit.Split.<tab>.<pane>` — e.g.
  `DotsAnimationToolkit.Split.ActorEditor.Profiles`. Eight keys, listed in §4.2.
- **A82-D7 — Defaults are today's open widths**, not the floors: Profiles 260, Rig catalog and Clip
  Sets catalog whatever their constructors pass today (T0 records them), Texture Packer sidebar 280.
- **A82-D8 — No behaviour change is visible except the divider.** Row heights, paddings, search
  behaviour, context menus and selection colours stay pixel-identical; T0's captures are the
  before, T10's the after.

---

## 3. Read first (only what your task names)

- `Editor/ClipEditor/Authoring/RigCatalogColumn.cs` — the reference implementation (`MakeRigRow`,
  the `userData` comment, the layout constants).
- `Editor/ClipEditor/ActorEditor/ActorProfileCatalogColumn.cs`, `Editor/TexturePacker/
  ImageCatalogColumn.cs`, `Editor/TexturePacker/RecipeCatalogColumn.cs` — the three mirrors.
- `Editor/ClipEditor/Authoring/ClipSetsPanel.cs` — the inline catalog; grep `ListView` and
  `MakeItem` for its range.
- `Editor/ClipEditor/Authoring/InlineRenameEditing.cs` in full (short).
- `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections "Rigs tab (A76, 0.23.0)", "Both
  catalog tabs create, rename and delete in place (A77)", "Shared asset selection (A80)".
- `Tests/EditMode/ClipEditorLayoutTests.cs` — the regression fixture every UI change runs.

---

## 4. Design

### 4.1 `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs` (T1)

```csharp
public sealed class ToolkitCatalogColumn<TAsset> : VisualElement where TAsset : UnityEngine.Object
{
    public ToolkitCatalogColumn(CatalogColumnOptions<TAsset> options);
    public TAsset Selected { get; }
    public void Rescan();                       // calls options.scan, keeps selection if still present
    public void Select(TAsset asset, bool notify);
    public event Action<TAsset> Selected;
    public event Action NewRequested;
    public event Action<TAsset, string> RenameRequested;
    public event Action<TAsset> DeleteRequested;
}

public sealed class CatalogColumnOptions<TAsset>
{
    public string newButtonTooltip;
    public Func<IReadOnlyList<TAsset>> scan;
    public Func<TAsset, string> secondLine;
    public Func<TAsset, Texture2D> thumbnail;   // null = no thumbnail column
    public Func<TAsset, bool> isMarked;         // null = no ✓
    public bool allowDelete;
    public bool allowRename;
}
```

Rules carried across verbatim from `RigCatalogColumn`: the row stores its asset in `userData` and
reads it live in `BindItem`; search filters on name case-insensitively; the New and Refresh
buttons are `ToolkitIcons.MakeIconButton`; selected row uses `ToolkitPalette.SelectedRow`; box
border/fill/header from `ToolkitPalette`. The class is an element, so `Conformance_G` does not
apply; the file lives in `Shared/` beside `ActiveAssetSelection`.

### 4.2 `Editor/ClipEditor/Shared/CoverPaneSplitView.cs` (T2)

```csharp
public sealed class CoverPaneSplitView : VisualElement
{
    public CoverPaneSplitView(string prefsKey, int fixedPaneIndex, float defaultDimension,
        TwoPaneSplitViewOrientation orientation);
    public VisualElement FixedPane { get; }
    public VisualElement FlexPane { get; }
    public float StoredDimension { get; }       // what EditorPrefs holds, for the fixture
    public void ReapplyStoredDimension();       // public so a host can call it from Show…Tab
}
```

Keys (D6): `Rigs.Catalog`, `Rigs.Targets`, `ClipSets.Catalog`, `ClipSets.Picker`,
`ActorEditor.Profiles`, `ActorEditor.Layers`, `ActorEditor.Preview`, `TexturePacker.Sidebar`,
all prefixed `DotsAnimationToolkit.Split.`. T0 confirms the count is eight by grepping
`new TwoPaneSplitView` under `Editor/`.

The re-apply trap: `TwoPaneSplitView` sets its fixed pane's dimension only in its own
`OnPostDisplaySetup` after the first layout; a re-apply issued before geometry is known is
overwritten. Register for `GeometryChangedEvent` on the split view and re-apply there when
`resolvedStyle.width` of the fixed pane is within 1px of `minWidth` and the stored value is larger.
Unregister after a successful re-apply so the user's live drag is not fought.

---

## 5. Tasks

Worker brief boilerplate: the spec path, the task text, its Files and Read lines, the §4 block,
the hard rules, and "at turn 30 stop editing and write your report; report ≤ 30 lines; never call
any `mcp__UnityMCP__*` tool." Spawn from the repo root.

- [ ] **T0 — Baseline (orchestrator).** Gate; record EditMode/PlayMode totals in §7. Grep
  `new TwoPaneSplitView` under `Editor/` and list every site with its `fixedPaneInitialDimension`
  literal — that list is D7's defaults. Capture Rigs, Clip Sets, Actor Profiles and Texture Packer
  tabs to `Library/A82Captures/before_*.png` (the vault's "A session CAN see the editor UI" section
  says how). Commit nothing yet.
- [ ] **T1 — `ToolkitCatalogColumn<TAsset>` [parallel-safe]** — Files: new
  `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs`. Read `RigCatalogColumn.cs` in full,
  `InlineRenameEditing.cs` in full. No fixture (UI wiring).
- [ ] **T2 — `CoverPaneSplitView` + fixture [parallel-safe]** — Files: new
  `Editor/ClipEditor/Shared/CoverPaneSplitView.cs`, new `Tests/EditMode/CoverPaneSplitViewTests.cs`.
  Read §4.2 only. Fixture: `StoredDimension_SurvivesHideShow` — construct with a scratch prefs key,
  set the stored value to 300 through the same path the dragline uses, remove and re-add the
  element to a test panel host, call `ReapplyStoredDimension`, assert `fixedPane.style.width` is
  300. Revert-to-fail: comment out the re-apply body. Delete the scratch prefs key in `TearDown`.
- **Gate wave 1.** Compile; run `CoverPaneSplitViewTests`. Commit `A82-T1,T2`.
- [ ] **T3 — Re-home `RigCatalogColumn` [parallel-safe]** — Files:
  `Editor/ClipEditor/Authoring/RigCatalogColumn.cs`, `Editor/ClipEditor/Authoring/RigsPanel.cs`
  (only the lines constructing the column and the split view; grep `RigCatalogColumn(` and
  `TwoPaneSplitView`). `RigCatalogColumn` becomes a thin host: builds `CatalogColumnOptions<RigAsset>`,
  forwards the four events to `RigAssetUtility`. Keep its public surface so `RigsPanel` compiles.
- [ ] **T4 — Re-home `ActorProfileCatalogColumn` [parallel-safe]** — Files:
  `Editor/ClipEditor/ActorEditor/ActorProfileCatalogColumn.cs`, `ActorEditorPanel.cs` (split-view
  construction lines only). Same shape as T3; the Profiles split gets key `ActorEditor.Profiles`,
  default 260.
- [ ] **T5 — Re-home `RecipeCatalogColumn` [parallel-safe]** — Files:
  `Editor/TexturePacker/RecipeCatalogColumn.cs`, `Editor/TexturePacker/TexturePackerSidebar.cs`
  (split-view lines only, key `TexturePacker.Sidebar`, default 280).
- [ ] **T6 — Re-home `ImageCatalogColumn` [parallel-safe]** — Files:
  `Editor/TexturePacker/ImageCatalogColumn.cs` only. Uses `thumbnail` and `isMarked`; the eye
  toggle stays host-side. If D4's size escape applies, leave the file alone and say so in the report.
- [ ] **T7 — Re-home the Clip Sets inline catalog [parallel-safe]** — Files:
  `Editor/ClipEditor/Authoring/ClipSetsPanel.cs` (the `ListView`/`MakeItem`/context-menu range
  T0 grepped, plus its split view: keys `ClipSets.Catalog`, `ClipSets.Picker`).
- [ ] **T8 — Layout fixture update [parallel-safe]** — Files:
  `Tests/EditMode/ClipEditorLayoutTests.cs`. Any assertion that names a raw `TwoPaneSplitView` in
  a cover pane now names `CoverPaneSplitView`. No new tests.
- **Gate wave 2.** Compile; run `ClipEditorLayoutTests`, `ActiveAssetSelectionTests`,
  `ActorEditorPanelTests`. Commit `A82-T3..T8`.
- [ ] **T9 — Docs, changelog (orchestrator or one worker).** `CHANGELOG.md` `## [0.29.0]`;
  `Documentation~/clip-editor.md` gains one sentence: dividers are remembered per tab.
  `package.json` version. Add "Catalog columns and cover-pane splits are shared (A82)" to the vault
  note with the prefs-key convention and the `GeometryChangedEvent` re-apply trap.
- [ ] **T10 — Drive (orchestrator).** Full suites. Open each of the four tabs, drag a divider,
  switch tab and back, confirm the width held; close and reopen the window, confirm again. Capture
  `Library/A82Captures/after_*.png`, compare with `before_*` by eye, and record what differs (D8
  says: nothing but the divider).
- [ ] **T11 — ⏸ owner checkpoint.** Message: "Open the DOTS Animator. On Rigs, Clip Sets, Actor
  Profiles and Texture Packer, drag the left divider, switch tabs and come back. It should stay.
  The rows, search and right-click menus should look exactly as before — say if anything moved.
  One ⚠: the Images catalog was / was not re-homed (D4) — see §7."

---

## 6. Deliberately out of scope

- Any new catalog (Events, Health, Materials get theirs in their own specs, on this column).
- Multi-select in catalogs; drag-reorder; catalog grouping by folder.
- Persisting the Clip Editor dock's own splits (they already persist through the window's session
  state — confirmed by `Dock_SplitsMatchTheOnesTheWindowPersists`).

## 7. Build log

_(empty — the session appends here: T0 totals and split-view inventory, D4 call, drift notes)_

### T0 — baseline (2026-09-10, head `5f0c91ab`)

- **Suites:** EditMode 823 discovered, 822 pass; the one failure is pre-existing and unrelated —
  `Conformance_A_AsmdefReferenceLists_MatchSection13Exactly` (the Editor asmdef carries an extra
  `Unity.RenderPipelines.Universal.Runtime` reference; A81 shipped with it). PlayMode 283 discovered, 283 pass.
- **Split-view inventory (`new TwoPaneSplitView` under `Editor/`):** ten sites, seven in cover
  panes (the spec said eight — `ClipSetsPanel` has one split, not two; there is no `ClipSets.Picker`
  key). Defaults for D7 are the shipped literals:
  `RigsPanel` outer 640 (fixed = inner split, → `Rigs.Targets`) and inner 280 (→ `Rigs.Catalog`);
  `ClipSetsPanel` 280 (→ `ClipSets.Catalog`); `ActorEditorPanel` body 260 (→
  `ActorEditor.Profiles`), middle 340 (`SideColumnWidth`, → `ActorEditor.Layers`), right 340 with
  fixed index 1 (→ `ActorEditor.Preview`); `TexturePackerPanel` 280 (→ `TexturePacker.Sidebar` —
  the split lives in the panel, not `TexturePackerSidebar.cs` as T5 assumed). The three in
  `CutsceneEditorPanel` are not cover-pane catalogs and stay raw.
- **Live probe on the Rigs tab (Unity 6000.5):** hide/show reproduces the collapse — fixed pane
  280 → 200 (its `minWidth`), the split's own width 640 → 560. `m_FixedPaneDimension` reads `-1`
  until something writes it; the vault's "re-assigning `fixedPaneInitialDimension` does not repair
  a collapsed split" is **stale for this version**: the public setter re-runs `Init`, moved the
  drag-line anchor and the pane to the new value both before and after the collapse, and with a
  non-`-1` dimension injected. Writing `fixedPane.style.width` alone moves the pane but leaves the
  drag-line anchor where it was. So `CoverPaneSplitView` re-applies through the public setter and
  writes the pane's style dimension as well. The drag-line anchor is named `unity-dragline-anchor`,
  class `unity-two-pane-split-view__dragline-anchor`.
- **Captures:** `EditorApplication.isFocused == false` for the whole session so far — no
  `before_*` capture is possible (vault rule: a stale frame is worse than none). Re-check at T10.
- **D4 call:** `ImageCatalogColumn` is **not** re-homed. Its item is `ImageCatalogEntry` (a GUID
  record with a lazily loaded texture), not a `UnityEngine.Object`; it has no New/Rename/Delete,
  multi-selects, drags to the canvas and activates on double-click. Re-homing it would load every
  project texture eagerly and bend the column around one consumer. With Images out, the
  `thumbnail`/`isMarked` decorators have no consumer and are not built. T6 is a no-op.
- **T8 pre-check:** `ClipEditorLayoutTests` names only the dock's four splits (`Dock_SplitsMatch…`),
  which stay raw `TwoPaneSplitView`s; no cover-pane split is asserted. T8 is a no-op.
- **Shape call:** the three thin hosts inherit `ToolkitCatalogColumn<TAsset>` (so it is not
  `sealed` as §4.1 sketched) rather than wrapping it — wrapping adds an element to the tree for
  nothing. Property/event names avoid the `Selected` clash in the sketch: `SelectedAsset` and
  `AssetSelected`.
