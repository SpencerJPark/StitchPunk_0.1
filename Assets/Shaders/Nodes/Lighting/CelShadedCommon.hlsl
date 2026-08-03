// CelShadedCommon.hlsl — shared, NON-exported cel-shading core.
//
// The math here is the project's toon lighting (banded diffuse, toon specular,
// rim) — identical to the old LightingCelShaded.hlsl custom function, so
// converting graphs to the reflection nodes must not change the look.
// Both CelShadedLighting (node) and the deprecated Legacy/LightingCelShaded.hlsl
// wrapper call into this file.

#ifndef CEL_SHADED_COMMON_INCLUDED
#define CEL_SHADED_COMMON_INCLUDED

// --- Uniform (stylized) cel constants -----------------------------------------
//
// These are the seven band-shape values that CelShadedLighting exposes as
// per-material inputs. The CelShadedUniform node freezes them here instead, because
// the vector-art look depends on every surface banding IDENTICALLY — a per-material
// terminator width is exactly what makes a scene read as inconsistent.
//
// Tuning the look is a deliberate one-line edit HERE that moves every material at
// once. Do not re-expose these as node inputs; add a new artist dial only if it is
// something that genuinely should differ between two objects in the same scene.
//
// edgeDiffuse is the terminator width: small = hard cel edge. Not 0 — a true step
// aliases badly on curved surfaces, so keep a sliver of smoothstep for the AA.

static const float kUniformEdgeDiffuse             = 0.025;
static const float kUniformEdgeSpecular            = 0.01;
static const float kUniformEdgeSpecularOffset      = 0.05;
static const float kUniformEdgeDistanceAttenuation = 0.1;
static const float kUniformEdgeShadowAttenuation   = 0.1;
static const float kUniformEdgeRim                 = 0.6;
static const float kUniformEdgeRimOffset           = 0.1;
static const float kUniformRimThreshold            = 0.1;

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

// Same banding math as CalculateCelShading, with two deliberate differences:
// the seven edge constants are frozen (see the top of this file), and rim strength
// is its own input instead of being tied to shininess — a matte object should still
// be able to catch a rim, and a glossy one should be able to have none.
float3 CalculateCelShadingUniform(
    Light incomingLight,
    float3 normal,
    float3 view,
    float shininess,
    float rimStrength)
{
    float shadowAttenuationSmoothStep = smoothstep(0.0f, kUniformEdgeShadowAttenuation, incomingLight.shadowAttenuation);
    float distanceAttenuationSmoothStep = smoothstep(0.0f, kUniformEdgeDistanceAttenuation, incomingLight.distanceAttenuation);
    float attenuation = shadowAttenuationSmoothStep * distanceAttenuationSmoothStep;

    float diffuse = saturate(dot(normal, incomingLight.direction));
    diffuse *= attenuation;
    diffuse = smoothstep(0.0f, kUniformEdgeDiffuse, diffuse);

    float3 halfVector = SafeNormalize(incomingLight.direction + view);
    float specularExponent = exp2(10 * shininess + 1);

    float specular = saturate(dot(normal, halfVector));
    specular = pow(specular, specularExponent);
    specular *= diffuse * shininess;
    specular = shininess * smoothstep(
        (1 - shininess) * kUniformEdgeSpecular + kUniformEdgeSpecularOffset,
        kUniformEdgeSpecular + kUniformEdgeSpecularOffset,
        specular);

    float rim = 1 - dot(view, normal);
    rim *= pow(diffuse, kUniformRimThreshold);
    rim = rimStrength * smoothstep(
        kUniformEdgeRim - 0.5 * kUniformEdgeRimOffset,
        kUniformEdgeRim + 0.5 * kUniformEdgeRimOffset,
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

// Uniform cel shading + a shadow floor.
//
// The un-lifted math bottoms out at literally zero — a fully shadowed fragment
// multiplies the albedo by 0 and goes pure black, which is why this shader had no way
// to say "dark but still coloured". shadowLift raises that floor toward shadowTint:
//
//   lift 0 -> identical to the un-lifted result (pure black shadows, max contrast)
//   lift 1 -> fully flat, tint only, no shading at all
//
// Lit areas are scaled by (1 - lift) so the total stays around 1.0 and turning the
// lift up cannot blow out the highlights.
void CelShadedUniformCore(
    float shininess,
    float rimStrength,
    float shadowLift,
    float3 shadowTint,
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    out float3 color)
{
#if defined(SHADERGRAPH_PREVIEW)
    color = float3(0.5, 0.5, 0.5);
#else
    float3 normal = normalize(normalWS);
    float3 view = SafeNormalize(viewDirectionWS);

    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    float3 accumulated = CalculateCelShadingUniform(mainLight, normal, view, shininess, rimStrength);

    // Reads _AdditionalLightsCount directly rather than the keyword-gated
    // GetAdditionalLightsCount(), same as CelShadedLightingCore — deliberate, so point
    // and spot lights work without _ADDITIONAL_LIGHTS being set on the graph.
    uint additionalLightCount = min(_AdditionalLightsCount.x, 8);

    for (uint lightIndex = 0; lightIndex < additionalLightCount; lightIndex++)
    {
        Light additionalLight = GetAdditionalPerObjectLight(lightIndex, positionWS);
        accumulated += CalculateCelShadingUniform(additionalLight, normal, view, shininess, rimStrength);
    }

    float clampedLift = saturate(shadowLift);
    color = shadowTint * clampedLift + accumulated * (1.0 - clampedLift);
#endif
}

#endif // CEL_SHADED_COMMON_INCLUDED
