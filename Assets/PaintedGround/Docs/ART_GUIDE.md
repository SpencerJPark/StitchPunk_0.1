# Painted Ground — Art Guide

What to paint, at what size, which pieces tile, and how each one gets used. Every texture in the
folder today is a generated placeholder; this is the list to replace them with.

## The two kinds of texture

**Tiling ("infinite loop") textures** repeat forever across the world. They must be seamless
(left edge continues into right edge, top into bottom) and must have **no landmarks**: no single
big rock, no distinctive dark blob. The shader already breaks up repetition (second rotated
sample, macro tint), but it cannot hide a landmark. Paint these as *texture*, not as *a picture*.

**Placed textures** appear once per object, so they can have all the personality they want.
This is where the hand-drawn charm lives: decals, cards, and the border strips.

| Texture | Kind | Size | Colour? | Used by |
|---|---|---|---|---|
| `T_Dirt` `T_Grass` `T_Forest` `T_Stone` | tiling, both axes | 1024–2048 sq | yes, sRGB | ground layers, projected top-down at 10–14 m |
| `T_Cliff` | tiling, both axes | 1024 sq | yes | cliff faces, projected sideways at 6 m |
| **`T_EdgeFringe`** | **tiles left↔right only** | 1024 × 1024 (4 rows × 256) | **no, data** | turf borders — grass blades etc. |
| `T_DecalAtlas` | not tiling | 2048 × 1024 (4 × 2 cells of 512) | yes + alpha | decals |
| `T_GrassTuft` (and other cards) | not tiling | 512 sq | yes + alpha | standing props |
| `T_PaperGrain` | tiling, both axes | 512 sq | no, data | post effect |
| `PaintedGround_Macro` `PaintedGround_Brush` | tiling, both axes | 512 sq | no, data | shader noise; you probably never repaint these |
| `<Island>_Splat` | not tiling | 1024 sq | no, data | signed-distance splat, baked by the tool. To hand-paint: paint a hard R/G/B mask, then Tools ▸ Encode Painted Splat Mask as SDF |

## How to make a seamless tile (the ground layers)

Any of these work; pick by tool:

- **Krita**: Wrap-Around Mode (`W`). Paint; the canvas wraps live. Best option, free.
- **Photoshop**: paint, then Filter ▸ Other ▸ Offset by half the size, fix the seam cross with
  the clone/heal brush, offset back. Repeat until the offset view shows no seam.
- **Procreate / Clip Studio**: paint, export, use a tiling checker; or paint bigger and crop
  with the offset trick in any editor.
- **Substance 3D Painter/Designer, Materialize**: automatic, but they push toward photo-real; use
  them for the base and paint over.

Rules for these four:
1. Mid-frequency brush texture everywhere, low contrast (value range about 0.35–0.65).
2. Keep the hue family tight. The macro tint in the shader adds the large-scale drift.
3. No object bigger than ~1/8 of the tile.
4. Paint at 2K, downscale if you need.
5. Match the world scale: at the default 12 m tile, one brush stroke should be 20–50 cm in the
   world, so about 20–40 px on a 1K tile. Check with the sample island; if strokes look like
   they belong to a giant, lower `Tile size` on the material or paint finer.

## The border strip — the one that makes grass look like grass

`T_EdgeFringe.png` is four horizontal strips. Each strip is *one turf's border, unrolled*:

```
 row 0  (R layer: grass)   ┌──────── inside the turf ────────┐  ← top of row
                           │ ╱╲ ╱╲╱╲  ╱╲ ╱╲╱╲╱╲ ╱╲ ╱╲╱╲ ╱╲╱╲  │  ← blades hang OUT over the neighbour
                           └──────── outside (neighbour) ────┘  ← bottom of row
 row 1  (G layer: forest)  leafy scallops
 row 2  (B layer: stone)   chips and cracks
 row 3  (cliff)            rock chunks
```

- **Tiles left↔right only.** Top and bottom must not tile; top must be fully solid, bottom
  fully empty.
- Channels are data, not colour: **R** = coverage silhouette (white = this turf), **G** =
  highlight rim painted just inside the silhouette (the lit blade edges), **B** = ink line just
  outside it. Import with sRGB **off**.
- The shader wraps the strip around every border in the world, whatever its curve. Paint
  straight; it bends.
- The strip is scaled like a tiling texture by `Fringe size` (default 3 m): 1024 px along the
  border = 3 m, and the 256 px row = 0.75 m across, so proportions you paint are what you get.
  A blade 40 px wide is ~12 cm in the world at size 3, ~24 cm at size 6. For the big, bold
  brush-stroke look of the reference: fewer, fatter blades in the strip AND a larger size.
- Easiest workflow: paint the silhouette in black on white; run `make_fringe.py`'s `bands()` on
  it, or in Photoshop use Layer Style ▸ Inner Glow (for G) and Outer Glow / Stroke (for B), set
  each to its own channel.
- Rows are 256 px tall; the strip's row order must match the material's layer order (R, G, B, cliff).

**Adding another turf** (say, cobble or snow): one splat channel + one tiling texture + one fringe row. The splat's alpha is free for a fourth; the lines to copy are the `_LayerBTex` / `gB` / row 2 lines in `PaintedGround.shader` and the matching node in `make_shadergraph.py`, and the strip needs a fifth row (`FRINGE_ROWS`). Ask me and I'll do it as a separate, optional variant.

## Decals

Decals are stamps projected down onto the ground: cracks, stains, scorch marks, puddles,
leaf litter, pebbles, moss. The atlas is a grid (default 4 × 2 of 512 px). Two blend modes:

- **Multiply** (cracks, stains, scorch): the RGB darkens whatever is underneath. Paint them as
  dark marks on white with alpha; they inherit the ground's lighting automatically.
- **Normal** (leaves, pebbles, puddles, moss): painted over. Give these an ink outline; they're
  objects.

Painting: 512 px cell, soft alpha edges, content in the middle 80 % of the cell (the shader
fades the last 10 % anyway). For a new atlas, change `Atlas grid` on the two decal materials.

**Which decals go where** is `Rules` at the top of `Editor/DecalScatter.cs`. Each rule is one
atlas tile with a weight per biome (dirt / grass / forest / stone), a size range and its blend
mode. The scatter tool reads the splat under each random point and rolls a weighted lottery, so
a tile with `onForest = 1.6, onStone = 0.1` almost never lands on the plateau. Set a weight to
0 to ban a decal from a biome. To add a biome, add a splat channel or a fourth weight column
— the tool reads vertex colours by barycentric interpolation, so any channel you paint works.

Hero decals (a big scorch under a fire pit, a ring of mushrooms) should be placed by hand:
duplicate any scattered decal, move, scale, set `tile` on its `PaintedDecal` component.

## Standing cards (props)

`Painted/Card` is for anything flat that stands up: ferns, mushrooms, signs, tall tufts. 512 px,
alpha, ink outline. Pivot at the bottom middle. Not for ordinary grass — see the border strip.

## Colour: what needs it and what doesn't

- **Colour (sRGB on):** the four ground layers, cliff, decal atlas, cards.
- **Data (sRGB off, no compression):** edge fringe, paper grain, macro, brush, baked splat.
  These are masks and noise; if sRGB is on they come out wrong (edges too thin, grain blotchy).
  The tools set this on import; if you re-import by hand, set it yourself.

Overall palette lives in the post effect (`M_PaintedPost` lift/gamma/gain) and the ground's
`Macro dark/light tint`, not in the textures. Paint the textures slightly desaturated and
neutral, and push the mood in the grade. That lets one set of textures serve every season.

## Editing the shaders in Shader Graph

The ground maths lives in `PaintedGroundCore.hlsl` as plain functions with no material
globals, and `PaintedGroundNodes.hlsl` exposes them as **reflected Shader Graph nodes**
(Unity 6.5's Shader Function Reflection API). Nothing to set up: with both files in the
project, open any Shader Graph and search the Create Node menu for **Painted/**:

| Node | Does |
|---|---|
| Painted Layer | world-projected tiling texture with the anti-repeat second sample |
| Splat Gradient | reads the baked splat map, gives weights + border directions |
| Painted Fringe | one turf with its painted border; chain one per layer |
| Painted Finish | macro tint, highlight rim, ink line |
| Painted Lighting | the flat ambient + shadow + campfire-pool lighting |

Build an **Unlit** URP graph: Position (Absolute World) ▸ split ▸ `.xz` into every node's
World XZ; Texture2D and SamplerState properties into the texture ports; chain three Painted
Fringe nodes (colour/ink/rim out → in) after the base layer; Painted Finish; multiply by
Painted Lighting; Base Color. For Painted Lighting add these Boolean keywords to the
blackboard as Global / Multi-compile so URP drives them: `_MAIN_LIGHT_SHADOWS`,
`_MAIN_LIGHT_SHADOWS_CASCADE`, `_MAIN_LIGHT_SHADOWS_SCREEN`, `_ADDITIONAL_LIGHTS`,
`_ADDITIONAL_LIGHT_SHADOWS`, `_CLUSTER_LIGHT_LOOP`, `_SHADOWS_SOFT`. Screen Position (Default)
into Screen Position.

Because the nodes call the same functions `PaintedGround.shader` calls, a graph built this way
renders the same picture; the graph is for when you want to rewire a stage as nodes (a new
noise, a snow layer, a different blend) without touching HLSL. If a texture port doesn't
appear on a node in your Shader Graph version, fall back to a Custom Function node (Type =
File, `PaintedGroundCore.hlsl`, Name = the function without `_float`) — same code.

Water, decal and fog are small hand-written shaders; say the word and I'll split them the same
way.

## Order to paint things in

1. `T_Grass` and `T_Dirt` (most of the screen).
2. `T_EdgeFringe` row 0, the grass blades. This is the biggest single win.
3. Decal atlas: leaves, cracks, one stain, one puddle.
4. `T_Stone`, `T_Forest`, `T_Cliff`, and their fringe rows.
5. Cards and the rest.
