# Painted Ground for Unity 6 (URP)

Hand-painted, non-repeating 3D terrain in the Don't Starve style, plus matching flat painted water.
Written for Unity 6.1 and later (tested against the 6.3–6.5 URP API).

## Folder layout

```
PaintedGround/
  Shaders/            the six .shader files + PaintedGround.shadergraph (the ground as a graph)
    Include/          PaintedGroundCore.hlsl   the maths, one function per stage
                      PaintedGroundGraph.hlsl  Custom Function entry points the .shadergraph uses
                      PaintedGroundNodes.hlsl  the same functions as reflected nodes (Unity 6.5 node menu)
  Textures/
    Layers/           T_Dirt, T_Grass, T_Forest, T_Stone, T_Cliff   (tiling, colour)
    Edges/            T_EdgeFringe                                  (the painted turf borders, data)
    Decals/           T_DecalAtlas
    Cards/            T_GrassTuft
    Noise/            PaintedGround_Macro, PaintedGround_Brush, T_PaperGrain (data)
  Runtime/            PaintedDecal, CampfireFlicker, PaintedDecalEntities
  Editor/             the Tools > Painted Ground menu items
  Materials/          created by the tools (M_PaintedGround, M_PaintedWater, decal, fog, post...)
  Generated/          created by the tools (island mesh, splat map, card quads)
  Docs/               this file, ART_GUIDE.md
  Tools/TextureGenerators/  the Python that made the placeholder textures
```

## What's in the folder

| File | What it is |
|---|---|
| `PaintedGround.shader` | The ground shader (`Painted/Ground`) — thin wrapper over the core |
| `PaintedGround.shadergraph` | **The ground as a Shader Graph** (URP Unlit): 7 Custom Function nodes on `PaintedGroundGraph.hlsl`, all properties on the blackboard with the same reference names as `Painted/Ground`, lighting keywords declared. The builder makes `M_PaintedGround_Graph` from it; swap it onto the Island to compare. |
| `PaintedGroundCore.hlsl` | The ground maths as plain functions (layer projection, splat gradient, painted fringe, finish, lighting) |
| `PaintedGroundNodes.hlsl` | The same functions as reflected Shader Graph nodes (Unity 6.5+), under Painted/ in the node menu |
| `ART_GUIDE.md` | What to paint, sizes, which textures tile, decal/biome rules, Shader Graph route |
| `Editor/SplatBaker.cs` | Bakes vertex-colour splats to a world texture (smooth border directions) |
| `PaintedWater.shader` | The water shader (`Painted/Water`) |
| `PaintedDecal.shader` | Projected decal shader (`Painted/Decal`), multiply or normal blend |
| `T_DecalAtlas.png` | 4×2 atlas of painted decals: crack, crack, stain, scorch / puddle, leaves, pebbles, moss |
| `PaintedDecal.cs` | Per-decal component (tile, opacity, tint) |
| `Editor/DecalScatter.cs` | Menu items that scatter decals over any ground collider, biome-aware |
| `PaintedCard.shader` + `T_GrassTuft.png` | Alpha-cut painted cards with wind sway (`Painted/Card`) |
| `PaintedFog.shader` | Drifting, depth-faded ground fog on flat cards (`Painted/Fog`) |
| `PaintedPost.shader` + `T_PaperGrain.png` | Paper grain, vignette and biome grade for URP's Full Screen Pass feature (`Painted/Post`) |
| `Editor/CliffSkirtBuilder.cs` | Finds cliff lips on a mesh and lines them with grass cards |
| `Editor/FogCardScatter.cs` | Scatters fog cards, weighted toward forest |
| `Editor/PostSetup.cs` | One click adds the post effect to your URP renderer |
| `PaintedDecalEntities.cs` | Entities Graphics components + baker for decals (compiles only with `PAINTED_ENTITIES` defined) |
| `PaintedGround_Macro.png` | Tileable noise: R = macro tint, G = patch mask for the anti-tiling sample |
| `PaintedGround_Brush.png` | Tileable dry-brush noise used for edge wobble and water ripple |
| `T_EdgeFringe.png` | **The painted turf borders.** Four strips (grass blades, forest scallops, stone chips, cliff chunks): R = silhouette, G = highlight rim, B = ink line. This is what makes grass look like grass. |
| `T_Dirt / T_Grass / T_Forest / T_Stone / T_Cliff.png` | Placeholder painted layers (1K, seamless). Replace with your own art. |
| `Editor/SampleIslandBuilder.cs` | Menu item that builds the test island |
| `CampfireFlicker.cs` | Tiny flicker script for the campfire light |
| `make_noise.py`, `make_layers.py`, `make_decals.py`, `make_cards.py` | Python that generated the textures, if you want different seeds |

## Install (5 minutes)

1. Drop the whole `PaintedGround` folder anywhere under `Assets/`.
2. Make sure the project uses URP (Edit ▸ Project Settings ▸ Graphics ▸ Default Render Pipeline is a URP asset).
3. In the **URP Asset** (select it in Project Settings ▸ Graphics, or Quality):
   - **Depth Texture: ON**, mode **After Opaques** (the default) — the water reads it for the shoreline and the decals project through it. Without it the water is one flat colour and the decals don't show.
   - **Rendering path: Forward+** (on the *Universal Renderer Data* asset the URP asset points to). Forward also works, but with the 8-lights-per-object cap.
   - Shadows on, with at least one cascade, if you want the main light shadow slider to do anything.
4. Menu **Tools ▸ Painted Ground ▸ Build Sample Island**. That creates the materials, the island mesh, water, a campfire, a moon light, the camera, ~220 scattered decals plus a scorch mark under the fire, grass tufts along every cliff lip, and fog cards through the forest.
5. Menu **Tools ▸ Painted Ground ▸ Add Painted Post Effect to Renderer**. Adds a Full Screen Pass Renderer Feature with `M_PaintedPost` (paper grain, vignette, grade) to your active URP renderer and turns Depth Texture on.
6. Press Play. Walk the camera around with the scene view.

The builder also fixes texture import settings (Repeat wrap, sRGB off on the two noise maps). If you add textures by hand, set those yourself.

## Reading the test island

Everything was placed to exercise a specific feature:

- **West beach** – the ground slopes gently under the water. Look at the white shoreline stroke and the ragged foam band behind it (`Painted/Water` ▸ Shoreline). Tilt the camera: the stroke stays the same width, because the shader converts view depth to vertical depth.
- **Meadow with campfire** – grass over dirt with a worn path. Look at the border: it is drawn as blades poking out over the dirt, with a light rim and an ink line, straight from the `T_EdgeFringe.png` strip (`Painted/Ground` ▸ Painted edges). Where the meadow meets the stone plateau the same border is made of chips instead. The fire is a point light with no N·L, so it makes a soft pool instead of a hotspot (`Lighting ▸ Point light boost`).
- **Plateau, north-east** – vertical faces switch to the side-projected cliff texture (`Cliffs`). The stone top is the B splat channel.
- **Forest, south** – the G channel. Darker, cooler; good for checking the macro tint against a dark layer.
- **Rocky outcrop, east** – cliff faces meeting the water directly, so shoreline and cliff overlap.
- **Forest fog** – flat fog cards 35 cm above the ground, drifting, fading where they cut the terrain (`FogCards` under the island).
- **The frame** – with the post effect on, the whole image gets paper grain (stronger in shadow), a ragged vignette, and a slight desaturation. Turn `Grain strength` to 0 to see how much it was doing.
- **Decals everywhere** – cracks and pebbles gather on stone, leaf litter in the forest, puddles and stains on the dirt path, moss on the shaded side. They fade off the cliff faces on their own.

Walk 30–40 m in any direction and look for repeating features. There shouldn't be any: the second rotated sample kicks in by patches (`Anti tiling`), and the macro tint drifts over ~140 m.

## Using it on your own terrain

**Mesh:** any mesh works. World-space projection means chunks need no UV work and no seams. Cliffs need real geometry (vertices on the face) so the normal is steep enough to trigger the cliff layer; a 1–2 m band of dense vertices along cliff edges is plenty. Unity's built-in Terrain works too, but it has its own material system — render it through a mesh export or use a custom terrain shader with the same fragment code.

**Splat weights** — two options on the material:
- *Vertex Color* (default). RGB = grass / forest / stone weights, black = base dirt. Paint with any vertex-colour tool (Polybrush is free from the Package Manager) or generate them in code, as the sample does.
- *World Texture*. One PNG for the whole map, placed by `World rect` (min X, min Z, size X, size Z). Paint it as a hard mask (R/G/B = which turf), then run **Tools ▸ Painted Ground ▸ Encode Painted Splat Mask as SDF** on it and assign the `_SDF` result. The shader reads the baked map as a *signed distance field* (0.5 = border, ±4 m = 0/1), which is what keeps borders crisp and blade directions stable; a raw mask would collapse the fringe to one texel. The sample builder does the same thing automatically from vertex colours.

Weights are not lerped: the border sits where the weight crosses 0.5, and the shader draws the fringe strip across it. So paint hard-ish, 0 or 1 with a short ramp. The ramp width doesn't change the look; `Fringe size` on the material does.

**Turf borders (`T_EdgeFringe.png`):** this is the most important texture in the folder. Each row is one turf's border, painted as a horizontal strip: top of the row = inside the turf, bottom = outside, left-to-right = along the border. R is the silhouette (paint blades, leaves, rocks hanging out over the neighbour), G a highlight rim just inside the silhouette, B an ink line just outside. The shader finds the border direction from the splat and wraps the strip around it, so a straight strip becomes blades radiating from any curve. Rows: R layer, G layer, B layer, cliff. `Fringe size` scales the strip like a tiling texture: one repeat along the border = that many metres, and it reaches a quarter of that across (rows are 4:1), so painted proportions are kept. `Fringe stretch` changes only the across reach. Bigger, bolder brush strokes like the reference = bigger `Fringe size` (try 4–6 m) and paint the strip with fewer, fatter blades.

**Layer textures:** the sizes in `Tile size in metres` are large on purpose (10–14 m). Paint at 2K, mid-frequency brush texture, **no landmark features** (no single big rock, no distinctive dark blob). Repeats are spotted by landmarks, not by texture. The dirt layer's sampler is shared by all layers, so set aniso on `T_Dirt`.

**Water:** a flat quad or plane at water height, scaled to cover the map. Position and UVs don't matter. If your camera is orthographic it just works; the depth conversion handles both.

## Decals

Decals are the biggest single step toward "hand-placed" ground. `Painted/Decal` is a screen-space projector: put it on a **unit cube**, and the cube's transform is the volume (X/Z = footprint, Y = how far above/below the ground it still lands). Nothing else needed; it works on any opaque surface, not only this ground shader.

- **Two materials**: `M_Decal_Multiply` (cracks, stains, scorch: darkens what's underneath and inherits its lighting) and `M_Decal_Normal` (leaves, pebbles, puddles, moss: painted over, lit with the same flat ambient + fire pool as the ground).
- **Tile** is picked per object by the `PaintedDecal` component, so all decals share the two materials. Change a decal's tile in the inspector, or make your own atlas (any grid; set `Atlas grid` on the material).
- **Scatter**: select any ground object with a collider and use **Tools ▸ Painted Ground ▸ Scatter Decals on Selected Ground**. The rules table at the top of `Editor/DecalScatter.cs` says which decal likes which biome (it reads vertex-colour splats under each hit). **Clear Scattered Decals** removes them.
- **Hand-place**: duplicate any scattered decal and move/rotate/scale it. That's how the hero spots (under a statue, around a campfire) should be done.
- Painting your own: one 512 px cell per decal, RGBA, keep the alpha soft-edged and put an ink rim on anything that should read as an object (puddle, leaf) rather than a mark (stain, scorch).

## Post effect and biome grading

`Painted/Post` runs through URP's built-in Full Screen Pass Renderer Feature (added by the menu item, or by hand: Renderer Data ▸ Add Renderer Feature ▸ Full Screen Pass, material `M_PaintedPost`, injection After Rendering Post Processing, Fetch Color Buffer on).

Per-biome/season mood lives in the **Lift / Gamma / Gain** colours (0.5 grey = neutral): cool blue lift + warm gain is the default night. Animate them from a script when the player crosses a biome boundary, or drop in a **LUT strip** (a 1024×32 `.png` in the standard Unity/Photoshop layout, `Use LUT strip` on) exported from your paint program with the grade baked in.

The **paper grain** is the piece that makes 2D sprites and 3D ground read as one drawing. Keep it subtle: 0.1–0.2. `More grain in shadows` mimics how pencil tooth shows more in dark washes.

## Cards: grass, ferns, props

`Painted/Card` is for props that stand up: ferns, mushrooms, signposts, the odd tall tuft. Alpha cutout, double-sided, swaying, lit with the same flat ambient + fire pool as the ground, casts shadows. Note that ordinary grass in this style is **not** a card: it's the border fringe in the ground shader. `CliffSkirtBuilder` (Tools ▸ Painted Ground ▸ Build Cliff Grass Cards) is there if you do want tufts along cliff lips, but the sample no longer uses it.

`Painted/Fog` is the same idea lying flat: a painted, thresholded body so it reads as brushed mist, soft-particle fade against the depth texture, drifting. Scale cards 6–12 m; a few big ones beat many small ones.

## Entities Graphics (DOTS)

All three shaders carry the `DOTS_INSTANCING_ON` variant and an SRP-Batcher layout, so they render through Entities Graphics / BatchRendererGroup. Ground and water need nothing else. For decals:

1. Install `com.unity.entities.graphics`.
2. Add `PAINTED_ENTITIES` to Project Settings ▸ Player ▸ Scripting Define Symbols. That compiles `PaintedDecalEntities.cs`, which bakes each decal's tile and tint into `[MaterialProperty]` components (Entities ignores MaterialPropertyBlocks).
3. Put the ground, water and the `Decals` group in a SubScene. Keep the camera, the lights and `CampfireFlicker` outside it: MonoBehaviours do not run on baked entities.

Spawning decals at runtime in ECS: instantiate a baked decal prefab entity and set `PaintedDecalTile`/`PaintedDecalTint` on it. Hundreds of decals are one batch per material.

If you use the plain GameObject path, nothing changes.

## Tuning cheat sheet

| Want… | Turn |
|---|---|
| Bigger / smaller grass blades on borders | `Fringe size` (keeps proportions); `Fringe stretch` for taller/shorter only |
| Borders too regular | `Edge wobble` up, or paint a longer strip |
| Softer, less inked edges | `Ink line` alpha down, `Highlight rim` alpha down |
| Less visible tiling | `Second sample scale` further from 1.0 (0.55 or 1.4), `Patch size` 20–40 m |
| Seasonal / biome mood | `Macro dark/light tint`. Cool blue + warm ochre = default; two greens = lush; grey + white = winter |
| Ground reads too 3D | `Main light N.L influence` to 0, `Shadow strength` 0.3–0.5 |
| Fire too bright at the base | `Point light boost` down; the shader already soft-clips it |
| Shoreline too clean | `Shoreline wobble` up, `Foam raggedness` up |
| Water too opaque / transparent | alpha of `Shallow colour` and `Deep colour` |
| Decals too strong / weak | `opacity` on the `PaintedDecal` component, or `Tint` alpha on the material |
| Decals bleeding onto rocks and props | shrink the cube's Y scale (default 1.5 m) |
| Decals showing on cliff faces | `Fade on slopes` up on the decal material |

## What this doesn't do (yet)

The full stack is here: ground, water, decals, cards, fog, post. What it doesn't do:

1. **Your art.** Every texture in this folder is a generated placeholder. The shaders are tuned for painted input; the look only lands when the layer textures, decals and tufts are painted.
2. **Sprites.** Characters, trees and props in the reference are 2D billboards; a sprite billboard shader with the same flat lighting is a natural next file.
3. **Seasons.** Snow needs a fourth layer or a global "snow amount" that lerps every layer toward a white version; the macro/blend machinery already supports it.

## Troubleshooting

- *Pink material* – shader didn't compile. Open the Console; the first error line is the one that matters. On Unity 6.0 rename `_CLUSTER_LIGHT_LOOP` → `_FORWARD_PLUS` and `USE_CLUSTER_LIGHT_LOOP` → `USE_FORWARD_PLUS` in `PaintedGround.shader`.
- *Water is one flat colour, no shoreline / decals don't show* – Depth Texture is off in the URP asset, or the camera overrides it to off, or its mode is "After Transparents".
- *Decals are mirrored or in the wrong place only in a build* – tell me; the projection uses the raw pixel UV for that reason, but if a platform disagrees, that's the line to look at (`screenUV` in `PaintedDecal.shader`).
- *Point light doesn't show on the ground* – Additional Lights is set to Disabled in the URP asset. Set Per Pixel (Per Vertex is treated as per pixel by this shader).
- *Post effect does nothing* – check the renderer feature is on the renderer your camera uses (the URP asset per Quality level can point at different renderers), and Fetch Color Buffer is on.
- *Borders look like a straight ruler line* – the fringe strip is not assigned (`Edge fringe strip` on the material), or it was imported with sRGB on. Also check `Fringe size` isn't tiny.
- *Cliffs not appearing* – the `Cliffs` toggle is off, or the mesh normals aren't steep enough. Lower `Cliff fully off above normal.y`.
- *Borders look blurry, or the fringe breaks into rectangles inside a soft area* – the splat map assigned is a raw weight/mask, not the SDF-encoded one. Re-run Bake Splat Map (mesh) or Encode Painted Splat Mask as SDF (PNG).
- *Painted borders show seams / flipped blades along mesh triangles* – the material is in Vertex Color splat mode. Run Tools ▸ Painted Ground ▸ Bake Splat Map from Vertex Colours on the ground; it switches the material to World Texture mode, where border directions are smooth.
- *Hard seams between terrain chunks* – shouldn't happen with world projection; check that the chunks' normals are recalculated with matching edge vertices.
