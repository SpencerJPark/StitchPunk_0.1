# Terrain Editor UI audit — 2026-11-06

Captured from the live window (all tabs, 760 x 900 pt, Editor unfocused but repainted; recipe in `Memories/Code/AnimationToolkit.md`). Captures live in the session scratchpad, not the repo. Style reference: `Docs/AnimationToolkit/EditorStyleGuide.md` (rules R01-R22; owner-approved 2026-09-15, shadcn/ui + Blender). **The terrain editor never followed it**: no brief, spec or worker prompt for P4-P10 mentioned the guide, and the plan's own notes say the brush overlay and sculptor were "compile + logic checked only". No capture was taken before this audit (R22 requires before/after captures for every visual change).

## What the captures show (against the rules)
| # | Finding | Rule |
|---|---|---|
| 1 | Every tab is a form stacked at the top of an otherwise empty 900 pt window; nothing in the panel helps you do the real work, which happens in the Scene view. No key hints, no brush preview, no on-screen tool state beyond a footer. | R13, R14, visual-first note |
| 2 | The left rail (10 buttons) and the in-tab mode buttons (Raise / Lower / Smooth..., Stamp / Erase / Region) are the same grey default Unity button. Navigation and modes look identical. | R11 |
| 3 | Selected state is blue fill with blue text (rail "Roads", mode "Raise", the road list row, the primary buttons): low contrast, hard to read. | R16, R18 |
| 4 | Wrong or missing primary action. Height's biggest control is "Rebuild Tile Mesh" (a repair action, not what you do there). Decals' "Scatter Tile" is a dim wide bar; "Create Default Set" competes with the real controls underneath. | R12 |
| 5 | **Roads has no width control, no knot list, no add/move/delete controls and no explanation.** The hint says "Drag the knots"; the only way to do that is Unity's generic spline tool, which is not surfaced. Select / Delete / Rebake are loose small buttons under a list row. | R12, owner request |
| 6 | Decals: an empty-state card ("No decal set") sits above live controls (Seed, Stamp/Erase/Region, Scatter Tile, Clear Unlocked, Lock All) so the tab reads as broken; no gallery of what a decal looks like, no list of placed decals. | R13, R09 |
| 7 | Generate: starts at "No terrain regions" and requires understanding a "region" and a "profile"; no preview, no seed, no live result. Its hint text tells you to use another tab's button to undo it. | R03, R13 |
| 8 | Paint: layer names sit at the card's left edge and overflow the card (text starts left of the swatch box). | R01, R05 |
| 9 | Inconsistent insets: hint text, list rows, buttons and labels start at three different x positions. | R05, R04 |
| 10 | No icons anywhere; every action is a word. | R21, visual-language note |
| 11 | A disabled "Stream" tab is a dead entry in the navigation. | R13 |
| 12 | The window is a stack of settings forms. Roads, Height, Paint, Decals and Scatter are all things you do by pointing at the world, but nothing in the window or the Scene view says what you can point at. | direct manipulation |

## Root causes
1. **Process:** workers were given text specs and never the style guide; nobody captured the window; green gates were treated as done (the exact failure the notes warn about).
2. **Design:** the editor was specced as one tab per feature with parameters, not around the owner's real tasks: shape the ground, paint it, lay roads, dress it with decals and props, see the whole world.
3. **Missing basics:** no on-screen brush cursor/preview confirmed, no width handles for roads, no overview of what exists (placed decals/props/roads), no hotkeys surfaced.

## Fix plan (after the research notes in this folder are read)
1. Read the three research notes (`Research_WorldEditor_*.md`), write a redesign spec in `Tasks/NewPlans/TerrainEditorRedesign_System.md` built on the style guide rules by id, and let the owner answer the product questions before any build.
2. Every UI task brief names the style guide, the rule ids to audit and the capture requirement; every UI task ends with before/after captures read by the orchestrator, plus a measured spacing probe.
3. Put the style rules in the repo's own instructions (CLAUDE.md) so no future brief skips them.
