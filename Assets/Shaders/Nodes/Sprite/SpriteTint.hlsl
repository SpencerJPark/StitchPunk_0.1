// SpriteTint — outline-safe multiply tint for a baked vector sprite.
//
// result = baseColor * tint. Because 0 * anything = 0, a black outline baked
// into the sprite (0,0,0) survives every tint automatically — no mask needed.
// A part must be baked into the atlas with a WHITE / light-gray fill and its
// outline baked black; the colour comes entirely from the Tint port. A part
// baked already-coloured multiplies to a muddy dark shade.
//
// This is the single-region node (pants, hat, one shirt zone). For 2+ colour
// zones in one sprite use SpriteTintMasked. In this project the 2D graphs
// already do this exact multiply via the _BaseColor property; this node is the
// named, reusable equivalent for hand-wiring or new graphs.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.SpriteTint</sg:ProviderKey>
///     <sg:DisplayName>Sprite Tint</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Sprite</sg:SearchCategory>
///</funchints>
///<paramhints name = "baseColor">
///     <sg:DisplayName>Base Color</sg:DisplayName>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "tint">
///     <sg:DisplayName>Tint</sg:DisplayName>
///     <sg:Color/>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void SpriteTint(float4 baseColor, float4 tint, out float4 tinted)
{
    tinted.rgb = baseColor.rgb * tint.rgb;   // black outline * tint = black
    tinted.a   = baseColor.a   * tint.a;     // keep sprite alpha, optional fade via Tint.a
}
