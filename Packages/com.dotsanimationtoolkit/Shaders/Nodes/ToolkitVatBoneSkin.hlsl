// ToolkitVatBoneSkin — skins a vertex against up to four bones read from a vertex animation
// texture, in the VERTEX stage.
//
// The bone-matrix flavour of VAT: the texture holds per-bone 3x4 skinning matrices and the
// mesh carries indices and weights in UV channels. Wire Position (Object Space) in and the
// result to the master stack's Vertex Position block.
//
// VERTEX STAGE ONLY, and the texture must be sampled with a point/clamp sampler — a filtered
// sampler blends neighbouring bones' matrices together and produces limbs that melt.
//
// Blend Weight 0 skips every Frame B fetch entirely. That fast path is why a non-crossfading
// crowd costs half what a crossfading one does, so leave it at 0 unless a layer is actually
// mid-crossfade.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.dotsanimationtoolkit/Shaders/Includes/ToolkitVat.hlsl"

///<funchints>
///     <sg:ProviderKey>DotsAnimationToolkit.ToolkitVatBoneSkin</sg:ProviderKey>
///     <sg:DisplayName>VAT Bone Skin</sg:DisplayName>
///     <sg:SearchCategory>DOTS Animation Toolkit</sg:SearchCategory>
///</funchints>
///<paramhints name = "vatTexture">
///     <sg:DisplayName>VAT Texture</sg:DisplayName>
///</paramhints>
///<paramhints name = "vatSampler">
///     <sg:DisplayName>VAT Sampler</sg:DisplayName>
///</paramhints>
///<paramhints name = "positionOS">
///     <sg:DisplayName>Position (Object)</sg:DisplayName>
///</paramhints>
///<paramhints name = "boneIndices">
///     <sg:DisplayName>Bone Indices</sg:DisplayName>
///</paramhints>
///<paramhints name = "boneWeights">
///     <sg:DisplayName>Bone Weights</sg:DisplayName>
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
void ToolkitVatBoneSkin(
    UnityTexture2D vatTexture,
    UnitySamplerState vatSampler,
    float3 positionOS,
    float4 boneIndices,
    float4 boneWeights,
    float globalFrameA,
    float globalFrameB,
    float blendWeight,
    float4 vatTexelParams,
    out float3 skinnedPositionOS)
{
#if defined(SHADERGRAPH_PREVIEW)
    skinnedPositionOS = positionOS;
#else
    // The include takes raw Texture2D/SamplerState because it must stay usable outside Shader
    // Graph; the wrapper structs unwrap to exactly those.
    skinnedPositionOS = VatBoneSkin(
        vatTexture.tex,
        vatSampler.samplerstate,
        positionOS,
        boneIndices,
        boneWeights,
        globalFrameA,
        globalFrameB,
        blendWeight,
        vatTexelParams);
#endif
}
