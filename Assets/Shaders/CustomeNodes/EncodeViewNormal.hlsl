#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

void EncodeViewNormal_float(float3 NormalOS, out float3 Out)
{
    // Transform object space normal to view space
    float3 normalWS = TransformObjectToWorldNormal(NormalOS);
    float3 normalVS = TransformWorldToViewDir(normalWS);
    
    // Encode
    Out = normalVS * 0.5 + 0.5;
}