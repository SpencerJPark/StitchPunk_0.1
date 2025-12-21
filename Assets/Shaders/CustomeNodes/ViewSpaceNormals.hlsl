#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

void ViewSpaceNormals_float(float3 WorldNormal, out float3 Out)
{
    float3 viewNormal = TransformWorldToViewDir(WorldNormal);
    Out = viewNormal * 0.5 + 0.5;
}