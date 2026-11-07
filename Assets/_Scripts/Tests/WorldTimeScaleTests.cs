using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace StitchPunk.Tests
{
    // Time scale easing must never overshoot (a clock running past 3x or below 1x breaks abstract travel pacing).
    [TestFixture]
    public sealed class WorldTimeScaleTests
    {
        private const float Tolerance = 0.01f;
        private const float StepSeconds = 0.016f;
        private const int StepCount = 400;

        [Test]
        public void ResolveTimeScaleTarget_PrefersOverrideThenRealTimeThenProfile()
        {
            Assert.AreEqual(2f, StrategicLodDriver.ResolveTimeScaleTarget(3f, 2f, true, true), Tolerance);
            Assert.AreEqual(1f, StrategicLodDriver.ResolveTimeScaleTarget(3f, 0f, true, false), Tolerance);
            Assert.AreEqual(1f, StrategicLodDriver.ResolveTimeScaleTarget(3f, 0f, false, true), Tolerance);
            Assert.AreEqual(3f, StrategicLodDriver.ResolveTimeScaleTarget(3f, 0f, false, false), Tolerance);
        }

        [Test]
        public void EaseTimeScale_RisesMonotonicallyToTargetWithoutOvershoot()
        {
            float current = 1f;
            for (int step = 0; step < StepCount; step++)
            {
                float next = WorldTimeScaleMath.EaseTimeScale(current, 3f, 4f, StepSeconds);
                Assert.GreaterOrEqual(next, current);
                Assert.LessOrEqual(next, 3f);
                current = next;
            }
            Assert.AreEqual(3f, current, Tolerance);
        }

        [Test]
        public void EaseTimeScale_FallingNeverGoesBelowTarget()
        {
            float current = 3f;
            for (int step = 0; step < StepCount; step++)
            {
                current = WorldTimeScaleMath.EaseTimeScale(current, 1f, 4f, StepSeconds);
                Assert.GreaterOrEqual(current, 1f);
            }
            Assert.AreEqual(1f, current, Tolerance);
        }

        [Test]
        public void DefaultZoomProfile_MapsTimeScaleAndDofAtTheEnds()
        {
            StrategicZoomProfile profile = StrategicZoomProfile.CreateDefault();
            try
            {
                Assert.AreEqual(1f, profile.EvaluateTimeScaleTarget(0f), Tolerance);
                Assert.AreEqual(3f, profile.EvaluateTimeScaleTarget(1f), Tolerance);
                Assert.AreEqual(0f, profile.EvaluateDofStrength(0f));
                Assert.AreEqual(0f, profile.EvaluateDofStrength(0.5f));
                Assert.AreEqual(0f, profile.EvaluateDofStrength(0.75f));
                Assert.AreEqual(1f, profile.EvaluateDofStrength(1f), Tolerance);
                Assert.AreEqual(1f, profile.EvaluateVisualLoadRadiusTiles(1f, 1), Tolerance);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AbstractTravelPosition_DependsOnlyOnWorldSeconds()
        {
            World testWorld = new World("WorldTimeScaleTests");
            try
            {
                EntityManager entityManager = testWorld.EntityManager;
                Entity entity = entityManager.CreateEntity(typeof(AbstractRoute), typeof(AbstractRouteWaypoint));
                DynamicBuffer<AbstractRouteWaypoint> buffer = entityManager.GetBuffer<AbstractRouteWaypoint>(entity);
                buffer.Add(new AbstractRouteWaypoint { position = new float3(0f, 0f, 0f) });
                buffer.Add(new AbstractRouteWaypoint { position = new float3(100f, 0f, 0f) });
                float total = AbstractTravel.BuildCumulativeLengths(ref buffer);
                AbstractRoute route = new AbstractRoute
                {
                    departureWorldSeconds = 0.0,
                    speedMetresPerSecond = 2f,
                    totalLengthMetres = total,
                    endBehaviour = AbstractRouteEnd.Stop
                };

                AbstractTravel.PointAlongRoute(in buffer, in route, 12.5, out float3 firstPosition, out float3 firstForward, out bool firstFinished);
                AbstractTravel.PointAlongRoute(in buffer, in route, 12.5, out float3 secondPosition, out float3 secondForward, out bool secondFinished);

                Assert.AreEqual(25f, firstPosition.x, Tolerance);
                Assert.IsTrue(firstPosition.Equals(secondPosition));
                Assert.IsTrue(firstForward.Equals(secondForward));
                Assert.AreEqual(firstFinished, secondFinished);
            }
            finally
            {
                testWorld.Dispose();
            }
        }
    }
}
