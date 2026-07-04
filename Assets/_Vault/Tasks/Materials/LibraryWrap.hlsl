// Extra — Wrapping an SRP Shader Library function (Unity 6.5+)
//
// One of the headline use cases for the reflection API: surfacing functions
// that already live in the SRP Shader Library without writing a Custom Function
// node by hand. Here we expose Unity's perceptual-quality desaturation helper
// (Luminance + a simple saturation control) as a one-click node.
//
// Because the file includes Color.hlsl, the wrapped function has access to
// everything in the URP/Core shader library.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

///<funchints>
///     <sg:ProviderKey>Malevolent.Saturation</sg:ProviderKey>
///     <sg:DisplayName>Saturation</sg:DisplayName>
///     <sg:SearchCategory>Malevolent/Color</sg:SearchCategory>
///</funchints>
///<paramhints name = "amount">
///     <sg:DisplayName>Amount</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float3 Saturation(float3 color, float amount) {
    // Luminance() comes from the SRP Core Color library include above.
    float luma = Luminance(color);
    return lerp(luma.xxx, color, amount);
}