// RobertsCrossDepth — depth-based Roberts-cross edge value. Replacement for
// SubGraphs/RobertsCrossDepth.shadersubgraph: samples Linear01 scene depth at
// the four diagonal UVs (from Cross Sample Screen UVs), takes the two diagonal
// differences, and returns sqrt(a^2 + b^2) * multiplier — multiplier applied
// AFTER the square root, exactly as the subgraph wired it.

#include "ShaderApiReflectionSupport.hlsl"
#include "ScreenSpaceCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.RobertsCrossDepth</sg:ProviderKey>
///     <sg:DisplayName>Roberts Cross Depth</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Screen</sg:SearchCategory>
///</funchints>
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
///<paramhints name = "robertsCrossMultiplier">
///     <sg:DisplayName>Roberts Cross Multiplier</sg:DisplayName>
///     <sg:Default>100</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float RobertsCrossDepth(
    float2 uvTopRight,
    float2 uvBottomLeft,
    float2 uvTopLeft,
    float2 uvBottomRight,
    float robertsCrossMultiplier)
{
#if defined(SHADERGRAPH_PREVIEW)
    return 0.0;
#else
    float depthTopRight = Linear01Depth(SampleSceneDepth(uvTopRight), _ZBufferParams);
    float depthBottomLeft = Linear01Depth(SampleSceneDepth(uvBottomLeft), _ZBufferParams);
    float depthTopLeft = Linear01Depth(SampleSceneDepth(uvTopLeft), _ZBufferParams);
    float depthBottomRight = Linear01Depth(SampleSceneDepth(uvBottomRight), _ZBufferParams);

    float diagonalDifferenceA = depthTopRight - depthBottomLeft;
    float diagonalDifferenceB = depthTopLeft - depthBottomRight;

    float edge = sqrt(diagonalDifferenceA * diagonalDifferenceA + diagonalDifferenceB * diagonalDifferenceB);
    return edge * robertsCrossMultiplier;
#endif
}
