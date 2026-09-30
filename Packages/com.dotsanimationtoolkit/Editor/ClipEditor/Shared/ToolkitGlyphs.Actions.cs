using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    // Drawn replacements for the two built-in action icons that read as a different family beside the
    // tab glyphs: Unity's filled, shaded trash and its tiny pixel pencil.
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterActionShapes()
        {
            RegisterShape(ToolkitGlyphId.Delete, DeleteDistance);
            RegisterShape(ToolkitGlyphId.Edit, EditDistance);
        }

        // An outlined bin: lid bar with a handle, tapered body, two ribs.
        private static float DeleteDistance(Vector2 samplePoint)
        {
            float lidDistance = SegmentDistance(
                samplePoint, new Vector2(0.20f, 0.72f), new Vector2(0.80f, 0.72f), StrokeHalfWidth);
            float handleDistance = Union(
                SegmentDistance(samplePoint, new Vector2(0.40f, 0.72f), new Vector2(0.40f, 0.83f), StrokeHalfWidth),
                Union(
                    SegmentDistance(samplePoint, new Vector2(0.40f, 0.83f), new Vector2(0.60f, 0.83f), StrokeHalfWidth),
                    SegmentDistance(samplePoint, new Vector2(0.60f, 0.83f), new Vector2(0.60f, 0.72f), StrokeHalfWidth)));

            float leftWallDistance = SegmentDistance(
                samplePoint, new Vector2(0.28f, 0.72f), new Vector2(0.33f, 0.17f), StrokeHalfWidth);
            float floorDistance = SegmentDistance(
                samplePoint, new Vector2(0.33f, 0.17f), new Vector2(0.67f, 0.17f), StrokeHalfWidth);
            float rightWallDistance = SegmentDistance(
                samplePoint, new Vector2(0.67f, 0.17f), new Vector2(0.72f, 0.72f), StrokeHalfWidth);

            float leftRibDistance = SegmentDistance(
                samplePoint, new Vector2(0.44f, 0.58f), new Vector2(0.445f, 0.31f), StrokeHalfWidth);
            float rightRibDistance = SegmentDistance(
                samplePoint, new Vector2(0.56f, 0.58f), new Vector2(0.555f, 0.31f), StrokeHalfWidth);

            float bodyDistance = Union(leftWallDistance, Union(floorDistance, rightWallDistance));
            float ribsDistance = Union(leftRibDistance, rightRibDistance);
            return Union(Union(lidDistance, handleDistance), Union(bodyDistance, ribsDistance));
        }

        // An outlined pencil on the diagonal, tip bottom-left, with a ferrule line near the cap.
        private static float EditDistance(Vector2 samplePoint)
        {
            Vector2 tip = new Vector2(0.18f, 0.18f);
            Vector2 tipBaseLeft = new Vector2(0.235f, 0.375f);
            Vector2 tipBaseRight = new Vector2(0.375f, 0.235f);
            Vector2 capLeft = new Vector2(0.695f, 0.835f);
            Vector2 capRight = new Vector2(0.835f, 0.695f);
            Vector2 ferruleLeft = new Vector2(0.605f, 0.745f);
            Vector2 ferruleRight = new Vector2(0.745f, 0.605f);

            float tipDistance = Union(
                SegmentDistance(samplePoint, tip, tipBaseLeft, StrokeHalfWidth),
                SegmentDistance(samplePoint, tip, tipBaseRight, StrokeHalfWidth));
            float sidesDistance = Union(
                SegmentDistance(samplePoint, tipBaseLeft, capLeft, StrokeHalfWidth),
                SegmentDistance(samplePoint, tipBaseRight, capRight, StrokeHalfWidth));
            float capDistance = SegmentDistance(samplePoint, capLeft, capRight, StrokeHalfWidth);
            float ferruleDistance = SegmentDistance(samplePoint, ferruleLeft, ferruleRight, StrokeHalfWidth);

            return Union(Union(tipDistance, sidesDistance), Union(capDistance, ferruleDistance));
        }
    }
}
