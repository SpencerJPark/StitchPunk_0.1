// CrossSampleUVs — the four diagonal sample UVs (plus the original) for
// Roberts-cross edge detection. Replaces the GetCrossSampleUVs_float custom
// function used by SubGraphs/CrossSamplesUVs.shadersubgraph.
//
// Change from the old version: the UV input is a float2 (the old float4 input
// was silently truncated to .xy anyway).

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.CrossSampleUVs</sg:ProviderKey>
///     <sg:DisplayName>Cross Sample UVs</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "uv">
///     <sg:DisplayName>UV</sg:DisplayName>
///</paramhints>
///<paramhints name = "texelSize">
///     <sg:DisplayName>Texel Size</sg:DisplayName>
///</paramhints>
///<paramhints name = "offsetMultiplier">
///     <sg:DisplayName>Offset Multiplier</sg:DisplayName>
///     <sg:Range>0, 8</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void CrossSampleUVs(
    float2 uv,
    float2 texelSize,
    float offsetMultiplier,
    out float2 uvOriginal,
    out float2 uvTopRight,
    out float2 uvBottomLeft,
    out float2 uvTopLeft,
    out float2 uvBottomRight)
{
    uvOriginal = uv;
    uvTopRight = uv + float2(texelSize.x, texelSize.y) * offsetMultiplier;
    uvBottomLeft = uv - float2(texelSize.x, texelSize.y) * offsetMultiplier;
    uvTopLeft = uv + float2(-texelSize.x * offsetMultiplier, texelSize.y * offsetMultiplier);
    uvBottomRight = uv + float2(texelSize.x * offsetMultiplier, -texelSize.y * offsetMultiplier);
}
