# Research: what makes a world editor human usable

Date 2026-11-06. Purpose: input for reworking the Terrain Editor (9 tool tabs, form-heavy, one-shot Generator).
Convention: "Source" claims were seen in a page or search result listed in section 6. "Inference" marks my own reasoning, not a sourced fact. "Unverified" marks things I looked for and did not confirm.

## 1. Summary

- Every professional terrain tool studied separates three things we mix together: what mode you are in (few, coarse), which tool is active (many, in the viewport), and the tool's settings (a small panel that follows the active tool).
- Unreal's Landscape mode has three top-level modes (Manage, Sculpt, Paint). Manage covers the landscape as a whole and its tiles/components; Sculpt and Paint are brush work. Nine peer tabs is more than any of these tools exposes at the top level.
- Unity 6 puts authoring tools in the Scene view as Overlays (toolbars and panels, dockable, saveable layouts). Our left-rail window pushes work out of the viewport; the engine's own convention is the opposite.
- Blender gives three feedback channels for a modal tool: status bar key hints (context, modifiers, active tool), a redo/"Adjust Last Operation" panel in the viewport, and undo history.
- Good generators are graphs or layer stacks, not a form plus a button. Gaea is a node graph with 2D and 3D viewports; Houdini adds one layer per operation; World Creator is a layer stack with real-time GPU preview; Unreal Landscape uses an edit-layer stack.
- Generated and hand-made data coexist through layers and masks: separate containers you can hide, lock, reorder, and blend (Unreal edit layers, Houdini height/mask layers, World Creator mask layers). Unreal's spline layer is procedural-only and cannot be painted by hand, so the boundary is explicit.
- Seed handling: a seed is a per-node property with a Randomize button (Gaea changelog 1.3.0). Parallel processing can make results vary slightly unless a Deterministic option is on (Gaea Wizard docs). Seed must be a visible, stored input.
- Heavy work is gated: Gaea marks "Heavy" nodes and holds changes until Apply; World Machine runs an interactive preview while dragging and a refining preview after commit. Cheap-while-dragging, exact-on-commit is the pattern.
- Large worlds use an overview map with rectangle region selection and explicit load/unload (Unreal World Partition minimap). Region selection is the standard way to constrain an operation or limit what is loaded.
- Nielsen's heuristics that bind hardest here: visibility of system status, recognition over recall, user control and freedom, flexibility and efficiency (hidden accelerators), minimalist design.

## 2. Per-tool findings

### 2.1 Unreal Engine: Landscape mode
- Feels like: one editor mode with a small number of top-level modes, and a tool list under each. Source: Epic Landscape quick start and mode pages.
- Surfacing: three modes selected by icons at the top of the Landscape window. Manage = the landscape as a whole plus components/tiles (New, Import, Select, Add, Delete, splines, Copy tool). Sculpt = Sculpt, Smooth, Flatten, Ramp, Erosion. Paint = Paint, Smooth on material layers. Source: Epic Landscape docs (search result summary of the 5.x pages and 4.26 pages).
- Edit layers: a stack of non-destructive containers, each with its own heightmap and paint data and alpha. Layers can be hidden (eye icon), locked, reordered by dragging, collapsed into the layer below. "Collapse All" flattens into base and is destructive. Default maximum 8. Source: Epic Landscape Edit Layers page (opened).
- Procedural vs manual: Spline and Patch layers hold only procedural data and cannot be sculpted or painted by hand; they update when the splines/patches change. Source: same page.
- Complaint evidence: forum reports that the mode panel can vanish in 5.4 until EditorLayout.ini is reset. That is a state-recovery problem, not a design critique. Source: Epic forum thread in search results.
- Unverified: I did not find a primary source for design complaints about Landscape UX beyond forum anecdotes.

### 2.2 Unreal Engine: World Partition (editor navigation)
- Feels like: a 2D overview window of the whole map where you rectangle-select a region and load or unload it, so only the area you work on is in memory.
- Surfacing: editor-only minimap actor (world bounds, units per pixel, excluded data layers) shown in the World Partition window. Editor command names include LoadSelectedRegions, UnloadSelectedRegions, FocusLoadedRegions, CreateRegionFromSelection. Source: Epic API pages in search results.
- Forum evidence (user opinion, not official): since 5.1 the minimap is no longer a fixed grid and any rectangle can be loaded; one user complains loading pulls in any actor whose bounds overlap the selection; others report regions persisting across editor sessions and the minimap showing "no regions loaded" incorrectly.
- Takeaway (inference): rectangle select on an overview, explicit loaded-region state, and persisted loaded state are all useful but need to be visible, because the failure reports are about confusing state.
- Unverified: official editor usage page for World Partition was not retrieved. World Composition (the older tiled system) was not researched.

### 2.3 Unreal Engine: PCG framework
- Feels like: a graph editor (nodes) driving generation in the level, with debug and inspect on nodes.
- Surfacing: three generation modes (non-partitioned default, partitioned over a user grid, hierarchical which requires partitioned and a grid size set in graph settings). Debug dropdown visualises each node's grid size; per-node debug toggle (D key) and Inspect (A key) from the Debug Tree. Source: Epic PCG generation modes docs (UE 5.3 pages in search result).
- Complaint evidence: forum threads report results vanishing in PIE with Editing Mode set to Preview, and partition actor problems with Load-as-Preview in offline commandlet runs (UE 5.6 era). User reports, not official.
- Unverified: PCG graph UX design rationale; I only saw docs summaries and forum threads.

### 2.4 Unity: Terrain Tools, Terrain Toolbox, Overlays
- Terrain Tools package adds erosion, sculpting, brush mask filters, and a Terrain Toolbox for preset file management and heightmap import. It supports batch settings across tiles and splatmap/heightmap import/export. Source: Unity 6.0 manual terrain-Tools page (search result).
- Terrain painting tools appear in a Terrain Tools overlay (Sculpt Mode, Materials Mode) as well as the Inspector. Tools with brushes show a Brush Masks overlay and sometimes a Brush Attributes overlay (opacity, size); custom tools get a fifth Custom Brushes button. Source: Unity 6.1 manual and TerrainPaintToolWithOverlaysBase script reference.
- Scene view Overlays: "persistent, customizable panels and toolbars", orientations panel/horizontal/vertical, toggled with the backtick key or the More menu, with save/switch/import/export of overlay configurations. They can also show contextual information about the selection. Source: Unity 6.0 overlays page (opened).
- Not found: a step-by-step Terrain Toolbox workflow guide; tool-context system and ProBuilder toolbar details were not opened. ProBuilder is unverified in this research.

### 2.5 Blender
- Feels like: one active tool at a time, its options in the header, key hints in the status bar, last operation editable in a HUD.
- Status bar: Julian Eisel's commits added a global bottom bar (file path, scene stats, progress and reports), then mouse-button action hints based on context, modifiers and active tool, and a work-in-progress display of modal operator keymaps. Source: Blender archive commits (search result).
- Header vs status bar: a developer proposed keeping values in the editor header and moving shortcuts to the status bar. Commenters were split: some found the status bar a step backward, one suggested hints inside the 3D view, others said experts can hide it. Source: BlenderArtists thread (search result).
- Redo panel: Adjust Last Operation (F9 or Edit menu) is a head-up panel at bottom left of the editor whose contents depend on the last operation and mode; Repeat Last is Shift-R; Undo History lets you jump back to any action. Source: Blender manual Undo and Redo page (search result).
- Complaint evidence: a 2.80-era bug where the top bar did not refresh when the active tool changed until the mouse hovered over it. Stale tool state is a real usability defect. Source: developer.blender.org T64650 (search result).
- Unverified: I could not fetch the manual's Status Bar and Tool System pages (the fetch returned only navigation). Statements about their current content are not confirmed.

### 2.6 Gaea (QuadSpinner)
- Feels like: a left-to-right node graph; 3D and 2D viewports show the terrain while you build; the 2D viewport doubles as an editing surface for nodes like Island and Mask. Source: Gaea interface page (opened).
- Properties panel shows the selected node's settings and hosts the Build tab (Build Manager, Terrain Definition). Auto Apply toggle: for nodes marked Heavy, or when Auto Apply is off, changes are not applied until you click Apply; an Apply/Lock button shows pending changes. Source: same page.
- Pipeline described as Create, Modify, Erode, Texture, Export; Texture node makes masks from slope and soil; render modes include raw data views. Source: Gaea Graph and Workflow pages (search result).
- Seed: defined per node as "the randomization pattern or seed"; a Randomize button was added to all Seed sliders (1.3.0 changelog); parallel CPU processing can cause small variations every build, and a Deterministic (Slow) option fixes this. Source: Gaea Erosion, Wizard and changelog pages (search result).
- Unverified: whether Gaea has a global seed or region-constrained build. Not found.

### 2.7 World Machine and World Creator
- World Machine: two preview engines, an interactive one while you drag a control and a refining one that builds in the background per committed change; background previews prioritise currently viewed devices. Tiled builds split the world into tiles so only the region in play must be in memory, tiles export as a tileset, and a file input device pointed at one tile finds the rest. Scripts can drive resolution, extents or specific tiles. Source: World Machine forum posts and Pro edition page (search result).
- World Creator: GPU-based; layer workflow (Biome, Mask, Shape layers); mask layers can target stamp, sculpt and procedural layers; sediment distribution controllable by paintable masks; live preview setting and hover preview of filters. Mostly vendor material. Source: world-creator.com features, docs generator options, CGChannel (search result).
- Unverified: World Machine seed behaviour and 3D view; World Creator mask-combination details.

### 2.8 Houdini heightfields
- Terrain is 2D volumes: height layers (default "height") plus separate mask layers holding a "selected-ness" value. Almost every operation creates a new layer, so the network stays editable. HeightField Paint can paint heights or paint a mask layer; masks are inserted before the node they limit; Layer Mode and HeightField Layer control stacking. Recommended approach: block a rough shape, then noise and erosion. Source: SideFX heightfield docs (search result, mixed 20.5/21.0/22.0).
- Takeaway (inference): masks are first-class data, not a UI selection. That is what makes "constrain generation to a region" a normal operation.

### 2.9 Cities: Skylines (I and II), RimWorld
- CS2 editor: one unified editor with terrain tools in a bottom tool area under a shovel icon; heightmap import/export retained. A launch-week Steam post says the terrain brush did not project properly onto the terrain. Source: Paradox dev diary and Steam thread (search result).
- CS1 landscaping update: Shift, Level, Smooth, Slopes with 3 brush sizes and 3 strengths. Source: GamingOnLinux (search result).
- RimWorld map generation UX: Unverified; nothing found.

### 2.10 Tiled and LDtk
- Tiled: primary tile tool is a stamp brush for painting and copying areas; tile stamp memory; automatic terrain transitions and pattern-matching auto-mapping; object layers not bound to the grid; projects optional; Ctrl+Shift+P action search; Ctrl+P file open. Source: Tiled introduction manual (opened). The manual excerpt did not mention worlds.
- LDtk: auto-layers are IntGrid layers plus rules; SHIFT+R toggles showing raw IntGrid with tiles hidden; 0.9.0 added optional rule groups (disabled by default, enabled per level) and a Rules panel with fewer buttons. Source: LDtk docs and 0.9.0 release notes (search result).
- Takeaway (inference): the praised pattern is paint abstract data, let rules produce the visuals, and let you flip to the abstract view in one key. That is the same split as hand-edit layer vs generated layer.
- Unverified: LDtk World view details and general praise. No source found; "praised for usability" in the brief is the owner's premise, not something I confirmed.

## 3. Usability principles for world editors (adoptable)

1. Few top-level modes, many tools. Epic uses three modes; each tool is one click inside a mode. Keep the mode list to the verbs a user thinks in (inference from 2.1).
2. The viewport is the work surface. Brushes, handles, region rectangles, previews and gizmos live in the Scene view; panels hold only the active tool's numbers (Unity Overlays, Blender HUD).
3. One active tool, always visible. The active tool is highlighted, its cursor/brush is drawn in the viewport, and its settings panel changes with it. Stale state is a bug (Blender T64650).
4. A hint bar shows the keys available right now, for the active tool and held modifiers (Blender status bar commits). Make it hideable, since experts disagree on value (BlenderArtists thread).
5. A last-operation panel: after any generate, stamp or fill, the parameters stay editable in place and re-apply (Blender F9 HUD). This replaces most "settings tabs".
6. Undo is total and listed. Include generation, tile creation and bake steps in undo, with a visible history (Blender undo history; Nielsen user control and freedom).
7. Cheap while dragging, exact on commit. Low-res interactive preview during a slider drag, full-quality on release (World Machine dual preview; Gaea Heavy/Apply).
8. Show pending state. If a parameter changed but is not applied, say so on the control (Gaea Apply/Lock button).
9. Seeds are inputs with a Randomize button, stored with the result, and determinism is a visible choice (Gaea changelog and Wizard docs).
10. Generated and hand-made data live in separate layers that can be hidden, locked and reordered; procedural-only layers say they are procedural (Unreal edit layers).
11. Masks are first-class. Any operation can take a region or mask, which is how generation is constrained (Houdini, World Creator mask layers).
12. Overview navigation: a whole-world map with rectangle select, explicit loaded region state, and focus-to-selection (World Partition editor commands).
13. Direct manipulation beats forms. Draw the region, drag the handle, paint the mask. Use a form only for numbers that have no spatial meaning.
14. Progressive disclosure. Defaults produce a good result; advanced parameters sit behind a fold; accelerators stay hidden from novices (Nielsen heuristic 7, heuristic 8).
15. Empty states teach the next action. An empty world shows "create first tile" with the tool already armed (Nielsen heuristics 6 and 10; inference for the specifics).
16. Customisable and recoverable layout. Save, reset and import layouts (Unity overlay configurations; the Unreal missing-panel reports show the cost of lacking a reset).
17. Command search for everything. Tiled has an action search widget (Ctrl+Shift+P); with many tools this replaces hunting through tabs.

## 4. Anti-patterns

- A tab per feature full of forms. Nine peer tabs make the user decide between features before deciding what to do, hide state in tabs not currently shown, and put the work away from the viewport. Professional tools use few modes plus an active-tool model.
- A generator that is a one-shot button. A button that overwrites the terrain gives no preview, no seed history, no region limit, no layer separation, and no way back except undo. Every generator studied here is graph- or layer-based and previews before committing.
- Generation that overwrites hand edits. Unreal and World Creator keep these in separate layers; Houdini creates a new layer per operation.
- Settings that live far from the thing they affect, with no on-canvas representation of the region, brush or output.
- Hidden modal state: no visible indication of which tool is active, or a header that does not refresh (Blender T64650).
- Silent loaded-region or cache state: World Partition forum threads show confusion when loaded regions persist or the minimap says nothing is loaded.
- Full-resolution recompute on every slider tick (World Machine and Gaea both avoid it).
- No reset for panel layout.
- Documentation as the only explanation of controls. Nielsen heuristic 10 says the system should not need it; hints belong in the UI.

## 5. Recommendations for our editor

These are proposals (inference), grounded in the findings above. Names refer to our nine tabs.

### 5.1 Overall structure
Replace "left rail of nine tabs" with: a Scene-view tool palette, a small contextual tool-settings overlay, an overview map, a hint bar, and one Layers panel. The EditorWindow keeps only what has no spatial meaning (tile list/budgets, bake status, project-wide settings).

Modes (top-level, three to four): World (tiles, overview, bake), Terrain (Height, Paint, Generate, Water), Features (Roads, Decals, Scatter), Check (Nav, budgets, validation). Source basis: Unreal Manage/Sculpt/Paint split. Exact grouping is a design call for the owner.

### 5.2 Scene view versus panel
- Scene view: brush cursor and falloff, region rectangle for Generate, tile borders with names, road splines with handles, scatter area outlines, water level plane, nav overlay toggles. Tool palette as an Overlay-style vertical toolbar at one edge; the active tool's settings in a compact panel overlay beside it.
- Panel (EditorWindow): tile list with status, layers stack, bake/export, budgets (ribbon vertex budget, tile counts). Anything numeric that is per-tile and not per-brush.

### 5.3 Tool palette organisation
- One palette, grouped by mode, active tool highlighted, tooltip with name, key and one sentence.
- Roads, Decals, Scatter, Water become object-placement tools: click in the viewport to place, handles to edit. Their forms shrink to the selected object's inspector.
- Height and Paint share one brush settings overlay (size, strength, falloff) instead of two forms (Unity Brush Attributes overlay is the precedent).

### 5.4 Status and hint bar
A bottom strip: active tool, held-modifier-dependent key hints for the active tool, current tile coordinate under the cursor, loaded/dirty tile counts, last bake time. Hideable. Source basis: Blender status bar; dissent noted in 2.5.

### 5.5 Generate
- Make it a layer, not an action. A "Generated" layer sits under hand-edit layers (Height edits, Paint edits). Regenerating replaces only the Generated layer's data; hand layers persist. Source basis: Unreal edit layers, Houdini layers.
- Region: the user drags a rectangle (or paints a mask) in the Scene view; Generate affects only that region, with a feathered edge. Default region: selected tiles. Source basis: World Partition rectangle select, Houdini masks.
- Seed: shown, stored on the layer, Randomize button, and a "lock" so tweaking other parameters does not reroll. Offer a deterministic toggle if we parallelise. Source basis: Gaea.
- Preview: cheap low-res preview in the viewport while dragging parameters; Apply to commit at full quality. Pending-changes indicator on the Apply button. Source basis: World Machine, Gaea.
- Structure of parameters: a short stack of named stages (Base shape, Erosion, Texture/masks) each with a handful of controls and presets, rather than one long form. A full node graph is probably overkill for us; the stack gives most of the reuse. (Inference.)
- Presets: named parameter sets with thumbnails; "Duplicate layer" to compare variants; the last-operation panel keeps parameters editable after generation.
- Outputs of Generate beyond height (splat masks, flow, slope) are exposed as masks the Paint and Scatter tools can read (Gaea Texture node, Houdini masks).

### 5.6 Tiles: overview, creation, navigation
- Overview map: a dockable view of all tiles as a grid with heightmap thumbnails, status colours (not created, clean, dirty, bake stale), and the current camera frustum. Click focuses the Scene view; rectangle-select chooses tiles for Generate, bake, or delete. Source basis: World Partition minimap and commands.
- Create tile: empty grid cells show a "+" in the overview and in the Scene view at the world border; clicking creates the tile with the default settings and arms the last tool. Neighbour-edge matching happens automatically.
- Navigation: "Focus selected tiles" and "Go to tile" (action search), the tile under the cursor in the hint bar, optional loaded-region rectangle with explicit loaded/unloaded display (and a reset), to avoid World Partition's confusing-state reports.
- Tile-level actions (bake, export, delete) are right-click on the overview, not a separate tab.

### 5.7 Delete or merge
- Delete the Tiles tab as a tab; its content becomes the overview map plus tile list panel.
- Merge Height and Paint into one Brush tool group sharing one settings overlay.
- Merge Roads, Decals, Scatter, Water into placement tools with in-viewport handles; keep their forms only as selected-object inspectors.
- Turn Generate into a layer with a region (5.5); delete the one-shot form.
- Nav becomes a Check-mode overlay toggle plus a validation list, not a tab.
- Add: hint bar, last-operation panel, layout reset, command search, undo history for generation and bake.

### 5.8 Order of work (suggestion)
1. Tool-active model plus palette and hint bar (everything else depends on it).
2. Overview map and tile creation.
3. Layers (Generated vs hand edits) and region-constrained Generate with seed and preview.
4. Fold Roads, Decals, Scatter, Water into placement tools.

## 6. Sources
Opened with WebFetch:
- https://dev.epicgames.com/documentation/unreal-engine/landscape-edit-layers-in-unreal-engine
- https://docs.quadspinner.com/Guide/Interface/Getting-around.html
- https://docs.unity.com/en-us/engine/6000.0/manual/working-with-scenes/scenes-manage-gameobjects/overlays
- https://www.nngroup.com/articles/ten-usability-heuristics/
- https://doc.mapeditor.org/en/stable/manual/introduction/

Seen in WebSearch results (summaries only, page not opened):
- https://dev.epicgames.com/documentation/unreal-engine/landscape-quick-start-guide-in-unreal-engine
- https://dev.epicgames.com/documentation/unreal-engine/landscape-overview
- https://dev.epicgames.com/documentation/unreal-engine/editing-landscapes-in-unreal-engine
- https://forums.unrealengine.com/t/ue-5-4-landscape-mode-active-but-landscape-editor-panel-not-visible/2702525
- https://dev.epicgames.com/documentation/en-us/unreal-engine/API/Runtime/Engine/AWorldPartitionMiniMap
- https://dev.epicgames.com/documentation/unreal-engine/API/Editor/WorldPartitionEditor/SWorldPartitionEditorGrid2D/FEditorCommands
- https://forums.unrealengine.com/t/how-to-load-small-cell-in-world-partition-window/1156402/3
- https://forums.unrealengine.com/t/bring-back-world-partition-cell-loading-or-make-it-optional/788178
- https://dev.epicgames.com/documentation/unreal-engine/using-pcg-generation-modes-in-unreal-engine?application_version=5.3
- https://forums.unrealengine.com/t/pcg-result-not-present-in-pie-when-using-editing-mode-set-to-preview/2551065
- https://forums.unrealengine.com/t/offline-pcg-workflow-broken-partition-actor-preview-mode-problems/2666133
- https://docs.unity3d.com/6/Documentation/Manual/terrain-Tools.html
- https://docs.unity3d.com/6000.1/Documentation/Manual/terrain-UsingTerrains.html
- https://docs.unity3d.com/ScriptReference/TerrainTools.TerrainPaintToolWithOverlaysBase.html
- https://docs.blender.org/manual/fi/latest/interface/undo_redo.html
- https://blenderartists.org/t/new-status-bar/1116690
- https://developer.blender.org/T64650
- https://projects.blender.org/archive/blender-archive/commits/commit/f45e55c9c83fd70b7534cb15019913a2e9ee33ed/source/blender/editors/screen?page=9
- https://docs.quadspinner.com/Guide/Graph/Graph.html
- https://www.quadspinner.com/Gaea/Workflow
- https://docs.quadspinner.com/Reference/Erosion/Wizard.html
- https://docs.quadspinner.com/Reference/Erosion/Sediment.html
- https://docs.quadspinner.com/Changelogs/Bleeding-Edge/1.3.0.0.html
- https://forum.world-machine.com/t/lte-feature-2/5070
- https://forum.world-machine.com/t/dev-build-4030-artist-point-available/6437
- https://www.world-machine.com/purchase.php?page=pro
- https://world-creator.com/features.html
- https://docs.world-creator.com/reference/options/generator
- https://www.cgchannel.com/2023/11/bitethebytes-releases-world-creator-2023
- https://www.sidefx.com/docs/houdini/heightfields/layers.html
- https://www.sidefx.com/docs/houdini/heightfields/painting.html
- https://www.sidefx.com/docs/houdini/heightfields/workflows.html
- https://www.paradoxinteractive.com/games/cities-skylines-ii/modding/dev-diary-2-map-editor
- https://steamcommunity.com/app/949230/discussions/0/4299322040762099267
- https://gamingonlinux.com/2016/03/cities-skylines-updated-with-new-map-tools-and-tons-of-fixes/page=2
- https://ldtk.io/docs/general/auto-layers
- https://ldtk.io/docs/general/auto-layers/auto-layer-rules
- https://deepnight.itch.io/ldtk/devlog/243249/090-biomes-tags

Not verified (searched, nothing usable): RimWorld map generation UX; Blender manual Status Bar and Tool System page text; Unity tool-context system and ProBuilder toolbars; Unreal World Composition; Gaea/World Machine global seed and region-limited builds; Houdini UI beyond node docs; Unreal/Blender UX design talks.
