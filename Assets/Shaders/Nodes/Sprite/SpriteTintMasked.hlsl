// SpriteTintMasked — up to three independent colour zones in one sprite
// (e.g. shirt + buttons + collar) plus automatic outline pass-through.
//
// Needs a companion MASK texture baked on the SAME UVs as the part:
//     mask.r = zone A pixels   (white where zone A is, else black)
//     mask.g = zone B pixels
//     mask.b = zone C pixels
// Pixels claimed by no zone (mask ~0) pass through multiplied by white, so a
// baked BLACK outline stays black and baked light-gray detail stays light.
//
// The shading/detail is read from the sprite luminance. If the part was baked
// neutral gray, baseColor.r == .g == .b, so .r is used (cheap). As with
// SpriteTint: bake WHITE fill + BLACK outline; colour comes from the tints.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.SpriteTintMasked</sg:ProviderKey>
///     <sg:DisplayName>Sprite Tint Masked</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Sprite</sg:SearchCategory>
///</funchints>
///<paramhints name = "baseColor">
///     <sg:DisplayName>Base Color</sg:DisplayName>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "maskSample">
///     <sg:DisplayName>Mask</sg:DisplayName>
///     <sg:Default>0, 0, 0, 0</sg:Default>
///</paramhints>
///<paramhints name = "tintZoneA">
///     <sg:DisplayName>Tint A (Mask R)</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "tintZoneB">
///     <sg:DisplayName>Tint B (Mask G)</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "tintZoneC">
///     <sg:DisplayName>Tint C (Mask B)</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 1, 1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void SpriteTintMasked(
    float4 baseColor,
    float4 maskSample,
    float3 tintZoneA,
    float3 tintZoneB,
    float3 tintZoneC,
    out float4 tinted)
{
    float grey = baseColor.r;

    // Pixels not claimed by any zone (outline / neutral detail) pass through by
    // multiplying against white, so grey carries them unchanged.
    float  free      = saturate(1.0 - maskSample.r - maskSample.g - maskSample.b);
    float3 zoneColor = maskSample.r * tintZoneA
                     + maskSample.g * tintZoneB
                     + maskSample.b * tintZoneC
                     + free         * float3(1.0, 1.0, 1.0);

    tinted.rgb = grey * zoneColor;   // outline (grey 0) -> 0 -> stays black
    tinted.a   = baseColor.a;
}
