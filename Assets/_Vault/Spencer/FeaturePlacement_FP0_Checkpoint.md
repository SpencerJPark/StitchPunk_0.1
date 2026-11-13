# FP-0 checkpoint: Terrain overlay and hidden-tab gating (2026-11-13)

Spec: `Tasks/Plans/FeaturePlacement_System.md`. FP-1 does not start until you have done this.

## Before you start
- Restart the Editor once if the console shows `Burst internal compiler error ... Error while hashing`. That is the old Burst cache fault, not this change.
- Open `Assets/Scenes/TerrainWorld.unity` and make sure the tile subscenes are open (they reopen themselves for about 10 s after a load).
- Keep the Terrain Editor docked as a tab next to the Inspector, as you normally have it.

## Steps
1. Click the **Terrain Editor** tab and open **Features**. A floating panel titled **Terrain** appears in the Scene view. It shows "Features" and one keycap row per key: Click, Enter, Esc, Backspace, Delete, Ctrl+Click.
   - Check: every row is readable, nothing is cut off, and no Unity overlay covers it. The old strip along the bottom and the line at the top left are gone.
2. Click a recipe tile to start drawing an area. The panel adds a muted line: "Drawing an outline. Shift+click makes a sharp corner; click the first point to close."
   - Click two or three points on the ground. They should land where you click.
3. Drag the panel by its header to wherever you want it, or dock it to a Scene view edge. Unity remembers the spot. If you want a different default spot, tell me (FP-Q6).
4. While you are still drawing, click the **Inspector** tab.
   - Check: the Terrain panel disappears. Your half-drawn outline is gone, because hiding the tab cancels it.
   - Click the ground in the Scene view. It should select objects normally and add no points.
   - Select an existing area GameObject. No knot handles should appear; it is just a GameObject in the Inspector.
5. Click the **Terrain Editor** tab again. The panel comes back with the Features keys.
6. Optional: try the **Roads** tab and a **Paint** brush, hide the tab mid-stroke, and check the brush circle and road handles disappear too.
7. Optional: draw an outline that crosses itself and try to close it. The warning "Outline crosses itself — move or remove a point" should appear in the panel and fade after about 3 s.

## Tell me
- Pass, or what looked wrong (a screenshot is best). I could not take a pixel capture this session because the Editor never had OS focus. The layout was measured instead: nothing clips, text is 13/12/11 px, and rows are 24 px.
- Where you left the panel (FP-Q6).
