# Editor Visual Unification — one look across the toolkit's tabs

> **Status:** ✅ spec written 2026-09-07, nothing built; execution waits for the owner's prompt.
> **Spec:** [`Amendment_A72_EditorVisualUnification_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A72_EditorVisualUnification_Spec.md).
> **Executor:** one Editor-connected orchestrator running the gate, small Sonnet/Haiku subagents
> doing the file edits (they never touch MCP). Task order and parallel-safety are in the spec's §6.

## What it delivers

- The **Clip Editor's layout leads**: the Cutscene and Actor tabs get its shape — asset identity
  on top, the transport as the middle strip on the timeline (or under the preview), a status row
  over the key area, titled panes with actions pushed right.
- **One transport** (`TransportCoreElement`, icon buttons in the cutscene's style) behind one
  interface (`ITransportTarget`); Space / arrows / Home / End reach whichever tab is showing.
  Today the window's Space handler drives the hidden Clip Editor on every tab.
- **Symbols over words** everywhere except the top tab strip, which stays as it is.
- **Boxed peers**: actor layers (with an eye toggle for `defaultActive`), cast rows, direction
  slots, New Rig candidates.
- **One palette** (`ToolkitPalette` ↔ `--toolkit-color-*`, guarded by a mirror test) and a
  **colour per event name**, identical in both timelines.

## Owner calls recorded in the spec (§2) — do not re-ask

Selected is blue everywhere (yellow stays "holding"); the eye writes `defaultActive`; event
colours are hashed from the key, not authored; Unity's `ListView`/`TreeView` are not boxed;
Stop returns the playhead to where Play was pressed.
