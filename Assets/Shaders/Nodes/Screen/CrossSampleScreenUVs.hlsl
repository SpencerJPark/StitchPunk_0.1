// CrossSampleScreenUVs — the four diagonal Roberts-cross sample UVs at SCREEN
// texel size, computed internally from _ScreenParams. This is the full
// replacement for SubGraphs/CrossSamplesUVs.shadersubgraph (which combined a
// Screen node, a divide and the old GetCrossSampleUVs custom function).
// Use the plain Cross Sample UVs node instead when sampling a texture whose
// texel size differs from the screen.

#include "ShaderApiReflectionSupport.hlsl"
#include "ScreenSpaceCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.CrossSampleScreenUVs</sg:ProviderKey>
///     <sg:DisplayName>Cross Sample Screen UVs</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "uv">
///     <sg:DisplayName>UV</sg:DisplayName>
///</paramhints>
///<paramhints name = "offsetMultiplier">
///     <sg:DisplayName>Offset Multiplier</sg:DisplayName>
///     <sg:Range>0, 8</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void CrossSampleScreenUVs(
    float2 uv,
    float offsetMultiplier,
    out float2 uvOriginal,
    out float2 uvTopRight,
    out float2 uvBottomLeft,
    out float2 uvTopLeft,
    out float2 uvBottomRight)
{
#if defined(SHADERGRAPH_PREVIEW)
    float2 texelSize = float2(1.0 / 512.0, 1.0 / 512.0);
#else
    float2 texelSize = 1.0 / _ScreenParams.xy;
#endif

    uvOriginal = uv;
    uvTopRight = uv + float2(texelSize.x, texelSize.y) * offsetMultiplier;
    uvBottomLeft = uv - float2(texelSize.x, texelSize.y) * offsetMultiplier;
    uvTopLeft = uv + float2(-texelSize.x * offsetMultiplier, texelSize.y * offsetMultiplier);
    uvBottomRight = uv + float2(texelSize.x * offsetMultiplier, -texelSize.y * offsetMultiplier);
}
