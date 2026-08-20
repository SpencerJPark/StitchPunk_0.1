#ifndef DOTS_ANIMATION_TOOLKIT_FLIPBOOK_INCLUDED
#define DOTS_ANIMATION_TOOLKIT_FLIPBOOK_INCLUDED

// =====================================================================================
// DOTS Animation Toolkit — flipbook addressing (architecture sections 6.1, 5.7)
//
// Two ways to show a frame from a sheet of them:
//
//   SLICE — the frames are layers of a Texture2DArray, and the frame index is the third
//           UV coordinate. No UV maths at all; the sampler does the work.
//   ATLAS — the frames are cells of one texture, and a frame is a scale+offset applied to
//           the mesh UV.
//
// STANDALONE BY DESIGN, like the other includes here: no other includes, no property
// declarations, no globals. Everything arrives as a parameter so these can be lifted into
// any URP project or called from a Custom Function node in someone else's graph.
// =====================================================================================

// -------------------------------------------------------------------------------------
// Slice mode: build the Texture2DArray sample coordinate for a frame.
//
// The index is carried as a float because that is what the per-instance property upload
// path uses (section 6.2, `_ImageIndex`), and because SAMPLE_TEXTURE2D_ARRAY takes a float
// slice anyway. Rounding rather than truncating matters: an index that arrives as 2.9999
// from float packing is meant to be frame 3, and truncation would show frame 2 for one
// frame in a way that reads as a flicker rather than as an off-by-one.
// -------------------------------------------------------------------------------------
float3 SliceUV(float2 uv, float imageIndex)
{
    return float3(uv, round(imageIndex));
}

// -------------------------------------------------------------------------------------
// Atlas mode: map a mesh UV into one cell of the sheet.
//
//   atlasRect — xy = scale, zw = offset. This is `_AtlasFrame` from section 6.2, and it
//               matches ClipSampler.IdentityAtlasRect = (1, 1, 0, 0), i.e. "the whole
//               texture", which is what an actor with no atlas track renders.
// -------------------------------------------------------------------------------------
float2 AtlasFrameUV(float2 uv, float4 atlasRect)
{
    return uv * atlasRect.xy + atlasRect.zw;
}

// -------------------------------------------------------------------------------------
// Atlas mode from a grid description, for callers who would rather say "4x4 sheet,
// frame 7" than compute a rect.
//
// Row 0 is the TOP row, because that is how sprite sheets are read and authored, whereas
// UV space runs upward from the bottom. Getting this backwards is a whole-sheet vertical
// flip that looks like the art was exported wrong.
// -------------------------------------------------------------------------------------
float4 AtlasRectFromGrid(float frameIndex, float columns, float rows)
{
    float safeColumns = max(columns, 1.0);
    float safeRows = max(rows, 1.0);

    float index = round(frameIndex);
    float column = fmod(index, safeColumns);
    float row = floor(index / safeColumns);

    float2 scale = float2(1.0 / safeColumns, 1.0 / safeRows);
    float2 offset = float2(column * scale.x, 1.0 - (row + 1.0) * scale.y);
    return float4(scale, offset);
}

#endif // DOTS_ANIMATION_TOOLKIT_FLIPBOOK_INCLUDED
