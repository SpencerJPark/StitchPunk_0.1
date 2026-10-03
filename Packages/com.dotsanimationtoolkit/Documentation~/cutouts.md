# The Cutouts tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Cutouts tab, right
after Flipbooks.

Builds a tight mesh around a flipbook's art, so a part stops paying for the
empty corners of its quad.

---

## Why a tighter mesh

A flipbook part renders on a quad, and every transparent pixel of that quad
still runs the fragment shader. The shipped `ToolkitSpriteUnlitArray` is
opaque plus alpha clip: one texture sample and one discard per empty pixel, in
every pass. Each part is already one instanced draw across the whole crowd, so
a tighter mesh adds no draws and cuts fragment work in proportion to the area
it removes. The asset bar's "41% of quad" readout is that saving.

## The sidebar

Two modes:

- **Cutouts** — the saved cutouts.
- **Flipbooks** — pick one to start a cutout. Picking a flipbook that already
  has a cutout opens it instead of starting a second.

There is one shape per flipbook, because every layer of an array is the same
size.

## The canvas

A 2D front view in world units: a 1-unit major grid with a 0.1 minor grid. The
wheel zooms at the cursor, middle-drag or Alt+drag pans, and **F** frames the
art. The rail toggles show or hide the shape, the origin and the reference.

**Size** is Pixels / unit, Unity's sprite meaning (default 100). The shape is
stored in the frame's pixel space, so it stays on the art when you rescale.

**All frames** draws every frame's art as a ghost. The footer names any frame
whose art pokes outside the shape, and by how many pixels.

## Reference image

Any `Texture2D`, drawn under the art with an opacity slider. Drag it to move
it, and drag its top-right corner (or use the Position and Size fields) to size
it. It keeps its world size when you change Pixels / unit, so matching the art
to the reference is how you size the art. It is stored on the cutout and never
baked into the mesh.

## Editing the shape

The shape starts as the full quad.

- Drag a vertex to move it.
- Click an edge to insert a vertex.
- Select a vertex and press Delete to remove it. A shape never drops below 3.
- Points snap to whole pixels. Hold Ctrl to snap to the 0.1 grid instead.

Concave shapes are fine; the mesh is triangulated by ear clipping. An outline
that crosses itself is not, and it blocks Save with the reason in the footer.

**Fit to art** traces every frame's alpha, takes the convex hull, grows it by
**Padding** (default 2 px, enough for bilinear and mip bleed) and reduces it to
the **Vertex budget** (default 8). Treat it as a starting point and edit by
hand from there. Hand edits may go concave.

## Origin

The origin is the mesh's (0, 0, 0), so the part rotates about it. Put it on the
joint. Drag the ⊕ or type pixel values; **Preset** places it at the centre,
bottom centre or a corner.

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

**Save Mesh** writes a `Mesh` asset at the Output path (remembered per machine)
and a `CutoutAsset` beside it. The cutout holds the flipbook, Pixels / unit,
outline, origin, facing, normals and reference, so reopening it restores the
session.

Saving again rewrites the same mesh asset and keeps its GUID, so every
`MeshFilter` using it updates.

## What this tab does not do

It does not assign the mesh to a rig part; the Materials tab owns part
renderers. It does not build several islands into one mesh, and it does not
subdivide for bending or VAT.
