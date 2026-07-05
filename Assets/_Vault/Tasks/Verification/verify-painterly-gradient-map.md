---
title: Verify Painterly Gradient-Map (UV palette atlas) shader
status: active
created: 2026-07-04
area: code
---

## Goal

Confirm the UV-driven palette atlas works: a model's mesh UVs pick a base colour per part out
of a 64×64 gradient atlas, brush strokes stay uniform, and cel lighting does the shading. All
code is committed. Spec: [`PainterlyGradientMap_Shader.md`](PainterlyGradientMap_Shader.md).

**Model built:** `_GradientLUT` (mesh UV) → base albedo → × Cel Shaded Lighting → BaseColor.
The stroke mask (`_MainTex`, its own tiled UV) now feeds **only** Height To Normal (the
painterly surface); it no longer drives colour. A material on `PainterlyPaletteShader` needs
**both** `_MainTex` (stroke mask) and `_GradientLUT` (palette) assigned.

**Deviation from spec:** the graph samples `_GradientLUT` with a **built-in Sample Texture 2D**
node (mesh UV), not the reflected `PainterlyGradientMap` node — behaviour is identical because
the LUT imports **Clamp** (matching the node's `saturate`). The node still ships in the library.

## Steps

### Compile + import (first)
- [ ] Focus Unity; confirm **no compile errors** (`error CS####` / Burst `BC####`) from
  `PainterlyGradientLUTSO.cs`, `PainterlyGradientLUTGenerator.cs`.
- [ ] No **duplicate-GUID** warnings on import (the new `.hlsl`/`.cs`/`.shadergraph` metas were
  hand-written: node `ef67c14d…`, SO `23e419ba…`, generator `1e5e6bab…`, graph `27d140a2…`).
- [ ] `PainterlyPaletteShader.shadergraph` imports with **no errors** and is not magenta. Open it
  once — confirm the layout reads sensibly (new Property + Sample Texture 2D feed the albedo
  Multiply's A input; Painterly Color still feeds Height To Normal).
- [ ] Create Node menu: search "StitchPunk" → **Painterly Gradient Map** appears under
  StitchPunk/Painterly.

### Authoring the LUT
- [ ] Create a `PainterlyGradientLUTSO` (Assets ▸ Create ▸ Dots Animation ▸ Painterly Gradient LUT);
  add a few gradients (e.g. brown, grey, red).
- [ ] Select it → **Stitch Punk ▸ Generate Painterly Gradient LUT** → `T_PainterlyGradientLUT.png`
  is written to `Assets/Textures/Painterly/` with the row reference sheet
  `T_PainterlyGradientLUT_rows.txt`. Confirm importer: sRGB **on**, wrap **Clamp**, filter
  **Point**, mipmaps **off**.

### Visual
- [ ] Make a material on `PainterlyPaletteShader`; assign `_MainTex` = `T_PainterlyMask` and
  `_GradientLUT` = the generated atlas.
- [ ] On a test mesh, sweeping a face's **UV.y** across the row bands (per the reference sheet)
  switches its colour; brush strokes remain uniform in scale regardless of UV layout.
- [ ] A prop with two UV regions (e.g. sword hilt vs blade) renders two colours from one
  material / one draw; lighting shades each colour (no flat, unlit look).
- [ ] Re-generate the LUT from edited gradients → the material updates in place (same texture
  GUID, no re-assign needed).

## Notes

- If entities in a baked subscene render magenta or log `RenderMeshArray … invalid out of bounds
  index` after assigning the new material, that's a **stale subscene bake** — reopen/rebake the
  subscene, not a shader bug.
- The reflected `PainterlyGradientMap` node is available if you'd rather hand-wire the single-node
  form; it's `saturate(uv)` sample of the LUT — swap it for the built-in Sample in the graph and
  re-point the albedo Multiply's A input.
- `PainterlyShader.shadergraph` (the original ramp shader) is untouched — existing painterly
  materials are unaffected.
