using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum TerrainRibbonCrossingState { None, Ford, Bridge }

public struct TerrainRoadRibbonInput
{
    public IReadOnlyList<Vector2> centrelineXZ;
    public bool closedLoop;
    public float widthMetres;
    public TerrainRoadRibbonStyle style;
    public Rect tileRectXZ;
    public Func<float, float, float> groundHeightAt;
    public Func<float, float, TerrainRibbonCrossingState> crossingAt;
    public IReadOnlyList<Vector2> junctionPointsXZ;
}

public static class TerrainRoadRibbonMeshBuilder
{
    public const int RingVertexCount = 7;
    public const int MaxVerticesPerTile = 4000;

    private const float ArcMergeToleranceMetres = 1e-3f;
    private const float NormalSampleOffsetMetres = 0.5f;
    private const float JunctionFadeDistanceMetres = 3f;

    private static readonly byte[] WearChannel = { 255, 255, 0, 0, 0, 255, 255 };
    private static readonly byte[] RutChannel = { 0, 0, 255, 0, 255, 0, 0 };
    private static readonly byte[] ShoulderChannel = { 255, 0, 0, 0, 0, 0, 255 };

    private struct RingSample
    {
        public float arc;
        public Vector2 centre;
        public Vector2 tangent;
        public bool forceInclude;
        public bool fromBorderCrossing;
    }

    public static Mesh Build(in TerrainRoadRibbonInput input, Vector3 meshOriginWorld, out int vertexCount, out int triangleCount)
    {
        vertexCount = 0;
        triangleCount = 0;

        List<Vector2> points = BuildCleanPolyline(input.centrelineXZ, input.closedLoop);
        if (points == null)
        {
            return null;
        }

        float[] cumulativeArc = BuildCumulativeArc(points);
        float totalLength = cumulativeArc[cumulativeArc.Length - 1];

        List<RingSample> rings = ResampleByArcLength(points, cumulativeArc, input.style.ringSpacingMetres, input.closedLoop);
        InsertBorderRings(rings, points, cumulativeArc, input.tileRectXZ);
        if (input.closedLoop)
        {
            AppendSeamRing(rings, totalLength);
        }

        List<RingSample> keptRings = new List<RingSample>();
        List<TerrainRibbonCrossingState> keptCrossings = new List<TerrainRibbonCrossingState>();
        List<bool> connectedToPrevious = new List<bool>();
        SelectRingsForTile(input, rings, keptRings, keptCrossings, connectedToPrevious);
        if (keptRings.Count == 0)
        {
            return null;
        }

        int totalVertices = keptRings.Count * RingVertexCount;
        Vector3[] vertices = new Vector3[totalVertices];
        Vector3[] normals = new Vector3[totalVertices];
        Color32[] colors = new Color32[totalVertices];
        Vector2[] primaryUvs = new Vector2[totalVertices];
        Vector2[] secondaryUvs = new Vector2[totalVertices];

        for (int ringIndex = 0; ringIndex < keptRings.Count; ringIndex++)
        {
            FillRingVertices(input, keptRings[ringIndex], keptCrossings[ringIndex], ringIndex, meshOriginWorld,
                vertices, normals, colors, primaryUvs, secondaryUvs);
        }

        List<int> triangles = BuildTriangles(connectedToPrevious);

        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.colors32 = colors;
        mesh.SetUVs(0, primaryUvs);
        mesh.SetUVs(1, secondaryUvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();

        vertexCount = totalVertices;
        triangleCount = triangles.Count / 3;
        return mesh;
    }

    private static List<Vector2> BuildCleanPolyline(IReadOnlyList<Vector2> source, bool closedLoop)
    {
        if (source == null)
        {
            return null;
        }

        List<Vector2> points = new List<Vector2>(source.Count + 1);
        for (int pointIndex = 0; pointIndex < source.Count; pointIndex++)
        {
            if (points.Count == 0 || (source[pointIndex] - points[points.Count - 1]).sqrMagnitude > 1e-8f)
            {
                points.Add(source[pointIndex]);
            }
        }

        if (closedLoop)
        {
            if (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude <= 1e-8f)
            {
                points.RemoveAt(points.Count - 1);
            }
            if (points.Count > 1)
            {
                points.Add(points[0]);
            }
        }

        return points.Count >= 2 ? points : null;
    }

    private static float[] BuildCumulativeArc(List<Vector2> points)
    {
        float[] cumulativeArc = new float[points.Count];
        for (int pointIndex = 1; pointIndex < points.Count; pointIndex++)
        {
            cumulativeArc[pointIndex] = cumulativeArc[pointIndex - 1] + Vector2.Distance(points[pointIndex - 1], points[pointIndex]);
        }
        return cumulativeArc;
    }

    private static RingSample MakeRingSample(List<Vector2> points, float[] cumulativeArc, int segmentIndex, float arc)
    {
        Vector2 start = points[segmentIndex];
        Vector2 end = points[segmentIndex + 1];
        float segmentLength = cumulativeArc[segmentIndex + 1] - cumulativeArc[segmentIndex];
        float fraction = Mathf.Clamp01((arc - cumulativeArc[segmentIndex]) / segmentLength);
        RingSample sample = new RingSample();
        sample.arc = arc;
        sample.centre = Vector2.Lerp(start, end, fraction);
        sample.tangent = (end - start) / segmentLength;
        return sample;
    }

    private static List<RingSample> ResampleByArcLength(List<Vector2> points, float[] cumulativeArc, float spacing, bool closedLoop)
    {
        float totalLength = cumulativeArc[cumulativeArc.Length - 1];
        int segmentCount = points.Count - 1;
        List<RingSample> rings = new List<RingSample>();
        int segmentIndex = 0;
        for (int sampleIndex = 0; ; sampleIndex++)
        {
            float arc = sampleIndex * spacing;
            bool pastEnd = closedLoop ? arc >= totalLength - ArcMergeToleranceMetres : arc > totalLength + ArcMergeToleranceMetres;
            if (pastEnd)
            {
                break;
            }
            while (segmentIndex < segmentCount - 1 && arc >= cumulativeArc[segmentIndex + 1])
            {
                segmentIndex++;
            }
            rings.Add(MakeRingSample(points, cumulativeArc, segmentIndex, arc));
        }
        return rings;
    }

    private static void InsertBorderRings(List<RingSample> rings, List<Vector2> points, float[] cumulativeArc, Rect tileRect)
    {
        float[] edgeValues = { tileRect.xMin, tileRect.xMax, tileRect.yMin, tileRect.yMax };
        for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
        {
            Vector2 start = points[segmentIndex];
            Vector2 end = points[segmentIndex + 1];
            for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
            {
                int axis = edgeIndex / 2;
                float edge = edgeValues[edgeIndex];
                float startAxisValue = start[axis];
                float endAxisValue = end[axis];
                if ((startAxisValue < edge) == (endAxisValue < edge))
                {
                    continue;
                }

                float fraction = (edge - startAxisValue) / (endAxisValue - startAxisValue);
                Vector2 crossingPoint = Vector2.Lerp(start, end, fraction);
                int otherAxis = 1 - axis;
                float otherMin = otherAxis == 0 ? tileRect.xMin : tileRect.yMin;
                float otherMax = otherAxis == 0 ? tileRect.xMax : tileRect.yMax;
                if (crossingPoint[otherAxis] < otherMin - 1e-4f || crossingPoint[otherAxis] > otherMax + 1e-4f)
                {
                    continue;
                }

                float segmentLength = cumulativeArc[segmentIndex + 1] - cumulativeArc[segmentIndex];
                RingSample crossing = MakeRingSample(points, cumulativeArc, segmentIndex, cumulativeArc[segmentIndex] + fraction * segmentLength);
                crossing.centre = crossingPoint;
                crossing.forceInclude = true;
                crossing.fromBorderCrossing = true;
                rings.Add(crossing);
            }
        }

        rings.Sort((first, second) => first.arc.CompareTo(second.arc));

        // A crossing that lands on an existing ring reuses that ring so no duplicate vertices appear.
        List<RingSample> merged = new List<RingSample>(rings.Count);
        for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            RingSample ring = rings[ringIndex];
            int lastIndex = merged.Count - 1;
            if (lastIndex >= 0 && ring.arc - merged[lastIndex].arc < ArcMergeToleranceMetres)
            {
                RingSample kept = merged[lastIndex];
                if (kept.fromBorderCrossing && !ring.fromBorderCrossing)
                {
                    kept = ring;
                }
                kept.forceInclude = kept.forceInclude || ring.forceInclude;
                merged[lastIndex] = kept;
            }
            else
            {
                merged.Add(ring);
            }
        }
        rings.Clear();
        rings.AddRange(merged);
    }

    private static void AppendSeamRing(List<RingSample> rings, float totalLength)
    {
        RingSample seamRing = rings[0];
        seamRing.arc = totalLength;
        seamRing.fromBorderCrossing = false;
        rings.Add(seamRing);
    }

    private static void SelectRingsForTile(in TerrainRoadRibbonInput input, List<RingSample> rings,
        List<RingSample> keptRings, List<TerrainRibbonCrossingState> keptCrossings, List<bool> connectedToPrevious)
    {
        Rect tileRect = input.tileRectXZ;
        bool previousWasKept = false;
        for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            RingSample ring = rings[ringIndex];
            bool insideTile = ring.centre.x >= tileRect.xMin && ring.centre.x < tileRect.xMax
                && ring.centre.y >= tileRect.yMin && ring.centre.y < tileRect.yMax;
            TerrainRibbonCrossingState crossing = input.crossingAt == null
                ? TerrainRibbonCrossingState.None
                : input.crossingAt(ring.centre.x, ring.centre.y);

            bool kept = (insideTile || ring.forceInclude) && crossing != TerrainRibbonCrossingState.Bridge;
            if (kept)
            {
                keptRings.Add(ring);
                keptCrossings.Add(crossing);
                connectedToPrevious.Add(previousWasKept);
            }
            previousWasKept = kept;
        }
    }

    private static void FillRingVertices(in TerrainRoadRibbonInput input, RingSample ring, TerrainRibbonCrossingState crossing,
        int ringIndex, Vector3 meshOriginWorld, Vector3[] vertices, Vector3[] normals, Color32[] colors,
        Vector2[] primaryUvs, Vector2[] secondaryUvs)
    {
        TerrainRoadRibbonStyle style = input.style;
        float halfRibbonWidth = input.widthMetres * 0.5f - style.ribbonInsetMetres;
        float fadeBand = style.fadeBandMetres;
        float rutOffset = style.rutSpacingMetres * 0.5f;
        float[] offsets =
        {
            -(halfRibbonWidth + fadeBand), -halfRibbonWidth, -rutOffset, 0f, rutOffset, halfRibbonWidth, halfRibbonWidth + fadeBand
        };

        Vector2 right = new Vector2(ring.tangent.y, -ring.tangent.x);
        byte opacity = ComputeJunctionOpacity(input.junctionPointsXZ, ring.centre);
        bool inFord = crossing == TerrainRibbonCrossingState.Ford;
        Vector2 secondaryUv = new Vector2(inFord ? 1f : 0f, inFord && style.fadeRutsOnFord ? 0f : 1f);

        for (int crossIndex = 0; crossIndex < RingVertexCount; crossIndex++)
        {
            Vector2 positionXZ = ring.centre + right * offsets[crossIndex];
            float height = input.groundHeightAt(positionXZ.x, positionXZ.y) + TerrainTileCreator.GroundTopHeight + style.verticalLiftMetres;
            int vertexIndex = ringIndex * RingVertexCount + crossIndex;

            vertices[vertexIndex] = new Vector3(positionXZ.x, height, positionXZ.y) - meshOriginWorld;
            normals[vertexIndex] = ComputeGroundNormal(input.groundHeightAt, positionXZ);
            colors[vertexIndex] = new Color32(WearChannel[crossIndex], RutChannel[crossIndex], ShoulderChannel[crossIndex], opacity);
            primaryUvs[vertexIndex] = new Vector2(offsets[crossIndex], ring.arc);
            secondaryUvs[vertexIndex] = secondaryUv;
        }
    }

    private static Vector3 ComputeGroundNormal(Func<float, float, float> groundHeightAt, Vector2 positionXZ)
    {
        float step = NormalSampleOffsetMetres;
        float slopeAlongX = (groundHeightAt(positionXZ.x + step, positionXZ.y) - groundHeightAt(positionXZ.x - step, positionXZ.y)) / (2f * step);
        float slopeAlongZ = (groundHeightAt(positionXZ.x, positionXZ.y + step) - groundHeightAt(positionXZ.x, positionXZ.y - step)) / (2f * step);
        return new Vector3(-slopeAlongX, 1f, -slopeAlongZ).normalized;
    }

    private static byte ComputeJunctionOpacity(IReadOnlyList<Vector2> junctionPoints, Vector2 ringCentre)
    {
        if (junctionPoints == null)
        {
            return 255;
        }

        float nearestDistance = float.MaxValue;
        for (int junctionIndex = 0; junctionIndex < junctionPoints.Count; junctionIndex++)
        {
            nearestDistance = Mathf.Min(nearestDistance, Vector2.Distance(ringCentre, junctionPoints[junctionIndex]));
        }
        float opacity = Mathf.Clamp01(nearestDistance / JunctionFadeDistanceMetres);
        return (byte)Mathf.RoundToInt(opacity * 255f);
    }

    private static List<int> BuildTriangles(List<bool> connectedToPrevious)
    {
        List<int> triangles = new List<int>();
        for (int ringIndex = 1; ringIndex < connectedToPrevious.Count; ringIndex++)
        {
            if (!connectedToPrevious[ringIndex])
            {
                continue;
            }
            for (int quadIndex = 0; quadIndex < RingVertexCount - 1; quadIndex++)
            {
                int cornerA = (ringIndex - 1) * RingVertexCount + quadIndex;
                int cornerB = cornerA + 1;
                int cornerC = cornerA + RingVertexCount;
                int cornerD = cornerA + RingVertexCount + 1;
                triangles.Add(cornerA);
                triangles.Add(cornerC);
                triangles.Add(cornerB);
                triangles.Add(cornerB);
                triangles.Add(cornerC);
                triangles.Add(cornerD);
            }
        }
        return triangles;
    }
}
