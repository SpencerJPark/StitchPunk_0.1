// NdotVTransform — steep-angle compensation factor for the depth outline.
// Replacement for SubGraphs/NdotVTransform.shadersubgraph. Exact traced math:
//
//   smoothstep(SteepAngleThreshold, 2.0, NdotV) * SteepAngleMultiplier + 1.0
//
// Note the smoothstep upper edge really is 2.0 in the subgraph (not the
// usual 1.0) — preserved here as the default of an exposed input so the
// converted result is bit-identical, but tweakable.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.NdotVTransform</sg:ProviderKey>
///     <sg:DisplayName>NdotV Transform</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
///<paramhints name = "nDotV">
///     <sg:DisplayName>NdotV</sg:DisplayName>
///</paramhints>
///<paramhints name = "steepAngleThreshold">
///     <sg:DisplayName>Steep Angle Threshold</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "steepAngleMultiplier">
///     <sg:DisplayName>Steep Angle Multiplier</sg:DisplayName>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "smoothstepUpperEdge">
///     <sg:DisplayName>Smoothstep Upper Edge</sg:DisplayName>
///     <sg:Default>2</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float NdotVTransform(
    float nDotV,
    float steepAngleThreshold,
    float steepAngleMultiplier,
    float smoothstepUpperEdge)
{
    return smoothstep(steepAngleThreshold, smoothstepUpperEdge, nDotV) * steepAngleMultiplier + 1.0;
}
