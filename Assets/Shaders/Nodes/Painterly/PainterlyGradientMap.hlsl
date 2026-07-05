// PainterlyGradientMap — the UV-driven palette lookup for the painterly setup.
//
// The gradient LUT is a 64x64 "atlas of gradients": each horizontal ROW is one
// colour gradient (a colour zone), stacked top-to-bottom. The mesh UV samples it
// to pick a BASE COLOUR per fragment — sword hilt UVs land on the brown row,
// blade UVs on the grey row. This node outputs colour ONLY; light/dark shading
// comes from Cel Shaded Lighting downstream, and the brush-stroke mask supplies
// the painterly surface through the Height To Normal path — neither is done here.
//
// UVs are clamped (saturate) so a region's UV can never wrap into a neighbouring
// zone. Crisp zone boundaries (no bleeding between rows) come from the LUT's
// import settings — Point filter, Clamp, no mips — set by the generator tool.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.PainterlyGradientMap</sg:ProviderKey>
///     <sg:DisplayName>Painterly Gradient Map</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Painterly</sg:SearchCategory>
///</funchints>
///<paramhints name = "gradientLut">
///     <sg:DisplayName>Gradient LUT</sg:DisplayName>
///</paramhints>
///<paramhints name = "gradientSampler">
///     <sg:DisplayName>Gradient Sampler</sg:DisplayName>
///</paramhints>
///<paramhints name = "uv">
///     <sg:DisplayName>UV</sg:DisplayName>
///     <sg:Default>0, 0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void PainterlyGradientMap(
    UnityTexture2D gradientLut,
    UnitySamplerState gradientSampler,
    float2 uv,
    out float3 color)
{
    color = gradientLut.Sample(gradientSampler, saturate(uv)).rgb;
}
