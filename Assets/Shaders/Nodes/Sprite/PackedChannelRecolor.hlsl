// PackedChannelRecolor — turns a channel-packed mask texture into a recolorable
// sprite. The packed sample's channels are zones: R = base fill, G = a second
// layer composited on top (e.g. a bloody mouth), B = a third layer on top of
// that, A = the literal output alpha, passed through untouched.
//
// Each layer has its own RGBA color input; only the RGB tints. The color's
// ALPHA is that layer's blend strength — 0 hides the layer entirely, 1 shows
// it fully, in-between fades it — so optional details toggle per material or
// per instance without extra properties. The base layer ignores its alpha.
//
// Pixels claimed by no channel composite to black, so baked black outlines
// survive any recolor. Pure math, no lighting includes — safe in preview.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.PackedChannelRecolor</sg:ProviderKey>
///     <sg:DisplayName>Packed Channel Recolor</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Sprite</sg:SearchCategory>
///</funchints>
///<paramhints name = "packedSample">
///     <sg:DisplayName>Packed Sample</sg:DisplayName>
///     <sg:Default>0, 0, 0, 1</sg:Default>
///</paramhints>
///<paramhints name = "baseLayerColor">
///     <sg:DisplayName>Base Color (R)</sg:DisplayName>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "secondLayerColor">
///     <sg:DisplayName>Secondary Color (G)</sg:DisplayName>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
///<paramhints name = "thirdLayerColor">
///     <sg:DisplayName>Tertiary Color (B)</sg:DisplayName>
///     <sg:Default>1, 1, 1, 1</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void PackedChannelRecolor(
    float4 packedSample,
    float4 baseLayerColor,
    float4 secondLayerColor,
    float4 thirdLayerColor,
    out float3 recoloredColor,
    out float recoloredAlpha)
{
    float3 composite = baseLayerColor.rgb * packedSample.r;
    composite = lerp(composite, secondLayerColor.rgb, packedSample.g * secondLayerColor.a);
    composite = lerp(composite, thirdLayerColor.rgb, packedSample.b * thirdLayerColor.a);

    recoloredColor = composite;
    recoloredAlpha = packedSample.a;
}
