# A110 — Polish pass 4 (fresh-eyes audit, 2026-09-30)

Owner, 2026-09-30: "I still see plenty of spacing problems and design inconsistencies … A++ professional level. Be harsh."
Audit basis: `Docs/AnimationToolkit/EditorStyleGuide.md` (R01–R22). Before captures: `Library/UIAudit_0930/before/`
(docked window 1275×628 pt, 2.5 ppp). Geometry probe: `Library/UIAudit_0930/probe.txt`, `titles.txt`.
Cutscenes tab (T17) is held: a peer session has an uncommitted probe in `CutsceneEditorPanel.cs`.

## 1. Global — done by the stage (ClipEditorWindow.uss)

| Id | Finding (measured) | Fix |
|---|---|---|
| G1 | Pane titles resolved 12px (card titles 13px, so every card outranked its pane) and carried 8px side padding, so every header sat 8px right of its rows (R02, R05). | `.toolkit-pane-title` 13px, no padding. |
| G2 | Flush panes (Clip Editor, viewport columns, Ragdoll/Retarget previews, Texture Packer graph) then had titles on the column edge. | Header carries the 12px inset in those panes. |
| G3 | Asset bar height 30/33/34/36 across tabs; right padding 8 (primary buttons 8–14px from the edge vs 12 on the left) (R05, R06). | `min-height: 36px`, `padding-right: 12px`. |
| G4 | Search fields 16px (Unity toolbar variant), below the 20px field token (R10). Health's sat 4px from the edge. | `.unity-toolbar-search-field` 20px; Health search 12px inset. |
| G5 | Retarget's preview header 8px above Tracks (R06). | `#retarget-preview` padding-top 8. |

## 2. Worker tasks (disjoint files; each ships captures, stage verifies)

| Task | Files | Findings |
|---|---|---|
| T1 Preview art | `Preview/PreviewRigMirror.cs`, `Preview/ClipPreviewController.cs` | The Clip Editor, Retarget, Ragdoll and Capture previews draw every target as a flat grey `PreviewNeutralSurface` quad **on top of** a static clone of the painted prefab: the character reads as grey slabs over a frozen body (R19 spirit — "a failed preview reads as broken"). Proxies borrow the source node's mesh, materials and sorting; the clone hides the renderers the proxies stand in for. |
| T2 Rigs | `Authoring/RigsPanel.cs` (+ its row element if separate) | Target rows show the truncated full path, ragged Kind/Tag columns, ticked rows taller than unticked, ticked checkbox drawn without its box (SG-D6: node name, path in tooltip, Kind/Tag chips on ticked rows). Folder label 5px left of the other labels; plain-text value 14px left of field text (R08). Hint repeats the count badge (R03). Catalog meta "1 targets". Footer "34 renderer(s)." |
| T3 Clip Sets | `Authoring/ClipSetsPanel.cs`, `Authoring/ClipPickerListElement.cs` | "1 clips"; picker row meta clipped at the card edge with no ellipsis ("VatSampleTentacle", R01); Folder value text misaligned with the Name field text (R08); catalog meta repeats the whole folder path (noise, truncates); header title repeats the Name field (R03). |
| T4 Clip Editor | `ClipEditorWindow.cs` (status line + viewport empty state only), `ClipEditorWindow.uss` Clip Editor section | Viewport empty-state text runs under the tool rail ("…ck a rig", R01). Status line reads as debug output ("NewClip duration 1s loop Once selected 0"). "0 track(s)". Rig Hierarchy colours an animated node's name blue (R16: blue means selected). |
| T5 Events | `Events/EventKeyCatalogColumn.cs`, `Events/EventKeyInspectorColumn.cs`, `Events/EventUsageColumn.cs` | Middle column has no header (R06). "4/64" badge takes its own row under the search; footer "Event key budget" shows a label and nothing else. Two identical "No event selected" empty states, one centred, one top-aligned (R03, R13). Row meta "16 · maskable" is cryptic. |
| T6 Health | `Health/HealthFindingListElement.cs`, `Health/HealthPanel.cs` | Code badges float at ragged x (they follow title width) — fixed column needed. "V38:" id leaks into titles. Count said three times (tab, filter "All (6)", "Findings (6)", "6 findings" status) (R03). |
| T7 Materials | `Materials/MaterialInspectorColumn.cs`, `Materials/MaterialsPanel.cs` | Detail header says "Material", not the material's name (16/600 detail title, R02). Every card opens with a sentence explaining itself (noise). Hint text 14px right of labels (R05). GPU instancing badge alone on a row. |
| T8 Capture | `Capture/CapturePanel.cs` | Size 512×512 while Preset reads 256×256 (bug). Background/Format segmented controls centred in their cards instead of in the property grid (R08). Sub-hints indented 15px past labels; output path wraps and clips at the card bottom (R01). Time slider floats mid-bar with no fill to the edges. Preview header inset (G2). |
| T9 VAT Bake | `VatBaking/VatPreviewElement.cs`, `VatBaking/VatBakePanel.cs` | Empty-state text drawn over the grid, the green axis runs through the title (R13). Footer is coloured text with no tone dot and 6px inset (R05, R14). |
| T10 Actor Profiles | `ActorEditor/ActorEditorPanel.cs`, `ActorEditor/ActorEditorLayersColumn.cs` | With no profile: four empty messages saying the same thing (Layers, Preview, badge "No profile", Inspector), at three vertical positions (R03). Transport enabled with nothing to play (R17). Catalog rows show ".profile" suffix. |
| T11 Retarget | `Retarget/RetargetTrackTableElement.cs`, `Retarget/RetargetPreviewElement.cs` | Table column headers misaligned with their cells (Clip track 19px right of names, Status 31px right of badges). Pause button filled green where every other transport is neutral. |
| T12 Stats | `Stats/StatsPanel.cs` | Empty state then 150px gap above the cards; footer repeats it (R03). "not playing" floats centred in the asset bar (R14: status in footer). Events/frame card has an empty unlabelled sparkline block. Card "Actors" first row "Actors" (R03). |
| T13 Texture Packer | `TexturePacker/TexturePackerGraphView.cs` (+ output node view) | Pack Output node: PNG badge above the title baseline; Size/View rows 8px left of channel rows; ~200px dead space then "(no output path chosen)" off-centre. Catalog thumbnails not uniform (stretch vs tiny). |
| T14 Flipbooks | `Flipbooks/FlipbooksPanel.cs`, `Flipbooks/FlipbookFramesColumn.cs` | No selection shows the full import form + disabled Bake/Save instead of an empty state (R13). Output row label 19px left of card labels; italic "No folder chosen" (R02). |
| T16 Tab list | `ClipEditorWindow.cs` (tab strip builder) | Health tab sits after a wider gap; its count is red text in parentheses instead of a badge. |
| T17 Cutscenes | held | Asset field truncated without ellipsis; kind badge blue (R16); cast hint misaligned; viewport rail differs (R20); inspector header 8px high (R06); nested Locomotion card (R09); ragged grid (R08); centred track headers. |

## 3. Log
- 2026-09-30: audit + G1–G5 landed (USS only, refreshed, captured in `Library/UIAudit_0930/scratch/`).
