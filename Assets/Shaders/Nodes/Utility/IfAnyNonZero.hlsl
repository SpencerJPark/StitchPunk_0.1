// IfAnyNonZero — 1 if any of the four inputs is greater than zero, else 0.
// Replacement for SubGraphs/IfAnyNonZeroReturnOneElseZero.shadersubgraph
// (four Greater-than-0 comparisons + branches + max chain, collapsed to one
// step()). Used for alpha-presence checks in the outline pipeline.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.IfAnyNonZero</sg:ProviderKey>
///     <sg:DisplayName>If Any Non Zero</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Utility</sg:SearchCategory>
///</funchints>
///<paramhints name = "valueA">
///     <sg:DisplayName>A</sg:DisplayName>
///</paramhints>
///<paramhints name = "valueB">
///     <sg:DisplayName>B</sg:DisplayName>
///</paramhints>
///<paramhints name = "valueC">
///     <sg:DisplayName>C</sg:DisplayName>
///</paramhints>
///<paramhints name = "valueD">
///     <sg:DisplayName>D</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
float IfAnyNonZero(float valueA, float valueB, float valueC, float valueD)
{
    float largestValue = max(max(valueA, valueB), max(valueC, valueD));
    return step(0.0000001, largestValue);
}
