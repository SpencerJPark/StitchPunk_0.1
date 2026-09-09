# Clip Sets — the "New Set" button becomes a tab that browses, creates and edits clip sets

> **Status:** ✅ built 2026-09-08, shipped as 0.22.0 (plus two visual follow-up commits).
> **Spec:** [`Amendment_A75_ClipSets_Spec.md`](../../../../Docs/AnimationToolkit/Amendment_A75_ClipSets_Spec.md).
> **Session prompt:** [`Amendment_A75_ClipSets_Prompt.md`](../../../../Docs/AnimationToolkit/Amendment_A75_ClipSets_Prompt.md).
> **Executor:** one Editor-connected orchestrator running the gate; `worker` subagents edit files in
> four waves (three at once in waves 1 and 2) and never touch MCP. Every task is sized under
> ~100k tokens: at most two files, named line ranges.

## What it delivers

- **A "Clip Sets" tab** in the Clip Editor's strip, between New Rig and Clip Editor, drawn over the
  dock like the other cover panes. The toolbar's "New Set" button and the window's `CreateClipSet`
  are deleted.
- **Catalog column:** every `ClipSetAsset` in the project as boxed rows (name, clip count, folder);
  New and Refresh in the header.
- **Editor column:** for a selected set, a virtualized, searchable, check-boxed list of every
  `ClipAsset` in the project with the set's members ticked — tick adds, untick removes, one undo
  step each, and the Clip Editor tab's own list follows. For a new set: name field, remembered save
  folder with a picker, a live "Will create …" path, the same picker, Load-into-editor toggle,
  Create.
- **Three small pure pieces** carry the logic and the fixtures: `ClipPickerModel` (filter + ticks),
  `ClipSetSaveLocation` (prefs, sanitize, project-relative folder), and two `ClipAssetUtility`
  methods (`AddExistingClipToSet`, `RemoveClipFromSet(set, clip)`).

## Owner calls recorded in the spec (§2) — do not re-ask

Tab not button; edit-mode ticks apply immediately with undo, create-mode ticks wait for Create;
folder remembered in `EditorPrefs` and validated on read; names sanitized and paths uniquified,
never overwritten; no `AssetPostprocessor` (rescan on show / Refresh / Create); panel reports,
window acts; the `ClipSetAsset` inspector is untouched.

## Open after the build

⏸ **T8 owner checkpoint** — the spec's §5 T8 message says exactly what to open and press, and asks
three layout questions (catalog search, Apply button, two-column shape).
