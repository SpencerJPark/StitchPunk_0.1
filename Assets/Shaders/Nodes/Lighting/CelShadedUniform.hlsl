// CelShadedUniform — the artist-facing toon lighting: same banding as
// CelShadedLighting, but the seven per-material "edge" dials are frozen as constants
// in CelShadedCommon.hlsl and two controls that actually vary between objects are
// exposed instead.
//
// Three inputs, by design:
//   Shininess    — how glossy: tightens AND strengthens the specular highlight.
//   Rim Strength — rim light on its own dial, decoupled from Shininess.
//   Shadow Lift  — how far shadows come up off pure black, toward Shadow Tint.
//
// Shadow Lift is the one this shader never had. Without it a fully shadowed fragment
// multiplies the albedo by zero and reads pure black; lift + a cool tint is what gives
// vector-art shading its "dark but still coloured" shadow side.
//
// Prefer this node over CelShadedLighting for any new graph. CelShadedLighting still
// exists because seven graphs are still wired to it — see Shaders.md for the rollout.

#include "ShaderApiReflectionSupport.hlsl"
#include "CelShadedCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.CelShadedUniform</sg:ProviderKey>
///     <sg:DisplayName>Cel Shaded Uniform</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Lighting</sg:SearchCategory>
///</funchints>
///<paramhints name = "shininess">
///     <sg:DisplayName>Shininess</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.5</sg:Default>
///</paramhints>
///<paramhints name = "rimStrength">
///     <sg:DisplayName>Rim Strength</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.2</sg:Default>
///</paramhints>
///<paramhints name = "shadowLift">
///     <sg:DisplayName>Shadow Lift</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0.25</sg:Default>
///</paramhints>
///<paramhints name = "shadowTint">
///     <sg:DisplayName>Shadow Tint</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>0.28, 0.34, 0.52</sg:Default>
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
UNITY_EXPORT_REFLECTION
void CelShadedUniform(
    float shininess,
    float rimStrength,
    float shadowLift,
    float3 shadowTint,
    float3 positionWS,
    float3 normalWS,
    float3 viewDirectionWS,
    out float3 color)
{
    CelShadedUniformCore(
        shininess, rimStrength, shadowLift, shadowTint,
        positionWS, normalWS, viewDirectionWS,
        color);
}
