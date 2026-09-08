# Amendment A72 — Editor Visual Unification

> **Status:** ✅ built 2026-09-07 (0.18.0), T0–T12 gated green. Open: the owner's visual pass on
> the BEFORE/AFTER captures in `Library/A72Captures/`. Two documented fallbacks (cutscene `F`
> centres on the playhead; the eye test calls `ToggleLayerDefaultActive`) are in HANDOFF §4.
> **Executor:** one orchestrating session (Editor-connected, runs the gate) plus **small Sonnet or
> Haiku subagents that only edit files** — a subagent never touches `mcp__UnityMCP__*`
> (`AnimationToolkit.md` "Do not spawn subagents against this package" is about MCP contention,
> not about file edits). Each task below is sized for one subagent under ~100k tokens: named files,
> named line ranges, no re-reading of what the orchestrator already knows.
> **Protocol:** `Docs/AnimationToolkit/HANDOFF.md` §2 (A69 comment rule, static-class suffixes, UI
> Toolkit only) and §3 (gate + discovered counts). Commit prefix `A72-Tn`.

---

## 1. What the owner asked for (2026-09-07, verbatim where it matters)

> "I want clip editors layout to lead, so cutscene editor should try to mimic it with play and
> animation controls being the middle bar instead of on the top. I want play and pause space bar to
> work on all areas with play and other common sense controls … I really like the play pause and
> move symbols used on the cutscene editor page and would like the other play controls to be the
> same style. I prefer symbols over words, though I like how the current top button look those
> should stay the same. For the animation layers, maybe box the various layers so they visually
> look separate, also use an enableable eye to toggle layers on and off … I never want a list of
> things like components or layers to not look like separate things. also be consistent with
> colors, choose a different color for different events too in the timeline."

Five requirements fall out of that, and every task in §6 serves one of them:

| # | Requirement | Where it lands |
|---|---|---|
| R1 | **The Clip Editor's layout leads.** Top bar = asset identity + tabs; transport bar is the *middle* strip docked on the timeline; a status row over the key area holds "what will my next edit do"; panes carry bold titles with actions pushed right. | §4.2 (Cutscene), §4.3 (Actor) |
| R2 | **One transport, one look, everywhere.** The cutscene's icon buttons (Unity's own `PlayButton`/`PauseButton`/`Animation.FirstKey`…) become the package's transport style. Space, arrows, Home/End work on every tab that plays. | §3.3, §3.6, T3–T5, T6, T9 |
| R3 | **Symbols over words** for actions; the top tab strip is exempt and stays exactly as it is. | §3.2, every task that builds a button |
| R4 | **Peers are boxed.** A layer, a cast member, a direction slot, a candidate node — anything that is one of several like things — draws as its own bordered box. | §3.4, T10, T11 |
| R5 | **One palette.** Shared state colours (playing, selected, looping, holding, recording) mean the same thing in every tab; timeline lane families keep their strips; **each event name gets its own colour**, the same colour in the Clip Editor and the Cutscene Editor. | §3.1, T1, T7, T8 |

## 2. Decisions (recorded — do not re-ask)

- **D1 — Shared building blocks live in `Editor/ClipEditor/Shared/`.** Four new files:
  `ToolkitPalette.cs`, `ToolkitIcons.cs`, `ITransportTarget.cs`, `TransportCoreElement.cs`. All in
  namespace `DotsAnimationToolkit.Editor`. The static classes are plain nouns (A69 §2.1: none of
  the role suffixes fit, and `Util`/`Helper`/`Common` are banned).
- **D2 — Playback is behind one interface.** `ITransportTarget` (§3.5). `ClipEditorWindow`
  implements it explicitly for the Clip Editor tab; `CutsceneEditorPanel` and `ActorEditorPanel`
  implement it publicly. The window's root `KeyDownEvent` handler dispatches Space / arrows / Home /
  End to **the active tab's target**, resolved from `activeTab`. Today that handler drives the
  hidden Clip Editor whatever tab is up — that is the bug behind "Space does nothing on the
  cutscene tab" (it toggles the invisible clip preview instead).
- **D3 — The shared part of a transport is the button run, not the whole bar.** Each tab's bar
  keeps its own captions and fields (Length/FPS, Time/Speed, Zoom…); `TransportCoreElement` is the
  `|◀ ◀ ▶ ■ ▶ ▶| ⟳` run every bar inserts. Capability flags hide the buttons a target cannot
  honour, so the run caps itself at whichever button is showing (the same rule
  `.clip-editor__bar-action` already states for the top bar).
- **D4 — Stop means "pause and return to where Play was pressed".** The cutscene already does this;
  the Clip Editor gains it; the Actor Editor's Stop is pause + `composer.Reset()` (its old Reset
  button, now the ■). Only the Cutscene Editor's Stop also clears a held rendezvous.
- **D5 — Selected is blue, everywhere.** `ToolkitPalette.Selected` = the Clip Editor's current
  selected-key fill. The cutscene's yellow selection border becomes this blue; yellow is left to
  mean *holding* (and the dimmer loop-on tint), which it already does in the cutscene. A selected
  pose key fills blue; a selected event pin or cutscene marker **keeps its own fill** and takes a
  2px blue stroke, so colour identity survives selection.
- **D6 — Event colour is a pure function of the event key.** `ToolkitPalette.ColorForEventKey`
  hashes the key onto an 8-entry palette (§3.1). Both editors read the same
  `AnimEventKeyRegistry`, so one name is one colour in both timelines and across sessions. No
  per-event colour is authored or stored anywhere.
- **D7 — The eye is `defaultActive`.** The Actor Editor's per-layer eye replaces the
  `defaultActive` checkbox: it writes the same field through the same undo path, and because every
  profile edit already marks the composer stale, the preview re-seeds and the layer visibly stops
  or starts. The live dot stays as the "is playing right now" readout — two facts, two indicators.
- **D8 — Boxing applies to hand-built lists, not to Unity's `ListView`/`TreeView`.** The Clips list
  and the Rig Hierarchy are selection lists whose rows are affordances, and boxing a `TreeView`
  breaks its indentation. Boxed: actor layers (with their animations inside), cast rows, direction
  slots, New Rig candidate rows. The Clip Inspector's component blocks are already boxed and become
  the reference (`.clip-editor__component` → `.toolkit-box`).
- **D9 — One stylesheet.** `ClipEditorWindow.uss` gains a `Shared` section of `toolkit-*` classes
  and `--toolkit-color-*` tokens. The cutscene and actor panels are inside the window's tree, so
  they already inherit it; no second sheet. The standalone `VatBakeWindow` is out of scope.
- **D10 — Inline styles are for layout only.** A panel built in C# may still set `flexGrow`,
  `width`, `flexDirection`, `marginLeft`. Anything *visual* — a colour, a border, a radius, chrome
  padding, an opacity — must be a class in the sheet. This is the existing rule of the USS file's
  own header, applied to the two panels that grew up without it.
- **D11 — Key map is the Clip Editor's.** Home/End = jump to start/end on every tab. F frames the
  selection in whichever pane has focus (timeline: `FrameSelection`; viewport: frame the rig/cast),
  Shift+F frames all. The cutscene's old `Home` = frame-whole-timeline moves to Shift+F; Alt+P
  (centre on playhead) stays. W/E/R gizmo modes stay on both viewports.
- **D12 — Text fallbacks stay.** Every icon button carries a fallback word for the day an icon
  leaves Unity's set (the package's standing convention: `SetOverlayToolIcon`,
  `CutsceneCastPanel`'s `--text` modifier). `ToolkitIcons.MakeIconButton` is the one place that
  implements it.

## 3. The design system

### 3.1 Palette — `ToolkitPalette` (C#) mirrored by `--toolkit-color-*` (USS)

`ToolkitPalette` is a `public static class` of `public static readonly Color` fields **plus**
`public static IReadOnlyDictionary<string, Color> Tokens` (token name → colour) that the mirror
test reads. The USS `.clip-editor__root` block declares one `--toolkit-color-<token>: rgb(r, g, b);`
per entry, same order. Values are 0–255 integers in both places (`new Color32` in C#).

State colours (the meaning is fixed; a tab may not reuse one for another meaning):

| Token | rgb | Means |
|---|---|---|
| `accent` | 88, 148, 216 | the active tab's underline, the actor lane strip, hover tint base |
| `selected` | 77, 158, 242 | the selected key / marker / block / row |
| `selected-row` | 61, 96, 133 @ 0.5 | a selected header or list row's ground |
| `playing` | 70, 130, 90 | the play button while running |
| `loop-on` | 150, 128, 52 | the loop button while looping |
| `recording` | 190, 70, 60 | Auto Key while on; the Key button's icon |
| `holding` | 242, 217, 77 | a hold, a holding event's ring, the transport while gated |
| `warning` | 235, 170, 60 | modified-unkeyed, off-grid, quantize |
| `error` | 230, 90, 82 | validation errors, invalid resolves |
| `clean` | 115, 200, 122 | validation clean, bound cast dot |
| `rig-edit` | 226, 138, 44 | Rig Edit mode chrome (unchanged) |
| `box-border` | 255, 255, 255 @ 0.08 | every `.toolkit-box` |
| `box-fill` | 255, 255, 255 @ 0.02 | every `.toolkit-box` |
| `box-header` | 255, 255, 255 @ 0.04 | a box's header strip |

Lane families (cutscene header strips — unchanged values, centralised):

| Token | rgb |
|---|---|
| `lane-actor` | 88, 148, 216 |
| `lane-prop` | 120, 190, 120 |
| `lane-camera` | 90, 190, 190 |
| `lane-events` | 230, 150, 70 |
| `lane-holds` | 230, 200, 80 |

Marker fills (cutscene lanes — today's `new Color(...)` literals, converted):

| Token | rgb | Today's literal |
|---|---|---|
| `marker-root` | 166, 217, 140 | `CutsceneEditorPanel.cs:2233` |
| `marker-mark` | 115, 166, 242 | `:2243` |
| `marker-facing` | 217, 191, 102 | `:2252` |
| `marker-part` | 191, 140, 217 | `:2268` |
| `marker-attach` | 115, 204, 204 | `:2405` |
| `marker-camera` | 140, 179, 242 | `:2436` |
| `marker-cut` | 242, 115, 115 | `:2460` |
| `marker-hold` | 242, 217, 77 | `:2535` |

Event palette — `ToolkitPalette.EventColors` (8 entries, this order) and
`public static Color ColorForEventKey(uint eventKey)`:

| index | name | rgb |
|---|---|---|
| 0 | amber | 235, 170, 60 |
| 1 | coral | 235, 110, 90 |
| 2 | magenta | 215, 110, 190 |
| 3 | violet | 150, 120, 230 |
| 4 | teal | 70, 190, 180 |
| 5 | lime | 160, 205, 80 |
| 6 | pink | 240, 150, 170 |
| 7 | mint | 120, 220, 170 |

Index = `(int)((eventKey * 2654435761u) >> 29)` — Knuth's multiplicative hash, top three bits,
so adjacent registry keys (16, 17, 18 …) scatter rather than march through the table. Amber is
index 0 on purpose: it is today's event colour, so a project with one event looks unchanged. The
window fill behind an event pin is the same colour at alpha 0.30. None of the eight is the
selected blue, the playing green, or the holding yellow.

### 3.2 Icons — `ToolkitIcons`

`public static class ToolkitIcons` with one `Resolve(string iconName)` that asks
`EditorGUIUtility.IconContent` for `"d_" + name` on the dark skin and `name` on the light skin,
falling back to the other, returning null when neither exists. Constants (the bare names; every
one was probed present in Unity 6.5 on 2026-09-07):

| Constant | Name | Used for |
|---|---|---|
| `Play` | `PlayButton` | play |
| `Pause` | `PauseButton` | pause (swapped onto the play button while playing) |
| `Stop` | `StopButton` | stop |
| `JumpToStart` | `Animation.FirstKey` | ⏮ |
| `StepBack` | `Animation.PrevKey` | ◀ one frame |
| `StepForward` | `Animation.NextKey` | ▶ one frame |
| `JumpToEnd` | `Animation.LastKey` | ⏭ |
| `Loop` | `preAudioLoopOff` | loop toggle (lit by class, one glyph) |
| `Record` | `Animation.Record` | Key, Auto Key |
| `AddEvent` | `Animation.AddEvent` | Add Event |
| `EyeOpen` | `animationvisibilitytoggleon` | layer on |
| `EyeClosed` | `animationvisibilitytoggleoff` | layer off |
| `Plus` | `Toolbar Plus` | add row / add slot |
| `Trash` | `TreeEditor.Trash` | delete row |
| `Frame` | `ViewToolZoom` | frame selection / cast |
| `ResetCamera` | `FrameCapture` | reset viewport camera (unchanged) |
| `Link` | `Linked` | bind, Drive Scene View |
| `ShotCamera` | `SceneViewCamera` | cutscene Shot mode |
| `Move` / `Rotate` / `Scale` | `MoveTool` / `RotateTool` / `ScaleTool` | gizmo modes (unchanged) |

Two factories, and nothing else in the package builds an icon button any other way after this
amendment:

```csharp
public static Button MakeIconButton(Action onClick, string iconName, string tooltip, string fallbackText)
public static void SetButtonIcon(Button button, string iconName, string fallbackText)
```

`MakeIconButton` returns a `Button` carrying `toolkit-icon-button`, with a child `Image`
(`toolkit-icon-button__icon`, `pickingMode = Ignore`); when the icon is missing it removes the
image, sets `text = fallbackText` and adds `toolkit-icon-button--text`. `SetButtonIcon` swaps the
image in place (play ↔ pause) and honours the same fallback. Reorder arrows in the layers column
keep the `▲`/`▼` glyphs — Unity has no crisp 16px reorder icon — through
`MakeIconButton(…, null, tooltip, "▲")`, which takes the text path deliberately.

### 3.3 The transport bar

Anatomy, left to right, on every tab that plays (groups a tab lacks are simply absent):

```
[ tab-specific leading groups ] [ |◀  ◀  ▶  ■  ▶  ▶|  ⟳ ] [ readouts ] [ zoom ] [ tab-specific trailing ]
```

- The bar is `toolkit-transport` (today's `.clip-editor__transport` rules verbatim: row,
  `space-evenly`, wraps, never shrinks, 1px bottom rule). Each group is
  `toolkit-transport__group`.
- The core run is `TransportCoreElement` (§3.5), inserted into the group the host names
  `transport-core-slot`.
- A number in the bar is a caption + field pair: `toolkit-transport__caption` (with
  `--draggable` when it is the field's drag handle) and `toolkit-transport__field` (58px). A
  derived readout is `toolkit-transport__derived` (dimmed). The caption-drag helper moves out of
  `ClipEditorTransport.cs` into `Shared/CaptionDragHandle.cs` as
  `public static class CaptionDragHandle { public static void Attach<TValue>(VisualElement caption, TextValueField<TValue> field) }`,
  with its two traps (lifting `isDelayed` on `PointerDownEvent`, restoring on
  `PointerCaptureOutEvent`) intact.
- Core buttons are `toolkit-icon-button` (28×22, 16px icon). The play button adds
  `toolkit-icon-button--playing` while running; loop adds `toolkit-icon-button--lit` while on and
  its icon rides at opacity 0.55 otherwise (today's loop rules, renamed).
- Element names inside the core, kept from the Clip Editor's UXML so nothing that resolves them
  breaks: `jump-start-button`, `step-back-button`, `play-toggle`, `stop-button`,
  `step-forward-button`, `jump-end-button`, `loop-button`, `loop-icon`.
- Tooltips name the shortcut: "Play or pause. Shortcut: Space." etc. — the existing strings in
  `ClipEditorTransport.cs:129-164` are the wording.

### 3.4 Boxes, pane titles, status rows

- **`toolkit-box`** = today's `.clip-editor__component` (6px vertical margin, 1px `box-border`,
  radius 3, `box-fill`). Modifiers: `--selected` (border → `selected`), `--active` (border →
  `accent` at 0.55, the component block's existing meaning). Children: `toolkit-box__header`
  (row, `box-header` ground, 2px/4px padding), `toolkit-box__title` (bold, grows),
  `toolkit-box__body` (6px left / 4px right padding), `toolkit-box__row` (row, `align-items:
  center`, 1px `box-border` top rule between rows, 2px vertical padding, `--selected` ground =
  `selected-row`), `toolkit-box__footer` (row, right-aligned, holds a box's add button).
- **`toolkit-pane-title` / `toolkit-pane-header` / `toolkit-pane-actions` / `toolkit-pane-action`**
  = today's `.clip-editor__pane-*` quartet. Every tab's side pane gets a header: "Clips", "Rig
  Hierarchy", "Clip Inspector" (unchanged); "Cast", "Cutscene Inspector" (new); "Layers",
  "Preview", "Actor Inspector" (new).
- **`toolkit-status-row` / `toolkit-status` / `toolkit-status-actions`** = today's
  `.clip-editor__status-row` trio. The status text sits left and ellipsizes; the right-aligned run
  is `clip-editor__bar-action` members (unchanged class, it is the segmented-strip rule).
- **`toolkit-eye-toggle`** = a `toolkit-icon-button` sized 20×18 whose icon is `EyeOpen` when on
  and `EyeClosed` at opacity 0.45 when off. A plain `Button`, state held by the caller (the layer's
  `defaultActive`), never a `Toggle` — a checkbox input beside an eye would be two controls for one
  fact.

Mapping of existing Clip Editor classes to shared ones (T2 renames; C# constants and the UXML
follow; no visual change intended on the Clip Editor tab):

| Today | Becomes |
|---|---|
| `clip-editor__transport` | `toolkit-transport` |
| `clip-editor__transport-group` | `toolkit-transport__group` |
| `clip-editor__transport-button`, `--play`, `--playing` | `toolkit-icon-button`, `--playing` |
| `clip-editor__transport-loop`, `-loop-icon`, `--on` | `toolkit-icon-button`, `__icon`, `--lit` |
| `clip-editor__transport-label`, `--draggable`, `-field`, `-derived` | `toolkit-transport__caption`, `--draggable`, `__field`, `__derived` |
| `clip-editor__pane-title`, `-header`, `-actions`, `-action` | `toolkit-pane-title`, `-header`, `-actions`, `-action` |
| `clip-editor__status-row`, `__status`, `__status-actions` | `toolkit-status-row`, `toolkit-status`, `toolkit-status-actions` |
| `clip-editor__component`, `--active`, `-header`, `-title`, `-body` | `toolkit-box`, `--active`, `__header`, `__title`, `__body` (the `--intrinsic` modifier and `-badge`/`-remove` stay `clip-editor__component-*`) |
| `cutscene-editor__transport`, `-button`, `-icon`, `-status`, `-status--holding` | `toolkit-transport`, `toolkit-icon-button`, `__icon`, `toolkit-transport__status`, `--holding` |
| `cutscene-editor__time-readout` | deleted (Time becomes a caption + field) |
| `actor-editor__layer-row`, `--selected` | `toolkit-box__header`, `toolkit-box--selected` (on the box) |
| `actor-editor__animation-row`, `--selected` | `toolkit-box__row`, `--selected` |

### 3.5 `ITransportTarget` and `TransportCoreElement`

```csharp
[System.Flags]
public enum TransportCapabilities { None = 0, StepBack = 1, StepForward = 2, Stop = 4, JumpToEnd = 8, Loop = 16 }

public interface ITransportTarget
{
    bool IsPlaying { get; }
    bool IsLooping { get; set; }
    TransportCapabilities Capabilities { get; }
    void TogglePlay();
    void Stop();
    void JumpToStart();
    void JumpToEnd();
    void Step(int frameDelta);   // negative = back; the target decides what a frame is
}
```

`public sealed class TransportCoreElement : VisualElement` — builds the seven buttons through
`ToolkitIcons.MakeIconButton`, `Bind(ITransportTarget target)` wires clicks and hides the buttons
whose capability flag is off, `RefreshState()` swaps play/pause and the lit classes. Hosts call
`RefreshState()` from their own `SetPlaying`/`SetLooping`. Shift on a step click = 10 frames
(`LargeStepFrames` semantics move here as a `public int largeStepFrames = 10` the Clip Editor
sets from its EditorPref).

Per-tab meaning of the interface:

| Target | frame | JumpToStart | JumpToEnd | Stop | Loop |
|---|---|---|---|---|---|
| Clip Editor (`ClipEditorWindow`, explicit impl) | one clip frame (`StepFrames`) | `SetPlayheadTime(0)` | `SetPlayheadTime(1)` | pause + playhead → `prePlayPlayheadTime` (new field, captured in `SetPlaying(true)`) | existing `isLoopEnabled` |
| Cutscene (`CutsceneEditorPanel`) | `1f / 30f` s (`StepSeconds` const) | `SetPlayhead(0)` | `SetPlayhead(ComputeContentEndSecondsSafe())` | existing `StopPlayback` | existing `loopPlaybackToggle` value → a bool field |
| Actor Editor (`ActorEditorPanel`) | one `composer.Tick(1f / 30f)` while paused | `composer.Reset()` (+ facing reset, today's `OnResetButtonClicked`) | not supported | pause + `JumpToStart` | not supported |

### 3.6 Keyboard map (every tab that plays)

| Key | Action | Today |
|---|---|---|
| Space | `TogglePlay` on the active tab's target | Clip Editor only, and fires on every tab |
| ← / → | `Step(∓1)`; Shift = ±10 | Clip Editor only |
| Home / End | `JumpToStart` / `JumpToEnd` | Clip Editor; cutscene `Home` framed the timeline |
| F / Shift+F | frame selection / frame all, in the focused pane | Clip Editor timeline + both viewports; cutscene timeline had `Home` |
| Alt+P | centre timeline on playhead | cutscene only (stays) |
| W / E / R | gizmo mode, in a viewport | both (stays) |
| Ctrl+Z / Y | undo / redo | root handler (stays) |
| Ctrl+C / V / D, Delete | clip keys or cutscene items | per tab (stays) |

Routing: `ClipEditorWindow.OnTransportKeyDown` resolves `ITransportTarget activeTransportTarget =
ResolveActiveTransportTarget()` (`ClipEditor` → `this`; `CutsceneEditor` → `cutscenePanel`;
`ActorEditor` → `actorEditorPanel`; else null → transport keys fall through). The Clip-Editor-only
cases in that switch (`HandleTransformKeyDown`, F/A/C/V/D key editing) are gated on
`activeTab == ClipEditorTab.ClipEditor`. **Do not add a second `KeyDownEvent` registration on
`rootVisualElement`** — `CreateGUI` re-runs after a domain reload and the root's callbacks
survive `Clear()` (`AnimationToolkit.md`, "The Clip Editor goes dead after any recompile").

## 4. Per-tab layout

### 4.1 Clip Editor (leads; minimal change)

Unchanged structure. Visible changes: the five transport words/glyphs (`|<`, `<`, `Play`, `>`,
`>|`) become the icon run with a new ■ Stop; the `Add Event` status-row button gets the
`AddEvent` icon beside its word (it stays a word: the picker it opens is the affordance);
`Auto Key` lights `recording` when on; event lanes colour per name (§4.4); selected event pins keep
their fill and take the blue stroke. The top bar and tab strip are untouched (R3).

### 4.2 Cutscene Editor (restructure)

```
┌ tab-local top bar ─────────────────────────────────────────────────────────────┐
│ Cutscene [ObjectField]  [New]   scene status … [scene action]                  │
├ upper (TwoPaneSplitView, unchanged proportions) ───────────────────────────────┤
│ Cast pane            │ viewport (rail on left edge)     │ Cutscene Inspector    │
│ title + [+Actor][+Prop][Sync]                          │                       │
├ timeline pane ─────────────────────────────────────────────────────────────────┤
│ toolkit-transport: [core] [Time ▸ field  / end s] [Speed ▸ field]              │
│                    [Zoom slider][All][Playhead] [Continue] [hold status]        │
│ toolkit-status-row: status text …          [● Key][Auto Key][Skip Holds]        │
│ header column | lanes (unchanged)                                              │
└────────────────────────────────────────────────────────────────────────────────┘
```

- **Top bar** (`BuildToolbar`, `CutsceneEditorPanel.cs:368-458`): keeps the Cutscene field, New,
  scene status and scene action. Loses Zoom, Key, Auto Key, Drive Scene View. Styled with the
  window's own classes: the label is `clip-editor__toolbar-label`, the field
  `clip-editor__object-field`, New is a `ToolbarButton` with `clip-editor__bar-action`, the row
  itself is a `uie:Toolbar`-styled strip (`clip-editor__toolbar`).
- **Transport** (`BuildTransportRow`, `:475-536`) moves from `Add(BuildTransportRow())` at `:159`
  to the head of `timelineArea` (`:162-168`), replacing the add-slot row. Groups: core; Time
  (caption + `FloatField`, draggable via `CaptionDragHandle.Attach`, plus a derived
  `"/ 12.40 s"` label) and Speed (caption + field, replacing the labelled `FloatField("Speed")`);
  Zoom (the slider from the top bar, caption "Zoom", plus `All` = `FrameWholeTimeline` and
  `Playhead` = `CentreTimelineOnPlayhead`); Continue + hold status (unchanged behaviour,
  `toolkit-transport__status` / `--holding`). `Loop` and `Skip Holds` stop being labelled
  `Toggle`s: Loop is the core's ⟳; Skip Holds moves to the status row.
- **Status row** (new, directly under the transport, mirrors the Clip Editor's
  `timeline-status-row`): left `toolkit-status` label carrying what `ReportTransportAction`
  and the empty-timeline hint say today (`:1915` builds a hint into the lanes — leave that; the
  status label reports counts and the last action); right run: **Key** (`MakeIconButton` with
  `Record` + text "Key" — keep the word, the icon is red and the word says what is keyed),
  **Auto Key** (`ToolbarToggle`, `clip-editor__bar-action`, lit `recording` when on — from
  `CutsceneEditorPanel.AutoKey.cs:27-42`), **Skip Holds** (`ToolbarToggle`, `bar-action`).
- **Cast pane**: `CutsceneCastPanel.cs:58-79` header becomes `toolkit-pane-header` with
  `toolkit-pane-title` "Cast", the stage status label, and a `toolkit-pane-actions` run of
  `+ Actor` / `+ Prop` (icon `Plus` + word, the pair is a new `event Action<CutsceneSlotKind>
  AddSlotRequested` the panel subscribes to `AddSlot`) and `Sync to Stage` (word; it is a
  write). `BuildAddSlotRow` (`:904-920`) is deleted. Cast rows become `toolkit-box` (keeping the
  3px kind accent as `border-left-color`; `--selected` on the box).
- **Viewport rail**: the top-right `cutscene-editor__viewport-controls` strip (`:956-981`) becomes
  a left-edge vertical rail identical to the Clip Editor's `clip-editor__overlay-column`
  (reuse the class and `clip-editor__overlay-tool-button`): Move / Rotate / Scale toggles bound to
  `SetViewportGizmoMode` (check `CutsceneEditorPanel.ViewportGizmo.cs` for the mode field and
  any existing buttons — reuse, do not duplicate state), run break, Frame (`Frame` icon), run
  break, Shot (`ShotCamera`, a toggle), Drive Scene View (`Link`, a toggle; from the old top bar).
- **Inspector**: `inspectorScroll` (`:182-186`) gets a `toolkit-pane-title` "Cutscene Inspector"
  above it inside a `clip-editor__pane`.
- **Selection colour**: `.cutscene-editor__clip-block--selected` and
  `.cutscene-editor__moment-marker--selected` borders → `var(--toolkit-color-selected)`.

### 4.3 Actor Editor (restructure)

```
┌ header row ───────────────────────────────────────────────────────────────────┐
│ Profile [ObjectField]  [validation badge]                                     │
├ body (row) ───────────────────────────────────────────────────────────────────┤
│ Layers pane          │ Preview pane                      │ Actor Inspector     │
│ title  [+ Layer]     │ title                             │ title               │
│ ┌ box: 👁 ● Base … ┐ │ status line                       │ fields …            │
│ │  ● Idle ▶ ■ 0.00 │ │ viewport image                    │                     │
│ │  ● Blink ▶ ■ … │ │                                   │                     │
│ │            [+]   │ │ toolkit-transport:                │                     │
│ └──────────────────┘ │ [|◀ ▶ ■ ▶] [Direction ▸ slider … ] │                     │
│ ┌ box: 👁 ● Override┐│                                   │                     │
└───────────────────────────────────────────────────────────────────────────────┘
```

- **Header** (`ActorEditorPanel.cs:207-228`): Profile field + badge only. `BuildTransportRow`
  (`:230-265`) is rebuilt as a `toolkit-transport` and appended to the **bottom of the viewport
  column** (after `viewportImage`, `:294-297`) — the middle-bar position on a tab with no
  timeline. Groups: core (`Capabilities = StepForward | Stop`), Direction (caption + slider +
  readout). The Reset button is gone (it is ■ / |◀).
- **Panes**: `layersColumn`, `viewportColumn`, `inspectorColumn` (`:273-312`) each get a
  `toolkit-pane-header` with title; the Layers header carries `+ Layer` (`Plus` icon + word) in
  its `toolkit-pane-actions`, replacing the `+ Layer` button at the foot of the scroll
  (`ActorEditorLayersColumn.cs:146-149` — expose `AddLayer` is already public; the column raises
  nothing new, the panel calls it).
- **Layers column** (`ActorEditorLayersColumn.cs:152-263`): each layer is a `toolkit-box`. Header
  row (`toolkit-box__header`): eye (`toolkit-eye-toggle`, `defaultActive`, tooltip "Start with
  this layer active. The preview re-seeds when you change it."), live dot, name
  (`toolkit-box__title`), starter button, then for non-bookends `▲` `▼` (text-path icon
  buttons, 18px) and delete (`Trash`). Body: one `toolkit-box__row` per animation — live dot,
  name, ▶ (`Play`), ■ (`Stop`), scrub field (`toolkit-transport__field`). Footer:
  `+ Animation` as a `Plus` icon button with tooltip, right-aligned. Selection: the box takes
  `toolkit-box--selected`; an animation row takes `toolkit-box__row--selected`. The
  `defaultActive` `Toggle` and the `▶`/`■`/`×` text buttons are removed. Names for tests: the
  header row keeps `name = "actor-editor-layer-row-" + layerIndex`; add
  `name = "actor-editor-animation-row-" + layerIndex + "-" + animationIndex` on rows and
  `name = "actor-editor-layer-eye-" + layerIndex` on the eye.

### 4.4 Event colours in both timelines

- **Clip Editor**: `TrackLaneElement` gains `public Color eventColor` (default = palette amber).
  `OnGenerateVisualContent` (`TrackLaneElement.cs:287`) uses it instead of `EventKeyFill`, and
  `DrawEventWindows` (`:355`) uses it at alpha 0.30 instead of `EventWindowFill`; both statics are
  deleted. `ClipEditorWindow.AddTrackRow` (`ClipEditorWindow.cs:4853`) does not know the key, so
  the event loop at `:4813-4825` sets it after the row is added: extend `AddTrackRow` with an
  optional `Color? laneAccent = null` that (a) sets `lane.eventColor` and (b) paints the header
  label's left edge (`style.borderLeftWidth = 3; style.borderLeftColor = accent` — a data-driven
  colour, so inline is correct here per the USS header's own rule). Selected event pin: fill stays,
  `strokeColor = Selected`, `lineWidth = 2` (`:288-290`).
- **Cutscene Editor**: `CutsceneMomentLaneElement.SetTimes` gains a fourth overload parameter
  `IReadOnlyList<Color> markerColors` (null = `markerColor` for all); `Rebuild` (`:140`) picks
  per index. `BuildEventRows` (`CutsceneEditorPanel.cs:2471-2501`) passes
  `ToolkitPalette.ColorForEventKey(cutscene.events[i].eventKey)` per event. The Events header
  strip stays `lane-events` orange — the strip says "this row is events", the marker says which.
  Every other `markerColor = new Color(...)` in `Build*Rows` becomes the matching
  `ToolkitPalette.Marker*` field.

## 5. Not decided here / out of scope

- The standalone `VatBakeWindow` (no window stylesheet) and the VAT Bake / New Rig tabs' forms —
  they get pane titles via T11 only where a list exists (New Rig candidates).
- Light-skin colours. `ToolkitIcons.Resolve` handles icons; palette values are dark-skin. Same as
  today.
- A per-event colour override on the registry. D6 says hash; if the owner wants to pick colours
  by hand later, it is a `Color` on `AnimEventKeyRegistry` rows and a one-line change in
  `ColorForEventKey`.
- Sound on scrub, and anything else in `HANDOFF.md` §5's "not on the queue".

## 6. Tasks

Every task: obey A69 (one `<summary>` per file, on the primary type; no `<remarks>`; **no
"amendment A72", "§", "Phase" in any comment** — `Conformance_F` scans and fails). Never `var`,
never single-letter names. UI Toolkit only. Report back ≤ 30 lines: files touched, names added,
anything the spec got wrong. **Subagents edit files only; the orchestrator runs the gate after
each task** (`refresh_unity` → `read_console` for `error CS` → touched fixtures by name).

Ordering: T0 → T1, T2 (parallel-safe) → T3 → T4 → T5 → T6 → T7 → T8, T9, T10, T11 (T8/T10/T11
parallel-safe with each other and with T9) → T12.

### T0 — Baseline captures [orchestrator]
Before any edit: capture the Clip Editor, Cutscene Editor and Actor Editor tabs pixel-exactly
with the `GUIView.GrabPixels` recipe in `AnimationToolkit.md` ("A session CAN see the editor
UI"), one PNG per tab, to `Library/A72Captures/before-<tab>.png`, and send them to the owner
with `SendUserFile`. Open `Assets/ScriptableObjects/Animations/MaleCitizen.profile.asset` for the
Actor tab and the G3 acceptance cutscene for the Cutscene tab so the captures show real rows.

### T1 — Palette and icons [parallel-safe]
Files: **new** `Editor/ClipEditor/Shared/ToolkitPalette.cs`, `Shared/ToolkitIcons.cs`,
**new** `Tests/EditMode/ToolkitPaletteTests.cs`.
- `ToolkitPalette` per §3.1: fields, `Tokens`, `EventColors`, `ColorForEventKey`.
- `ToolkitIcons` per §3.2: constants, `Resolve`, `MakeIconButton`, `SetButtonIcon`. Read
  `ClipEditorWindow.cs:2700-2750` (`SetOverlayToolIcon` / `ResolveOverlayToolIconTexture`) and
  `CutsceneCastPanel.cs:270-300` (its `--text` fallback) for the two fallback shapes being
  unified; do not edit those files.
- Test `UssTokens_MatchToolkitPalette`: read `ClipEditorWindow.uss` as text, regex
  `--toolkit-color-([a-z-]+):\s*rgba?\(([^)]*)\)`, assert every `Tokens` entry is present with
  equal channels (alpha to 2 decimals) and nothing extra. It will fail until T2 lands — that is
  the point; the orchestrator runs it after T2. (Revert-to-fail: edit one USS value.)
- Test `ColorForEventKey_IsStableAndUsesEveryPaletteEntry`: keys 16..1015 hit all 8 indices and
  the same key twice gives the same colour.

### T2 — Shared stylesheet section and rename sweep [parallel-safe with T1]
Files: `Editor/ClipEditor/ClipEditorWindow.uss`, `ClipEditorWindow.uxml`,
`Tests/EditMode/ClipEditorLayoutTests.cs`; C# constant renames in `ClipEditorWindow.cs`,
`ClipEditorWindow.ComponentStack.cs`, `ClipEditorTransport.cs` (grep each for the old class
literals — `rg "clip-editor__(transport|pane-|status|component)" Packages/com.dotsanimationtoolkit/Editor`).
- Add the `--toolkit-color-*` tokens to `.clip-editor__root` (§3.1, all four tables).
- Add a `Shared` section (after the root tokens, before Toolbar) defining every `toolkit-*` class
  in §3.3/§3.4, **by moving** the existing rules named in the mapping table and renaming their
  selectors; existing comments travel with them. Colours inside those rules become `var(--toolkit-color-…)`.
- Apply the renames in the UXML and in every C# `AddToClassList` / const string. The
  `cutscene-editor__*` and `actor-editor__*` rows of the table are *deleted from the sheet* here;
  their C# users are rewritten in T6/T10 (the orchestrator gates T2 knowing those two panels
  lose styling until then — acceptable, the tabs still function).
- `ClipEditorLayoutTests.RequiredElementNames`: replace the five transport buttons + loop names
  with `transport-core-slot`; keep `transport-bar` and the captions/fields.
- UXML: the `PlayHead` group (`ClipEditorWindow.uxml:97-106`) becomes
  `<ui:VisualElement name="transport-core-slot" class="toolkit-transport__group"/>`; the
  status-row's four inline `style="border-top-width…"` attributes (`:127-129`) move into a
  `clip-editor__status-action` class rule.

### T3 — Transport interface and core element
Files: **new** `Shared/ITransportTarget.cs`, `Shared/TransportCoreElement.cs`,
`Shared/CaptionDragHandle.cs`. Depends on T1 (icons) and T2 (classes).
- Per §3.5 and §3.3. `CaptionDragHandle.Attach` is `ClipEditorTransport.cs:102-125` moved
  verbatim (keep the two trap comments, drop the `<summary>`'s `<see cref>` if it cites anything
  banned). Leave the original in place; T4 deletes it.
- No test (pure wiring; zero fixtures per HANDOFF §2).

### T4 — Clip Editor adopts the core
Files: `ClipEditorTransport.cs` (whole file, 769 lines), `ClipEditorWindow.cs:4445-4450`
(`SetPlaying`) and `:248` (fields). Depends on T3.
- `ClipEditorWindow : … , ITransportTarget` (explicit implementation, §3.5 row 1;
  `prePlayPlayheadTime` captured in `SetPlaying(true)`).
- `BindTransportBar`: instantiate `TransportCoreElement`, add to `transport-core-slot`, `Bind(this)`,
  `largeStepFrames = LargeStepFrames`. Delete `playButton`/`jumpStart…`/`loopButton` fields and
  their binding blocks (`:129-164`, `:261-286`), `RefreshPlayButtonState` (`:411-419`) and
  `RefreshLoopButtonState` (`:433-440`) become calls to `transportCore.RefreshState()`;
  `MakeCaptionDragHandle` (`:102-125`) → `CaptionDragHandle.Attach`.
- `add-event-button` gets the `AddEvent` icon via `ToolkitIcons.SetButtonIcon(addEventButton,
  ToolkitIcons.AddEvent, "Add Event")` **keeping** its text (icon + word); Auto Key toggle adds
  `toolkit-bar-action--recording` when checked (define the rule in T2's shared section:
  `.clip-editor__bar-action.toolkit-bar-action--recording { background-color: var(--toolkit-color-recording); }`
  — add it now if T2 missed it).
- Fixture: `ClipEditorLayoutTests` (already updated by T2) must pass; nothing new.

### T5 — Key routing by active tab
File: `ClipEditorTransport.cs:595-737` (`RegisterTransportShortcuts`, `OnTransportKeyDown`),
`ClipEditorWindow.cs` (one new method `ResolveActiveTransportTarget`, near `SetActiveTab`
`:1607`). Depends on T4.
- Per §3.6. Space/←/→/Home/End go through the resolved target and `StopPropagation()` when a
  target exists. `HandleTransformKeyDown`, F/KeypadPeriod/A/C/V/D cases run only when
  `activeTab == ClipEditorTab.ClipEditor`.
- `OnTimelineKeyDown` (`ClipEditorWindow.cs:5636-5700`) duplicates Space/Home/End/arrows for the
  focused lane stack — route those five through the same target call so the two handlers cannot
  disagree; leave Delete/C/V/D there.

### T6 — Cutscene Editor restructure
Files: `Cutscene/CutsceneEditorPanel.cs` — read **only** `:60-218` (fields, ctor), `:319-640`
(layout, transport), `:842-925` (status, add-slot row), `:926-1005` (viewport), `:2888-2960`
(keys); `Cutscene/CutsceneEditorPanel.AutoKey.cs:15-42`; `Cutscene/CutsceneCastPanel.cs:28-85`;
`Cutscene/CutsceneEditorPanel.ViewportGizmo.cs` (grep `GizmoMode` for the mode field). Depends on
T3, T5. The file is 4500 lines: **do not read it whole**.
- Per §4.2, in this order: (1) `CutsceneEditorPanel : VisualElement, ITransportTarget` (§3.5 row
  2; `IsLooping` backs a bool that replaces `loopPlaybackToggle.value` reads in `Tick`); (2)
  rebuild `BuildTransportRow` on `TransportCoreElement` + groups, move its `Add` into
  `timelineArea`; (3) new `BuildStatusRow` with Key / Auto Key / Skip Holds; (4) strip
  `BuildToolbar` to identity + scene status and restyle with the window's toolbar classes; (5)
  `AddSlotRequested` on the cast panel, pane header with title + actions, delete
  `BuildAddSlotRow`; (6) viewport rail; (7) inspector pane title; (8) keys: `Home`/`End` →
  jump, `F`/`Shift+F` → `FrameSelection` (add it: centre the selection's earliest time, or
  `FrameWholeTimeline` when nothing is selected) / `FrameWholeTimeline`.
- Traps: `OnEditorTick` and `RebuildAll` stay wired exactly as they are (the panel has its own
  deferred-rebuild pair); `RestoreSessionCutscene` stays last in the ctor; `SetPlaying` must call
  `transportCore.RefreshState()` where it swaps the icon today (`:601-605`).
- Fixture: none new. `ClipEditorAddEventTests` and `PackagingConformanceTests` must still pass.

### T7 — Cutscene lane colours and per-event markers
Files: `Cutscene/CutsceneMomentLaneElement.cs:76-173`, `Cutscene/CutsceneEditorPanel.cs:2186-2550`
(the `Build*Rows` methods only), USS `.cutscene-editor__moment-marker--selected` and
`.cutscene-editor__clip-block--selected`. Depends on T1; run after T6 (same file).
- Per §4.4 second bullet and D5. The `markerColor` literals → `ToolkitPalette.Marker*`; the
  selected borders → `var(--toolkit-color-selected)`.
- No test.

### T8 — Clip Editor per-event lane colours [parallel-safe]
Files: `ClipEditor/TrackLaneElement.cs:13-21, 282-313, 338-380`, `ClipEditorWindow.cs:4805-4826`
and `AddTrackRow` signature at `:4853`. Depends on T1.
- Per §4.4 first bullet and D5.
- No test (a paint path; the mirror test already covers the palette).

### T9 — Actor Editor restructure
Files: `ActorEditor/ActorEditorPanel.cs:30-70, 203-360, 446-479`, `Tests/EditMode/ActorEditorPanelTests.cs:33-60`.
Depends on T3, T5.
- Per §4.3 first two bullets and §3.5 row 3. `ActorEditorPanel : VisualElement, ITransportTarget`.
  The transport row keeps `name = "actor-editor-transport-row"`; the three column names stay
  (`Panel_ExposesThreeNamedColumnsAndTheProfileField` must pass unchanged).
- Tick (`:446-479`): `Step(+1)` while paused = `composer.Tick(1f / 30f, previewController)` then
  `RenderViewport()`.

### T10 — Layers column boxes and eye [parallel-safe with T9]
Files: `ActorEditor/ActorEditorLayersColumn.cs` (whole file, 494 lines),
`Tests/EditMode/ActorEditorPanelTests.cs:62-86` (`LayersColumn_ListsOneRowPerLayerAndPerAnimation`
queries by class `actor-editor__layer-row` / `actor-editor__animation-row` — switch it to the
`name` prefixes in §4.3). Depends on T1, T2.
- Per §4.3 third bullet and D7. `SetLayerDefaultActive` is the eye's write; the fingerprint
  already covers `defaultActive` so the eye repaints after undo.
- Test: extend the existing fixture with one assertion — clicking the eye (send a
  `ClickEvent`/call the bound action) flips `layer.defaultActive` and is undoable
  (`Undo.PerformUndo` restores). Revert-to-fail: make the eye not write.

### T11 — Box the remaining hand-built lists [parallel-safe]
Files: `Cutscene/CutsceneCastPanel.cs:131-260` (`BuildRow`), `ActorEditor/DirectionSetClipQueueView.cs:73-145`,
`Authoring/NewRigPanel.cs` (the candidate row builder around `:190-230`; grep `ToggleControl =`).
Depends on T2.
- Each row → `toolkit-box` with a `toolkit-box__header` line; delete the inline
  `backgroundColor`/padding literals they use today (D10). Cast rows keep their kind accent as an
  inline `borderLeftColor` (data-driven). The `×` clear button in the direction view → `Trash`
  icon button; `Open` stays a word.
- No test.

### T12 — Gate, captures, docs, memory [orchestrator]
- Full gate once: EditMode `DotsAnimationToolkit.Tests.EditMode` (expect ≥ 823 + 3 new) and
  PlayMode (≥ 291). Counts must not drop.
- AFTER captures of the three tabs to `Library/A72Captures/after-<tab>.png`; `SendUserFile` all
  six. **The owner's eye closes this amendment** — never report the visuals as verified.
- Docs: `rg -il "Play\b|Continue ▶|Auto Key|Reset button|\+ Layer" Packages/com.dotsanimationtoolkit/Documentation~`
  and fix every sentence that names a control by its old word; `CHANGELOG.md` `## [0.18.0]`
  entry; `HANDOFF.md` §4 queue line; `Assets/_Vault/Memories/Code/AnimationToolkit.md` gets a
  short "Shared editor chrome (ToolkitPalette / ToolkitIcons / TransportCoreElement)" section
  naming the three traps: the mirror test, the one-root-KeyDown rule, and "inline styles are
  layout only".

## 7. Checkpoints

| After | Gate | Owner sees |
|---|---|---|
| T2 | compile + `ClipEditorLayoutTests` | nothing (Clip Editor unchanged on screen) |
| T5 | compile + `ClipEditorLayoutTests`; Space/arrows on each tab by hand in `execute_code` is not possible — the orchestrator sends synthetic `KeyDownEvent`s at the root via `execute_code` and checks `IsPlaying` flips on the cutscene tab | — |
| T6 | compile + `ClipEditorAddEventTests` + `PackagingConformanceTests` | ⏸ capture of the Cutscene tab |
| T10 | compile + `ActorEditorPanelTests` | ⏸ capture of the Actor tab |
| T12 | full suites | ⏸ all six captures; the owner judges cohesion and the eye/box reading |
