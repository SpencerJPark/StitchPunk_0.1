#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class TerraceOutlineChain
{
    public Vector3[] points;
    public float[] bottomHeights;
    public float startArcPhase;
    // TX-D11 profile per point: outward = toward the lower side; top fillet radius; horizontal distance from the point to the grass foot end.
    public Vector2[] outwardNormals;
    public float[] topRoundMetres;
    public float[] footOffsetMetres;
}

// Pure mesh generation for one terraced tile; no scene or AssetDatabase access.
public static class TerraceTileMeshBuilder
{
    public static List<TerraceOutlineChain> LastBuildOutlineChains = new List<TerraceOutlineChain>();
    public static int LastBuildMultiCrossingSubcells;

    private const int SamplesPerMetre = 4;
    private const int CellsPerSide = 100;
    private const int SamplesPerSide = CellsPerSide * SamplesPerMetre + 1;
    private const float HalfTile = 50f;
    private const float SubcellMetres = 1f / SamplesPerMetre;

    public static Mesh BuildTerracedTileMesh(
        TerrainTileData data,
        TerrainWorldProfile world,
        Func<Vector2Int, TerrainTileData> neighbourLookup,
        IReadOnlyList<TerraceRampFootprint> ramps,
        float verticalOffsetMetres)
    {
        int levelLimit = Mathf.Clamp(world.maxTerraceLevels, 1, TerrainTileData.MaxTerraceLevel);
        int firstThreshold = Mathf.Max(TerrainTileData.MinTerraceLevel + 1, 1 - levelLimit);
        int lastThreshold = levelLimit;
        float[][] signedDistance = new float[lastThreshold - firstThreshold + 1][];
        bool anyLevelIsNonZero = false;
        for (int threshold = firstThreshold; threshold <= lastThreshold; threshold++)
        {
            float[] field = TerraceLevelField.BuildSignedDistance(
                data, threshold, world.terraceCornerRadiusMetres, neighbourLookup, SamplesPerMetre);
            signedDistance[threshold - firstThreshold] = field;
            // Level 0 everywhere means thresholds <= 0 are all inside and thresholds >= 1 all outside.
            if (threshold <= 0 ? AnySampleNegative(field) : AnySampleNonNegative(field))
            {
                anyLevelIsNonZero = true;
            }
        }

        LastBuildOutlineChains = new List<TerraceOutlineChain>();
        LastBuildMultiCrossingSubcells = 0;
        if (!anyLevelIsNonZero)
        {
            return TerrainMeshBuilder.BuildTileMesh(data, 1, neighbourLookup, verticalOffsetMetres);
        }

        TileBuild build = new TileBuild(data, world.terraceLevelHeightMetres, verticalOffsetMetres, signedDistance, firstThreshold, ramps,
            world.terraceTopRoundMetres, world.terraceFootRoundMetres);
        return build.BuildMesh();
    }

    private static bool AnySampleNegative(float[] samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            if (samples[index] < 0f)
            {
                return true;
            }
        }
        return false;
    }

    private static bool AnySampleNonNegative(float[] samples)
    {
        for (int index = 0; index < samples.Length; index++)
        {
            if (samples[index] >= 0f)
            {
                return true;
            }
        }
        return false;
    }

    private struct WallSegment
    {
        public Vector2 start;
        public Vector2 end;
        public int lowerLevel;
        public Vector2 normal;
    }

    private sealed class TileBuild
    {
        private readonly TerrainTileData data;
        private const int FilletSteps = 4;
        private const float FootLiftMetres = 0.03f;
        private readonly float topRoundMetres;
        private readonly float footRoundMetres;
        private readonly float levelHeight;
        private readonly float verticalOffset;
        private readonly float[][] signedDistance;
        private readonly int firstThreshold;
        private readonly Vector2 tileWorldCentre;
        private readonly List<Vector2[]> rampCorners = new List<Vector2[]>();
        private readonly List<Vector4> rampBounds = new List<Vector4>();
        private readonly int[] levels = new int[SamplesPerSide * SamplesPerSide];
        private readonly bool[] cellSubdivided = new bool[CellsPerSide * CellsPerSide];
        private readonly List<Vector3> positions = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> groundTriangles = new List<int>();
        private readonly List<int> wallTriangles = new List<int>();
        private readonly List<WallSegment> wallSegments = new List<WallSegment>();

        public TileBuild(
            TerrainTileData data,
            float levelHeight,
            float verticalOffset,
            float[][] signedDistance,
            int firstThreshold,
            IReadOnlyList<TerraceRampFootprint> ramps,
            float topRoundMetres,
            float footRoundMetres)
        {
            this.topRoundMetres = Mathf.Max(0f, topRoundMetres);
            this.footRoundMetres = Mathf.Max(0f, footRoundMetres);
            this.firstThreshold = firstThreshold;
            this.data = data;
            this.levelHeight = levelHeight;
            this.verticalOffset = verticalOffset;
            this.signedDistance = signedDistance;
            tileWorldCentre = new Vector2(data.coordinate.x, data.coordinate.y) * TerrainTileData.TileSizeMetres;
            CacheRamps(ramps);
        }

        public Mesh BuildMesh()
        {
            ComputeLevels();
            ComputeSubdividedCells();
            for (int cellZ = 0; cellZ < CellsPerSide; cellZ++)
            {
                for (int cellX = 0; cellX < CellsPerSide; cellX++)
                {
                    if (cellSubdivided[cellZ * CellsPerSide + cellX])
                    {
                        EmitSubdividedCell(cellX, cellZ);
                    }
                    else
                    {
                        EmitPlainCell(cellX, cellZ);
                    }
                }
            }
            EmitWallsAndChains();

            Mesh mesh = new Mesh();
            mesh.name = "TerracedTile_" + data.coordinate.x + "_" + data.coordinate.y;
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(groundTriangles, 0);
            mesh.SetTriangles(wallTriangles, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void CacheRamps(IReadOnlyList<TerraceRampFootprint> ramps)
        {
            if (ramps == null)
            {
                return;
            }
            for (int index = 0; index < ramps.Count; index++)
            {
                Vector2[] corners = new Vector2[4];
                TerraceRampFootprint ramp = ramps[index];
                TerraceRampMeshBuilder.GetFootprintCornersXZ(in ramp, corners);
                Vector4 bounds = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector2 localCorner = corners[corner] - tileWorldCentre + new Vector2(HalfTile, HalfTile);
                    bounds.x = Mathf.Min(bounds.x, localCorner.x);
                    bounds.y = Mathf.Min(bounds.y, localCorner.y);
                    bounds.z = Mathf.Max(bounds.z, localCorner.x);
                    bounds.w = Mathf.Max(bounds.w, localCorner.y);
                }
                rampCorners.Add(corners);
                rampBounds.Add(bounds);
            }
        }

        private void ComputeLevels()
        {
            for (int sampleIndex = 0; sampleIndex < levels.Length; sampleIndex++)
            {
                int level = firstThreshold - 1;
                for (int fieldIndex = 0; fieldIndex < signedDistance.Length; fieldIndex++)
                {
                    if (signedDistance[fieldIndex][sampleIndex] < 0f)
                    {
                        break;
                    }
                    level = firstThreshold + fieldIndex;
                }
                levels[sampleIndex] = level;
            }
        }

        private void ComputeSubdividedCells()
        {
            for (int cellZ = 0; cellZ < CellsPerSide; cellZ++)
            {
                for (int cellX = 0; cellX < CellsPerSide; cellX++)
                {
                    cellSubdivided[cellZ * CellsPerSide + cellX] = CellTouchesRamp(cellX, cellZ) || CellHasMixedLevels(cellX, cellZ);
                }
            }
        }

        private bool CellHasMixedLevels(int cellX, int cellZ)
        {
            int firstLevel = levels[cellZ * SamplesPerMetre * SamplesPerSide + cellX * SamplesPerMetre];
            for (int offsetZ = 0; offsetZ <= SamplesPerMetre; offsetZ++)
            {
                for (int offsetX = 0; offsetX <= SamplesPerMetre; offsetX++)
                {
                    int sampleIndex = (cellZ * SamplesPerMetre + offsetZ) * SamplesPerSide + cellX * SamplesPerMetre + offsetX;
                    if (levels[sampleIndex] != firstLevel)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool CellTouchesRamp(int cellX, int cellZ)
        {
            for (int index = 0; index < rampBounds.Count; index++)
            {
                Vector4 bounds = rampBounds[index];
                if (bounds.z >= cellX && bounds.x <= cellX + 1 && bounds.w >= cellZ && bounds.y <= cellZ + 1)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsCellSubdivided(int cellX, int cellZ)
        {
            if (cellX < 0 || cellZ < 0 || cellX >= CellsPerSide || cellZ >= CellsPerSide)
            {
                return false;
            }
            return cellSubdivided[cellZ * CellsPerSide + cellX];
        }

        private void EmitPlainCell(int cellX, int cellZ)
        {
            int level = levels[cellZ * SamplesPerMetre * SamplesPerSide + cellX * SamplesPerMetre];
            List<Vector2> ring = new List<Vector2>();
            AppendEdgePoints(ring, new Vector2(cellX, cellZ), new Vector2(1f, 0f), IsCellSubdivided(cellX, cellZ - 1));
            AppendEdgePoints(ring, new Vector2(cellX + 1, cellZ), new Vector2(0f, 1f), IsCellSubdivided(cellX + 1, cellZ));
            AppendEdgePoints(ring, new Vector2(cellX + 1, cellZ + 1), new Vector2(-1f, 0f), IsCellSubdivided(cellX, cellZ + 1));
            AppendEdgePoints(ring, new Vector2(cellX, cellZ + 1), new Vector2(0f, -1f), IsCellSubdivided(cellX - 1, cellZ));
            if (ring.Count == 4)
            {
                EmitTriangle(ring[0], ring[3], ring[1], level, groundTriangles);
                EmitTriangle(ring[1], ring[3], ring[2], level, groundTriangles);
                return;
            }
            EmitCentroidFan(ring, new Vector2(cellX + 0.5f, cellZ + 0.5f), level);
        }

        private static void AppendEdgePoints(List<Vector2> ring, Vector2 edgeStart, Vector2 direction, bool includeInteriorPoints)
        {
            ring.Add(edgeStart);
            if (!includeInteriorPoints)
            {
                return;
            }
            for (int step = 1; step < SamplesPerMetre; step++)
            {
                ring.Add(edgeStart + direction * (step * SubcellMetres));
            }
        }

        private void EmitSubdividedCell(int cellX, int cellZ)
        {
            for (int subZ = 0; subZ < SamplesPerMetre; subZ++)
            {
                for (int subX = 0; subX < SamplesPerMetre; subX++)
                {
                    EmitSubcell(cellX * SamplesPerMetre + subX, cellZ * SamplesPerMetre + subZ);
                }
            }
        }

        private void EmitSubcell(int sampleX, int sampleZ)
        {
            int[] cornerIndices =
            {
                sampleZ * SamplesPerSide + sampleX,
                sampleZ * SamplesPerSide + sampleX + 1,
                (sampleZ + 1) * SamplesPerSide + sampleX + 1,
                (sampleZ + 1) * SamplesPerSide + sampleX,
            };
            Vector2[] cornerPositions =
            {
                new Vector2(sampleX, sampleZ) * SubcellMetres,
                new Vector2(sampleX + 1, sampleZ) * SubcellMetres,
                new Vector2(sampleX + 1, sampleZ + 1) * SubcellMetres,
                new Vector2(sampleX, sampleZ + 1) * SubcellMetres,
            };
            if (AllPointsInsideRamp(cornerPositions))
            {
                return;
            }

            int lowestLevel = int.MaxValue;
            int highestLevel = int.MinValue;
            for (int corner = 0; corner < 4; corner++)
            {
                lowestLevel = Mathf.Min(lowestLevel, levels[cornerIndices[corner]]);
                highestLevel = Mathf.Max(highestLevel, levels[cornerIndices[corner]]);
            }
            if (lowestLevel == highestLevel)
            {
                EmitTriangle(cornerPositions[0], cornerPositions[3], cornerPositions[1], lowestLevel, groundTriangles);
                EmitTriangle(cornerPositions[1], cornerPositions[3], cornerPositions[2], lowestLevel, groundTriangles);
                return;
            }
            if (highestLevel > lowestLevel + 1)
            {
                LastBuildMultiCrossingSubcells++;
            }
            EmitMarchedSubcell(cornerIndices, cornerPositions, lowestLevel);
        }

        private void EmitMarchedSubcell(int[] cornerIndices, Vector2[] cornerPositions, int lowestLevel)
        {
            float[] field = signedDistance[lowestLevel + 1 - firstThreshold];
            bool[] inside = new bool[4];
            Vector2[] isoPoints = new Vector2[4];
            bool[] crossing = new bool[4];
            int crossingCount = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                inside[corner] = field[cornerIndices[corner]] >= 0f;
            }
            for (int edge = 0; edge < 4; edge++)
            {
                int next = (edge + 1) % 4;
                crossing[edge] = inside[edge] != inside[next];
                if (crossing[edge])
                {
                    isoPoints[edge] = IsoPoint(field, cornerIndices[edge], cornerIndices[next], cornerPositions[edge], cornerPositions[next]);
                    crossingCount++;
                }
            }

            Vector2 wallNormal = DecreasingFieldDirection(field, cornerIndices);
            if (crossingCount == 2)
            {
                EmitSidePolygon(cornerPositions, inside, isoPoints, crossing, false, lowestLevel);
                EmitSidePolygon(cornerPositions, inside, isoPoints, crossing, true, lowestLevel + 1);
                int firstEdge = -1;
                int secondEdge = -1;
                for (int edge = 0; edge < 4; edge++)
                {
                    if (!crossing[edge])
                    {
                        continue;
                    }
                    if (firstEdge < 0)
                    {
                        firstEdge = edge;
                    }
                    else
                    {
                        secondEdge = edge;
                    }
                }
                AddWallSegment(isoPoints[firstEdge], isoPoints[secondEdge], lowestLevel, wallNormal);
                return;
            }

            float centreValue = (field[cornerIndices[0]] + field[cornerIndices[1]] + field[cornerIndices[2]] + field[cornerIndices[3]]) * 0.25f;
            bool centreInside = centreValue >= 0f;
            List<Vector2> centreQuad = new List<Vector2> { isoPoints[0], isoPoints[1], isoPoints[2], isoPoints[3] };
            EmitCentroidFan(centreQuad, (isoPoints[0] + isoPoints[1] + isoPoints[2] + isoPoints[3]) * 0.25f, centreInside ? lowestLevel + 1 : lowestLevel);
            for (int corner = 0; corner < 4; corner++)
            {
                int previousEdge = (corner + 3) % 4;
                int cornerLevel = inside[corner] ? lowestLevel + 1 : lowestLevel;
                EmitTriangle(isoPoints[previousEdge], cornerPositions[corner], isoPoints[corner], cornerLevel, groundTriangles);
                if (inside[corner] != centreInside)
                {
                    AddWallSegment(isoPoints[previousEdge], isoPoints[corner], lowestLevel, WallNormalForCorner(inside[corner], cornerPositions[corner], isoPoints[previousEdge], isoPoints[corner]));
                }
            }
        }

        // Saddle wall faces away from the corner polygon that sits on the higher level.
        private static Vector2 WallNormalForCorner(bool cornerIsUpper, Vector2 cornerPosition, Vector2 isoFrom, Vector2 isoTo)
        {
            Vector2 tangent = isoTo - isoFrom;
            Vector2 perpendicular = new Vector2(tangent.y, -tangent.x).normalized;
            Vector2 towardCorner = cornerPosition - (isoFrom + isoTo) * 0.5f;
            if (Vector2.Dot(perpendicular, towardCorner) < 0f)
            {
                perpendicular = -perpendicular;
            }
            return cornerIsUpper ? -perpendicular : perpendicular;
        }

        private void EmitSidePolygon(Vector2[] cornerPositions, bool[] inside, Vector2[] isoPoints, bool[] crossing, bool side, int level)
        {
            List<Vector2> polygon = new List<Vector2>();
            for (int corner = 0; corner < 4; corner++)
            {
                if (inside[corner] == side)
                {
                    polygon.Add(cornerPositions[corner]);
                }
                if (crossing[corner])
                {
                    polygon.Add(isoPoints[corner]);
                }
            }
            if (polygon.Count == 3)
            {
                EmitTriangle(polygon[0], polygon[1], polygon[2], level, groundTriangles);
            }
            else if (polygon.Count > 3)
            {
                Vector2 centroid = Vector2.zero;
                for (int index = 0; index < polygon.Count; index++)
                {
                    centroid += polygon[index];
                }
                EmitCentroidFan(polygon, centroid / polygon.Count, level);
            }
        }

        // Interpolates from the lower sample index to the higher so both subcells sharing an edge compute identical bits.
        private static Vector2 IsoPoint(float[] field, int indexA, int indexB, Vector2 positionA, Vector2 positionB)
        {
            if (indexA > indexB)
            {
                int swappedIndex = indexA;
                indexA = indexB;
                indexB = swappedIndex;
                Vector2 swappedPosition = positionA;
                positionA = positionB;
                positionB = swappedPosition;
            }
            float valueA = field[indexA];
            float valueB = field[indexB];
            float fraction = valueA / (valueA - valueB);
            return positionA + (positionB - positionA) * fraction;
        }

        private static Vector2 DecreasingFieldDirection(float[] field, int[] cornerIndices)
        {
            float gradientX = field[cornerIndices[1]] + field[cornerIndices[2]] - field[cornerIndices[0]] - field[cornerIndices[3]];
            float gradientZ = field[cornerIndices[3]] + field[cornerIndices[2]] - field[cornerIndices[0]] - field[cornerIndices[1]];
            Vector2 direction = new Vector2(-gradientX, -gradientZ);
            return direction.sqrMagnitude > 1e-12f ? direction.normalized : Vector2.up;
        }

        private void AddWallSegment(Vector2 start, Vector2 end, int lowerLevel, Vector2 normal)
        {
            Vector2 midpointLocal = (start + end) * 0.5f;
            // Endpoints too: a segment straddling the footprint edge has its midpoint outside and floats over the opening.
            if (InsideAnyRamp(midpointLocal) || InsideAnyRamp(start) || InsideAnyRamp(end))
            {
                return;
            }
            // Chains travel with the lower side on the RIGHT; TerraceDressingBuilder relies on this.
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            if (Vector2.Dot(end - start, tangent) < 0f)
            {
                Vector2 swapped = start;
                start = end;
                end = swapped;
            }
            WallSegment segment = new WallSegment();
            segment.start = start;
            segment.end = end;
            segment.lowerLevel = lowerLevel;
            segment.normal = normal;
            wallSegments.Add(segment);
        }

        private bool AllPointsInsideRamp(Vector2[] localPoints)
        {
            if (rampCorners.Count == 0)
            {
                return false;
            }
            for (int index = 0; index < localPoints.Length; index++)
            {
                if (!InsideAnyRamp(localPoints[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private bool InsideAnyRamp(Vector2 localPoint)
        {
            Vector2 worldPoint = localPoint - new Vector2(HalfTile, HalfTile) + tileWorldCentre;
            for (int index = 0; index < rampCorners.Count; index++)
            {
                if (PointInConvexQuad(rampCorners[index], worldPoint))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PointInConvexQuad(Vector2[] corners, Vector2 point)
        {
            bool sawPositive = false;
            bool sawNegative = false;
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 edge = corners[(corner + 1) % 4] - corners[corner];
                Vector2 toPoint = point - corners[corner];
                float cross = edge.x * toPoint.y - edge.y * toPoint.x;
                sawPositive |= cross > 0f;
                sawNegative |= cross < 0f;
            }
            return !(sawPositive && sawNegative);
        }

        private float LayerHeight(Vector2 tileMetres)
        {
            return data.SampleBilinear(tileWorldCentre.x + tileMetres.x - HalfTile, tileWorldCentre.y + tileMetres.y - HalfTile) + verticalOffset;
        }

        private Vector3 GroundNormal(Vector2 tileMetres)
        {
            float worldX = tileWorldCentre.x + tileMetres.x - HalfTile;
            float worldZ = tileWorldCentre.y + tileMetres.y - HalfTile;
            float slopeX = data.SampleBilinear(worldX + 0.5f, worldZ) - data.SampleBilinear(worldX - 0.5f, worldZ);
            float slopeZ = data.SampleBilinear(worldX, worldZ + 0.5f) - data.SampleBilinear(worldX, worldZ - 0.5f);
            return new Vector3(-slopeX, 1f, -slopeZ).normalized;
        }

        private int AddGroundVertex(Vector2 tileMetres, int level)
        {
            positions.Add(new Vector3(tileMetres.x - HalfTile, LayerHeight(tileMetres) + level * levelHeight, tileMetres.y - HalfTile));
            normals.Add(GroundNormal(tileMetres));
            uvs.Add(new Vector2(
                (tileWorldCentre.x + tileMetres.x - HalfTile) / TerrainTileData.TileSizeMetres,
                (tileWorldCentre.y + tileMetres.y - HalfTile) / TerrainTileData.TileSizeMetres));
            return positions.Count - 1;
        }

        private void EmitTriangle(Vector2 first, Vector2 second, Vector2 third, int level, List<int> target)
        {
            float doubledArea = (second.y - first.y) * (third.x - first.x) - (second.x - first.x) * (third.y - first.y);
            if (Mathf.Abs(doubledArea) < 1e-9f)
            {
                return;
            }
            int firstIndex = AddGroundVertex(first, level);
            int secondIndex = AddGroundVertex(second, level);
            int thirdIndex = AddGroundVertex(third, level);
            target.Add(firstIndex);
            target.Add(doubledArea > 0f ? secondIndex : thirdIndex);
            target.Add(doubledArea > 0f ? thirdIndex : secondIndex);
        }

        private void EmitCentroidFan(List<Vector2> ring, Vector2 centre, int level)
        {
            for (int index = 0; index < ring.Count; index++)
            {
                EmitTriangle(centre, ring[index], ring[(index + 1) % ring.Count], level, groundTriangles);
            }
        }

        private void EmitWallsAndChains()
        {
            Dictionary<long, int> startLookup = new Dictionary<long, int>();
            HashSet<long> endKeys = new HashSet<long>();
            for (int index = 0; index < wallSegments.Count; index++)
            {
                startLookup.TryAdd(QuantisedKey(wallSegments[index].start), index);
                endKeys.Add(QuantisedKey(wallSegments[index].end));
            }

            bool[] visited = new bool[wallSegments.Count];
            for (int index = 0; index < wallSegments.Count; index++)
            {
                if (!endKeys.Contains(QuantisedKey(wallSegments[index].start)))
                {
                    EmitChainFrom(index, startLookup, visited);
                }
            }
            for (int index = 0; index < wallSegments.Count; index++)
            {
                if (!visited[index])
                {
                    EmitChainFrom(index, startLookup, visited);
                }
            }
        }

        private void EmitChainFrom(int firstSegment, Dictionary<long, int> startLookup, bool[] visited)
        {
            List<int> chainSegments = new List<int>();
            int current = firstSegment;
            while (current >= 0 && !visited[current])
            {
                visited[current] = true;
                chainSegments.Add(current);
                current = startLookup.TryGetValue(QuantisedKey(wallSegments[current].end), out int next) ? next : -1;
            }

            Vector2 worldStart = wallSegments[chainSegments[0]].start - new Vector2(HalfTile, HalfTile) + tileWorldCentre;
            float arcPhase = HashToArcPhase(worldStart);
            int segmentCount = chainSegments.Count;
            int pointCount = segmentCount + 1;
            Vector2[] chainPoints = new Vector2[pointCount];
            int[] pointLowerLevels = new int[pointCount];
            float[] pointArcs = new float[pointCount];
            float[] tops = new float[pointCount];
            float[] bottoms = new float[pointCount];
            float arcLength = 0f;
            for (int index = 0; index < segmentCount; index++)
            {
                WallSegment segment = wallSegments[chainSegments[index]];
                if (index == 0)
                {
                    chainPoints[0] = segment.start;
                    pointLowerLevels[0] = segment.lowerLevel;
                    pointArcs[0] = arcPhase;
                }
                arcLength += Vector2.Distance(segment.start, segment.end);
                chainPoints[index + 1] = segment.end;
                pointLowerLevels[index + 1] = segment.lowerLevel;
                pointArcs[index + 1] = arcPhase + arcLength;
            }

            List<Vector3> points = new List<Vector3>();
            List<float> bottomHeights = new List<float>();
            for (int index = 0; index < pointCount; index++)
            {
                float groundHeight = LayerHeight(chainPoints[index]);
                tops[index] = groundHeight + (pointLowerLevels[index] + 1) * levelHeight;
                bottoms[index] = groundHeight + pointLowerLevels[index] * levelHeight;
                points.Add(new Vector3(chainPoints[index].x - HalfTile, tops[index], chainPoints[index].y - HalfTile));
                bottomHeights.Add(bottoms[index]);
            }

            bool chainIsClosed = segmentCount > 1 && Vector2.Distance(chainPoints[0], chainPoints[segmentCount]) < 1e-3f;
            Vector2[] outward = new Vector2[pointCount];
            float[] topRounds = new float[pointCount];
            float[] footRounds = new float[pointCount];
            float[] batters = new float[pointCount];
            float[] footOffsets = new float[pointCount];
            float[] footHeights = new float[pointCount];
            for (int index = 0; index < pointCount; index++)
            {
                Vector2 summed = Vector2.zero;
                if (index > 0)
                {
                    summed += wallSegments[chainSegments[index - 1]].normal;
                }
                if (index < segmentCount)
                {
                    summed += wallSegments[chainSegments[index]].normal;
                }
                if (chainIsClosed && (index == 0 || index == segmentCount))
                {
                    summed = wallSegments[chainSegments[0]].normal + wallSegments[chainSegments[segmentCount - 1]].normal;
                }
                outward[index] = summed.sqrMagnitude > 1e-8f ? summed.normalized : wallSegments[chainSegments[Mathf.Min(index, segmentCount - 1)]].normal;

                float wallHeight = tops[index] - bottoms[index];
                float topRound = Mathf.Min(topRoundMetres, 0.3f * wallHeight);
                float footRound = Mathf.Min(footRoundMetres, 0.3f * wallHeight);
                float batter = 0.08f * wallHeight;
                float footOffset = topRound + batter + footRound;
                if (InsideAnyRamp(chainPoints[index] + outward[index] * footOffset))
                {
                    topRound = 0f;
                    footRound = 0f;
                    batter = 0f;
                    footOffset = 0f;
                }
                topRounds[index] = topRound;
                footRounds[index] = footRound;
                batters[index] = batter;
                footOffsets[index] = footOffset;
                footHeights[index] = footOffset > 0f
                    ? LayerHeight(chainPoints[index] + outward[index] * footOffset) + pointLowerLevels[index] * levelHeight + FootLiftMetres
                    : bottoms[index];
            }

            EmitCliffStrips(chainPoints, outward, pointArcs, tops, bottoms, footHeights, topRounds, footRounds, batters);

            TerraceOutlineChain chain = new TerraceOutlineChain();
            chain.points = points.ToArray();
            chain.bottomHeights = bottomHeights.ToArray();
            chain.startArcPhase = arcPhase;
            chain.outwardNormals = outward;
            chain.topRoundMetres = topRounds;
            chain.footOffsetMetres = footOffsets;
            LastBuildOutlineChains.Add(chain);
        }

        // Group 0 = convex top fillet (grass), 1 = steep face (rock), 2 = concave foot fillet (grass).
        private void EmitCliffStrips(
            Vector2[] chainPoints, Vector2[] outward, float[] pointArcs, float[] tops, float[] bottoms,
            float[] footHeights, float[] topRounds, float[] footRounds, float[] batters)
        {
            int pointCount = chainPoints.Length;
            int[] groupRowCounts = { FilletSteps + 1, 2, FilletSteps + 1 };
            for (int group = 0; group < 3; group++)
            {
                int rowCount = groupRowCounts[group];
                int[][] rowVertices = new int[rowCount][];
                for (int row = 0; row < rowCount; row++)
                {
                    rowVertices[row] = new int[pointCount];
                    for (int index = 0; index < pointCount; index++)
                    {
                        float wallHeight = tops[index] - bottoms[index];
                        float topRound = topRounds[index];
                        float footRound = footRounds[index];
                        float batter = batters[index];
                        float faceRun = Mathf.Max(0f, wallHeight - footRound - topRound);
                        float outwardMetres;
                        float downMetres;
                        float normalOut;
                        float normalUp;
                        float profileArc = 0f;
                        float angle = row / (float)FilletSteps * Mathf.PI * 0.5f;
                        if (group == 0)
                        {
                            outwardMetres = topRound * Mathf.Sin(angle);
                            downMetres = topRound * (1f - Mathf.Cos(angle));
                            normalOut = Mathf.Sin(angle);
                            normalUp = Mathf.Cos(angle);
                        }
                        else if (group == 1)
                        {
                            float faceLength = Mathf.Sqrt(batter * batter + faceRun * faceRun);
                            outwardMetres = row == 0 ? topRound : topRound + batter;
                            downMetres = row == 0 ? topRound : wallHeight - footRound;
                            normalOut = faceLength > 1e-6f ? faceRun / faceLength : 1f;
                            normalUp = faceLength > 1e-6f ? batter / faceLength : 0f;
                            profileArc = topRound * Mathf.PI * 0.5f + (row == 0 ? 0f : faceLength);
                        }
                        else
                        {
                            outwardMetres = topRound + batter + footRound * (1f - Mathf.Cos(angle));
                            downMetres = wallHeight - footRound + footRound * Mathf.Sin(angle);
                            normalOut = Mathf.Cos(angle);
                            normalUp = Mathf.Sin(angle);
                        }

                        Vector2 tileMetres = chainPoints[index] + outward[index] * outwardMetres;
                        float height = tops[index] - downMetres;
                        if (group == 2 && footRound > 0f)
                        {
                            height += (footHeights[index] - bottoms[index]) * ((downMetres - (wallHeight - footRound)) / footRound);
                        }
                        Vector3 normal = new Vector3(outward[index].x * normalOut, normalUp, outward[index].y * normalOut);
                        Vector2 uv = group == 1
                            ? new Vector2(profileArc, pointArcs[index])
                            : new Vector2(
                                (tileWorldCentre.x + tileMetres.x - HalfTile) / TerrainTileData.TileSizeMetres,
                                (tileWorldCentre.y + tileMetres.y - HalfTile) / TerrainTileData.TileSizeMetres);
                        positions.Add(new Vector3(tileMetres.x - HalfTile, height, tileMetres.y - HalfTile));
                        normals.Add(normal.normalized);
                        uvs.Add(uv);
                        rowVertices[row][index] = positions.Count - 1;
                    }
                }

                List<int> target = group == 1 ? wallTriangles : groundTriangles;
                float[] roundings = group == 0 ? topRounds : footRounds;
                for (int row = 0; row < rowCount - 1; row++)
                {
                    for (int index = 0; index < pointCount - 1; index++)
                    {
                        if (group != 1 && roundings[index] <= 0f && roundings[index + 1] <= 0f)
                        {
                            continue;
                        }
                        EmitProfileQuad(rowVertices[row][index], rowVertices[row][index + 1], rowVertices[row + 1][index + 1], rowVertices[row + 1][index], target);
                    }
                }
            }
        }

        private void EmitProfileQuad(int first, int second, int third, int fourth, List<int> target)
        {
            Vector3 firstPosition = positions[first];
            Vector3 facing = Vector3.Cross(positions[second] - firstPosition, positions[third] - firstPosition)
                + Vector3.Cross(positions[third] - firstPosition, positions[fourth] - firstPosition);
            Vector3 vertexNormal = normals[first] + normals[second] + normals[third] + normals[fourth];
            bool facesOutward = Vector3.Dot(facing, vertexNormal) > 0f;
            target.Add(first);
            target.Add(facesOutward ? second : third);
            target.Add(facesOutward ? third : second);
            target.Add(first);
            target.Add(facesOutward ? third : fourth);
            target.Add(facesOutward ? fourth : third);
        }

        private static long QuantisedKey(Vector2 tileMetres)
        {
            long quantisedX = Mathf.RoundToInt(tileMetres.x * 10000f);
            long quantisedZ = Mathf.RoundToInt(tileMetres.y * 10000f);
            return (quantisedX << 32) | (quantisedZ & 0xFFFFFFFFL);
        }

        private static float HashToArcPhase(Vector2 worldPoint)
        {
            int quantisedX = Mathf.RoundToInt(worldPoint.x * 100f);
            int quantisedZ = Mathf.RoundToInt(worldPoint.y * 100f);
            uint hash = unchecked((uint)quantisedX * 73856093u) ^ unchecked((uint)quantisedZ * 19349663u);
            return (hash % 10000u) * 0.001f;
        }
    }
}
#endif
