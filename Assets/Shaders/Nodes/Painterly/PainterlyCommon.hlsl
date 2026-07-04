// PainterlyCommon.hlsl — shared, NON-exported helpers for the Painterly node library.
//
// Every exported node file in this folder includes this file and wraps one of
// these helpers. Keeping the math here means the combined PainterlyColor node
// can reuse the exact same code paths as the small composable nodes.
//
// This file must NOT include ShaderApiReflectionSupport.hlsl and must NOT
// contain UNITY_EXPORT_REFLECTION — nothing here is a node.

#ifndef PAINTERLY_COMMON_INCLUDED
#define PAINTERLY_COMMON_INCLUDED

// --- Color space -------------------------------------------------------------

float3 PainterlyRgbToHsv(float3 rgbColor)
{
    float4 hueConstants = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 maxChannel = lerp(float4(rgbColor.bg, hueConstants.wz), float4(rgbColor.gb, hueConstants.xy), step(rgbColor.b, rgbColor.g));
    float4 sorted = lerp(float4(maxChannel.xyw, rgbColor.r), float4(rgbColor.r, maxChannel.yzx), step(maxChannel.x, rgbColor.r));
    float chroma = sorted.x - min(sorted.w, sorted.y);
    float epsilon = 1.0e-10;
    return float3(abs(sorted.z + (sorted.w - sorted.y) / (6.0 * chroma + epsilon)), chroma / (sorted.x + epsilon), sorted.x);
}

float3 PainterlyHsvToRgb(float3 hsvColor)
{
    float4 hueConstants = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 hueRamp = abs(frac(hsvColor.xxx + hueConstants.xyz) * 6.0 - hueConstants.www);
    return hsvColor.z * lerp(hueConstants.xxx, saturate(hueRamp - hueConstants.xxx), hsvColor.y);
}

// --- Hashing (per-object randomness) ------------------------------------------

float PainterlyHash13(float3 position)
{
    float3 scrambled = frac(position * 0.1031);
    scrambled += dot(scrambled, scrambled.zyx + 31.32);
    return frac((scrambled.x + scrambled.y) * scrambled.z);
}

float3 PainterlyHash33(float3 position)
{
    float3 scrambled = frac(position * float3(0.1031, 0.1030, 0.0973));
    scrambled += dot(scrambled, scrambled.yxz + 33.33);
    return frac((scrambled.xxy + scrambled.yxx) * scrambled.zyx);
}

// --- Mask channel selection ----------------------------------------------------

float PainterlySelectMaskChannel(float4 maskSample, float channel)
{
    int channelIndex = (int)round(channel);
    if (channelIndex <= 0)
    {
        return maskSample.r;
    }
    if (channelIndex == 1)
    {
        return maskSample.g;
    }
    if (channelIndex == 2)
    {
        return maskSample.b;
    }
    return maskSample.a;
}

// --- Value contrast --------------------------------------------------------------

float PainterlyApplyValueContrast(float value, float contrast, float pivot, float brightness)
{
    return saturate((value - pivot) * contrast + pivot + brightness);
}

// --- Variable-stop color ramp (1..4 stops) ------------------------------------
// Every stop has its own position; Stop Count decides how many are active
// (1 = flat Color A, 2 = A->B, 3 = A->B->C, 4 = all). Values below the first
// position hold the first color, values above the last hold the last color.
// Positions are expected ascending; each is clamped to stay above the previous
// so out-of-order sliders degrade gracefully instead of inverting.
// smoothness 0 -> hard toon bands at each segment midpoint, 1 -> smooth blend.

float PainterlyRampSegmentBlend(float value, float segmentStart, float segmentEnd, float smoothness)
{
    float linearBlend = saturate((value - segmentStart) / max(segmentEnd - segmentStart, 0.0001));
    float sharpenedBlend = saturate((linearBlend - 0.5) / max(smoothness, 0.0001) + 0.5);
    return smoothstep(0.0, 1.0, sharpenedBlend);
}

float3 PainterlyRampVariable(
    float value,
    float3 colorA, float positionA,
    float3 colorB, float positionB,
    float3 colorC, float positionC,
    float3 colorD, float positionD,
    float stopCount,
    float smoothness)
{
    int activeStops = (int)clamp(round(stopCount), 1.0, 4.0);
    if (activeStops == 1)
    {
        return colorA;
    }

    float clampedPositionA = saturate(positionA);
    float clampedPositionB = max(saturate(positionB), clampedPositionA + 0.001);
    float3 rampColor = lerp(colorA, colorB, PainterlyRampSegmentBlend(value, clampedPositionA, clampedPositionB, smoothness));

    if (activeStops >= 3)
    {
        float clampedPositionC = max(saturate(positionC), clampedPositionB + 0.001);
        rampColor = lerp(rampColor, colorC, PainterlyRampSegmentBlend(value, clampedPositionB, clampedPositionC, smoothness));

        if (activeStops >= 4)
        {
            float clampedPositionD = max(saturate(positionD), clampedPositionC + 0.001);
            rampColor = lerp(rampColor, colorD, PainterlyRampSegmentBlend(value, clampedPositionC, clampedPositionD, smoothness));
        }
    }

    return rampColor;
}

// --- Hue / saturation / value adjust ------------------------------------------------

float3 PainterlyApplyHueSatValue(float3 color, float hueShift, float saturation, float value)
{
    float3 hsvColor = PainterlyRgbToHsv(color);
    hsvColor.x = frac(hsvColor.x + hueShift + 1.0);
    hsvColor.y = saturate(hsvColor.y * saturation);
    hsvColor.z = hsvColor.z * value;
    return PainterlyHsvToRgb(hsvColor);
}

#endif // PAINTERLY_COMMON_INCLUDED
