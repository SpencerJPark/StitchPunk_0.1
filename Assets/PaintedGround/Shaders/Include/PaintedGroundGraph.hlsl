// PaintedGroundGraph.hlsl
// Custom Function node entry points for PaintedGround.shadergraph. These take Shader Graph's
// UnityTexture2D (texture + its own sampler, so wrap modes come from the texture import
// settings) and delegate to PaintedGroundCore.hlsl, the same code the hand-written shader
// runs. Node = Custom Function, Type = File, this file, Name = the part before _float.

#ifndef PAINTED_GROUND_GRAPH_INCLUDED
#define PAINTED_GROUND_GRAPH_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Texture.hlsl"
#include "PaintedGroundCore.hlsl"

void SG_Noise_float(UnityTexture2D macro, UnityTexture2D brush, float3 posWS,
                    float macroSize, float patchSize, float brushSize, float secondBlend,
                    out float macroNoise, out float patchMask, out float3 brushNoise)
{
    float2 wxz = posWS.xz;
    macroNoise = macro.tex.Sample(macro.samplerstate, wxz / max(macroSize, 0.01)).r;
    float patch = macro.tex.Sample(macro.samplerstate, wxz / max(patchSize, 0.01) + 0.37).g;
    patchMask = smoothstep(0.4, 0.6, patch) * secondBlend;
    brushNoise = brush.tex.Sample(brush.samplerstate, wxz / max(brushSize, 0.01)).rgb;
}

void SG_Layer_float(UnityTexture2D tex, float3 posWS, float tileSize, float secondScale, float patchMask,
                    out float3 col)
{
    PaintedLayer_float(tex.tex, tex.samplerstate, posWS.xz, tileSize, secondScale, patchMask, col);
}

void SG_SplatGradient_float(UnityTexture2D splat, float3 posWS, float4 bounds, float step,
                            out float3 weights, out float2 gradR, out float2 gradG, out float2 gradB)
{
    SplatGradient_float(splat.tex, splat.samplerstate, posWS.xz, bounds, step, weights, gradR, gradG, gradB);
}

// sizeM = metres per strip repeat along the border; the strip keeps its painted proportions
// (rows are 4:1, so it reaches sizeM/4 across), stretch scales the across reach only.
void SG_Fringe_float(UnityTexture2D fringe, float3 posWS, float weight, float2 grad,
                     float row, float rows, float sizeM, float stretch, float wobble, float wobbleAmount,
                     float3 layerCol, float3 colIn, float inkIn, float rimIn,
                     out float3 colOut, out float inkOut, out float rimOut)
{
    float widthM = sizeM / 4.0 * stretch;
    PaintedFringe_float(fringe.tex, fringe.samplerstate, weight, grad, posWS.xz, row, rows, widthM, sizeM,
                        wobble, wobbleAmount, layerCol, colIn, inkIn, rimIn, colOut, inkOut, rimOut);
}

void SG_Cliff_float(UnityTexture2D cliff, float3 posWS, float3 normalWS, float tileSize,
                    float cliffStart, float cliffEnd,
                    out float3 cliffCol, out float steep, out float2 grad)
{
    float3 n = normalize(normalWS);
    float2 cw = pow(abs(n.xz), 4.0);
    cw /= max(cw.x + cw.y, 0.0001);
    float ts = max(tileSize, 0.01);
    cliffCol = cliff.tex.Sample(cliff.samplerstate, posWS.zy / ts).rgb * cw.x +
               cliff.tex.Sample(cliff.samplerstate, posWS.xy / ts).rgb * cw.y;
    steep = 1.0 - smoothstep(cliffStart, cliffEnd, n.y);
    DerivativeGradient_float(steep, posWS.xz, grad);
}

void SG_Finish_float(float3 col, float macroNoise, float3 macroDark, float3 macroLight, float macroStrength,
                     float rim, float4 rimColor, float ink, float4 inkColor, out float3 outCol)
{
    PaintedFinish_float(col, macroNoise, macroDark, macroLight, macroStrength, rim, rimColor, ink, inkColor, outCol);
}

void SG_Lighting_float(float3 posWS, float3 normalWS, float3 ambientTint, float shadowStrength,
                       float ndotlInfluence, float pointBoost, out float3 lighting)
{
#ifdef SHADERGRAPH_PREVIEW
    lighting = float3(1, 1, 1);
#else
    // normalised screen UV from the world position (no Screen Position node needed)
    float2 screenUV = ComputeNormalizedDeviceCoordinates(posWS, UNITY_MATRIX_VP);
    PaintedLighting_float(posWS, normalize(normalWS), screenUV, ambientTint, shadowStrength, ndotlInfluence, pointBoost, lighting);
#endif
}

#endif
