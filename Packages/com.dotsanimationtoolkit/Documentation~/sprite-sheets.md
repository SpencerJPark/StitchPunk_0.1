# The Sprite Sheets tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Sprite Sheets tab.

Names every layer of a `Texture2DArray` so a sprite key can pick a frame by
name instead of a typed index. Every array already in the project shows up
here on its own, and the tab can also stack loose images into a new array.

---

## Why there is no atlas builder

A slice key selects a `Texture2DArray` layer, and no shipped shader can
address a grid cell from a slice index. An atlas rect only remaps UVs on a
fixed quad, so every frame of a part must already share one canvas size —
which is exactly what an array layer already is.

## The Sheets list

Every `Texture2DArray` in the project appears in the list, whether or not the
tab has touched it. A PNG imported with Texture Shape set to "2D Array" and
flipbook rows/columns counts too.

A row reads its frame count and layer size, plus a status:

- `64 frames · 64×64 · imported, unnamed` — an array the tab has never named.
- `64 frames · 64×64 · imported` — an array that has a names asset.

Right-click a row for **Rename** and **Delete**. Both act on the array asset
itself.

## Opening an array: the contact sheet

Opening a row shows its layers as a contact sheet, one thumbnail per frame,
named `0`, `1`, `2` … by default. Thumbnails are copied on the GPU, so a
compressed, non-readable array still previews correctly.

The importer owns layer order, so the tab does not let you change it here:
**Bake**, the output path, adding images, drag-reorder, and removing frames
are all disabled while viewing an existing array. **Filter**, **Wrap**,
**Mips**, and **Linear** show the array's own import settings, read-only.

## Naming frames

Double-click a frame's name to rename it. Names stay unique — renaming a
second frame to `mouth` when one already exists gives it `mouth 1` instead.

**Save** stays disabled until at least one frame has a name other than its
own number. The first Save writes a small `<ArrayName>_Sheet.asset` beside
the array, holding only the frame names — nothing about the array itself
changes, and no material is touched.

A **●** marks unsaved name changes, and switching sheets while it shows asks
before discarding.

## Re-importing an array

If the source PNG is re-imported with more layers, opening the sheet again
adds the new frames with numeric names. If it comes back with fewer layers,
the trailing names are dropped and the tab warns how many frames were lost.

## Using a sheet in the Clip Editor

On a sprite track, the **Sheet** field accepts either a saved sprite sheet or
a `Texture2DArray` directly. Dropping in an array reuses its existing names
sheet if one exists, or creates one. The **Frame** and **Base Frame**
dropdowns then list frame names where they exist, and numbers otherwise.

- An `Absolute` key stores the frame's layer directly.
- A `RelativeToBase` key stores the frame's layer minus the track's base
  index, and **Base Frame** picks that base by name.
- The raw **Index** field stays visible but read-only while a sheet is
  bound.
- `AtlasRect` tracks show the Sheet field disabled, labelled "sheets bind
  Slice tracks" — atlas rects are a different addressing scheme and a sheet
  has nothing to bind there.

The sheet reference is authoring-only and is never baked into clip data — a
bound track's keys still store plain layer numbers, so renaming a frame never
changes an animation.

## Building an array from loose images

Use this when the frames start as separate textures instead of an existing
array.

- **New** asks for a name and a folder before creating the sheet asset.
- Drag images from the **Images** column into **Frames**, or double-click an
  image to add it.
- Drag rows in Frames to reorder them — list order is layer order, and the
  `#` number beside a row is the layer that frame bakes to.
- Set **Filter** (Point by default), **Wrap** (Clamp by default), **Mips**,
  and **Linear** for the baked array.
- **Bake**, then **Save**.

Every frame must match the first frame's size; Bake refuses and names every
offender rather than baking a mismatched array. Source textures do not need
Read/Write enabled.

Bake composes the frames into one grid PNG, `T_<SheetName>_Array.png`,
written beside the sheet by default or to the path chosen with the **…**
button, row by row from the top-left. If the frame count doesn't fill a full
grid, the leftover cells are padded with transparent layers — padding never
shows up as a frame. The PNG is then imported as a `Texture2DArray`.
Re-baking rewrites the same PNG in place, so its GUID and every reference to
it stay put. The sheet asset itself is written only by **Save** — a **●**
marks unsaved changes, and switching sheets while it shows asks before
discarding.

### Matching import settings

**Match import settings of** picks one of the project's existing arrays;
Bake then copies that array's compression, max size, filter, wrap, mips,
sRGB, and per-platform overrides — everything except rows and columns.

Left empty, Bake falls back to the settings the project's arrays already
share: Compressed (normal quality), mipmaps on, Point filter, Clamp, sRGB,
max size 2048, no crunch — with the sheet's own Filter, Wrap, Mips, and
Linear fields layered on top. There is no uncompressed option.

## Contact sheet zoom

A Zoom slider resizes the thumbnails in the contact sheet, whether you're
viewing an existing array or frames staged for Bake.

## Material rule

The part's material must sample an array. Use
`ToolkitSpriteUnlitArray.shadergraph`, or any material with a `_MainTexArray`
slot, and drag the baked array into that slot by hand — the tab never edits
materials for you.
