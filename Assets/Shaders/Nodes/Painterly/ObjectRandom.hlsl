// ObjectRandom — per-instance randomness derived from the object's world
// position (feed it from the Object node's Position output). This is the
// position-based variation trick from the painterly setup: neighbouring props
// sharing one material get different hue/value jitter and UV offsets, so
// copies never look stamped.
//
// Position Scale controls sensitivity: higher = small moves change the result
// more; lower = objects must move further before their look shifts.

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ObjectRandom</sg:ProviderKey>
///     <sg:DisplayName>Object Random</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "objectPosition">
///     <sg:DisplayName>Object Position</sg:DisplayName>
///</paramhints>
///<paramhints name = "positionScale">
///     <sg:DisplayName>Position Scale</sg:DisplayName>
///     <sg:Range>0, 10</sg:Range>
///     <sg:Default>1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ObjectRandom(
    float3 objectPosition,
    float positionScale,
    out float3 random3,
    out float random01)
{
    float3 scaledPosition = objectPosition * positionScale;
    random3 = PainterlyHash33(scaledPosition);
    random01 = PainterlyHash13(scaledPosition);
}
