# Worktree Toolkit — UI redesign (node window, Cult of the Lamb skill-tree style)

> **Status:** 📝 specced 2026-09-14, not built.
> **Owner input (2026-09-14):**
> - C1 verdict on the first window: *"Definitely needs some ui work, pretty ugly and unprofessional looking in its current state."*
> - Style direction: *"I gave you an image of the game Cult of the Lamb skill tree in tasks/claude. I like the colors and style."*
>   The reference is `Assets/_Vault/Tasks/Claude/Cult-of-the-Lamb10312022-092045-91069.jpg`.
>
> **Executor:** orchestrator + `worker`s in one parallel wave against §5/§6, then one gate.
> **Blocked on:** the C2 dry run finishing (redesign compiles would clash with its stage swaps).
> **Before building:** capture the current window (needs `EditorApplication.isFocused`; ask the owner to click into Unity).
> Parent: [`WorktreeToolkit_Roadmap.md`](WorktreeToolkit_Roadmap.md); Editor spec: [`WorktreeToolkit_Phase23_Editor.md`](WorktreeToolkit_Phase23_Editor.md).

## 1. What reads as unprofessional today (from the code; no capture was possible, Unity unfocused)

- **Toolbar:** a plain row of text glyphs (`⟳`, `＋`), a checkbox labelled "Broker", and bare
  "Stage: main" / "Queue: 0" labels, with no grouping.
- **Cards:** four stacked labels with no hierarchy, text glyphs for actions, and a hand-picked palette.
  Inactive cards are whole-card `opacity: 0.45`, which makes them unreadable.
- **Canvas:** free Bézier edges, no ground, no selection, no empty state.
- **The gate queue** is a bare foldout floating above the canvas.
- **Every action lives on the card;** there is nowhere to read details.

## 2. Style reference, read from the image

| Element in the image | What it looks like | Worktree window equivalent |
|---|---|---|
| Ground | near-black charcoal `≈rgb(22,22,22)`, flat | canvas ground |
| Title bar | solid black band, centred cream title flanked by small ornaments | window header: "Worktrees" + stage chip |
| Footer bar | solid black band, round cream key badges ("A Accept", "B Back") | footer key hints: "Double-click · Put on stage", "Right-click · Actions", "F5 · Refresh" |
| Locked node | square tile, dark grey fill, thin grey border, lock badge top-right, icon faded | stale / parked / unclaimed / missing / locked worktree |
| Unlocked node | square tile, grey-teal lit fill with faint radial rays, **thick bright-teal border** | a worktree you can act on (building / ready / done) |
| Current node | square tile, **thick crimson border** | the tile on stage (trunk or a reviewed branch) |
| Connectors | thick straight lines, right angles only: grey locked, teal unlocked, **crimson along the current path** | edges: crimson from the trunk to the on-stage tile, teal to actionable tiles, grey to locked ones |
| Selection cursor | white rounded-square outline offset from the tile, an eye ornament above it | selected tile |
| Selected caption | cream serif name under the tile on a dark brush-stroke strip | the selected tile's full branch name on a black strip; other tiles show a short dim caption |
| Divider | long cream horizontal rule | separator between the canvas and the gate-queue strip |

**Palette** (sampled from the image; USS custom properties on `:root`, mirrored as C# constants for
Painter2D in `WorktreePalette`):

| Token | Value | Use |
|---|---|---|
| `--worktree-color-ground` | `rgb(22, 22, 22)` | canvas, inspector ground |
| `--worktree-color-bar` | `rgb(8, 8, 8)` | header, footer, status bar, caption strip |
| `--worktree-color-tile` | `rgb(44, 46, 45)` | locked tile fill |
| `--worktree-color-tile-lit` | `rgb(74, 88, 84)` | unlocked / current tile fill |
| `--worktree-color-line-locked` | `rgb(60, 60, 60)` | locked border and connector |
| `--worktree-color-teal` | `rgb(32, 190, 158)` | unlocked border, connector, positive state |
| `--worktree-color-crimson` | `rgb(200, 32, 30)` | on-stage border, current path, failures, destructive actions |
| `--worktree-color-cream` | `rgb(240, 234, 214)` | titles, captions, key badges |
| `--worktree-color-cream-dim` | `rgba(240, 234, 214, 0.45)` | secondary text |
| `--worktree-color-selection` | `rgb(245, 245, 245)` | selection outline |
| `--worktree-color-amber` | `rgb(232, 170, 60)` | gating pulse only (the image has no amber; keep it to that one use) |

**Font:** Unity's default editor font in cream, bold for titles, with no custom font this pass. ⚠
**Owner question for the checkpoint:** the image's serif, hand-drawn look needs a bundled font file
(e.g. an OFL-licensed serif shipped in the package); the owner decides whether it is worth it.

Carried over from the owner's editor rules: symbols over words for actions (Unity built-in icons),
peers visually separate, one palette, and an icon beside a word via a `Label` child (never
`button.text` beside an `Image`).

## 3. The design

```
┌ Header bar (black) ─────────────────────────────────────────────────────────────────────┐
│   ~ ⎇  Worktrees ~        [stage: dryrun-trunk ⚠6]           [↻] [+] [▶ Broker ●] [⋮]     │
├ Canvas (charcoal) ────────────────────────────────────────────┬ Inspector (charcoal) ────┤
│                                                               │  SPEC/ALPHA              │
│   ┏━━━━━━┓           ┏━━━━━━┓ (teal)                          │  ● ready                  │
│   ┃  ⎇   ┃━━━━━┳━━━━━┃  ✔   ┃                                 │  Spec    alpha.md         │
│   ┗━━━━━━┛     ┃     ┗━━━━━━┛                                 │  Models  haiku ▸ haiku    │
│   dryrun-trunk ┃     spec/alpha                               │  Ahead 1 · Behind 0       │
│   (crimson =   ┃      ╭ 👁 ╮                                   │  ─────────────────────── │
│    on stage)   ┃     ┌┏━━━━━━┓┐ (selected: white outline)      │  [👁 Put on stage]  teal  │
│                ┗━━━━━┃  ◌   ┃                                 │  [⤓ Merge]          teal  │
│                      ┗━━━━━━┛                                 │  [🗑 Remove]      crimson │
│                   ▐ spec/beta ▌  (caption strip)              │  [📂 Reveal]              │
│ ═══════════════════════════════════════ (cream divider) ═════ │  ─ Gate queue (1) ─────── │
│                                                               │  beta   compiling   42 s  │
├ Footer bar (black) ───────────────────────────────────────────┴──────────────────────────┤
│  (⎋) Double-click  Put on stage    (☰) Right-click  Actions    (↻) F5  Refresh             │
└──────────────────────────────────────────────────────────────────────────────────────────┘
```

- **Header bar** (`worktree-header`, black, 34 px)
  - Left: branch icon + "Worktrees" (cream bold 14) flanked by two dim `~` ornaments (labels).
  - Then the stage chip: black with a cream 1 px border, "stage: <branch>", and a `⚠ N` amber badge
    when files are modified. Clicking it selects the trunk tile.
  - Flex spacer.
  - Right: icon buttons, each 24×22, transparent until hover (`rgba(240,234,214,0.08)`):
    - Refresh;
    - Create (opens the inline create row under the header);
    - the Broker toggle: PlayButton icon + "Broker" + a status dot, teal when the heartbeat is alive,
      line-locked grey when off;
    - Menu (`_Menu`): Adopt existing worktrees · Edit noise globs… · Open documentation · Gate broker
      enabled ✓.
- **Status line** (under the header, black, 20 px, cream-dim 11): "N worktrees · N building · N
  gating · N ready". While the stage is busy, the animated `WaitSpin` icon plus "busy: <holder>" in
  amber. Errors replace the line in crimson with the console error icon; clicking copies the message.
- **Canvas** (left of a `TwoPaneSplitView`; the fixed pane is the inspector at index 1, start 300 px)
  - Charcoal ground; clicking empty space clears the selection.
  - A layout slot is 132×112: a 72×72 tile centred at the top, a caption under it.
  - `left = 24 + column × 184`, `top = 24 + row × 132`.
- **Tile** (`WorktreeNodeElement`)
  - *Border and fill:*
    - locked: 2 px `line-locked` border, `tile` fill, icon at 35 % opacity, lock badge (12 px) at the
      top-right corner;
    - unlocked: 3 px `teal` border, `tile-lit` fill, faint radial rays (12 lines from the centre,
      cream at 6 % alpha, Painter2D);
    - on stage: 3 px `crimson` border, `tile-lit` fill with rays.
  - *Central icon* (32 px):
    - trunk: branch icon;
    - worktree by lead status: building → `WaitSpin` frames, ready/done → `TestPassed`, failed →
      `TestFailed`, gating → `WaitSpin` with an amber corner pip pulsing between 100 % and 35 % alpha
      every 600 ms, unclaimed → `Project`.
  - *Locked* means: `stale`, `missing`, `locked`, `parked`, or lead `unclaimed`.
  - *Caption* under the tile: the branch's short name (text after the last `/`), cream-dim 11,
    ellipsized to 132 px. When selected it is the full branch name in cream 12 bold on a black strip
    (`worktree-node__caption--selected`), like the image's "Temple".
  - *Selection:* a white 2 px rounded (radius 6) outline 6 px outside the tile, and an eye icon
    (`animationvisibilitytoggleon`, 14 px, cream) centred above it.
  - Hover: border brightens (teal → `rgb(80,220,190)`, locked → `rgb(90,90,90)`).
  - Click selects; double-click → PutOnStage (dialog confirms); right-click → the same actions menu as
    the inspector.
- **Edges** (`WorktreeEdgeLayer`):
  - Straight orthogonal polylines only, no rounding: parent tile right-middle → horizontal to the
    midpoint column → vertical → horizontal into the child tile's left-middle.
  - Width 3 px. Colour: crimson when the child is on stage, or it is the trunk's own edge and the
    trunk is on stage; teal when the child is unlocked; `line-locked` otherwise.
  - Draw locked lines first, then teal, then crimson, so the current path sits on top.
- **Divider + gate strip:** a cream 2 px rule across the canvas bottom, 28 px above it, with the gate
  queue as a single horizontal strip of chips under the rule ("beta · compiling · 42 s"). An empty
  queue shows "gate queue empty" in cream-dim.
- **Inspector** (`WorktreeInspectorPane`, charcoal)
  - *Header:* branch name uppercase cream bold 13, the lead-status dot and word under it.
  - *Key/value rows* (key cream-dim 11, value cream 12): Spec, Models, Ahead / Behind, Uncommitted,
    Path (ellipsized, tooltip full, click to reveal), Last gate (verdict + time, when known).
  - *Thin `line-locked` rule*, then full-width action buttons (icon + word, 28 px, 1 px border):
    - Put on stage (eye) or Return to trunk (`back`) — teal border;
    - Merge (`Import`) — teal;
    - Remove (`TreeEditor.Trash`) — crimson;
    - Reveal (`FolderOpened Icon`) — `line-locked`.
    - Buttons that cannot run are disabled with the reason in the tooltip.
  - Nothing selected: a centred cream-dim hint "Select a tile".
- **Footer bar** (`worktree-footer`, black, 30 px): three hints, each a round cream badge (20 px circle,
  black icon or letter) + cream-dim word: "Double-click · Put on stage", "Right-click · Actions",
  "F5 · Refresh" (F5 refreshes while the window has focus).
- **Empty state:** zero worktrees shows the trunk tile plus a cream-dim hint under it: "No worktrees
  yet. Run `/worktree-run` in Claude, or press + to create one."

### Icons (probed present on 6000.5, pro skin, 2026-09-14)
| Use | Name |
|---|---|
| Refresh / Create / Menu | `Refresh` / `Toolbar Plus` / `_Menu` |
| Branch (header, trunk tile) | `UnityEditor.VersionControl` |
| Selection eye / Put on stage | `animationvisibilitytoggleon` |
| Return to trunk / Merge / Remove / Reveal | `back` / `Import` / `TreeEditor.Trash` / `FolderOpened Icon` |
| Broker on / off | `PlayButton` / `PauseButton` |
| Building / gating spinner | `WaitSpin00`…`WaitSpin11` (⚠ 01–11 unprobed) |
| Ready / failed | `TestPassed` / `TestFailed` (resolve without `d_`) |
| Unclaimed | `Project` |
| Warning / error | `console.warnicon.sml` / `console.erroricon.sml` |
| Lock badge | `InspectorLock` (dark and light both present; T0 probe 2026-09-14) |
Missing (do not use): `VersionControl.Local`, `CollabNew Icon`.

## 4. Behaviour kept from Phase 2–3 (do not regress)

- CLI task polling in `DrainCompletedCliTasks`, before the focus check.
- `[System.NonSerialized]` session fields.
- `StageSwapGuard` for every stage move, with confirm dialogs.
- Never `SaveAssets`.
- Poll `list` every 3 s while focused.

## 5. Pinned surfaces (one parallel wave)

```csharp
// T1 Editor/Window/WorktreeIcons.cs
public static class WorktreeIcons
{
    public const string Refresh = "Refresh", Create = "Toolbar Plus", Menu = "_Menu", Branch = "UnityEditor.VersionControl",
        SelectionEye = "animationvisibilitytoggleon", ReturnStage = "back", Merge = "Import", Remove = "TreeEditor.Trash",
        Reveal = "FolderOpened Icon", BrokerOn = "PlayButton", BrokerOff = "PauseButton", Ready = "TestPassed", Failed = "TestFailed",
        Unclaimed = "Project", Warning = "console.warnicon.sml", Error = "console.erroricon.sml", SpinnerFramePrefix = "WaitSpin";
    public static string LockIconName { get; }                                   // first of the §3 lock candidates that resolves (T0 fills the list), else null
    public static Texture2D Resolve(string iconName);                            // d_ on pro skin, fallback to the other, null when neither
    public static Texture2D SpinnerFrame(double editorTimeSeconds);              // WaitSpin00..11 at 100 ms per frame
    public static Image MakeIcon(string iconName, float sizePixels);             // class worktree-icon, pickingMode Ignore
    public static Button MakeIconButton(System.Action onClick, string iconName, string tooltip, string fallbackText);   // class worktree-icon-button
    public static Button MakeIconTextButton(System.Action onClick, string iconName, string tooltip, string text);      // Label child; class worktree-icon-button--with-text
}
// T1b Editor/Window/WorktreePalette.cs
public static class WorktreePalette
{
    public static readonly Color Ground, Bar, Tile, TileLit, LineLocked, Teal, TealHover, Crimson, Cream, CreamDim, Selection, Amber; // §2 values
}
// T2 Editor/Window/WorktreeToolkit.uss — full rewrite; §6 class list; §2 tokens on :root.
// T3 Editor/Window/WorktreeNodeElement.cs — rewrite; keeps enum WorktreeNodeAction { PutOnStage, ReturnStage, Merge, Remove, Reveal }
public enum WorktreeTileState { Locked, Unlocked, OnStage }
public sealed class WorktreeNodeElement : VisualElement
{
    public const float SlotWidth = 132f; public const float SlotHeight = 112f; public const float TileSize = 72f;
    public string NodeId { get; }
    public WorktreeTileState TileState { get; }                                   // for the edge colours
    public event System.Action<string> SelectionRequested;
    public event System.Action<string, WorktreeNodeAction> ActionRequested;       // double-click → PutOnStage; right-click menu
    public WorktreeNodeElement(string nodeId);
    public void BindWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy);
    public void BindTrunk(StageDto stage, string trunkBranch, bool isOnStage);
    public void SetSelected(bool isSelected);
    public void Tick(double editorTimeSeconds);                                   // spinner frame + gating pulse; the window calls it from update
}
// T4 Editor/Window/WorktreeEdgeLayer.cs — replaces WorktreeEdge
public readonly struct WorktreeEdge { public readonly Vector2 start; public readonly Vector2 end; public readonly WorktreeTileState childState; public WorktreeEdge(Vector2 start, Vector2 end, WorktreeTileState childState); }
public sealed class WorktreeEdgeLayer : VisualElement { public WorktreeEdgeLayer(); public void SetEdges(IReadOnlyList<WorktreeEdge> edges); } // §3 edges, draw order locked → teal → crimson
// T5 Editor/Window/WorktreeInspectorPane.cs — new
public sealed class WorktreeInspectorPane : VisualElement
{
    public event System.Action<string, WorktreeNodeAction> ActionRequested;
    public WorktreeInspectorPane();
    public void ShowNothingSelected();
    public void ShowTrunk(StageDto stage, string trunkBranch, bool trunkIsOnStage);
    public void ShowWorktree(WorktreeNodeDto node, bool isOnStage, bool stageIsBusy);
}
// T6 Editor/Window/GateQueueStrip.cs — replaces GateQueuePanel.cs (delete that file and its .meta)
public sealed class GateQueueStrip : VisualElement { public GateQueueStrip(); public void Refresh(GateRequestStore requestStore); }
// T7 Editor/Window/WorktreeFooterBar.cs — new
public sealed class WorktreeFooterBar : VisualElement { public WorktreeFooterBar(); }
// T8 Editor/Window/WorktreeGraphWindow.cs — restructure: header, status line, split view, canvas slots, selection state,
//     edge colours from TileState, divider + GateQueueStrip, footer, empty state, F5, Tick on update; keeps §4.
```

## 6. USS class contract

`worktree-window`, `worktree-header`, `worktree-header__title`, `worktree-header__ornament`,
`worktree-header__stage-chip`, `worktree-header__badge`, `worktree-header__broker-dot` (`--alive`),
`worktree-status-line`, `worktree-status-line__text` (`--busy`, `--error`), `worktree-create-row`,
`worktree-canvas`, `worktree-empty-hint`, `worktree-node`, `worktree-node__tile` (`--locked`, `--unlocked`,
`--on-stage`), `worktree-node__tile-icon` (`--dimmed`), `worktree-node__lock`, `worktree-node__pip`,
`worktree-node__caption` (`--selected`), `worktree-node__selection`, `worktree-node__selection-eye`,
`worktree-divider`, `worktree-queue-strip`, `worktree-queue-chip` (`--active`), `worktree-inspector`,
`worktree-inspector__title`, `worktree-inspector__status`, `worktree-kv`, `worktree-kv__key`,
`worktree-kv__value`, `worktree-inspector__rule`, `worktree-action-button` (`--teal`, `--crimson`,
`--neutral`), `worktree-footer`, `worktree-footer__hint`, `worktree-footer__key`, `worktree-footer__word`,
`worktree-icon`, `worktree-icon-button` (`--with-text`), `worktree-icon-button__label`.

## 7. Tasks

- **T0 (orchestrator, Editor):**
  - Capture the current window once Unity has focus (baseline; still owed, Unity was unfocused).
  - ✅ 2026-09-14: `WaitSpin00`–`11` all present; lock = `InspectorLock`;
    `TwoPaneSplitView(Int32 fixedPaneIndex, Single fixedPaneStartDimension, TwoPaneSplitViewOrientation orientation)`
    confirmed. `WorktreeIcons.LockIconName` is a plain const `"InspectorLock"`.
- **T1, T1b, T2, T3, T4, T5, T6, T7, T8 [parallel-safe]:** one worker each; files disjoint; code only
  against §5/§6.
- **T9 (orchestrator):**
  - One compile gate and `WorktreeTreeLayoutTests`.
  - Drive: select a tile, double-click to stage, return, then create and remove a scratch worktree.
  - Capture after, next to the reference image.
  - ⏸ **Owner checkpoint** with before / after / reference, and ask the font question in §2.
