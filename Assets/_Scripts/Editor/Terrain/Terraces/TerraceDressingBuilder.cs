#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerraceDressingBuilder
{
    const float LipInwardMetres = 0.6f;
    const float LipOuterDroopMetres = 0.25f;
    const float SurfaceLiftMetres = 0.02f;
    const float FootWallAlpha = 0.7f;

    // submesh 0 = grass lip at the wall top, submesh 1 = dark foot fringe on the lower ground.
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
        float lipOuterMetres = fringeSizeMetres * 0.5f;

        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            TerraceOutlineChain chain = chains[chainIndex];
            if (chain == null || chain.points == null || chain.points.Length < 2)
                continue;

            int pointCount = chain.points.Length;
            Vector2[] outwardNormals = ComputeOutwardNormals(chain.points);
            float arcMetres = 0f;
            int lipStart = positions.Count;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (pointIndex > 0)
                    arcMetres += HorizontalDistance(chain.points[pointIndex - 1], chain.points[pointIndex]);
                Vector3 point = chain.points[pointIndex];
                Vector2 outward = outwardNormals[pointIndex];
                float uvAlong = (chain.startArcPhase + arcMetres) / fringeSizeMetres;

                positions.Add(new Vector3(point.x - outward.x * LipInwardMetres, point.y + SurfaceLiftMetres, point.z - outward.y * LipInwardMetres));
                positions.Add(new Vector3(point.x + outward.x * lipOuterMetres, point.y - LipOuterDroopMetres, point.z + outward.y * lipOuterMetres));
                AddColumnAttributes(normals, uvs, colors, uvAlong, Color.white, Color.white);
            }
            AddStripIndices(lipIndices, lipStart, pointCount);

            float footArcMetres = 0f;
            int footStart = positions.Count;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                if (pointIndex > 0)
                    footArcMetres += HorizontalDistance(chain.points[pointIndex - 1], chain.points[pointIndex]);
                Vector3 point = chain.points[pointIndex];
                Vector2 outward = outwardNormals[pointIndex];
                float footY = chain.bottomHeights[pointIndex] + SurfaceLiftMetres;
                float uvAlong = (chain.startArcPhase + footArcMetres) / fringeSizeMetres;

                positions.Add(new Vector3(point.x, footY, point.z));
                positions.Add(new Vector3(point.x + outward.x * fringeSizeMetres, footY, point.z + outward.y * fringeSizeMetres));
                AddColumnAttributes(normals, uvs, colors, uvAlong, new Color(0f, 0f, 0f, FootWallAlpha), new Color(0f, 0f, 0f, 0f));
            }
            AddStripIndices(footIndices, footStart, pointCount);
        }

        if (lipIndices.Count == 0)
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

    // Column layout is inner then outer; uv.y is 1 at the inner edge and 0 at the outer edge.
    static void AddColumnAttributes(List<Vector3> normals, List<Vector2> uvs, List<Color> colors, float uvAlong, Color innerColor, Color outerColor)
    {
        normals.Add(Vector3.up);
        normals.Add(Vector3.up);
        uvs.Add(new Vector2(uvAlong, 1f));
        uvs.Add(new Vector2(uvAlong, 0f));
        colors.Add(innerColor);
        colors.Add(outerColor);
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
