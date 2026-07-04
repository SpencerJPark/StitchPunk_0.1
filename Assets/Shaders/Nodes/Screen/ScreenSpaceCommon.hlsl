// ScreenSpaceCommon.hlsl — shared, NON-exported depth/normal reconstruction
// helpers for the screen-space (outline) nodes.
//
// Fixes over the old CustomeNodes versions:
//   - ReconstructViewPosition and ComputeScreenSpaceNormal disagreed on the
//     signature (1-arg vs 2-arg call), so the old normal reconstruction never
//     compiled. The 2-arg core below is the single source of truth.
//   - The old code used _ScreenParams.zw as a texel size, but those components
//     are 1 + 1/width and 1 + 1/height — an offset of ~1.0 UV, i.e. sampling
//     the far corner of the screen. Subtracting 1 gives the intended texel.

#ifndef SCREEN_SPACE_COMMON_INCLUDED
#define SCREEN_SPACE_COMMON_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float3 ReconstructViewPositionCore(float2 screenUV, float rawDepth)
{
    return ComputeViewSpacePosition(screenUV, rawDepth, UNITY_MATRIX_I_P);
}

float3 SampleViewPosition(float2 screenUV)
{
    return ReconstructViewPositionCore(screenUV, SampleSceneDepth(screenUV));
}

float3 ComputeScreenSpaceNormalCore(float2 screenUV)
{
    float3 centerPosition = SampleViewPosition(screenUV);

    // _ScreenParams.zw = 1 + 1/resolution, so subtract 1 to get the texel size.
    float2 texelSize = _ScreenParams.zw - 1.0;

    float3 rightPosition = SampleViewPosition(screenUV + float2(texelSize.x, 0));
    float3 upPosition = SampleViewPosition(screenUV + float2(0, texelSize.y));

    float3 horizontalDelta = rightPosition - centerPosition;
    float3 verticalDelta = upPosition - centerPosition;

    return normalize(cross(horizontalDelta, verticalDelta));
}
#endif // SHADERGRAPH_PREVIEW

#endif // SCREEN_SPACE_COMMON_INCLUDED
