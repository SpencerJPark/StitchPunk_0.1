using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    public class TerraceTileMeshBuilderTests
    {
        private const float WeldQuantum = 1e-4f;
        private const float RimTolerance = 1e-4f;

        private TerrainTileData tile;
        private TerrainWorldProfile world;
        private Mesh mesh;

        [SetUp]
        public void SetUp()
        {
            tile = ScriptableObject.CreateInstance<TerrainTileData>();
            tile.EnsureHeightArrays();
            tile.baseTerraceLevels = new byte[TerrainTileData.TerraceCellsPerSide * TerrainTileData.TerraceCellsPerSide];
            System.Array.Fill(tile.baseTerraceLevels, (byte)TerrainTileData.TerraceLevelBias);
            world = ScriptableObject.CreateInstance<TerrainWorldProfile>();
        }

        [TearDown]
        public void TearDown()
        {
            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(world);
        }

        [Test]
        public void FlatTileMatchesPlainTileMesh()
        {
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);
            Mesh plain = TerrainMeshBuilder.BuildTileMesh(tile, 1, null, 0f);
            try
            {
                CollectionAssert.AreEqual(plain.vertices, mesh.vertices);
                CollectionAssert.AreEqual(plain.triangles, mesh.triangles);
                CollectionAssert.AreEqual(plain.uv, mesh.uv);
                Assert.AreEqual(0, TerraceTileMeshBuilder.LastBuildOutlineChains.Count);
            }
            finally
            {
                Object.DestroyImmediate(plain);
            }
        }

        [Test]
        public void InteriorPlateauWithNotchIsWatertightAndHasOneChain()
        {
            FillLevel(30, 69, 30, 69, 1);
            FillLevel(45, 54, 30, 44, 0);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            Assert.AreEqual(0, CountOpenEdges(mesh, false), "open edges away from the rim");
            Assert.AreEqual(400, CountOpenEdges(mesh, true), "rim of an untouched tile edge is one open edge per 1 m cell");
            Assert.AreEqual(1, TerraceTileMeshBuilder.LastBuildOutlineChains.Count);
            TerraceOutlineChain chain = TerraceTileMeshBuilder.LastBuildOutlineChains[0];
            Assert.AreEqual(-world.terraceLevelHeightMetres, chain.bottomHeights[0] - chain.points[0].y, 1e-3f);
        }

        [Test]
        public void PlateauCrossingRimOnlyLeavesRimBoundaryEdges()
        {
            FillLevel(80, 99, 20, 60, 1);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            Assert.AreEqual(0, CountOpenEdges(mesh, false), "open edges away from the rim");
            Assert.Greater(CountOpenEdges(mesh, true), 0);
        }

        [Test]
        public void GroundTrianglesStayInsideOneCell()
        {
            FillLevel(30, 69, 30, 69, 1);
            FillLevel(45, 54, 30, 44, 0);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.GetTriangles(0);
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 first = vertices[triangles[index]];
                Vector3 second = vertices[triangles[index + 1]];
                Vector3 third = vertices[triangles[index + 2]];
                float longestEdge = Mathf.Max(
                    Vector3.Distance(first, second),
                    Mathf.Max(Vector3.Distance(second, third), Vector3.Distance(third, first)));
                Assert.LessOrEqual(longestEdge, 1.415f);
            }
        }

        [Test]
        public void SunkenHalfOnSlopedTileGivesEveryWallPointAThreeMetreDrop()
        {
            for (int sampleZ = 0; sampleZ < TerrainTileData.HeightSamplesPerSide; sampleZ++)
            {
                for (int sampleX = 0; sampleX < TerrainTileData.HeightSamplesPerSide; sampleX++)
                {
                    tile.baseHeights[sampleZ * TerrainTileData.HeightSamplesPerSide + sampleX] = sampleX * 0.1f;
                }
            }
            FillLevel(0, 49, 0, 99, -1);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            Assert.Greater(TerraceTileMeshBuilder.LastBuildOutlineChains.Count, 0);
            foreach (TerraceOutlineChain chain in TerraceTileMeshBuilder.LastBuildOutlineChains)
            {
                for (int pointIndex = 0; pointIndex < chain.points.Length; pointIndex++)
                {
                    Assert.AreEqual(world.terraceLevelHeightMetres, chain.points[pointIndex].y - chain.bottomHeights[pointIndex], 0.05f);
                }
            }
        }

        private void FillLevel(int minCellX, int maxCellX, int minCellZ, int maxCellZ, int level)
        {
            for (int cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                for (int cellX = minCellX; cellX <= maxCellX; cellX++)
                {
                    tile.baseTerraceLevels[cellZ * TerrainTileData.TerraceCellsPerSide + cellX] = (byte)(level + TerrainTileData.TerraceLevelBias);
                }
            }
        }

        // Counts edges used by exactly one triangle across both submeshes, split by whether both endpoints lie on the tile rim.
        private static int CountOpenEdges(Mesh builtMesh, bool onRim)
        {
            Vector3[] vertices = builtMesh.vertices;
            Dictionary<Vector3Int, int> weldedIds = new Dictionary<Vector3Int, int>();
            int[] vertexToWelded = new int[vertices.Length];
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(vertices[index].x / WeldQuantum),
                    Mathf.RoundToInt(vertices[index].y / WeldQuantum),
                    Mathf.RoundToInt(vertices[index].z / WeldQuantum));
                if (!weldedIds.TryGetValue(key, out int weldedId))
                {
                    weldedId = weldedIds.Count;
                    weldedIds.Add(key, weldedId);
                }
                vertexToWelded[index] = weldedId;
            }

            Dictionary<long, int> edgeUseCounts = new Dictionary<long, int>();
            Dictionary<long, bool> edgeOnRim = new Dictionary<long, bool>();
            for (int submesh = 0; submesh < builtMesh.subMeshCount; submesh++)
            {
                int[] triangles = builtMesh.GetTriangles(submesh);
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int fromVertex = triangles[index + corner];
                        int toVertex = triangles[index + (corner + 1) % 3];
                        int fromWelded = vertexToWelded[fromVertex];
                        int toWelded = vertexToWelded[toVertex];
                        if (fromWelded == toWelded)
                        {
                            continue;
                        }
                        long edgeKey = ((long)Mathf.Min(fromWelded, toWelded) << 32) | (long)Mathf.Max(fromWelded, toWelded);
                        edgeUseCounts.TryGetValue(edgeKey, out int useCount);
                        edgeUseCounts[edgeKey] = useCount + 1;
                        edgeOnRim[edgeKey] = IsOnRim(vertices[fromVertex]) && IsOnRim(vertices[toVertex]);
                    }
                }
            }

            int openEdgeCount = 0;
            foreach (KeyValuePair<long, int> entry in edgeUseCounts)
            {
                if (entry.Value == 1 && edgeOnRim[entry.Key] == onRim)
                {
                    openEdgeCount++;
                }
            }
            return openEdgeCount;
        }

        private static bool IsOnRim(Vector3 position)
        {
            return Mathf.Abs(Mathf.Abs(position.x) - 50f) < RimTolerance || Mathf.Abs(Mathf.Abs(position.z) - 50f) < RimTolerance;
        }
    }
}
