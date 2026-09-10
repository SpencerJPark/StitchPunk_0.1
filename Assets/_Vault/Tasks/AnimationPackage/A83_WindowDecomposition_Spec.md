# Amendment A83 — Decompose `ClipEditorWindow.cs` into pane elements

> **Status:** 📝 specced 2026-09-10, not built. Takes `0.30.0`.
> **Roadmap:** [`AnimationPackage_Roadmap.md`](AnimationPackage_Roadmap.md) Phase 0, second.
> **Predecessors:** A82 (the shared column and split view, so the extracted panes do not carry
> raw split views). `ActorEditorPanel` hosting `ActorEditorLayersColumn` / `ActorEditorProfilesColumn`
> is the shape being copied.
> **Executor:** one orchestrator; **one worker per extraction, sequential**, four extractions, each
> gated before the next starts. This is the only spec in the roadmap that forbids a parallel wave —
> every task edits the same file.

---

## 0. Session prompt (paste into a fresh session)

You are running **Amendment A83** on the DOTS Animation Toolkit package (head `0.29.0` after A82).
Spec: `Assets/_Vault/Tasks/AnimationPackage/A83_WindowDecomposition_Spec.md`. Read it, then the
roadmap §3 protocol, then only what §3 here names. This spec produces **no behaviour change**: it
moves code out of a 9,300-line file into four pane elements. You are the orchestrator; do T0 and
T1 yourself, then run T2, T3, T4, T5 **one at a time**, each as one `worker` briefed with the exact
line ranges T1 produced, each followed by a compile gate and `ClipEditorLayoutTests`. Expect two
sessions; stop cleanly after any gated extraction and record where you are in §7.

---

## 1. Goal

`ClipEditorWindow.cs` is 9,319 lines plus 1,531 in `ClipEditorWindow.ComponentStack.cs`. No
session or subagent can read it; every window task since A56 has been briefed with grep-derived
line ranges. A80's two waves existed only because two tasks touched this file. After A83 the window
keeps the tab strip, the shared selection, session state, the transport target and the validation
badge; the four dock panes are elements:

| Element | UXML pane | Owns today (grep anchors) |
|---|---|---|
| `ClipListPane` | `clip-list-pane` | clip `ListView`, Clip Set field, clip create/rename/delete |
| `RigHierarchyPane` | `hierarchy-pane` | hierarchy `TreeView`, Rig field, `CountTracksForTarget`, drag-reparent |
| `ClipInspectorPane` | `inspector-pane` | key/event/track inspectors (`BuildEventInspector`, `KeyAddress` handling) |
| `TimelinePane` | `timeline-pane` | `TimeRulerElement`, `TrackLaneElement` rows, `PlayheadElement`, `EventLaneAddressing` callers, box select |

Each element is constructed with the same three references: `ActiveAssetSelection`,
`ClipPreviewController`, and a new `ClipEditorSession` value object (the selected clip, selected
key address, playhead) so panes talk through it instead of through window fields.

---

## 2. Decisions (recorded — do not re-ask)

- **A83-D1 — Characterise first, move second.** T1 captures every tab and runs the full EditMode
  suite before a line moves; the same captures and suite are the acceptance after each extraction.
  `ClipEditorLayoutTests` stays green throughout **unchanged** — the UXML does not change, only who
  queries it.
- **A83-D2 — One extraction per worker, one gate per extraction, in the table's order.** Clip list
  first because it has the fewest cross-references; timeline last because it has the most.
- **A83-D3 — The pane element queries its own UXML subtree.** The window passes the pane's root
  `VisualElement` (`rootVisualElement.Q("clip-list-pane")`) into the element's `Bind(VisualElement
  paneRoot, ...)`; the element does its own `Q<>` calls. No UXML edits.
- **A83-D4 — Cross-pane calls go through `ClipEditorSession` events**, never through a pane holding
  another pane. Where today's code calls a window method from what becomes another pane, the method
  becomes a session event (`SelectedClipChanged`, `SelectedKeyChanged`, `PlayheadChanged`,
  `RebuildRequested`) or stays on the window if it touches the transport or the badge.
- **A83-D5 — The `ComponentStack`, `CameraNavigation` and `RagdollHandles` partials stay where they
  are.** They are already partial files; A99 moves the ragdoll one.
- **A83-D6 — Nothing is renamed.** Methods keep their names when they move so grep still finds
  them and the vault's line-range notes only need a file, not a new name.
- **A83-D7 — The window's remaining size target is under 2,500 lines.** If an extraction leaves it
  above that, the next session's T0 decides what else to lift; do not lift it in the same task.

---

## 3. Read first

- `Editor/ClipEditor/ClipEditorWindow.cs` — **by grep only.** T1 produces the range map; workers
  read only their pane's ranges.
- `Editor/ClipEditor/ActorEditor/ActorEditorPanel.cs` lines 1–120 (how a host hands columns their
  roots and the composer) and `ActorEditorLayersColumn.cs` lines 1–60 (an element's `Bind`).
- `Editor/ClipEditor/Shared/ActiveAssetSelection.cs` in full (short).
- `Tests/EditMode/ClipEditorLayoutTests.cs` in full — the invariant.
- Vault `AnimationToolkit.md`: "Never rebuild a pane from a value-changed callback", "The header
  column and the lane column agree by height, not by index", "The Clip Editor goes dead after any
  recompile while it's open".

---

## 4. Design

### 4.1 `Editor/ClipEditor/ClipEditorSession.cs` (T1, orchestrator)

```csharp
public sealed class ClipEditorSession
{
    public ClipAsset SelectedClip { get; }
    public KeyAddress SelectedKey { get; }
    public float PlayheadNormalized { get; }
    public event Action<ClipAsset> SelectedClipChanged;
    public event Action<KeyAddress> SelectedKeyChanged;
    public event Action<float> PlayheadChanged;
    public event Action RebuildRequested;   // "something structural changed; panes re-query"
    public void SetSelectedClip(ClipAsset clip);
    public void SetSelectedKey(KeyAddress address);
    public void SetPlayhead(float normalizedTime);
    public void RequestRebuild();
}
```

Plain class, no `UnityEngine.Object`; the window owns one and passes it to every pane.

### 4.2 Pane element shape (T2–T5)

```csharp
public sealed class ClipListPane : VisualElement, IDisposable
{
    public void Bind(VisualElement paneRoot, ActiveAssetSelection selection,
        ClipEditorSession session, ClipPreviewController preview);
    public void Dispose();   // unregisters every callback it registered
}
```

The pane registers on `selection.ClipSetChanged` and the session events in `Bind`, and unregisters
in `Dispose`. The window calls `Dispose` on every pane in `OnDisable`, which also fixes the "goes
dead after recompile" trap's cousin: stale delegates on a destroyed window.

### 4.3 The range map (T1 output, pasted into §7)

For each pane: every method that becomes part of it, with start–end lines, and every window field
it reads, marked **moves** / **becomes session state** / **stays on window**. This is the brief for
T2–T5; a worker reads only its rows.

---

## 5. Tasks

- [ ] **T0 — Baseline (orchestrator).** Gate; totals in §7. `wc -l` on the window and its
  partials. Capture every tab to `Library/A83Captures/before_*.png`.
- [ ] **T1 — Range map + `ClipEditorSession` (orchestrator).** Grep the window for every method
  and field; classify by the §1 table; write §4.3 into §7. Write `ClipEditorSession.cs` by hand.
  Gate. Commit `A83-T1`.
- [ ] **T2 — `ClipListPane` (one worker, sequential).** Files: new
  `Editor/ClipEditor/Panes/ClipListPane.cs`, `Editor/ClipEditor/ClipEditorWindow.cs` (only the
  ranges T1 lists for this pane, plus the construction site in `CreateGUI`). Move, do not rewrite.
  Gate + `ClipEditorLayoutTests` + `ClipEditorAuthoringTests`. Commit `A83-T2`.
- [ ] **T3 — `RigHierarchyPane` (one worker).** Same shape; ranges from T1; carries
  `CountTracksForTarget` unchanged (A84 fixes its matching). Gate +
  `ClipEditorHierarchySelectionTests`. Commit `A83-T3`.
- [ ] **T4 — `ClipInspectorPane` (one worker).** Ranges from T1, including the two
  `(KeyAddress address, EventMarker marker, AnimEventKeyRegistry registry)` builders. Gate +
  `ClipEditorAddEventTests`. Commit `A83-T4`.
- [ ] **T5 — `TimelinePane` (one worker, possibly two if the range exceeds ~1,500 lines — then the
  lane rows and the ruler/playhead are split into two sequential workers).** Gate +
  `ClipEditorLayoutTests` + `ClipKeyClipboardTests`. Commit `A83-T5`.
- [ ] **T6 — Verification (orchestrator).** Full suites; totals must equal T0's. Captures
  `after_*.png`; compare with `before_*` — identical is the acceptance. `wc -l` the window; record
  against D7.
- [ ] **T7 — Docs (orchestrator or worker).** `CHANGELOG.md` `## [0.30.0]` (one paragraph: no
  user-visible change; four pane elements). Vault note: replace every "grep the member, read forty
  lines" instruction that names the window with the pane file. `package.json`.
- [ ] **T8 — ⏸ owner checkpoint.** Message: "Nothing should look different. Open the Clip Editor,
  pick a clip, scrub, add an event, add a key, drag a hierarchy row. If any of that misbehaves,
  that is this amendment. Line counts before/after are in the spec's §7."

---

## 6. Deliberately out of scope

- Any behaviour change, any rename, any UXML change.
- The `ComponentStack` / `CameraNavigation` / `RagdollHandles` partials (D5).
- The cutscene panel (5,175 lines) — a later amendment with the same shape if the owner wants it.

## 7. Build log

_(empty — T0 totals and line counts, the T1 range map, per-extraction gate results, D7 outcome)_
