#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class TerraceDressingBuilder
{
    const float DefaultFootOffsetMetres = 1.5f;
    const float RibbonLiftMetres = 0.015f;
    const float RibbonWidthMetres = 1.55f;
    const float ArtCellMetres = 2f;
    const float ArtMidLineFromWallMetres = 0.55f;
    const float FadeRampMetres = 2f;
    const float BiomeBlendMetres = 2f;
    const float SeamToleranceMetres = 0.05f;
    const float CurvatureClampFraction = 0.9f;
    const float SetIndexColourScale = 2f;
    static readonly float[] RowAcrossMetres = { 0f, 0.4f, 0.8f, 1.2f, RibbonWidthMetres };

    // submesh 0 = lip ribbon over the flat top beside the bevel, submesh 1 = foot ribbon over the lower ground beyond the foot bevel.
    // uv0 = (arc / 2, v), uv1.x = chain start phase, colour = (set / 2, next set / 2, biome blend, fade); see Painted/CliffEdgeRibbon.
    public static Mesh BuildLipAndFootMesh(IReadOnlyList<TerraceOutlineChain> chains, Func<Vector2, int> edgeSetIndexAtWorldXZ, Vector2 tileMinCornerWorldXZ)
    {
        if (chains == null || chains.Count == 0)
            return null;

        RibbonBuffers buffers = new RibbonBuffers();
        Vector2 tileCentreWorldXZ = tileMinCornerWorldXZ + new Vector2(TerrainTileData.TileSizeMetres, TerrainTileData.TileSizeMetres) * 0.5f;

        for (int chainIndex = 0; chainIndex < chains.Count; chainIndex++)
        {
            TerraceOutlineChain chain = chains[chainIndex];
            if (chain == null || chain.points == null || chain.points.Length < 2)
                continue;

            int pointCount = chain.points.Length;
            Vector2[] outwardNormals = chain.outwardNormals != null && chain.outwardNormals.Length == pointCount
                ? chain.outwardNormals
                : ComputeOutwardNormals(chain.points);
            float[] arcMetres = ComputeArcMetres(chain);
            float[] leftTurnPerMetre = ComputeLeftTurnPerMetre(chain.points);

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
                {
                    bool fadeAtStart = runStart > 0 || !IsOnTileBorder(chain.points[0], tileCentreWorldXZ, tileMinCornerWorldXZ);
                    bool fadeAtEnd = pointIndex < pointCount || !IsOnTileBorder(chain.points[pointCount - 1], tileCentreWorldXZ, tileMinCornerWorldXZ);
                    RibbonRun run = new RibbonRun(chain, outwardNormals, arcMetres, leftTurnPerMetre, runStart, pointIndex, fadeAtStart, fadeAtEnd);
                    AddRibbon(buffers, run, false, edgeSetIndexAtWorldXZ, tileCentreWorldXZ);
                    AddRibbon(buffers, run, true, edgeSetIndexAtWorldXZ, tileCentreWorldXZ);
                }
                runStart = -1;
            }
        }

        if (buffers.lipIndices.Count == 0 && buffers.footIndices.Count == 0)
            return null;

        Mesh mesh = new Mesh();
        mesh.name = "TerraceLipAndFoot";
        mesh.indexFormat = buffers.positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.SetVertices(buffers.positions);
        mesh.SetNormals(buffers.normals);
        mesh.SetUVs(0, buffers.uvs);
        mesh.SetUVs(1, buffers.phaseUvs);
        mesh.SetColors(buffers.colours);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(buffers.lipIndices, 0);
        mesh.SetTriangles(buffers.footIndices, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    sealed class RibbonBuffers
    {
        public readonly List<Vector3> positions = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<Vector2> phaseUvs = new List<Vector2>();
        public readonly List<Color> colours = new List<Color>();
        public readonly List<int> lipIndices = new List<int>();
        public readonly List<int> footIndices = new List<int>();
    }

    readonly struct RibbonRun
    {
        public readonly TerraceOutlineChain chain;
        public readonly Vector2[] outwardNormals;
        public readonly float[] arcMetres;
        public readonly float[] leftTurnPerMetre;
        public readonly int firstPoint;
        public readonly int endPoint;
        public readonly bool fadeAtStart;
        public readonly bool fadeAtEnd;

        public RibbonRun(TerraceOutlineChain chain, Vector2[] outwardNormals, float[] arcMetres, float[] leftTurnPerMetre,
                         int firstPoint, int endPoint, bool fadeAtStart, bool fadeAtEnd)
        {
            this.chain = chain;
            this.outwardNormals = outwardNormals;
            this.arcMetres = arcMetres;
            this.leftTurnPerMetre = leftTurnPerMetre;
            this.firstPoint = firstPoint;
            this.endPoint = endPoint;
            this.fadeAtStart = fadeAtStart;
            this.fadeAtEnd = fadeAtEnd;
        }
    }

    static void AddRibbon(RibbonBuffers buffers, in RibbonRun run, bool isFoot,
                          Func<Vector2, int> edgeSetIndexAtWorldXZ, Vector2 tileCentreWorldXZ)
    {
        int columnCount = run.endPoint - run.firstPoint;
        int rowCount = RowAcrossMetres.Length;
        int baseIndex = buffers.positions.Count;
        float runStartArc = run.arcMetres[run.firstPoint];
        float runEndArc = run.arcMetres[run.endPoint - 1];

        int fromSet = 0;
        int toSet = 0;
        float blendStartArc = 0f;
        for (int columnIndex = 0; columnIndex < columnCount; columnIndex++)
        {
            int pointIndex = run.firstPoint + columnIndex;
            Vector3 point = run.chain.points[pointIndex];
            Vector2 outward = run.outwardNormals[pointIndex];
            float arc = run.arcMetres[pointIndex];
            float topRound = HasValue(run.chain.topRoundMetres, pointIndex) ? run.chain.topRoundMetres[pointIndex] : 0f;
            float footOffset = GetFootOffset(run.chain, pointIndex);

            // The lip steps inward (to the left of travel), the foot outward (to the right); the offset side decides which curvature squeezes the rows.
            float startMetres = isFoot ? footOffset : topRound;
            float squeezeCurvature = isFoot ? -run.leftTurnPerMetre[pointIndex] : run.leftTurnPerMetre[pointIndex];
            float maxOffsetMetres = squeezeCurvature > 1e-4f ? CurvatureClampFraction / squeezeCurvature : float.MaxValue;
            float sideSign = isFoot ? 1f : -1f;
            float height = isFoot ? GetFootHeight(run.chain, pointIndex) : point.y;

            Vector2 biomeProbeLocal = new Vector2(point.x, point.z) + outward * sideSign * (startMetres + RibbonWidthMetres * 0.5f);
            int biomeSet = edgeSetIndexAtWorldXZ != null ? edgeSetIndexAtWorldXZ(biomeProbeLocal + tileCentreWorldXZ) : 0;
            if (columnIndex == 0)
            {
                fromSet = biomeSet;
                toSet = biomeSet;
            }
            else if (biomeSet != toSet)
            {
                fromSet = toSet;
                toSet = biomeSet;
                blendStartArc = run.arcMetres[pointIndex - 1];
            }
            float blend = Mathf.Clamp01((arc - blendStartArc) / BiomeBlendMetres);
            if (blend >= 1f)
                fromSet = toSet;

            float fade = 1f;
            if (run.fadeAtStart)
                fade = Mathf.Min(fade, Mathf.Clamp01((arc - runStartArc) / FadeRampMetres));
            if (run.fadeAtEnd)
                fade = Mathf.Min(fade, Mathf.Clamp01((runEndArc - arc) / FadeRampMetres));
            Color colour = new Color(fromSet / SetIndexColourScale, toSet / SetIndexColourScale, fromSet == toSet ? 0f : blend, fade);

            for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
            {
                float acrossMetres = RowAcrossMetres[rowIndex];
                float clampedAcross = Mathf.Min(acrossMetres, Mathf.Max(0f, maxOffsetMetres - startMetres));
                float reach = sideSign * (startMetres + clampedAcross);
                buffers.positions.Add(new Vector3(point.x + outward.x * reach, height + RibbonLiftMetres, point.z + outward.y * reach));
                buffers.normals.Add(Vector3.up);
                buffers.uvs.Add(new Vector2(arc / ArtCellMetres, 0.5f + (acrossMetres - ArtMidLineFromWallMetres) / ArtCellMetres));
                buffers.phaseUvs.Add(new Vector2(run.chain.startArcPhase, 0f));
                buffers.colours.Add(colour);
            }
        }

        List<int> indices = isFoot ? buffers.footIndices : buffers.lipIndices;
        for (int columnIndex = 0; columnIndex < columnCount - 1; columnIndex++)
        {
            for (int rowIndex = 0; rowIndex < rowCount - 1; rowIndex++)
            {
                int cornerA = baseIndex + columnIndex * rowCount + rowIndex;
                int cornerB = cornerA + rowCount;
                int cornerC = cornerB + 1;
                int cornerD = cornerA + 1;
                AddUpFacingTriangle(indices, buffers.positions, cornerA, cornerB, cornerC);
                AddUpFacingTriangle(indices, buffers.positions, cornerA, cornerC, cornerD);
            }
        }
    }

    // Arc runs along the whole chain from its start phase so a gap between runs keeps the art phase.
    static float[] ComputeArcMetres(TerraceOutlineChain chain)
    {
        float[] arcMetres = new float[chain.points.Length];
        float running = chain.startArcPhase;
        for (int pointIndex = 0; pointIndex < arcMetres.Length; pointIndex++)
        {
            if (pointIndex > 0)
            {
                Vector3 previous = chain.points[pointIndex - 1];
                Vector3 current = chain.points[pointIndex];
                running += new Vector2(current.x - previous.x, current.z - previous.z).magnitude;
            }
            arcMetres[pointIndex] = running;
        }
        return arcMetres;
    }

    // Positive = the chain bends to the left of travel (centre of curvature on the lip side).
    static float[] ComputeLeftTurnPerMetre(Vector3[] points)
    {
        float[] leftTurnPerMetre = new float[points.Length];
        for (int pointIndex = 1; pointIndex < points.Length - 1; pointIndex++)
        {
            Vector2 incoming = SegmentDirection(points[pointIndex - 1], points[pointIndex]);
            Vector2 outgoing = SegmentDirection(points[pointIndex], points[pointIndex + 1]);
            if (incoming == Vector2.zero || outgoing == Vector2.zero)
                continue;
            float turnRadians = Mathf.Atan2(incoming.x * outgoing.y - incoming.y * outgoing.x, Vector2.Dot(incoming, outgoing));
            float spanMetres = (new Vector2(points[pointIndex + 1].x - points[pointIndex - 1].x, points[pointIndex + 1].z - points[pointIndex - 1].z)).magnitude * 0.5f;
            leftTurnPerMetre[pointIndex] = spanMetres > 1e-4f ? turnRadians / spanMetres : 0f;
        }
        return leftTurnPerMetre;
    }

    // A chain end on the tile border continues in the neighbour tile, so it must not fade out.
    static bool IsOnTileBorder(Vector3 localPoint, Vector2 tileCentreWorldXZ, Vector2 tileMinCornerWorldXZ)
    {
        float worldX = localPoint.x + tileCentreWorldXZ.x;
        float worldZ = localPoint.z + tileCentreWorldXZ.y;
        float tileSize = TerrainTileData.TileSizeMetres;
        return Mathf.Abs(worldX - tileMinCornerWorldXZ.x) <= SeamToleranceMetres
            || Mathf.Abs(worldX - (tileMinCornerWorldXZ.x + tileSize)) <= SeamToleranceMetres
            || Mathf.Abs(worldZ - tileMinCornerWorldXZ.y) <= SeamToleranceMetres
            || Mathf.Abs(worldZ - (tileMinCornerWorldXZ.y + tileSize)) <= SeamToleranceMetres;
    }

    // A zero top radius marks a profile that collapsed beside a ramp opening; no dressing crosses it.
    static bool IsCollapsed(TerraceOutlineChain chain, int pointIndex)
    {
        return chain.topRoundMetres != null && chain.topRoundMetres.Length > pointIndex && chain.topRoundMetres[pointIndex] <= 0f;
    }

    static float GetFootOffset(TerraceOutlineChain chain, int pointIndex)
    {
        return HasValue(chain.footOffsetMetres, pointIndex) ? chain.footOffsetMetres[pointIndex] : DefaultFootOffsetMetres;
    }

    static float GetFootHeight(TerraceOutlineChain chain, int pointIndex)
    {
        return HasValue(chain.footHeights, pointIndex) ? chain.footHeights[pointIndex] : chain.bottomHeights[pointIndex];
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
