using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterMotionShapes()
        {
            RegisterShape(ToolkitGlyphId.ClipEditor, ClipEditorDistance);
            RegisterShape(ToolkitGlyphId.Events, EventsDistance);
            RegisterShape(ToolkitGlyphId.Cutscenes, CutscenesDistance);
        }

        // Timeline baseline with three key diamonds and a playhead — the playhead cap is what
        // separates this from the single-pin Events glyph.
        private static float ClipEditorDistance(Vector2 samplePoint)
        {
            float baselineDistance = SegmentDistance(
                samplePoint, new Vector2(0.14f, 0.40f), new Vector2(0.86f, 0.40f), StrokeHalfWidth);

            float firstKeyDistance = Mathf.Abs(samplePoint.x - 0.30f) + Mathf.Abs(samplePoint.y - 0.40f) - 0.085f;
            float secondKeyDistance = Mathf.Abs(samplePoint.x - 0.50f) + Mathf.Abs(samplePoint.y - 0.40f) - 0.085f;
            float thirdKeyDistance = Mathf.Abs(samplePoint.x - 0.70f) + Mathf.Abs(samplePoint.y - 0.40f) - 0.085f;
            float keysDistance = Union(Union(firstKeyDistance, secondKeyDistance), thirdKeyDistance);

            float playheadStemDistance = SegmentDistance(
                samplePoint, new Vector2(0.50f, 0.40f), new Vector2(0.50f, 0.82f), StrokeHalfWidth);
            float playheadCapDistance = BoxDistance(
                samplePoint, new Vector2(0.50f, 0.82f), new Vector2(0.075f, 0.05f));

            return Union(Union(baselineDistance, keysDistance), Union(playheadStemDistance, playheadCapDistance));
        }

        // One pin standing on a baseline — a single well-weighted mark reads at 16px where three
        // small pins would not.
        private static float EventsDistance(Vector2 samplePoint)
        {
            float baselineDistance = SegmentDistance(
                samplePoint, new Vector2(0.14f, 0.22f), new Vector2(0.86f, 0.22f), StrokeHalfWidth);
            float stemDistance = SegmentDistance(
                samplePoint, new Vector2(0.50f, 0.22f), new Vector2(0.50f, 0.66f), StrokeHalfWidth);
            float headDistance = CircleDistance(samplePoint, new Vector2(0.50f, 0.74f), 0.13f);

            return Union(baselineDistance, Union(stemDistance, headDistance));
        }

        // Clapperboard: an outlined body ring with a filled, hinged top bar; two diagonal stripes
        // are cut from the bar so it reads as a clapper rather than a lid.
        private static float CutscenesDistance(Vector2 samplePoint)
        {
            float bodyFillDistance = RoundedBoxDistance(
                samplePoint, new Vector2(0.5f, 0.38f), new Vector2(0.34f, 0.20f), 0.04f);
            float bodyRingDistance = Mathf.Abs(bodyFillDistance) - StrokeHalfWidth;

            float barFillDistance = RoundedBoxDistance(
                samplePoint, new Vector2(0.5f, 0.68f), new Vector2(0.34f, 0.09f), 0.03f);
            float firstStripeDistance = SegmentDistance(
                samplePoint, new Vector2(0.30f, 0.59f), new Vector2(0.40f, 0.77f), StrokeHalfWidth);
            float secondStripeDistance = SegmentDistance(
                samplePoint, new Vector2(0.58f, 0.59f), new Vector2(0.68f, 0.77f), StrokeHalfWidth);
            float barDistance = Subtract(Subtract(barFillDistance, firstStripeDistance), secondStripeDistance);

            return Union(bodyRingDistance, barDistance);
        }
    }
}
