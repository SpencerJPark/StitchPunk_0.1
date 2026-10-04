# Amendment A111F — Cutouts: object/edit modes, art on a fixed grid, drawn edges

> **Status:** ✅ built 2026-10-04 as 0.64.0 (T1–T8); ⏸ T9 owner checkpoint — the gestures by hand.
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
- **F-D7 — Images as a source** (owner, 2026-10-04: "I could possibly make one off an atlas texture that wouldn't need
  the flipbook option, or a single image"; atlas scope answered **whole texture only**): the sidebar becomes
  **Cutouts | Flipbooks | Images**; Images reuses `ImageCatalogColumn` (every `Texture2D`). A texture is a one-frame
  source spanning the whole image (0–1 UVs; for an atlas, draw the outline around the wanted part on the full sheet).
  `CutoutAsset.flipbook` (already `UnityEngine.Object`) holds it. Frame stepper and All frames hide for one frame; the
  inspector card reads **Source**. Ships as **0.65.0**.

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
- [x] T8 — Orchestrator: changelog 0.64.0, docs, drive + captures (Object: move/scale/pivot/zero; Edit: vertex and
  edge modes, a drawn diagonal survives Save into the mesh's triangles), R01–R22 audit.
- [ ] T9 — ⏸ Owner checkpoint: the gestures by hand.
- **F-D7 wave [parallel]**: [x] T10 `CutoutAsset.cs` (source helpers) · [x] T11 `CutoutAlphaTracer.cs` + fixture
  (`ReadSourceMasks`) · [x] T12 `CutoutInspectorColumn.cs` + `CutoutCatalogColumn.cs` (Source card, one-frame rows) ·
  [x] T13 `CutoutsPanel.cs` + `CutoutMeshWriter.cs` (Images mode, texture source). Then gate, drive, 0.65.0.

## 3. Log

- 2026-10-04: F-D6 (Output Name) added mid-session by the owner.
- 2026-10-04: specced from the owner's notes on 0.63.0; F-D2 (split), F-D3 (edges, not vertices, in Edit — "drag
  from one vert to another to make an edge"), Vertex | Edge sub-modes and F-D4 (free pivot + Zero) answered in-session.
- 2026-10-04 wave 1 (T1–T3, three workers): gate PASS, 9/9 Cutouts fixtures; revert-to-fail in the Editor — making TryTriangulateWithEdges ignore its edges fails DrawnDiagonal_IsKeptAsATriangleEdge (and only it).
- 2026-10-04 wave 2 (T4–T7, four workers on one pinned contract): gate PASS, lint PASS, Editor recompile clean, EditMode 899/899.
- 2026-10-04 T8 drive (HeadArray; captures `Library/A111Captures/a111f_*.png`): location (0.5, 0.25) moves art and
  origin together; scaling 100 → 50 ppu holding pixel (0,0) keeps that corner at exactly (−0.78, 0.25); moving the
  pivot to (300, 40) — off the art — leaves the art's top at (1.78, 5.37) and moves only the Location; Zero puts the
  origin on (0, 0); the working copy follows ppu and Location. Edit/Edge: drawn edge 1–5 accepted, 0–3 refused "That
  edge crosses another edge."; Save → the mesh's 6 triangles include edge 1–5. Name "HeadPart" → `HeadPart.asset` +
  `HeadPart_Cutout.asset`; rename to "HeadPiece" + Save → both renamed, **mesh GUID kept**. Scratch deleted.
  Captures: Object mode shows corner handles, thin outline, off-art ⊕ (R01–R22 clean); Edit/Edge shows the wireframe,
  the drawn edge in Accent and the Vertex | Edge control in the canvas header (R11: segmented ≠ tab list ✓).
  **Not proven, for T9:** every pointer gesture (art drag, corner scale, pivot drag, double-click insert, vertex→vertex
  edge drag, Delete) and the Tab / 1 / 2 keys were driven through canvas/panel methods, not real pointer or key events —
  the drawn rubber band and Tab-not-moving-focus are unverified. Editor was unfocused for these captures (45-repaint
  grab). Pixels / unit field and corner-scale share one value, so the inspector field updates as you drag.
- 2026-10-04 F-D7 wave (T10–T13, four workers): gate PASS, 27/27 Cutouts + conformance fixtures; revert-to-fail in
  the Editor — dropping ReadSourceMasks' Texture2D branch fails ReadSourceMasks_SingleImage (count 0, not 1). Drive:
  Images mode → BaseHead.png (194×240) loads as 1 frame, Fit to art 8 verts, Save → mesh UVs inside 0–1 on the whole
  texture. Owner's mid-wave note: Fit to art / Clear edges moved from the Shape card header to a row under Padding.
  Capture found the three-mode sidebar header wrapping its buttons onto a second row (R06/R07) → sidebar default
  260 → 310pt under a new split key. Shipped 0.65.0.
