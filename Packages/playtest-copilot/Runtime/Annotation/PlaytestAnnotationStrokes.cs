using System.Collections.Generic;
using UnityEngine;

namespace PlaytestCopilot
{
    /// Pure geometry and pixel work for turning drawn strokes into textures and regions.
    /// No MonoBehaviour, no input: the overlay collects strokes and calls this.
    public static class PlaytestAnnotationStrokes
    {
        private const float MinimumRegionSizePixels = 8f;
        private const float ArrowHeadAngleDegrees = 25f;
        private const float ArrowHeadLengthPixels = 18f;

        public static Rect BoundsOf(PlaytestStroke stroke)
        {
            if (stroke == null || stroke.ScreenPoints == null || stroke.ScreenPoints.Count == 0)
            {
                return Rect.zero;
            }

            float minimumX = float.MaxValue;
            float minimumY = float.MaxValue;
            float maximumX = float.MinValue;
            float maximumY = float.MinValue;

            for (int pointIndex = 0; pointIndex < stroke.ScreenPoints.Count; pointIndex++)
            {
                Vector2 point = stroke.ScreenPoints[pointIndex];
                minimumX = Mathf.Min(minimumX, point.x);
                minimumY = Mathf.Min(minimumY, point.y);
                maximumX = Mathf.Max(maximumX, point.x);
                maximumY = Mathf.Max(maximumY, point.y);
            }

            return Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
        }

        /// One region per non-eraser stroke. A tap collapses to a zero-size rect, so it is
        /// inflated to a minimum size or the region resolver's raycast would never hit it.
        public static List<PlaytestScreenRegion> ToRegions(List<PlaytestStroke> strokes)
        {
            List<PlaytestScreenRegion> regions = new List<PlaytestScreenRegion>();
            if (strokes == null)
            {
                return regions;
            }

            for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
            {
                PlaytestStroke stroke = strokes[strokeIndex];
                if (stroke == null)
                {
                    continue;
                }

                PlaytestScreenRegion region = new PlaytestScreenRegion
                {
                    ScreenRect = InflateToMinimumSize(BoundsOf(stroke), MinimumRegionSizePixels),
                    Tool = stroke.Tool
                };
                regions.Add(region);
            }

            return regions;
        }

        public static Texture2D RenderToTexture(List<PlaytestStroke> strokes, int width, int height)
        {
            int clampedWidth = Mathf.Max(1, width);
            int clampedHeight = Mathf.Max(1, height);
            Texture2D texture = new Texture2D(clampedWidth, clampedHeight, TextureFormat.RGBA32, false);

            Color32[] pixels = new Color32[clampedWidth * clampedHeight];
            Color32 transparent = new Color32(0, 0, 0, 0);
            for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
            {
                pixels[pixelIndex] = transparent;
            }

            if (strokes != null)
            {
                for (int strokeIndex = 0; strokeIndex < strokes.Count; strokeIndex++)
                {
                    DrawStroke(pixels, clampedWidth, clampedHeight, strokes[strokeIndex]);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// Scales by nearest neighbour when the two textures differ in size, because a resized
        /// game view mid-session is normal input, not an error to throw on.
        public static Texture2D Combine(Texture2D frame, Texture2D annotation)
        {
            int outputWidth = frame != null ? frame.width : (annotation != null ? annotation.width : 1);
            int outputHeight = frame != null ? frame.height : (annotation != null ? annotation.height : 1);
            outputWidth = Mathf.Max(1, outputWidth);
            outputHeight = Mathf.Max(1, outputHeight);

            Texture2D result = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
            Color32[] combinedPixels = new Color32[outputWidth * outputHeight];

            Color32[] framePixels = frame != null ? frame.GetPixels32() : null;
            int frameWidth = frame != null ? frame.width : 0;
            int frameHeight = frame != null ? frame.height : 0;

            Color32[] annotationPixels = annotation != null ? annotation.GetPixels32() : null;
            int annotationWidth = annotation != null ? annotation.width : 0;
            int annotationHeight = annotation != null ? annotation.height : 0;

            for (int pixelY = 0; pixelY < outputHeight; pixelY++)
            {
                for (int pixelX = 0; pixelX < outputWidth; pixelX++)
                {
                    int outputIndex = pixelY * outputWidth + pixelX;

                    Color32 basePixel = SampleNearest(framePixels, frameWidth, frameHeight, pixelX, pixelY,
                        outputWidth, outputHeight, new Color32(0, 0, 0, 255));
                    Color32 overlayPixel = SampleNearest(annotationPixels, annotationWidth, annotationHeight,
                        pixelX, pixelY, outputWidth, outputHeight, new Color32(0, 0, 0, 0));

                    combinedPixels[outputIndex] = AlphaBlendOver(overlayPixel, basePixel);
                }
            }

            result.SetPixels32(combinedPixels);
            result.Apply();
            return result;
        }

        private static Color32 SampleNearest(Color32[] sourcePixels, int sourceWidth, int sourceHeight,
            int destinationX, int destinationY, int destinationWidth, int destinationHeight, Color32 fallback)
        {
            if (sourcePixels == null || sourceWidth <= 0 || sourceHeight <= 0)
            {
                return fallback;
            }

            int sampleX = sourceWidth == destinationWidth ? destinationX : (destinationX * sourceWidth) / destinationWidth;
            int sampleY = sourceHeight == destinationHeight ? destinationY : (destinationY * sourceHeight) / destinationHeight;
            sampleX = Mathf.Clamp(sampleX, 0, sourceWidth - 1);
            sampleY = Mathf.Clamp(sampleY, 0, sourceHeight - 1);
            return sourcePixels[sampleY * sourceWidth + sampleX];
        }

        private static Color32 AlphaBlendOver(Color32 sourceOnTop, Color32 destinationBelow)
        {
            float sourceAlpha = sourceOnTop.a / 255f;
            if (sourceAlpha <= 0f)
            {
                return destinationBelow;
            }
            if (sourceAlpha >= 1f)
            {
                return sourceOnTop;
            }

            float inverseSourceAlpha = 1f - sourceAlpha;
            byte blendedRed = (byte)(sourceOnTop.r * sourceAlpha + destinationBelow.r * inverseSourceAlpha);
            byte blendedGreen = (byte)(sourceOnTop.g * sourceAlpha + destinationBelow.g * inverseSourceAlpha);
            byte blendedBlue = (byte)(sourceOnTop.b * sourceAlpha + destinationBelow.b * inverseSourceAlpha);
            byte blendedAlpha = (byte)(sourceOnTop.a + destinationBelow.a * inverseSourceAlpha);
            return new Color32(blendedRed, blendedGreen, blendedBlue, blendedAlpha);
        }

        private static Rect InflateToMinimumSize(Rect rect, float minimumSize)
        {
            float width = rect.width < minimumSize ? minimumSize : rect.width;
            float height = rect.height < minimumSize ? minimumSize : rect.height;
            Vector2 center = rect.center;
            return new Rect(center.x - width / 2f, center.y - height / 2f, width, height);
        }

        private static void DrawStroke(Color32[] pixels, int width, int height, PlaytestStroke stroke)
        {
            if (stroke == null || stroke.ScreenPoints == null || stroke.ScreenPoints.Count == 0)
            {
                return;
            }

            Color32 drawColor = (Color32)stroke.Color;
            float radius = Mathf.Max(0.5f, stroke.ThicknessPixels / 2f);

            switch (stroke.Tool)
            {
                case PlaytestAnnotationTool.Circle:
                    DrawEllipseInBounds(pixels, width, height, BoundsOf(stroke), drawColor, radius);
                    break;
                case PlaytestAnnotationTool.Arrow:
                    DrawArrow(pixels, width, height, stroke.ScreenPoints, drawColor, radius);
                    break;
                case PlaytestAnnotationTool.Pen:
                default:
                    DrawPolyline(pixels, width, height, stroke.ScreenPoints, drawColor, radius);
                    break;
            }
        }

        private static void DrawPolyline(Color32[] pixels, int width, int height, List<Vector2> points,
            Color32 color, float radius)
        {
            if (points.Count == 1)
            {
                StampDisc(pixels, width, height, points[0], radius, color);
                return;
            }

            for (int pointIndex = 0; pointIndex < points.Count - 1; pointIndex++)
            {
                DrawThickLine(pixels, width, height, points[pointIndex], points[pointIndex + 1], color, radius);
            }
        }

        /// Ellipse inscribed in the stroke's bounding box, traced as short thick-line segments
        /// so it shares the same disc-stamping thickness as every other tool.
        private static void DrawEllipseInBounds(Color32[] pixels, int width, int height, Rect bounds,
            Color32 color, float radius)
        {
            if (bounds.width <= 0f && bounds.height <= 0f)
            {
                StampDisc(pixels, width, height, bounds.center, radius, color);
                return;
            }

            float centerX = bounds.center.x;
            float centerY = bounds.center.y;
            float semiAxisX = Mathf.Max(0.5f, bounds.width / 2f);
            float semiAxisY = Mathf.Max(0.5f, bounds.height / 2f);

            int stepCount = Mathf.Max(36, Mathf.CeilToInt((semiAxisX + semiAxisY) * 2f));
            Vector2 previousPoint = new Vector2(centerX + semiAxisX, centerY);
            for (int stepIndex = 1; stepIndex <= stepCount; stepIndex++)
            {
                float angle = (stepIndex / (float)stepCount) * Mathf.PI * 2f;
                Vector2 nextPoint = new Vector2(
                    centerX + semiAxisX * Mathf.Cos(angle),
                    centerY + semiAxisY * Mathf.Sin(angle));
                DrawThickLine(pixels, width, height, previousPoint, nextPoint, color, radius);
                previousPoint = nextPoint;
            }
        }

        private static void DrawArrow(Color32[] pixels, int width, int height, List<Vector2> points,
            Color32 color, float radius)
        {
            Vector2 startPoint = points[0];
            Vector2 endPoint = points[points.Count - 1];

            if (points.Count == 1)
            {
                StampDisc(pixels, width, height, startPoint, radius, color);
                return;
            }

            DrawThickLine(pixels, width, height, startPoint, endPoint, color, radius);

            Vector2 shaftDirection = startPoint - endPoint;
            if (shaftDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }
            shaftDirection.Normalize();

            Vector2 headWingA = RotateVector(shaftDirection, ArrowHeadAngleDegrees * Mathf.Deg2Rad) * ArrowHeadLengthPixels;
            Vector2 headWingB = RotateVector(shaftDirection, -ArrowHeadAngleDegrees * Mathf.Deg2Rad) * ArrowHeadLengthPixels;

            DrawThickLine(pixels, width, height, endPoint, endPoint + headWingA, color, radius);
            DrawThickLine(pixels, width, height, endPoint, endPoint + headWingB, color, radius);
        }

        private static Vector2 RotateVector(Vector2 vector, float angleRadians)
        {
            float cosAngle = Mathf.Cos(angleRadians);
            float sinAngle = Mathf.Sin(angleRadians);
            return new Vector2(
                vector.x * cosAngle - vector.y * sinAngle,
                vector.x * sinAngle + vector.y * cosAngle);
        }

        /// Bresenham between the two points, stamping a filled disc at every plotted pixel
        /// so the line reads at ThicknessPixels wide instead of a hairline.
        private static void DrawThickLine(Color32[] pixels, int width, int height, Vector2 startPoint,
            Vector2 endPoint, Color32 color, float radius)
        {
            int startX = Mathf.RoundToInt(startPoint.x);
            int startY = Mathf.RoundToInt(startPoint.y);
            int endX = Mathf.RoundToInt(endPoint.x);
            int endY = Mathf.RoundToInt(endPoint.y);

            int deltaX = Mathf.Abs(endX - startX);
            int deltaY = -Mathf.Abs(endY - startY);
            int stepX = startX < endX ? 1 : -1;
            int stepY = startY < endY ? 1 : -1;
            int error = deltaX + deltaY;

            int currentX = startX;
            int currentY = startY;

            while (true)
            {
                StampDisc(pixels, width, height, new Vector2(currentX, currentY), radius, color);
                if (currentX == endX && currentY == endY)
                {
                    break;
                }

                int doubledError = 2 * error;
                if (doubledError >= deltaY)
                {
                    error += deltaY;
                    currentX += stepX;
                }
                if (doubledError <= deltaX)
                {
                    error += deltaX;
                    currentY += stepY;
                }
            }
        }

        private static void StampDisc(Color32[] pixels, int width, int height, Vector2 center, float radius, Color32 color)
        {
            int minimumX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius));
            int maximumX = Mathf.Min(width - 1, Mathf.CeilToInt(center.x + radius));
            int minimumY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius));
            int maximumY = Mathf.Min(height - 1, Mathf.CeilToInt(center.y + radius));
            float radiusSquared = radius * radius;

            for (int pixelY = minimumY; pixelY <= maximumY; pixelY++)
            {
                for (int pixelX = minimumX; pixelX <= maximumX; pixelX++)
                {
                    float offsetX = pixelX + 0.5f - center.x;
                    float offsetY = pixelY + 0.5f - center.y;
                    if (offsetX * offsetX + offsetY * offsetY > radiusSquared)
                    {
                        continue;
                    }

                    pixels[pixelY * width + pixelX] = color;
                }
            }
        }
    }
}
