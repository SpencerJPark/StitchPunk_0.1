# The Cutouts tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Cutouts tab, right
after Flipbooks.

Builds a tight mesh around a flipbook's or an image's art, so a part stops
paying for the empty corners of its quad.

---

## Why a tighter mesh

A flipbook part renders on a quad, and every transparent pixel of that quad
still runs the fragment shader. The shipped `ToolkitSpriteUnlitArray` is
opaque plus alpha clip: one texture sample and one discard per empty pixel, in
every pass. Each part is already one instanced draw across the whole crowd, so
a tighter mesh adds no draws and cuts fragment work in proportion to the area
it removes. The asset bar's "41% of quad" readout is that saving.

## The sidebar

Three modes:

- **Cutouts** — the saved cutouts.
- **Flipbooks** — pick one to start a cutout. Picking a flipbook that already
  has a cutout opens it instead of starting a second.
- **Images** — any texture: a single image, or a whole atlas sheet. An image is
  a one-frame source, so the frame stepper and All frames are hidden.

There is one shape per flipbook, because every layer of an array is the same
size. An image's cutout always spans the whole texture (UVs 0–1): for an atlas,
draw the outline around the part you want on the full sheet, and use the atlas
texture itself in the material.

## The canvas

A 2D front view in world units: a 1-unit major grid with a 0.1 minor grid. The
grid is a fixed ruler; the art sits on it at a **Location**, the world position
of its origin. Location is editor-only and is never baked into the mesh.

View: the wheel zooms at the cursor, middle-drag or Alt+drag pans, and **F**
frames the art. The rail toggles show or hide the shape, the origin and the
reference.

**Size** is Pixels / unit, Unity's sprite meaning (default 100). The shape is
stored in the frame's pixel space, so it stays on the art when you rescale.

**All frames** draws every frame's art as a ghost. The footer names any frame
whose art pokes outside the shape, and by how many pixels.

## Object mode

The default. The outline is drawn thin, with no vertex handles and no
wireframe.

- **Move the art:** drag its body. Ctrl snaps to the 0.1 grid.
- **Scale the art:** drag a corner of its frame. The opposite corner stays put.
  This is Pixels / unit, and the mesh scales with the art because it is cut to
  it. Ctrl rounds to a whole number.
- **Move the pivot:** drag the ⊕ origin. Only the pivot moves; the art and the
  grid stay still. The origin can go anywhere, even off the art.

**Zero** (Origin card) puts the origin back on the grid's centre, carrying the
art with it, like zeroing an object's location in Blender.

## Reference image

Any `Texture2D`, drawn under the art with an opacity slider. In Object mode,
drag it to move it where the art is not, and drag its top-right corner (or use
the Position and Size fields) to size it. It keeps its world size when you
change Pixels / unit, so matching the art to the reference is how you size the
art. It is stored on the cutout and never baked into the mesh.

## Editing the shape

The shape starts as the full quad. Turn on **Edit mesh** in the rail (or press
**Tab**) to show the triangle wireframe and a **Vertex | Edge** switch in the
canvas header (keys **1** / **2**).

**Vertex**

- Drag a vertex to move it.
- Double-click the outline to add a vertex.
- Select a vertex and press Delete to remove it. A shape never drops below 3.
- Points snap to whole pixels. Hold Ctrl to snap to the 0.1 grid instead.

**Edge**

- Drag from one vertex to another to draw an edge. The triangles must follow
  it, so it is how you force a seam through a concave shape.
- Click a drawn edge and press Delete to remove it. Outline edges are the shape
  and cannot be removed.
- An edge that leaves the shape, crosses the outline or crosses another drawn
  edge is refused, and the footer says why. An edge that a later vertex move
  invalidates is kept but ignored, with a warning in the footer.
- Adding a vertex renumbers the edges, deleting one drops its edges, and Fit to
  art clears them. The Shape card shows the **Edges** count and **Clear edges**.

Concave shapes are fine; the rest of the shape is ear clipped around your
edges. An outline that crosses itself is not, and it blocks Save with the
reason in the footer.

**Fit to art** traces every frame's alpha, takes the convex hull, grows it by
**Padding** (default 2 px, enough for bilinear and mip bleed) and reduces it to
the **Vertex budget** (default 8). Treat it as a starting point and edit by
hand from there. Hand edits may go concave.

## Origin

The origin is the mesh's (0, 0, 0), so the part rotates about it. Put it on the
joint. Drag the ⊕ in Object mode or type values: **Pivot (px)** is the origin
in the frame's pixels, **Location** is where it sits on the grid, and
**Preset** places the pivot at the centre, bottom centre or a corner. The saved
mesh's (0, 0, 0) is always the origin; Location never reaches it.

## Facing and normals

**Facing** is −Z (toward a default camera, like Unity's built-in Quad) or +Z.
Whether the back face draws is a material setting, not a mesh one.

**Normals** are Flat, or Rounded with a **Roundness** of 0–1. At 1, each
outline normal bends 45° away from the shape's centre so a lit part shades like
a soft form. Every vertex is on the outline, so the middle of the face is
interpolated; there are no interior vertices.

UVs come from position (pixel ÷ frame size). Nothing stretches, and the same
UVs work for every slice of the array.

## Saving

The Output card has a **Name** above a **Folder**. **Save Mesh** writes the
`Mesh` asset to `<Folder>/<Name>.asset` and a `CutoutAsset` beside it as
`<Name>_Cutout.asset`. The default Name is the flipbook's name; the Folder is
remembered per machine. The cutout holds the flipbook, Pixels / unit, outline,
drawn edges, origin, location, facing, normals and reference, so reopening it
restores the session.

Saving again rewrites the same mesh asset and keeps its GUID, so every
`MeshFilter` using it updates. Renaming a saved cutout moves the mesh asset on
the next Save and keeps the GUID, so those MeshFilters keep working.

## What this tab does not do

It does not assign the mesh to a rig part; the Materials tab owns part
renderers. It does not build several islands into one mesh, and it does not
subdivide for bending or VAT.
