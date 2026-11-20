---
title: Ink Look (hand-drawn detail, hatched shadows, tiled ground transitions)
status: built T1-T5 2026-11-18, T6 owner look pass open
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
| `_InkDetailBaseTex`, `_InkDetailRTex`, `_InkDetailGTex`, `_InkDetailBTex` (2D, default `"gray"`) | one `T_InkDetail_*` per layer slot, mirroring `_BaseTex`/`_LayerRTex..BTex` |
| `_InkDetailTileMetres` (float4) | world metres per detail tile, per layer (x base, y R, z G, w B) |
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
float3 detail = SAMPLE_TEXTURE2D(detailTex, sampler_BaseTex, wxz / tileMetres).rgb;
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

## File manifest (sizes measured 2026-11-18)

| File | Lines | Change |
|---|---|---|
| `Tools/inkplaceholders/generate_ink_placeholders.py` | — | new (P0, done) |
| `Assets/PaintedGround/Textures/Ink/*.png` | — | new placeholders (P0, done) |
| `Assets/PaintedGround/Shaders/Include/PaintedGroundCore.hlsl` | 213 | hatch globals, `StitchHatchUV`, hatch + grain in `PaintedLighting_float`; `PaintedTransitionTile_float` |
| `Assets/PaintedGround/Shaders/PaintedGround.shader` | 520 | detail texture props + sampling, transition keyword path |
| `Assets/_Scripts/MonoBehaviours/Terrain/TerrainLightingBridge.cs` | 67 | hatch texture, strength, metres, space mode, grain, ink colour, boil |
| `Assets/_Vault/Memories/Code/Shaders.md`, `Terrain.md` | — | record the outcome |

**Blast radius of the core edit:** `PaintedGroundCore.hlsl` is included by `PaintedGround.shader:219`,
`PaintedRibbon.shader:104`, `PaintedTreeCard.shader:111`, `Shaders/StitchGlass.shader:80`,
`Include/PaintedGroundGraph.hlsl:11` (all eight production graphs) and `Include/PaintedGroundNodes.hlsl:21`.
A syntax error there turns every lit material pink, so T2 is compiled in the Editor before anything else lands.

## Build tasks (token budget)

The whole build is roughly 250 lines across three files. **Run it inline in one orchestrator session;
do not spawn workers.** A worker brief would cost more than the edit, and no worker can check a shader
(the devloop gate covers C# only, subagents never touch MCP). The only gate for T2/T4/T5 is the
orchestrator's `refresh_unity` → `read_console` (filter `Shader error`), then one capture.

Read only the anchors below. Do not read whole files; every anchor is grep-able by the quoted name.

| Task | Edits | Read first (anchors) | Depends | Check |
|---|---|---|---|---|
| **T1** import settings | 5 `.meta` via `manage_texture` / execute_code: sRGB off; Repeat (atlas Clamp) | — | — | Inspector shows Linear |
| **T2** hatch + grain (R1) | `PaintedGroundCore.hlsl` only | globals block `_TerrainAmbientColor`…`_StitchTintedShadowParams` (~123–138); `PaintedLighting_float` (~162–211) | T1 | Editor shader compile; capture with strength 0 must equal before |
| **T3** bridge dials | `TerrainLightingBridge.cs` only | whole file (67 lines) | — [parallel-safe with T2] | `devloop.py gate` on its assembly; lint |
| **T4** ground detail (R2) | `PaintedGround.shader` only | Properties `_LayerRTex` (~34–36); `TEXTURE2D(_LayerRTex)` (~222); `half3 Layer(` (~272); layer calls (~316–323); `PaintedFinish_float(` (~340) | — [parallel-safe with T2/T3] | Editor compile; capture of one tile |
| **T5** transitions (R3) | `PaintedGroundCore.hlsl` (new function after `PaintedFringe_float`, ~99) + `PaintedGround.shader` (keyword `#pragma` ~205–207, branch around ~316–333) | `PaintedFringe_float` (~78–99) as the output contract | T2, T4 (same files) | Editor compile; seam + saddle captures |
| **T6** look pass (P4) | one TerrainWorld tile material, bridge values | — | T1–T5 | owner reads captures vs `TextureInspo.png` |

Paste-ready facts so the build session does not rediscover them:
- `Layer(TEXTURE2D_PARAM(tex, samp), float2 wxz, float tileSize, half patchMask)` is the per-layer sampler in
  `PaintedGround.shader`; the detail sample goes beside it, not inside `PaintedLayer_float` (shared with graphs).
- `PaintedFringe_float` outputs `(colOut, inkOut, rimOut)` and is chained `col, ink, rim → col, ink, rim`
  across R, G, B, then the cliff pass (row 3). The transition function keeps that exact signature shape.
- Bridge pattern: cache `Shader.PropertyToID` in static readonly ints, `SetGlobal*` in `Update`, reset to
  zero in `OnDisable` (lines ~46–60).
- Unity 6.5 Shader Graph custom functions see `UNITY_MATRIX_V` and `GetObjectToWorldMatrix()`; verify by a
  compile, not from memory.

**Later, after the owner picks (not budgeted here):** the owner's hand-drawn sheets replace the
placeholders with no code change; units/props get their own detail sheet only if ground-only looks too clean.

## Verification

- Compile: `refresh_unity` → `read_console` (shaders: look for shader compile errors on `Painted/Ground`
  and on one converted graph, e.g. `2DShader`, since all of them include the core).
- With every new dial at 0 the frame must be identical to before (diff a capture).
- T2: captures of a shadowed unit and shadowed ground in modes 0/1/2, then a short pan; the swim check needs the owner's eye.
- T5: a capture where a grass patch crosses a 100 m tile seam, and one at a saddle (two diagonal corners).

## Build log (2026-11-18, T1-T5)

All of T1-T5 built inline; C# gate + lint pass on `StitchPunk.MonoBehaviours`; `Painted/Ground` (world-texture and
vertex-colour variants, every new keyword on), Ribbon, TreeCard, Glass and the `2DShader` graph compile with no messages
(`ShaderUtil.CompilePass(material, pass, true)` per variant, then `GetShaderMessages`). Nothing seen on screen yet.

Where the build differs from the text above:
- **Detail lines are inked into the layer colour**, not `max`-ed into the shared `ink` channel (`InkedLayer` in
  `PaintedGround.shader`). Same pen (`_InkColor`), but the lines are covered exactly as far as the layer is; a shared
  channel would have let a buried layer's lines show through the layer painted over it.
- **Ink detail is behind a keyword** (`_INK_DETAIL`, default off) as well as the strength vector, so an off material
  costs no extra samples.
- **Mode 2 never anchors the ground.** `PaintedGround.shader` defines `STITCH_HATCH_WORLD_ANCHORED` before including the
  core: subtracting a tile origin projected on yawed camera axes is not a whole number of hatch tiles, so the hatch
  would have jumped at every 100 m seam. Units, props and graphs still anchor to their object origin.
- **Hatch and grain skip the texture tap when both dials are 0** (uniform branch), so the "all dials 0 = identical"
  check holds by construction.
- **Tile transitions need the world splat map**; with `_SPLAT_VERTEXCOLOR` the keyword falls back to the fringe.
  The atlas is sampled with `SampleGrad` from the continuous cell coordinate so cell borders do not drop to the
  smallest mip in a one-pixel line.
- **Material staged:** `Terrain/TestWorld/Tile_0_0/M_Ground_Tile_0_0.mat` has the detail sheets (Base Dirt, R/G Grass,
  B Cobble) and the atlas assigned, both keywords off. The bridge's hatch texture is not assigned in any scene yet.

## Owner feedback round 1 (2026-11-18)

Owner verdict on the first switched-on captures: "nothing like the example". The build had been judged against the
spec text, never against `TextureInspo.png` / `DontStarveTiles.png`. Fixed in the same session:
- **Shadows are drawn only by the hatch.** New global `_StitchInkShadowLift` (bridge: `shadowDrawnOnlyByHatch`,
  default 1, pushed only while the hatch draws): the sun term ignores the shadow, the hatch still reads it.
- **Flat local colour.** `_InkDetailParams.w` flattens each painted layer to its own smallest-mip average, so all
  surface detail comes from the ink sheets (the reference is flat colour + ink). Macro strength on the two CampLoop
  tile materials 0.6 -> 0.15.
- **One pen** for detail lines and edge ink: `_StitchInkColor` (0.17, 0.12, 0.09) whenever the bridge pushes it; the
  tile's own `_InkColor` is bright green.
- **Placeholder art redrawn to the reference:** packed outlined cobbles, sparse grass strokes, pebbles + stipple, all
  with 3-4 px lines so they survive minification; hatch 128 px at 4 px pitch, 5 m per tile (about 4 px on screen
  at play zoom); transition cells redrawn Don't Starve style (outward turf flicks, sparse curved strokes, no
  outline, no highlight band). Transition cell 4 m.
- Not touched by the ground changes: roads (`Painted/Ribbon`), tree cards, props and units keep their old look
  apart from the hatch.

## Owner feedback round 2 (2026-11-19) — hatch redesigned

Owner on round 1: zoom-out broke; hatch bad on moving objects (maybe static only), swims when the camera turns, odd on
characters in shade, too big, pixelated, vanishes zoomed out. Texture hatch dropped. Now:
- **Hatch is ground-only and procedural** (`StitchGroundHatchLines` in `PaintedGround.shader`): world-locked diagonal
  lines computed per pixel, anti-aliased to a fixed pixel width, constant spacing on screen at every zoom (world pitch
  steps in octaves, every other line fades between them). Characters and props keep their normal shading; the shadow
  they cast on the ground is hatched. `T_InkHatch.png` deleted.
- **The ink look is global**, not per material: keyword `STITCH_INK_LOOK` + `_Stitch*` globals from
  `TerrainLightingBridge` (`inkLookEnabled`). The zoomed-out stand-in chunks are separate baked material copies made
  before the ink existed, which is why zoom-out showed the old ground; a global reaches them with no rebake.
- **Shadow lift is ground-only**: `StitchPaintedLighting` (core) takes hatch lines + lift; `PaintedLighting_float`
  calls it with 0, 0, so every graph is unchanged from before the experiment.
- Ground materials and the Main Camera override in CampLoop restored to their pre-experiment values.
- Not compiled in the Editor yet (Unity was closed).

## Owner feedback round 3 (2026-11-20) — hatch on objects

Owner: ground shadows better, but caravan, building and characters had no hatch, so the look could not be judged; with
the World anchor, hatch slid over a unit walking along X but not along Z.
- `PaintedLighting_float` (every graph) now draws `StitchObjectHatchLines` in shade: the plane the surface mostly faces,
  same pixel spacing as the ground. Bridge: `objectHatchStrength` 1, `objectHatchAnchor`, `objectShadowDrawnOnlyByHatch` 0.5.
- **Anchor default = Object.** World on a camera-facing card uses the X/Y plane, so only X motion moves the lines.
- **Hatch mask ignores the material's darkening strength**: objects keep N.L influence at 0.15, so a wall facing away
  from the sun never crossed the threshold. A material with both shade dials at 0 (eyes) still never hatches.
- Open: units are many part entities, each with its own origin, so faint seams show at part borders (captures 10/11).
  The fix is a per-actor anchor (root position as a per-instance property, the `BodyPartTint` pattern), not built.
- Open: characters whose card faces away from the sun are hatched all over (capture 10).

## Owner feedback round 4 (2026-11-20) — characters: real shade only, lines slide

Owner: with the Object anchor the lines did not move on characters at all; asked for both "hatch only in real shade" and
"lines slide over them".
- Flat cards hatch only from cast shadows: the six `2D*` graphs now call `SG_CardLighting` (`PaintedGroundGraph.hlsl` ->
  `PaintedCardLighting_float`); 3D graphs, tree cards, ribbon and glass keep `SG_Lighting` / `PaintedLighting_float`.
  Renaming a graph's Custom Function is a two-line JSON edit (`m_FunctionName` + the node `m_Name`); same ports.
- Object lines are slices of one fixed 3D direction (1, -1, -1): "/" on cards and on horizontal surfaces, and a unit
  moves through them equally along X and Z (captures 12-14). Anchor default back to World.
- Still open: per-part seams under the Object anchor (per-actor anchor not built); not visible under World.

## Open decisions

Recorded defaults (architecture calls, decided here; overturn freely):
- [x] D1 Hatch lives in `PaintedLighting_float` (all shaders at once), not per graph.
- [x] D2 One hatch mask from `shade * ndl`: cast shadows and form shading hatch alike.
- [x] D6 Transition tiles read the existing SDF corners; no new paint data or editor work in this experiment.
- [x] D10 Detail sheets are four plain 2D slots, not a Texture2DArray: no array builder exists for
      Assets (the Texture Packer is a toolkit flipbook tab), and the ground already has four layer slots.
- [x] D7 Mips: default box filter for the first look. If strokes go grey when zoomed out, author mips in the
      generator script as extra mip PNGs (thicker, sparser lines per level).

Owner look calls (answer after the T6 captures):
- [ ] D3 Hatch space mode: 0 world / 1 camera-plane / 2 camera-plane object-anchored (+ per-actor anchor if parts seam).
- [ ] D4 Transition cell size (2 m default) and whether transitions replace the fringe everywhere.
- [ ] D5 One ink colour for hatch, detail, edges and outlines (`RobertsCross`), or separate.
- [ ] D8 Boil on or off.
- [ ] D9 Hatch density in world units (denser when zoomed out) vs constant on screen.
