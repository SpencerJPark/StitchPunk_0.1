using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainHeightSamplerTests
    {
        private const int SamplesPerSide = 101;
        private const float Tolerance = 0.0001f;

        private readonly List<BlobAssetReference<TerrainTileHeightBlob>> blobsToDispose = new List<BlobAssetReference<TerrainTileHeightBlob>>();

        [TearDown]
        public void DisposeBlobs()
        {
            foreach (BlobAssetReference<TerrainTileHeightBlob> blob in blobsToDispose)
                blob.Dispose();
            blobsToDispose.Clear();
        }

        private BlobAssetReference<TerrainTileHeightBlob> BuildBlob(System.Func<int, int, float> heightAtVertex)
        {
            BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref TerrainTileHeightBlob root = ref builder.ConstructRoot<TerrainTileHeightBlob>();
            root.samplesPerSide = SamplesPerSide;
            root.sampleSpacingMetres = 1f;
            BlobBuilderArray<float> heights = builder.Allocate(ref root.groundHeights, SamplesPerSide * SamplesPerSide);
            for (int vertexZ = 0; vertexZ < SamplesPerSide; vertexZ++)
                for (int vertexX = 0; vertexX < SamplesPerSide; vertexX++)
                    heights[vertexZ * SamplesPerSide + vertexX] = heightAtVertex(vertexX, vertexZ);
            BlobAssetReference<TerrainTileHeightBlob> blob = builder.CreateBlobAssetReference<TerrainTileHeightBlob>(Allocator.Persistent);
            builder.Dispose();
            blobsToDispose.Add(blob);
            return blob;
        }

        [Test]
        public void SampleOnGridVertexReturnsThatVertexsStoredHeight()
        {
            BlobAssetReference<TerrainTileHeightBlob> blob = BuildBlob((vertexX, vertexZ) => ((vertexX * 7 + vertexZ * 13) % 17) * 0.25f);
            int2 tileCoordinate = new int2(2, -1);

            float sampled = TerrainHeightSampler.SampleTile(ref blob.Value, tileCoordinate, 150f + 3f, -150f + 5f);

            Assert.AreEqual(((3 * 7 + 5 * 13) % 17) * 0.25f, sampled, Tolerance);
        }

        [Test]
        public void SampleInsideCellFollowsTheTwoTrianglesNotBilinear()
        {
            const int cellX = 10;
            const int cellZ = 20;
            const float heightMinMin = 1f;
            const float heightMaxMin = 3f;
            const float heightMinMax = 2f;
            const float heightMaxMax = 7f;
            BlobAssetReference<TerrainTileHeightBlob> blob = BuildBlob((vertexX, vertexZ) =>
            {
                if (vertexX == cellX && vertexZ == cellZ) return heightMinMin;
                if (vertexX == cellX + 1 && vertexZ == cellZ) return heightMaxMin;
                if (vertexX == cellX && vertexZ == cellZ + 1) return heightMinMax;
                if (vertexX == cellX + 1 && vertexZ == cellZ + 1) return heightMaxMax;
                return 0f;
            });
            int2 tileCoordinate = new int2(2, -1);
            float cellWorldX = 150f + cellX;
            float cellWorldZ = -150f + cellZ;

            float lowerTriangleSample = TerrainHeightSampler.SampleTile(ref blob.Value, tileCoordinate, cellWorldX + 0.25f, cellWorldZ + 0.5f);
            float upperTriangleSample = TerrainHeightSampler.SampleTile(ref blob.Value, tileCoordinate, cellWorldX + 0.75f, cellWorldZ + 0.5f);

            float expectedLower = heightMinMin + (heightMaxMin - heightMinMin) * 0.25f + (heightMinMax - heightMinMin) * 0.5f;
            float expectedUpper = heightMaxMax + (heightMinMax - heightMaxMax) * 0.25f + (heightMaxMin - heightMaxMax) * 0.5f;
            Assert.AreEqual(expectedLower, lowerTriangleSample, Tolerance);
            Assert.AreEqual(expectedUpper, upperTriangleSample, Tolerance);
        }

        [Test]
        public void TrySampleGroundHeightPicksContainingTileAndFailsWhenTileIsNotLoaded()
        {
            BlobAssetReference<TerrainTileHeightBlob> originBlob = BuildBlob((vertexX, vertexZ) => 5f);
            BlobAssetReference<TerrainTileHeightBlob> neighbourBlob = BuildBlob((vertexX, vertexZ) => 9f);
            NativeArray<TerrainTileHeightField> loadedTiles = new NativeArray<TerrainTileHeightField>(2, Allocator.Temp);
            loadedTiles[0] = new TerrainTileHeightField { blob = originBlob, coordinate = new int2(0, 0) };
            loadedTiles[1] = new TerrainTileHeightField { blob = neighbourBlob, coordinate = new int2(2, -1) };

            bool foundOrigin = TerrainHeightSampler.TrySampleGroundHeight(in loadedTiles, new float3(1f, 0f, 1f), out float originHeight);
            bool foundNeighbour = TerrainHeightSampler.TrySampleGroundHeight(in loadedTiles, new float3(210f, 0f, -90f), out float neighbourHeight);
            bool foundMissing = TerrainHeightSampler.TrySampleGroundHeight(in loadedTiles, new float3(1000f, 0f, 1000f), out float missingHeight);
            loadedTiles.Dispose();

            Assert.IsTrue(foundOrigin);
            Assert.AreEqual(5f, originHeight, Tolerance);
            Assert.IsTrue(foundNeighbour);
            Assert.AreEqual(9f, neighbourHeight, Tolerance);
            Assert.IsFalse(foundMissing);
            Assert.IsFalse(missingHeight == 5f || missingHeight == 9f);
        }
    }
}
