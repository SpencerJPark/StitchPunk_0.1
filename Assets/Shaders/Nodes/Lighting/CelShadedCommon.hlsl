// CelShadedCommon.hlsl — shared, NON-exported cel-shading core.
//
// The math here is the project's toon lighting (banded diffuse, toon specular,
// rim) — identical to the old LightingCelShaded.hlsl custom function, so
// converting graphs to the reflection nodes must not change the look.
// Both CelShadedLighting (node) and the deprecated Legacy/LightingCelShaded.hlsl
// wrapper call into this file.

#ifndef CEL_SHADED_COMMON_INCLUDED
#define CEL_SHADED_COMMON_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

struct CelEdgeConstants
{
    float edgeDiffuse;
    float edgeSpecular;
    float edgeSpecularOffset;
    float edgeDistanceAttenuation;
    float edgeShadowAttenuation;
    float edgeRim;
    float edgeRimOffset;
};

struct CelSurfaceVariables
{
    float3 normal;
    float3 view;
    float smoothness;
    float shininess;
    float rimThreshold;
    CelEdgeConstants edges;
};

float3 CalculateCelShading(Light incomingLight, CelSurfaceVariables surface)
{
    float shadowAttenuationSmoothStep = smoothstep(0.0f, surface.edges.edgeShadowAttenuation, incomingLight.shadowAttenuation);
    float distanceAttenuationSmoothStep = smoothstep(0.0f, surface.edges.edgeDistanceAttenuation, incomingLight.distanceAttenuation);
    float attenuation = shadowAttenuationSmoothStep * distanceAttenuationSmoothStep;

    float diffuse = saturate(dot(surface.normal, incomingLight.direction));
    diffuse *= attenuation;
    diffuse = smoothstep(0.0f, surface.edges.edgeDiffuse, diffuse);

    float3 halfVector = SafeNormalize(incomingLight.direction + surface.view);

    float specular = saturate(dot(surface.normal, halfVector));
    specular = pow(specular, surface.shininess);
    specular *= diffuse * surface.smoothness;
    specular = surface.smoothness * smoothstep(
        (1 - surface.smoothness) * surface.edges.edgeSpecular + surface.edges.edgeSpecularOffset,
        surface.edges.edgeSpecular + surface.edges.edgeSpecularOffset,
        specular);

    float rim = 1 - dot(surface.view, surface.normal);
    rim *= pow(diffuse, surface.rimThreshold);
    rim = surface.smoothness * smoothstep(
        surface.edges.edgeRim - 0.5 * surface.edges.edgeRimOffset,
        surface.edges.edgeRim + 0.5 * surface.edges.edgeRimOffset,
        rim);

    return incomingLight.color * (diffuse + max(specular, rim));
}

CelSurfaceVariables BuildCelSurface(
    float smoothness,
    float rimThreshold,
    float3 normal,
    float3 view,
    float edgeDiffuse,
    float edgeSpecular,
    float edgeSpecularOffset,
    float edgeDistanceAttenuation,
    float edgeShadowAttenuation,
    float edgeRim,
    float edgeRimOffset)
{
    CelSurfaceVariables surface;
    surface.normal = normalize(normal);
    surface.view = SafeNormalize(view);
    surface.smoothness = smoothness;
    surface.shininess = exp2(10 * smoothness + 1);
    surface.rimThreshold = rimThreshold;
    surface.edges.edgeDiffuse = edgeDiffuse;
    surface.edges.edgeSpecular = edgeSpecular;
    surface.edges.edgeSpecularOffset = edgeSpecularOffset;
    surface.edges.edgeDistanceAttenuation = edgeDistanceAttenuation;
    surface.edges.edgeShadowAttenuation = edgeShadowAttenuation;
    surface.edges.edgeRim = edgeRim;
    surface.edges.edgeRimOffset = edgeRimOffset;
    return surface;
}
#endif // SHADERGRAPH_PREVIEW

// Full main-light + additional-lights evaluation. Reads _AdditionalLightsCount
// directly (GetAdditionalPerObjectLight) instead of the keyword-gated
// GetAdditionalLightsCount() so point/spot lights work without the
// _ADDITIONAL_LIGHTS keyword being set on the graph — deliberate, do not
// "simplify" back to the gated call.
void CelShadedLightingCore(
    float smoothness,
    float rimThreshold,
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    float edgeDiffuse,
    float edgeSpecular,
    float edgeSpecularOffset,
    float edgeDistanceAttenuation,
    float edgeShadowAttenuation,
    float edgeRim,
    float edgeRimOffset,
    out float3 color)
{
#if defined(SHADERGRAPH_PREVIEW)
    color = float3(0.5, 0.5, 0.5);
#else
    CelSurfaceVariables surface = BuildCelSurface(
        smoothness, rimThreshold, normalWS, viewDirectionWS,
        edgeDiffuse, edgeSpecular, edgeSpecularOffset,
        edgeDistanceAttenuation, edgeShadowAttenuation,
        edgeRim, edgeRimOffset);

    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    color = CalculateCelShading(mainLight, surface);

    uint additionalLightCount = min(_AdditionalLightsCount.x, 8); // Cap at 8 for safety

    for (uint lightIndex = 0; lightIndex < additionalLightCount; lightIndex++)
    {
        Light additionalLight = GetAdditionalPerObjectLight(lightIndex, positionWS);
        color += CalculateCelShading(additionalLight, surface);
    }
#endif
}

#endif // CEL_SHADED_COMMON_INCLUDED
