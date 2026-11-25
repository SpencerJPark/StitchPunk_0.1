#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerraceDressingBuilder
{
    const float DefaultFootOffsetMetres = 1.5f;
    const float RootInsetMetres = 0.10f;
    const float RootLiftMetres = 0.015f;
    const float ShoulderReachMetres = 0.02f;
    const float TopShoulderDropMetres = 0.01f;
    const float TopTuftReachMin = 0.05f;
    const float TopTuftReachMax = 0.20f;
    const float TopTuftDropMin = 0.03f;
    const float TopTuftDropMax = 0.08f;
    const float FootTuftReachMin = 0.05f;
    const float FootTuftReachMax = 0.18f;
    const float FootTuftRaiseMin = 0.03f;
    const float FootTuftRaiseMax = 0.07f;
    const float TuftReachCapFraction = 0.9f;

    // submesh 0 = top fringe over the cliff bevel, submesh 1 = foot fringe lapping the foot bevel; drawn with the tile's ground material.
    public static Mesh BuildLipAndFootMesh(IReadOnlyList<TerraceOutlineChain> chains, float fringeSizeMetres)
    {
        if (chains == null || chains.Count == 0)
            return null;

        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<int> lipIndices = new List<int>();
        List<int> footIndices = new List<int>();

        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            TerraceOutlineChain chain = chains[chainIndex];
            if (chain == null || chain.points == null || chain.points.Length < 2)
                continue;

            int pointCount = chain.points.Length;
            Vector2[] outwardNormals = chain.outwardNormals != null && chain.outwardNormals.Length == pointCount
                ? chain.outwardNormals
                : ComputeOutwardNormals(chain.points);

            int runStart = -1;
            for (int pointIndex = 0; pointIndex <= pointCount; pointIndex++)
            {
                bool collapsed = pointIndex == pointCount || IsCollapsed(chain, pointIndex);
                if (!collapsed)
                {
                    if (runStart < 0)
                        runStart = pointIndex;
                    continue;
                }
                if (runStart >= 0 && pointIndex - runStart >= 2)
                    AddRun(chain, outwardNormals, runStart, pointIndex, positions, normals, lipIndices, footIndices);
                runStart = -1;
            }
        }

        if (lipIndices.Count == 0 && footIndices.Count == 0)
            return null;

        Mesh mesh = new Mesh();
        mesh.name = "TerraceLipAndFoot";
        mesh.indexFormat = positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(lipIndices, 0);
        mesh.SetTriangles(footIndices, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    // A zero top radius marks a profile that collapsed beside a ramp opening; no dressing crosses it.
    static bool IsCollapsed(TerraceOutlineChain chain, int pointIndex)
    {
        return chain.topRoundMetres != null && chain.topRoundMetres.Length > pointIndex && chain.topRoundMetres[pointIndex] <= 0f;
    }

    static void AddRun(TerraceOutlineChain chain, Vector2[] outwardNormals, int firstPoint, int endPoint,
                       List<Vector3> positions, List<Vector3> normals, List<int> lipIndices, List<int> footIndices)
    {
        for (int pointIndex = firstPoint; pointIndex < endPoint - 1; pointIndex++)
        {
            int nextIndex = pointIndex + 1;
            Vector3 pointA = chain.points[pointIndex];
            Vector3 pointB = chain.points[nextIndex];
            Vector2 outwardA = outwardNormals[pointIndex];
            Vector2 outwardB = outwardNormals[nextIndex];
            Vector2 outwardMid = outwardA + outwardB;
            outwardMid = outwardMid.sqrMagnitude < 1e-8f ? outwardA : outwardMid.normalized;
            Vector3 midPoint = (pointA + pointB) * 0.5f;
            float hash = HashZeroToOne(midPoint.x, midPoint.z);

            float topRoundA = HasValue(chain.topRoundMetres, pointIndex) ? chain.topRoundMetres[pointIndex] : 0f;
            float topRoundB = HasValue(chain.topRoundMetres, nextIndex) ? chain.topRoundMetres[nextIndex] : 0f;
            float topRoundMid = (topRoundA + topRoundB) * 0.5f;

            float topReach = Mathf.Lerp(TopTuftReachMin, TopTuftReachMax, hash);
            if (topRoundMid > 0f)
                topReach = Mathf.Min(topReach, TuftReachCapFraction * topRoundMid);
            float topDrop = Mathf.Lerp(TopTuftDropMin, TopTuftDropMax, HashZeroToOne(midPoint.z + 17.3f, midPoint.x - 5.1f));

            AddFringeSegment(positions, normals, lipIndices,
                OffsetPoint(pointA, outwardA, -RootInsetMetres, RootLiftMetres),
                OffsetPoint(pointB, outwardB, -RootInsetMetres, RootLiftMetres),
                OffsetPoint(pointA, outwardA, ShoulderReachMetres, -TopShoulderDropMetres),
                OffsetPoint(pointB, outwardB, ShoulderReachMetres, -TopShoulderDropMetres),
                OffsetPoint(midPoint, outwardMid, topReach, -topDrop));

            float footOffsetA = GetFootOffset(chain, pointIndex);
            float footOffsetB = GetFootOffset(chain, nextIndex);
            float footHeightA = GetFootHeight(chain, pointIndex);
            float footHeightB = GetFootHeight(chain, nextIndex);
            float footOffsetMid = (footOffsetA + footOffsetB) * 0.5f;
            float footHeightMid = (footHeightA + footHeightB) * 0.5f;

            float footReach = Mathf.Lerp(FootTuftReachMin, FootTuftReachMax, HashZeroToOne(midPoint.x + 3.7f, midPoint.z + 9.2f));
            if (topRoundMid > 0f && footOffsetMid > topRoundMid)
                footReach = Mathf.Min(footReach, TuftReachCapFraction * (footOffsetMid - topRoundMid));
            float footRaise = Mathf.Lerp(FootTuftRaiseMin, FootTuftRaiseMax, HashZeroToOne(midPoint.z - 11.9f, midPoint.x + 2.4f));

            AddFringeSegment(positions, normals, footIndices,
                FootPoint(pointA, outwardA, footOffsetA + RootInsetMetres, footHeightA - RootLiftMetres),
                FootPoint(pointB, outwardB, footOffsetB + RootInsetMetres, footHeightB - RootLiftMetres),
                FootPoint(pointA, outwardA, footOffsetA + ShoulderReachMetres, footHeightA),
                FootPoint(pointB, outwardB, footOffsetB + ShoulderReachMetres, footHeightB),
                FootPoint(midPoint, outwardMid, footOffsetMid - footReach, footHeightMid + footRaise));
        }
    }

    static float GetFootOffset(TerraceOutlineChain chain, int pointIndex)
    {
        return HasValue(chain.footOffsetMetres, pointIndex) ? chain.footOffsetMetres[pointIndex] : DefaultFootOffsetMetres;
    }

    static float GetFootHeight(TerraceOutlineChain chain, int pointIndex)
    {
        return HasValue(chain.footHeights, pointIndex) ? chain.footHeights[pointIndex] : chain.bottomHeights[pointIndex];
    }

    static Vector3 OffsetPoint(Vector3 point, Vector2 outward, float reachMetres, float heightDelta)
    {
        return new Vector3(point.x + outward.x * reachMetres, point.y + heightDelta, point.z + outward.y * reachMetres);
    }

    static Vector3 FootPoint(Vector3 point, Vector2 outward, float reachMetres, float absoluteHeight)
    {
        return new Vector3(point.x + outward.x * reachMetres, absoluteHeight, point.z + outward.y * reachMetres);
    }

    // Deterministic per-position noise so rebuilds do not reshuffle the ragged edge.
    static float HashZeroToOne(float worldX, float worldZ)
    {
        float raw = Mathf.Sin(worldX * 12.9898f + worldZ * 78.233f) * 43758.5453f;
        return raw - Mathf.Floor(raw);
    }

    // Root quad plus one tuft triangle; each triangle is flipped if needed so it faces up.
    static void AddFringeSegment(List<Vector3> positions, List<Vector3> normals, List<int> indices,
                                 Vector3 rootA, Vector3 rootB, Vector3 shoulderA, Vector3 shoulderB, Vector3 tip)
    {
        int baseIndex = positions.Count;
        positions.Add(rootA);
        positions.Add(rootB);
        positions.Add(shoulderB);
        positions.Add(shoulderA);
        positions.Add(tip);
        for (int normalIndex = 0; normalIndex < 5; normalIndex++)
            normals.Add(Vector3.up);

        AddUpFacingTriangle(indices, positions, baseIndex, baseIndex + 1, baseIndex + 2);
        AddUpFacingTriangle(indices, positions, baseIndex, baseIndex + 2, baseIndex + 3);
        AddUpFacingTriangle(indices, positions, baseIndex + 3, baseIndex + 2, baseIndex + 4);
    }

    static void AddUpFacingTriangle(List<int> indices, List<Vector3> positions, int first, int second, int third)
    {
        Vector3 faceNormal = Vector3.Cross(positions[second] - positions[first], positions[third] - positions[first]);
        if (faceNormal.y < 0f)
        {
            int swapped = second;
            second = third;
            third = swapped;
        }
        indices.Add(first);
        indices.Add(second);
        indices.Add(third);
    }

    static bool HasValue(float[] values, int pointIndex)
    {
        return values != null && values.Length > pointIndex;
    }

    // Assumption: the lower side (the side the wall faces) is to the RIGHT of travel direction.
    static Vector2[] ComputeOutwardNormals(Vector3[] points)
    {
        int pointCount = points.Length;
        Vector2[] outwardNormals = new Vector2[pointCount];
        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            Vector2 tangent = Vector2.zero;
            if (pointIndex > 0)
                tangent += SegmentDirection(points[pointIndex - 1], points[pointIndex]);
            if (pointIndex < pointCount - 1)
                tangent += SegmentDirection(points[pointIndex], points[pointIndex + 1]);
            if (tangent.sqrMagnitude < 1e-8f)
                tangent = Vector2.right;
            tangent.Normalize();
            outwardNormals[pointIndex] = new Vector2(tangent.y, -tangent.x);
        }
        return outwardNormals;
    }

    static Vector2 SegmentDirection(Vector3 from, Vector3 to)
    {
        Vector2 direction = new Vector2(to.x - from.x, to.z - from.z);
        return direction.sqrMagnitude < 1e-10f ? Vector2.zero : direction.normalized;
    }
}
#endif
