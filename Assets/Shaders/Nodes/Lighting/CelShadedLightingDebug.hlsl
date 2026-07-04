// CelShadedLightingDebug — diagnostic variant of Cel Shaded Lighting.
//
// Differences from the main node, kept on purpose for comparison debugging:
//   - Additional lights go through the keyword-gated GetAdditionalLightsCount()
//     / GetAdditionalLight() path (the main node reads _AdditionalLightsCount
//     directly). If lights show in the main node but not here, the
//     _ADDITIONAL_LIGHTS keyword isn't active on the graph.
//   - Shadow coords honor the _MAIN_LIGHT_SHADOWS* keywords.
//
// The old file had a hardcoded early-return that ALWAYS showed the light-count
// colors (despite its comment saying it was opt-in). That's now a separate
// output instead: wire Light Count Color to Base Color when you want the
// overlay (black=0, red=1+, green=2+, blue=3+ additional lights), and Color
// for the actual lit result — no code editing required.

#include "ShaderApiReflectionSupport.hlsl"
#include "CelShadedCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.CelShadedLightingDebug</sg:ProviderKey>
///     <sg:DisplayName>Cel Shaded Lighting (Debug)</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Lighting</sg:SearchCategory>
///</funchints>
///<paramhints name = "smoothness">
///     <sg:DisplayName>Smoothness</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.5</sg:Default>
///</paramhints>
///<paramhints name = "rimThreshold">
///     <sg:DisplayName>Rim Threshold</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.1</sg:Default>
///</paramhints>
///<paramhints name = "positionWS">
///     <sg:DisplayName>Position (World)</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalWS">
///     <sg:DisplayName>Normal (World)</sg:DisplayName>
///</paramhints>
///<paramhints name = "viewDirectionWS">
///     <sg:DisplayName>View Direction (World)</sg:DisplayName>
///</paramhints>
///<paramhints name = "edgeDiffuse">
///     <sg:DisplayName>Edge Diffuse</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.02</sg:Default>
///</paramhints>
///<paramhints name = "edgeSpecular">
///     <sg:DisplayName>Edge Specular</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.01</sg:Default>
///</paramhints>
///<paramhints name = "edgeSpecularOffset">
///     <sg:DisplayName>Edge Specular Offset</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.05</sg:Default>
///</paramhints>
///<paramhints name = "edgeDistanceAttenuation">
///     <sg:DisplayName>Edge Distance Attenuation</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.1</sg:Default>
///</paramhints>
///<paramhints name = "edgeShadowAttenuation">
///     <sg:DisplayName>Edge Shadow Attenuation</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.1</sg:Default>
///</paramhints>
///<paramhints name = "edgeRim">
///     <sg:DisplayName>Edge Rim</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.6</sg:Default>
///</paramhints>
///<paramhints name = "edgeRimOffset">
///     <sg:DisplayName>Edge Rim Offset</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void CelShadedLightingDebug(
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
    out float3 color,
    out float3 lightCountColor)
{
#if defined(SHADERGRAPH_PREVIEW)
    color = float3(0.5, 0.5, 0.5);
    lightCountColor = float3(0, 0, 0);
#else
    CelSurfaceVariables surface = BuildCelSurface(
        smoothness, rimThreshold, normalWS, viewDirectionWS,
        edgeDiffuse, edgeSpecular, edgeSpecularOffset,
        edgeDistanceAttenuation, edgeShadowAttenuation,
        edgeRim, edgeRimOffset);

#if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    #if SHADOWS_SCREEN
        float4 clipPosition = TransformWorldToHClip(positionWS);
        float4 shadowCoord = ComputeScreenPos(clipPosition);
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif
#else
    float4 shadowCoord = float4(0, 0, 0, 0);
#endif

    Light mainLight = GetMainLight(shadowCoord);
    color = CalculateCelShading(mainLight, surface);

    int pixelLightCount = GetAdditionalLightsCount();
    lightCountColor = float3(pixelLightCount >= 1, pixelLightCount >= 2, pixelLightCount >= 3);

    for (int lightIndex = 0; lightIndex < pixelLightCount; lightIndex++)
    {
        Light additionalLight = GetAdditionalLight(lightIndex, positionWS, 1);
        color += CalculateCelShading(additionalLight, surface);
    }
#endif
}
