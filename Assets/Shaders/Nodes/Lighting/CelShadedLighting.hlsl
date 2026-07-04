// CelShadedLighting — the project's toon lighting as a reflection node.
// Same math as the old LightingCelShaded_float custom function (banded diffuse,
// toon specular, rim; main light + up to 8 additional per-object lights), so
// swapping a graph's Custom Function node for this one is a pure rewire — the
// image must not change.
//
// Feed Position/Normal/View from world-space Position, Normal Vector and
// View Vector nodes. Multiply the result with the surface albedo (e.g. the
// PainterlyColor output) before the master stack.

#include "ShaderApiReflectionSupport.hlsl"
#include "CelShadedCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.CelShadedLighting</sg:ProviderKey>
///     <sg:DisplayName>Cel Shaded Lighting</sg:DisplayName>
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
void CelShadedLighting(
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
    CelShadedLightingCore(
        smoothness, rimThreshold, positionWS, normalWS, viewDirectionWS,
        edgeDiffuse, edgeSpecular, edgeSpecularOffset,
        edgeDistanceAttenuation, edgeShadowAttenuation,
        edgeRim, edgeRimOffset,
        color);
}
