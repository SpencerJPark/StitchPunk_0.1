// ReconstructViewPosition — view-space position of the scene surface under a
// screen UV, reconstructed from the depth texture. Requires the URP Depth
// Texture to be enabled (it is, for the outline pipeline). Fullscreen /
// post-process graphs only.

#include "ShaderApiReflectionSupport.hlsl"
#include "ScreenSpaceCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ReconstructViewPosition</sg:ProviderKey>
///     <sg:DisplayName>Reconstruct View Position</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "screenUV">
///     <sg:DisplayName>Screen UV</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 ReconstructViewPosition(float2 screenUV)
{
#if defined(SHADERGRAPH_PREVIEW)
    return float3(0, 0, 0);
#else
    return SampleViewPosition(screenUV);
#endif
}
