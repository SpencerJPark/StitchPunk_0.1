---
tags: [plan, terrain, tooling]
related: "[[Editor]], [[Shaders]]"
---

> **Superseded 2026-10-30 by `Terrain_System.md`** (full architecture, spikes, Sonnet task list). This file stays as the T0/T1 record and the home of TT-D1 to TT-D5.

# Terrain Tiles — painted ground you can author and stream

Open-world goal: the world is a grid of **100 x 100 m tiles**. Tile (x, z) is centred on (x*100, z*100); tile (0,0) is the camp-centred square -50..50. Ground look comes from `Assets/PaintedGround` (world-projected layers, painted turf borders, projected decals). Streaming loads whole tiles.

## Settled calls (TT-D)

- TT-D1 One splat mask + one material per tile. A single world-wide mask cannot stream.
- TT-D2 The hard RGB mask PNG (R grass, G forest, B stone, black dirt) is the source of truth; the SDF the shader samples is derived from it by `TerrainSplatCodec` and is never hand-edited.
- TT-D3 Mask is 512 px per tile (about 0.2 m/texel); SDF range 4 m.
- TT-D4 Painted post effect dropped (rendered black on 6.5, cause unfound). Decals only need Depth Texture, already on.
- TT-D5 Decals are plain GameObjects per tile, not subscene entities, until `PAINTED_ENTITIES` is decided with streaming.

## Phases

- **T0 built (2026-10-30)** `Ground` uses the painted material, 500 decals under `GroundDecals`.
- **T1 built (2026-10-30)** `TerrainTile` component, `TerrainSplatCodec` (SDF port), `TerrainTileAssets`, `Stitch Punk > Terrain > Create Tile... / Adopt Selected Ground As Tile`. Tile (0,0) adopted.
- **T2 next** Splat brush: Scene-view brush that paints the mask with live SDF re-encode (visual feedback is the feature), layer picker, Undo.
- **T3** Spline roads: Unity Splines (already in PackageCache via Cinemachine) rasterised into the mask as a dirt/stone layer with width + edge falloff.
- **T4** Per-tile decal scatter (biome from mask) + road-edge decals; decals stored per tile.
- **T5** Streaming: tile -> subscene, load radius around the camera/player, seam handling.

## Known gaps

- Neighbouring tile masks are independent: a turf border at a tile edge will not continue across the seam until T2 can paint across tiles.
- Tile creation puts the tile in the active scene; move it into the tile's subscene by hand until T5.
- The test mask for tile (0,0) was generated procedurally, not painted.
