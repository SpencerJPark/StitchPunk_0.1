# Shaders — Context

Folder: `Assets/Shaders/` — reorganized + migrated to reflection-API nodes 2026-07-04
(the old `Used/`/`Unused/`/`Subshaders/`/`CustomeNodes/` split and all `_float/_half`
custom-function files are gone):

| Folder | Holds |
|---|---|
| `Graphs/` | Production shader graphs: `2DShader`, `2DTextureArrayShader` (unit parts), `3DShader` (environment/props), `PainterlyShader` (painterly single-texture environment look) — all four use the **Cel Shaded Lighting** reflection node |
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
  (tileable, linear, R/G/B = three independent stroke layers).

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
