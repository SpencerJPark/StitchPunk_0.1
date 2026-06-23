using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests
{
    // The awareness pipeline scores targets with these helpers. A sign flip or off-by-one here silently
    // inverts AI behaviour (e.g. preferring the FARTHEST threat), so the boundaries are pinned explicitly.
    [TestFixture]
    public sealed class AIUtilsTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void FastDistanceScore_IsOneAtZeroDistance()
        {
            float3 position = new float3(5f, 0f, 5f);
            float score = AIUtils.FastDistanceScore(position, position, 100f);
            Assert.AreEqual(1f, score, Tolerance);
        }

        [Test]
        public void FastDistanceScore_IsZeroAtMaxRange()
        {
            float3 from = new float3(0f, 0f, 0f);
            float3 to = new float3(10f, 0f, 0f); // distSq = 100 == maxRangeSq
            float score = AIUtils.FastDistanceScore(from, to, 100f);
            Assert.AreEqual(0f, score, Tolerance);
        }

        [Test]
        public void FastDistanceScore_FallsOffLinearlyInSquaredSpace()
        {
            float3 from = new float3(0f, 0f, 0f);
            float3 to = new float3(0f, 0f, math.sqrt(50f)); // distSq = 50, half of maxRangeSq
            float score = AIUtils.FastDistanceScore(from, to, 100f);
            Assert.AreEqual(0.5f, score, Tolerance);
        }

        [Test]
        public void FastDistanceScore_SaturatesToZeroBeyondMaxRange()
        {
            float3 from = new float3(0f, 0f, 0f);
            float3 to = new float3(0f, 0f, 1000f);
            float score = AIUtils.FastDistanceScore(from, to, 100f);
            Assert.AreEqual(0f, score, Tolerance);
        }

        [Test]
        public void AttackRangeScore_IsOneWithinRange()
        {
            Assert.AreEqual(1f, AIUtils.AttackRangeScore(1.5f, 2f), Tolerance);
            Assert.AreEqual(1f, AIUtils.AttackRangeScore(2f, 2f), Tolerance);
        }

        [Test]
        public void AttackRangeScore_DecaysAsRangeRatioBeyondReach()
        {
            // dist twice the range -> half score.
            Assert.AreEqual(0.5f, AIUtils.AttackRangeScore(4f, 2f), Tolerance);
        }

        [Test]
        public void IsTargetInRange_RespectsRadiusBoundary()
        {
            LocalTransform self = LocalTransform.FromPosition(new float3(0f, 0f, 0f));
            LocalTransform insideTarget = LocalTransform.FromPosition(new float3(3f, 0f, 0f));
            LocalTransform outsideTarget = LocalTransform.FromPosition(new float3(6f, 0f, 0f));

            Assert.IsTrue(AIUtils.IsTargetInRange(self, insideTarget, 5f));
            Assert.IsFalse(AIUtils.IsTargetInRange(self, outsideTarget, 5f));
        }

        [Test]
        public void IsTargetOutOfRange_IsInverseOfInRange()
        {
            LocalTransform self = LocalTransform.FromPosition(new float3(0f, 0f, 0f));
            LocalTransform target = LocalTransform.FromPosition(new float3(6f, 0f, 0f));

            Assert.IsTrue(AIUtils.IsTargetOutOfRange(self, target, 5f));
            Assert.IsFalse(AIUtils.IsTargetOutOfRange(self, target, 10f));
        }
    }
}
