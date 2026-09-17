using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterRigShapes()
        {
            RegisterShape(ToolkitGlyphId.Rigs, RigsDistance);
            RegisterShape(ToolkitGlyphId.Retarget, RetargetDistance);
            RegisterShape(ToolkitGlyphId.ActorProfiles, ActorProfilesDistance);
        }

        // One bone, drawn the way a bone is drawn: a diagonal shaft with a pair of knuckles at
        // each end. The crossed-bones pair was tried first and read as a node graph at 16px --
        // the double knuckle is what makes the silhouette unmistakably a bone.
        private static float RigsDistance(Vector2 samplePoint)
        {
            Vector2 shaftStart = new Vector2(0.31f, 0.31f);
            Vector2 shaftEnd = new Vector2(0.69f, 0.69f);
            float knuckleRadius = 0.105f;
            // Across the shaft, so each end reads as a pair rather than one blob.
            Vector2 knuckleOffset = new Vector2(0.105f, -0.105f);

            float shaftDistance = SegmentDistance(samplePoint, shaftStart, shaftEnd, StrokeHalfWidth);
            float startNearKnuckleDistance = CircleDistance(samplePoint, shaftStart + knuckleOffset, knuckleRadius);
            float startFarKnuckleDistance = CircleDistance(samplePoint, shaftStart - knuckleOffset, knuckleRadius);
            float endNearKnuckleDistance = CircleDistance(samplePoint, shaftEnd + knuckleOffset, knuckleRadius);
            float endFarKnuckleDistance = CircleDistance(samplePoint, shaftEnd - knuckleOffset, knuckleRadius);

            float startKnucklesDistance = Union(startNearKnuckleDistance, startFarKnuckleDistance);
            float endKnucklesDistance = Union(endNearKnuckleDistance, endFarKnuckleDistance);
            return Union(shaftDistance, Union(startKnucklesDistance, endKnucklesDistance));
        }

        // Two armless stick figures (head + spine + leg V each) with a barbed arrow between them,
        // reading as motion carried from the left figure onto the right one.
        private static float RetargetDistance(Vector2 samplePoint)
        {
            float leftFigureDistance = StickFigureDistance(samplePoint, 0.24f);
            float rightFigureDistance = StickFigureDistance(samplePoint, 0.76f);

            Vector2 arrowTailPoint = new Vector2(0.42f, 0.50f);
            Vector2 arrowTipPoint = new Vector2(0.58f, 0.50f);
            Vector2 arrowUpperBarbPoint = new Vector2(0.50f, 0.58f);
            Vector2 arrowLowerBarbPoint = new Vector2(0.50f, 0.42f);
            float arrowShaftDistance = SegmentDistance(samplePoint, arrowTailPoint, arrowTipPoint, StrokeHalfWidth);
            float arrowUpperBarbDistance =
                SegmentDistance(samplePoint, arrowTipPoint, arrowUpperBarbPoint, StrokeHalfWidth);
            float arrowLowerBarbDistance =
                SegmentDistance(samplePoint, arrowTipPoint, arrowLowerBarbPoint, StrokeHalfWidth);
            float arrowDistance = Union(arrowShaftDistance, Union(arrowUpperBarbDistance, arrowLowerBarbDistance));

            return Union(leftFigureDistance, Union(rightFigureDistance, arrowDistance));
        }

        // One armless stick figure centred on figureCenterX: a head circle, a vertical spine and a
        // shallow leg V, spanning roughly y = 0.24 .. 0.80.
        private static float StickFigureDistance(Vector2 samplePoint, float figureCenterX)
        {
            Vector2 headCenter = new Vector2(figureCenterX, 0.73f);
            Vector2 spineTopPoint = new Vector2(figureCenterX, 0.655f);
            Vector2 hipPoint = new Vector2(figureCenterX, 0.40f);
            Vector2 nearLegFootPoint = new Vector2(figureCenterX - 0.08f, 0.24f);
            Vector2 farLegFootPoint = new Vector2(figureCenterX + 0.08f, 0.24f);

            float headDistance = CircleDistance(samplePoint, headCenter, 0.075f);
            float spineDistance = SegmentDistance(samplePoint, spineTopPoint, hipPoint, StrokeHalfWidth);
            float nearLegDistance = SegmentDistance(samplePoint, hipPoint, nearLegFootPoint, StrokeHalfWidth);
            float farLegDistance = SegmentDistance(samplePoint, hipPoint, farLegFootPoint, StrokeHalfWidth);

            return Union(headDistance, Union(spineDistance, Union(nearLegDistance, farLegDistance)));
        }

        // Filled head-and-shoulders bust: a head circle over a rounded shoulder box, the box
        // clipped to its top half so the shoulders taper instead of forming a closed capsule.
        private static float ActorProfilesDistance(Vector2 samplePoint)
        {
            Vector2 headCenter = new Vector2(0.5f, 0.66f);
            float headDistance = CircleDistance(samplePoint, headCenter, 0.17f);

            Vector2 shoulderCenter = new Vector2(0.5f, 0.26f);
            Vector2 shoulderHalfExtents = new Vector2(0.30f, 0.16f);
            float shoulderBoxDistance = RoundedBoxDistance(samplePoint, shoulderCenter, shoulderHalfExtents, 0.14f);
            float shoulderTopHalfPlaneDistance = 0.26f - samplePoint.y;
            float shoulderDistance = Intersect(shoulderBoxDistance, shoulderTopHalfPlaneDistance);

            return Union(headDistance, shoulderDistance);
        }
    }
}
