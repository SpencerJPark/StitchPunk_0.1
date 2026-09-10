# Shared asset selection — the toolbar's Clip Set and Rig fields move into the tabs, and every tab shares them

> **Status:** 📝 specced 2026-09-09, not built.
> **Spec:** [`Amendment_A80_SharedAssetSelection_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A80_SharedAssetSelection_Spec.md).
> **Session prompt:** [`Amendment_A80_SharedAssetSelection_Prompt.md`](../../../../Docs/AnimationToolkit/Amendment_A80_SharedAssetSelection_Prompt.md).
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> two waves — fifteen at once, then two — and never touch MCP. Tasks code against each other's
> spec'd public surfaces and are gated once per wave. Every task is at most two files with named
> line ranges — sized to finish inside the 40-turn cap.
> **Predecessors:** [`ClipSets_Roadmap.md`](ClipSets_Roadmap.md), [`RigsTab_Roadmap.md`](RigsTab_Roadmap.md)
> — their catalogs become the pickers. A79 (VAT preview modes) is unrelated and stays queued behind
> A78's checkpoint.

## What it delivers

- **The top bar is tabs and the validation badge.** The Clip Set and Rig `ObjectField`s leave it.
- **One `ActiveAssetSelection` per window** — an instance class holding the clip set and the rig,
  one event per property, written by every tab and read by every tab. The window's `activeRig` /
  `clipSet` fields mirror it; session state and re-dock carry it as they already do.
- **Clip Editor:** the clip set field sits under the **Clips** pane header, the rig field under the
  **Rig Hierarchy** header, full width, same element names as before.
- **Clip Sets / Rigs tabs:** clicking a row *is* the pick (reverses A76-D3). "Open/Use in Clip
  Editor" become plain tab jumps.
- **VAT Bake:** the two fields are live and shared; the "change them in its top bar" hint is gone.
  The standalone `VatBakeWindow` owns a selection of its own.
- **Actor Profiles:** four resizable columns — the two shared fields plus a searchable **Profiles**
  catalog (New, Refresh, right-click Rename/Delete; mirror of the rig catalog), then Layers,
  Preview, Actor Inspector. Picking a profile sets the shared rig. The header Profile field is gone.
- **Every "assign … in the toolbar" string** in editor sources and `Documentation~` is rewritten.

## Owner calls recorded in the spec (§2) — do not re-ask

Panels write the selection directly (supersedes "panel reports, window acts" for these two
values); catalog click = pick; Open/Use buttons stay as tab jumps; VAT Bake fields stay and go
live; four columns over three nested splits with floors 880/680/460; the profiles catalog mirrors
`RigCatalogColumn`; A77 parity (New/Rename/Delete) on the profiles catalog; picking a profile sets
the rig, never the clip set; the two USS toolbar classes stay because the Cutscene panel uses them.

## ⚠ Three interpretations the owner judges at T13

1. Both shared fields at the top of the Profiles column (his words were "it will have clip").
2. VAT Bake's fields kept and made editable, rather than removed.
3. **New** on Clip Sets/Rigs now makes the empty asset active everywhere.
