// RobertsCrossNormals — normals-based Roberts-cross edge value. Replacement
// for SubGraphs/RobertsCrossNormals.shadersubgraph: samples the captured
// view-space-normals buffer (the ViewSpaceNormalsCapture render feature
// output) at the four diagonal UVs and returns
// sqrt(dot(a, a) + dot(b, b)) where a/b are the diagonal normal differences —
// same math as the subgraph, samples used raw (no *2-1 decode), matching it.
//
// NOTE: this node takes Texture2D + SamplerState ports. If the reflection
// importer rejects the texture parameter types, keep using the subgraph and
// delete this file.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.RobertsCrossNormals</sg:ProviderKey>
///     <sg:DisplayName>Roberts Cross Normals</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "normalsTexture">
///     <sg:DisplayName>Normals Texture</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalsSampler">
///     <sg:DisplayName>Sampler</sg:DisplayName>
///</paramhints>
///<paramhints name = "uvTopRight">
///     <sg:DisplayName>UV Top Right</sg:DisplayName>
///</paramhints>
///<paramhints name = "uvBottomLeft">
///     <sg:DisplayName>UV Bottom Left</sg:DisplayName>
///</paramhints>
///<paramhints name = "uvTopLeft">
///     <sg:DisplayName>UV Top Left</sg:DisplayName>
///</paramhints>
///<paramhints name = "uvBottomRight">
///     <sg:DisplayName>UV Bottom Right</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
float RobertsCrossNormals(
    UnityTexture2D normalsTexture,
    UnitySamplerState normalsSampler,
    float2 uvTopRight,
    float2 uvBottomLeft,
    float2 uvTopLeft,
    float2 uvBottomRight)
{
    float3 normalTopRight = normalsTexture.Sample(normalsSampler, uvTopRight).rgb;
    float3 normalBottomLeft = normalsTexture.Sample(normalsSampler, uvBottomLeft).rgb;
    float3 normalTopLeft = normalsTexture.Sample(normalsSampler, uvTopLeft).rgb;
    float3 normalBottomRight = normalsTexture.Sample(normalsSampler, uvBottomRight).rgb;

    float3 diagonalDifferenceA = normalTopRight - normalBottomLeft;
    float3 diagonalDifferenceB = normalTopLeft - normalBottomRight;

    return sqrt(dot(diagonalDifferenceA, diagonalDifferenceA) + dot(diagonalDifferenceB, diagonalDifferenceB));
}
