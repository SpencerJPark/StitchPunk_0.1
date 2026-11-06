# Research: Brush, Paint, Decal and Scatter UX in Terrain/World Editors

Date: 2026-11-06. Scope: how shipping tools make sculpting, painting, decals and prop scattering usable, and what Stitch Punk's Terrain Editor should copy.
Source tags: [F] = page fetched and read; [S] = only seen in search-result excerpts; [U] = unverified, do not rely on it. Gaea / World Machine could not be verified (see section 6).

## 1. Summary

1. Every tool studied shows the brush in the viewport as a ring. Unreal draws two concentric circles: outer edge = size, inner = where falloff begins [F]. Polybrush has an explicit Inner and Outer radius [S].
2. Size and strength are never only slider-driven. Unity binds `[` `]` to size and `-` `=` to opacity [S]; Terrain Tools adds hold-key-and-drag-mouse (S size, A opacity, D rotation) [F]; Blender uses F and Shift+F drag-to-set [F, 2.80 page].
3. One inversion gesture everywhere. Unity Terrain: Shift lowers (Raise tool) [F]. Unreal: Shift inverts sculpt, paint and foliage-erase [F]. Terrain Tools: Ctrl inverts most brushes [F]. Blender: Ctrl toggles add/subtract [F]. Our rule: pick one (Ctrl or Shift) and show it in a hint bar.
4. Shift as "temporary smooth" exists in Terrain Tools [F]: smoothing is reachable without leaving the current tool.
5. Falloff is a named preset list plus a curve. Blender: Smooth, Sphere, Root, Sharp, Linear, Constant [S]. Unreal: Smooth, Linear, Sphere, Tip [F]. Polybrush: editable curve [S].
6. Paint targets are a list with name and thumbnail (Unreal Target Layers) [F]. Unity's first added layer flood-fills the terrain [F], a known surprise.
7. Flatten is the one mode that needs a visible target. Unreal draws a preview grid at the target height ("Show Preview Grid") [F].
8. Decals are objects with a gizmo box, not a brush. Unreal: translate/rotate widgets, non-uniform scale sets width, height and far-plane depth [F]. Unity URP: Scale / Crop / Pivot-UV edit modes, fade by draw distance and angle [F].
9. Scatter splits into two models: brush (Polybrush, Unreal Foliage; palette with thumbnails, size, density, Shift/Ctrl to erase) and rule-based (Gaia/Vegetation Studio class; only broadly described [S]).
10. Hotkeys are not discoverable by default. Unreal's brush size/falloff/strength hotkeys are unbound out of the box [S]. A permanently visible hint line beats any shortcut list.

## 2. Per-tool notes

### 2.1 Unity Terrain Inspector (built-in)
- Workflow: Terrain Inspector, Paint Terrain icon, choose tool; or Terrain Tools overlay with Sculpt Mode / Materials Mode [F: terrain-RaiseLowerTerrain, terrain-paint-texture].
- Raise or Lower: pick a brush from the palette, click-drag raises, hold Shift lowers [F]. Sliders: Brush Size, Opacity (100 = full, 50 = half) [F].
- Manual advice on brushes: soft-edged for rolling hills, hard-edged for cliffs and valleys [F].
- Paint Texture: Edit Terrain Layers adds layers; the first layer flood-fills the tile; brush size and opacity; you can paint across tile boundaries to blend [F]. Layers-list appearance is not described on that page [F].
- Default shortcuts (Unity 6.0/6.1 docs, remappable in Shortcuts Manager, Terrain category): `[` `]` size, `-` `=` opacity, `,` `.` cycle brushes [S].
- Viewport: not described in the pages read.
- Complaint evidence: none sourced. The shortcut set differs by version (A/S/D hold-drag in package 2.0/5.2 vs bracket keys in Unity 6.x docs) [S]; treat that churn as a usability signal, not a verified complaint.

### 2.2 Unity Terrain Tools package
- Brush Strength replaces "Opacity"; adds Brush Rotation, Scatter and Spacing; min/max per brush [F: brushes.html 5.3].
- Hold-key shortcuts in the Scene view while moving the mouse horizontally [F: brush-controls-shortcut-keys 5.2]: A = opacity, S = size, D = rotation, F = frame the Scene view on the cursor position over a tile, Ctrl = invert most brushes, Shift = temporary smoothing brush, Shift+A = Terrain Layer Eyedropper.
- Brush Mask Filters: a grayscale mask modulates strength (white full, black none); filters stack top to bottom, drag handle to reorder; terrain-based filters (Aspect, Concavity, Height, Slope) multiply [S: brush-mask-filters 5.0]. Example use: paint rock only where slope is steep.
- Terrain Toolbox: batch settings across tiles, heightmap/splatmap import and export [F: package index].

### 2.3 Unreal Engine Landscape (Sculpt, Paint, Foliage)
- Sculpt: tool tabs Sculpt, Smooth, Flatten, Ramp, Erosion, Hydro, Noise, Visibility, Mirror, Region Select, Copy [F]. LMB raises, Shift+LMB lowers, Ctrl+Z / Ctrl+Y undo and redo a stroke [F]. Works on the active Edit Layer, so edits are non-destructive [F].
- Brush: size measured in Unreal units including falloff. Falloff 0.0 = hard edge full effect; 1.0 = full effect only at the centre [F]. Brush types: Circle, Alpha, Pattern, Component, Gizmo [F]. Circle falloff types: Smooth, Linear, Sphere, Tip [F].
- Viewport: current brush size and falloff drawn as two concentric circles [F]. Brush size and strength can be set in the panel or in the toolbar above the viewport [S].
- Hotkeys: size/falloff/strength shortcuts exist under Editor Preferences, Keyboard Shortcuts, search "Landscape", but are unbound by default [S forum]. Ctrl+wheel strength / Shift+wheel falloff was a user request; no confirmation it exists [U].
- Flatten: Flatten Target, Use Slope Flatten, Pick Value Per Apply, Show Preview Grid (grid shows the target height) [F].
- Paint: target layers come from the landscape material, each shown with name and thumbnail; choose the active one before painting [F]. Tools Paint, Smooth, Flatten, Noise. LMB paints, Shift+LMB opposite [F]. Orphaned layers show a "?" icon [F]. Layers need a Layer Info Object; "Create Layers From Materials" and "Auto-Fill Target Layer Assets" automate setup [F]. Weight-blended layers reduce each other; non-blended work independently (snow overlay) [F].
- Foliage mode (Shift+3): tools Select, All, Deselect, Invalid, Lasso, Paint, Reapply, Single, Fill, Erase, Remove, Move [F]. Paint adds, Shift+paint erases; Brush Size; Point Density multiplies the per-type density; Erase Density sets how much remains when erasing; Ctrl multi-selects meshes; mesh list entries have a hover checkbox to enable/disable [F]. Per-type Radius and Scale in Foliage Details [F]. Thumbnails in the mesh list: page does not say [F].
- Praised/complained: only the forum complaint that brush size/falloff/strength live in a shared panel with no hotkeys [S].

### 2.4 Blender sculpt mode
- F + drag sets brush radius (pixels), LMB confirms, or type a number and Enter. Shift+F does the same for strength [F: 2.80 manual]. `[` and `]` resize [S, newer versions].
- Direction: Add / Subtract toggled with Ctrl while sculpting [F: 2.80].
- Falloff presets: Smooth, Sphere, Root, Sharp, Linear, Constant, plus Smoother, Sharper, Inverse Square [S: 2.90 falloff pages]. Constant gives a hard edge; Sharp gives a fine point.
- 2D Falloff turns the sphere of influence into a cylinder [F: 2.80]. Autosmooth sets smoothing per stroke [F: 2.80].
- Not verified: cursor display options and Stabilize Stroke wording. The current English page returned only navigation text [F attempt failed]. The headings "Stroke", "Falloff", "Cursor" exist in the current manual tree [F, nav].

### 2.5 Cities: Skylines (and 2)
- Terrain tools: Shift, Level, Soften, Slope; brush size and strength indicators at the left of the menu; one guide advises low-to-medium strength with a medium brush to start [S: guide results, likely CS1].
- CS1 Steam thread: keypad +/- size, Shift+keypad +/- strength [S, CS1 only]. A CS2 mod adds scroll-wheel size/strength [S]. No official CS2 hotkeys verified [U].
- Lesson only: a small, named tool set (shift, level, soften, slope) is easier to learn than a long list. I did not open these pages.

### 2.6 Decals
- Unreal decal actor: pick the decal material in the Content Browser, right-click in viewport, Place Actor; move/rotate with gizmo widgets; non-uniform scale widget controls width, height and far-plane distance of the volume; Sort Order controls stacking, higher on top [F]. Limits noted: deferred decals only, static objects only [F].
- Unity URP Decal Projector: Scale Mode (Scale Invariant / Inherit from Hierarchy), Width, Height, Projection Depth along local Z, Pivot, Draw Distance, Start Fade (fraction of draw distance), Angle Fade, Opacity, UV Tiling/Offset [F: projector-reference]. Scene-view edit buttons: Scale (resizes box and UVs together), Crop (resizes box, UVs fixed, so it crops or tiles), Pivot/UV (moves pivot without moving box) [F]. Atlas several decal textures into one material and pick with UV offset to keep batching [F: decal page]. No SRP Batcher; GPU instancing needs same material [F]. No projection on transparent surfaces [F].
- Gap: no source describes a paint-decals-with-a-brush workflow. Both engines treat a decal as an actor with a box gizmo.

### 2.7 Polybrush scatter (Unity)
- Steps: select mesh, Scatter mode icon, set radius/falloff/strength under Brush Settings (optional mirroring), drag prefabs into Current Palette and check them, select loadout slots (Shift multi-select), hover and left-click to paint [F: Polybrush 1.1].
- Settings: Use Pivot (default on), Hit Surface is Parent (default on), Avoid Overlap (default on) [F]. Ctrl+click erases the selected prefab [F]. Backspace removes a palette slot [F]. Preview Size slider for palette thumbnails [F].
- Brush has Outer Radius (effect 0%) and Inner Radius (100%) plus a Falloff Curve opened in the Unity curve editor [S]. Not documented on the page: random rotation/scale ranges, undo [F].

### 2.8 Gaia, Vegetation Studio, MegaWorld, Gaea, World Machine
- Gaia described as mixing procedural and manual work; Vegetation Studio Pro as rule-based vegetation plus a manual painting system; rule-set edits update the terrain directly [S, thin]. No workflow detail verified.
- Gaea and World Machine: searches returned unrelated tools (Fusion, Flame). Nothing verified; excluded from recommendations.

## 3. Patterns that make brush tools usable

1. Two-ring cursor. Outer ring = size, inner ring = where full strength ends (Unreal [F], Polybrush inner/outer [S]). The cursor must be visible before the first click.
2. Falloff as a preset row with a tiny curve icon, plus a numeric 0-1 value (Unreal: 0 hard, 1 centre-only [F]; Blender presets [S]). Presets first, raw curve editing second.
3. Size and strength reachable without moving to the panel: bracket keys for step changes [S Unity], hold-key-and-drag for continuous change [F Terrain Tools, Blender].
4. One invert modifier, same across Height, Paint and Scatter (Shift in Unreal [F], Ctrl in Blender/Terrain Tools [F]). Show the active state: cursor tint or icon change on invert.
5. Temporary smooth on a modifier key [F Terrain Tools]: you never need to switch tools to fix a lump.
6. Mode row as icon buttons with names: Raise/Lower, Smooth, Flatten (+ Ramp/Noise) [F Unreal]. Keep the list short (Cities: four tools [S]).
7. Flatten shows its target: preview grid at the target height [F]. Add a pick-height-from-terrain action (Unreal "Pick Value Per Apply", Terrain Tools eyedropper Shift+A [F]).
8. Layer palette with swatch and name, one active row highlighted (Unreal [F]). Pick-from-terrain eyedropper [F Terrain Tools]. Mark empty/orphan layers visibly (Unreal "?" [F]).
9. Mask the effect by terrain properties (height, slope, concavity filters multiply) instead of hand-painting every edge [S].
10. Stroke-level undo. Ctrl+Z reverts a whole stroke [F Unreal]. Edit-layer non-destructive editing is a stretch goal [F].
11. Decal handles do one job each: Scale, Crop, Pivot are separate modes in URP [F]; Unreal gives box scale in X/Y/depth [F]. Show the projection depth box, not only the image.
12. Scatter palette with thumbnails, checkbox per entry, hover affordances, Shift multi-select, erase by modifier [F Polybrush, Unreal].
13. Fade controls in the same inspector as the decal (draw distance, start fade, angle fade) [F].
14. A persistent hint line stating the current modifiers and shortcuts (derived from the finding that Unreal's shortcuts are unbound and invisible [S]).

## 4. Anti-patterns

1. Hotkeys that exist but are unbound or undocumented in-tool (Unreal brush size/falloff [S]).
2. Shortcut sets that change between versions (Unity A/S/D vs brackets [S]); pick once.
3. Brush properties hidden in a shared panel far from the cursor [S forum].
4. A first-layer flood fill with no warning (Unity Paint Texture [F]).
5. Inversion differing per tool (Shift in one place, Ctrl in another within the same product: Unreal uses Shift, Terrain Tools uses Ctrl [F]).
6. Layer or palette entries with no preview, so users guess from names (Unreal's mesh list: thumbnails not stated [F]).
7. Flatten that gives no sign of its target height.
8. Decal edit with no visible volume, so the user cannot tell what surface it projects on.
9. Opacity slider labelled differently across packages (Opacity vs Brush Strength [F]). Use one word.
10. Rule-based scatter with no preview: edits that only show after a full run (inferred from the "rule changes update terrain directly" claim being a selling point [S]).

## 5. Recommendations for Stitch Punk (interaction spec)

Context: tiles are 100 m, 101x101 height grid, 4-layer splat mask, decals, scatter props. Sizes below are proposals, not measured values.

### 5.1 Global (all tools)
- One tool strip, icon plus word: Height, Paint, Decals, Scatter. Active tool highlighted. Mirrors Unity's Terrain Tools overlay idea [F].
- Hint bar fixed at the bottom of the Scene view and window: current tool, modifier state ("Ctrl: lower", "Shift: smooth"), size/strength keys. Always on, not a tooltip.
- Brush cursor ring always visible when the mouse is over a tile; outside a tile, show a "no tile here" cursor.
- Shortcuts (Unity-compatible so muscle memory transfers): `[` `]` size, `-` `=` strength; plus hold-S-and-drag size and hold-A-and-drag strength [F]. Register them as Unity Shortcut Manager entries so they are remappable [S].
- Ctrl = invert in every tool; Shift = smooth in Height, erase in Scatter/Decals (decision: use Ctrl for invert as Terrain Tools and Blender do [F]).

### 5.2 Height
- Modes (icons): Raise/Lower, Smooth, Flatten, Noise (optional later). Ctrl inverts Raise; holding Shift smooths from any mode [F Terrain Tools].
- Controls: Size (m), Strength (%), Falloff preset row (Smooth, Linear, Sphere, Tip, Constant) plus a numeric hardness 0-1 [F Unreal falloff names].
- Cursor: two rings, outer = size, inner = hardness. A thin height readout (metres) under the cursor and the target height for Flatten.
- Flatten: show a translucent plane or grid at the target height [F Unreal]; click-with-Alt (or an eyedropper button) to sample the height under the cursor. Alt is our proposal, not a copied shortcut.
- Tile edges: strokes must write into all tiles under the ring and keep shared border samples equal (Unreal's Component brush has "Include Border" to touch shared border pixels [F]). Show a seam warning if neighbouring border rows differ.
- Undo: one undo step per stroke.
- Live result: update the terrain mesh as the stroke moves, not on mouse-up.

### 5.3 Paint (4-layer splat)
- Layer palette: four large rows, each with a texture swatch, name, and a coverage bar. Click selects the active layer, active row highlighted [F Unreal]. Empty layers shown dimmed. A "Pick layer from terrain" eyedropper [F Terrain Tools: Shift+A].
- Brush: Size, Strength, Falloff, same cursor and shortcuts as Height. Ctrl = erase (reduce weight of the active layer) [F Unreal Shift-opposite].
- Weights must stay normalised across the four layers (Unreal weight-blended behaviour [F]).
- Optional masks later: slope/height limits using multiplied masks [S Terrain Tools filters].
- Warn before any operation that flood-fills a layer [F Unity].

### 5.4 Decals
- Place: choose a decal from a thumbnail list; click on terrain to drop; it is created selected (Unreal: place actor then adjust with gizmo [F]).
- Select: click a decal in the Scene view or in a list panel that shows thumbnail, name, tile. List is the "what do I have" view; clicking a row frames it (like Terrain Tools F-to-frame [F]).
- Handles: move, rotate (yaw), and a box with width, height, and projection depth drawn as a wireframe volume (Unreal scale widget [F]; URP Projection Depth [F]). Three explicit modes: Scale, Crop, Pivot as in URP [F], or simplified to Scale and Crop.
- Properties in the same panel: Opacity, Draw Distance, Start Fade, Angle Fade [F URP]. Sort order if layering is needed [F Unreal].
- Delete: Delete key on the selected decal; Ctrl+click erases with a brush-style tool if added.
- Visibility: selected decal outline plus projection box, unselected decals optionally show only their outline (toggle "Show decal bounds").
- Atlas multiple decal images into one material to keep batching [F URP].

### 5.5 Scatter
- Start with brush mode (matches Polybrush and Unreal Foliage [F]): palette of prop prefabs with thumbnails and checkboxes, size, density, falloff; click-drag paints, Ctrl erases [F Polybrush, Unreal].
- Density control: "Point Density" multiplier over each prop's own density, and an "Erase Density" if partial erase is wanted [F Unreal].
- Placement options as checkboxes: align to surface normal, avoid overlap (on by default) [F Polybrush], random yaw, scale range, height/slope limits.
- Preview: ghost instances inside the brush ring before the click, or show instances live while dragging; undo per stroke.
- Rule-based scatter (by height, slope, splat layer) as a second phase: expose as "Fill tile from layer" with a seed and a visible re-roll button. No source detail verified for Gaia/VSP, so this part is our own design [U].

### 5.6 Unity gives vs we build
- Unity gives: Shortcuts Manager remapping [S]; the Terrain Tools package's brush controls, mask filters, eyedropper and mouse-drag hotkeys for built-in Terrain [F] (only useful if our data were a Unity Terrain, which it is not); URP Decal Projector and its Scale/Crop/Pivot edit gizmo and fade properties [F]; Polybrush scatter on meshes [F]; UI Toolkit controls and the Scene-view overlay system.
- We build: the brush cursor (rings, hardness, invert tint) over our own 101x101 grid; height/splat strokes and cross-tile border handling; flatten target plane; layer palette with coverage bars; decal list and selection handles over our own decal data (unless decals become DecalProjector objects, which would give the box gizmo free); scatter brush and preview; hint bar; per-stroke undo; live mesh/texture updates.

## 6. Sources

Fetched and read:
- https://docs.unity3d.com/6/Documentation/Manual/terrain-RaiseLowerTerrain.html
- https://docs.unity.com/en-us/engine/current/manual/creating-environments/script-terrain/terrain-textures/terrain-paint-texture
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.2/manual/brush-controls-shortcut-keys.html
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.3/manual/brushes.html
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.0/manual/index.html
- https://docs.unity.com/en-us/engine/6000.0/manual/visual-effects/decals/renderer-feature-decal/projector-reference
- https://docs.unity.com/en-us/engine/6000.0/manual/visual-effects/decals/renderer-feature-decal/decal
- https://docs.unity3d.com/Packages/com.unity.polybrush@1.1/manual/modes_prefab.html
- https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-sculpt-mode-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-paint-mode-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-brushes-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/landscape-flatten-tool-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/foliage-mode-in-unreal-engine
- https://dev.epicgames.com/documentation/en-us/unreal-engine/decal-actors-in-unreal-engine
- https://docs.blender.org/manual/en/2.80/sculpt_paint/sculpting/tool_settings/brush.html

Seen in search results only:
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.0/manual/brush-mask-filters.html
- https://docs.unity3d.com/Packages/com.unity.polybrush@1.0/manual/modes_prefab.html
- https://docs.unity3d.com/Packages/com.unity.terrain-tools@5.2/manual/brush-controls-shortcut-keys.html (Unity 6 bracket keys come from the Unity 6.0/6.1 terrain-UsingTerrains result, https://docs.unity.com/en-us/engine/6000.0/manual/creating-environments/script-terrain/terrain-using-terrains)
- https://docs.blender.org/manual/de/2.90/sculpt_paint/brush/falloff.html (falloff presets; English text paraphrased by search tool)
- https://forums.unrealengine.com/t/scultpting-painting-brush-size-hotkey/310708 (unbound hotkeys)
- https://primagames.com/tips/how-to-terraform-your-city-in-cities-skylines-2
- https://steamcommunity.com/workshop/filedetails/discussion/502750307/3845556684647234293 (CS1 hotkeys)
- https://www.nexusmods.com/citiesskylines2/mods/159 (CS2 scroll-wheel mod)

Not verified: Blender stabilize stroke and cursor display options; Unity Terrain scroll-wheel behaviour; Unreal strength hotkeys; Gaea, World Machine, Gaia, Vegetation Studio workflows; Cities: Skylines 2 official hotkeys; what the viewport shows in Unity's built-in terrain brush.
