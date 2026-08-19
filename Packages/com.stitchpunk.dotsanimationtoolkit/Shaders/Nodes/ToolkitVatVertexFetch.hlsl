// ToolkitVatVertexFetch — reads a vertex's baked object-space position out of a vertex
// animation texture, in the VERTEX stage.
//
// The vertex flavour of VAT: no bones, no skinning, the texture simply holds where every
// vertex is on every frame. Heavier on texture memory than the bone flavour and cheaper on
// ALU, and the only option for deformation no skeleton can express — cloth, a cape, a
// tentacle.
//
// VERTEX STAGE ONLY, point/clamp sampler.
//
// Vertex Index comes from a UV channel the bake writes, NOT from a vertex-id semantic: vertex
// id is not available in every pass on every target, and a UV is.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitVat.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ToolkitVatVertexFetch</sg:ProviderKey>
///     <sg:DisplayName>VAT Vertex Fetch</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Animation</sg:SearchCategory>
///</funchints>
///<paramhints name = "vatTexture">
///     <sg:DisplayName>VAT Texture</sg:DisplayName>
///</paramhints>
///<paramhints name = "vatSampler">
///     <sg:DisplayName>VAT Sampler</sg:DisplayName>
///</paramhints>
///<paramhints name = "vertexIndex">
///     <sg:DisplayName>Vertex Index</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "globalFrameA">
///     <sg:DisplayName>Frame A</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "globalFrameB">
///     <sg:DisplayName>Frame B</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "blendWeight">
///     <sg:DisplayName>Blend Weight</sg:DisplayName>
///     <sg:Range>0, 1</sg:Range>
///     <sg:Default>0</sg:Default>
///</paramhints>
///<paramhints name = "vatTexelParams">
///     <sg:DisplayName>Texel Params</sg:DisplayName>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ToolkitVatVertexFetch(
    UnityTexture2D vatTexture,
    UnitySamplerState vatSampler,
    float vertexIndex,
    float globalFrameA,
    float globalFrameB,
    float blendWeight,
    float4 vatTexelParams,
    out float3 fetchedPositionOS)
{
#if defined(SHADERGRAPH_PREVIEW)
    fetchedPositionOS = float3(0.0, 0.0, 0.0);
#else
    fetchedPositionOS = VatVertexFetch(
        vatTexture.tex,
        vatSampler.samplerstate,
        vertexIndex,
        globalFrameA,
        globalFrameB,
        blendWeight,
        vatTexelParams);
#endif
}
