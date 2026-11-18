---
title: Ink Look (hand-drawn detail, hatched shadows, tiled ground transitions)
status: spec ready (exploration)
created: 2026-11-18
area: Shaders / PaintedGround
---

# Ink Look — exploration spec

**Goal:** match the feel of `Tasks/Claude/TextureInspo.png`. Flat local colour, one-tone shadow, and all
"detail" carried by warm-brown ink: lines inside surfaces, diagonal pencil hatching in shadow, faint paper
grain. Plus a Don't Starve-style tile-based ground transition (`Tasks/Claude/DontStarveTiles.png` +
`DontStarveNoise.png`) to replace the fringe edges that warp on curves.

This is an **experiment**: every piece sits behind a global dial or a material keyword so it can be
A/B'd against the current look in one Play session, then kept or deleted.

## Context

- **One lighting function lights everything.** All eight production graphs, `PaintedGround.shader`,
  `Painted/Ribbon` and the cards call `PaintedLighting_float` (`PaintedGround/Shaders/Include/PaintedGroundCore.hlsl`,
  see `Memories/Code/Shaders.md`, "One lighting model everywhere"). **Hatching added there reaches
  ground, units and props with no graph edits.**
- Global dials already flow through `MonoBehaviours/Terrain/TerrainLightingBridge.cs`
  (`Shader.SetGlobal*`, unset globals read 0 = no change). New dials follow the same rule.
- Ground today: base + 3 splat layers from a per-tile SDF, borders drawn by `PaintedFringe_float`
  sampling a strip texture **along the SDF tangent**, ink/rim from the strip, `PaintedFinish_float` at the end.
- Colour stays in the 64×64 UV-palette textures. **Nothing in this spec paints light into a texture.**

### Why the current transitions look warped

`PaintedFringe_float` lays the strip along the border with `s = dot(wxz, tangent)`. The tangent turns with
the border, so on a curve the strip art fans out on the convex side and bunches up on the concave side, and
where two borders meet or the SDF gradient goes flat (saddles, thin necks, corners) the tangent swings and
the strip smears. The art is drawn along a coordinate that does not exist cleanly on a curve.

Don't Starve avoids that entirely: the edge art is **drawn in tile space**. The world is a grid; each cell
looks at which of its 4 corners are inside a ground type (16 marching-squares cases), picks the matching
pre-drawn tile (plus a random variant), and uses it as an alpha mask over that ground type's layer.
Types are layered by priority, each one painted over the ones below. The layer itself is a flat tint
**multiplied** by a tileable grayscale stroke texture (`DontStarveNoise.png`). Edge ink is part of the
tile art, so it never stretches. This spec explores that next to the fringe, driven by the **same SDF
masks** (no new paint data, no editor changes).

## Skills Needed

- `shader-edit` (only if a graph needs touching; the plan avoids it)
- No `dots-*` skills: no ECS data, no systems. Pure rendering + one MonoBehaviour bridge.

## Placeholder art (P0, done 2026-11-18)

`Tools/inkplaceholders/generate_ink_placeholders.py` (numpy + Pillow, ~3 s, deterministic) writes to
`Assets/PaintedGround/Textures/Ink/`. Everything tiles. Re-run it after changing it; replace any file with
hand-drawn art using the same channel layout and the shader does not change.

| File | Size | R | G | B |
|---|---|---|---|---|
| `T_InkDetail_Cobble.png` / `_Grass` / `_Dirt` | 512² | ink lines (1 = full ink) | per-shape tone, 0.5 = neutral | scratch strokes (DS-noise-like) |
| `T_InkHatch.png` | 256² | `/` diagonal pencil hatch, 8 px pitch | paper grain, 0.5 = neutral | unused |
| `T_TransitionAtlas.png` | 1024×512 | coverage | highlight rim | ink line |

**Atlas layout (shader must match):** 128 px cells, 8 columns × 4 rows. Case bits: 1 = top-left,
2 = top-right, 4 = bottom-right, 8 = bottom-left corner inside. Case `c`, variant `v` (0/1) sits at
column `c % 8`, row `c / 8 + 2v`. Every case meets its neighbour at the edge midpoints whatever variant
either side picks (checked by script: variant border difference 0, neighbour coverage jump ≤ 1 AA pixel).
Assembled-map preview looked seamless.

Import settings for all of them: linear (sRGB off), wrap Repeat (atlas: Clamp), no compression on the
atlas for the first look (BC7 later), mipmaps on with the default filter for now (see D7).

## Data model (shader globals and material properties)

Globals, set by `TerrainLightingBridge` (new serialized fields, same `SetGlobal`/reset-on-disable pattern):

| Global | Type | Meaning |
|---|---|---|
| `_StitchHatchTexture` | Texture2D | `T_InkHatch` |
| `_StitchHatchParams` | float4 | x = hatch strength (0 = off), y = metres per hatch tile, z = space mode, w = paper-grain strength |
| `_StitchInkColor` | float4 | rgb = ink colour (one pen for everything), a unused |
| `_StitchHatchBoil` | float4 | x = boil frames per second (0 = off), y = boil offset in tiles |

Ground material (`PaintedGround.shader`) additions:

| Property | Meaning |
|---|---|
| `_InkDetailArray` (Texture2DArray) | the `T_InkDetail_*` slices, built with the existing Texture Packer (`TexturePackerConfig`) |
| `_InkDetailSlices` (float4) | slice per layer: x = base, y = R, z = G, w = B |
| `_InkDetailTileMetres` (float4) | world metres per detail tile, per layer |
| `_InkDetailParams` (float4) | x = line strength, y = tone strength, z = scratch strength |
| `_TransitionAtlas` + keyword `_GROUND_TRANSITION_TILES` | DS-style transitions instead of the fringe |
| `_TransitionCellMetres` | grid cell size (default 2 m) |

## Rendering changes

### R1. Hatched shadows (`PaintedLighting_float`, everything)

After `shade` and `ndl` are computed:

```hlsl
float sunBlocked = 1.0 - shade * ndl;                          // already banded: 0 lit .. 1 shadow
float2 hatchUV   = StitchHatchUV(positionWS, normalWS);         // space mode, see below
float2 hatchTap  = SAMPLE_TEXTURE2D(_StitchHatchTexture, sampler_LinearRepeat, hatchUV).rg;
float  inkAmount = hatchTap.r * smoothstep(0.35, 0.65, sunBlocked) * _StitchHatchParams.x;
lighting *= lerp(1.0, _StitchInkColor.rgb, inkAmount);
lighting *= lerp(1.0, hatchTap.g * 2.0, _StitchHatchParams.w); // paper grain everywhere
```

One shadow tone stays one tone. Hatching only adds strokes, never a second darkness level.
Point-light pools are added after the hatch, so a lamp lifts the strokes' surroundings but not the strokes.
`Eyes.mat` (`_ShadowStrength 0`, `_NdotLInfluence 0`) never reads as shadowed, so eyes never hatch.

**Space modes**: this is the experiment the owner flagged ("how hatch lines look with a moving
object/camera"). All three are built, switchable live from the bridge:

| Mode | UV | Camera pans | Object moves | Billboards |
|---|---|---|---|---|
| 0 World triplanar | world XZ / XY / ZY by normal | glued to world | object slides **through** the hatch ("shower door") | plane flips when yaw changes |
| 1 Camera-plane, world-anchored | `(dot(P, camRight), dot(P, camUp))` | glued to world, always `/` on screen | shower door | consistent |
| 2 Camera-plane, object-anchored | same, with `P - objectOrigin` | glued | **hatch rides with the object** | consistent |

Mode 2 is the expected winner: lines always read as `/` on screen at constant density, the ground
(one object per 100 m tile, origin on a tile corner) stays world-fixed, and a walking unit carries its
hatch with it. Tile seams stay continuous as long as 100 m is a whole number of hatch tiles (it is for
any y that divides 100). Camera axes come from `UNITY_MATRIX_V` rows 0 and 1; object origin from
`GetObjectToWorldMatrix()._m03_m13_m23` (works under Entities Graphics instancing).

**Known risk for mode 2:** units are built from several part entities. Each part has its own origin, so
the hatch phase jumps at part borders. Accept for the experiment; if it shows, the fix is a per-actor
anchor (root position written to a per-instance material property, the `BodyPartTint` pattern). ← DECISION D3

**Boil** (optional, off by default): offset the hatch UV by `_StitchHatchBoil.y` tiles, stepping
`floor(time * fps)` through 3 fixed offsets. Hand-drawn shimmer; judged by eye.

### R2. Ground ink detail (`PaintedGround.shader`)

Each layer's colour is multiplied by its ink-detail slice, sampled world XZ at that layer's tile metres,
the way DS multiplies tint × noise:

```hlsl
float3 detail = SAMPLE_TEXTURE2D_ARRAY(_InkDetailArray, sampler, wxz / tileMetres, slice).rgb;
layerCol *= lerp(1.0, detail.g * 2.0, _InkDetailParams.y);       // per-stone tone
layerCol *= 1.0 - detail.b * _InkDetailParams.z * 0.25;          // scratch strokes
ink = max(ink, detail.r * _InkDetailParams.x);                   // lines join the existing ink
```

Lines go into the existing `ink` channel, so `PaintedFinish_float` inks them with the same colour as the
edges. That colour should become `_StitchInkColor` for one pen everywhere. ← DECISION D5
The detail is sampled once per layer inside the existing `Layer()` helper so it follows the anti-tiling
patch swap.

### R3. Tile transitions (`_GROUND_TRANSITION_TILES`)

Per fragment:
1. `cell = floor(wxz / _TransitionCellMetres)`, `local = frac(wxz / _TransitionCellMetres)`.
2. Point-sample the splat SDF at the 4 cell corners (4 taps, shared by all three channels).
3. Per channel R, G, B in the existing paint order: `case = Σ bit(corner > 0.5)`,
   `variant = hash(cell) & 1`, sample the atlas cell, then
   `col = lerp(col, layerCol, atlas.r)`, `rim = max(rim·(1-a), atlas.g)`, `ink = max(ink·(1-a), atlas.b)`.
   That has the same outputs as `PaintedFringe_float`, so `PaintedFinish_float` and the steep-slope
   cliff pass are untouched.
4. Atlas UV is inset half a texel per cell so bilinear filtering never bleeds across atlas cells.

Corners sampled from the SDF are shared by neighbouring cells, so neighbours always agree and seams
cannot open. The look is coarser than the SDF (borders snap to the grid's corner pattern), which is the
Don't Starve look. Cell size is the dial: 2 m is a guess. ← DECISION D4

The fringe path stays the default; the keyword is per material so two tiles can sit side by side.

## File manifest

| File | Change |
|---|---|
| `Tools/inkplaceholders/generate_ink_placeholders.py` | new (P0, done) |
| `Assets/PaintedGround/Textures/Ink/*.png` | new placeholders (P0, done) |
| `Assets/PaintedGround/Textures/Ink/InkDetail_Array` (TexturePackerConfig + generated array) | new |
| `Assets/PaintedGround/Shaders/Include/PaintedGroundCore.hlsl` | hatch globals, `StitchHatchUV`, hatch + grain in `PaintedLighting_float`; `PaintedTransitionTile_float` |
| `Assets/PaintedGround/Shaders/PaintedGround.shader` | detail array props + sampling, transition keyword path |
| `Assets/_Scripts/MonoBehaviours/Terrain/TerrainLightingBridge.cs` | hatch texture, strength, metres, space mode, grain, ink colour, boil |
| `Assets/_Vault/Memories/Code/Shaders.md`, `Terrain.md` | record the outcome |

## Build phases

- **P0 Placeholders** done (above).
- **P1 Hatch + grain** (R1): core function + bridge dials, default strength 0, so nothing changes until turned
  on. Verify in `Game.unity` / `CampLoop`: a unit walking in and out of a building shadow, a camera pan, a
  rotate if the camera can, in each space mode.
- **P2 Ground ink detail** (R2): Texture Packer array, shader sampling, one TerrainWorld tile material set
  up (cobble on the stone layer, grass, dirt).
- **P3 Tile transitions** (R3): keyword path on one tile's material, next to a fringe tile.
- **P4 Owner look pass:** captures of each mode next to `TextureInspo.png`; owner picks, losers deleted.
- **P5 (after the pick)** owner's hand-drawn sheets drop in over the placeholders; units/props get their
  own detail sheet if the ground-only result makes them look too clean.

## Verification

- Compile: `refresh_unity` → `read_console` (shaders: look for shader compile errors on `Painted/Ground`
  and on one converted graph, e.g. `2DShader`, since all of them include the core).
- With every new dial at 0 the frame must be identical to before (diff a capture).
- P1: captures of a shadowed unit and shadowed ground in modes 0/1/2, then a short pan; the swim check needs the owner's eye.
- P3: a capture where a grass patch crosses a 100 m tile seam, and one at a saddle (two diagonal corners).

## Open decisions

Recorded defaults (architecture calls, decided here; overturn freely):
- [x] D1 Hatch lives in `PaintedLighting_float` (all shaders at once), not per graph.
- [x] D2 One hatch mask from `shade * ndl`: cast shadows and form shading hatch alike.
- [x] D6 Transition tiles read the existing SDF corners; no new paint data or editor work in this experiment.
- [x] D7 Mips: default box filter for the first look. If strokes go grey when zoomed out, author mips in the
      Texture Packer (thicker, sparser lines per level).

Owner look calls (answer after P4 captures):
- [ ] D3 Hatch space mode: 0 world / 1 camera-plane / 2 camera-plane object-anchored (+ per-actor anchor if parts seam).
- [ ] D4 Transition cell size (2 m default) and whether transitions replace the fringe everywhere.
- [ ] D5 One ink colour for hatch, detail, edges and outlines (`RobertsCross`), or separate.
- [ ] D8 Boil on or off.
- [ ] D9 Hatch density in world units (denser when zoomed out) vs constant on screen.
