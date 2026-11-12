using NUnit.Framework;
using UnityEngine;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class TerrainAreaRestrictedSamplerTests
    {
        private sealed class AlwaysAcceptingSampler : ITerrainScatterSampler
        {
            public float HeightAt(float worldX, float worldZ)
            {
                return 0f;
            }

            public Vector3 NormalAt(float worldX, float worldZ)
            {
                return Vector3.up;
            }

            public bool TryGetBiome(float worldX, float worldZ, out float dirt, out float grass, out float forest, out float stone)
            {
                dirt = 0f;
                grass = 1f;
                forest = 0f;
                stone = 0f;
                return true;
            }
        }

        private static TerrainAreaRestrictedSampler BuildSampler(float clearingThreshold01)
        {
            TerrainAreaStamp area = new TerrainAreaStamp
            {
                outlineXZ = new Vector2[]
                {
                    new Vector2(-20f, -20f),
                    new Vector2(20f, -20f),
                    new Vector2(20f, 20f),
                    new Vector2(-20f, 20f)
                },
                edgeNoiseMetres = 0f,
                seed = 7
            };
            return new TerrainAreaRestrictedSampler(new AlwaysAcceptingSampler(), area, 5f, clearingThreshold01);
        }

        private static bool Accepts(TerrainAreaRestrictedSampler sampler, float worldX, float worldZ)
        {
            return sampler.TryGetBiome(worldX, worldZ, out float dirt, out float grass, out float forest, out float stone);
        }

        [Test]
        public void PointsOutsideTheOutlineAreRejected()
        {
            TerrainAreaRestrictedSampler sampler = BuildSampler(0f);

            Assert.IsFalse(Accepts(sampler, 25f, 0f));
            Assert.IsFalse(Accepts(sampler, 0f, -30f));
            Assert.IsFalse(Accepts(sampler, 100f, 100f));
            Assert.IsTrue(Accepts(sampler, 0f, 0f));
        }

        [Test]
        public void ZeroThresholdNeverMakesClearings()
        {
            TerrainAreaRestrictedSampler sampler = BuildSampler(0f);

            for (float worldX = -18f; worldX <= 18f; worldX += 1.5f)
            {
                for (float worldZ = -18f; worldZ <= 18f; worldZ += 1.5f)
                {
                    Assert.IsTrue(Accepts(sampler, worldX, worldZ), "rejected at " + worldX + "," + worldZ);
                }
            }
        }

        [Test]
        public void ThresholdOneRejectsEveryInsidePoint()
        {
            TerrainAreaRestrictedSampler sampler = BuildSampler(1f);

            for (float worldX = -18f; worldX <= 18f; worldX += 1.5f)
            {
                for (float worldZ = -18f; worldZ <= 18f; worldZ += 1.5f)
                {
                    Assert.IsFalse(Accepts(sampler, worldX, worldZ), "accepted at " + worldX + "," + worldZ);
                }
            }
        }
    }
}
