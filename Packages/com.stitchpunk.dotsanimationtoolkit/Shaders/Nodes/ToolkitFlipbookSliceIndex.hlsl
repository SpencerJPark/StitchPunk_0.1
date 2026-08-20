// ToolkitFlipbookSliceIndex — resolves a flipbook frame number to a Texture2DArray slice.
//
// Wire the frame number in and the result into a Sample Texture 2D Array node's Index port.
//
// The whole node is a round, and the round is the point. An index arriving as 2.9999 out of
// float packing means frame 3; truncating would show frame 2 for a single frame, which reads
// as a flicker rather than as an off-by-one and is correspondingly hard to diagnose.
//
// Separate from ToolkitFlipbookSliceUV because Shader Graph's array sampler takes UV and
// index on SEPARATE ports, whereas the include's SliceUV packs both into a float3 for
// hand-written callers. Same rule, two shapes, because the two consumers genuinely differ.

#include "ShaderApiReflectionSupport.hlsl"

///<funchints>
///     <sg:ProviderKey>DotsAnimationToolkit.ToolkitFlipbookSliceIndex</sg:ProviderKey>
///     <sg:DisplayName>Flipbook Slice Index</sg:DisplayName>
///     <sg:SearchCategory>DOTS Animation Toolkit</sg:SearchCategory>
///</funchints>
///<paramhints name = "imageIndex">
///     <sg:DisplayName>Image Index</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ToolkitFlipbookSliceIndex(float imageIndex, out float sliceIndex)
{
    sliceIndex = round(imageIndex);
}
