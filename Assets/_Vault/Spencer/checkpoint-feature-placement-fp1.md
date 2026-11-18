# FP-1 owner checkpoint: draw fences, tree rows and your own feature

Spec: `Tasks/Plans/FeaturePlacement_System.md` (FP-1). About 10 minutes. Nothing here needs code.

## Before you start
1. Open `Assets/Scenes/TerrainWorld.unity`. Make sure the Editor window has focus (the overlay and the drawing only react when it does).
2. Open the Terrain Editor (dock it beside the Inspector as usual) and click the **Features** tab (the tree icon in the left rail).
3. Open a tile to draw on: World tab, open tile (1, 2) (a hill) or any tile you like.

## 1. Wooden fence, straight
1. Features tab: click **New feature** (top right). A picker opens with Areas / Lines and fences / Ground cover.
2. Pick **Wooden Fence**. The Scene-view overlay should show the shape row (Curve, Straight, Box, Circle, Polygon) with **Straight** selected; Box and Polygon greyed out.
3. Click three or four points on the ground. Press **Enter** (or double-click the last point) to finish.
4. Look: posts every 2.5 m standing on the ground, rails stretched between neighbouring posts, tilted with the slope.

## 2. Curved fence with a gate
1. New feature, Wooden Fence again. Press **1** (or click Curve in the overlay) and click a curved line of points; Enter.
2. With the new fence selected, **Alt+click** one of its knots (the round handles). That knot's segment turns red dotted: it is a gap.
3. Look: the rails starting in that stretch are gone, the posts stay. Alt+click it again to close the gap.
4. Also try: drag a knot (the fence follows after a moment), Ctrl+click on the line to insert a knot, select a knot and press Delete.

## 3. Tree row
1. New feature, **Tree Row**. Draw a curve; Enter.
2. Look: a tree every ~6 m along the line, on the ground.

## 4. Your own feature
1. New feature, last row **Create recipe...**, choose **Along a path** (or Fill an area / In rows), save it anywhere.
2. The new recipe asset is selected in the Project window. In its Inspector: set **Placer** values (spacing, mode Points or Segments), and add one entry to **Placeables** with any prefab you like (Tier: Interactable). For Segments, also set **Segment Prefab**.
3. New feature, pick your recipe, draw it.

## 5. Narrow window check
The Terrain Editor at your docked width is 177 points wide. Read the Features tab (list, selected card, picker): is everything readable? Long names end in "..." and show the full text on hover.

## Also check
- **Forest** still draws and fills like before (it was converted in place to the new recipe type).
- Click the Inspector tab: the overlay disappears and clicks in the Scene view no longer add points (FP-0 still holds).

## Known limits (by design for FP-1, not bugs)
- The fence outline and the shape you are drawing are drawn straight between knots, so on a hill the line can sink into or float over the ground. Draped outlines, moving a whole feature live, and editing single trees are the next phase (FP-L).
- Grass/wheat-style card placeables on lines and rows do not show yet (they need card sources, FP-3).
- A gate gap removes the rails that START on the gap stretch; a rail that crosses the knot into the gap stays.

## Tell me
- Does anything feel wrong while drawing (which key, which click)?
- Is the overlay's shape row where you want it?
- Anything in the Features tab you cannot read at your width?
