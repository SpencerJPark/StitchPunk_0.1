using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    // Wave B shapes for the three asset-catalog tabs: Texture Packer, Flipbooks, Clip Sets.
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterAssetShapes()
        {
            RegisterShape(ToolkitGlyphId.TexturePacker, TexturePackerDistance);
            RegisterShape(ToolkitGlyphId.Flipbooks, FlipbooksDistance);
            RegisterShape(ToolkitGlyphId.ClipSets, ClipSetsDistance);
        }

        // Outlined rounded square with two filled quadrant blocks on the diagonal — packed channels
        // against empty ones. Blocks are shrunk from the brief's 0.145 half-extent to 0.095 so they
        // clear the ring's stroke band instead of nearly touching it.
        private static float TexturePackerDistance(Vector2 samplePoint)
        {
            float ringDistance = Mathf.Abs(RoundedBoxDistance(
                samplePoint, new Vector2(0.5f, 0.5f), new Vector2(0.34f, 0.34f), 0.06f)) - StrokeHalfWidth;

            float topLeftBlockDistance = BoxDistance(
                samplePoint, new Vector2(0.335f, 0.665f), new Vector2(0.095f, 0.095f));
            float bottomRightBlockDistance = BoxDistance(
                samplePoint, new Vector2(0.665f, 0.335f), new Vector2(0.095f, 0.095f));

            return Union(ringDistance, Union(topLeftBlockDistance, bottomRightBlockDistance));
        }

        // A front frame ring with two sheets behind it reduced to just their top-right corners —
        // full rings would mush together at 16px, corners are what still reads as "stacked".
        private static float FlipbooksDistance(Vector2 samplePoint)
        {
            float frontRingDistance = Mathf.Abs(RoundedBoxDistance(
                samplePoint, new Vector2(0.42f, 0.42f), new Vector2(0.26f, 0.26f), 0.05f)) - StrokeHalfWidth;

            float outerCornerHorizontalDistance = SegmentDistance(
                samplePoint, new Vector2(0.30f, 0.82f), new Vector2(0.80f, 0.82f), StrokeHalfWidth);
            float outerCornerVerticalDistance = SegmentDistance(
                samplePoint, new Vector2(0.80f, 0.82f), new Vector2(0.80f, 0.32f), StrokeHalfWidth);

            float innerCornerHorizontalDistance = SegmentDistance(
                samplePoint, new Vector2(0.23f, 0.75f), new Vector2(0.73f, 0.75f), StrokeHalfWidth);
            float innerCornerVerticalDistance = SegmentDistance(
                samplePoint, new Vector2(0.73f, 0.75f), new Vector2(0.73f, 0.25f), StrokeHalfWidth);

            float outerCornerDistance = Union(outerCornerHorizontalDistance, outerCornerVerticalDistance);
            float innerCornerDistance = Union(innerCornerHorizontalDistance, innerCornerVerticalDistance);

            return Union(frontRingDistance, Union(outerCornerDistance, innerCornerDistance));
        }

        // A film strip: outlined rounded rectangle with four filled sprocket squares down each side.
        private static float ClipSetsDistance(Vector2 samplePoint)
        {
            float outlineDistance = Mathf.Abs(RoundedBoxDistance(
                samplePoint, new Vector2(0.5f, 0.5f), new Vector2(0.30f, 0.34f), 0.05f)) - StrokeHalfWidth;

            Vector2 sprocketHalfExtents = new Vector2(0.045f, 0.045f);

            float leftSprocketOneDistance = BoxDistance(samplePoint, new Vector2(0.36f, 0.29f), sprocketHalfExtents);
            float leftSprocketTwoDistance = BoxDistance(samplePoint, new Vector2(0.36f, 0.43f), sprocketHalfExtents);
            float leftSprocketThreeDistance = BoxDistance(samplePoint, new Vector2(0.36f, 0.57f), sprocketHalfExtents);
            float leftSprocketFourDistance = BoxDistance(samplePoint, new Vector2(0.36f, 0.71f), sprocketHalfExtents);

            float rightSprocketOneDistance = BoxDistance(samplePoint, new Vector2(0.64f, 0.29f), sprocketHalfExtents);
            float rightSprocketTwoDistance = BoxDistance(samplePoint, new Vector2(0.64f, 0.43f), sprocketHalfExtents);
            float rightSprocketThreeDistance = BoxDistance(samplePoint, new Vector2(0.64f, 0.57f), sprocketHalfExtents);
            float rightSprocketFourDistance = BoxDistance(samplePoint, new Vector2(0.64f, 0.71f), sprocketHalfExtents);

            float leftColumnDistance = Union(
                Union(leftSprocketOneDistance, leftSprocketTwoDistance),
                Union(leftSprocketThreeDistance, leftSprocketFourDistance));
            float rightColumnDistance = Union(
                Union(rightSprocketOneDistance, rightSprocketTwoDistance),
                Union(rightSprocketThreeDistance, rightSprocketFourDistance));

            return Union(outlineDistance, Union(leftColumnDistance, rightColumnDistance));
        }
    }
}
