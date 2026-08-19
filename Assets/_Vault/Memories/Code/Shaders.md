# Shaders — Context

Folder: `Assets/Shaders/` — reorganized + migrated to reflection-API nodes 2026-07-04
(the old `Used/`/`Unused/`/`Subshaders/`/`CustomeNodes/` split and all `_float/_half`
custom-function files are gone):

| Folder | Holds |
|---|---|
| `Graphs/` | Production shader graphs: `2DShader`, `2DTextureArrayShader` (unit parts), `2DPackedRecolorShader` (channel-packed recolorable unit parts — see below), `3DShader` (environment/props), `PainterlyShader` (painterly single-texture environment look), `PainterlyPaletteShader` (UV-palette variant of PainterlyShader — see below) — all use the **Cel Shaded Lighting** reflection node |
| `RenderFeatures/` | Hand-written `.shader` passes driven by the renderer features: `ViewSpaceNormalsCapture`, `RobertsCrossEdgeDetection`, `SilhouetteOutline`. **This is the live outline pipeline** (`RobertsCrossRenderFeature` + `SilhouetteOutlineFeature` in `Game_Renderer.asset`) |
| `SubGraphs/` | Only `WorldSpaceSurfaceData` — bundles Position/Normal/View geometry-context nodes for the lighting inputs; correctly a subgraph (HLSL functions can't access geometry implicitly), used by all production graphs |
| `Nodes/` | **Reflection-API node library** — one Shader Graph node per `.hlsl` file (see below) |
| `Legacy/` | Parked graph experiments only (old outline/2D iterations, `TextureArray.shadersubgraph`). Nothing in production references anything here, and nothing here references deleted assets (verified — `OutlineShader.shadergraph` had to be deleted with its subgraphs because the ShaderGraph importer NREs on missing subgraph dependencies rather than failing gracefully). Delete the rest at will; git history keeps them |

## Reflection-API node convention (Unity 6.5+)

`.hlsl` files that `#include "ShaderApiReflectionSupport.hlsl"` (package-provided;
nothing under Assets) and mark **exactly ONE function per file** with
`UNITY_EXPORT_REFLECTION` + `///<funchints>`/`///<paramhints>` XML hints — the
importer turns each file into a real Shader Graph node. Hint rules: `<sg:Color/>`
only on float3/float4, `<sg:Range>` only on scalars, `<sg:Default>` =
comma-separated floats. Shared math lives in non-exported `*Common.hlsl` files
next to the nodes (no reflection include, no export macro). Texture ports work
via `UnityTexture2D`/`UnitySamplerState` parameters (`.Sample(sampler, uv)`).
Reference examples: `_Vault/Tasks/Materials/*.hlsl`. **Confirmed working
in-Editor 2026-07-04** (production graphs run on these nodes).

## Node library — `Nodes/`

ProviderKeys are `StitchPunk.<Name>`; search the Create Node menu for "StitchPunk".

- **`Nodes/Lighting/`** — `CelShadedCommon.hlsl` (core: banded diffuse, toon
  specular, rim; main light + up to 8 per-object additional lights reading
  `_AdditionalLightsCount` directly — deliberately NOT the keyword-gated call,
  don't "simplify" it back; also holds the frozen `kUniform*` edge constants and
  `CelShadedUniformCore`), **`CelShadedUniform`** (4 artist dials + shadow lift —
  **prefer this for new graphs**, see the 2026-07-28 section), `CelShadedLighting`
  (9 per-material dials, still used by the other 7 production graphs — pending
  rollout), `CelShadedLightingDebug` (keyword-gated light path + separate
  Light Count Color output for comparison debugging).
- **`Nodes/Screen/`** — `ScreenSpaceCommon.hlsl` (depth → view position/normal
  reconstruction), `ReconstructViewPosition`, `ScreenSpaceNormal`,
  `EncodeViewSpaceNormal` (world- or object-space input via toggle),
  `CrossSampleUVs`, `CrossSampleScreenUVs` (screen texel computed internally),
  `RobertsCrossDepth` (Linear01 depth, multiplier after sqrt),
  `RobertsCrossNormals` (Texture2D + Sampler ports), `NdotVTransform`
  (`smoothstep(threshold, upperEdge=2, NdotV) * multiplier + 1`). Currently
  **unreferenced by production** — they're the ready-made library for any future
  shader-graph outline rebuild (the old shader-graph outline chain was dead and
  was deleted).
- **`Nodes/Utility/`** — `IfAnyNonZero`.
- **`Nodes/Animation/`** (2026-08-19) — Shader Graph nodes over the DOTS
  Animation Toolkit package's standalone HLSL includes, so the toolkit's vertex
  and UV maths can be wired into an ordinary graph instead of a hand-written
  `.shader`. `ToolkitBillboardVertex` (billboard displacement, **vertex stage**),
  `ToolkitVatBoneSkin` / `ToolkitVatVertexFetch` (VAT, **vertex stage**,
  point/clamp sampler or limbs melt), `ToolkitFlipbookSliceUV` /
  `ToolkitFlipbookAtlasUV` (fragment).

  **These are thin wrappers and deliberately so.** The package ships portable
  HLSL that any Unity 6.5 project can `#include`; it must not depend on this
  host's reflection-node convention, or it stops being sellable. Architecture
  §6.1 planned exactly this split — "the host may wrap the same includes in its
  reflection nodes locally" — and this is that wrap. Change the maths in the
  package include, never in the node.

  Two traps worth knowing. `ToolkitBillboardVertex` reads the
  `_ToolkitCameraForward` global (written by `ToolkitCameraBinder`) rather than
  exposing a port: screen-aligned billboarding silently degrades to spherical
  when that forward is zero, and a port someone forgot to wire looks exactly
  like a working billboard that curves at the screen edges. And the wrapper
  unwraps `UnityTexture2D.tex` / `UnitySamplerState.samplerstate` before calling
  the include, because the include takes raw `Texture2D`/`SamplerState` to stay
  usable outside Shader Graph.
- **`Nodes/Sprite/`** — outline-safe multiply-tint for baked vector sprites
  (from `_Vault/Tasks/Materials/info.md`). `SpriteTint` (single zone:
  `baseColor.rgb * tint.rgb`; black outline `0*tint=0` stays black) and
  `SpriteTintMasked` (up to 3 zones via an R/G/B mask baked on the same UVs,
  outline/detail pass through the free channel). Library/hand-wire nodes — the
  production 2D graphs already do the single-zone multiply via `_BaseColor`
  (see below), so these are for new graphs or the future multi-zone parts.
  `PackedChannelRecolor` (2026-07-11) — composites a channel-PACKED mask texture
  (Texture Channel Packer output) into a recolorable sprite: R = base fill,
  G = layer 2 on top, B = layer 3 on top, A = literal output alpha. Each layer's
  RGBA color input tints with its RGB; the color's **alpha is the layer's blend
  strength** (0 = layer off — e.g. hide the bloody mouth), base layer ignores its
  alpha. Unclaimed pixels composite black, so outlines survive recoloring.
  `PackedChannelSwitch` (2026-07-11) — TWO-VARIANT packed sprite: one slice
  carries two shape/alpha pairs; a switch picks the pair. Normal = R shape ×
  color, G alpha; alt = B shape × color, A alpha. Built for hair-under-hats
  (alt = hat-hugging silhouette, same slice + rolled colour); wire Use Alt
  Shape to a per-instance float property so equipment code flips it per
  character (0–1 cross-fades). Needs a hair variant graph of the packed array
  shader (not yet built).

## Packed-channel recolor graph — `2DPackedRecolorShader` (2026-07-11)

Duplicate of `2DTextureArrayShader` with the albedo path swapped: `Sample
Texture 2D Array (_Texture2D_Array, _ImageIndex)` → **Packed Channel Recolor**
(→ its Recolored Alpha drives the Alpha block) → `× Cel Shaded Lighting →
interactable Branch`. The old sample-tint Multiply is gone — **`_BaseColor` IS
the base (R) layer color**, so the existing per-instance `BodyPartTint`
component drives the base recolor with no new plumbing (its alpha is ignored:
base is always on). `_SecondaryColor` (G) and `_TertiaryColor` (B)
(ColorShaderProperty, **Hybrid Per Instance**) feed the top layers; toggle a
layer per unit by writing its color's ALPHA (0 = hidden). Test material:
`Materials/Units/PackedRecolorTest.mat` (binds `T_Packed2` — the 8×8 head
flipbook imported as a Texture2DArray). DOTS per-instance override components
for _SecondaryColor/_TertiaryColor are **not yet written** (follow the
`BodyPartTint` pattern in `Components/Animation/AnimationComponents.cs`).

## Per-instance sprite tint — `_BaseColor` (2026-07-05)

Both `2DShader` and `2DTextureArrayShader` already multiply their sprite by a
`_BaseColor` colour property (`texture sample × _BaseColor → Cel Shaded
Lighting → Base Color`) — that IS the outline-safe multiply-tint. `_BaseColor`
is now **Hybrid Per Instance** (`overrideHLSLDeclaration:true`,
`hlslDeclarationOverride:3`) in both graphs, matching `_ImageIndex` /
`_IsInteractable`. The DOTS override is `BodyPartTint : IComponentData`
(`[MaterialProperty("_BaseColor")]`, `float4 Value`) in
`Components/Animation/AnimationComponents.cs`, baked **white (1,1,1,1)** on every
rendering body part by `BodyPartAuthoring` (alongside `ImageIndexOverride`).
White = authored colour unchanged; a skin/design system writes per-part colours
and the crowd still batches into one draw call. **Gotcha:** because the part
carries a per-instance override, color-picking `_BaseColor` on the *material*
no longer drives baked parts (the component wins) — set the entity's
`BodyPartTint.Value` to tint a specific part. Bake the sprite atlas
**white-fill / black-outline** or the multiply muddies the colour.
- **`Nodes/Painterly/`** — single-RGB-mask painterly setup (recreation of the
  Unreal color-curve workflow in `_Vault/Tasks/Materials/Transcript.md`; wiring
  guide: `_Vault/Tasks/Materials/PainterlyShader_Guide.md`, migration checklist:
  `ShaderRework_SetupGuide.md`). `PainterlyCommon.hlsl` + nodes `SelectChannel`,
  `ValueContrast`, `ColorRamp4` (VARIABLE-stop ramp: Stop Count 1–4, every stop
  has its own position slider, positions self-clamp ascending; smoothness 0 =
  hard toon bands), `RampUVFromValue` (grayscale float → **inverted** ramp U,
  V fixed 0.5 — the live colour path on `PainterlyShader`, see the 2026-07-28
  section), `HueSatValue` / `SelectChannel` / `ValueContrast` (all void+out since
  2026-07-28), `ObjectRandom` (position-hash per-instance
  variation; feed from Object node — drives hue/value jitter AND the mask UV
  shift via the `_UVJitter` slider, so moving a prop re-rolls its strokes),
  `PainterlyColor` (combined node, same
  variable-stop ramp; Mask Value output feeds `HeightToNormal`), and
  `HeightToNormal` (ddx/ddy world-space bump from the mask height — plugs into
  Cel Shaded Lighting's Normal input, since the custom-lit graphs have no
  master-stack Normal block for tangent normal maps). Mask generator:
  `Assets/_Scripts/Editor/PainterlyMaskGenerator.cs` → **Stitch Punk ▸ Generate
  Painterly Mask Texture** → `Assets/Textures/Painterly/T_PainterlyMask.png`
  (tileable, linear, R/G/B = three independent stroke layers). Hand-painted
  masks: paint three plain grayscale PNGs (`Mask_R/G/B.png`, same folder), then
  pack them with **Window ▸ Stitch Punk ▸ Texture Channel Packer** — wire each
  file's R channel into the output R/G/B slots, bake over `T_PainterlyMask.png`,
  and save a recipe beside it for one-click repacks. The bake overwrites in
  place, preserving GUID/import settings. (This replaced the fixed-purpose
  `PainterlyMaskPacker.cs` menu item, deleted 2026-07-09.) See [[Editor]].

## Cel shading collapsed to 4 artist dials — `CelShadedUniform` (2026-07-28)

`PainterlyShader` **only** so far. The other seven graphs still use `CelShadedLighting` — see the
rollout note at the end of this section.

The old node exposed **nine** per-material lighting dials, seven of which are band-shape values that
must NOT vary between objects if a scene is to read as one style. `Nodes/Lighting/CelShadedUniform.hlsl`
(`StitchPunk.CelShadedUniform`, guid `ad6e4220eb1d45b4834a8b3a36f18b87`) exposes four instead:

| Input | Property | Notes |
|---|---|---|
| Shininess | `_Smoothness` (reused) | tightens **and** strengthens the specular |
| Rim Strength | `_RimStrength` (new) | **decoupled** from shininess — matte objects can still rim |
| Shadow Lift | `_ShadowLift` (new) | how far shadows come up off pure black |
| Shadow Tint | `_ShadowTint` (new) | colour shadows settle toward |

- **The seven `kUniform*` edge constants are frozen at the top of `CelShadedCommon.hlsl`.** Tuning the
  look is a one-line edit there that moves every material at once. Do not re-expose them.
- **`CelShadedLighting` was NOT touched.** It is placed in 8 graphs and a signature change there
  renumbers slots in all of them — the single easiest way to break every shader in the project. The
  new node is a parallel addition; rollout is per-graph node-swap, not a signature edit.
- **Shadow Lift is genuinely new math.** `CelShadedLightingCore` bottoms out at literally zero
  (`color = lightColor * (diffuse + max(specular, rim))`), so a fully shadowed fragment multiplied
  albedo by 0 and went **pure black** — there was no way to say "dark but still coloured".
  `CelShadedUniformCore` ends with `color = shadowTint * lift + accumulated * (1 - lift)`.
  **lift 0 reproduces the old math exactly**; lift 1 is fully flat.
- **Why this was overdue:** `Painterly.mat` had `_EdgeSpecularOffset: -35.79`, `_EdgeSpecular: -2.63`,
  `_RimThreshold: 2` — far outside their declared 0–1 sliders. That makes the specular smoothstep
  `smoothstep(-36.6, -38.4, spec)` with edge0 > edge1, which saturates to **1 everywhere**: the
  "specular" was a flat `+0.7` constant being used as a fake ambient. Un-exposing/removing those
  properties snaps the look to the frozen defaults, so **the material will change visibly** — the
  previous state was accidental, not authored.
- `Painterly Color` (21 slots, only `maskValue` live) was replaced by **`Select Channel` →
  `Value Contrast`**, which is exactly what `maskValue` computed. 22 dead properties removed
  (`_ColorA-D`, `_PositionA-D`, `_StopCount`, `_RampSmoothness`, `_Saturation`, `_Value`,
  `_HueJitter`, `_ValueJitter`, `_RimThreshold`, the 7 `_Edge*`). Exposed properties: **20 → 15**.
- **`SelectChannel` and `ValueContrast` converted to `void`+`out`** (like `HueSatValue` before them),
  since no returning reflected node had ever been placed in a graph and the slot ids were unverified.
  Both were in zero graphs. The Painterly node library is now uniformly `void`+`out`.

### Rollout to the other 7 graphs — not done

`2DShader`, `2DArrayShader`, `2DPackedArrayShader`, `2DViewSwitchingPackedArrayShader`, `3DShader`,
`PainterlyPaletteShader`, `PainterlyZoneGradientShader`. Per graph: swap the `Cel Shaded Lighting`
ProviderNode for `Cel Shaded Uniform` (8 slots vs 13), re-point `_Smoothness`/Position/Normal/View,
add `_RimStrength`/`_ShadowLift`/`_ShadowTint`, delete `_RimThreshold` + the 7 `_Edge*` properties.
Deliberately deferred until the frozen constants are confirmed on `PainterlyShader` in-Editor —
tuning them after 8 graphs are converted is 8 graphs to redo.

### ⚠ Editing a graph while its Shader Graph window is open will clobber one side

During this change the on-disk graph twice diverged from what the surgery script wrote: an edge into
`Luminance Ramp UV` was re-sourced, `SurfaceDescription.Alpha` lost its input entirely (everything
renders opaque), `_IsInteractable` flipped exposed→hidden, and a stray `_Offset` PropertyNode
vanished. Replaying the identical script on the identical backup produced the **correct** result, so
the script was not at fault — an open editor was writing its in-memory copy over the file.
**Close the Shader Graph window before scripted surgery, and reopen it after.** Verify block inputs
(`BaseColor`, `Alpha`) after any edit — a disconnected Alpha block still validates ALL CLEAN.

## PainterlyShader colour = inverted-luminance gradient map through a baked ramp (2026-07-28)

**This supersedes the `_GradientLUT` / `_GradientRow` half of the 2026-07-27 section below for
`PainterlyShader` only.** The base graph no longer picks a colour by UV row — it does a true
gradient map: the stroke texture's luminance chooses a position along a single-ramp texture.

Albedo chain now reads:

```
_MainTex Sample .RGBA -> Select Channel (_Channel) -> Value Contrast (_Contrast) -+-> Height To Normal
                                                                                  |
                                            Ramp UV From Value <------------------+
                                                     |
                                            _RampTex Sample .UV
                                                     |
                        Hue Sat Value (_HueShift) -> lit Multiply.A -> Lerp -> Branch -> BaseColor
```

- **`Nodes/Painterly/RampUVFromValue.hlsl`** (`StitchPunk.RampUVFromValue`, guid
  `9fec3781418c4307ad718bc9f03c7716`) — takes the grayscale float and **inverts** it:
  value 1.0 (light) → U 0.0 (ramp start), 0.0 (dark) → U 1.0 (ramp end). **Author ramps
  left-to-right as highlight → shadow.** V hardwired to 0.5 (one ramp per texture, every row
  identical — no row to pick).
- **`Value Contrast`'s output is the single grayscale source of truth**, feeding BOTH the ramp
  lookup and `Height To Normal`. That is what makes `_Channel` and `_Contrast` shape colour and
  bump identically.
- ⚠ **Never drive a ramp lookup from a luminance of the raw `_MainTex` sample.** `_MainTex` is the
  packed stroke mask: R/G/B/A are four INDEPENDENT noise layers, not the channels of one colour.
  `dot(rgb, Rec.709)` averages all of them together, so the ramp ignores `_Channel` entirely and the
  slider appears to affect only the normal/bump path. This shipped briefly (a `LuminanceRampUV` node,
  deleted 2026-07-28) and is exactly the bug it caused. Extract one channel first, always.
- **`HueSatValue` signature changed** from `float3 HueSatValue(...)` to `void HueSatValue(...,
  out float3 outputColor)`, and its first param `color` → `inputColor`. It was placed in **zero**
  graphs at the time, so no slot renumbering fallout. Reason: no returning reflected node had ever
  been placed in this project, so return-value slot ids were unverified — void+out is the proven
  pattern. Slots: 1 inputColor, 2 hueShift, 3 saturation, 4 value, 5 outputColor.
- Only `hueShift` is wired (to the **re-exposed `_HueShift` property**, reusing the old painterly
  hue dial rather than adding a duplicate). `saturation`/`value` sit at their 1.0 slot defaults —
  wire them to properties if per-material sat/value is ever wanted.
- **`_RampTex` was broken**: serialized `m_GeneratePropertyBlock: false` +
  `hlslDeclarationOverride: 2` (**Global**), so it was not a material property at all and
  `material.SetTexture("_RampTex", …)` silently did nothing (`Painterly.mat` had it null). Now a
  normal per-material texture slot. **If a ramp ever "doesn't apply", check this flag first.**
- **Swept:** the dead `Vector 2` node + `_GradientRow` property (leftovers from the 64×64 LUT
  path — the Vector2's output was already connected to nothing). The graph's
  `m_CustomEditorGUI` hook was cleared.
- `Painterly Color` was replaced by `Select Channel` → `Value Contrast` in the same-day cel-shading
  pass (see the section above); `_Channel` and `_Contrast` drive **both** colour and bump.
- **Retired:** `Editor/RampShaderGUI.cs` (deleted). It baked a runtime `new Texture2D` that was
  never an asset, so the reference could not survive serialization or exist in a build — a second,
  independent reason the old setup could not have worked. `Shaders/ColorRampShader.shader` (a
  scratch UV-ramp test used by `Materials/Painterly 1.mat`) lost its `CustomEditor` line with it.

### Ramp authoring — `ColorRampSO` + `ColorRampGenerator` (2026-07-28)

- `Data/SOs/ColorRampSO.cs` — one asset per ramp: `Gradient gradient`, `width` (8–1024, default
  256), `sRGB`. Create via **Assets ▸ Create ▸ Colors ▸ Color Ramp**.
- `Editor/ColorRampGenerator.cs` — **Bake Ramp Texture** button on the SO's inspector (with a live
  gradient preview + the current on-disk texture), and **Stitch Punk ▸ Bake All Color Ramps**.
  Output: `Assets/Textures/ColorRamps/T_Ramp_<asset name>.png`, 8px tall (readable Project-view
  thumbnail; V is irrelevant), **sRGB / Clamp / Bilinear / no mips / uncompressed**.
  **Clamp is load-bearing** — a pure-white or pure-black pixel lands exactly on U 0 / U 1 and must
  hold the end colour, not wrap to the opposite end of the ramp.
- Bakes **overwrite the same path**, so the PNG's GUID and import settings survive and materials
  never lose their ramp. **Renaming the SO mints a new texture** and orphans the old one.
- Unlimited stops and Blend/Fixed mode come free from Unity's gradient editor — **Fixed mode is how
  you get hard cel bands**, no shader work involved.

## Painterly ramp is now gradient-LUT-driven, not an analytic 4-stop ramp (2026-07-27)

`PainterlyColor`'s built-in ramp (`ColorA-D`/`PositionA-D`/`StopCount`/`RampSmoothness`, plus its
post-ramp `HueShift`/`Saturation`/`Value`/`HueJitter`/`ValueJitter`) is **no longer the live colour
path** on any of the three production painterly graphs — those 15 properties still exist on the node
(its HLSL signature wasn't touched, to avoid a cross-graph slot-renumber) but are now hidden from the
material Inspector (`m_GeneratePropertyBlock: false`) because their computed `color` output is either
unused (`PainterlyPaletteShader`, `PainterlyZoneGradientShader` — always used only `maskValue`) or,
on `PainterlyShader`, has been rewired away entirely. Do not re-expose them; add real inputs to the
new gradient path below instead.

- **`PainterlyShader`** (the base "big general mix" graph — rocks/clutter/single-continuous-blend
  props) now samples the shared `_GradientLUT` instead of computing `Painterly Color`'s analytic
  ramp: `Painterly Color`'s `maskValue` output (channel-select + contrast, unchanged) feeds a
  hand-built **Vector 2** node's X; a new single-float property **`_GradientRow`** (0–1, same
  row-band convention as `PainterlyPaletteShader`'s mesh-UV.y) feeds its Y; the Vector2 Out feeds a
  new `Sample Texture 2D` on `_GradientLUT`, whose RGBA replaces `Painterly Color`'s old `color`
  output at the lit-colour Multiply. **`_GradientRow` is the one artist-facing "which gradient" dial**
  — the ramp itself is authored once as a `Gradient` entry in `PainterlyGradientLUTSO` (Blend or Fixed
  mode, unlimited stops via Unity's native gradient-key editor), not per-material colour/position
  fields. Hue-shift/jitter scrolling (discussed but not built) would be a future add-on — right now
  the ramp position is pure `maskValue`, no per-instance variation.
- **`Vector2Node`** had no live instance anywhere in the project to clone from, so it was hand-built
  from explicit, individually-templated slots (X/Y from a plain `Vector1MaterialSlot`, Out from a
  plain `Vector2MaterialSlot`) rather than relying on Unity to backfill a minimal stub — **this is the
  one piece of this change without an in-repo precedent to verify against; check Console after import**
  for anything referencing this node before trusting it further.
- **`PainterlyPaletteShader` / `PainterlyZoneGradientShader`** only got the property-hiding half of
  this (their `Painterly Color` ramp `color` output was already dead) — no wiring changed there.

## Painterly palette atlas — `PainterlyPaletteShader` (2026-07-04)

A UV-driven palette variant of `PainterlyShader`: the mesh UV samples a **64×64 gradient atlas**
(`_GradientLUT`) to pick a **base colour per part** (sword hilt → brown row, blade → grey row);
Cel Shaded Lighting does the shading. The stroke mask (`_MainTex`) no longer drives colour here —
it feeds **only** Height To Normal (the painterly surface). A material needs BOTH textures set.

- **Node:** `Nodes/Painterly/PainterlyGradientMap.hlsl` (`StitchPunk.PainterlyGradientMap`) —
  `saturate(uv)` sample of the LUT, colour out only. Thin, self-contained (no `PainterlyCommon`).
- **Graph:** `Graphs/PainterlyPaletteShader.shadergraph` — built by duplicating `PainterlyShader`
  and swapping the albedo source: a built-in **Sample Texture 2D** (`_GradientLUT`, mesh UV) feeds
  the albedo Multiply's A input in place of `Painterly Color`'s colour out. `Painterly Color`
  stays (its Mask Value out still drives Height To Normal). The graph uses the **built-in** sampler
  rather than the reflected node — cloning reflected texture/sampler slots has no safe same-file
  template; behaviour matches because the LUT imports **Clamp**. Swap the node in by hand if wanted.
- **LUT authoring:** `Data/SOs/PainterlyGradientLUTSO.cs` (a `List<Gradient>`, one per colour zone)
  → **Stitch Punk ▸ Generate Painterly Gradient LUT** (`Editor/PainterlyGradientLUTGenerator.cs`)
  bakes `Textures/Painterly/T_PainterlyGradientLUT.png` (gradient index 0 = top band; equal row
  bands give UV tolerance) + a `_rows.txt` reference sheet (UV.y band → zone). Importer: **sRGB on,
  Clamp, Point, no mips, uncompressed** — Point + Clamp keep colour zones crisp (no row bleed).
  Each `Gradient`'s own **Blend/Fixed mode** (Unity's built-in gradient editor) controls whether its
  row is a smooth ramp (Blend) or a hard-edged swatch strip with zero interpolation (Fixed) — a "car
  paint colours" row with 6 distinct picks is just a Fixed-mode gradient, no code involved.

## Zone-based item recolor — `PainterlyZoneGradientShader` (2026-07-27)

Multi-part item recolor (e.g. a car with independently-coloured body/trim/glass) built by combining
the packed-channel zone idea from `2DPackedRecolorShader` with the gradient-LUT palette idea from
`PainterlyPaletteShader`, so items pull colour from the same artist-authored, unlimited-stop gradient
resource characters and painterly props already use — no raw colour pickers.

- **Graph:** `Graphs/PainterlyZoneGradientShader.shadergraph` — duplicated from `PainterlyPaletteShader`
  then extended via scripted graph surgery (`shadergraph_lib.py`), not hand-wired in the Editor.
- **New texture property `_ZoneMask`** (separate from `_MainTex`, sampled at **plain, unjittered mesh
  UV** — zone identity must stay exact per copy, unlike the stroke mask's per-instance jitter): a
  channel-packed mask (Texture Channel Packer, same convention as character part masks) where R/G/B
  identify up to 3 colourable zones. Author its channels with real grayscale/brush variation, not flat
  0/1 fills — that variation is what reads as painterly shading once multiplied through.
- **Three `_ZoneXPaletteUV` Vector2 properties** (`_ZoneAPaletteUV/_ZoneBPaletteUV/_ZoneCPaletteUV`):
  each is a raw UV into the shared `_GradientLUT` (X = position along that row's gradient/swatch strip,
  Y = which row/family) sampled via a built-in Sample Texture 2D (same "no reflected-node texture
  clone" precedent as `PainterlyPaletteShader`) — NOT mesh-driven, these are per-material (or later,
  per-instance) picks into the curated palette.
- **`Packed Channel Recolor` node** (cross-graph-cloned from `2DPackedRecolorShader`, unmodified):
  `packedSample` = `_ZoneMask` sample, `baseLayerColor/secondLayerColor/thirdLayerColor` = the three
  LUT samples instead of literal `_BaseColor/_SecondaryColor/_TertiaryColor` properties. Its
  `recoloredColor` output replaces `PainterlyPaletteShader`'s single LUT-sample-by-mesh-UV as the input
  to the existing lit-colour Multiply — everything downstream (Cel Shaded Lighting, interactable
  Branch, BaseColor block) is untouched. `recoloredAlpha` is left unconnected; Alpha still comes from
  the stroke mask's alpha channel, same as `PainterlyPaletteShader`.
- Stroke mask (`_MainTex`) → `Painterly Color` → `Height To Normal` path is **fully unchanged** — zone
  colour and painterly surface detail stay decoupled, same as the palette shader.
- No new `.hlsl` node files — this is pure graph wiring reusing `PackedChannelRecolor` and the
  built-in `Sample Texture 2D` node exactly as they already work elsewhere.
- **Not yet done:** no test material, and no per-instance (DOTS) variant of the `_ZoneXPaletteUV`
  properties — those are plain material properties for now (fixed-per-item-type). Follow the
  `BodyPartTint` pattern in `Components/Animation/AnimationComponents.cs` if per-instance variation
  is needed later.

## Custom hand-authored mips (Texture Packer) — you set the count, no auto-fill (2026-07-07)

`Editor/TextureArrayBuilder.cs` (`TexturePackerBuilder` custom inspector on `TexturePackerConfig`)
builds a `Texture2D` or `Texture2DArray` from **explicitly assigned per-level textures** — so mips
are hand-authored (painterly/pixel control) instead of Unity's box-filter blur. How it works:

- **The mip count *is* your array length.** It allocates `new Texture2D(baseSize, baseSize, format,
  mips.Length, linear)` (array path: `mipCount = slices[0].mips.Length`, and every slice must match
  or it errors `inconsistent mip count`), `SetPixels(src.GetPixels(), m)` per level, then
  **`Apply(false)`** — the `false` means **do NOT auto-generate mips**. Unity never fills missing
  lower levels; the chain ends at your last provided one.
- **Stopping early is fine — sampling clamps to your smallest level.** You do *not* need to go down
  to 1×1. Mips only matter for **minification** (sprite drawn smaller than native); with our fairly
  fixed 2.5D camera, slices rarely render far below native, so a shallow chain is plenty.
- **Per-level requirements:** each `mips[m]` must be the exact halved dimension (mip0 = baseSize²,
  mip1 = (baseSize/2)², …) or `SetPixels` throws; every source needs **Read/Write enabled** (else
  `needs Read/Write enabled`). Output asset is written next to the config (`_Tex.asset` / `_Array.asset`).

**Decision (2026-07-07):** for the 256px unit slices we author the chain down to **32** only
(256 → 128 → 64 → 32 = 4 levels, mips 0–3) and stop. Below 32 is imperceptible at our on-screen
sizes and just clamps. Only add a 16 level if the most zoomed-out framing shrinks a unit to a few
pixels and shows shimmer.

## Outline features are incompatible with MSAA — use post-process AA (2026-07-06)

**Do not turn on MSAA in the URP asset (`Game_RPAsset.asset`, `m_MSAA`). Keep it at 1
(off).** The two outline renderer features (`RobertsCrossRenderFeature`,
`SilhouetteOutlineFeature` in `Core/RenderFeatures/`) fundamentally conflict with MSAA:

- Each capture pass renders normals/silhouette into an offscreen color RT while sharing
  the camera depth read-only via `SetRenderAttachmentDepth(activeDepthTexture, Read)`
  (so occluded outline-layer geometry isn't outlined through walls). Render Graph
  requires all attachments in a pass to agree on MSAA sample count.
- Turn MSAA on → the shared depth is multisampled → the capture throws
  **"Mismatch in number of MSAA samples ... Expected 2 but got None"** (the trailing
  `ZBinningJob`/`ForwardLights` error is just cascade noise — ignore it). This is *our*
  Normal Capture Pass, **not** SSAO/Decals/Depth-Priming (none of those are on the
  renderer — don't chase them).
- Matching the capture RT's `msaaSamples` to the camera makes it compile but then the
  outline shader (`RobertsCrossEdgeDetection.shader`, plain `TEXTURE2D` +
  `SAMPLE_TEXTURE2D`) can't sample a multisampled texture → *"A multisampled texture
  being bound to a non-multisampled sampler. Disabling ..."* → outline silently gone.
  Tried and reverted; would need a `Texture2DMS` shader rewrite or an extra resolve pass.

**Resolution (chosen): post-process AA instead of MSAA.** For flat 2.5D quads MSAA only
AAs geometry edges anyway — the visible aliasing is on shader-drawn outlines and sprite
alpha, which post AA handles and MSAA does not. The game camera prefab
`Assets/Prefabs/SetUp/Main Camera 1.prefab` is already configured this way:
`m_RenderPostProcessing: 1`, `m_Antialiasing: 2` (**SMAA**), `m_AntialiasingQuality: 2`.
So the config is already correct — just never raise `m_MSAA`. Switch-perf alt: FXAA
(`m_Antialiasing: 1`) is cheaper than SMAA if needed.

## Migration log (2026-07-04, complete)

- All `_float/_half` custom-function HLSL converted or deleted; the three
  production graphs rewired to **Cel Shaded Lighting** in-Editor and
  `LightingCelShaded.hlsl` deleted.
- Dead code removed after a usage audit: `PostProcessing.shadergraph` (referenced
  by nothing — the outline was never shader-graph in production), the six outline
  subgraphs (only consumer was the parked `Legacy/OutlineShader`),
  `GetCrossSampleUVs.hlsl`, and `Core/RenderFeatures/CelShadingFeature.cs`
  (not on the renderer; its `Shader.Find("Hidden/CelShading")` target never existed).
- Bug fixes made during conversion (live in the Screen nodes): the old
  `ComputeScreenSpaceNormal` never compiled (1-arg vs 2-arg signature mismatch)
  and used `_ScreenParams.zw` (= 1 + 1/res) as a texel size; the old debug
  lighting file always returned its light-count overlay.
