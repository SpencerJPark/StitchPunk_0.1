// EncodeViewSpaceNormal — transforms a normal to view space and encodes it
// into 0..1 for writing to a normals buffer (the ViewSpaceNormalsCapture
// pass / outline pipeline).
//
// Replaces BOTH old files: ViewSpaceNormals.hlsl (took a world-space normal)
// and EncodeViewNormal.hlsl (took an object-space normal). Set
// Normal Is Object Space to 1 to get the old EncodeViewNormal behaviour.

#include "ShaderApiReflectionSupport.hlsl"

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#endif

///<funchints>
///     <sg:ProviderKey>StitchPunk.EncodeViewSpaceNormal</sg:ProviderKey>
///     <sg:DisplayName>Encode View Space Normal</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "normal">
///     <sg:DisplayName>Normal</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalIsObjectSpace">
///     <sg:DisplayName>Normal Is Object Space</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 EncodeViewSpaceNormal(float3 normal, float normalIsObjectSpace)
{
#if defined(SHADERGRAPH_PREVIEW)
    return normal * 0.5 + 0.5;
#else
    float3 worldNormal = normal;
    if (normalIsObjectSpace >= 0.5)
    {
        worldNormal = TransformObjectToWorldNormal(normal);
    }
    float3 viewNormal = TransformWorldToViewDir(worldNormal);
    return viewNormal * 0.5 + 0.5;
#endif
}
