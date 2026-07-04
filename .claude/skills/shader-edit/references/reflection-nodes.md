# Authoring reflection-API HLSL nodes (Unity 6.5+)

Each `.hlsl` file under `Assets/Shaders/Nodes/` that follows this recipe
becomes a real Shader Graph node — no Custom Function node, no subgraph.

## The recipe

```hlsl
// <NodeName> — one-paragraph comment: what it does, where it's meant to be
// wired, and any non-obvious constraint (fragment-only, expects world space…).

#include "ShaderApiReflectionSupport.hlsl"   // package-provided; resolves automatically
#include "PainterlyCommon.hlsl"              // optional shared math (relative include works)

///<funchints>
///     <sg:ProviderKey>StitchPunk.<NodeName></sg:ProviderKey>
///     <sg:DisplayName><Node Name></sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/<Category></sg:SearchCategory>
///</funchints>
///<paramhints name = "someScalar">
///     <sg:DisplayName>Some Scalar</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.5</sg:Default>
///</paramhints>
///<paramhints name = "someColor">
///     <sg:DisplayName>Some Color</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 0.5, 0.25</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void NodeName(
    float someScalar,
    float3 someColor,
    out float3 result)
{
    result = someColor * someScalar;
}
```

Conventions (match the existing library):
- Function name == file name == last segment of the ProviderKey.
- Categories in use: `StitchPunk/Painterly`, `StitchPunk/Lighting`,
  `StitchPunk/Screen`, `StitchPunk/Utility`. Search "StitchPunk" in the
  Create Node menu shows the whole library.
- Descriptive camelCase parameter names — they become `m_ShaderOutputName`
  in serialized graphs and read like documentation.
- Explicit types, no single-letter names (project-wide rule applies to HLSL).

## Hint validation is strict and type-aware

- `<sg:Color/>` — only on `float3`/`float4` (and half variants).
- `<sg:Range>lo, hi</sg:Range>` (synonym `Slider`) — only on scalar float/half.
  Produces a `Vector1MaterialRangeSlot` port with a slider.
- `<sg:Default>` — comma-separated floats matching the param width.
- A wrong hint fails the whole file's import; the Console names the file.

## Signature → ports

- Ports number sequentially from 1 in parameter order; `out` params become
  output ports. **Prefer `void` + `out` over return values** — the numbering
  is then fully predictable (verified pattern: 12 ins + 1 out = slots 1..13).
- `in`, `out`, `inout` all supported; multiple outputs are fine.

## Textures

`UnityTexture2D` + `UnitySamplerState` parameters become Texture/Sampler
ports. Sample through the wrapper's method — the raw macro does not accept
the wrapper structs:

```hlsl
float4 sampleValue = someTexture.Sample(someSampler, uv);
```

## SHADERGRAPH_PREVIEW guards

URP lighting/depth includes and light-loop functions don't exist in node
preview compilation. Any node touching `Lighting.hlsl`, `SampleSceneDepth`,
etc. needs the same two-part guard the library files use:

```hlsl
#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
...
#endif

// inside the function:
#if defined(SHADERGRAPH_PREVIEW)
    result = <neutral value>;
#else
    <real implementation>
#endif
```

Pure-math nodes need no guard. `ddx/ddy` are fine (fragment stage), but note
the node then only works in fragment wiring.

## Shared math files (`*Common.hlsl`)

- Include guard, NO `ShaderApiReflectionSupport.hlsl`, NO export macro.
- Prefix functions (`Painterly*`, `CelShaded*`) to avoid collisions — every
  node file including it gets these symbols.
- Put real math here and keep the exported function a thin wrapper whenever a
  combined node needs to reuse it (see `PainterlyColor` reusing the same
  helpers as the small nodes).

## Meta files

If a graph edit will reference the new node before Unity imports it, write
the meta yourself (`python -c "import uuid; print(uuid.uuid4().hex)"`):

```
fileFormatVersion: 2
guid: <32 hex chars>
ShaderIncludeImporter:
  externalObjects: {}
  userData:
  assetBundleName:
  assetBundleVariant:
```

## Existing library (grep before writing — the node may exist)

- `Nodes/Lighting/` — `CelShadedLighting` (the production toon lighting; reads
  `_AdditionalLightsCount` directly ON PURPOSE, don't "fix" it to the
  keyword-gated call), `CelShadedLightingDebug` (gated path + light-count
  output), `CelShadedCommon.hlsl`.
- `Nodes/Painterly/` — `SelectChannel`, `ValueContrast`, `ColorRamp4`
  (variable 1–4 stops, per-stop positions, self-clamping ascending),
  `HueSatValue`, `ObjectRandom` (position-hash instance variation),
  `PainterlyColor` (combined chain), `HeightToNormal` (ddx/ddy world-space
  bump — used because the custom-lit graphs have no master-stack Normal
  block), `PainterlyCommon.hlsl`.
- `Nodes/Screen/` — `ReconstructViewPosition`, `ScreenSpaceNormal`,
  `EncodeViewSpaceNormal`, `CrossSampleUVs`, `CrossSampleScreenUVs`,
  `RobertsCrossDepth`, `RobertsCrossNormals`, `NdotVTransform`,
  `ScreenSpaceCommon.hlsl` — outline-rebuild library, currently unreferenced
  by production.
- `Nodes/Utility/` — `IfAnyNonZero`.
