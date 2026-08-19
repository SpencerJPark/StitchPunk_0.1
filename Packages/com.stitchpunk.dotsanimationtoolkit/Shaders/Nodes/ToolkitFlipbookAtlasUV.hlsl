// ToolkitFlipbookAtlasUV — maps a mesh UV into one cell of a sprite sheet.
//
// Wire UV0 in and the result into a Sample Texture 2D node's UV port. Fragment stage.
//
// Atlas Rect is xy = scale, zw = offset — the toolkit's `_AtlasFrame` per-instance property.
// Its identity value is (1, 1, 0, 0), meaning "the whole texture", which is what an actor
// with no atlas track renders. That is also the default here, so an unwired node shows the
// full sheet rather than a collapsed corner of it.

#include "ShaderApiReflectionSupport.hlsl"
#include "Packages/com.stitchpunk.dotsanimationtoolkit/Shaders/Includes/ToolkitFlipbook.hlsl"

///<funchints>
///     <sg:ProviderKey>StitchPunk.ToolkitFlipbookAtlasUV</sg:ProviderKey>
///     <sg:DisplayName>Flipbook Atlas UV</sg:DisplayName>
///     <sg:SearchCategory>StitchPunk/Animation</sg:SearchCategory>
///</funchints>
///<paramhints name = "uv">
///     <sg:DisplayName>UV</sg:DisplayName>
///</paramhints>
///<paramhints name = "atlasRect">
///     <sg:DisplayName>Atlas Rect</sg:DisplayName>
///     <sg:Default>1, 1, 0, 0</sg:Default>
///</paramhints>
UNITY_EXPORT_REFLECTION
void ToolkitFlipbookAtlasUV(float2 uv, float4 atlasRect, out float2 atlasUv)
{
    atlasUv = AtlasFrameUV(uv, atlasRect);
}
