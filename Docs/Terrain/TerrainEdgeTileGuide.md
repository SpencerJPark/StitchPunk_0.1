# Terrain Edge Tiles: drawing guide

> **Status (2026-11-25): spec only.** The packer, the table asset and the shader path described here are planned in
> `Assets/_Vault/Tasks/Plans/TerrainEdgeTiles_System.md` and not built yet. The drawing rules and the templates are
> final, so you can start drawing now.

This guide covers how biome edges are drawn, where the pieces line up, how to draw a new edge set, and how to give
two specific biomes their own edge.

---

## 1. The idea in one minute

The ground is split into a **2 m grid**. Every grid **corner** holds one biome (Grass, Dirt, Forest, Stone...).
The game draws one tile in each cell. A tile looks at its 4 corners and picks one of **16 cases**:

```
 corners -> case               bit values
  G . . . G                      1 ---- 2        1 = top-left
  .       .    all grass           |  |          2 = top-right
  .       .    = case 15 (full)    |  |          4 = bottom-right
  G . . . G                      8 ---- 4        8 = bottom-left
                                                  top of the tile = world north (+Z)
  G . . . G
  .  grass.    top two grass, bottom two dirt
  .-------.    = 1 + 2 = case 3 (an edge piece)
  D . . . D
```

You do **not** draw 16 tiles. You draw **5 pieces**, and the packer rotates them into the other cases:

| Piece | Canonical case | Becomes (rotated 90 deg clockwise each step) |
|---|---|---|
| Outer corner | 1 (top-left only) | 2, 4, 8 |
| Edge | 3 (top half) | 6, 12, 9 |
| Inner corner | 7 (all but bottom-left) | 14, 13, 11 |
| Diagonal | 5 (top-left + bottom-right) | 10 |
| Full | 15 | (no edge art; see 3.4) |
| (empty) | 0 | nothing drawn, the lower biome shows |

Templates (open these in your paint program as a guide layer):
- `Docs/Terrain/EdgeTile_Template_5Pieces.png`: 5 cells, 256 x 256 each, left to right in the order above.
- `Docs/Terrain/EdgeTile_Template_16Case.png`: all 16 cases in a 4 x 4 grid (case `c` at column `c % 4`, row `c / 4`,
  row 0 at the top). Use this only when you want to hand-draw every case instead of letting the packer rotate.

In the templates: **black dots** = corner inside the top biome, **white dots** = outside, **grey** = the straight
reference shape, **red dots** = connection points, **blue box** = the 8 px safe margin.

---

## 2. What you draw

An edge set is **one biome drawn over another**: the "top" biome (turf, like your `edgesSample.png`) is opaque,
the rest of the tile is transparent and the lower biome shows through.

- **Alpha** = where the top biome covers. This is the silhouette.
- **Colour** = what you see there. Draw the fill in the top biome's flat colour, and draw ink flicks, rim
  highlights, wet edges or pebbles in other colours.
- The packer **replaces the flat fill colour with the real world texture**, and keeps everything that differs from
  it (the ink and details). That is what makes a full cell and an edge cell meet with no visible square. So:
  - keep the fill one flat colour (the set's **fill key**, auto-picked as the median colour of the Full piece);
  - anything you want kept must be clearly different from that colour (default tolerance 0.12);
  - flicks may hang **outside** the silhouette over the lower biome (as in your sample): they stay ink only.

Size: **256 x 256 px per cell = 2 m**, so 128 px per metre. Ink strokes at least **3 px wide**, or they vanish
when the game shrinks the texture (Switch imports at 128 px).

---

## 3. Lining up: the rules every piece must follow

These rules make any piece sit next to any other piece, any variant, any rotation. The packer checks them and
names the piece and side that breaks one.

### 3.1 Connection points: the edge crosses a side at its middle

Where a side's two corners differ (one inside, one outside), the silhouette **must cross that side at the
midpoint, 128 px**, and cross it **straight** (at a right angle) for the last 16 px before the side.

```
   side of the tile
   |
   |      ink and wobble are free in here
   |            ~~~~~~~~~~
   |  ~~~~~~~~~/          \~~~~~
   ===========                  ======  <- last 16 px: straight, exactly at 128 px
   |
```

Between the two sides the edge can wander as much as you like (the template's straight line is only a
reference). Wobble is what makes it look hand-drawn.

### 3.2 Solid sides stay solid

Where a side's two corners are the same, that whole side is **fully opaque** (both inside) or **fully
transparent** (both outside) along its full length, in the outer 2 px. Otherwise a thin line or notch appears where
two cells meet.

### 3.3 Safe margin: keep ink 8 px off the sides

Ink flicks, highlights and details stay inside the blue box (8 px from every side). The only exception is the
silhouette itself at a connection point. A flick that crosses a side gets cut in half by the neighbour cell.

### 3.4 The Full piece

Fully opaque, flat fill colour, no edge art. You may add a few sparse interior details, but they repeat on every
full cell, so keep them faint or leave it plain. Interior texture belongs in the biome's ground texture, not here.

### 3.5 Diagonal: the top biome does NOT connect

In the diagonal piece the two top-biome corners stay **separate blobs** and the lower biome runs between them. The
shader relies on this to layer 3 biomes in one cell without holes; do not join them.

---

## 4. Making it look good

- **Draw 2 variants of each piece** (up to 4). The game picks one per cell at random, which breaks up repetition
  along long straight borders. Variants must all follow section 3, so any mix lines up.
- **Flicks point outward** from the top biome, as in `edgesSample.png`: turf hanging over the edge.
- **Rotation is automatic, light is not.** A 5-piece set is rotated, so do not paint a light direction into it.
  If an edge facing the camera (south side) needs to look different, draw that case by hand (section 6).
- **Wobble in the middle, straight at the ends.** The best-looking pieces swing well away from the template
  line in the middle and return to the connection point.
- **Avoid one memorable feature** (a unique loop, a big pebble). It will repeat every few metres.
- **Small islands are handled for you.** Generation removes single-corner speckle and rings, so you do not need
  art for tiny noise blobs.

---

## 5. Making a new edge set (planned workflow)

1. Copy `EdgeTile_Template_5Pieces.png`, draw on a layer above it, hide the template, and export a **transparent
   PNG** 1280 x 256. Name it `T_Edge_<Top>Over<Bottom>_v0.png` (e.g. `T_Edge_GrassOverStone_v0.png`); variant 2 is
   `_v1`. Put it in `Assets/PaintedGround/Textures/EdgeTiles/`.
2. Create the set: Project window, **Create > Stitch Punk > Terrain > Edge Tile Set**. Add one variant per PNG,
   layout **Five Pieces**. Leave Fill Key at auto unless the packer reports the wrong fill colour.
3. Hook it up in the **Biome Edge Table** (`TerrainBiomeEdgeTable` asset referenced by the active palette):
   - as a biome's **Default Edge Set**, used whenever that biome sits over anything without a pair override; or
   - as a **Pair Override** (Upper = Grass, Lower = Stone), used only for that exact meeting.
4. Press **Pack** on the table (or `Stitch Punk/Terrain/Edge Tiles/Pack Edge Tiles`). Read the console: any piece
   that breaks section 3 is listed by name and side.
5. Look at it in the Scene view. No rebake is needed; the packed textures are global.

---

## 6. Hand-drawing every case (16-case layout)

Use `EdgeTile_Template_16Case.png` (1024 x 1024) and set the variant's layout to **Sixteen Cases**. Every case is
used exactly as drawn, with no rotation. Do this when a set must not rotate: a lit rim, a south-facing lip, a
directional pattern. Cases 0 and 15 are still drawn (0 empty, 15 full), and the rules in section 3 still apply.

---

## 7. How specific biomes meet each other

Two things decide what a border looks like.

**Draw order** (in the Biome Edge Table). The higher biome is drawn over the lower one and owns the edge art.
Default: Dirt 0, Grass 1, Forest 2, Stone 3. Where 3 biomes meet in one cell, the lowest is drawn first, then
each higher one with its own edge over everything below it:

```
  Dirt (0) fill -> Grass (1) edge over Dirt -> Stone (3) edge over Grass
```

**Which art is used** for "Upper over Lower":
1. A **pair override** for exactly (Upper, Lower) if one exists;
2. otherwise Upper's **default edge set**.

So you need one default set per biome (4 sets for today's 4 biomes), then add pair overrides only where a meeting
should look special: "Grass over Stone" with roots over the rock, "Forest over Grass" with fallen needles.

**Adjacency rules** (also in the table) control which biomes may touch at all in generated ground. A row
"Forest / Stone / buffer Grass / width 1" makes generation insert one corner of Grass wherever Forest would touch
Stone. Painted ground is never changed by these rules.

---

## 8. Troubleshooting

| You see | Cause | Fix |
|---|---|---|
| A step or gap where two cells meet along an edge | Crossing not at 128 px, or not straight in the last 16 px | Section 3.1 |
| A thin line or notch along a cell side | A solid side is not fully opaque or transparent | Section 3.2 |
| Half a flick, cut off | Ink inside the 8 px margin | Section 3.3 |
| A square of slightly different colour inside the turf | Fill not one flat colour, or wrong fill key | Flatten the fill; set Fill Key by hand |
| Your ink is missing | Ink too close to the fill colour | Darken it, or lower the tolerance |
| Same shape repeating along a border | One variant only | Add `_v1` |
| Grey smear or grid lines when zoomed out | Strokes thinner than 3 px | Thicken strokes |
| Stone and grass edge look identical | No pair override, so Stone's default set is used | Add a pair override (section 7) |

---

## 9. Examples

`Docs/Terrain/EdgeTileExamples/` holds four generated example sets in the 5-piece layout (Grass over Dirt,
Forest over Grass, Stone over Dirt, Stone over Grass as a pair override), two variants each. They pass every rule
above; copy one as a starting point. Rendered results: `Assets/_Vault/Tasks/Captures/EdgeTiles_2026-11-25/`.
Regenerate with `python Tools/terrain_edge_mock/edge_tile_mock.py <capture_dir> <example_dir>`.

## 10. Reading `edgesSample.png` (your reference)

That sheet uses the 47-tile "blob" scheme (each tile looks at 8 neighbours), so its tiles cannot be dropped in
directly. To reuse it: take its straight edge tile, an outer corner, an inner corner and a full tile, re-cut them
onto the 5-piece template so each edge crosses at the side midpoints (section 3.1), and draw a diagonal from two
outer corners. Its look (flat turf, dark flicks hanging outward, no outline on the lower side) is exactly what this
system is built for.
