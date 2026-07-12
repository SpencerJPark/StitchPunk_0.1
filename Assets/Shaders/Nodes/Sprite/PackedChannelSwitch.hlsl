// PackedChannelSwitch — a two-variant packed sprite: ONE texture slice carries two shape/alpha
// pairs and a switch picks which pair renders. Packing convention:
//   normal variant : R = shape mask (multiplied by the colour), G = output alpha
//   alt variant    : B = shape mask (multiplied by the colour), A = output alpha
// Built for hair-under-hats: the alt pair is the same hair reshaped to hug the head, so putting a
// hat on swaps to a silhouette that doesn't poke through the brim — same slice, same rolled colour.
// Wire Use Alt Shape to a per-instance (Hybrid Per Instance) float property so equipment code can
// flip it per character; values between 0 and 1 cross-fade the two variants. Pixels claimed by no
// shape channel multiply to black, so baked black outlines survive the recolor. Pure math, no
// lighting includes — safe in preview.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.PackedChannelSwitch</sg:ProviderKey>
///     <sg:DisplayName>Packed Channel Switch</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Sprite</sg:SearchCategory>
///</funchints>
///<paramhints name = "packedSample">
///     <sg:DisplayName>Packed Sample</sg:DisplayName>
///     <sg:Default>0, 0, 0, 0</sg:Default>
///</paramhints>
///<paramhints name = "shapeColor">
///     <sg:DisplayName>Shape Color</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "useAltShape">
///     <sg:DisplayName>Use Alt Shape (B/A)</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void PackedChannelSwitch(
    float4 packedSample,
    float4 shapeColor,
    float useAltShape,
    out float3 recoloredColor,
    out float recoloredAlpha)
{
    float altBlend = saturate(useAltShape);

    float shapeMask  = lerp(packedSample.r, packedSample.b, altBlend);
    float shapeAlpha = lerp(packedSample.g, packedSample.a, altBlend);

    recoloredColor = shapeColor.rgb * shapeMask;
    recoloredAlpha = shapeAlpha;
}
