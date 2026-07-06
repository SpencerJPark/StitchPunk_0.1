---
title: Verify Sprite Tint — per-instance _BaseColor tinting + Sprite Tint nodes
status: active
created: 2026-07-05
area: code
---

## Goal

Confirm that body-part sprites can be recoloured per-instance via a multiply-tint on
`_BaseColor`, that black outlines survive the tint, and that the crowd still batches into one
draw call. Also confirm the converted `SpriteTint` / `SpriteTintMasked` reflection nodes import.

**What was built:** both 2D graphs already multiplied the sprite by a `_BaseColor` colour
property (`texture sample × _BaseColor → Cel Shaded Lighting → Base Color`) — the outline-safe
multiply-tint (`0 × tint = 0` keeps outlines black). `_BaseColor` is now **Hybrid Per Instance**
in `2DTextureArrayShader` and `2DShader`. New `BodyPartTint : IComponentData`
(`[MaterialProperty("_BaseColor")]`, `float4 Value`) is baked **white (1,1,1,1)** on every
rendering part by `BodyPartAuthoring`. The old `_float`/`_half` `SpriteTint.hlsl` in
`_Vault/Tasks/Materials/` was retired; the nodes now live in `Assets/Shaders/Nodes/Sprite/`.
Context: [`Shaders.md`](../../Memories/Code/Shaders.md).

## Steps

### Compile + import (first)
- [ ] Focus Unity; confirm **no compile errors** (`error CS####` / Burst `BC####`) from
  `AnimationComponents.cs`, `BodyPartAuthoring.cs`.
- [ ] No **duplicate-GUID** warnings on import (hand-written node metas:
  `SpriteTint` `f323cfd8…`, `SpriteTintMasked` `f7cde32b…`).
- [ ] Both `2DShader.shadergraph` and `2DTextureArrayShader.shadergraph` import with **no errors**
  and are not magenta.
- [ ] Create Node menu: search "StitchPunk" → **Sprite Tint** and **Sprite Tint Masked** appear
  under StitchPunk/Sprite.

### Rebake
- [ ] Reopen/rebake the character subscene (e.g. `DOTSTestScene`) so parts get the new
  `BodyPartTint` component. If parts render magenta or log `RenderMeshArray … invalid out of
  bounds index`, that's a **stale subscene bake** — rebake, not a shader bug.

### Visual — per-instance tint (the feature)
- [ ] Enter Play mode → **Window ▸ Entities ▸ Hierarchy** → select one body-part entity →
  set its **BodyPartTint → Value** to e.g. `1, 0.3, 0.3, 1`.
- [ ] That part recolours (red fill), its **black outline stays black**, and **only that part**
  changes — neighbouring parts / other characters are unaffected (proves per-instance override).
- [ ] Set Value back to `1,1,1,1` → the part returns to its authored colour (white = neutral).

### Batching (optional)
- [ ] With several characters carrying different `BodyPartTint` values, the Frame Debugger / stats
  still show them batched (Entities Graphics uploads the tint per-instance, one draw).

## Notes

- **Material vs per-instance:** because every part carries a `BodyPartTint` override, color-picking
  `_BaseColor` on the *material* no longer drives baked parts (the component wins). Tint by writing
  the entity's `BodyPartTint`. If per-material material-picking is wanted for early art tests, make
  the bake optional (authoring checkbox) so the material colour drives parts until one opts in.
- **Bake rule:** sprite atlas must be baked **white/light fill + black outline** — a part baked
  already-coloured multiplies to a muddy dark shade.
- The `SpriteTint` / `SpriteTintMasked` nodes are library/hand-wire nodes; production uses the
  built-in `_BaseColor` multiply, so nothing wires them yet. `SpriteTintMasked` is for future parts
  with 2–3 colour zones in one sprite (needs an R/G/B mask texture on the same UVs).
- No new system writes `BodyPartTint` yet — a skin/design system is the intended driver.
