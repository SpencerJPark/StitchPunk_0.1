// Example 2 — Customizing the node with XML hints (Unity 6.5+)
//
// The same three ingredients as Example 1, plus reflection hints that change
// how the node looks and behaves in Shader Graph:
//
//   funchints  -> customize the node as a whole (name, search category...)
//   paramhints -> customize an individual port (display name, color picker,
//                 slider range, default value...)
//
// Hint validation is strict and type-aware:
//   <sg:Color/>   only valid on float3/float4 (and half variants)
//   <sg:Range>    only valid on a float/half scalar  (synonym: Slider)
//   <sg:Default>  a comma separated list of floats
//
// This node desaturates the input color, then tints it - a quick duotone /
// sepia look driven entirely by the Tint color picker and Strength slider.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>Malevolent.Colorize</sg:ProviderKey>
///     <sg:DisplayName>Colorize</sg:DisplayName>
///     <sg:SearchCategory>Malevolent/Color</sg:SearchCategory>
///</funchints>
///<paramhints name = "tint">
///     <sg:DisplayName>Tint</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 0.4, 0.1</sg:Default>
///</paramhints>
///<paramhints name = "strength">
///     <sg:DisplayName>Strength</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.75</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 Colorize(float3 color, float3 tint, float strength) {
    float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
    return lerp(color, luma * tint, strength);
}