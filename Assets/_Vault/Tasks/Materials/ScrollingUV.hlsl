// Example 1 — Basics of the Shader Function Reflection API (Unity 6.5+)
//
// Three ingredients are all you need to turn a plain HLSL function into a
// Shader Graph node:
//   1. #include "ShaderApiReflectionSupport.hlsl"   (gives us the macro)
//   2. UNITY_EXPORT_REFLECTION                       (marks the function for reflection)
//   3. an <sg:ProviderKey> hint                      (makes it show up in the Create Node menu)
//
// Input/output ports are reflected straight from the function signature, so
// the three parameters below become three input ports, and the float2 return
// value becomes the output port.

#include "ShaderApiReflectionSupport.hlsl"
///<funchints>
///</funchints>
///<funchints>
///     <sg:ProviderKey>Malevolent.ScrollingUV</sg:ProviderKey>
///     <sg:SearchCategory>Malevolent/UV</sg:SearchCategory>
///</funchints>
UNITY_EXPORT_REFLECTION
float2 ScrollingUV(float2 uv, float2 speed, float time) {
    return uv + frac(speed * time);
}