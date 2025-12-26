#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

float3 ReconstructViewPosition(float2 uv)
{
    return ComputeViewSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_P);
}