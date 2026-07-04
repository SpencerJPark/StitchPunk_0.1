// ScreenSpaceNormal — reconstructs a view-space normal purely from the depth
// texture (cross product of horizontal/vertical position deltas). Useful for
// outline edge detection without a normals prepass. Fullscreen / post-process
// graphs only.

#include "ShaderApiReflectionSupport.hlsl"
#include "ScreenSpaceCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ScreenSpaceNormal</sg:ProviderKey>
///     <sg:DisplayName>Screen Space Normal</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "screenUV">
///     <sg:DisplayName>Screen UV</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 ScreenSpaceNormal(float2 screenUV)
{
#if defined(SHADERGRAPH_PREVIEW)
    return float3(0, 0, 1);
#else
    return ComputeScreenSpaceNormalCore(screenUV);
#endif
}
