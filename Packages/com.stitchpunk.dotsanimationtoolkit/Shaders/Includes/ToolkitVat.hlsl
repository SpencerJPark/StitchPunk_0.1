#ifndef STITCHPUNK_TOOLKIT_VAT_INCLUDED
#define STITCHPUNK_TOOLKIT_VAT_INCLUDED

// =====================================================================================
// DOTS Animation Toolkit — vertex animation textures (architecture sections 6.4, 4.7)
//
// Plays baked animation off a texture instead of a skeleton. Two flavours:
//
//   BONE   — the texture holds one 3x4 matrix per bone per frame. The vertex is skinned on
//            the GPU against up to four influences. Small textures, cheap to author, and
//            the deformation is exactly what the source rig did.
//   VERTEX — the texture holds one position (and normal) per vertex per frame. Larger,
//            but it reproduces anything: cloth, blendshapes, simulation caches.
//
// STANDALONE BY DESIGN, like the other includes here: no other includes, no property
// declarations, no globals. Textures and parameters arrive as arguments so these can be
// lifted into any URP project or driven from a Custom Function node.
//
// ------------------------------------------------------------------------------------
// TEXTURE LAYOUT (the contract with VatTextureBaker)
//
//   A "global frame" is a row block. Frame f of a clip occupies `rowsPerFrame` consecutive
//   rows starting at `f * rowsPerFrame`. Within a frame, element e (a bone or a vertex)
//   sits at column `e % textureWidth`, on the row offset `e / textureWidth` — so an
//   element count larger than the texture width simply wraps onto the next row.
//
//   Bone flavour uses rowsPerFrame = 3: one row per matrix row, x/y/z packed as RGBA
//   with translation in .w.
//
// The CPU writes a FRACTIONAL global frame index into `_VatFrameA` (section 5.8), so the
// shader lerps between floor and ceil itself. That is what makes VAT playback smooth at
// any framerate rather than stepping at the bake rate.
// =====================================================================================

// vatTexelParams packs the layout so a shader needs one float4 rather than four floats:
//   x = textureWidth, y = textureHeight, z = rowsPerFrame, w = boneOrVertexCount
#define TOOLKIT_VAT_WIDTH(vatTexelParams)     ((vatTexelParams).x)
#define TOOLKIT_VAT_HEIGHT(vatTexelParams)    ((vatTexelParams).y)
#define TOOLKIT_VAT_ROWS_PER_FRAME(vatTexelParams) ((vatTexelParams).z)
#define TOOLKIT_VAT_ELEMENT_COUNT(vatTexelParams)  ((vatTexelParams).w)

// -------------------------------------------------------------------------------------
// The texel centre for element `elementIndex` at integer frame `frame`, row `rowInFrame`.
//
// Sampling at the centre (+0.5) rather than the corner is not a nicety: at a corner a
// bilinear sampler blends two neighbouring elements, which on a bone matrix means an
// average of two unrelated joints and a limb that visibly melts.
// -------------------------------------------------------------------------------------
float2 ToolkitVatTexelUV(float elementIndex, float frame, float rowInFrame, float4 vatTexelParams)
{
    float width = TOOLKIT_VAT_WIDTH(vatTexelParams);
    float height = TOOLKIT_VAT_HEIGHT(vatTexelParams);
    float rowsPerFrame = TOOLKIT_VAT_ROWS_PER_FRAME(vatTexelParams);

    float column = fmod(elementIndex, width);
    float wrapRow = floor(elementIndex / width);
    float row = frame * rowsPerFrame + rowInFrame + wrapRow;

    return float2((column + 0.5) / width, (row + 0.5) / height);
}

// -------------------------------------------------------------------------------------
// Reads one bone matrix (3x4, row-major) for `boneIndex` at integer `frame`.
// -------------------------------------------------------------------------------------
float3x4 ToolkitVatReadBoneMatrix(
    Texture2D vatTexture, SamplerState vatSampler,
    float boneIndex, float frame, float4 vatTexelParams)
{
    float4 row0 = vatTexture.SampleLevel(vatSampler, ToolkitVatTexelUV(boneIndex, frame, 0.0, vatTexelParams), 0);
    float4 row1 = vatTexture.SampleLevel(vatSampler, ToolkitVatTexelUV(boneIndex, frame, 1.0, vatTexelParams), 0);
    float4 row2 = vatTexture.SampleLevel(vatSampler, ToolkitVatTexelUV(boneIndex, frame, 2.0, vatTexelParams), 0);
    return float3x4(row0, row1, row2);
}

// -------------------------------------------------------------------------------------
// Bone matrix at a FRACTIONAL frame, lerping the two neighbouring frames.
//
// Component-wise matrix lerp, deliberately. It is not a correct rotation interpolation —
// that would need per-bone quaternion slerp, which the GPU cannot afford here — and §12 R1
// records the consequence: long crossfades between very different poses look rubbery.
// Between ADJACENT frames of one clip the error is negligible, which is the case this
// path is for.
// -------------------------------------------------------------------------------------
float3x4 VatBoneMatrixAtFrame(
    Texture2D vatTexture, SamplerState vatSampler,
    float boneIndex, float globalFrame, float4 vatTexelParams)
{
    float frameFloor = floor(globalFrame);
    float frameWeight = globalFrame - frameFloor;

    float3x4 matrixA = ToolkitVatReadBoneMatrix(vatTexture, vatSampler, boneIndex, frameFloor, vatTexelParams);

    // Loop-safe clips carry a duplicated final frame (§4.7), so floor+1 never leaves the
    // clip's own row range and no wrap test is needed here.
    float3x4 matrixB = ToolkitVatReadBoneMatrix(vatTexture, vatSampler, boneIndex, frameFloor + 1.0, vatTexelParams);

    return matrixA + (matrixB - matrixA) * frameWeight;
}

// -------------------------------------------------------------------------------------
// Skins a vertex against up to four bone influences, with an optional crossfade to a
// second clip.
//
//   blendWeight — 0 uses frame A only and skips every B fetch. That fast path is why a
//                 non-crossfading crowd costs half what a crossfading one does, and it is
//                 why VatMaterialSystem forces the weight to 0 rather than leaving it
//                 stale when a layer stops.
// -------------------------------------------------------------------------------------
float3 VatBoneSkin(
    Texture2D vatTexture, SamplerState vatSampler,
    float3 positionOS, float4 boneIndices, float4 boneWeights,
    float globalFrameA, float globalFrameB, float blendWeight,
    float4 vatTexelParams)
{
    float4 position = float4(positionOS, 1.0);
    float3 skinned = float3(0.0, 0.0, 0.0);

    [unroll]
    for (int influence = 0; influence < 4; influence++)
    {
        float weight = boneWeights[influence];
        if (weight <= 0.0)
        {
            continue;
        }

        float boneIndex = boneIndices[influence];
        float3x4 boneMatrix = VatBoneMatrixAtFrame(
            vatTexture, vatSampler, boneIndex, globalFrameA, vatTexelParams);

        if (blendWeight > 0.0)
        {
            float3x4 boneMatrixB = VatBoneMatrixAtFrame(
                vatTexture, vatSampler, boneIndex, globalFrameB, vatTexelParams);
            boneMatrix = boneMatrix + (boneMatrixB - boneMatrix) * blendWeight;
        }

        skinned += mul(boneMatrix, position) * weight;
    }
    return skinned;
}

// -------------------------------------------------------------------------------------
// Vertex flavour: reads the baked position for this vertex directly.
//
// `vertexIndex` is the vertex's own index in the mesh, which a baked mesh carries in a UV
// channel — vertex id semantics are not available in every pass on every target, and a UV
// is.
// -------------------------------------------------------------------------------------
float3 VatVertexFetch(
    Texture2D vatTexture, SamplerState vatSampler,
    float vertexIndex, float globalFrameA, float globalFrameB, float blendWeight,
    float4 vatTexelParams)
{
    float frameFloor = floor(globalFrameA);
    float frameWeight = globalFrameA - frameFloor;

    float3 positionA0 = vatTexture.SampleLevel(
        vatSampler, ToolkitVatTexelUV(vertexIndex, frameFloor, 0.0, vatTexelParams), 0).xyz;
    float3 positionA1 = vatTexture.SampleLevel(
        vatSampler, ToolkitVatTexelUV(vertexIndex, frameFloor + 1.0, 0.0, vatTexelParams), 0).xyz;
    float3 positionA = lerp(positionA0, positionA1, frameWeight);

    if (blendWeight <= 0.0)
    {
        return positionA;
    }

    float frameFloorB = floor(globalFrameB);
    float frameWeightB = globalFrameB - frameFloorB;

    float3 positionB0 = vatTexture.SampleLevel(
        vatSampler, ToolkitVatTexelUV(vertexIndex, frameFloorB, 0.0, vatTexelParams), 0).xyz;
    float3 positionB1 = vatTexture.SampleLevel(
        vatSampler, ToolkitVatTexelUV(vertexIndex, frameFloorB + 1.0, 0.0, vatTexelParams), 0).xyz;
    float3 positionB = lerp(positionB0, positionB1, frameWeightB);

    return lerp(positionA, positionB, blendWeight);
}

// -------------------------------------------------------------------------------------
// Octahedral normal decode (section 6.4).
//
// Normals bake into two channels rather than three: an octahedral mapping costs a handful
// of ALU to decode and saves a third of the normal texture, which on a vertex-flavour bake
// of a dense mesh is tens of megabytes.
// -------------------------------------------------------------------------------------
float3 ToolkitVatDecodeNormal(float2 encoded)
{
    float2 signedEncoded = encoded * 2.0 - 1.0;
    float3 normal = float3(signedEncoded.x, signedEncoded.y, 1.0 - abs(signedEncoded.x) - abs(signedEncoded.y));

    float fold = saturate(-normal.z);
    normal.xy += normal.xy >= 0.0 ? -fold : fold;
    return normalize(normal);
}

#endif // STITCHPUNK_TOOLKIT_VAT_INCLUDED
