#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float3 ComputeScreenSpaceNormal(float2 uv)
{
    float depth = SampleSceneDepth(uv);

    float3 center = ReconstructViewPosition(uv, depth);

    float2 offset = float2(_ScreenParams.z, _ScreenParams.w);

    float3 right = ReconstructViewPosition(uv + float2(offset.x, 0),
                                           SampleSceneDepth(uv + float2(offset.x, 0)));

    float3 up = ReconstructViewPosition(uv + float2(0, offset.y),
                                        SampleSceneDepth(uv + float2(0, offset.y)));

    float3 dx = right - center;
    float3 dy = up - center;

    return normalize(cross(dx, dy));
}
