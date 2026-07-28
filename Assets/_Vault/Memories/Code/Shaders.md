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
  don't "simplify" it back), `CelShadedLighting` (used by all four production
  graphs), `CelShadedLightingDebug` (keyword-gated light path + separate
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
  hard toon bands), `HueSatValue`, `ObjectRandom` (position-hash per-instance
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
