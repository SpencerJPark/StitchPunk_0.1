// PainterlyColor — the whole painterly single-texture chain in one node:
//
//   mask sample -> select channel -> contrast -> variable-stop color ramp
//               -> hue/sat/value adjust (+ per-object hue & value jitter)
//
// The ramp has 1–4 stops (Stop Count), each with its own position slider —
// values below the first stop hold its color, above the last hold its color.
// Wire the mask texture's Sample Texture 2D into Mask Sample and (optionally)
// the ObjectRandom node's Random3 into Object Random for per-instance
// variation. The Mask Value output is the post-contrast grayscale — feed it
// into the Height To Normal node for the bump.
//
// For unusual chains (extra ramps, different jitter wiring, the future
// cel-shading port) use the small nodes in this folder instead — this node is
// just those helpers composed in the standard order.

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.PainterlyColor</sg:ProviderKey>
///     <sg:DisplayName>Painterly Color</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "maskSample">
///     <sg:DisplayName>Mask Sample</sg:DisplayName>
///</paramhints>
///<paramhints name = "channel">
///     <sg:DisplayName>Channel (0=R 1=G 2=B 3=A)</sg:DisplayName>
///     <sg:Range>0, 3</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "contrast">
///     <sg:DisplayName>Contrast</sg:DisplayName>
///     <sg:Range>0, 4</sg:Range>
///     <sg:Default>1</sg:Default>
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
///     <sg:DisplayName>Ramp Smoothness</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "hueShift">
///     <sg:DisplayName>Hue Shift</sg:DisplayName>
///     <sg:Range>-0.5, 0.5</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "saturation">
///     <sg:DisplayName>Saturation</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "value">
///     <sg:DisplayName>Value</sg:DisplayName>
///     <sg:Range>0, 2</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
///<paramhints name = "objectRandom">
///     <sg:DisplayName>Object Random</sg:DisplayName>
///     <sg:Default>0.5, 0.5, 0.5</sg:Default>
///</paramhints>
///<paramhints name = "hueJitter">
///     <sg:DisplayName>Hue Jitter</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "valueJitter">
///     <sg:DisplayName>Value Jitter</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void PainterlyColor(
    float4 maskSample,
    float channel,
    float contrast,
    float stopCount,
    float3 colorA,
    float positionA,
    float3 colorB,
    float positionB,
    float3 colorC,
    float positionC,
    float3 colorD,
    float positionD,
    float smoothness,
    float hueShift,
    float saturation,
    float value,
    float3 objectRandom,
    float hueJitter,
    float valueJitter,
    out float3 color,
    out float maskValue)
{
    maskValue = PainterlySelectMaskChannel(maskSample, channel);
    maskValue = PainterlyApplyValueContrast(maskValue, contrast, 0.5, 0.0);

    float3 rampColor = PainterlyRampVariable(maskValue, colorA, positionA, colorB, positionB, colorC, positionC, colorD, positionD, stopCount, smoothness);

    // objectRandom defaults to 0.5 so unwired jitter is a no-op.
    float jitteredHueShift = hueShift + (objectRandom.x - 0.5) * hueJitter;
    float jitteredValue = value * (1.0 + (objectRandom.y - 0.5) * valueJitter);

    color = PainterlyApplyHueSatValue(rampColor, jitteredHueShift, saturation, jitteredValue);
}
