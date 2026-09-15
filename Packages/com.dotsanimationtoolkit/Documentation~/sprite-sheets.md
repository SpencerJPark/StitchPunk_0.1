# The Sprite Sheets tab

**Window ▸ DOTS Animation Toolkit ▸ DOTS Animator** — the Sprite Sheets tab.

Stacks same-size frames into one `Texture2DArray` and names every layer, so a
sprite key picks a frame by name instead of a typed index.

---

## Why there is no atlas builder

A slice key selects a `Texture2DArray` layer, and no shipped shader can
address a grid cell from a slice index. An atlas rect only remaps UVs on a
fixed quad, so every frame of a part must already share one canvas size —
which is exactly what an array layer already is.

## Workflow

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

Bake writes `T_<SheetName>_Array.asset` beside the sheet by default, or to
the path chosen with the **…** button, and overwrites an existing array in
place so anything already pointing at it keeps pointing at it. The sheet
asset itself is written only by **Save** — a **●** marks unsaved changes, and
switching sheets while it shows asks before discarding. Baked arrays are
uncompressed RGBA32, and mips are generated from each layer automatically.

## Contact sheet

Every frame appears as a thumbnail in layer order. Hovering a thumbnail names
its frame and layer, and clicking one selects its row in Frames. A Zoom
slider resizes the thumbnails.

## Using a sheet in the Clip Editor

On a sprite track, choose the sheet in the **Sheet** field; the **Frame**
dropdown then picks a frame by name instead of a number.

- An `Absolute` key stores the frame's layer directly.
- A `RelativeToBase` key stores the frame's layer minus the track's base
  index, and **Base Frame** picks that base by name.
- The raw **Index** field stays visible but read-only while a sheet is
  bound.
- `AtlasRect` tracks show the Sheet field disabled, labelled "sheets bind
  Slice tracks" — atlas rects are a different addressing scheme and a sheet
  has nothing to bind there.

The sheet reference is authoring-only and is never baked into clip data —
a bound track's keys still store plain layer numbers, exactly as an unbound
one would.

## Material rule

The part's material must sample an array. Use
`ToolkitSpriteUnlitArray.shadergraph`, or any material with a `_MainTexArray`
slot, and drag the baked array into that slot by hand — the tab never edits
materials for you.
