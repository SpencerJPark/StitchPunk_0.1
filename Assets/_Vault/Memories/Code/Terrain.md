---
tags: [memory, code, terrain]
related: "[[Editor]], [[Data]], [[Gotchas]]"
---

# Terrain (plan: `Tasks/Plans/Terrain_System.md`, spikes: `Terrain_Spikes.md`)

## What exists (P1 + P2)
- Data (`Data/Terrain/`): `TerrainWorldProfile` (30 x 30 tiles from -15), `TerrainPalette` (`ApplyToMaterial` names match `Painted/Ground`), `TerrainTileData` (one SO per tile, every source layer a separate array; final height = sum of base + hand + river + road, 101 x 101 samples, index `z * 101 + x`, sample 0 = min corner).
- `TerrainTile` (MonoBehaviours) now carries `tileData` + `palette`.
- Editor: see the `Terrain/` row in `Editor.md`. Tile (0,0) lives in its own subscene `Assets/Terrain/Tiles/Tile_0_0/Tile_0_0.unity` (AutoLoad true until P9 streaming), under a `TerrainWorld` object in `CampLoop`.
- Collider is a legacy `MeshCollider` on the tile (S1: a heightfield is 10x smaller, 76 KB vs 807 KB, but needs a custom baker; deferred).

## Paint (P3)
- Sources per tile: base mask PNG (hard RGB, opaque), `PaintOverlay.png` (RGB layer colour, A = painted), optional `RoadMask.png`. `TerrainCompositor` (base, then overlay, then road wins; river layer not built) writes the SDF PNG; it never writes a source layer or a height array.
- Live painting (`Brushes/TerrainPaintBrush`): per-stroke session per tile with its own uncompressed RGBA32 SDF texture on the material (the imported SDF is BC7 and cannot be edited), dirty-rect encode `EncodeSignedDistanceFieldRegion`, PNG + recomposite only on mouse-up. Undo = revision match + saved overlay rect.
- Mask resolution is per tile (`TerrainTileAssets.MaskResolutionFor`; tile (0,0) stays 512); SDF PNGs are stamped BC7 on first creation, `Compress Selected Tile SDF (BC7)` fixes an old one.

## Traps
- A domain reload (any recompile) clears the Undo stack and closes tile subscenes.
- Sculpt only ever writes `handHeightDelta`. An edge sample exists in 2 (corner: 4) tiles and every copy must hold the same value; `TerrainHeightBrush` computes it once and writes all copies (test: `TerrainHeightBrushTests`).
- `ConvertToTileScene` closes the new scene with `removeScene`, which destroys the tile GameObject in the editor session; reopen with `SubSceneUtility.EditScene(subScene)`.
- A hand-built `Unity.Physics.PhysicsWorld` needs `RigidBody.Scale = 1` or every ray misses.
- DXT1/DXT5 break the SDF (1.3 m error); the SDF import format is BC7 (P3-0).
- Full SDF encode is 236 ms at 512 px (62 ms at 256), so live painting needs the dirty-rect encode (P3-1).
- Nothing has been done in the Scene view with the mouse yet (brush overlay and sculptor are compile + logic checked only); drive them once by hand.
