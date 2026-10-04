# Amendment A111F — Cutouts: object/edit modes, art on a fixed grid, drawn edges

> **Status:** 📝 specced 2026-10-04 from the owner's first look at A111 (0.63.0); every decision below answered by the
> owner the same day — buildable. Ships as **0.64.0**.
> **Why:** "the grid itself is for measuring, so it is useless if I can't scale and move the flipbook around." In
> A111 the grid was mesh space: the art was nailed to grid zero and dragging the origin panned the grid. The owner
> wants Blender's model: a fixed grid, an object you move and scale on it, a free pivot that zeroes back to the
> centre, and an Edit mode where they draw the triangulation's edges themselves.

## 1. Decisions (owner, 2026-10-04 — do not re-ask)

- **F-D1 — The grid is a fixed world ruler.** View pan/zoom are unchanged (wheel, middle drag, Alt drag, F). The art
  has a **location** on the grid: `CutoutAsset.artPositionWorld` = the world position of the origin. Pixel → world =
  `artPositionWorld + (pixel − originPixels) / pixelsPerUnit`. Editor-only; never baked.
- **F-D2 — Object mode / Edit mode split** (owner picked the Blender split). A rail toggle **Edit mesh** (key Tab).
  - **Object**: drag the art's body to move it; drag one of the four frame corners to scale it uniformly with the
    opposite corner held still (this *is* Pixels / unit — the mesh is cut to the art, so it scales with it); drag the
    origin; the reference drags/resizes where the art isn't. Outline drawn thin, no vertex handles, no wireframe.
  - **Edit**: wireframe of the generated triangles; a **Vertex | Edge** segmented control in the canvas header
    (keys 1 / 2), the owner's choice of Blender's select modes.
    - **Vertex**: drag a vertex to move it, double-click the outline to add one, Delete removes one (never below 3).
    - **Edge**: drag from one vertex to another to draw an edge; click a drawn edge to select it, Delete removes it.
      Outline edges are the shape and cannot be removed.
- **F-D3 — Drawn edges constrain the triangulation** ("they will auto gen tris"): every valid drawn edge
  (`CutoutAsset.innerEdges`, pairs of outline vertex indices) is kept as a triangle edge, and the rest is ear-clipped
  around them. An edge that is adjacent, leaves the shape, crosses the outline or crosses an earlier edge is refused
  when drawn (footer says why); one that a later vertex move invalidates is kept but ignored, with a footer warning.
  Inserting a vertex renumbers the edges; deleting one drops its edges; Fit to art clears them.
- **F-D4 — Free pivot + Zero** (owner): dragging the origin moves only the pivot — art and grid stay still (the
  location compensates) — and it may go anywhere, outside the art included. **Zero** (Origin card) sets the location
  to (0, 0): the art jumps so the origin sits on grid centre, like zeroing an object's location in Blender. The saved
  mesh's (0,0,0) is always the origin.
- **F-D5 — Inspector**: Origin card gets **Location** (world X/Y fields) and a **Zero** action; the pivot field is
  renamed **Pivot (px)**. Shape card gets **Edges** (drawn count) and a **Clear edges** action.
- **F-D6 — Output Name** (owner, 2026-10-04: "add a name in the output node above the mesh save path"): the Output
  card shows **Name** (a text field) above **Folder** (the path row, now a folder picker). The mesh is written as
  `<Folder>/<Name>.asset` and the cutout beside it as `<Name>_Cutout.asset`; the default Name is the flipbook's name.
  Renaming a cutout whose mesh already exists renames that mesh asset on Save (`AssetDatabase.RenameAsset`), so its
  GUID — and every MeshFilter using it — survives. `outputPath` stays the stored truth (folder + name).

## 2. Tasks

- **Wave 1 [parallel]**
  - [x] T1 — `CutoutAsset.cs` (+ `artPositionWorld`, `innerEdges`), `CutoutEnums.cs` (+ `CutoutCanvasMode`).
  - [x] T2 — `PolygonTriangulator.cs` (constrained triangulation, edge validation, index upkeep) +
    `PolygonTriangulatorTests.cs` (`DrawnDiagonal_IsKeptAsATriangleEdge`, `CrossingEdge_IsRefused`).
  - [x] T3 — `CutoutMeshBuilder.cs` (an overload taking the drawn edges).
- Gate, fixtures, commit.
- **Wave 2 [parallel, one pinned contract]**
  - [x] T4 — `CutoutCanvasElement.cs` (location mapping, modes, wireframe, corner handles, rubber band, keys).
  - [x] T5 — `CutoutShapeManipulator.cs` (per-mode gestures).
  - [x] T6 — `CutoutInspectorColumn.cs` (Location + Zero, Pivot rename, Edges + Clear edges, Output Name + Folder).
  - [x] T7 — `CutoutsPanel.cs` + `CutoutMeshWriter.cs` (Edit toggle, Vertex | Edge header control, wiring, status).
- Gate, fixtures, commit.
- [ ] T8 — Orchestrator: changelog 0.64.0, docs, drive + captures (Object: move/scale/pivot/zero; Edit: vertex and
  edge modes, a drawn diagonal survives Save into the mesh's triangles), R01–R22 audit.
- [ ] T9 — ⏸ Owner checkpoint: the gestures by hand.

## 3. Log

- 2026-10-04: F-D6 (Output Name) added mid-session by the owner.
- 2026-10-04: specced from the owner's notes on 0.63.0; F-D2 (split), F-D3 (edges, not vertices, in Edit — "drag
  from one vert to another to make an edge"), Vertex | Edge sub-modes and F-D4 (free pivot + Zero) answered in-session.
- 2026-10-04 wave 1 (T1–T3, three workers): gate PASS, 9/9 Cutouts fixtures; revert-to-fail in the Editor — making TryTriangulateWithEdges ignore its edges fails DrawnDiagonal_IsKeptAsATriangleEdge (and only it).
- 2026-10-04 wave 2 (T4–T7, four workers on one pinned contract): gate PASS, lint PASS, Editor recompile clean, EditMode 899/899.
