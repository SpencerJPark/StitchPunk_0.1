// RampUVFromValue — turns an already-extracted grayscale value into a lookup
// coordinate for a 1D colour ramp (a gradient map / recolour).
//
// The mapping is deliberately INVERTED: value 1.0 (light) lands at U = 0.0 (the START
// of the ramp) and value 0.0 (dark) lands at U = 1.0 (the END). So author ramps
// left-to-right as highlight -> shadow.
//
// V is fixed at 0.5 because ramps baked by the Color Ramp tool
// (Stitch Punk > Bake All Color Ramps -> Assets/Textures/ColorRamps/) are a single
// gradient repeated down every row — one texture per ramp, so there is no row to pick.
//
// Takes a single grayscale FLOAT on purpose. Do not feed this (or any ramp lookup) a
// luminance of the raw _MainTex sample: _MainTex is the packed stroke mask, whose
// R/G/B/A are four INDEPENDENT noise layers, not the channels of one colour. Averaging
// them blends all four layers together and makes the Channel slider affect only the
// bump path while colour silently ignores it. Feed it Select Channel -> Value Contrast
// so one grayscale source drives both colour and bump.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.RampUVFromValue</sg:ProviderKey>
///     <sg:DisplayName>Ramp UV From Value</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "value">
///     <sg:DisplayName>Value</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
void RampUVFromValue(
    float value,
    out float2 rampUV)
{
    // Light -> ramp start, dark -> ramp end.
    rampUV = float2(1.0 - saturate(value), 0.5);
}
