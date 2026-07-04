// SelectChannel — pick one grayscale stroke layer out of the RGB(A) mask texture.
// The painterly mask stores three independent brush-stroke layers in R, G and B;
// this node chooses which one drives the material (0=R, 1=G, 2=B, 3=A).

#include "ShaderApiReflectionSupport.hlsl"
#include "PainterlyCommon.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.SelectChannel</sg:ProviderKey>
///     <sg:DisplayName>Select Channel</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "maskSample">
///     <sg:DisplayName>Mask Sample</sg:DisplayName>
///</paramhints>
///<paramhints name = "channel">
///     <sg:DisplayName>Channel (0=R 1=G 2=B 3=A)</sg:DisplayName>
///     <sg:Range>0, 3</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
float SelectChannel(float4 maskSample, float channel)
{
    return PainterlySelectMaskChannel(maskSample, channel);
}
