// HeightToNormal — perturbs a world-space normal from a grayscale height
// (the painterly Mask Value), using screen-space derivatives — the classic
// bump-from-height surface-gradient technique. Because the output is a WORLD
// space normal, it plugs directly into Cel Shaded Lighting's Normal input
// (this project lights via a custom node, not the master-stack Normal block,
// so tangent-space normal maps have nowhere to go — this node is the painterly
// "normal from height" for that custom-lit setup).
//
// Wiring: PainterlyColor.Mask Value -> Height; World Space Surface Data ->
// Position/Normal; output -> Cel Shaded Lighting.Normal (World).
// Fragment stage only (ddx/ddy).

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.HeightToNormal</sg:ProviderKey>
///     <sg:DisplayName>Height To Normal</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "height">
///     <sg:DisplayName>Height</sg:DisplayName>
///</paramhints>
///<paramhints name = "positionWS">
///     <sg:DisplayName>Position (World)</sg:DisplayName>
///</paramhints>
///<paramhints name = "normalWS">
///     <sg:DisplayName>Normal (World)</sg:DisplayName>
///</paramhints>
///<paramhints name = "strength">
///     <sg:DisplayName>Strength</sg:DisplayName>
///     <sg:Range>0, 4</sg:Range>
///     <sg:Default>0.4</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void HeightToNormal(
    float height,
    float3 positionWS,
    float3 normalWS,
    float strength,
    out float3 normal)
{
    float3 derivativeX = ddx(positionWS);
    float3 derivativeY = ddy(positionWS);
    float3 baseNormal = normalize(normalWS);

    float3 crossY = cross(derivativeY, baseNormal);
    float3 crossX = cross(baseNormal, derivativeX);
    float determinant = dot(derivativeX, crossY);

    float heightDerivativeX = ddx(height) * strength;
    float heightDerivativeY = ddy(height) * strength;
    float3 surfaceGradient = sign(determinant) * (heightDerivativeX * crossY + heightDerivativeY * crossX);

    normal = normalize(abs(determinant) * baseNormal - surfaceGradient);
}
