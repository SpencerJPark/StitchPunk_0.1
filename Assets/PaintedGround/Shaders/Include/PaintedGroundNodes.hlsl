// PaintedGroundNodes.hlsl
// Shader Graph nodes (Unity 6.5+ Shader Function Reflection API) for the painted ground.
// Drop this file in the project and the nodes appear under Painted/... in the Create Node
// menu. Each node is a thin wrapper around PaintedGroundCore.hlsl, which is the same code
// PaintedGround.shader runs, so a graph built from these renders identically.
//
// Suggested graph (Unlit URP target):
//   Position(Absolute World).xz ─┐
//   Splat Gradient ──────────────┼─> Painted Fringe (R) ─> Painted Fringe (G) ─> Painted Fringe (B)
//   Painted Layer x4 ────────────┘        ↓ colour / ink / rim chained through
//   Painted Finish ─> multiply by Painted Lighting ─> Base Color
// For Painted Lighting, add Boolean keywords (Global, Multi-compile) to the blackboard:
//   _MAIN_LIGHT_SHADOWS  _MAIN_LIGHT_SHADOWS_CASCADE  _MAIN_LIGHT_SHADOWS_SCREEN
//   _ADDITIONAL_LIGHTS  _ADDITIONAL_LIGHT_SHADOWS  _CLUSTER_LIGHT_LOOP  _SHADOWS_SOFT
//
// Visual Studio may underline the first include as "cannot open source file"; Unity resolves
// it fine (it ships with Shader Graph 17.5+).

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "PaintedGroundCore.hlsl"

/// <summary>Projects a tiling texture top-down from world XZ, with a second rotated sample
/// swapped in by patches to break repetition.</summary>
/// <funchints>
///     <sg:ProviderKey>Painted.Layer</sg:ProviderKey>
///     <sg:DisplayName>Painted Layer</sg:DisplayName>
///     <sg:SearchCategory>Painted/Ground</sg:SearchCategory>
///     <sg:SearchTerms>terrain, tiling, world projection, anti tiling</sg:SearchTerms>
///     <sg:ReturnDisplayName>Colour</sg:ReturnDisplayName>
/// </funchints>
/// <paramhints name="tex"><sg:DisplayName>Texture</sg:DisplayName></paramhints>
/// <paramhints name="ss"><sg:DisplayName>Sampler</sg:DisplayName></paramhints>
/// <paramhints name="wxz"><sg:DisplayName>World XZ</sg:DisplayName></paramhints>
/// <paramhints name="tileSize"><sg:DisplayName>Tile Size (m)</sg:DisplayName><sg:Default>12</sg:Default></paramhints>
/// <paramhints name="secondScale"><sg:DisplayName>Second Sample Scale</sg:DisplayName><sg:Range>0.2, 1.5</sg:Range><sg:Default>0.61</sg:Default></paramhints>
/// <paramhints name="patchMask"><sg:DisplayName>Patch Mask</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0</sg:Default></paramhints>
UNITY_EXPORT_REFLECTION float3 PaintedLayerNode(UnityTexture2D tex, UnitySamplerState ss, float2 wxz,
                                                float tileSize, float secondScale, float patchMask)
{
    float3 c;
    PaintedLayer_float(tex.tex, ss.samplerstate, wxz, tileSize, secondScale, patchMask, c);
    return c;
}

/// <summary>Reads a world-space splat map and returns the three weights plus their world
/// gradients, which the fringe nodes use to orient the painted borders.</summary>
/// <funchints>
///     <sg:ProviderKey>Painted.SplatGradient</sg:ProviderKey>
///     <sg:DisplayName>Splat Gradient</sg:DisplayName>
///     <sg:SearchCategory>Painted/Ground</sg:SearchCategory>
///     <sg:SearchTerms>splat, weights, gradient, border direction</sg:SearchTerms>
/// </funchints>
/// <paramhints name="splat"><sg:DisplayName>Splat Map</sg:DisplayName></paramhints>
/// <paramhints name="ss"><sg:DisplayName>Sampler (clamp)</sg:DisplayName></paramhints>
/// <paramhints name="wxz"><sg:DisplayName>World XZ</sg:DisplayName></paramhints>
/// <paramhints name="bounds"><sg:DisplayName>World Rect (minX, minZ, sizeX, sizeZ)</sg:DisplayName><sg:Default>-128,-128,256,256</sg:Default></paramhints>
/// <paramhints name="step"><sg:DisplayName>Sample Step (m)</sg:DisplayName><sg:Range>0.05, 2</sg:Range><sg:Default>0.35</sg:Default></paramhints>
/// <paramhints name="weights"><sg:DisplayName>Weights (RGB)</sg:DisplayName></paramhints>
/// <paramhints name="gradR"><sg:DisplayName>Gradient R</sg:DisplayName></paramhints>
/// <paramhints name="gradG"><sg:DisplayName>Gradient G</sg:DisplayName></paramhints>
/// <paramhints name="gradB"><sg:DisplayName>Gradient B</sg:DisplayName></paramhints>
UNITY_EXPORT_REFLECTION void SplatGradientNode(UnityTexture2D splat, UnitySamplerState ss, float2 wxz,
                                               float4 bounds, float step,
                                               out float3 weights, out float2 gradR, out float2 gradG, out float2 gradB)
{
    SplatGradient_float(splat.tex, ss.samplerstate, wxz, bounds, step, weights, gradR, gradG, gradB);
}

/// <summary>Paints one turf over the running colour with a hand-drawn border taken from a
/// fringe strip (R silhouette, G highlight rim, B ink). Chain one per layer.</summary>
/// <funchints>
///     <sg:ProviderKey>Painted.Fringe</sg:ProviderKey>
///     <sg:DisplayName>Painted Fringe</sg:DisplayName>
///     <sg:SearchCategory>Painted/Ground</sg:SearchCategory>
///     <sg:SearchTerms>border, edge, grass, blades, turf, fringe</sg:SearchTerms>
/// </funchints>
/// <paramhints name="fringe"><sg:DisplayName>Fringe Strip</sg:DisplayName></paramhints>
/// <paramhints name="row"><sg:DisplayName>Strip Row</sg:DisplayName><sg:Default>0</sg:Default></paramhints>
/// <paramhints name="rows"><sg:DisplayName>Strip Row Count</sg:DisplayName><sg:Default>4</sg:Default></paramhints>
/// <paramhints name="ss"><sg:DisplayName>Sampler (repeat)</sg:DisplayName></paramhints>
/// <paramhints name="weight"><sg:DisplayName>Layer Weight</sg:DisplayName><sg:Range>0, 1</sg:Range></paramhints>
/// <paramhints name="grad"><sg:DisplayName>Weight Gradient</sg:DisplayName></paramhints>
/// <paramhints name="wxz"><sg:DisplayName>World XZ</sg:DisplayName></paramhints>
/// <paramhints name="sizeM"><sg:DisplayName>Fringe Size (m)</sg:DisplayName><sg:Range>0.5, 14</sg:Range><sg:Default>3</sg:Default></paramhints>
/// <paramhints name="stretch"><sg:DisplayName>Fringe Stretch</sg:DisplayName><sg:Range>0.5, 2</sg:Range><sg:Default>1</sg:Default></paramhints>
/// <paramhints name="wobble"><sg:DisplayName>Wobble Noise</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.5</sg:Default></paramhints>
/// <paramhints name="wobbleAmount"><sg:DisplayName>Wobble Amount</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.25</sg:Default></paramhints>
/// <paramhints name="layerCol"><sg:DisplayName>Layer Colour</sg:DisplayName><sg:Color /></paramhints>
/// <paramhints name="colIn"><sg:DisplayName>Colour In</sg:DisplayName><sg:Color /></paramhints>
/// <paramhints name="inkIn"><sg:DisplayName>Ink In</sg:DisplayName></paramhints>
/// <paramhints name="rimIn"><sg:DisplayName>Rim In</sg:DisplayName></paramhints>
/// <paramhints name="colOut"><sg:DisplayName>Colour</sg:DisplayName></paramhints>
/// <paramhints name="inkOut"><sg:DisplayName>Ink</sg:DisplayName></paramhints>
/// <paramhints name="rimOut"><sg:DisplayName>Rim</sg:DisplayName></paramhints>
UNITY_EXPORT_REFLECTION void PaintedFringeNode(UnityTexture2D fringe, UnitySamplerState ss,
                                               float weight, float2 grad, float2 wxz,
                                               float row, float rows, float sizeM, float stretch,
                                               float wobble, float wobbleAmount,
                                               float3 layerCol, float3 colIn, float inkIn, float rimIn,
                                               out float3 colOut, out float inkOut, out float rimOut)
{
    float widthM = sizeM / 4.0 * stretch;     // strip rows are 4:1, keep painted proportions
    PaintedFringe_float(fringe.tex, ss.samplerstate, weight, grad, wxz, row, rows, widthM, sizeM,
                        wobble, wobbleAmount, layerCol, colIn, inkIn, rimIn, colOut, inkOut, rimOut);
}

/// <summary>Macro colour drift, highlight rim and ink line, applied once after all layers.</summary>
/// <funchints>
///     <sg:ProviderKey>Painted.Finish</sg:ProviderKey>
///     <sg:DisplayName>Painted Finish</sg:DisplayName>
///     <sg:SearchCategory>Painted/Ground</sg:SearchCategory>
///     <sg:SearchTerms>macro, tint, ink, rim, outline</sg:SearchTerms>
///     <sg:ReturnDisplayName>Colour</sg:ReturnDisplayName>
/// </funchints>
/// <paramhints name="col"><sg:DisplayName>Colour</sg:DisplayName><sg:Color /></paramhints>
/// <paramhints name="macroNoise"><sg:DisplayName>Macro Noise</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.5</sg:Default></paramhints>
/// <paramhints name="macroDark"><sg:DisplayName>Macro Dark Tint</sg:DisplayName><sg:Color /><sg:Default>0.36,0.39,0.45</sg:Default></paramhints>
/// <paramhints name="macroLight"><sg:DisplayName>Macro Light Tint</sg:DisplayName><sg:Color /><sg:Default>0.58,0.55,0.48</sg:Default></paramhints>
/// <paramhints name="macroStrength"><sg:DisplayName>Macro Strength</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.6</sg:Default></paramhints>
/// <paramhints name="rim"><sg:DisplayName>Rim Mask</sg:DisplayName></paramhints>
/// <paramhints name="rimColor"><sg:DisplayName>Rim Colour (A = strength)</sg:DisplayName><sg:Color /><sg:Default>0.95,0.92,0.80,0.55</sg:Default></paramhints>
/// <paramhints name="ink"><sg:DisplayName>Ink Mask</sg:DisplayName></paramhints>
/// <paramhints name="inkColor"><sg:DisplayName>Ink Colour (A = strength)</sg:DisplayName><sg:Color /><sg:Default>0.12,0.10,0.12,0.75</sg:Default></paramhints>
UNITY_EXPORT_REFLECTION float3 PaintedFinishNode(float3 col, float macroNoise, float3 macroDark, float3 macroLight,
                                                 float macroStrength, float rim, float4 rimColor, float ink, float4 inkColor)
{
    float3 o;
    PaintedFinish_float(col, macroNoise, macroDark, macroLight, macroStrength, rim, rimColor, ink, inkColor, o);
    return o;
}

/// <summary>Flat painted lighting: ambient + shadowed main light with little N.L + soft point
/// light pools. Multiply your colour by this in an Unlit graph.</summary>
/// <funchints>
///     <sg:ProviderKey>Painted.Lighting</sg:ProviderKey>
///     <sg:DisplayName>Painted Lighting</sg:DisplayName>
///     <sg:SearchCategory>Painted/Lighting</sg:SearchCategory>
///     <sg:SearchTerms>flat, unlit, painted, campfire, point light</sg:SearchTerms>
///     <sg:ReturnDisplayName>Lighting</sg:ReturnDisplayName>
/// </funchints>
/// <paramhints name="positionWS"><sg:DisplayName>Position (World)</sg:DisplayName></paramhints>
/// <paramhints name="normalWS"><sg:DisplayName>Normal (World)</sg:DisplayName><sg:Default>0,1,0</sg:Default></paramhints>
/// <paramhints name="screenUV"><sg:DisplayName>Screen Position</sg:DisplayName></paramhints>
/// <paramhints name="ambientTint"><sg:DisplayName>Ambient Tint</sg:DisplayName><sg:Color /><sg:Default>1,1,1</sg:Default></paramhints>
/// <paramhints name="shadowStrength"><sg:DisplayName>Shadow Strength</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.6</sg:Default></paramhints>
/// <paramhints name="ndotlInfluence"><sg:DisplayName>N.L Influence</sg:DisplayName><sg:Range>0, 1</sg:Range><sg:Default>0.15</sg:Default></paramhints>
/// <paramhints name="pointBoost"><sg:DisplayName>Point Light Boost</sg:DisplayName><sg:Range>0, 4</sg:Range><sg:Default>1</sg:Default></paramhints>
UNITY_EXPORT_REFLECTION float3 PaintedLightingNode(float3 positionWS, float3 normalWS, float2 screenUV,
                                                   float3 ambientTint, float shadowStrength, float ndotlInfluence, float pointBoost)
{
    float3 l;
    PaintedLighting_float(positionWS, normalWS, screenUV, ambientTint, shadowStrength, ndotlInfluence, pointBoost, l);
    return l;
}
