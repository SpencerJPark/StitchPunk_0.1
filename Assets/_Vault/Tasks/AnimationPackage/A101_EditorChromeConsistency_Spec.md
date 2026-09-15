# Amendment A101 — Editor chrome consistency: one look, one code path, across every tab

> **Status:** 📝 specced 2026-09-15, not built. Takes the next free minor after A100 (expected `0.53.0`).
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 3.
> **Predecessors:** A72 (the design system: `ToolkitPalette`, `ToolkitIcons`, `TransportCoreElement`, the
> `toolkit-*` classes), A82 (`ToolkitCatalogColumn`, `CoverPaneSplitView`), A83 (window decomposition),
> A93F/A94F/A95F/A96/A97/A98 (the tabs this sweeps). **Runs alone, after the A96F/A97F/A99 batch has
> merged** — it edits files those specs edit.
> **Executor:** one orchestrator; `worker` subagents (Sonnet) in **two waves**, every worker ≤ 2 files and
> ≤ 30 turns. Wave 1 is seven foundation workers whose public surfaces §4 pins, so wave 2's twenty-one
> per-tab workers code against §4 and never against each other. Two orchestrator-only steps (the rename
> sweep and the drive) are sed and MCP work, not worker work.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A101 — Editor chrome consistency** on the DOTS Animation Toolkit package
(`Packages/com.dotsanimationtoolkit`, head `0.52.0` or later; **A96F, A97F and A99 merged**). Spec:
`Assets/_Vault/Tasks/AnimationPackage/A101_EditorChromeConsistency_Spec.md`. Read it in full, then the
roadmap §3 protocol, `Docs/AnimationToolkit/HANDOFF.md` §2, and only what §3 here names at the line
ranges it names. §2 is settled. T0 is yours. Wave 1 = T1–T7 in parallel, one compile gate. Wave 2 =
T8–T28 in parallel (T25 and T26 are a sequential pair), one compile gate. T29 (rename sweep) and T30
(docs) are yours; then fixtures, full suites, the drive (T31), close (T32), and stop at T33.

Every worker brief carries: the spec path; the task's text; §4.10 (the adoption recipe) verbatim for a
wave-2 task; the §4 block the task builds against; the hard rules (no `var`, no single-letter names,
explicit types; one `<summary>` per file ≤ 3 lines; no `§`, amendment numbers or spec citations in
shipped code — `Conformance_F` scans for them); and the closing lines "at turn 30 stop editing and write
your report; report ≤ 30 lines; never call any `mcp__UnityMCP__*` tool."

---

## 1. Goal, and what the audit found

The owner (2026-09-15): "the tabs for the dots animator have inconsistent ui between them, my favorite
right now is the clip editor and the texture packer. I would like to see more cohesive styles when it
comes to spacing around containers, colors, etc. I also would like to make sure that the ui reused
designs as much as possible to keep its code maintainable. also I would like the cutscene editor to
also have the key rows visible like the clip editor even when nothing is selected. … I want this to
look very professional."

A72 built the design system and applied it to three tabs. Ten tabs have shipped since, each one
re-deriving the chrome by hand. The audit at `4d05a70e` (every claim below is a file and line):

| Surface | What is inconsistent today |
|---|---|
| **Column padding** | 8/10/10 (top/left/right) inline in `TexturePackerSidebar.cs:36-39`, `ImageCatalogColumn.cs:51-54`, `EventKeyCatalogColumn.cs:35-38`, `ToolkitCatalogColumn.cs:60-63`, `ClipSetsPanel.cs:148-151`, `RigsPanel.cs:240-243`, `VatBakePanel.cs:63-66`, `CapturePanel.cs:237-240`, `SpriteSheetFramesColumn.cs:33-36`; 6/6/6/6 in `RetargetPanel.cs:72-75`; 6/6/4 in `CutsceneCastPanel.cs:57-59`; 6/8/8 in `CapturePanel.cs:90-92`; **none** in `HealthPanel`, `EventsPanel`, `MaterialsPanel`, `EventUsageColumn`, `MaterialInspectorColumn`. |
| **Section headings** | Four private `BuildHeading` copies with inline bold + 10/2 margins (`CapturePanel.cs:343`, `RigsPanel.cs:481`, `VatBakePanel.cs:299`) versus the cutscene's `clip-editor__heading` class with 6/4 margins (`CutsceneEditorPanel.cs:5438`); Health has a fifth, `MakeSectionHeader` (`HealthFindingDetailElement.cs:257`). |
| **Pane titles** | Most columns use `toolkit-pane-header` + `toolkit-pane-title`. Missing the title class: Texture Packer's recipe label (`TexturePackerPanel.cs:83`), Sprite Sheets' sheet label (`SpriteSheetsPanel.cs:128`), the frames title (`SpriteSheetFramesColumn.cs:39`), Materials' "Rig" caption (`MaterialsPanel.cs:44`). No header at all: Capture's two columns, Events' usage column, Health's list and detail, Retarget's header row (three object fields, `RetargetPanel.cs:70-131`). Health is the only tab with a Unity `Toolbar` under the tab strip (`HealthPanel.cs:88`). |
| **Primary action** | Scan: 32px, minWidth 150, inline accent fill (`HealthPanel.cs:97-100`). Capture: 36px bold inline (`CapturePanel.cs:322-325`). VAT: 28px plain "Bake VAT Textures" (`VatBakePanel.cs:190-192`). Texture Packer and Sprite Sheets: Bake is an ordinary header icon button. Clip Sets "Open in Clip Editor" and Rigs "Use in Clip Editor": ordinary header buttons. |
| **Hints and results** | `clip-editor__hint` in nine files; hand-rolled hints with inline margin/colour in `RigsPanel.cs:260-266`, `ClipSetsPanel.cs:167-173`, `HealthFindingDetailElement.cs:29-34`, `HealthFindingListElement.cs:41-43`, `CutsceneEditorPanel.cs:1971-1980` (inline grey, centred in a void). Result labels: bold inline (`RigsPanel.cs:372-376`, `VatBakePanel.cs:194-198`), plain (`CapturePanel.cs:335-338`), hint class (`MaterialsPanel.cs:61`), a red literal on error (`RigsPanel.cs:812`). |
| **Boxes and rows** | Health hand-builds a `toolkit-box` from nineteen inline lines (`HealthFindingDetailElement.cs:233-255`). The ListView slot trap (a clear-background slot around a boxed row) is copied three times (`ToolkitCatalogColumn.cs:193-220`, `EventKeyCatalogColumn.cs:210-224`, `HealthFindingListElement.cs` `MakeFindingRow`). Retarget's roster chips are 28 inline visual writes (`RosterCoverageStripElement.cs`). |
| **Viewports** | Frame + overlay rail + reset-camera button copied in `ActorEditorPanel.cs:376-420`, `CaptureViewportElement.cs:31-59`, `VatPreviewElement.cs:70-110`, `RigSourcePreviewElement.cs:58-100`; Retarget's preview is a bare `Image` with plain "Pause" / "Reset View" buttons (`RetargetPreviewElement.cs:42-50`). The vault records one blank-button shipping bug from exactly this copy (AnimationToolkit.md, "The 4-arg SetButtonIcon does not parent the icon"). |
| **Splits** | Every tab uses `CoverPaneSplitView` (remembered divider) except the cutscene's three raw `TwoPaneSplitView`s (`CutsceneEditorPanel.cs:177,208,214`). |
| **Buttons with words where the rule is icons** | Cutscene zoom "All"/"Playhead" plain (`:517-527`) versus the Clip Editor's `toolkit-icon-button--text` pair; "Continue" (`:533`), cast "Sync" (`CutsceneCastPanel.cs:93`), "+ Part Track" with an inline 10px font (`CutsceneEditorPanel.cs:2338-2347`), Materials "Select in Inspector" (`MaterialInspectorColumn.cs:34`), three "…" folder buttons (`ClipSetsPanel.cs:206`, `RigsPanel.cs:297`, `SpriteSheetsPanel.cs:142`). |
| **Colour literals in chrome** | `EventKeyCatalogColumn.cs:16`, `ValidationBadgeElement.cs:18-20`, `CutsceneEditorPanel.cs:1978,4526`, `DirectionSetClipQueueView.cs:124,133`, `RigsPanel.cs:812`, `CaptureViewportElement.cs:35`; the playhead colour declared twice (`PlayheadElement.cs:14`, `CutsceneTimelinePlayheadElement.cs:11`). |
| **Cutscene timeline, nothing loaded** | A centred label (`CutsceneEditorPanel.cs:1971-1980`). The Clip Editor draws the ruler and striped ghost lanes and puts the message in its status row (`TimelinePane.cs:431-437`, `GhostLaneStripElement`). The lane heights already agree (both 22px); the lane shades already agree (`cutscene-editor__lane-row` mirrors `TrackLaneElement`). |

Inline *visual* style writes (colour, opacity, font, border, radius, text-align) per Editor file at
`4d05a70e`, top of the list: Health detail 43, cutscene panel 37, roster strip 28, Rigs 20, Capture
19, VAT 17, Health list 16, Clip Sets 15. A72-D10 said these were banned. Nothing enforced it.

After this amendment: every tab is built from the same dozen `toolkit-*` classes and four shared
elements; a conformance test keeps inline visual styles out; the cutscene timeline looks like the
Clip Editor's when it is empty; and the Clip Editor and Texture Packer tabs look the same as today.

---

## 2. Decisions (recorded — do not re-ask). ⚠ = interpretation for the checkpoint.

- **A101-D1 — The Clip Editor and the Texture Packer are the reference.** The owner named them. Their
  measurements become the tokens (8/10/10 column padding is the Texture Packer sidebar's; headings,
  hints, status rows and pane headers are the Clip Editor's). Neither tab changes visibly except:
  the Texture Packer's recipe label gains the pane-title weight and Bake gains the primary-action
  fill. The tab strip is untouched (owner exemption, A72-R3).
- **A101-D2 — One column class, one asset bar, one primary action, one hint, one heading, one status
  row.** §4.1 lists the classes. Every cover-pane column is a `toolkit-column`; every tab whose subject
  is picked (clip set, rig, clip, cutscene, capture source) shows those pickers in a `toolkit-asset-bar`
  across the top of the tab, the cutscene's bar being the model; every tab has at most one
  `toolkit-primary-action`; every empty/no-selection/explanatory line is a `toolkit-hint`; every form
  section title is a `toolkit-heading`; every result line is a `toolkit-status` in a
  `toolkit-status-row--footer`. The Clip Editor keeps its pane fields (A80's call) — it leads.
- **A101-D3 — Shared elements, not shared snippets.** Four new files in `Editor/ClipEditor/Shared/`:
  `ToolkitChrome.cs` (factories for the classes above and the one ListView-slot trap),
  `ViewportFrameElement.cs` (frame + overlay rail + reset camera), `CatalogSidebarElement.cs` (the
  Texture Packer's mode-toggle sidebar, generalised), `PathPickerRowElement.cs` (caption + path + folder
  icon button). Existing copies are replaced, not left beside them.
- **A101-D4 — Inline styles are layout only, and a test says so.** `Conformance_I` (§4.7) scans
  `Editor/**/*.cs` (not `Editor/Inspectors/`, §6) for inline visual writes. A line whose colour comes
  from data (an event lane's colour, a severity dot, a chip's fill) carries the trailing comment
  `// colour from data` and is exempt. Files this amendment does not convert sit in a ratchet
  allowlist inside the test that may only shrink; a stale entry (a listed file with no violation
  left) fails the test too.
- **A101-D5 — Renames go through, old names die.** `clip-editor__hint` → `toolkit-hint`,
  `clip-editor__heading` → `toolkit-heading`, `clip-editor__toolbar` → `toolkit-asset-bar`,
  `clip-editor__toolbar-label` → `toolkit-asset-bar__label`, `clip-editor__object-field` →
  `toolkit-asset-bar__field`. Orchestrator sed sweep (T29) over `Editor/`, the UXML and `Tests/`,
  after wave 2; workers write the **new** names from the start. `clip-editor__pane-field`,
  `clip-editor__bar-action`, `clip-editor__viewport-*` and `clip-editor__overlay-*` keep their names
  (they are Clip-Editor-owned rules other tabs borrow; renaming them buys nothing).
- **A101-D6 — Cutscene empty state = Clip Editor empty state.** With no cutscene loaded the timeline
  still draws its ruler and its header column, and `GhostLaneStripElement` fills the lane column with
  striped rows; the "No cutscene loaded…" sentence moves to the status row. With a cutscene loaded,
  ghost rows also fill whatever is left under the last row (the Clip Editor does this; the cutscene
  does not). `GhostLaneStripElement` gains one flag, `paintRangeShading` (the Clip Editor's normalized
  0–1 shading is wrong for a seconds-based timeline). The cutscene ruler stays 24px (the Clip
  Editor's is 20px; the seconds labels need the height) ⚠.
- **A101-D7 — "Key rows visible even when nothing is selected" is read as D6.** ⚠ The other reading —
  one override row per rig part, always, the way the Clip Editor lists every part — is the A73
  part-track model (rows are added on demand with "+ Part Track") and is a different amendment; the
  checkpoint asks which the owner meant.
- **A101-D8 — Every split is a `CoverPaneSplitView`.** The cutscene's three become
  `Cutscene.Cast` (index 0, 220), `Cutscene.Inspector` (index 1, 300), `Cutscene.Timeline` (index 1,
  240). Their `minWidth` floors stay as today's comments describe.
- **A101-D9 — Icons over words, everywhere a momentary action has one.** The sweep list is in §4.9.
  Where the word is the affordance (Continue, Key, Bake, Scan, Capture) it is icon **plus** word
  through `ToolkitIcons.MakeIconTextButton`, never a bare `new Button { text = … }`.
- **A101-D10 — Primary-action placement.** In the working column's pane header actions when the
  column has a header (Texture Packer Bake, Sprite Sheets Bake, Clip Sets Open, Rigs Use, Materials
  Create); at the head of the asset bar when the tab's job is the action itself (Health Scan); at the
  foot of the form when the form feeds it (VAT Bake, Capture). One per tab; Events, Retarget, Actor
  Profiles, Cutscene Director and the Clip Editor have none (their actions are per-item).
- **A101-D11 — Sprite Sheets adopts the Texture Packer sidebar.** ⚠ Sheets | Images as two modes of
  one `CatalogSidebarElement` instead of two catalogs stacked in a vertical split. Dragging an image
  onto the frames column still works (the frames column is not in the sidebar). The
  `SpriteSheets.Catalogs` prefs key is retired.
- **A101-D12 — Health's Toolbar becomes an asset bar.** Scan (primary) · scan status · the three
  severity toggles as a `clip-editor__bar-action` run · spacer · search. The A94F "big Scan" survives as
  the primary-action class; nothing about findings, filters or the detail panel's behaviour changes.
- **A101-D13 — Materials gets pickers.** A Rig field and a Clip Set field in its asset bar, writing the
  shared selection like Retarget's do, so "No rig selected. Pick one in the Rigs tab." stops sending the
  author to another tab. Target dropdown + Create (primary) sit at the bar's right edge.
- **A101-D14 — Retarget's preview is a viewport.** `ViewportFrameElement` + a `TransportCoreElement`
  (capabilities `None` — play/pause only) in a `toolkit-transport` row under it, replacing the
  "Pause" / "Reset View" buttons; reset camera is the rail button. `RetargetPreviewElement` implements
  `ITransportTarget` so Space works there too (window routing per A72-D2, one new case in
  `ResolveActiveTransportTarget`).
- **A101-D15 — Colour tokens for the drawn chrome.** `playhead` rgb(242, 92, 77), `box-select-fill`
  rgba(77, 158, 242, 0.18), `box-select-outline` rgba(115, 184, 255, 0.9) join `ToolkitPalette` and the
  USS mirror block (the mirror test enforces both). `ValidationBadgeElement`'s three statics,
  `EventKeyCatalogColumn.BudgetWarningColor`, the cutscene's clip-status literal and
  `DirectionSetClipQueueView`'s two become `ToolkitPalette` reads or `toolkit-text--*` classes. Canvas
  elements that paint with `Painter2D` (lanes, rulers, easing curve, mark overlay) keep their private
  statics — they are drawing, not chrome.
- **A101-D16 — Cutscene transport order follows the Clip Editor.** `[Length (derived)] [core] [Time ·
  Speed] [Zoom · All · Playhead] [hold status]`. Today the core is first and the length readout trails
  the Time field; the readout becomes a leading "Length" caption + derived label. Nothing else moves.
- **A101-D17 — Health's detail title and severity are chrome, not data.** The finding's code and
  severity word become a pane header (title = code, actions = severity as a `toolkit-text--error` /
  `--warning` / `--dim` label); the finding title is a `toolkit-detail-title`. The severity **dot** in the
  list row keeps its data-driven fill with the D4 marker.

---

## 3. Read first

Line ranges verified at `4d05a70e`; if a range has drifted, follow the code and log it in §7.

- `Docs/AnimationToolkit/Amendment_A72_EditorVisualUnification_Spec.md` §2 D8–D12 and §3.4 (the
  box/pane/status contract this extends).
- `Editor/ClipEditor/ClipEditorWindow.uss` lines 20–92 (root tokens), 93–421 (the Shared section),
  426–437 (toolbar rules being renamed), 538–556 (`bar-action`, `object-field`), 600–628 (cover pane,
  pane, hint), 1049–1053 (heading), 1306–1315 (ghost lanes), 1533–1562 (cutscene rows).
- `Editor/ClipEditor/Shared/ToolkitIcons.cs` 148–235 (the factories; the 4-arg `SetButtonIcon` does
  not parent the icon — `ViewportFrameElement` is where that trap gets its one home).
- `Editor/ClipEditor/Shared/ToolkitPalette.cs` — grep `Tokens` and one `public static readonly Color`
  to see the field + dictionary pattern.
- `Editor/ClipEditor/Shared/ToolkitCatalogColumn.cs` 55–125 (column construction), 193–245 (the
  ListView slot trap, verbatim comments worth keeping).
- `Editor/ClipEditor/GhostLaneStripElement.cs` in full (240 lines).
- `Editor/ClipEditor/Panes/TimelinePane.cs` 424–437 (the Clip Editor's empty state);
  `TimelinePane.View.cs` 300–335 (`SyncGhostLanes`).
- `Editor/TexturePacker/TexturePackerSidebar.cs` in full (the sidebar being generalised).
- `Tests/EditMode/PackagingConformanceTests.cs` 340–411 (`Conformance_E` as the scan template; the
  `PlainNounStaticClasses` allowlist a new static class must join); `Tests/EditMode/ToolkitPaletteTests.cs`
  1–80 (the mirror test that gates D15).
- `Assets/_Vault/Memories/Code/AnimationToolkit.md` sections "Shared editor chrome", "The 4-arg
  SetButtonIcon does not parent the icon", "A per-frame readout in a transport row re-spaces the whole
  row", "Catalog columns and cover-pane splits are shared".
- Per-tab ranges are on the tasks in §5.

---

## 4. Design

### 4.1 Stylesheet additions and renames (`ClipEditorWindow.uss`, Shared section) — T1

New tokens in `.clip-editor__root`, after the event palette:

```css
    --toolkit-color-playhead: rgb(242, 92, 77);
    --toolkit-color-box-select-fill: rgba(77, 158, 242, 0.18);
    --toolkit-color-box-select-outline: rgba(115, 184, 255, 0.9);
```

New rules, appended to the Shared section (values are the reference tabs' measurements):

```css
/* A cover-pane column: the Texture Packer sidebar's padding, so every tab's columns start their
   content on the same grid. --flush is for a viewport column, whose frame runs edge to edge. */
.toolkit-column { flex-grow: 1; min-width: 200px; padding-top: 8px; padding-left: 10px; padding-right: 10px; }
.toolkit-column--flush { padding-left: 0; padding-right: 0; }

/* Was clip-editor__hint. */
.toolkit-hint { white-space: normal; margin-left: 6px; margin-right: 6px; margin-top: 2px; margin-bottom: 2px; opacity: 0.7; }

/* Was clip-editor__heading. A form section's title. */
.toolkit-heading { -unity-font-style: bold; margin-top: 6px; margin-bottom: 4px; }

/* The selected item's name in a detail column, above its fields. */
.toolkit-detail-title { font-size: 14px; -unity-font-style: bold; white-space: normal; margin-bottom: 6px; }

/* Was clip-editor__toolbar. The row across a tab's top that names its subject: caption + field
   pairs, then whatever acts on the subject as a whole, pushed right by a spacer. */
.toolkit-asset-bar { flex-direction: row; align-items: center; flex-wrap: wrap; flex-shrink: 0;
    padding-top: 4px; padding-bottom: 4px; padding-right: 6px; border-bottom-width: 1px; border-bottom-color: rgba(0, 0, 0, 0.35); }
.toolkit-asset-bar__label { -unity-text-align: middle-left; margin-left: 10px; margin-right: 4px; }
.toolkit-asset-bar__field { width: 200px; }
.toolkit-asset-bar__spacer { flex-grow: 1; }

/* The one action a tab exists for. Accent fill and bold so it is found before it is read. Built
   through ToolkitIcons.MakeIconTextButton, so the word is a Label child and keeps its measure. */
.toolkit-primary-action { min-height: 28px; padding-left: 14px; padding-right: 14px;
    background-color: var(--toolkit-color-accent); color: rgb(245, 248, 252); border-color: rgba(0, 0, 0, 0.4); }
.toolkit-primary-action:hover { background-color: rgb(104, 162, 228); }
.toolkit-primary-action:disabled { opacity: 0.5; }
.toolkit-primary-action .toolkit-icon-button__label { -unity-font-style: bold; }
.toolkit-primary-action .toolkit-icon-button__icon { opacity: 1; }

/* A status row that closes a column rather than heading a timeline. */
.toolkit-status-row--footer { margin-top: auto; border-top-width: 1px; border-top-color: rgba(0, 0, 0, 0.35); }

/* Tone on any label -- a status, a hint, a secondary line. */
.toolkit-text--dim { opacity: 0.6; }
.toolkit-text--warning { color: var(--toolkit-color-warning); }
.toolkit-text--error { color: var(--toolkit-color-error); }

/* A boxed row inside a ListView: tighter than a free-standing box, flush with the list's edges. */
.toolkit-box.toolkit-list-row { margin-top: 4px; margin-bottom: 4px; margin-left: 0; margin-right: 0; }

/* A chip: one of several small peers in a wrapping strip (the retarget roster). Blocks are its
   five-step coverage bar. */
.toolkit-chip { flex-direction: row; align-items: center; margin-right: 6px; margin-bottom: 4px;
    padding-top: 2px; padding-bottom: 2px; padding-left: 6px; padding-right: 6px;
    border-width: 1px; border-color: var(--toolkit-color-box-border); border-radius: 3px; background-color: var(--toolkit-color-box-fill); }
.toolkit-chip--selected { border-color: var(--toolkit-color-selected); }
.toolkit-chip__blocks { flex-direction: row; margin-left: 6px; }
.toolkit-chip__block { width: 6px; height: 8px; margin-right: 1px; background-color: rgba(255, 255, 255, 0.12); }
.toolkit-chip__block--filled { background-color: var(--toolkit-color-clean); }

/* A severity dot beside a row title. The fill is data (see the conformance marker). */
.toolkit-severity-dot { width: 8px; height: 8px; border-radius: 4px; margin-right: 6px; flex-shrink: 0; }

/* Caption + path + browse button, for "where does the next New land" and output paths. */
.toolkit-path-row { flex-direction: row; align-items: center; margin-top: 4px; margin-bottom: 4px; }
.toolkit-path-row__caption { min-width: 120px; }
.toolkit-path-row__path { flex-grow: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }

/* A sidebar's mode toggles (Images | Recipes), grouped so the header's space-between cannot part them. */
.toolkit-sidebar__modes { flex-direction: row; flex-shrink: 0; }
```

Renames (T1 renames the selectors; T29 renames the call sites): `.clip-editor__hint` → `.toolkit-hint`,
`.clip-editor__heading` → `.toolkit-heading`, `.clip-editor__toolbar` → `.toolkit-asset-bar`,
`.clip-editor__toolbar-label` → `.toolkit-asset-bar__label`, `.clip-editor__object-field` →
`.toolkit-asset-bar__field`. The old `.clip-editor__toolbar` rule (`flex-shrink: 0`) folds into
`.toolkit-asset-bar`; `clip-editor__toolbar` in the UXML on the tab-strip `Toolbar` becomes
`toolkit-asset-bar` too (it only carried `flex-shrink: 0`; the strip's own rules are unaffected).

### 4.2 `Editor/ClipEditor/Shared/ToolkitChrome.cs` — T2

```csharp
public enum ToolkitStatusTone { Neutral, Warning, Error }

public static class ToolkitChrome
{
    public static VisualElement MakeColumn(string elementName);                           // toolkit-column
    public static VisualElement MakePaneHeader(string title, out Label titleLabel, out VisualElement actions); // toolkit-pane-header > toolkit-pane-title + toolkit-pane-actions
    public static Label MakeHeading(string text);                                          // toolkit-heading
    public static Label MakeHint(string text);                                             // toolkit-hint
    public static Label MakeDetailTitle(string text);                                      // toolkit-detail-title
    public static VisualElement MakeAssetBar(string elementName);                          // toolkit-asset-bar
    public static Label MakeAssetBarLabel(string text);                                    // toolkit-asset-bar__label
    public static VisualElement MakeAssetBarSpacer();                                      // toolkit-asset-bar__spacer
    public static Button MakePrimaryAction(Action onClick, string iconName, string tooltip, string text); // MakeIconTextButton + toolkit-primary-action
    public static VisualElement MakeStatusRow(out Label statusLabel, out VisualElement actions, bool isFooter); // toolkit-status-row (+ --footer)
    public static void SetStatus(Label statusLabel, string text, ToolkitStatusTone tone);  // text + toolkit-text--warning / --error
    public static VisualElement MakeListRowSlot(string rowElementName, out VisualElement row); // the ListView slot trap, once
    public static VisualElement MakeSeverityDot(Color fill);                               // toolkit-severity-dot; the fill line carries the D4 marker
}
```

`MakeListRowSlot` returns the clear-background slot and hands back the `toolkit-box toolkit-list-row`
child; the two comments from `ToolkitCatalogColumn.cs:195-207` move here verbatim (they are the only
record of why). `ToolkitChrome` joins `PlainNounStaticClasses` in `PackagingConformanceTests.cs`.

### 4.3 `Editor/ClipEditor/Shared/ViewportFrameElement.cs` — T3

```csharp
public sealed class ViewportFrameElement : VisualElement            // root carries clip-editor__viewport-frame, flex-grow 1
{
    public Image ViewportImage { get; }                              // scaleMode ScaleToFit, flex-grow 1
    public VisualElement Overlay { get; }                            // clip-editor__viewport-overlay, pickingMode Ignore
    public VisualElement OverlayColumn { get; }                      // clip-editor__overlay-column
    public ToolbarButton AddRailButton(string exactIconName, string tooltip, string fallbackText, Action onClick, bool startsRun = false);
    public ToolbarToggle AddRailToggle(string exactIconName, string tooltip, string fallbackText, bool startsRun = false);
    public ToolbarToggle AddRailToggle(Texture iconTexture, string tooltip, string fallbackText, bool startsRun = false);
    public ToolbarButton AddResetCameraButton(Action onClick);       // "d_FrameCapture", fallback "Reset Camera", the ActorEditorPanel tooltip text
}
```

Rail controls carry `clip-editor__overlay-tool-button`, their icon `clip-editor__overlay-tool-icon`
with `pickingMode = Ignore`, `startsRun` adds `clip-editor__overlay-run-break`. The icon `Image` is
created and **parented before** `ToolkitIcons.SetButtonIcon(button, icon, …)` / `SetToggleIcon` is
called — the one place that ordering lives. Hosts add their own classes to `ViewportImage` (the Actor
Editor's `actor-editor__viewport-image`) and attach `PreviewCameraNavigation` to it themselves.

### 4.4 `Editor/ClipEditor/Shared/CatalogSidebarElement.cs` — T4

```csharp
public class CatalogSidebarElement : VisualElement                  // toolkit-column; header = toolkit-pane-header
{
    public string Mode { get; }                                      // the active mode's name
    public event Action<string> ModeChanged;
    public void AddMode(string modeName, string toggleText, VisualElement column, VisualElement headerActions); // toggle carries clip-editor__tab; the toggles live in a toolkit-sidebar__modes group
    public void SetMode(string modeName);                            // shows that column, lights that toggle, hoists its headerActions into the header's toolkit-pane-actions slot
}
```

`TexturePackerSidebar : CatalogSidebarElement` keeps its `SidebarMode` enum, `Images`, `Recipes`,
`SetMode(SidebarMode)` and `RescanProject()` so `TexturePackerPanel` does not change; its body shrinks
to two `AddMode` calls and a mapping. Element names stay (`sidebar-images-toggle`,
`sidebar-recipes-toggle`, `sidebar-actions`).

### 4.5 `Editor/ClipEditor/Shared/PathPickerRowElement.cs` — T5

```csharp
public sealed class PathPickerRowElement : VisualElement            // toolkit-path-row
{
    public PathPickerRowElement(string captionText, string browseTooltip); // captionText null = no caption
    public string Path { get; set; }                                 // the path label's text
    public event Action BrowseRequested;                             // the host opens whichever dialog fits (folder or save file)
}
```

Browse is `ToolkitIcons.MakeIconButton(…, "FolderOpened Icon", browseTooltip, "…")` (resolve with the
`d_` rule like every other constant; T0 probes the name exists on 6.5).

### 4.6 `ToolkitPalette` additions — T6

`Playhead`, `BoxSelectFill`, `BoxSelectOutline` as `public static readonly Color` fields and `Tokens`
entries `playhead`, `box-select-fill`, `box-select-outline`, the rgb values in 4.1. Nothing else in the
file changes.

### 4.7 `Tests/EditMode/EditorStyleConformanceTests.cs` — T7

Two fixtures, one scan. Pattern (comments stripped first, like `Conformance_E`):

```
style\.(backgroundColor|color|opacity|fontSize|unityFontStyleAndWeight|unityTextAlign|border(Top|Bottom|Left|Right)Color|border(Top|Bottom|Left|Right)Width|border(TopLeft|TopRight|BottomLeft|BottomRight)Radius|unityBackgroundImageTintColor)\s*=
```

A matching line is exempt when the **raw** line ends with `// colour from data`. Scope: `Editor/**/*.cs`
minus `Editor/Inspectors/`. `InlineStyleAllowlist` is a `HashSet<string>` of package-relative paths
the amendment does not convert (T0 fills it: the scan's violating files minus every file a wave-2
task names). `Conformance_I_NoInlineVisualStyles_OutsideTheAllowlist` fails on any unmarked match in a
file not listed; `Conformance_I_AllowlistEntriesStillNeedListing` fails on a listed file with zero
matches (the ratchet only shrinks). The fixture is expected **red** from T7 until wave 2 lands and
green at the wave-2 gate; that is its revert-to-fail proof.

### 4.8 Cutscene empty state — T26

In `RebuildTimeline` (`CutsceneEditorPanel.cs:1959`): the `cutscene == null` branch no longer returns
after adding a label. It builds `headerContent` / `content` exactly as the loaded path does, adds the
ruler row with `contentEndSeconds = 0` and `contentWidth = visibleWidth` (NaN-guarded as the loaded
path is), then `AppendGhostLanes(content, contentWidth)`; the loaded path calls the same after
`BuildHoldRows`. Status text for the empty case ("No cutscene loaded. Pick one in the bar above, or
press New.") goes through the existing status-label path (T0 names the method around line 3784).

```csharp
private GhostLaneStripElement ghostLanes;   // rebuilt per RebuildTimeline

private void AppendGhostLanes(VisualElement content, float contentWidth)
{
    ghostLanes = new GhostLaneStripElement { paintRangeShading = false };
    ghostLanes.style.width = contentWidth;
    content.Add(ghostLanes);
    SyncGhostLanes();
}

private void SyncGhostLanes()   // also from a GeometryChangedEvent on timelineLaneScroll registered once in BuildTimelineColumns
{
    float viewportHeight = timelineLaneScroll.contentViewport.contentRect.height;
    if (ghostLanes == null || !(viewportHeight > 1f)) return;
    float usedHeight = RulerHeight + timelineLaneRowCount * LaneRowHeight;   // header-only rows count as lane rows: T0 confirms MarkAsLaneRow's count includes them, else count content.childCount - 1
    ghostLanes.SyncRows(viewportHeight - usedHeight, (timelineLaneRowCount & 1) == 1);
}
```

`GhostLaneStripElement` gains `public bool paintRangeShading = true;` and guards the
`TimelineRangeShading.Paint` call on it. Nothing else in the strip changes.

### 4.9 The icon sweep (D9) — which button becomes what

| Today | Becomes |
|---|---|
| Cutscene zoom `All` / `Playhead` (`Button { text }`) | `Button` with `toolkit-icon-button toolkit-icon-button--text`, like the Clip Editor's `frame-all-button` |
| Cutscene `Continue` | `MakeIconTextButton(ReleaseHold, ToolkitIcons.Play, tooltip, "Continue")` |
| Cutscene `New` (ToolbarButton, bar-action) | `MakeIconTextButton(CreateCutsceneAsset, ToolkitIcons.Plus, tooltip, "New")` |
| Cutscene `+ Part Track` (inline 10px font) | `MakeIconTextButton(…, ToolkitIcons.Plus, tooltip, "Part Track")` + `toolkit-pane-action`; keeps its `HeaderColumnWidth - 16` width (layout) |
| Cast `Sync` | `MakeIconTextButton(…, "d_Refresh", tooltip, "Sync")` |
| Materials `Select in Inspector` | `MakeIconTextButton(…, "d_UnityEditor.InspectorWindow", tooltip, "Inspector")` |
| Retarget `Pause` / `Reset View` | `TransportCoreElement` + `ViewportFrameElement.AddResetCameraButton` |
| Three `…` folder buttons | `PathPickerRowElement` |
| Health `Scan`, Capture `Capture`, VAT `Bake VAT Textures`, Texture Packer `Bake`, Sprite Sheets `Bake`, Clip Sets `Open in Clip Editor`, Rigs `Use in Clip Editor`, Materials `Create` | `ToolkitChrome.MakePrimaryAction` (icons: `d_Refresh`, `d_Animation.Record`, `d_PreTextureRGB`, `d_PreTextureRGB`, `d_PreTextureRGB`, `editicon.sml`, `editicon.sml`, `d_Toolbar Plus`) |

Icon names not in `ToolkitIcons`' constant table are probed by T0 (`EditorGUIUtility.IconContent`),
and any that fails falls back to the word — the `MakeIconButton` contract, so nothing ships blank.

### 4.10 The adoption recipe (every wave-2 tab task applies this; the task line adds specifics)

1. **Columns.** Each cover-pane column: `ToolkitChrome.MakeColumn(name)` (or add `toolkit-column` to the
   existing element) and delete its inline `paddingTop/Left/Right`. Viewport columns add
   `toolkit-column--flush`. Keep `minWidth` inline (layout).
2. **Header.** Each column starts with `ToolkitChrome.MakePaneHeader(title, out titleLabel, out actions)`.
   A column that had a bare title label or none gets one. Titles are nouns: "Findings", "Finding",
   "Keys", "Used by", "Materials", "Material", "Tracks (n)", "Preview", "Settings", "Frames (n)", the
   selected asset's name where the column edits it.
3. **Asset bar.** If the tab picks its subject, the pickers move into `ToolkitChrome.MakeAssetBar(name)`
   as `MakeAssetBarLabel("Rig")` + field (`toolkit-asset-bar__field`) pairs, a `MakeAssetBarSpacer()`,
   then the whole-subject actions. The fields keep their existing shared-selection callbacks verbatim.
4. **Primary action.** The tab's one primary action (D10) is `ToolkitChrome.MakePrimaryAction`; its
   element name, tooltip and click handler are unchanged.
5. **Headings, hints, status.** Private `BuildHeading` → `ToolkitChrome.MakeHeading`; hand-rolled hint
   labels → `MakeHint`; result labels → a `MakeStatusRow(out status, out _, isFooter: true)` at the
   column's end, written through `SetStatus(label, text, tone)`. Delete the inline bold/colour/margin
   lines those replaced.
6. **Rows and boxes.** Hand-built ListView slots → `MakeListRowSlot`; hand-built boxes → `toolkit-box`
   + `__header` + `__title` + `__body`; chips → `toolkit-chip*`; severity dots → `MakeSeverityDot`.
7. **Colours.** No `new Color(…)` for chrome; `ToolkitPalette.*` or a `toolkit-text--*` class. A colour
   that is data keeps its inline write and ends the line with `// colour from data`.
8. **Buttons.** Apply the 4.9 row(s) for this tab. Any other `new Button { text = "Word" }` momentary
   action becomes `MakeIconTextButton` with the nearest Unity icon, or stays a word if no icon fits
   (say which in the report).
9. **Viewports.** Frame + rail copies → `ViewportFrameElement`; the host keeps its camera navigation,
   render loop and toggles' callbacks.
10. **Names.** Every existing element `name` stays (tests and captures resolve them). Write the new
    class names (`toolkit-hint`, not `clip-editor__hint`).
11. **Report** (≤ 30 lines): the files touched, each inline visual write removed or marked, any button
    left as a word and why, any name the spec got wrong.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; record EditMode/PlayMode totals and the CHANGELOG head.
  Confirm A96F/A97F/A99 are merged (if A99's `Editor/Ragdoll/` exists, add task **T28b** = its panel +
  bodies column by the §4.10 recipe, same wave). Run the §4.7 regex over `Editor/` (Bash, comments not
  stripped is fine for the list) and write `InlineStyleAllowlist` = violating files minus every file a
  wave-2 task names; paste it into T7's brief. Probe the icon names in §4.9 plus `FolderOpened Icon`
  via `mcp__UnityMCP__execute_code` and note misses (the word fallback covers them, but the report
  should say). Name the cutscene status method around `CutsceneEditorPanel.cs:3784` and confirm
  whether `timelineLaneRowCount` counts header-only rows (§4.8). Capture every tab before (the A72
  capture path; `Library/A101Captures/before-<tab>.png`; the Editor must be focused or captures go stale).

### Wave 1 — foundations (seven workers in parallel; one gate)

- [ ] **T1 — Stylesheet [parallel-safe]** — Files: `Editor/ClipEditor/ClipEditorWindow.uss`. §4.1
  verbatim: three tokens, the new rules, the five selector renames. Read: lines 20–92, 93–421, 426–437,
  538–556, 600–628, 1049–1053 only.
- [ ] **T2 — `ToolkitChrome` [parallel-safe]** — Files: new `Editor/ClipEditor/Shared/ToolkitChrome.cs`,
  `Tests/EditMode/PackagingConformanceTests.cs` (the `PlainNounStaticClasses` set only, lines ~391–411).
  §4.2. Read: `ToolkitIcons.cs` 148–192, `ToolkitCatalogColumn.cs` 193–245.
- [ ] **T3 — `ViewportFrameElement` [parallel-safe]** — Files: new
  `Editor/ClipEditor/Shared/ViewportFrameElement.cs`. §4.3. Read: `ActorEditorPanel.cs` 376–420 (the
  copy to lift, tooltip text included), `ToolkitIcons.cs` 215–245.
- [ ] **T4 — `CatalogSidebarElement` [parallel-safe]** — Files: new
  `Editor/ClipEditor/Shared/CatalogSidebarElement.cs`, `Editor/TexturePacker/TexturePackerSidebar.cs`.
  §4.4. `TexturePackerPanel` must compile unchanged.
- [ ] **T5 — `PathPickerRowElement` [parallel-safe]** — Files: new
  `Editor/ClipEditor/Shared/PathPickerRowElement.cs`. §4.5. Read: `ClipSetsPanel.cs` 180–212 (the row
  being replaced).
- [ ] **T6 — Palette tokens [parallel-safe]** — Files: `Editor/ClipEditor/Shared/ToolkitPalette.cs`.
  §4.6. Read: grep `Tokens` and `public static readonly Color` in that file.
- [ ] **T7 — Conformance_I [parallel-safe]** — Files: new `Tests/EditMode/EditorStyleConformanceTests.cs`.
  §4.7 with T0's allowlist pasted in. Read: `PackagingConformanceTests.cs` 340–370 (`StripComments`,
  `PackageRootPath`, `ToPackageRelativePath` — reuse by making them `internal static` if they are
  private; that is the only other-file edit allowed, and it counts as this task's second file).
- **Gate wave 1.** Compile; `ToolkitPaletteTests` (green: T1+T6 agree); `PackagingConformanceTests`
  (18 of 19, standing `Conformance_A` only); `EditorStyleConformanceTests` (**expected red** — record the
  violation count in §7). Commit `A101-T1..T7`.

### Wave 2 — adoption (twenty-one workers in parallel; T25 → T26 sequential; one gate)

Each task: "Apply §4.10 to the files below" plus the specifics. Files are disjoint across tasks.

- [ ] **T8 — Health panel + list [parallel-safe]** — Files: `Editor/Health/HealthPanel.cs` (84–156),
  `Editor/Health/HealthFindingListElement.cs` (ctor + `MakeFindingRow`/`BindFindingRow`). D12: the
  `Toolbar` becomes an asset bar; Scan is the primary action (element name `health-scan-button` stays);
  the three toggles keep `clip-editor__bar-action`; list column gets a "Findings (n)" header (count
  from `SetFindings`); rows through `MakeListRowSlot` + `MakeSeverityDot`.
- [ ] **T9 — Health detail [parallel-safe]** — Files: `Editor/Health/HealthFindingDetailElement.cs`.
  D17; `MakeSectionBox`/`MakeSectionHeader` → `toolkit-box` with `__header`/`__title`/`__body`; the
  column is a `toolkit-column`; the empty label is a `MakeHint`.
- [ ] **T10 — Events columns [parallel-safe]** — Files: `Editor/Events/EventKeyCatalogColumn.cs`
  (32–95, 210–266), `Editor/Events/EventUsageColumn.cs` (24–40). Keys: column class, `MakeListRowSlot`,
  `BudgetWarningColor` → `toolkit-text--warning` on the budget label. Usage: column class + "Used by"
  header.
- [ ] **T11 — Materials [parallel-safe]** — Files: `Editor/Materials/MaterialsPanel.cs` (36–77),
  `Editor/Materials/MaterialInspectorColumn.cs` (22–45). D13 asset bar (Rig + Clip Set fields writing
  `selection.SetRig`/`SetClipSet`, the pattern at `RetargetPanel.cs:78-131`); Target dropdown + Create
  (primary) at the bar's right; the "Rig" caption/name labels go (the catalog header title becomes
  "Materials · <rig name>"); result label → footer status; 4.9 Inspector button.
- [ ] **T12 — Retarget panel + preview [parallel-safe]** — Files: `Editor/Retarget/RetargetPanel.cs`
  (66–160), `Editor/Retarget/RetargetPreviewElement.cs` (28–120). Asset bar for Clip Set / Clip / Rig
  (callbacks verbatim); D14: `ViewportFrameElement`, `TransportCoreElement` in a `toolkit-transport`
  row, `ITransportTarget` (`IsPlaying`, `TogglePlay`; `Capabilities = None`; `Step`/`JumpToStart`/
  `JumpToEnd`/`Stop` no-ops; `IsLooping` get/set stored, unused); expose `public ITransportTarget
  TransportTarget`.
- [ ] **T13 — Retarget table + roster [parallel-safe]** — Files:
  `Editor/Retarget/RetargetTrackTableElement.cs` (rows: `MakeListRowSlot`, glyph colours marked as
  data), `Editor/Retarget/RosterCoverageStripElement.cs` (chips → `toolkit-chip*`; the strip becomes a
  footer `toolkit-status-row--footer` host with the heading as `toolkit-status`).
- [ ] **T14 — Capture panel [parallel-safe]** — Files: `Editor/Capture/CapturePanel.cs` (68–120,
  214–350). Asset bar = source kind + the per-kind rows (they wrap inside the bar; callbacks verbatim);
  Preview and Settings headers; headings via `ToolkitChrome`; Capture primary; progress/cancel/result
  as today but result → footer status; preview-time slider stays under the viewport inside a
  `toolkit-transport` row with a "Time" caption.
- [ ] **T15 — Capture viewport [parallel-safe]** — Files: `Editor/Capture/CaptureViewportElement.cs`
  (29–70). `ViewportFrameElement`; the frame's dark fill (`:35`) is the capture background preview —
  keep it inline with the D4 marker; status label → `MakeHint`.
- [ ] **T16 — VAT Bake panel [parallel-safe]** — Files: `Editor/VatBaking/VatBakePanel.cs` (55–215,
  299–306). Asset bar for Clip Set / Rig (+ the freshness badge and resolved-source line stay under the
  bar as a hint row); form column class + "Bake" header; headings via `ToolkitChrome`; Bake primary
  (icon `d_PreTextureRGB`, word "Bake"); `summaryLabel` → footer status; the log stays.
- [ ] **T17 — VAT preview [parallel-safe]** — Files: `Editor/VatBaking/VatPreviewElement.cs` (60–150),
  `Editor/VatBaking/VatFreshnessBadgeElement.cs` (its 11 inline writes → `toolkit-text--*` /
  `toolkit-box` classes; the freshness colours are state, not data — tokens). Viewport →
  `ViewportFrameElement` (ghost toggle via `AddRailToggle(Texture …)`, `startsRun: true`).
- [ ] **T18 — Sprite Sheets panel [parallel-safe]** — Files: `Editor/SpriteSheets/SpriteSheetsPanel.cs`
  (44–170). D11 sidebar (`CatalogSidebarElement`: "sheets" | "images", each catalog's `HeaderActions`
  hoisted); working column: header title = sheet name, `infoLabel` as `toolkit-text--dim` beside it,
  actions = Bake (primary) + Save; Filter/Wrap/Mips/Linear/Match fields move into a `toolkit-box`
  titled "Import settings" under the header; output row → `PathPickerRowElement("Output", …)`;
  imported-hint and depth-warning → `MakeHint` + `toolkit-text--warning`.
- [ ] **T19 — Sprite Sheets frames [parallel-safe]** — Files:
  `Editor/SpriteSheets/SpriteSheetFramesColumn.cs` (30–70 + `MakeFrameRow`). Column class; a real pane
  header ("Frames (n)" title, Remove in actions); rows through `MakeListRowSlot`.
- [ ] **T20 — Clip Sets [parallel-safe]** — Files: `Editor/ClipEditor/Authoring/ClipSetsPanel.cs`
  (145–260 + `ReportFailure`/result writes). Column class; hint → `MakeHint`; folder row →
  `PathPickerRowElement("Folder", tooltip)` (`BrowseRequested` → `OnFolderButtonClicked`); Open in Clip
  Editor → primary; result label → footer status with `Error` tone on failure.
- [ ] **T21 — Rigs [parallel-safe]** — Files: `Editor/ClipEditor/Authoring/RigsPanel.cs` (238–372,
  481–488, 812). Same recipe as T20 (Use in Clip Editor → primary; `BuildHeading` → `ToolkitChrome`;
  `:812` colour → `Error` tone).
- [ ] **T22 — Texture Packer [parallel-safe]** — Files: `Editor/TexturePacker/TexturePackerPanel.cs`
  (33–105), `Editor/TexturePacker/ImageCatalogColumn.cs` (48–120 + `MakeRow`). Recipe label →
  `toolkit-pane-title`; Bake → primary (Bake As… and Clear unchanged); graph column unchanged;
  image column class + `MakeListRowSlot`.
- [ ] **T23 — Actor Profiles [parallel-safe]** — Files:
  `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs` (323–480),
  `Editor/ClipEditor/ActorEditor/DirectionSetClipQueueView.cs` (115–140). Viewport → `ViewportFrameElement`
  (billboard/ragdoll toggles via `AddRailToggle`, callbacks verbatim; `cameraNavigation.AttachTo(frame.ViewportImage)`);
  `viewportStatusLabel` → `MakeHint`; the two colour literals → `toolkit-text--dim` / `--warning`.
- [ ] **T24 — Rigs preview [parallel-safe]** — Files:
  `Editor/ClipEditor/Authoring/RigSourcePreviewElement.cs` (55–110). Viewport → `ViewportFrameElement`
  (show-excluded toggle via `AddRailToggle`, `startsRun: true`).
- [ ] **T25 — Cutscene chrome (first of a sequential pair)** — Files:
  `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` lines 131–233 and 386–585 **only**. D8 (three
  `CoverPaneSplitView`s), D16 (transport order; "Length" caption + `timeEndLabel` as a leading derived
  group — no counter class, it changes only on edit), 4.9 rows for All/Playhead/Continue/New; the
  toolbar → `MakeAssetBar("cutscene-editor-asset-bar")` with `MakeAssetBarLabel("Cutscene")`;
  `sceneStatusLabel` → `toolkit-text--dim`; `sceneActionButton` → `MakeIconTextButton(…, "d_SceneAsset Icon", …)`
  keeping its dynamic text through `SetButtonIconAndText`.
- [ ] **T26 — Cutscene timeline empty state + rows (after T25 reports)** — Files:
  `Editor/ClipEditor/Cutscene/CutsceneEditorPanel.cs` lines 337–360 and 1959–2350, plus the inline
  visual lines at 2554, 2988 (mark `// colour from data`), 4400, 4526, 4777, 5265 and `BuildHeading`
  at 5438; `Editor/ClipEditor/GhostLaneStripElement.cs`. §4.8; "+ Part Track" per 4.9; `BuildHeading`
  → `ToolkitChrome.MakeHeading`; `:4526` → `toolkit-text--warning`; `:4400`/`:5265` → `toolkit-text--dim`.
- [ ] **T27 — Cast panel + validation badge [parallel-safe]** — Files:
  `Editor/ClipEditor/Cutscene/CutsceneCastPanel.cs` (54–108), `Editor/ClipEditor/ValidationBadgeElement.cs`
  (18–20 + any `style.color` writes). Cast: column class (drop the inline padding), Sync per 4.9; badge:
  the three statics → `ToolkitPalette.Error/Warning/Clean`.
- [ ] **T28 — Playheads + box select [parallel-safe]** — Files: `Editor/ClipEditor/PlayheadElement.cs`,
  `Editor/ClipEditor/Cutscene/CutsceneTimelinePlayheadElement.cs` → `ToolkitPalette.Playhead`; and
  (a second worker, T28b if A99 is absent) `Editor/ClipEditor/BoxSelectElement.cs` → `BoxSelectFill` /
  `BoxSelectOutline`.
- **Gate wave 2.** Compile. `EditorStyleConformanceTests` (**now green**, both fixtures);
  `ToolkitPaletteTests`; `ClipEditorLayoutTests`; `ClipSetsPanelTests`, `RigsPanelTests`,
  `VatBakePanelTests`, `ActorEditorPanelTests`, `ActorEditorInspectorColumnTests` (the panel fixtures
  that construct these elements). Commit `A101-T8..T28`.

### Orchestrator steps

- [ ] **T29 — Rename sweep + window routing.** sed over `Editor/**/*.cs`, `ClipEditorWindow.uxml`,
  `Tests/**/*.cs` for the five D5 renames (whole-token, so `clip-editor__hint` does not also hit a
  longer name — check with `grep -rn "clip-editor__hint\|clip-editor__heading\|clip-editor__toolbar\|clip-editor__object-field"`
  afterwards: zero hits). In `ClipEditorWindow.cs`, `ResolveActiveTransportTarget` gains
  `ClipEditorTab.Retarget → retargetPanel.TransportTarget` (D14), and the Rigs / Clip Sets primary
  buttons' handlers are unchanged. Gate; `ClipEditorLayoutTests`. Commit `A101-T29`.
- [ ] **T30 — Docs + changelog (one worker, [parallel-safe] with T29)** — Files:
  `Documentation~/index.md` ("Windows and inspectors": one paragraph, "The window's look", naming the
  asset bar / column / pane header / primary action / status footer families and that a conformance
  test keeps inline visual styles out — no list of classes, the stylesheet is the list),
  `CHANGELOG.md` (`## [0.53.0] — A101 — Editor chrome consistency`: Changed / Added / Removed, one line
  per tab).
- **Fixtures.** Keep `EditorStyleConformanceTests` (its red-then-green across the waves is the proof).
  Optional: `ViewportFrameElementTests.RailButton_ParentsItsIconBeforeResolving` — build the element,
  `AddRailButton("d_FrameCapture", …)`, assert `button.Q<Image>() != null` and either an image or the
  fallback word; keep only if removing the `Insert(0, icon)` line in T3's file makes it fail.
- [ ] **T31 — Drive.** Full suites (totals must not drop). Then, per tab, on the docked window with the
  Editor focused: switch to it, capture `Library/A101Captures/after-<tab>.png`, and check the eight
  things by eye in each capture: column padding equal across columns; a titled header on every column;
  one primary action, accent-filled; hints dim and wrapping; result in a footer status row; no plain-word
  momentary buttons except the ones the reports listed; the cutscene timeline empty → ruler + stripes,
  status text in the status row; the Clip Editor and Texture Packer look as they did in `before-`.
  Retarget: Space toggles the preview. Cutscene: drag every divider, hide and show the tab, dividers
  remembered. Sprite Sheets: Images mode → drag an image onto Frames still adds a frame. Scratch assets
  only (`Assets/A101Scratch/`), deleted after; registry sha256s unchanged.
- [ ] **T32 — Close.** Status line; `package.json` and the conformance version pin; HANDOFF §4 (one
  paragraph); `AnimationToolkit.md` (a "Chrome is shared (A101)" section: the four elements, the
  conformance marker, the ratchet allowlist rule, and any trap the waves found); roadmap checkbox;
  commit and push.
- [ ] **T33 — ⏸ owner checkpoint.** Message: "A101 — every tab now builds from the same chrome.
  Open `Library/A101Captures/` (before/after per tab) or flip through the tabs. Three interpretations
  to confirm: (1) Cutscene Director with nothing loaded now shows the ruler and striped rows with the
  message in the status line — is that what 'key rows visible even when nothing is selected' meant,
  or did you mean one override row per rig part always listed? (D7) (2) Sprite Sheets' two catalogs
  are now Sheets | Images toggles in one sidebar, like the Texture Packer's Images | Recipes (D11).
  (3) Materials gained Rig and Clip Set pickers in its top bar so you no longer go to the Rigs tab to
  change the subject (D13). Everything else is the Clip Editor's and Texture Packer's own measurements
  applied to the other eleven tabs; those two should look unchanged — say so if they do not."

---

## 6. Deliberately out of scope

- `Editor/Inspectors/*` (the custom asset inspectors): they do not load the window's stylesheet and
  render inside Unity's Inspector; converting them needs a stylesheet-loading decision first. They are
  excluded from `Conformance_I` by folder, not by allowlist, so the ratchet does not pretend to cover
  them.
- Canvas elements that paint with `Painter2D` (lanes, rulers, easing curve, graph nodes, mark overlay,
  contact-sheet preview): their colours are drawing constants, listed in the allowlist until someone
  decides to token them.
- The tab strip (owner exemption), the Clip Editor's UXML layout (it leads; only class names change),
  any behaviour: every callback, event, undo path and element name is unchanged by design.
- A per-rig-part row model for cutscene part tracks (D7's other reading) — its own amendment if the
  owner asks.
- Sound, runtime, and anything under `Runtime/` or `Authoring/`.

---

## 7. Build log

_(empty)_
