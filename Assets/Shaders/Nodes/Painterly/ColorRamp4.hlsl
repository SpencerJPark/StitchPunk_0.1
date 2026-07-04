// ColorRamp4 — the Unity stand-in for Unreal's Color Curve: remaps a grayscale
// mask value through a color gradient with a VARIABLE number of stops (1–4).
// Every stop has its own position slider; Stop Count controls how many are
// active (1 = flat Color A). Values below the first stop hold its color,
// values above the last stop hold its color. Because the stops are plain node
// inputs, they can be wired to Color properties and tuned per-material.
//
// Smoothness 1 = soft painterly blend, 0 = hard toon bands (cel-friendly).

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ColorRamp4</sg:ProviderKey>
///     <sg:DisplayName>Color Ramp 4</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "value">
///     <sg:DisplayName>Value</sg:DisplayName>
///</paramhints>
///<paramhints name = "stopCount">
///     <sg:DisplayName>Stop Count</sg:DisplayName>
///     <sg:Range>1, 4</sg:Range>
///     <sg:Default>4</sg:Default>
///</paramhints>
///<paramhints name = "colorA">
///     <sg:DisplayName>Color A</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.10, 0.16, 0.08</sg:Default>
///</paramhints>
///<paramhints name = "positionA">
///     <sg:DisplayName>Position A</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "colorB">
///     <sg:DisplayName>Color B</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.22, 0.35, 0.12</sg:Default>
///</paramhints>
///<paramhints name = "positionB">
///     <sg:DisplayName>Position B</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.33</sg:Default>
///</paramhints>
///<paramhints name = "colorC">
///     <sg:DisplayName>Color C</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.45, 0.55, 0.18</sg:Default>
///</paramhints>
///<paramhints name = "positionC">
///     <sg:DisplayName>Position C</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.66</sg:Default>
///</paramhints>
///<paramhints name = "colorD">
///     <sg:DisplayName>Color D</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.75, 0.72, 0.35</sg:Default>
///</paramhints>
///<paramhints name = "positionD">
///     <sg:DisplayName>Position D</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "smoothness">
///     <sg:DisplayName>Smoothness</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 ColorRamp4(
    float value,
    float stopCount,
    float3 colorA,
    float positionA,
    float3 colorB,
    float positionB,
    float3 colorC,
    float positionC,
    float3 colorD,
    float positionD,
    float smoothness)
{
    return PainterlyRampVariable(value, colorA, positionA, colorB, positionB, colorC, positionC, colorD, positionD, stopCount, smoothness);
}
