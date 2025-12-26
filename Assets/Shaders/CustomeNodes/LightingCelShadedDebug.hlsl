#ifndef LIGHTING_CEL_SHADED_INCLUDED
#define LIGHTING_CEL_SHADED_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct EdgeConstants
{
    float edgeDiffuse;
    float edgeSpecular;
    float edgeSpecularOffset;
    float edgeDistanceAttenuation;
    float edgeShadowAttenuation;
    float edgeRim;
    float edgeRimOffset;
};

struct SurfaceVariables
{
    float3 normal;
    float3 view;
    float smoothness;
    float shininess;
    float rimThreshold;
    EdgeConstants ec;
};

float3 CalculateCelShading(Light l, SurfaceVariables s)
{
    float shadowAttenuationSmoothStep = smoothstep(0.0f, s.ec.edgeShadowAttenuation, l.shadowAttenuation);
    float distanceAttenuationSmoothStep = smoothstep(0.0f, s.ec.edgeDistanceAttenuation, l.distanceAttenuation);
    float attenuation = shadowAttenuationSmoothStep * distanceAttenuationSmoothStep;

    float diffuse = saturate(dot(s.normal, l.direction));
    diffuse *= attenuation;
    diffuse = smoothstep(0.0f, s.ec.edgeDiffuse, diffuse);

    float3 h = SafeNormalize(l.direction + s.view);

    float specular = saturate(dot(s.normal, h));
    specular = pow(specular, s.shininess);
    specular *= diffuse * s.smoothness;
    specular = s.smoothness * smoothstep(
        (1 - s.smoothness) * s.ec.edgeSpecular + s.ec.edgeSpecularOffset,
        s.ec.edgeSpecular + s.ec.edgeSpecularOffset,
        specular);

    float rim = 1 - dot(s.view, s.normal);
    rim *= pow(diffuse, s.rimThreshold);
    rim = s.smoothness * smoothstep(
        s.ec.edgeRim - 0.5 * s.ec.edgeRimOffset,
        s.ec.edgeRim + 0.5 * s.ec.edgeRimOffset,
        rim);

    return l.color * (diffuse + max(specular, rim));
}
#endif

void LightingCelShaded_float(
    float Smoothness,
    float RimThreshold,
    float3 Position,
    float3 Normal,
    float3 View,
    float EdgeDiffuse,
    float EdgeSpecular,
    float EdgeSpecularOffset,
    float EdgeDistanceAttenuation,
    float EdgeShadowAttenuation,
    float EdgeRim,
    float EdgeRimOffset,
    out float3 Color)
{
#if defined(SHADERGRAPH_PREVIEW)
    Color = float3(0.5, 0.5, 0.5);
#else
    SurfaceVariables s;
    s.normal = normalize(Normal);
    s.view = SafeNormalize(View);
    s.smoothness = Smoothness;
    s.shininess = exp2(10 * Smoothness + 1);
    s.rimThreshold = RimThreshold;
    s.ec.edgeDiffuse = EdgeDiffuse;
    s.ec.edgeSpecular = EdgeSpecular;
    s.ec.edgeSpecularOffset = EdgeSpecularOffset;
    s.ec.edgeDistanceAttenuation = EdgeDistanceAttenuation;
    s.ec.edgeShadowAttenuation = EdgeShadowAttenuation;
    s.ec.edgeRim = EdgeRim;
    s.ec.edgeRimOffset = EdgeRimOffset;

#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    #if SHADOWS_SCREEN
        float4 clipPos = TransformWorldToHClip(Position);
        float4 shadowCoord = ComputeScreenPos(clipPos);
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(Position);
    #endif
#else
    float4 shadowCoord = float4(0, 0, 0, 0);
#endif

    Light light = GetMainLight(shadowCoord);
    Color = CalculateCelShading(light, s);

    // DEBUG: Show light count as color
    // 0 lights = black, 1 = red, 2 = green, 3 = blue, 4+ = white
    int pixelLightCount = GetAdditionalLightsCount();
    
    // Uncomment the next line to see debug colors instead of actual lighting
    Color = float3(pixelLightCount >= 1, pixelLightCount >= 2, pixelLightCount >= 3); return;

    for (int i = 0; i < pixelLightCount; i++)
    {
        light = GetAdditionalLight(i, Position, 1);
        Color += CalculateCelShading(light, s);
    }
#endif
}

void LightingCelShaded_half(
    half Smoothness,
    half RimThreshold,
    half3 Position,
    half3 Normal,
    half3 View,
    half EdgeDiffuse,
    half EdgeSpecular,
    half EdgeSpecularOffset,
    half EdgeDistanceAttenuation,
    half EdgeShadowAttenuation,
    half EdgeRim,
    half EdgeRimOffset,
    out half3 Color)
{
    float3 colorFloat;
    LightingCelShaded_float(
        Smoothness,
        RimThreshold,
        Position,
        Normal,
        View,
        EdgeDiffuse,
        EdgeSpecular,
        EdgeSpecularOffset,
        EdgeDistanceAttenuation,
        EdgeShadowAttenuation,
        EdgeRim,
        EdgeRimOffset,
        colorFloat);
    Color = colorFloat;
}

#endif
