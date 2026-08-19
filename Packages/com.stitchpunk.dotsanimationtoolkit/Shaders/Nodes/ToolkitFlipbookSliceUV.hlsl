// ToolkitFlipbookSliceUV — builds the Texture2DArray sample coordinate for a flipbook frame.
//
// Wire UV0 in and the result into a Sample Texture 2D Array node's UV port. Fragment stage.
//
// The index rounds rather than truncates, and that is deliberate: an index arriving as
// 2.9999 out of float packing means frame 3, and truncating would show frame 2 for a single
// frame in a way that reads as a flicker rather than as an off-by-one.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ToolkitFlipbookSliceUV</sg:ProviderKey>
///     <sg:DisplayName>Flipbook Slice UV</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Animation</sg:SearchCategory>
///</funchints>
///<paramhints name = "uv">
///     <sg:DisplayName>UV</sg:DisplayName>
///</paramhints>
///<paramhints name = "imageIndex">
///     <sg:DisplayName>Image Index</sg:DisplayName>
///     <sg:Default>0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ToolkitFlipbookSliceUV(float2 uv, float imageIndex, out float3 sliceCoordinate)
{
    sliceCoordinate = SliceUV(uv, imageIndex);
}
