using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterBodyShapes()
        {
            RegisterShape(ToolkitGlyphId.Ragdoll, RagdollDistance);
            RegisterShape(ToolkitGlyphId.Stats, StatsDistance);
            RegisterShape(ToolkitGlyphId.Health, HealthDistance);
        }

        // A slack, limbed figure with jointed knees and dotted joints — deliberately full-length and
        // asymmetric-reading so it never collides with ActorProfiles' compact head-and-shoulders bust.
        private static float RagdollDistance(Vector2 samplePoint)
        {
            const float jointRadius = 0.055f;

            Vector2 headCenter = new Vector2(0.50f, 0.80f);
            Vector2 shoulderPoint = new Vector2(0.50f, 0.66f);
            Vector2 hipPoint = new Vector2(0.50f, 0.42f);
            Vector2 leftKneePoint = new Vector2(0.36f, 0.28f);
            Vector2 rightKneePoint = new Vector2(0.64f, 0.28f);

            float headDistance = CircleDistance(samplePoint, headCenter, 0.10f);
            float spineDistance = SegmentDistance(samplePoint, new Vector2(0.50f, 0.70f), hipPoint, StrokeHalfWidth);
            float leftArmDistance = SegmentDistance(
                samplePoint, shoulderPoint, new Vector2(0.28f, 0.46f), StrokeHalfWidth);
            float rightArmDistance = SegmentDistance(
                samplePoint, shoulderPoint, new Vector2(0.72f, 0.46f), StrokeHalfWidth);
            float leftThighDistance = SegmentDistance(samplePoint, hipPoint, leftKneePoint, StrokeHalfWidth);
            float leftShinDistance = SegmentDistance(
                samplePoint, leftKneePoint, new Vector2(0.34f, 0.14f), StrokeHalfWidth);
            float rightThighDistance = SegmentDistance(samplePoint, hipPoint, rightKneePoint, StrokeHalfWidth);
            float rightShinDistance = SegmentDistance(
                samplePoint, rightKneePoint, new Vector2(0.66f, 0.14f), StrokeHalfWidth);

            float shoulderJointDistance = CircleDistance(samplePoint, shoulderPoint, jointRadius);
            float hipJointDistance = CircleDistance(samplePoint, hipPoint, jointRadius);
            float leftKneeJointDistance = CircleDistance(samplePoint, leftKneePoint, jointRadius);
            float rightKneeJointDistance = CircleDistance(samplePoint, rightKneePoint, jointRadius);

            float limbsDistance = Union(leftArmDistance, rightArmDistance);
            limbsDistance = Union(limbsDistance, Union(leftThighDistance, leftShinDistance));
            limbsDistance = Union(limbsDistance, Union(rightThighDistance, rightShinDistance));

            float jointsDistance = Union(shoulderJointDistance, hipJointDistance);
            jointsDistance = Union(jointsDistance, Union(leftKneeJointDistance, rightKneeJointDistance));

            return Union(Union(headDistance, spineDistance), Union(limbsDistance, jointsDistance));
        }

        // Three filled bars of uneven height on a baseline — the unevenness is the entire signal.
        private static float StatsDistance(Vector2 samplePoint)
        {
            const float baselineY = 0.18f;
            const float barHalfWidth = 0.075f;

            Vector2 baselineStart = new Vector2(0.14f, baselineY);
            Vector2 baselineEnd = new Vector2(0.86f, baselineY);
            float baselineDistance = SegmentDistance(samplePoint, baselineStart, baselineEnd, StrokeHalfWidth);

            float shortBarDistance = FilledBarDistance(samplePoint, 0.28f, baselineY, 0.52f, barHalfWidth);
            float tallBarDistance = FilledBarDistance(samplePoint, 0.50f, baselineY, 0.78f, barHalfWidth);
            float midBarDistance = FilledBarDistance(samplePoint, 0.72f, baselineY, 0.64f, barHalfWidth);

            float barsDistance = Union(shortBarDistance, Union(tallBarDistance, midBarDistance));
            return Union(baselineDistance, barsDistance);
        }

        private static float FilledBarDistance(
            Vector2 samplePoint, float barCenterX, float baselineY, float barTopY, float barHalfWidth)
        {
            Vector2 barCenter = new Vector2(barCenterX, (baselineY + barTopY) * 0.5f);
            Vector2 barHalfExtents = new Vector2(barHalfWidth, (barTopY - baselineY) * 0.5f);
            return BoxDistance(samplePoint, barCenter, barHalfExtents);
        }

        // An outlined clipboard body with a small filled clip and an inset tick — the tick is sized
        // to sit clear of the ring rather than widening the board to make room for it.
        private static float HealthDistance(Vector2 samplePoint)
        {
            Vector2 bodyCenter = new Vector2(0.50f, 0.46f);
            Vector2 bodyHalfExtents = new Vector2(0.26f, 0.32f);
            float bodyRingDistance = Mathf.Abs(
                RoundedBoxDistance(samplePoint, bodyCenter, bodyHalfExtents, 0.05f)) - StrokeHalfWidth;

            Vector2 clipCenter = new Vector2(0.50f, 0.80f);
            Vector2 clipHalfExtents = new Vector2(0.11f, 0.05f);
            float clipDistance = RoundedBoxDistance(samplePoint, clipCenter, clipHalfExtents, 0.02f);

            Vector2 tickStart = new Vector2(0.37f, 0.46f);
            Vector2 tickMiddle = new Vector2(0.46f, 0.36f);
            Vector2 tickEnd = new Vector2(0.64f, 0.58f);
            float tickDownStrokeDistance = SegmentDistance(samplePoint, tickStart, tickMiddle, StrokeHalfWidth);
            float tickUpStrokeDistance = SegmentDistance(samplePoint, tickMiddle, tickEnd, StrokeHalfWidth);
            float tickDistance = Union(tickDownStrokeDistance, tickUpStrokeDistance);

            return Union(bodyRingDistance, Union(clipDistance, tickDistance));
        }
    }
}
