using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class ProjectileHitUtilsTests
    {
        private static readonly Entity ShooterEntity = new Entity { Index = 1, Version = 1 };
        private static readonly Entity FirstOtherEntity = new Entity { Index = 2, Version = 1 };
        private static readonly Entity SecondOtherEntity = new Entity { Index = 3, Version = 1 };

        private static ProjectileHitCandidate MakeCandidate(Entity entity, float3 position)
        {
            return new ProjectileHitCandidate { entity = entity, position = position };
        }

        private static bool Sweep(
            float3 previousPosition, float3 currentPosition, float projectileRadius,
            ProjectileHitCandidate[] candidates, out int hitIndex, out float hitFraction)
        {
            NativeArray<ProjectileHitCandidate> candidateArray =
                new NativeArray<ProjectileHitCandidate>(candidates, Allocator.Temp);
            try
            {
                return ProjectileHitUtils.FindFirstHitAlongSegment(
                    previousPosition, currentPosition, projectileRadius,
                    ShooterEntity, float3.zero, candidateArray,
                    out hitIndex, out hitFraction);
            }
            finally
            {
                candidateArray.Dispose();
            }
        }

        [Test]
        public void FirstCandidateAlongPathWins_NotTheNearestToEndPoint()
        {
            ProjectileHitCandidate[] candidates =
            {
                MakeCandidate(FirstOtherEntity, new float3(0f, 0f, 9.8f)),
                MakeCandidate(SecondOtherEntity, new float3(0f, 0f, 6f)),
            };

            bool didHit = Sweep(new float3(0f, 0f, 5f), new float3(0f, 0f, 10f), 0.05f, candidates, out int hitIndex, out float _);

            Assert.IsTrue(didHit);
            Assert.AreEqual(1, hitIndex);
        }

        [Test]
        public void ShooterIsNeverHit()
        {
            ProjectileHitCandidate shooterOnPath = MakeCandidate(ShooterEntity, new float3(0f, 0f, 6f));

            bool hitWithShooterOnly = Sweep(
                new float3(0f, 0f, 5f), new float3(0f, 0f, 7f), 0.05f,
                new[] { shooterOnPath }, out int _, out float _);
            Assert.IsFalse(hitWithShooterOnly);

            ProjectileHitCandidate otherOnPath = MakeCandidate(FirstOtherEntity, new float3(0f, 0f, 6.5f));
            bool hitWithOtherAdded = Sweep(
                new float3(0f, 0f, 5f), new float3(0f, 0f, 7f), 0.05f,
                new[] { shooterOnPath, otherOnPath }, out int hitIndex, out float _);
            Assert.IsTrue(hitWithOtherAdded);
            Assert.AreEqual(1, hitIndex);
        }

        [Test]
        public void NothingHitsInsideClearOriginDistance()
        {
            ProjectileHitCandidate[] candidates = { MakeCandidate(FirstOtherEntity, new float3(0f, 0f, 0.8f)) };

            bool hitInsideClearDistance = Sweep(
                new float3(0f, 0f, 0f), new float3(0f, 0f, 1.0f), 0.2f, candidates, out int _, out float _);
            Assert.IsFalse(hitInsideClearDistance);

            bool hitAfterClearing = Sweep(
                new float3(0f, 0f, 1.0f), new float3(0f, 0f, 1.6f), 0.2f, candidates, out int _, out float hitFraction);
            Assert.IsTrue(hitAfterClearing);
            Assert.Greater(hitFraction, 0f);
        }

        [Test]
        public void FastBulletCannotTunnelThroughUnit()
        {
            ProjectileHitCandidate[] candidates = { MakeCandidate(FirstOtherEntity, new float3(0f, 0f, 10f)) };

            bool hitWithHalfMeterStep = Sweep(
                new float3(0f, 0f, 9.75f), new float3(0f, 0f, 10.25f), 0.05f, candidates, out int _, out float _);
            Assert.IsTrue(hitWithHalfMeterStep);

            bool hitWithOneMeterStep = Sweep(
                new float3(0f, 0f, 9.5f), new float3(0f, 0f, 10.5f), 0.05f, candidates, out int _, out float _);
            Assert.IsTrue(hitWithOneMeterStep);
        }

        [Test]
        public void MissesWhenPathPassesWide()
        {
            ProjectileHitCandidate[] candidates = { MakeCandidate(FirstOtherEntity, new float3(1.0f, 0f, 7f)) };

            bool didHit = Sweep(new float3(0f, 0f, 5f), new float3(0f, 0f, 10f), 0.05f, candidates, out int _, out float _);

            Assert.IsFalse(didHit);
        }

        [Test]
        public void IgnoresHeight()
        {
            ProjectileHitCandidate[] candidates = { MakeCandidate(FirstOtherEntity, new float3(0f, 5f, 7f)) };

            bool didHit = Sweep(new float3(0f, 0f, 5f), new float3(0f, 0f, 10f), 0.05f, candidates, out int _, out float _);

            Assert.IsTrue(didHit);
        }
    }
}
