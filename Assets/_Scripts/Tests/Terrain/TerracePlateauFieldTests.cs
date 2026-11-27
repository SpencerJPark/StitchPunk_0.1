using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public class TerracePlateauFieldTests
    {
        private const int WorldSeed = 12345;
        private const float TileSizeMetres = 100f;

        private static float2 TileCentre(int2 tileCoordinate)
        {
            return new float2(tileCoordinate.x, tileCoordinate.y) * TileSizeMetres;
        }

        private static float2 PointAtDistanceFromBorder(int2 tileCoordinate, float distanceFromBorder, float alongBorderFraction)
        {
            float2 centre = TileCentre(tileCoordinate);
            return centre + new float2(50f - distanceFromBorder, (alongBorderFraction - 0.5f) * 100f);
        }

        [Test]
        public void LockBandValueIgnoresTheRollAndTheTileStyle()
        {
            List<TerracePlateauStyle> styles = TerracePlateauStyleDefaults.CreateDefaultStyles();
            float2 worldSeedOffset = TerracePlateauField.WorldSeedOffset(WorldSeed);
            System.Random random = new System.Random(7);
            int2 tileCoordinate = new int2(2, -3);
            float2 tileCentre = TileCentre(tileCoordinate);

            for (int pointIndex = 0; pointIndex < 200; pointIndex++)
            {
                float distanceFromBorder = (float)random.NextDouble() * 6f;
                float alongBorder = ((float)random.NextDouble() - 0.5f) * 100f;
                float borderSign = pointIndex % 2 == 0 ? 1f : -1f;
                float2 worldPoint = pointIndex % 4 < 2
                    ? tileCentre + new float2(borderSign * (50f - distanceFromBorder), alongBorder)
                    : tileCentre + new float2(alongBorder, borderSign * (50f - distanceFromBorder));

                float expected = TerracePlateauField.SampleStyle(worldPoint, worldSeedOffset, styles[0]);

                for (int rollIndex = 0; rollIndex < 5; rollIndex++)
                {
                    float actual = TerracePlateauField.SampleTileField(
                        WorldSeed, tileCoordinate, rollIndex, styles, worldPoint,
                        out float firstThreshold, out float secondThreshold);

                    Assert.AreEqual(expected, actual, "value, roll " + rollIndex);
                    Assert.AreEqual(styles[0].firstLevelThreshold, firstThreshold, "first threshold, roll " + rollIndex);
                    Assert.AreEqual(styles[0].secondLevelThreshold, secondThreshold, "second threshold, roll " + rollIndex);
                }
            }
        }

        [Test]
        public void BlendFactorIsZeroInTheLockBandAndOneFromThirtyMetresIn()
        {
            int2 tileCoordinate = new int2(-1, 4);

            foreach (float lockBandDistance in new float[] { 0f, 3f, 5.9f })
            {
                float factor = TerracePlateauField.TileBlendFactor(PointAtDistanceFromBorder(tileCoordinate, lockBandDistance, 0.5f), tileCoordinate);
                Assert.AreEqual(0f, factor, 1e-6f, "lock band " + lockBandDistance);
            }

            float2 outsideTile = TileCentre(tileCoordinate) + new float2(60f, 0f);
            Assert.AreEqual(0f, TerracePlateauField.TileBlendFactor(outsideTile, tileCoordinate), 1e-6f, "outside");

            foreach (float interiorDistance in new float[] { 30f, 40f, 50f })
            {
                float factor = TerracePlateauField.TileBlendFactor(PointAtDistanceFromBorder(tileCoordinate, interiorDistance, 0.5f), tileCoordinate);
                Assert.AreEqual(1f, factor, 1e-6f, "interior " + interiorDistance);
            }

            float midFactor = TerracePlateauField.TileBlendFactor(PointAtDistanceFromBorder(tileCoordinate, 18f, 0.5f), tileCoordinate);
            Assert.Greater(midFactor, 0f);
            Assert.Less(midFactor, 1f);
        }

        private static float StandardDeviation(IReadOnlyList<float> values)
        {
            double sum = 0.0;
            for (int index = 0; index < values.Count; index++)
            {
                sum += values[index];
            }

            double mean = sum / values.Count;
            double squaredSum = 0.0;
            for (int index = 0; index < values.Count; index++)
            {
                double difference = values[index] - mean;
                squaredSum += difference * difference;
            }

            return (float)Math.Sqrt(squaredSum / values.Count);
        }

        [Test]
        public void RecentredBlendKeepsTheSpreadOfItsInputs()
        {
            List<TerracePlateauStyle> styles = TerracePlateauStyleDefaults.CreateDefaultStyles();
            TerracePlateauStyle style = styles[0];
            float2 firstSeedOffset = TerracePlateauField.WorldSeedOffset(WorldSeed);
            float2 secondSeedOffset = TerracePlateauField.WorldSeedOffset(WorldSeed + 977);
            System.Random random = new System.Random(21);
            const int SampleCount = 4000;
            List<float> firstValues = new List<float>(SampleCount);
            List<float> secondValues = new List<float>(SampleCount);
            List<float> blendedValues = new List<float>(SampleCount);

            for (int sampleIndex = 0; sampleIndex < SampleCount; sampleIndex++)
            {
                float2 worldPoint = new float2((float)random.NextDouble() * 4000f - 2000f, (float)random.NextDouble() * 4000f - 2000f);
                float firstValue = TerracePlateauField.SampleStyle(worldPoint, firstSeedOffset, style);
                float secondValue = TerracePlateauField.SampleStyle(worldPoint, secondSeedOffset, style);
                firstValues.Add(firstValue);
                secondValues.Add(secondValue);
                blendedValues.Add(TerracePlateauField.BlendWorldAndTile(firstValue, secondValue, 0.5f, style.bias));
            }

            float inputSpread = (StandardDeviation(firstValues) + StandardDeviation(secondValues)) * 0.5f;
            float blendedSpread = StandardDeviation(blendedValues);
            Assert.AreEqual(inputSpread, blendedSpread, inputSpread * 0.15f);
        }

        [Test]
        public void ZeroWeightStyleIsNeverPicked()
        {
            List<TerracePlateauStyle> styles = TerracePlateauStyleDefaults.CreateDefaultStyles();
            TerracePlateauStyle firstZeroed = styles[1];
            firstZeroed.weight = 0f;
            styles[1] = firstZeroed;
            TerracePlateauStyle secondZeroed = styles[3];
            secondZeroed.weight = 0f;
            styles[3] = secondZeroed;

            HashSet<int> picked = new HashSet<int>();
            for (int tileX = 0; tileX < 30; tileX++)
            {
                for (int tileY = 0; tileY < 20; tileY++)
                {
                    for (int rollIndex = 0; rollIndex < 5; rollIndex++)
                    {
                        picked.Add(TerracePlateauField.StyleIndexFor(WorldSeed, new int2(tileX - 15, tileY - 10), rollIndex, styles));
                    }
                }
            }

            Assert.IsFalse(picked.Contains(1));
            Assert.IsFalse(picked.Contains(3));
            for (int styleIndex = 0; styleIndex < styles.Count; styleIndex++)
            {
                if (styleIndex != 1 && styleIndex != 3)
                {
                    Assert.IsTrue(picked.Contains(styleIndex), "style " + styleIndex + " never picked");
                }
            }
        }

        [Test]
        public void SameInputsGiveTheSameField()
        {
            List<TerracePlateauStyle> styles = TerracePlateauStyleDefaults.CreateDefaultStyles();
            System.Random random = new System.Random(99);
            int2 tileCoordinate = new int2(1, 1);

            for (int pointIndex = 0; pointIndex < 50; pointIndex++)
            {
                float2 worldPoint = new float2((float)random.NextDouble() * 100f + 50f, (float)random.NextDouble() * 100f + 50f);
                float firstValue = TerracePlateauField.SampleTileField(WorldSeed, tileCoordinate, 1, styles, worldPoint, out float firstOfFirst, out float secondOfFirst);
                float secondValue = TerracePlateauField.SampleTileField(WorldSeed, tileCoordinate, 1, styles, worldPoint, out float firstOfSecond, out float secondOfSecond);
                Assert.AreEqual(firstValue, secondValue);
                Assert.AreEqual(firstOfFirst, firstOfSecond);
                Assert.AreEqual(secondOfFirst, secondOfSecond);
            }
        }

        [Test]
        public void DifferentRollsGiveDifferentTileInteriors()
        {
            List<TerracePlateauStyle> styles = TerracePlateauStyleDefaults.CreateDefaultStyles();
            int2 tileCoordinate = new int2(0, 0);
            int differingPointCount = 0;

            for (int gridX = 0; gridX < 10; gridX++)
            {
                for (int gridY = 0; gridY < 10; gridY++)
                {
                    float2 worldPoint = new float2(-20f + gridX * 4.4f, -20f + gridY * 4.4f);
                    float rollOneValue = TerracePlateauField.SampleTileField(WorldSeed, tileCoordinate, 1, styles, worldPoint, out float unusedFirstOne, out float unusedSecondOne);
                    float rollTwoValue = TerracePlateauField.SampleTileField(WorldSeed, tileCoordinate, 2, styles, worldPoint, out float unusedFirstTwo, out float unusedSecondTwo);
                    if (rollOneValue != rollTwoValue)
                    {
                        differingPointCount++;
                    }
                }
            }

            Assert.GreaterOrEqual(differingPointCount, 50);
        }
    }
}
