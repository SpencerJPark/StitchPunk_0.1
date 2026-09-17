using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    // Wave B surface glyphs: Materials, VatBake, Capture.
    public static partial class ToolkitGlyphs
    {
        static partial void RegisterSurfaceShapes()
        {
            RegisterShape(ToolkitGlyphId.Materials, MaterialsDistance);
            RegisterShape(ToolkitGlyphId.VatBake, VatBakeDistance);
            RegisterShape(ToolkitGlyphId.Capture, CaptureDistance);
        }

        // A material preview ball: a ring with one specular highlight inside it. Two earlier
        // shapes were rendered and rejected -- a thin crescent read as a moon, and a half-filled
        // disc read as a prohibition sign because the terminator crossed the ring like a slash.
        private static float MaterialsDistance(Vector2 samplePoint)
        {
            Vector2 sphereCenter = new Vector2(0.5f, 0.5f);
            float sphereRingDistance = Mathf.Abs(CircleDistance(samplePoint, sphereCenter, 0.34f)) - StrokeHalfWidth;
            float highlightDistance = CircleDistance(samplePoint, new Vector2(0.63f, 0.65f), 0.075f);
            return Union(sphereRingDistance, highlightDistance);
        }

        // A sine wave running through a rounded grid square, reaching both edges of the ring
        // to read as "motion baked into a texture" rather than a floating squiggle.
        private static float VatBakeDistance(Vector2 samplePoint)
        {
            float gridSquareDistance = RoundedBoxDistance(
                samplePoint, new Vector2(0.5f, 0.5f), new Vector2(0.34f, 0.34f), 0.06f);
            float gridRingDistance = Mathf.Abs(gridSquareDistance) - StrokeHalfWidth;

            Vector2 waveStart = new Vector2(0.22f, 0.50f);
            Vector2 waveRise = new Vector2(0.36f, 0.68f);
            Vector2 waveMid = new Vector2(0.50f, 0.50f);
            Vector2 waveFall = new Vector2(0.64f, 0.32f);
            Vector2 waveEnd = new Vector2(0.78f, 0.50f);

            float waveChordOneDistance = SegmentDistance(samplePoint, waveStart, waveRise, StrokeHalfWidth);
            float waveChordTwoDistance = SegmentDistance(samplePoint, waveRise, waveMid, StrokeHalfWidth);
            float waveChordThreeDistance = SegmentDistance(samplePoint, waveMid, waveFall, StrokeHalfWidth);
            float waveChordFourDistance = SegmentDistance(samplePoint, waveFall, waveEnd, StrokeHalfWidth);

            float waveDistance = Union(
                Union(waveChordOneDistance, waveChordTwoDistance),
                Union(waveChordThreeDistance, waveChordFourDistance));

            return Union(gridRingDistance, waveDistance);
        }

        // An aperture: an outer ring, a pupil, and three shutter blades at 120 degrees. A single
        // notch was tried first and read as a dial with one hand; the three-fold symmetry is what
        // makes it a lens instead of a gauge.
        private static float CaptureDistance(Vector2 samplePoint)
        {
            Vector2 apertureCenter = new Vector2(0.5f, 0.5f);
            float bladeInnerRadius = 0.14f;
            float bladeOuterRadius = 0.31f;

            float outerRingDistance = Mathf.Abs(CircleDistance(samplePoint, apertureCenter, 0.34f)) - StrokeHalfWidth;
            float pupilDistance = CircleDistance(samplePoint, apertureCenter, bladeInnerRadius);

            float bladesDistance = float.MaxValue;
            for (int bladeIndex = 0; bladeIndex < 3; bladeIndex++)
            {
                float bladeAngleRadians = Mathf.PI * 0.5f + bladeIndex * Mathf.PI * 2f / 3f;
                Vector2 bladeDirection = new Vector2(Mathf.Cos(bladeAngleRadians), Mathf.Sin(bladeAngleRadians));
                float bladeDistance = SegmentDistance(
                    samplePoint,
                    apertureCenter + bladeDirection * bladeInnerRadius,
                    apertureCenter + bladeDirection * bladeOuterRadius,
                    StrokeHalfWidth);
                bladesDistance = Union(bladesDistance, bladeDistance);
            }

            return Union(outerRingDistance, Union(pupilDistance, bladesDistance));
        }
    }
}
