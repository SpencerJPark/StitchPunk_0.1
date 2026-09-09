# Rigs tab — a rig catalog, an editable target list, and the preview beside them

> **Status:** ✅ built 2026-09-08, shipped as 0.23.0. One ⏸ owner checkpoint open.
> **Spec:** [`Amendment_A76_RigsTab_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A76_RigsTab_Spec.md).
> **Session prompt:** [`Amendment_A76_RigsTab_Prompt.md`](../../../../Docs/AnimationToolkit/Amendment_A76_RigsTab_Prompt.md).
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> four waves (four at once in wave 1) and never touch MCP. Every task is at most two files with
> named line ranges — sized to stay under ~100k tokens.
> **Predecessor:** [`ClipSets_Roadmap.md`](ClipSets_Roadmap.md) — A76 is A75's shape applied to rigs.

## What it delivers

- **Three columns** in the existing Rigs tab, over two nested `TwoPaneSplitView`s: a rig **catalog**
  (280px), the **targets** list that is the tab's current left column (to 640px together), and the
  existing **preview**, which takes the rest and grows.
- **Catalog:** every `RigAsset` in the project as boxed rows (name, `n targets · folder`), New and
  Refresh in the header, a search field — a close mirror of A75's clip-set catalog, deliberately not
  an extraction of it.
- **Edit mode:** clicking a rig fills the middle column with every renderer-bearing node in its
  prefab, that rig's targets ticked. Tick adds a target, untick removes one, the Tag button retags —
  each an immediate, undoable `.asset` write. Unticking a target that clips animate asks first,
  naming them. A target whose node is gone shows as a ⚠ row rather than vanishing.
- **Create mode is untouched** — the same flow that exists today, reached by New.
- **Three small pure pieces** carry the logic and the fixtures: `RigTargetRowBuilder` (the
  prefab-nodes ∪ rig-targets merge), `RigTargetReferenceResolver` (which clips bind to a target),
  and four write methods on `RigAssetUtility`.

## Owner calls recorded in the spec (§2) — do not re-ask

Full edit, not browse-only; ticks apply immediately with undo; selecting a rig does **not** repoint
the Clip Editor (an explicit "Use in Clip Editor" button does); no delete in the catalog this round;
the catalog mirrors A75's rather than extracting a shared control; 640/280 starting widths;
`NewRigPanel` → `RigsPanel` and `ClipEditorTab.NewRig` → `ClipEditorTab.Rigs`, but the UXML element
names stay.

## Open after the build

⏸ **T8 owner checkpoint** — the spec's §5 T8 message says exactly what to open and press, and asks
three questions: the two starting widths, whether the divider positions should be remembered between
sessions, and whether the ⚠ missing-node row reads clearly. It also puts the deliberately-absent
rig delete back on the table.
