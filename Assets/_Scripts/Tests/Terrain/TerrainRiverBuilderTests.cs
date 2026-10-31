using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainRiverBuilderTests
    {
        private const int MaskResolution = 128;
        private const int HeightSamplesPerSide = 101;
        private const float FlatTerrainHeight = 10f;
        private const float BedDepthMetres = 2f;
        private const float BankSlopeMetres = 4f;
        private const float ShallowBandMetres = 1.5f;
        private const float DeepBlocksBelowDepthMetres = 1.2f;
        private const float TexelToleranceMetres = 1f;

        private static TerrainRiverStamp CreateStraightRiver(float startX, float endX, float zPosition, float width)
        {
            int segmentCount = Mathf.RoundToInt(endX - startX);
            Vector3[] points = new Vector3[segmentCount + 1];
            float[] terrainHeights = new float[segmentCount + 1];
            float[] widths = new float[segmentCount + 1];
            for (int index = 0; index <= segmentCount; index++)
            {
                points[index] = new Vector3(startX + index, 0f, zPosition);
                terrainHeights[index] = FlatTerrainHeight;
                widths[index] = width;
            }

            TerrainRiverStamp river = new TerrainRiverStamp();
            river.points = points;
            river.widths = widths;
            river.waterLevels = TerrainRiverBuilder.ComputeWaterLevels(points, terrainHeights, BedDepthMetres, float.NaN);
            river.bedDepthMetres = BedDepthMetres;
            river.bankSlopeMetres = BankSlopeMetres;
            river.shallowBandMetres = ShallowBandMetres;
            river.deepBlocksBelowDepthMetres = DeepBlocksBelowDepthMetres;
            return river;
        }

        private static float[] CreateFlatTerrain()
        {
            float[] terrain = new float[HeightSamplesPerSide * HeightSamplesPerSide];
            for (int index = 0; index < terrain.Length; index++)
            {
                terrain[index] = FlatTerrainHeight;
            }
            return terrain;
        }

        private static float[] CarveTile(TerrainRiverStamp river, Vector2Int tileCoordinate, float[] terrain)
        {
            float[] delta = new float[HeightSamplesPerSide * HeightSamplesPerSide];
            TerrainRiverBuilder.ApplyRiverCarve(new List<TerrainRiverStamp> { river }, tileCoordinate, terrain, delta);
            return delta;
        }

        [Test]
        public void WaterLevelsNeverRiseDownstream()
        {
            System.Random random = new System.Random(4);
            Vector3[] points = new Vector3[100];
            float[] terrain = new float[100];
            for (int index = 0; index < points.Length; index++)
            {
                points[index] = new Vector3(index, 0f, 0f);
                terrain[index] = 5f + (float)random.NextDouble() * 10f;
            }

            float[] levels = TerrainRiverBuilder.ComputeWaterLevels(points, terrain, BedDepthMetres, float.NaN);
            for (int index = 0; index < levels.Length; index++)
            {
                Assert.LessOrEqual(levels[index], terrain[index] - 0.3f + 1e-4f, "index " + index);
                if (index + 1 < levels.Length)
                {
                    Assert.LessOrEqual(levels[index + 1], levels[index], "index " + index);
                }
            }

            float[] startedHigh = TerrainRiverBuilder.ComputeWaterLevels(points, terrain, BedDepthMetres, 50f);
            Assert.AreEqual(50f, startedHigh[0], 1e-4f);
        }

        [Test]
        public void CarveIsZeroOutsideHalfWidthPlusBank_AndNeverRaisesTheGround()
        {
            TerrainRiverStamp river = CreateStraightRiver(-80f, 80f, 0f, 6f);
            float[] delta = CarveTile(river, new Vector2Int(0, 0), CreateFlatTerrain());

            bool foundBank = false;
            for (int sampleZ = 0; sampleZ < HeightSamplesPerSide; sampleZ++)
            {
                float worldZ = -50f + sampleZ;
                float absoluteZ = Mathf.Abs(worldZ);
                for (int sampleX = 0; sampleX < HeightSamplesPerSide; sampleX++)
                {
                    float value = delta[sampleZ * HeightSamplesPerSide + sampleX];
                    Assert.LessOrEqual(value, 0f);
                    if (absoluteZ > 7f)
                    {
                        Assert.AreEqual(0f, value, "z " + worldZ);
                    }
                    else if (absoluteZ <= 3f)
                    {
                        Assert.AreEqual(-2.3f, value, 1e-3f, "z " + worldZ);
                    }
                    else if (absoluteZ < 7f)
                    {
                        // exactly at half + bank the smoothstep reaches the terrain, so that edge sample is 0
                        Assert.Less(value, 0f, "z " + worldZ);
                        Assert.Greater(value, -2.3f, "z " + worldZ);
                        foundBank = true;
                    }
                }
            }
            Assert.IsTrue(foundBank);
        }

        [Test]
        public void ShallowBandIsInsideTheBankBand_AndDeepIsOnlyTheMiddle()
        {
            TerrainRiverStamp river = CreateStraightRiver(-80f, 80f, 0f, 6f);
            float[] terrain = CreateFlatTerrain();
            float[] delta = CarveTile(river, new Vector2Int(0, 0), terrain);
            float[] ground = new float[terrain.Length];
            for (int index = 0; index < ground.Length; index++)
            {
                ground[index] = terrain[index] + delta[index];
            }

            byte[] shallow = new byte[MaskResolution * MaskResolution];
            byte[] deep = new byte[MaskResolution * MaskResolution];
            TerrainRiverBuilder.RasteriseSurfaceMasks(
                new List<TerrainRiverStamp> { river }, new Vector2Int(0, 0), MaskResolution, ground, shallow, deep);

            bool foundShallow = false;
            bool foundDeep = false;
            for (int row = 0; row < MaskResolution; row++)
            {
                float worldZ = -50f + (row + 0.5f) * 100f / MaskResolution;
                float absoluteZ = Mathf.Abs(worldZ);
                for (int column = 0; column < MaskResolution; column++)
                {
                    int index = row * MaskResolution + column;
                    bool isShallow = shallow[index] == 255;
                    bool isDeep = deep[index] == 255;
                    if (isShallow || isDeep)
                    {
                        Assert.LessOrEqual(absoluteZ, 7f + TexelToleranceMetres, "row " + row);
                    }
                    if (isDeep)
                    {
                        Assert.Less(absoluteZ, 1.5f + TexelToleranceMetres, "row " + row);
                        Assert.AreEqual(0, shallow[index], "deep wins at row " + row + " column " + column);
                        foundDeep = true;
                    }
                    if (isShallow)
                    {
                        foundShallow = true;
                    }
                }
            }
            Assert.IsTrue(foundDeep);
            Assert.IsTrue(foundShallow);
        }

        [Test]
        public void RiverCrossingATileBorder_GivesIdenticalCarveAtTheSeam()
        {
            TerrainRiverStamp river = CreateStraightRiver(-30f, 130f, 10f, 5f);
            float[] terrain = CreateFlatTerrain();
            float[] westDelta = CarveTile(river, new Vector2Int(0, 0), terrain);
            float[] eastDelta = CarveTile(river, new Vector2Int(1, 0), terrain);

            bool foundCarve = false;
            for (int sampleZ = 0; sampleZ < HeightSamplesPerSide; sampleZ++)
            {
                float westEdge = westDelta[sampleZ * HeightSamplesPerSide + 100];
                float eastEdge = eastDelta[sampleZ * HeightSamplesPerSide + 0];
                Assert.AreEqual(westEdge, eastEdge, "sample z " + sampleZ);
                foundCarve |= westEdge != 0f;
            }
            Assert.IsTrue(foundCarve);
        }

        [Test]
        public void RibbonMesh_EachSegmentBelongsToExactlyOneTile()
        {
            TerrainRiverStamp river = CreateStraightRiver(-30f, 130f, 10f, 5f);
            int totalTriangles = 0;
            for (int tileX = -1; tileX <= 2; tileX++)
            {
                Mesh mesh = TerrainRiverBuilder.BuildRibbonMeshForTile(river, new Vector2Int(tileX, 0), 0.05f);
                if (tileX == 0 || tileX == 1)
                {
                    Assert.IsNotNull(mesh, "tile " + tileX);
                }
                if (mesh == null)
                {
                    continue;
                }

                totalTriangles += mesh.triangles.Length / 3;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                int[] triangles = mesh.triangles;
                for (int normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                {
                    Assert.Greater(normals[normalIndex].y, 0.99f);
                }
                for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex += 3)
                {
                    Vector3 edgeA = vertices[triangles[triangleIndex + 1]] - vertices[triangles[triangleIndex]];
                    Vector3 edgeB = vertices[triangles[triangleIndex + 2]] - vertices[triangles[triangleIndex]];
                    Assert.Greater(Vector3.Cross(edgeA, edgeB).y, 0f, "triangle " + triangleIndex / 3);
                }
                Object.DestroyImmediate(mesh);
            }

            Assert.AreEqual(2 * 160, totalTriangles);
            Assert.IsNull(TerrainRiverBuilder.BuildRibbonMeshForTile(river, new Vector2Int(5, 5), 0.05f));
        }

        [Test]
        public void RibbonUvRunsAlongTheFlow()
        {
            TerrainRiverStamp river = CreateStraightRiver(-30f, 40f, 10f, 5f);
            Mesh mesh = TerrainRiverBuilder.BuildRibbonMeshForTile(river, new Vector2Int(0, 0), 0.05f);
            Assert.IsNotNull(mesh);

            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            Assert.GreaterOrEqual(uvs.Length, 4);
            Assert.AreEqual(0f, uvs[0].y, 1e-4f);
            Assert.AreEqual(0f, uvs[1].y, 1e-4f);

            float previousV = uvs[0].y;
            float previousX = vertices[0].x;
            for (int index = 0; index < uvs.Length; index++)
            {
                Assert.IsTrue(Mathf.Approximately(uvs[index].x, 0f) || Mathf.Approximately(uvs[index].x, 1f), "vertex " + index);
                if (vertices[index].x > previousX + 1e-4f)
                {
                    Assert.GreaterOrEqual(uvs[index].y, previousV - 1e-4f, "vertex " + index);
                    previousX = vertices[index].x;
                    previousV = uvs[index].y;
                }
            }
            Object.DestroyImmediate(mesh);
        }
    }
}
