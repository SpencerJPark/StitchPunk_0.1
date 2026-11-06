---
status: IN PROGRESS, autonomous run started 2026-11-06 (owner away; decisions below were made by the orchestrator and are final unless the owner overrides)
inputs: Tasks/Claude/Audit_TerrainEditor_UI_2026-11-06.md, Research_WorldEditor_Splines.md, Research_WorldEditor_Brushes.md, Research_WorldEditor_Workflow.md
style: Docs/AnimationToolkit/EditorStyleGuide.md (R01-R22, shadcn + Blender, owner-approved)
---

# Terrain Editor Redesign

## 0. Mandate (owner, 2026-11-06)
The current editor is "very far from any sort of final product": tabs of forms, no visible way to edit a road spline or its thickness, unclear how to sculpt, paint, place decals, a bad generator, and UI that ignores the style guide. Build it to professional quality, run unattended, judge harshly, review on return. Use research (`Tasks/Claude/Research_*`) not guesses.

## 1. Decisions (TE-D1 onward; made by the orchestrator, do not re-ask)
- **TE-D1 Reuse the approved chrome.** `StitchPunk.Editor` already references `DotsAnimationToolkit.Editor`; build every panel with `ToolkitChrome` (cards, segmented control, primary/secondary/ghost actions, empty state, property rows, status row, badges) and the package tokens (`ToolkitChrome.AddToolkitStyleSheets`). `TerrainEditor.uss` keeps only terrain-specific classes (`terrain-*`), no literal greys (R15). The package tab list is replicated with `Toolbar` + `.toolkit-tablist-bar` / `.toolkit-tablist` / `ToolbarToggle.toolkit-tablist__tab` (+ `--active`).
- **TE-D2 Tabs (top tab list, R11), plain names:** World, Sculpt, Paint, Roads, Water, Decals, Props, Generate, Nav. (Old: Tiles, Height, Scatter.) Tab indices and session restore stay. No dead tabs (the disabled Stream entry is removed).
- **TE-D3 Every tab has the same skeleton:** 32px pane header (title left; primary action right, R12), mode segmented control under it where the tab has modes, cards for groups (R09), empty states designed (R13), footer status/hint row (R14) showing active tool, key hints and the tile under the cursor.
- **TE-D4 Scene view is the work surface.** A shared `TerrainSceneHud` draws a bottom-centre hint strip (the keys that work right now, changes with held modifiers) and the tool cursor; the panel holds only numbers and palettes. Immediate-mode drawing (`Handles`, `Handles.BeginGUI`) is allowed here (StitchPunk.Editor, not the package).
- **TE-D5 One shortcut set across Sculpt, Paint, Decals, Props:** `[` `]` brush size, `-` `=` strength, **Ctrl = invert** (raise/lower, paint/erase), **Shift = smooth** in Sculpt and **erase** in Decals/Props. Cursor = two rings (outer size, inner hardness), tinted when inverted. Falloff = a preset row with curve icons plus a hardness value. Strokes are one undo step.
- **TE-D6 Roads = a custom Scene-view tool** (research 5.2): selected road always shows its centreline, knot dots and two width edge lines; hover shows a ghost knot on the line, **click the line inserts a knot (no modifier)**, drag moves (terrain-snapped), Delete removes (neighbours refit, minimum 2), Shift+click at an end extends, right click menu, Esc deselect. **Width is full metres**, edited by a slider in the panel and by drag handles on the edge lines; **per-knot width** is stored as a parallel array kept in step by the tool (insert/delete/reverse update it); Unity's stock spline tool is not offered for these roads.
- **TE-D7 Generate keeps its data layering** (generator writes `baseHeights` only; hand edits live in `handHeightDelta`, so regenerating never destroys them, and the panel says so). Region = the tiles selected in the World map. Seed shown with Randomize and a lock; presets (named parameter sets); cheap low-resolution preview on the active tile while dragging, Apply commits, Revert restores; a pending-changes indicator on Apply.
- **TE-D8 Decals and Props are object tools:** a thumbnail gallery of the set's entries (atlas tile images), a "placed in this tile" list (thumbnail, name, lock, select / frame / delete), the selected item's properties; stamp/erase/region as segmented modes; in the Scene view selected items show an outline and the projection volume.
- **TE-D9 World tab** = overview map (status colours, multi-select by click/shift/drag, create via "+", right-click for open/close/frame/delete) + cards for Navigation bake status, Seams check and Budgets. The Nav tab keeps the bake controls and overlay toggles.
- **TE-D10 Proof (R22):** every phase ends with a capture of each affected tab at 760 x 900 pt before and after, read by the orchestrator against R01-R21 and a spacing probe; interaction logic lives in pure static helpers with EditMode tests (tests prove they can fail); a phase is not done on a green gate.

## 2. Phases
| Phase | Scope | Status |
|---|---|---|
| E1 Foundation | window shell (tab list, header, footer hint row), shared panel building blocks, `TerrainSceneHud`, stylesheet rewrite, restyle of all panels to the skeleton without behaviour change | todo |
| E2 Sculpt + Paint | cursor rings, shortcuts, falloff presets, mode icons, flatten target, layer palette with coverage bars, live update check | todo |
| E3 Roads | the custom Scene-view tool, width (whole + per knot), panel | todo |
| E4 Decals + Props + Water | galleries, placed lists, selection and handles, water panel on the skeleton | todo |
| E5 Generate + World | layered generator panel with presets/seed/preview/apply, World overview, checks | todo |
| E6 Harsh review | capture every tab again, fix list, owner checklist, docs, commit | todo |

## 3. Progress log (append, newest last)
- 2026-11-06 spec written. Research read. Audit captured.
