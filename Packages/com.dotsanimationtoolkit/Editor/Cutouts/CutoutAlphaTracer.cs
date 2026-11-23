// Copyright (c) 2026 Spencer Park. All rights reserved.
using System.Collections.Generic;
using UnityEngine;

namespace DotsAnimationToolkit.Editor
{
    public struct FrameOverhang
    {
        public int layerIndex;
        public float overhangPixels;
    }

    public static class CutoutAlphaTracer
    {
        public const float DefaultAlphaThreshold = 0.1f;

        const float ParallelEdgeEpsilon = 1e-6f;

        // The arrays are usually compressed (DXT5) and not CPU-readable, so each layer goes through a GPU blit.
        public static List<bool[]> ReadLayerMasks(Texture2DArray array, float alphaThreshold)
        {
            List<bool[]> layerMasks = new List<bool[]>();
            if (array == null)
            {
                return layerMasks;
            }

            for (int layerIndex = 0; layerIndex < array.depth; layerIndex++)
            {
                int capturedLayerIndex = layerIndex;
                layerMasks.Add(ReadMaskThroughBlit(array.width, array.height, alphaThreshold,
                    (RenderTexture target) => Graphics.Blit(array, target, capturedLayerIndex, 0)));
            }
            return layerMasks;
        }

        // Texture2D sources may be compressed or unreadable too, so they take the same GPU blit.
        public static List<bool[]> ReadSourceMasks(Texture source, float alphaThreshold)
        {
            if (source is Texture2DArray array)
            {
                return ReadLayerMasks(array, alphaThreshold);
            }

            List<bool[]> masks = new List<bool[]>();
            if (source is Texture2D image)
            {
                masks.Add(ReadMaskThroughBlit(image.width, image.height, alphaThreshold,
                    (RenderTexture target) => Graphics.Blit(image, target)));
            }
            return masks;
        }

        static bool[] ReadMaskThroughBlit(int width, int height, float alphaThreshold, System.Action<RenderTexture> blitIntoTarget)
        {
            Texture2D readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            try
            {
                blitIntoTarget(target);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                Color32[] pixels = readback.GetPixels32();
                bool[] mask = new bool[pixels.Length];
                for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
                {
                    mask[pixelIndex] = pixels[pixelIndex].a / 255f > alphaThreshold;
                }
                return mask;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(readback);
            }
        }

        public static bool[] ReadAlphaUnion(Texture2DArray array, float alphaThreshold)
        {
            return Union(ReadLayerMasks(array, alphaThreshold));
        }

        public static bool[] Union(IReadOnlyList<bool[]> layerMasks)
        {
            if (layerMasks == null)
            {
                return new bool[0];
            }
            bool[] union = null;
            for (int layerIndex = 0; layerIndex < layerMasks.Count; layerIndex++)
            {
                bool[] layerMask = layerMasks[layerIndex];
                if (layerMask == null)
                {
                    continue;
                }
                if (union == null)
                {
                    union = new bool[layerMask.Length];
                }
                int sharedLength = Mathf.Min(union.Length, layerMask.Length);
                for (int pixelIndex = 0; pixelIndex < sharedLength; pixelIndex++)
                {
                    union[pixelIndex] |= layerMask[pixelIndex];
                }
            }
            return union ?? new bool[0];
        }

        public static List<Vector2> FitOutline(bool[] mask, Vector2Int size, int vertexBudget, float paddingPixels)
        {
            int budget = Mathf.Max(4, vertexBudget);
            List<Vector2> candidates = CollectPaddedRowExtremeCorners(mask, size, paddingPixels);
            if (candidates.Count == 0)
            {
                return CreateFrameQuad(size);
            }

            List<Vector2> hull = BuildConvexHullCounterClockwise(candidates);
            List<Vector2> simplified = SimplifyByEdgeRemoval(hull, budget);
            List<Vector2> clipped = ClipToFrame(simplified, size);
            if (clipped.Count < 3)
            {
                return CreateFrameQuad(size);
            }
            if (clipped.Count > budget)
            {
                // Edge removal extends neighbouring edges, which can push a corner back past the frame.
                clipped = SimplifyByEdgeRemoval(clipped, budget);
                for (int pointIndex = 0; pointIndex < clipped.Count; pointIndex++)
                {
                    clipped[pointIndex] = new Vector2(
                        Mathf.Clamp(clipped[pointIndex].x, 0f, size.x),
                        Mathf.Clamp(clipped[pointIndex].y, 0f, size.y));
                }
            }
            return clipped;
        }

        // A sheet piece needs a concave outline: a convex hull around a tree swallows its neighbours' art, and the
        // mesh samples the shared sheet, so that art would show. Falls back to FitOutline when tracing fails.
        public static List<Vector2> FitTracedOutline(bool[] mask, Vector2Int size, int vertexBudget, float paddingPixels)
        {
            int budget = Mathf.Max(4, vertexBudget);
            int growPixels = Mathf.Max(0, Mathf.CeilToInt(paddingPixels));
            // Closing first fills the gaps between spikes, so a few vertices wrap the silhouette instead of
            // zig-zagging through the leaves; closing only ever adds pixels, so nothing of the art is lost.
            int closingRadius = Mathf.Max(growPixels, Mathf.RoundToInt(Mathf.Max(size.x, size.y) / (float)(budget + 4)));
            bool[] closedMask = ErodeSquare(DilateSquare(mask, size, closingRadius), size, closingRadius - growPixels);
            bool[] grownMask = closedMask;
            List<Vector2> contour = TraceLargestOuterContour(grownMask, size);
            if (contour.Count < 3)
            {
                return FitOutline(mask, size, vertexBudget, paddingPixels);
            }

            List<Vector2> simplified = SimplifyClosedContourToBudget(contour, budget);
            for (int pointIndex = 0; pointIndex < simplified.Count; pointIndex++)
            {
                simplified[pointIndex] = new Vector2(
                    Mathf.Clamp(simplified[pointIndex].x, 0f, size.x),
                    Mathf.Clamp(simplified[pointIndex].y, 0f, size.y));
            }
            if (simplified.Count < 3 || PolygonTriangulator.IsSelfIntersecting(simplified))
            {
                return FitOutline(mask, size, vertexBudget, paddingPixels);
            }
            if (PolygonTriangulator.SignedArea(simplified) < 0f)
            {
                simplified.Reverse();
            }
            return simplified;
        }

        static bool[] ErodeSquare(bool[] mask, Vector2Int size, int distance)
        {
            if (distance <= 0)
            {
                return mask;
            }
            bool[] inverted = new bool[mask.Length];
            for (int pixelIndex = 0; pixelIndex < mask.Length; pixelIndex++)
            {
                inverted[pixelIndex] = !mask[pixelIndex];
            }
            bool[] grownGaps = DilateSquare(inverted, size, distance);
            bool[] eroded = new bool[mask.Length];
            for (int pixelIndex = 0; pixelIndex < mask.Length; pixelIndex++)
            {
                eroded[pixelIndex] = !grownGaps[pixelIndex];
            }
            return eroded;
        }

        static bool[] DilateSquare(bool[] mask, Vector2Int size, int distance)
        {
            if (distance <= 0)
            {
                return mask;
            }
            bool[] horizontal = new bool[mask.Length];
            for (int rowIndex = 0; rowIndex < size.y; rowIndex++)
            {
                int lastFilledColumn = int.MinValue;
                for (int columnIndex = 0; columnIndex < size.x + distance; columnIndex++)
                {
                    if (columnIndex < size.x && mask[rowIndex * size.x + columnIndex])
                    {
                        lastFilledColumn = columnIndex;
                    }
                    int targetColumn = columnIndex - distance;
                    if (targetColumn >= 0 && targetColumn < size.x)
                    {
                        bool filledWithinReach = lastFilledColumn != int.MinValue && columnIndex - lastFilledColumn <= 2 * distance;
                        horizontal[rowIndex * size.x + targetColumn] = filledWithinReach;
                    }
                }
            }

            bool[] dilated = new bool[mask.Length];
            for (int columnIndex = 0; columnIndex < size.x; columnIndex++)
            {
                int lastFilledRow = int.MinValue;
                for (int rowIndex = 0; rowIndex < size.y + distance; rowIndex++)
                {
                    if (rowIndex < size.y && horizontal[rowIndex * size.x + columnIndex])
                    {
                        lastFilledRow = rowIndex;
                    }
                    int targetRow = rowIndex - distance;
                    if (targetRow >= 0 && targetRow < size.y)
                    {
                        bool filledWithinReach = lastFilledRow != int.MinValue && rowIndex - lastFilledRow <= 2 * distance;
                        dilated[targetRow * size.x + columnIndex] = filledWithinReach;
                    }
                }
            }
            return dilated;
        }

        // Walks the pixel-edge boundary with the filled side on the left, so the biggest loop is the outer contour.
        static List<Vector2> TraceLargestOuterContour(bool[] mask, Vector2Int size)
        {
            int cornerStride = size.x + 1;
            Dictionary<int, List<int>> outgoingEdges = new Dictionary<int, List<int>>();
            for (int pixelY = 0; pixelY < size.y; pixelY++)
            {
                for (int pixelX = 0; pixelX < size.x; pixelX++)
                {
                    if (!mask[pixelY * size.x + pixelX])
                    {
                        continue;
                    }
                    int bottomLeft = pixelY * cornerStride + pixelX;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + cornerStride;
                    int topRight = topLeft + 1;
                    if (pixelY == 0 || !mask[(pixelY - 1) * size.x + pixelX])
                    {
                        AddBoundaryEdge(outgoingEdges, bottomLeft, bottomRight);
                    }
                    if (pixelX == size.x - 1 || !mask[pixelY * size.x + pixelX + 1])
                    {
                        AddBoundaryEdge(outgoingEdges, bottomRight, topRight);
                    }
                    if (pixelY == size.y - 1 || !mask[(pixelY + 1) * size.x + pixelX])
                    {
                        AddBoundaryEdge(outgoingEdges, topRight, topLeft);
                    }
                    if (pixelX == 0 || !mask[pixelY * size.x + pixelX - 1])
                    {
                        AddBoundaryEdge(outgoingEdges, topLeft, bottomLeft);
                    }
                }
            }

            List<Vector2> largestLoop = new List<Vector2>();
            float largestArea = 0f;
            List<int> startCorners = new List<int>(outgoingEdges.Keys);
            foreach (int startCorner in startCorners)
            {
                while (outgoingEdges.TryGetValue(startCorner, out List<int> remaining) && remaining.Count > 0)
                {
                    List<Vector2> loop = new List<Vector2>();
                    int currentCorner = startCorner;
                    while (outgoingEdges.TryGetValue(currentCorner, out List<int> exits) && exits.Count > 0)
                    {
                        int nextCorner = exits[exits.Count - 1];
                        exits.RemoveAt(exits.Count - 1);
                        loop.Add(new Vector2(currentCorner % cornerStride, currentCorner / cornerStride));
                        currentCorner = nextCorner;
                        if (currentCorner == startCorner)
                        {
                            break;
                        }
                    }
                    float loopArea = PolygonTriangulator.SignedArea(loop);
                    if (loopArea > largestArea)
                    {
                        largestArea = loopArea;
                        largestLoop = loop;
                    }
                }
            }
            return RemoveCollinearPoints(largestLoop);
        }

        static void AddBoundaryEdge(Dictionary<int, List<int>> outgoingEdges, int fromCorner, int toCorner)
        {
            if (!outgoingEdges.TryGetValue(fromCorner, out List<int> exits))
            {
                exits = new List<int>(1);
                outgoingEdges.Add(fromCorner, exits);
            }
            exits.Add(toCorner);
        }

        static List<Vector2> RemoveCollinearPoints(List<Vector2> loop)
        {
            List<Vector2> corners = new List<Vector2>(loop.Count / 2);
            int count = loop.Count;
            for (int pointIndex = 0; pointIndex < count; pointIndex++)
            {
                Vector2 previous = loop[(pointIndex - 1 + count) % count];
                Vector2 current = loop[pointIndex];
                Vector2 next = loop[(pointIndex + 1) % count];
                float cross = (current.x - previous.x) * (next.y - current.y) - (current.y - previous.y) * (next.x - current.x);
                if (Mathf.Abs(cross) > ParallelEdgeEpsilon)
                {
                    corners.Add(current);
                }
            }
            return corners;
        }

        // Douglas-Peucker on the closed loop, with the tolerance searched until the result fits the budget.
        static List<Vector2> SimplifyClosedContourToBudget(List<Vector2> contour, int budget)
        {
            if (contour.Count <= budget)
            {
                return new List<Vector2>(contour);
            }

            int farthestIndex = 0;
            float farthestDistance = -1f;
            for (int pointIndex = 1; pointIndex < contour.Count; pointIndex++)
            {
                float distance = (contour[pointIndex] - contour[0]).sqrMagnitude;
                if (distance > farthestDistance)
                {
                    farthestDistance = distance;
                    farthestIndex = pointIndex;
                }
            }

            float lowTolerance = 0f;
            float highTolerance = Mathf.Sqrt(farthestDistance);
            List<Vector2> best = SimplifyClosedContour(contour, farthestIndex, highTolerance);
            for (int searchStep = 0; searchStep < 24; searchStep++)
            {
                float middleTolerance = (lowTolerance + highTolerance) * 0.5f;
                List<Vector2> candidate = SimplifyClosedContour(contour, farthestIndex, middleTolerance);
                if (candidate.Count <= budget)
                {
                    best = candidate;
                    highTolerance = middleTolerance;
                }
                else
                {
                    lowTolerance = middleTolerance;
                }
            }
            return best;
        }

        static List<Vector2> SimplifyClosedContour(List<Vector2> contour, int splitIndex, float tolerance)
        {
            bool[] keep = new bool[contour.Count];
            keep[0] = true;
            keep[splitIndex] = true;
            MarkDouglasPeucker(contour, 0, splitIndex, tolerance, keep);
            MarkDouglasPeucker(contour, splitIndex, contour.Count, tolerance, keep);
            List<Vector2> kept = new List<Vector2>();
            for (int pointIndex = 0; pointIndex < contour.Count; pointIndex++)
            {
                if (keep[pointIndex])
                {
                    kept.Add(contour[pointIndex]);
                }
            }
            return kept;
        }

        // endIndex may equal contour.Count, meaning the loop's wrap back to point 0.
        static void MarkDouglasPeucker(List<Vector2> contour, int startIndex, int endIndex, float tolerance, bool[] keep)
        {
            Stack<Vector2Int> spans = new Stack<Vector2Int>();
            spans.Push(new Vector2Int(startIndex, endIndex));
            while (spans.Count > 0)
            {
                Vector2Int span = spans.Pop();
                Vector2 spanStart = contour[span.x];
                Vector2 spanEnd = contour[span.y % contour.Count];
                float worstDistance = -1f;
                int worstIndex = -1;
                for (int pointIndex = span.x + 1; pointIndex < span.y; pointIndex++)
                {
                    float distance = DistanceToSegment(contour[pointIndex], spanStart, spanEnd);
                    if (distance > worstDistance)
                    {
                        worstDistance = distance;
                        worstIndex = pointIndex;
                    }
                }
                if (worstIndex >= 0 && worstDistance > tolerance)
                {
                    keep[worstIndex] = true;
                    spans.Push(new Vector2Int(span.x, worstIndex));
                    spans.Push(new Vector2Int(worstIndex, span.y));
                }
            }
        }

        static float DistanceToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= ParallelEdgeEpsilon)
            {
                return (point - segmentStart).magnitude;
            }
            float along = Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared);
            return (point - (segmentStart + segment * along)).magnitude;
        }

        public static bool ContainsPoint(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            int count = polygon.Count;
            int previousIndex = count - 1;
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                Vector2 current = polygon[vertexIndex];
                Vector2 previous = polygon[previousIndex];
                if ((current.y > point.y) != (previous.y > point.y))
                {
                    float crossingX = (previous.x - current.x) * (point.y - current.y) / (previous.y - current.y) + current.x;
                    if (point.x < crossingX)
                    {
                        inside = !inside;
                    }
                }
                previousIndex = vertexIndex;
            }
            return inside;
        }

        public static List<FrameOverhang> FindOverhangs(IReadOnlyList<bool[]> layerMasks, Vector2Int size, IReadOnlyList<Vector2> outlinePixels)
        {
            List<FrameOverhang> overhangs = new List<FrameOverhang>();
            for (int layerIndex = 0; layerIndex < layerMasks.Count; layerIndex++)
            {
                bool[] layerMask = layerMasks[layerIndex];
                if (layerMask == null)
                {
                    continue;
                }
                float largestDistance = 0f;
                bool anyOutside = false;
                for (int pixelIndex = 0; pixelIndex < layerMask.Length; pixelIndex++)
                {
                    if (!layerMask[pixelIndex])
                    {
                        continue;
                    }
                    Vector2 pixelCentre = new Vector2(pixelIndex % size.x + 0.5f, pixelIndex / size.x + 0.5f);
                    if (ContainsPoint(outlinePixels, pixelCentre))
                    {
                        continue;
                    }
                    anyOutside = true;
                    largestDistance = Mathf.Max(largestDistance, DistanceToBoundary(outlinePixels, pixelCentre));
                }
                if (anyOutside)
                {
                    overhangs.Add(new FrameOverhang { layerIndex = layerIndex, overhangPixels = largestDistance });
                }
            }
            return overhangs;
        }

        static List<Vector2> CreateFrameQuad(Vector2Int size)
        {
            return new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(size.x, 0f),
                new Vector2(size.x, size.y),
                new Vector2(0f, size.y),
            };
        }

        // Row extremes are enough for a hull; interior pixels can never be hull vertices.
        static List<Vector2> CollectPaddedRowExtremeCorners(bool[] mask, Vector2Int size, float paddingPixels)
        {
            List<Vector2> corners = new List<Vector2>();
            if (mask == null || size.x <= 0 || size.y <= 0)
            {
                return corners;
            }
            for (int rowIndex = 0; rowIndex < size.y; rowIndex++)
            {
                int leftmost = -1;
                int rightmost = -1;
                int rowStart = rowIndex * size.x;
                for (int columnIndex = 0; columnIndex < size.x; columnIndex++)
                {
                    if (rowStart + columnIndex < mask.Length && mask[rowStart + columnIndex])
                    {
                        if (leftmost < 0)
                        {
                            leftmost = columnIndex;
                        }
                        rightmost = columnIndex;
                    }
                }
                if (leftmost < 0)
                {
                    continue;
                }
                AddPaddedPixelCorners(corners, leftmost, rowIndex, paddingPixels);
                if (rightmost != leftmost)
                {
                    AddPaddedPixelCorners(corners, rightmost, rowIndex, paddingPixels);
                }
            }
            return corners;
        }

        static void AddPaddedPixelCorners(List<Vector2> corners, int pixelX, int pixelY, float paddingPixels)
        {
            float minX = pixelX - paddingPixels;
            float maxX = pixelX + 1f + paddingPixels;
            float minY = pixelY - paddingPixels;
            float maxY = pixelY + 1f + paddingPixels;
            corners.Add(new Vector2(minX, minY));
            corners.Add(new Vector2(maxX, minY));
            corners.Add(new Vector2(minX, maxY));
            corners.Add(new Vector2(maxX, maxY));
        }

        static float Cross(Vector2 first, Vector2 second)
        {
            return first.x * second.y - first.y * second.x;
        }

        static List<Vector2> BuildConvexHullCounterClockwise(List<Vector2> points)
        {
            List<Vector2> sorted = new List<Vector2>(points);
            sorted.Sort((Vector2 left, Vector2 right) => left.x != right.x ? left.x.CompareTo(right.x) : left.y.CompareTo(right.y));

            List<Vector2> hull = new List<Vector2>();
            for (int pointIndex = 0; pointIndex < sorted.Count; pointIndex++)
            {
                PushHullPoint(hull, sorted[pointIndex], 1);
            }
            int lowerCount = hull.Count + 1;
            for (int pointIndex = sorted.Count - 2; pointIndex >= 0; pointIndex--)
            {
                PushHullPoint(hull, sorted[pointIndex], lowerCount);
            }
            if (hull.Count > 1)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static void PushHullPoint(List<Vector2> hull, Vector2 point, int minimumCountToPop)
        {
            while (hull.Count >= Mathf.Max(2, minimumCountToPop)
                && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 2]) <= 0f)
            {
                hull.RemoveAt(hull.Count - 1);
            }
            hull.Add(point);
        }

        // Each removal replaces an edge with the corner where its neighbouring edges meet, so the polygon only ever grows.
        static List<Vector2> SimplifyByEdgeRemoval(List<Vector2> polygon, int budget)
        {
            List<Vector2> working = new List<Vector2>(polygon);
            while (working.Count > budget)
            {
                int vertexCount = working.Count;
                int bestEdgeIndex = -1;
                float bestAddedArea = float.MaxValue;
                Vector2 bestCorner = Vector2.zero;
                for (int edgeIndex = 0; edgeIndex < vertexCount; edgeIndex++)
                {
                    Vector2 beforeEdge = working[(edgeIndex + vertexCount - 1) % vertexCount];
                    Vector2 edgeStart = working[edgeIndex];
                    Vector2 edgeEnd = working[(edgeIndex + 1) % vertexCount];
                    Vector2 afterEdge = working[(edgeIndex + 2) % vertexCount];
                    Vector2 incomingDirection = edgeStart - beforeEdge;
                    Vector2 outgoingDirection = afterEdge - edgeEnd;
                    float denominator = Cross(incomingDirection, outgoingDirection);
                    if (denominator <= ParallelEdgeEpsilon)
                    {
                        continue;
                    }
                    Vector2 offset = edgeEnd - beforeEdge;
                    float incomingParameter = Cross(offset, outgoingDirection) / denominator;
                    float outgoingParameter = Cross(offset, incomingDirection) / denominator;
                    if (incomingParameter <= 1f || outgoingParameter >= 0f)
                    {
                        continue;
                    }
                    Vector2 corner = beforeEdge + incomingParameter * incomingDirection;
                    float addedArea = Mathf.Abs(Cross(corner - edgeStart, edgeEnd - edgeStart)) * 0.5f;
                    if (addedArea < bestAddedArea)
                    {
                        bestAddedArea = addedArea;
                        bestEdgeIndex = edgeIndex;
                        bestCorner = corner;
                    }
                }

                if (bestEdgeIndex < 0)
                {
                    return CreateBoundingRectangle(working);
                }
                working[bestEdgeIndex] = bestCorner;
                working.RemoveAt((bestEdgeIndex + 1) % vertexCount);
            }
            return working;
        }

        static List<Vector2> CreateBoundingRectangle(List<Vector2> points)
        {
            Vector2 minimum = points[0];
            Vector2 maximum = points[0];
            for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                minimum = Vector2.Min(minimum, points[pointIndex]);
                maximum = Vector2.Max(maximum, points[pointIndex]);
            }
            return new List<Vector2>
            {
                new Vector2(minimum.x, minimum.y),
                new Vector2(maximum.x, minimum.y),
                new Vector2(maximum.x, maximum.y),
                new Vector2(minimum.x, maximum.y),
            };
        }

        static List<Vector2> ClipToFrame(List<Vector2> polygon, Vector2Int size)
        {
            List<Vector2> clipped = ClipAgainstAxisBound(polygon, 0, 0f, true);
            clipped = ClipAgainstAxisBound(clipped, 0, size.x, false);
            clipped = ClipAgainstAxisBound(clipped, 1, 0f, true);
            clipped = ClipAgainstAxisBound(clipped, 1, size.y, false);
            return clipped;
        }

        static float AxisValue(Vector2 point, int axis)
        {
            return axis == 0 ? point.x : point.y;
        }

        static List<Vector2> ClipAgainstAxisBound(List<Vector2> polygon, int axis, float bound, bool keepGreaterOrEqual)
        {
            List<Vector2> output = new List<Vector2>();
            int count = polygon.Count;
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                Vector2 current = polygon[vertexIndex];
                Vector2 previous = polygon[(vertexIndex + count - 1) % count];
                bool currentInside = keepGreaterOrEqual ? AxisValue(current, axis) >= bound : AxisValue(current, axis) <= bound;
                bool previousInside = keepGreaterOrEqual ? AxisValue(previous, axis) >= bound : AxisValue(previous, axis) <= bound;
                if (currentInside != previousInside)
                {
                    float fraction = (bound - AxisValue(previous, axis)) / (AxisValue(current, axis) - AxisValue(previous, axis));
                    output.Add(Vector2.Lerp(previous, current, fraction));
                }
                if (currentInside)
                {
                    output.Add(current);
                }
            }
            return output;
        }

        static float DistanceToBoundary(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            float smallestDistance = float.MaxValue;
            int count = polygon.Count;
            for (int vertexIndex = 0; vertexIndex < count; vertexIndex++)
            {
                Vector2 segmentStart = polygon[vertexIndex];
                Vector2 segmentEnd = polygon[(vertexIndex + 1) % count];
                Vector2 segment = segmentEnd - segmentStart;
                float lengthSquared = segment.sqrMagnitude;
                float along = lengthSquared > 0f ? Mathf.Clamp01(Vector2.Dot(point - segmentStart, segment) / lengthSquared) : 0f;
                float distance = Vector2.Distance(point, segmentStart + along * segment);
                smallestDistance = Mathf.Min(smallestDistance, distance);
            }
            return smallestDistance;
        }
    }
}
