// PaintedGroundCore.hlsl
// The painted-ground maths, written as plain functions with no dependency on material
// globals so it can be used two ways:
//   1. by PaintedGround.shader (hand-written URP shader), and
//   2. from Shader Graph: PaintedGroundNodes.hlsl wraps these as reflected nodes (Unity 6.5+,
//      they show up in the Create Node menu under Painted/). On older Shader Graph, use
//      Custom Function nodes (Type = File, this file, Name without the _float suffix).
//
// All positions are world-space XZ in metres.

#ifndef PAINTED_GROUND_CORE_INCLUDED
#define PAINTED_GROUND_CORE_INCLUDED

// Lighting.hlsl gives Light / InputData / GetMainLight / the light loop. PaintedGround.shader
// includes it already; Shader Graph (Unlit target) does not, so pull it in here, except in
// the graph's node previews where it is unavailable.
#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

// ---------------------------------------------------------------------------------------------
// 1. Layer sampling with anti-tiling.
//    The texture is projected top-down from world XZ, then sampled a second time rotated 37
//    degrees at another scale, and the two are swapped by patchMask (a large-scale noise cut
//    to 0/1 patches). Because the swap is by patches, contrast is preserved.
// ---------------------------------------------------------------------------------------------
void PaintedLayer_float(Texture2D tex, SamplerState ss, float2 wxz, float tileSize,
                        float secondScale, float patchMask, out float3 col)
{
    const float2x2 kRot = float2x2(0.7986, -0.6018, 0.6018, 0.7986);
    float2 uv = wxz / max(tileSize, 0.01);
    float3 a = tex.Sample(ss, uv).rgb;
    float2 uv2 = mul(kRot, uv) * secondScale + float2(0.37, 0.71);
    float3 b = tex.Sample(ss, uv2).rgb;
    col = lerp(a, b, patchMask);
}

// ---------------------------------------------------------------------------------------------
// 2. Splat weights + their world-space gradients from a world-space splat texture.
//    Central differences at +-step metres. The baked map is a signed distance field per
//    channel (0.5 = border, +-4 m = 1/0), so the gradient is continuous and (w-0.5)/|grad|
//    is true metres from the border. No per-triangle seams.
//    bounds = (minX, minZ, sizeX, sizeZ) of the texture's world rectangle.
// ---------------------------------------------------------------------------------------------
void SplatGradient_float(Texture2D splat, SamplerState ss, float2 wxz, float4 bounds, float step,
                         out float3 w, out float2 gradR, out float2 gradG, out float2 gradB)
{
    float2 inv = 1.0 / max(bounds.zw, 0.001);
    float2 uv  = (wxz - bounds.xy) * inv;
    float2 dx  = float2(step, 0) * inv;
    float2 dy  = float2(0, step) * inv;
    w = splat.Sample(ss, uv).rgb;
    float3 px = splat.Sample(ss, uv + dx).rgb, mx = splat.Sample(ss, uv - dx).rgb;
    float3 pz = splat.Sample(ss, uv + dy).rgb, mz = splat.Sample(ss, uv - dy).rgb;
    float k = 0.5 / max(step, 0.001);
    gradR = float2(px.r - mx.r, pz.r - mz.r) * k;
    gradG = float2(px.g - mx.g, pz.g - mz.g) * k;
    gradB = float2(px.b - mx.b, pz.b - mz.b) * k;
}

// Fallback for vertex-colour splats: gradient from screen derivatives (per triangle, seams).
void DerivativeGradient_float(float w, float2 wxz, out float2 grad)
{
    float2 dPx = ddx(wxz), dPy = ddy(wxz);
    float  dwx = ddx(w),   dwy = ddy(w);
    float  det = dPx.x * dPy.y - dPx.y * dPy.x;
    grad = abs(det) > 1e-9 ? float2(dwx * dPy.y - dwy * dPx.y, dwy * dPx.x - dwx * dPy.x) / det : float2(0, 0);
}

// ---------------------------------------------------------------------------------------------
// 3. Painted border ("fringe").
//    Paints layerCol over colIn where w > 0.5, but the border is drawn from a strip texture:
//    the strip is sampled ACROSS the edge (row top = inside, bottom = outside) and ALONG it,
//    so whatever silhouette is painted in the strip becomes the shape of the border.
//    Strip channels: R = coverage, G = highlight rim, B = ink line. `rows` strips stacked
//    vertically, `row` selects one (0 = top).
// ---------------------------------------------------------------------------------------------
void PaintedFringe_float(Texture2D fringe, SamplerState ss,
                         float w, float2 grad, float2 wxz,
                         float row, float rows, float widthM, float lengthM,
                         float wobble, float wobbleAmount,
                         float3 layerCol, float3 colIn, float inkIn, float rimIn,
                         out float3 colOut, out float inkOut, out float rimOut)
{
    float  gl   = length(grad);
    float  dist = (w - 0.5) / max(gl, 1e-4);                 // metres inside (+) / outside (-)
    float2 tang = gl > 1e-5 ? float2(-grad.y, grad.x) / gl : float2(1, 0);
    float  s = dot(wxz, tang) / max(lengthM, 0.01);          // along the border
    float  v = 0.5 - dist / max(widthM, 0.01);               // 0 inside .. 1 outside
    v += (wobble - 0.5) * wobbleAmount;
    v = saturate(v) * 0.96 + 0.02;                           // stay inside this row
    float2 uv = float2(s, 1.0 - (row + v) / rows);
    float3 f = fringe.Sample(ss, uv).rgb;

    float a = f.r;
    colOut = lerp(colIn, layerCol, a);
    rimOut = max(rimIn * (1.0 - a), f.g);
    inkOut = max(inkIn * (1.0 - a), f.b);
}

// ---------------------------------------------------------------------------------------------
// 3b. Tile transition (Don't Starve style), same in/out shape as PaintedFringe_float.
//     The border art is drawn in tile space so it never bends round a curve: each grid cell takes
//     the 16-case marching-squares pattern of its 4 corner weights and samples that pre-drawn cell.
//     cornerWeights = (top-left, top-right, bottom-right, bottom-left), top = +Z. Corners are shared
//     with the neighbouring cells, so neighbours always agree and no seam can open.
//     Atlas: 8 x 4 cells, case c variant v at column c % 8, row c / 8 + 2v. R coverage, G rim, B ink.
// ---------------------------------------------------------------------------------------------
void PaintedTransitionTile_float(Texture2D atlas, SamplerState ss,
                                 float4 cornerWeights, float2 cellCoordinate, float variantSeed,
                                 float3 layerCol, float3 colIn, float inkIn, float rimIn,
                                 out float3 colOut, out float inkOut, out float rimOut)
{
    float4 cornerInside = step(0.5, cornerWeights);
    float  caseIndex = dot(cornerInside, float4(1, 2, 4, 8));
    float2 cellIndex = floor(cellCoordinate);
    float2 cellLocal = frac(cellCoordinate);
    float  variant = step(0.5, frac(sin(dot(cellIndex + variantSeed, float2(12.9898, 78.233))) * 43758.5453));

    // Half a texel inset (128 px cells) so bilinear filtering never reads the neighbouring atlas cell.
    const float halfTexelInCell = 0.5 / 128.0;
    float2 insetLocal = clamp(cellLocal, halfTexelInCell, 1.0 - halfTexelInCell);
    float  atlasColumn = fmod(caseIndex, 8.0);
    float  atlasRow = floor(caseIndex / 8.0) + 2.0 * variant;
    float2 atlasUV = float2((atlasColumn + insetLocal.x) / 8.0, 1.0 - (atlasRow + 1.0 - insetLocal.y) / 4.0);

    // Gradients from the continuous coordinate: the frac() jump at cell borders would pick the smallest mip in a one-pixel line.
    float2 atlasDdx = ddx(cellCoordinate) / float2(8.0, 4.0);
    float2 atlasDdy = ddy(cellCoordinate) / float2(8.0, 4.0);
    float3 tile = atlas.SampleGrad(ss, atlasUV, atlasDdx, atlasDdy).rgb;

    float a = tile.r;
    colOut = lerp(colIn, layerCol, a);
    rimOut = max(rimIn * (1.0 - a), tile.g);
    inkOut = max(inkIn * (1.0 - a), tile.b);
}

// ---------------------------------------------------------------------------------------------
// 4. Macro variation, rim and ink, applied once at the end.
// ---------------------------------------------------------------------------------------------
void PaintedFinish_float(float3 col, float macroNoise, float3 macroDark, float3 macroLight,
                         float macroStrength, float rim, float4 rimColor, float ink, float4 inkColor,
                         out float3 outCol)
{
    float3 tint = lerp(macroDark, macroLight, macroNoise) * 2.0;
    col *= lerp(float3(1, 1, 1), tint, macroStrength);
    col  = lerp(col, rimColor.rgb * col * 1.6 + rimColor.rgb * 0.25, rim * rimColor.a);
    col  = lerp(col, col * inkColor.rgb, ink * inkColor.a);
    outCol = col;
}

// ---------------------------------------------------------------------------------------------
// 5. Flat "painted" lighting: ambient + shadowed main light (mostly no N.L) + soft point
//    light pools. For Shader Graph, use an Unlit graph, multiply your colour by this, and add
//    the Boolean keywords _MAIN_LIGHT_SHADOWS, _MAIN_LIGHT_SHADOWS_CASCADE,
//    _MAIN_LIGHT_SHADOWS_SCREEN, _ADDITIONAL_LIGHTS, _ADDITIONAL_LIGHT_SHADOWS,
//    _CLUSTER_LIGHT_LOOP, _SHADOWS_SOFT to the blackboard (Global, multi-compile) so URP
//    can drive them. screenUV = Screen Position node (Default).
// ---------------------------------------------------------------------------------------------
// Set by a C# bridge via Shader.SetGlobalVector/Float. Unset globals read (0,0,0,0): alpha 0 blends
// to no ambient change and flash 0 adds nothing, so the look is unchanged when the bridge is absent.
float4 _TerrainAmbientColor;   // rgb = ambient multiplier, a = blend amount (0 = off)
float _TerrainLightningFlash;  // added to the final lighting
float _TerrainGroundExposureBoost;  // lighting is multiplied by 1 + this (0 = off); set by WorldAtmosphereDriver

// Global look dials, pushed by TerrainLightingBridge. Both read 0 when unset, which changes nothing.
float _StitchShadowSoftening;     // 0 = each material's own shadow strength, 1 = no cast shadows anywhere
float _StitchShadowBandHardness;  // 0 = soft painted falloff, 1 = hard cel edge on shadows and form shading

// Coloured light through glass, written by TintedGlassShadowFeature. Params x = 0 when the feature is absent.
float4x4 _StitchTintedShadowWorldToLight;  // world position -> (u, v, depth01), depth01 = 0 nearest the sun
float4 _StitchTintedShadowParams;          // x = enabled, y = depth bias, z = strength

// Ink look, pushed by TerrainLightingBridge. Hatch strength and grain both 0 when unset = no change.
float4 _StitchHatchParams;  // x = hatch strength, y = metres per hatch tile, z = space mode (0 world, 1 camera plane, 2 camera plane + object anchor), w = paper grain
float4 _StitchInkColor;     // rgb = the one ink colour
float4 _StitchHatchBoil;    // x = boil frames per second (0 = off), y = boil offset in tiles
float  _StitchInkShadowLift; // 0 = shadows darken as before, 1 = shadows are drawn only by the hatch (the ink reference)

#ifndef SHADERGRAPH_PREVIEW
TEXTURE2D(_StitchTintedShadowTexture);     // rgb = colour the sunlight was filtered to, a = depth01 of the nearest glass
TEXTURE2D(_StitchHatchTexture);            // R = "/" hatch strokes, G = paper grain (0.5 neutral)

// The ground defines STITCH_HATCH_WORLD_ANCHORED: its 100 m tiles must stay world-fixed, and an object-anchored phase would
// jump at every tile seam whenever the camera is yawed off the world axes.
float2 StitchHatchUV(float3 positionWS, float3 normalWS)
{
    float spaceMode = _StitchHatchParams.z;
    float2 planeCoordinates;
    if (spaceMode < 0.5)
    {
        float3 absoluteNormal = abs(normalWS);
        if (absoluteNormal.y >= max(absoluteNormal.x, absoluteNormal.z))
            planeCoordinates = positionWS.xz;
        else if (absoluteNormal.x >= absoluteNormal.z)
            planeCoordinates = positionWS.zy;
        else
            planeCoordinates = positionWS.xy;
    }
    else
    {
        float3 anchoredPosition = positionWS;
    #ifndef STITCH_HATCH_WORLD_ANCHORED
        if (spaceMode > 1.5)
            anchoredPosition -= GetObjectToWorldMatrix()._m03_m13_m23;
    #endif
        planeCoordinates = float2(dot(anchoredPosition, UNITY_MATRIX_V[0].xyz), dot(anchoredPosition, UNITY_MATRIX_V[1].xyz));
    }

    float boilFrame = _StitchHatchBoil.x > 0.0 ? fmod(floor(_Time.y * _StitchHatchBoil.x), 3.0) : 0.0;
    float2 boilOffset = boilFrame * _StitchHatchBoil.y * float2(0.37, 0.61);
    return planeCoordinates / max(_StitchHatchParams.y, 0.01) + boilOffset;
}

float3 StitchTintedSunlight(float3 positionWS)
{
    if (_StitchTintedShadowParams.x < 0.5)
        return float3(1, 1, 1);

    float3 lightCoord = mul(_StitchTintedShadowWorldToLight, float4(positionWS, 1.0)).xyz;
    if (any(lightCoord.xy < 0.0) || any(lightCoord.xy > 1.0))
        return float3(1, 1, 1);

    float4 glass = SAMPLE_TEXTURE2D_LOD(_StitchTintedShadowTexture, sampler_LinearClamp, lightCoord.xy, 0);
    // Only surfaces further from the sun than the glass are tinted, so the glass itself and anything above it stay clean.
    float behindGlass = step(glass.a + _StitchTintedShadowParams.y, lightCoord.z);
    return lerp(float3(1, 1, 1), glass.rgb, behindGlass * _StitchTintedShadowParams.z);
}

float StitchBandedLightAmount(float lightAmount, float bandCentre, float bandHalfWidth)
{
    float banded = smoothstep(bandCentre - bandHalfWidth, bandCentre + bandHalfWidth, lightAmount);
    return lerp(lightAmount, banded, saturate(_StitchShadowBandHardness));
}
#endif

void PaintedLighting_float(float3 positionWS, float3 normalWS, float2 screenUV,
                           float3 ambientTint, float shadowStrength, float ndotlInfluence,
                           float pointBoost, out float3 lighting)
{
#ifdef SHADERGRAPH_PREVIEW
    lighting = float3(1, 1, 1);
#else
    InputData inputData = (InputData)0;                       // name required by LIGHT_LOOP_BEGIN
    inputData.positionWS = positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    inputData.normalizedScreenSpaceUV = screenUV;

    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(screenUV, 0.0, 1.0);
    #else
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif

    // The positionWS overload applies URP's soft fade near the shadow distance; the shadowCoord-only one cuts shadows off in a hard line.
    Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1, 1, 1, 1));
    float castShadow  = StitchBandedLightAmount(mainLight.shadowAttenuation, 0.5, 0.05);
    float formShading = StitchBandedLightAmount(saturate(dot(normalWS, mainLight.direction)), 0.025, 0.025);
    float shade = lerp(1.0, castShadow, shadowStrength * (1.0 - saturate(_StitchShadowSoftening)));
    float ndl   = lerp(1.0, formShading, ndotlInfluence);

    lighting = SampleSH(half3(0, 1, 0)) * ambientTint * lerp(float3(1, 1, 1), _TerrainAmbientColor.rgb, _TerrainAmbientColor.a);
    float sunAmount = shade * ndl;
    lighting += mainLight.color * StitchTintedSunlight(positionWS) * lerp(sunAmount, 1.0, saturate(_StitchInkShadowLift));

    // Hatching only adds strokes inside the one shadow tone, never a second darkness level. Point lights are
    // added after it, so a lamp lifts the surroundings of the strokes but not the strokes themselves.
    if (_StitchHatchParams.x > 0.0 || _StitchHatchParams.w > 0.0)
    {
        float  sunBlocked = 1.0 - sunAmount;
        float2 hatchTap = SAMPLE_TEXTURE2D(_StitchHatchTexture, sampler_LinearRepeat, StitchHatchUV(positionWS, normalWS)).rg;
        float  hatchInkAmount = hatchTap.r * smoothstep(0.35, 0.65, sunBlocked) * _StitchHatchParams.x;
        lighting *= lerp(float3(1, 1, 1), _StitchInkColor.rgb, hatchInkAmount);
        lighting *= lerp(1.0, hatchTap.g * 2.0, _StitchHatchParams.w);
    }

    #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
        #if USE_CLUSTER_LIGHT_LOOP
        UNITY_LOOP for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
        {
            Light dirLight = GetAdditionalLight(dirIndex, positionWS, half4(1, 1, 1, 1));
            lighting += dirLight.color * dirLight.shadowAttenuation;
        }
        #endif
        uint pixelLightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light l = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
            float atten = l.distanceAttenuation * l.shadowAttenuation * pointBoost;
            atten = atten / (1.0 + atten);                    // soft shoulder, no hotspot
            lighting += l.color * atten;
        LIGHT_LOOP_END
    #endif

    lighting *= 1.0 + _TerrainGroundExposureBoost;
    lighting += _TerrainLightningFlash.xxx;
#endif
}

#endif // PAINTED_GROUND_CORE_INCLUDED
