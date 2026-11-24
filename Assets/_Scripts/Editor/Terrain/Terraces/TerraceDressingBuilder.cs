#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerraceDressingBuilder
{
    const float DefaultTopRoundMetres = 0.75f;
    const float DefaultFootOffsetMetres = 1.5f;
    const float LipInwardMetres = 0.3f;
    const float LipLiftMetres = 0.03f;
    const float FootTuftBottomDropMetres = 0.05f;
    const float FootTuftHeightMetres = 0.35f;
    const float FootTuftLeanMetres = 0.15f;
    const float FootTuftOutwardMetres = 0.05f;

    // submesh 0 = grass curtain over the top fillet, submesh 1 = grass tufts at the foot of the wall.
    public static Mesh BuildLipAndFootMesh(IReadOnlyList<TerraceOutlineChain> chains, float fringeSizeMetres)
    {
        if (chains == null || chains.Count == 0)
            return null;

        List<Vector3> positions = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Color> colors = new List<Color>();
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

            float[] arcMetres = new float[pointCount];
            for (int pointIndex = 1; pointIndex < pointCount; pointIndex++)
                arcMetres[pointIndex] = arcMetres[pointIndex - 1] + HorizontalDistance(chain.points[pointIndex - 1], chain.points[pointIndex]);

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
                    AddRun(chain, outwardNormals, arcMetres, runStart, pointIndex, fringeSizeMetres,
                           positions, normals, uvs, colors, lipIndices, footIndices);
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
        mesh.SetUVs(0, uvs);
        mesh.SetColors(colors);
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

    static void AddRun(TerraceOutlineChain chain, Vector2[] outwardNormals, float[] arcMetres, int firstPoint, int endPoint,
                       float fringeSizeMetres, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs,
                       List<Color> colors, List<int> lipIndices, List<int> footIndices)
    {
        int columnCount = endPoint - firstPoint;

        int lipStart = positions.Count;
        for (int pointIndex = firstPoint; pointIndex < endPoint; pointIndex++)
        {
            Vector3 point = chain.points[pointIndex];
            Vector2 outward = outwardNormals[pointIndex];
            float topRound = HasValue(chain.topRoundMetres, pointIndex) ? chain.topRoundMetres[pointIndex] : DefaultTopRoundMetres;
            float uvAlong = (chain.startArcPhase + arcMetres[pointIndex]) / fringeSizeMetres;
            float outerReach = topRound * 0.95f + 0.1f;

            positions.Add(new Vector3(point.x - outward.x * LipInwardMetres, point.y + LipLiftMetres, point.z - outward.y * LipInwardMetres));
            positions.Add(new Vector3(point.x + outward.x * outerReach, point.y - topRound * 0.6f - 0.35f, point.z + outward.y * outerReach));
            AddColumnAttributes(normals, uvs, colors, uvAlong, 1f, 0f);
        }
        AddStripIndices(lipIndices, lipStart, columnCount);

        int footStart = positions.Count;
        for (int pointIndex = firstPoint; pointIndex < endPoint; pointIndex++)
        {
            Vector3 point = chain.points[pointIndex];
            Vector2 outward = outwardNormals[pointIndex];
            float footOffset = HasValue(chain.footOffsetMetres, pointIndex) ? chain.footOffsetMetres[pointIndex] : DefaultFootOffsetMetres;
            float uvAlong = (chain.startArcPhase + arcMetres[pointIndex]) / fringeSizeMetres;
            float rootReach = footOffset + FootTuftOutwardMetres;
            float tipReach = rootReach + FootTuftLeanMetres;
            float bottomHeight = HasValue(chain.footHeights, pointIndex) ? chain.footHeights[pointIndex] : chain.bottomHeights[pointIndex];

            positions.Add(new Vector3(point.x + outward.x * rootReach, bottomHeight - FootTuftBottomDropMetres, point.z + outward.y * rootReach));
            positions.Add(new Vector3(point.x + outward.x * tipReach, bottomHeight + FootTuftHeightMetres, point.z + outward.y * tipReach));
            AddColumnAttributes(normals, uvs, colors, uvAlong, 1f, 0f);
        }
        AddStripIndices(footIndices, footStart, columnCount);
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

    static float HorizontalDistance(Vector3 from, Vector3 to)
    {
        return new Vector2(to.x - from.x, to.z - from.z).magnitude;
    }

    // Column layout is first then second vertex; uv.y runs firstV to secondV, vertex colours stay white.
    static void AddColumnAttributes(List<Vector3> normals, List<Vector2> uvs, List<Color> colors, float uvAlong, float firstV, float secondV)
    {
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(uvAlong, firstV));
        uvs.Add(new Vector2(uvAlong, secondV));
        colors.Add(Color.white);
        colors.Add(Color.white);
    }

    // With outward on the right of travel this winding faces up in Unity's clockwise-front convention.
    static void AddStripIndices(List<int> indices, int firstVertex, int columnCount)
    {
        for (int columnIndex = 0; columnIndex < columnCount - 1; columnIndex++)
        {
            int innerCurrent = firstVertex + columnIndex * 2;
            int outerCurrent = innerCurrent + 1;
            int innerNext = innerCurrent + 2;
            int outerNext = innerCurrent + 3;
            indices.Add(innerCurrent);
            indices.Add(innerNext);
            indices.Add(outerNext);
            indices.Add(innerCurrent);
            indices.Add(outerNext);
            indices.Add(outerCurrent);
        }
    }
}
#endif
