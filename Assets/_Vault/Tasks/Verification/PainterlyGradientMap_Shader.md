# Painterly Gradient-Map (UV Palette Atlas) — Shader Spec

> **Status:** 🔨 built (2026-07-04) — code landed, Editor verify pending. See `verify-painterly-gradient-map.md`.
>
> **Resolved decisions (build):** §2 LUT = **colour only, lighting does the shading** (the brush-stroke mask no longer drives colour light/dark — it only feeds Height To Normal); mesh UV samples the LUT for base albedo. New `PainterlyGradientMap` node + variant `PainterlyPaletteShader.shadergraph` + `PainterlyGradientLUTSO` + generator. **Deviation:** the variant graph samples `_GradientLUT` via a **built-in Sample Texture 2D** node (mesh UV), not the reflected node — cloning reflected texture/sampler slots has no safe same-file template, and behaviour is identical (LUT imports **Clamp**, matching the node's `saturate`). The `PainterlyGradientMap` node ships in the library for hand-wiring.
> **Goal (Spencer):** a 64×64 texture of gradients acts as the "uber colour map" for a model — the mesh **UVs point each part at a colour zone** (sword hilt → brown, blade → grey), and the painterly system then shades within that colour using the brush-stroke mask. One small texture + one sample = many colours per object, brush strokes stay uniform.
> **Relates to:** [`DirectionalTexturePacking_System.md`](DirectionalTexturePacking_System.md) (also a grayscale-mask→colour idea, but for character parts via per-part palette ramps — this is the *environment/prop* palette-atlas equivalent and reuses the same Painterly nodes). Build skill: `shader-edit`.

---

**Skills Needed:** `shader-edit` — new reflection node + `PainterlyShader.shadergraph` surgery (§4–5). The generator (§6) is a hand-written editor tool mirroring `PainterlyMaskGenerator.cs` (no skill).

---

## 1. Purpose & scope

Replace the *inline* 4-stop colour ramp in the painterly chain with a **gradient-map texture LUT** indexed by two things:
- **V axis (which colour) = mesh UV.y** — the artist UV-maps each region of the model onto a different row of the atlas. Row 3 = brown → hilt UVs sit on row 3; row 8 = grey → blade UVs sit on row 8.
- **U axis (shading within that colour) = the brush-stroke mask value** (post-contrast, same value the ramp uses today) — so each row is a *gradient* (shadow → highlight of that colour) and the strokes drive painterly light/dark **in the correct hue**.

`color = GradientLUT.Sample(u = maskValue, v = meshUV.y)`. Downstream is unchanged: this colour becomes Base Color into **Cel Shaded Lighting**; the mask value still feeds `HeightToNormal`; per-object jitter still applies.

**In scope:** the LUT node, the atlas generator, wiring into `PainterlyShader`, importer settings, the row-bleed fix.
**Out of scope:** converting existing painterly materials (opt-in per material); animating the LUT; the character-part recolor (that's the sibling Directional plan).

## 2. Why this shape (the reasoning Spencer asked about)

The brush strokes must stay **uniform** regardless of how the model is UV'd, so the stroke mask keeps its **own independent UV** (world/object space, tiling as today) — decoupled from the mesh UVs, which now do nothing but *steer colour*. That decoupling is the whole trick: mesh UVs are free to be a tiny palette lookup (they don't need to cover surface detail), while the mask supplies detail at a constant scale. Efficiency: one 64×64 sample replaces per-material ramp params and lets a single material carry dozens of colours.

**← DECISION (recommended: gradient-per-row).** Two ways a region's colour can shade:
| Model | Behaviour | Verdict |
|---|---|---|
| **Gradient per row** (U = mask value) | Each row is shadow→highlight of one colour; strokes shade along the authored gradient, so shadows can hue-shift (painterly). | **Recommended** — richest look, matches "painterly based on the colour distribution". |
| **Flat colour per texel** (U also from mesh UV) | UV points at one texel = flat base colour; mask only multiplies its brightness. | Simpler/Synty-style, but shading is a plain darken — muddier, no authored shadow hue. Fallback. |

## 3. The LUT texture — `T_PainterlyGradientLUT.png`

- **64×64, one gradient per row** (up to 64 colour zones). Column = gradient position 0→1 (left = U 0 = shadow, right = U 1 = highlight).
- **Importer:** `sRGBTexture = true` (it holds real colours, unlike the linear mask), `wrapMode = Clamp` (never tile a palette), `mipmapEnabled = false` (mips would blend rows), `filterMode = Point` (see below).
- **Row-bleed gotcha (must handle):** with bilinear filtering, `v = meshUV.y` interpolates **between adjacent gradient rows** → wrong colours at region boundaries. Fix: **Point filter** + sample **row centres**: `v = (floor(meshUV.y * rowCount) + 0.5) / 64`. Point filtering also quantises U into ≤64 gradient steps — which reads as clean painterly banding, on-brand, not a defect. (If smoother U is ever wanted, lerp two adjacent columns in the node; not needed for v1.)
- **Authoring convention:** the generator lays gradients **top row = index 0** downward; it also emits a reference sheet (row index → name → the `meshUV.y` band `[row/64, (row+1)/64)`) so UV-mapping a part to a colour is lookup-not-guess.

## 4. New node — `Nodes/Painterly/PainterlyGradientMap.hlsl`

Reflection node (same conventions as `SelectChannel`/`PainterlyColor`: `#include ShaderApiReflectionSupport.hlsl` + `PainterlyCommon.hlsl`, one `UNITY_EXPORT_REFLECTION`, ProviderKey `StitchPunk.PainterlyGradientMap`, category `StitchPunk/Painterly`).

Signature (final names ← DECISION on exact ports):
```
void PainterlyGradientMap(
    UnityTexture2D lut, UnitySamplerState lutSampler,
    float maskValue,        // U — post-contrast stroke value (from SelectChannel + ValueContrast, or PainterlyColor's Mask Value out)
    float regionV,          // V — mesh UV.y (0..1)
    float rowCount,         // active rows in the atlas (default 64) — for row-centre snapping
    out float3 color)
```
Body: snap `v = (floor(regionV * rowCount) + 0.5) / rowCount`; `color = lut.Sample(lutSampler, float2(saturate(maskValue), v)).rgb`. Row-centre snap lives **in the node** so the graph can't get it wrong. Shared helper (`PainterlyGradientLookup`) goes in `PainterlyCommon.hlsl` so the combined node could adopt it later. No colour-space math here — the importer's sRGB flag delivers linear.

**PainterlyColor stays untouched** (the inline-ramp node remains for materials that want a global ramp). This node is the *atlas* alternative.

## 5. Graph wiring — `Graphs/PainterlyShader.shadergraph` (`shader-edit`)

New properties: `_GradientLUT` (Texture2D), `_GradientRowCount` (Float, default 64). Chain:
```
Mask Sample (own UV, uniform strokes) → SelectChannel → ValueContrast ──┐  (= maskValue, U)
UV.y ──────────────────────────────────────────────────────────────────┤
                                                     PainterlyGradientMap ┘→ color → (HueSatValue optional) → Base Color → Cel Shaded Lighting
ValueContrast out (maskValue) ─────────────────────────────────────────────→ HeightToNormal → Normal
```
Keep `ObjectRandom`→jitter and `HeightToNormal` exactly as today. ← DECISION: branch `PainterlyShader` in place (a `_UseGradientLUT` keyword toggling ramp vs atlas) **vs** a `PainterlyPaletteShader` variant graph (recommended — leaves existing painterly materials untouched, no keyword permutations).

## 6. Generator — `Editor/PainterlyGradientLUTGenerator.cs`

Mirrors `PainterlyMaskGenerator`/`PainterlyMaskPacker`: a `[MenuItem("Stitch Punk/Generate Painterly Gradient LUT")]` that reads a serialized `List<Gradient>` (authored in Unity's Gradient editor — no hand-painting texels), writes each gradient into its row (64 samples across), encodes `T_PainterlyGradientLUT.png` to `Assets/Textures/Painterly/`, and sets the importer (sRGB on, Clamp, no mips, Point). Overwrites in place to preserve GUID/material refs (the Packer's proven pattern). Also writes/logs the row-index reference sheet from §3. ← DECISION: gradients stored on a small `PainterlyGradientLUTSO` asset (versionable, re-generatable) vs. a window-local list.

## 7. File manifest

**New:** `Shaders/Nodes/Painterly/PainterlyGradientMap.hlsl` (+`.meta`), `Editor/PainterlyGradientLUTGenerator.cs` (+`.meta`), `Graphs/PainterlyPaletteShader.shadergraph` (or in-place edit), `Textures/Painterly/T_PainterlyGradientLUT.png` (generated), optional `Data/SOs/PainterlyGradientLUTSO.cs`.
**Edited:** `Shaders/Nodes/Painterly/PainterlyCommon.hlsl` (+`PainterlyGradientLookup` helper), `_Vault/Memories/Code/Shaders.md` (document the node + LUT convention + row-bleed gotcha).

## 8. Build phases

1. **Node + hand-made LUT.** Write `PainterlyGradientMap` + the `PainterlyCommon` helper; hand-make a 4-row test PNG (brown/grey/red/blue); wire into a test material; confirm UV.y bands select colours and the mask shades within them.
2. **Graph integration.** Variant graph `PainterlyPaletteShader` with `_GradientLUT`/`_GradientRowCount`; verify strokes stay uniform (independent mask UV) while UV.y drives colour on a real prop.
3. **Generator + authoring.** `PainterlyGradientLUTGenerator` from a `List<Gradient>`; regenerate `T_PainterlyGradientLUT.png`; emit the row reference sheet; re-map a sword's UVs to hilt/blade rows.
4. **Docs.** Update `Shaders.md`.

## 9. Verification (Editor — Spencer)

- Ph1: on the test material, moving a face's UV.y across row bands switches its colour; the brush mask still darkens/lightens within the colour (not a flat fill). No bleed at row boundaries (point + row-centre snap working).
- Ph2: a prop with two UV regions renders two colours from one material/one draw; rotating/scaling UVs does **not** change brush-stroke scale.
- Ph3: regenerate the LUT from Gradients → material updates in place (same GUID); sword shows brown hilt + grey blade, painterly shaded.

## Open decisions (collected)
- [ ] §2 — gradient-per-row (recommended) vs flat-colour-per-texel shading.
- [ ] §4 — final node port names/count (single `maskValue`+`regionV`, or expose channel/contrast inside the node too).
- [ ] §5 — variant graph `PainterlyPaletteShader` (recommended) vs in-place keyword branch.
- [ ] §6 — gradients on a `PainterlyGradientLUTSO` (recommended) vs window-local list.
- [ ] §3 — active `rowCount` default (64 max; likely far fewer real zones — a smaller count = fatter bands = more UV drift tolerance).
