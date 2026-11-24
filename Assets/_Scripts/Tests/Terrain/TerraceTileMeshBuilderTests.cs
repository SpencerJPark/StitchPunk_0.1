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
        public void InteriorPlateauWithNotchOnlyOpenEdgesAreTheHiddenSkirtSeams()
        {
            FillLevel(30, 69, 30, 69, 1);
            FillLevel(45, 54, 30, 44, 0);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            AssertNonRimOpenEdgesAreSkirtSeams();
            Assert.AreEqual(400, CountOpenEdges(mesh, true), "rim of an untouched tile edge is one open edge per 1 m cell");
            Assert.AreEqual(1, TerraceTileMeshBuilder.LastBuildOutlineChains.Count);
            TerraceOutlineChain chain = TerraceTileMeshBuilder.LastBuildOutlineChains[0];
            Assert.AreEqual(-world.terraceLevelHeightMetres, chain.bottomHeights[0] - chain.points[0].y, 1e-3f);
        }

        [Test]
        public void PlateauCrossingRimOnlyOpenEdgesAreRimAndHiddenSkirtSeams()
        {
            FillLevel(80, 99, 20, 60, 1);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            AssertNonRimOpenEdgesAreSkirtSeams();
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
                if (longestEdge > 1.415f && IsNearAnyFilletZone((first + second + third) / 3f))
                {
                    continue; // TX-D11 fillets in submesh 0 legitimately span cells
                }
                Assert.LessOrEqual(longestEdge, 1.415f);
            }
        }

        [Test]
        public void CliffProfileFacesOutwardAndUp()
        {
            FillLevel(30, 69, 30, 69, 1);
            FillLevel(45, 54, 30, 44, 0);
            mesh = TerraceTileMeshBuilder.BuildTerracedTileMesh(tile, world, null, null, 0f);

            Assert.Greater(TerraceTileMeshBuilder.LastBuildOutlineChains.Count, 0);
            foreach (TerraceOutlineChain chain in TerraceTileMeshBuilder.LastBuildOutlineChains)
            {
                for (int pointIndex = 0; pointIndex < chain.points.Length; pointIndex++)
                {
                    Assert.Greater(chain.footOffsetMetres[pointIndex], 0f, "foot offset at point " + pointIndex);
                    Assert.Greater(chain.topRoundMetres[pointIndex], 0f, "top round at point " + pointIndex);
                }
            }

            Vector3[] vertices = mesh.vertices;
            int[] wallTriangles = mesh.GetTriangles(1);
            Assert.Greater(wallTriangles.Length, 0);
            for (int index = 0; index < wallTriangles.Length; index += 3)
            {
                Vector3 first = vertices[wallTriangles[index]];
                Vector3 second = vertices[wallTriangles[index + 1]];
                Vector3 third = vertices[wallTriangles[index + 2]];
                Vector3 frontNormal = Vector3.Cross(second - first, third - first);
                Vector3 centroid = (first + second + third) / 3f;
                Vector2 outward = NearestOutwardNormal(centroid);
                Assert.Greater(Vector3.Dot(frontNormal, new Vector3(outward.x, 1f, outward.y)), 0f, "profile triangle faces inward or down at " + centroid);
            }

            int[] groundTriangles = mesh.GetTriangles(0);
            for (int index = 0; index < groundTriangles.Length; index += 3)
            {
                Vector3 first = vertices[groundTriangles[index]];
                Vector3 second = vertices[groundTriangles[index + 1]];
                Vector3 third = vertices[groundTriangles[index + 2]];
                Vector3 frontNormal = Vector3.Cross(second - first, third - first);
                Assert.Greater(frontNormal.y, 0f, "ground triangle faces down at " + (first + second + third) / 3f);
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

        private bool IsNearAnyFilletZone(Vector3 position)
        {
            foreach (TerraceOutlineChain chain in TerraceTileMeshBuilder.LastBuildOutlineChains)
            {
                for (int pointIndex = 0; pointIndex < chain.points.Length; pointIndex++)
                {
                    Vector3 point = chain.points[pointIndex];
                    float horizontalDistance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(point.x, point.z));
                    if (horizontalDistance <= chain.footOffsetMetres[pointIndex] + 0.1f)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static Vector2 NearestOutwardNormal(Vector3 position)
        {
            float bestDistance = float.MaxValue;
            Vector2 bestNormal = Vector2.zero;
            foreach (TerraceOutlineChain chain in TerraceTileMeshBuilder.LastBuildOutlineChains)
            {
                for (int pointIndex = 0; pointIndex < chain.points.Length; pointIndex++)
                {
                    Vector3 point = chain.points[pointIndex];
                    float distance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(point.x, point.z));
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestNormal = chain.outwardNormals[pointIndex];
                    }
                }
            }
            return bestNormal;
        }

        private void AssertNonRimOpenEdgesAreSkirtSeams()
        {
            List<Vector3> lowerGroundCutAnchors = new List<Vector3>();
            List<Vector3> footEndAnchors = new List<Vector3>();
            foreach (TerraceOutlineChain chain in TerraceTileMeshBuilder.LastBuildOutlineChains)
            {
                for (int pointIndex = 0; pointIndex < chain.points.Length; pointIndex++)
                {
                    Vector3 point = chain.points[pointIndex];
                    Vector2 outward = chain.outwardNormals[pointIndex];
                    float bottomHeight = chain.bottomHeights[pointIndex];
                    lowerGroundCutAnchors.Add(new Vector3(point.x, bottomHeight, point.z));
                    footEndAnchors.Add(new Vector3(
                        point.x + outward.x * chain.footOffsetMetres[pointIndex],
                        bottomHeight,
                        point.z + outward.y * chain.footOffsetMetres[pointIndex]));
                }
            }

            List<Vector3[]> openEdges = CollectOpenEdges(mesh, false);
            foreach (Vector3[] edge in openEdges)
            {
                bool isSeam = false;
                for (int endpoint = 0; endpoint < 2; endpoint++)
                {
                    isSeam = IsNearAnchor(edge[endpoint], lowerGroundCutAnchors, 0.1f) || IsNearAnchor(edge[endpoint], footEndAnchors, 0.15f);
                    if (!isSeam)
                    {
                        break;
                    }
                }
                Assert.IsTrue(isSeam, "open edge that is not a skirt seam (real hole) from " + edge[0] + " to " + edge[1]);
            }
        }

        private static bool IsNearAnchor(Vector3 position, List<Vector3> anchors, float heightTolerance)
        {
            foreach (Vector3 anchor in anchors)
            {
                float horizontalDistance = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(anchor.x, anchor.z));
                if (horizontalDistance < 0.05f && Mathf.Abs(position.y - anchor.y) < heightTolerance)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountOpenEdges(Mesh builtMesh, bool onRim)
        {
            return CollectOpenEdges(builtMesh, onRim).Count;
        }

        // Collects edges used by exactly one triangle across both submeshes, split by whether both endpoints lie on the tile rim.
        private static List<Vector3[]> CollectOpenEdges(Mesh builtMesh, bool onRim)
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
            Dictionary<long, Vector3[]> edgeEndpoints = new Dictionary<long, Vector3[]>();
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
                        edgeEndpoints[edgeKey] = new Vector3[] { vertices[fromVertex], vertices[toVertex] };
                    }
                }
            }

            List<Vector3[]> openEdges = new List<Vector3[]>();
            foreach (KeyValuePair<long, int> entry in edgeUseCounts)
            {
                if (entry.Value == 1 && edgeOnRim[entry.Key] == onRim)
                {
                    openEdges.Add(edgeEndpoints[entry.Key]);
                }
            }
            return openEdges;
        }

        private static bool IsOnRim(Vector3 position)
        {
            return Mathf.Abs(Mathf.Abs(position.x) - 50f) < RimTolerance || Mathf.Abs(Mathf.Abs(position.z) - 50f) < RimTolerance;
        }
    }
}
