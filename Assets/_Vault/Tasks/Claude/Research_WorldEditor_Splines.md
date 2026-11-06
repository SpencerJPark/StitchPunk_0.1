# Research: Authoring linear features (roads, rivers, paths) in the viewport

Date 2026-11-06. Purpose: find interaction details to copy for the Terrain Editor Roads tab. Every tool claim carries a source number from section 6. Anything marked UNVERIFIED was not seen in a page that was opened or in search results.

## 1. Summary

1. Every tool studied has a path where the line itself is the interface: you see the curve and its points in the viewport, and you act on them there, not in a list or inspector.
2. Add-a-point has two forms. Append at the end (Shift/Ctrl+click in EasyRoads3D, Road Architect, UE) and insert into the middle by clicking the segment (UE Ctrl+click on a segment, EasyRoads3D `I`, Unity Knot Move click-on-spline).
3. Delete is almost always the Delete key or a one-key shortcut on the selected point (UE Del, Unity Delete, EasyRoads3D Shift+R). Blender uses X/Delete.
4. UE shows width in the viewport as solid lines (half-width) and falloff as dotted lines, and the numbers are editable per control point. This is the clearest width model found.
5. Width is per point in UE (half-width on each control point, shared by connected segments). EasyRoads3D and Road Architect expose width per road or per lane, not per point (as far as documented).
6. Unity Splines splits editing across three tools in a Scene view overlay (Knot Placement, Knot Move, Tangent Move) plus an Element Inspector overlay with tangent modes Linear/Auto/Bezier and Mirrored/Continuous/Broken. The insert-by-click behaviour is mentioned only once, under Knot Move. That hidden, mode-dependent behaviour is the likeliest source of the owner's confusion.
7. Unity Splines has no built-in per-knot width. Per-knot data needs custom code (extra data on the spline, or our own data array).
8. Road junctions in the asset-store tools are made by dragging an end node onto another node (Road Architect) or by attaching a crossing prefab to an end marker (EasyRoads3D). Neither tool treats a junction as a drawn feature of a line.
9. Cities: Skylines gives the user a small set of explicit modes (straight, curved, freeform, upgrade) plus snapping aids, and each in-progress step cancels with right mouse button.
10. Gaea and Houdini road/river tools, Blender's Ctrl+RMB extrude and Blender's handle-type shortcut, and official Godot Path3D controls could not be verified (section 6, last list).

## 2. Per-tool sections

### 2.1 Unity Splines package (editing UX)

What the user does (from the Getting Started page for 2.5, source 1):
- Create: GameObject > Spline > New Spline, right-click Hierarchy > Spline > New Spline, or add a Spline Container component. The first two routes activate the Knot Placement tool automatically.
- Place knots: with Knot Placement, click a Terrain or mesh surface. Clicks that miss a surface snap to the grid. Clicking the first knot closes the loop.
- Move knots: with Knot Move, select one or more knots; a position handle appears.
- Insert a knot: with Knot Move, click on the spline. This is the only mention of insertion on that page.
- Delete: select a knot, press Delete. Ctrl+Z deletes the last created knot. Esc exits knot creation.
- Tangents: Tangent Move tool. Holding Shift while dragging a tangent handle shows a radial rotation gizmo. The page states Tangent Move needs the container's Edit Mode Type set to Bezier. The Splines 2.x search results suggest Edit Mode Type was later split into per-knot tangent mode, so this may be outdated (source 2).

What the user sees: a Component Editor Tools overlay in the Scene view with the three tools (source 1). Selecting knots or tangents opens the Element Inspector overlay (source 3), which sets knot position and rotation, tangent mode (Linear, Auto, Bezier; Bezier adds Mirrored, Continuous, Broken), In/Out tangent strength and direction, and tangent magnitude/direction. It also relates to linking/unlinking, splitting/joining knots and reversing flow (source 3).

What makes it confusing (engineering assessment from the documented behaviour, not a quoted complaint; no user complaints were fetched):
- Which action a click performs depends on the active tool. Click on a spline inserts only under Knot Move; under Knot Placement a click adds at the end.
- Tangents live in a third tool and used to depend on a container setting.
- Nothing in the documentation shows a visible hover cue for "click here inserts a knot".
- No width concept at all in the stock overlay; the road's thickness is elsewhere (our own tab).
- Hotkeys for the tools: none found in the manual pages opened (UNVERIFIED).

Praised: surface-snapped placement onto terrain/mesh, Element Inspector for exact numbers.

### 2.2 Unreal Engine landscape splines (source 4)

Steps (Landscape Mode > Manage > Spline):
1. Ctrl+LMB sets the first control point; each further Ctrl+LMB adds a point joined by a segment.
2. Select a control point and Alt+LMB drag to extend from either end of the spline.
3. Ctrl+LMB on a segment splits it and inserts a control point. Alt+LMB dragging a point through a segment also splits.
4. Ctrl+LMB on a different spline while one is selected joins them.
5. LMB selects a point or segment; Shift+LMB adds to the selection; Ctrl+A selects connected points or segments.
6. Del deletes selected points or segments.
7. Translate and rotate tools move points. The scale tool displays tangent handles: a selected segment shows tangents at both ends; a selected point shows tangents for all connected segments.
8. R auto-calculates rotation; T flips tangents; F flips segments (meshes only); End snaps selected points to the landscape below.

Width in the viewport: Half-Width is shown as solid lines; Side Falloff is shown as dotted lines (both per control point). Other per-point fields: left/right falloff factors, layer falloff factors, Layer Width Ratio, End Falloff. Control points are drawn as sprites (source 4). Colours are not specified.
Per-segment: Layer Name (paint layer) is on the segment (sources 4, 5).
Note: splines affect the heightmap only after a Spline edit layer is added to the edit layer stack (source 4). This is a classic silent-failure trap: the user draws a spline and nothing happens.
Unit note: the API reference says the width field is half-width (source 6 API entry); a forum answer says entering 4000 gives 4 m (source 7). These seem to disagree on the factor of two; check before copying.
Complaints: not researched beyond forum thread titles ("width affects segment mesh offset", "spline issues"); contents not opened.

### 2.3 Cities: Skylines

- Original game: road tool offers straight, curved and freeform modes plus an upgrade tool; a 90-degree snap exists in straight mode (source 8). Community description: freeform is the quick smooth curve, curved is the precise mode where you set length then width (source 9).
- Cities: Skylines II: more snapping options, a road guide system for difficult terrain, a Replace tool that works like Upgrade with freer changes, a reworked intersection system with easier placement rules (source 10). Each step cancels with right mouse button; Bulldoze removes finished roads (source 11).
- Not found: node-dragging rules and intersection guideline details.
- Takeaway: the tool is a mode-driven build palette (not a spline editor), but its good ideas transfer: visible snapping, cancel-by-right-click, one-click change of road class.

### 2.4 Blender curves

Verified only (sources 12, 13):
- Radius, Alt+S: "allows you to directly control the width of the extrusion along the spinal curve"; also available in the Sidebar Transform panel; radius interpolates point to point.
- Delete (X, Delete; Ctrl+X for dissolve): removes control points and refits the remaining segment by adjusting handles.
- Converting NURBS/Catmull-Rom to Bezier gives automatic handles; poly splines convert to vector handles; a closed curve adjusts handles only if they are Auto.
UNVERIFIED (pages opened did not contain it): Ctrl+RMB extrude, subdivide to insert, the V handle-type menu and the definitions of Automatic/Vector/Aligned/Free. Blender is a reference model for per-point radius and for point-wise width interpolation.

### 2.5 Godot Path3D

Official Path3D editing page was not found. Seen: a forum post says a select mode button is at the top of the editor (source 14); EditorNode3DGizmo docs say holding Shift raises secondary handles (in/out handles) above regular handles when they overlap (source 15); the Godot 3.2 Path2D source shows right-click removes a point in edit mode (source 16); a third-party addon uses Ctrl+click to create points and Shift+drag to add handles (source 17). Mode names and built-in shortcuts are UNVERIFIED.
Takeaway worth keeping: when two handles coincide, a modifier key switches priority rather than making one unreachable.

### 2.6 EasyRoads3D (V3 manual mirror, source 18; Quick Start, source 19)

Workflow: add a Unity terrain; GameObject > 3D Object > EasyRoads3D > New Road Network; create roads from the toolbar; flatten terrain in a later build step.
- Shift+Click adds markers; the road appears after two markers (source 19). Tip: place markers at roughly equal distances for smooth bends (source 19 search summary).
- Shift+Click+Drag starts a new marker from the selected end marker without snapping to ground (bridges).
- I inserts a marker (at the mouse position per Quick Start). Shift+R removes the selected marker or crossing.
- M free move, W position handle (press again to switch path-forward/horizontal), E tilt for selected markers. Ctrl+Click+Move snaps the marker to terrain height while moving.
- Shift+S selects all markers of a road, or all between two selected. F frames the selected marker. Space toggles the road highlight surfaces.
- Shift+J joins two roads (types must match) or splits a road at the selected marker. Shift+U disconnects from a crossing. P/O attach crossing/connection prefabs to an end marker.
- Width: General Settings "Road Width", per road; road types carry width (source 20). Per-marker width: not documented in sources seen.
Complaints: not researched. Observation: the shortcut list is long and each key is a separate memory item; this is exactly the discoverability problem the owner raised.

### 2.7 Road Architect (Unity, GitHub wiki, source 21)

- Ctrl+Left-Click adds road nodes on the terrain; Shift+Left-Click inserts nodes (insertion mode).
- Lane count per road; lane width in metres.
- Intersection: drag a node onto another node (same road or another road); not possible with consecutive nodes of the same road. Intersections are generated procedurally from lanes, width and angle.
- Compatibility: README states 5.5.0f3 for the original repo; forks exist. Not verified for Unity 6.
- Not to be confused with BeamNG's Road Architect, whose docs say click "Add a new spline road" then left-click creates nodes (source 22).

### 2.8 Gaea, World Machine, Houdini

- Gaea and Houdini curve-edit interaction: search returned nothing; UNVERIFIED.
- World Machine: its river device is driven by a drawn path plus channel width parameters; width varies relative to meanders (source 23, search summary). Interaction details not verified.
- The Massive World / Calysto docs ("Drawing rivers and lakes (beta)", sources 24) exist for a terrain tool but were not opened.

## 3. Patterns that make it usable

1. The line is always drawn when the road (or its tab) is selected: polyline of the centreline, knots as visible dots. Do not require entering a mode to see the curve (UE draws control points as sprites; Unity requires a tool).
2. One selection model: LMB selects a knot or segment, Shift+LMB adds (UE).
3. Insert by clicking the segment, with a modifier (UE Ctrl+click). Show a ghost knot under the cursor first (engineering practice; no source shows it).
4. Append by extending from an end (UE Alt+drag from an end; EasyRoads3D Shift+drag from the end marker).
5. Delete on the selected knot with Del (UE, Unity). Blender's delete refits handles so the curve stays smooth (source 13); do the same when removing a knot.
6. Move with a standard gizmo or direct drag; offer a terrain-snap toggle (UE `End` snaps to the landscape; EasyRoads3D Ctrl while moving).
7. Width is drawn in the viewport as two edge lines at the knot (UE: solid half-width lines) with falloff drawn differently (dotted). Width values belong to the knot and interpolate between knots (Blender radius interpolates, source 12).
8. Exact numbers in a small overlay or panel for the selected knot (Unity Element Inspector, UE Details), with the viewport as the primary route.
9. Whole-road settings (type, paint layer) are per segment or road (UE Layer Name per segment; EasyRoads3D width per road type); per-knot settings are limited to geometry.
10. Junctions: pick one explicit gesture (Road Architect: drag an end node onto another node; UE: Ctrl+click another spline to join) and generate the junction shape procedurally.
11. Cancel the in-progress action with right mouse button or Esc (Cities: Skylines II source 11; Unity Esc source 1).
12. Make a "nothing happened" state visible: UE splines do nothing until a Spline edit layer exists (source 4); show a warning in the panel in that kind of case.
13. Frame the selection (EasyRoads3D F) and select-all-along-road (Shift+S).
14. Handle priority modifiers when handles overlap (Godot Shift, source 15).

## 4. Anti-patterns

- Behaviour that depends on a hidden active tool (Unity: insert only under Knot Move).
- Tangent editing behind a third tool or a container setting.
- Long single-key shortcut lists as the only discovery path (EasyRoads3D has about 20).
- Silent no-ops (UE spline without edit layer).
- Per-road width only when the road needs to taper, or a width field with unclear units (UE half-width vs full width).
- No visible representation of the thing being edited (the owner's complaint about the generic tool).
- Ambiguous modifier semantics across tools (Road Architect: Ctrl adds, Shift inserts; EasyRoads3D: Shift adds, I inserts; UE: Ctrl inserts, Alt extends). Choose one set and show it on screen.

## 5. Recommendations for our tool

### 5.1 Where code may live
This is an engineering rule from the task brief, not from a source: package Editor assemblies must not use `Handles.` or `OnGUI`, but this game's `StitchPunk.Editor` assembly can. The Roads tab custom Scene-view tool therefore goes in StitchPunk.Editor and may use `Handles`, `HandleUtility`, `Event` and `EditorTool`/`SceneView.duringSceneGui`.

### 5.2 Proposed interaction spec

Select road: left click on a road's ribbon or centreline in the Scene view selects it (raycast the terrain, find nearest road within a pixel threshold with `HandleUtility.DistancePointToLineSegment`-style math in screen space). The selected road shows its centreline, a dot at each knot, and two edge lines at the current width. Unselected roads show a thin hover outline.
Hover: nearest knot enlarges and highlights; hovering a segment shows a ghost dot at the nearest point on the line and the cursor text "Click to add knot".
Click on segment: inserts a knot at that point (no modifier needed in the Roads tool; this is one deliberate difference from UE, which uses Ctrl). Rationale: the owner's expectation is "click on the line to add a knot".
Click on knot: select. Shift+click: add to selection. Drag knot: moves it along the terrain (terrain-snapped by default; hold Alt to move without snapping; Ctrl for grid snap). Marquee drag in empty space: box select.
Delete / Backspace: removes selected knots; refit tangents so the neighbours stay smooth (Blender behaviour, source 13). Refuse to go below 2 knots.
Extend: Shift+click empty terrain adds a knot after the selected end knot (or at the nearest end). Esc or right click ends.
Right click: context menu on a knot (Delete, Split road here, Set tangent: Auto/Linear/Smooth, Snap to terrain); on a segment (Insert knot here); on empty terrain (New road here, Deselect).
Ctrl+Z: standard Undo for every step (use `Undo.RecordObject` on the spline container and width data).
Esc: deselect, then exit tool.

Scene overlay (small, top-left or bottom of Scene view): tool mode toggle off by default, plus a one-line hint strip that changes with context: "Click line: add knot | Drag: move | Del: remove | Shift+click end: extend". The strip is the discoverability fix; EasyRoads3D's shortcut table lives in a manual, ours lives on screen.

Width editing:
- Whole road: slider and numeric field in the panel ("Width (m)"), plus a handle: two edge handles at the road midpoint that can be dragged outward.
- Per knot: when a knot is selected, drag its two side handles (dots on the edge lines at that knot) to change that knot's width; the panel shows "Width at this knot" with a "Match whole road" button. Widths interpolate between knots.
- State units as full width in metres (avoid the UE half-width ambiguity).
- Display: solid edge lines for road width; a dotted line outside for any shoulder/blend width if that concept exists (UE model, source 4).

Panel (UI Toolkit, Roads tab): road list; selected road header (name, type/style, total width, length, knot count); selected knot block (position, width, tangent mode); buttons for New Road, Delete Road, Split, Reverse, Snap all to terrain. A visible banner if baking is needed or a tile does not have the required layer.

Junctions: first version should use one gesture, as Road Architect does: drag an end knot onto another road's knot or segment to join (snap indicator appears), with the junction mesh generated. Defer if the ribbon builder cannot do T-joins.

### 5.3 What Unity Splines gives for free
- Spline data model (`SplineContainer`, knots, tangent modes Linear/Auto/Bezier and sub-modes, closed flag, reversing, linking), evaluation (`SplineUtility`, nearest point, `GetNearestPoint`), and undo-aware editing through the container.
- Its own tools (Knot Placement, Knot Move, Tangent Move) and the Element Inspector, which we should not expose to the user.
- A framework for customizing the standard editing tools (source 25: the package describes a framework for customizing editing tools). The exact customization API was not verified in this research.

### 5.4 What we must build
- A single custom `EditorTool` (or `duringSceneGui` handler) in StitchPunk.Editor that owns hover, ghost point, select, insert-by-click, drag, Delete, extend, right-click menu and the hint strip.
- Road picking in the Scene view (screen-space distance to the sampled centreline).
- Per-knot width storage and interpolation. Stock splines have no per-knot width in the documentation seen; options are `ISplineContainer` extra data (the package has per-knot/`SplineData<T>` style data, UNVERIFIED here) or a parallel array keyed by knot index. A parallel array breaks when knots are inserted unless the tool updates it; spline data keyed by distance survives insertion. Decide in the build spec.
- Width handles and edge-line drawing, with undo.
- Hide the stock spline tool for these containers so the user sees only one editing path.
- Junction handling.

### 5.5 Open questions for the team
- Do roads need per-knot width in the shipped style, or only taper at ends?
- Is full-width the unit stored, or half-width? State once in code and UI.
- Should insert-by-click require a modifier in the Roads tool? Recommendation above: no.

## 6. Sources

1. https://docs.unity3d.com/Packages/com.unity.splines@2.5/manual/getting-started-with-splines.html (opened)
2. Search results for Splines tangent modes and Draw Splines tool, including https://docs.unity3d.com/Packages/com.unity.splines@2.2/manual/select-tangent-mode.html and https://docs.unity3d.com/Packages/com.unity.splines@2.5/manual/select-default-tangent.html (search summaries only)
3. https://docs.unity3d.com/Packages/com.unity.splines@2.5/manual/element-inspector.html (opened)
4. https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-splines-in-unreal-engine (opened)
5. https://docs.unrealengine.com/4.26/BuildingWorlds/Landscape/Editing/Splines (search results only)
6. https://docs.unrealengine.com/5.1/en-US/PythonAPI/class/LandscapeSplineControlPoint.html (search results only)
7. https://forums.unrealengine.com/t/landscape-splines-width-in-meters/27786 (search results only)
8. https://forum.paradoxplaza.com/forum/threads/cities-skylines-dev-diary-1-roads.802639/page-2 (search results only)
9. https://cal1.lr.ggtyler.dev/r/CitiesSkylines/comments/fgr5as/perfect_sturns (search results only)
10. https://colossalorder.fi/?p=1547 (search results only)
11. https://admin-forum.paradoxplaza.com/forum/developer-diary/development-diary-1-road-tools.1590300/page-6 (search results only)
12. https://docs.blender.org/manual/en/dev/modeling/curves_new/editing/curves.html (search results only)
13. https://docs.blender.org/manual/it/2.91/modeling/curves/editing/curve.html (opened)
14. https://godotforums.org/d/20354-how-to-edit-pah (search results only)
15. https://docs.godotengine.org/en/4.2/classes/class_editornode3dgizmo.html (search results only)
16. https://grok.dragonflybsd.org/xref/dports/devel/godot-tools/godot-3.2.3-stable/editor/plugins/path_2d_editor_plugin.cpp (search results only)
17. https://github.com/Firepal/godot-polyliner (search results only)
18. https://nlp.fi.muni.cz/aiproject/ui/hutta_peter2019/pa026-project/Assets/EasyRoads3D/html/shortcuts.html (opened; unofficial mirror of the V3 manual)
19. https://nlp.fi.muni.cz/aiproject/ui/hutta_peter2019/pa026-project/Assets/EasyRoads3D/html/quick_start.html (search results only)
20. https://nlp.fi.muni.cz/aiproject/ui/hutta_peter2019/pa026-project/Assets/EasyRoads3D/html/general_settings.html (search results only)
21. https://github.com/MicroGSD/RoadArchitect/wiki/Documentation (search results only)
22. https://docs.beamng.com/world_editor/tools/road_architect/getting_started/ (search results only)
23. https://www.world-machine.com/blog/wp-json/wp/v2/posts/470 (search results only)
24. https://qwertystudio.gitbook.io/massive-world/drawing-roads-rivers-lakes-and-biomes/drawing-rivers-and-lakes-beta (search results only)
25. https://docs.unity3d.com/Packages/com.unity.splines@2.5/manual/index.html (opened)

Not verified: Gaea and Houdini interaction; Blender extrude, subdivide and handle types; official Godot Path3D controls; Unity Splines hotkeys beyond Delete/Esc/Ctrl+Z/Shift; Road Architect and EasyRoads3D complaints; Unity 6.x specifics (Unity Splines 2.5 docs were the newest opened); the UE width units; complaints about any tool (no user complaint sources were opened).
