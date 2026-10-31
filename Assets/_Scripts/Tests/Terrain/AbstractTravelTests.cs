using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.Tests
{
    [TestFixture]
    public sealed class AbstractTravelTests
    {
        private const float Tolerance = 0.001f;

        private World testWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("AbstractTravelTests");
            entityManager = testWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            testWorld.Dispose();
        }

        private Entity CreateRouteEntity(float3[] positions, float speed, AbstractRouteEnd endBehaviour)
        {
            Entity entity = entityManager.CreateEntity(typeof(AbstractRoute), typeof(AbstractRouteWaypoint), typeof(AbstractSimulation));
            DynamicBuffer<AbstractRouteWaypoint> buffer = entityManager.GetBuffer<AbstractRouteWaypoint>(entity);
            for (int index = 0; index < positions.Length; index++)
            {
                buffer.Add(new AbstractRouteWaypoint { position = positions[index] });
            }

            float total = AbstractTravel.BuildCumulativeLengths(ref buffer);
            entityManager.SetComponentData(entity, new AbstractRoute
            {
                departureWorldSeconds = 0.0,
                speedMetresPerSecond = speed,
                totalLengthMetres = total,
                endBehaviour = endBehaviour
            });
            return entity;
        }

        private void Evaluate(Entity entity, double now, out float3 position, out float3 forward, out bool isFinished)
        {
            DynamicBuffer<AbstractRouteWaypoint> buffer = entityManager.GetBuffer<AbstractRouteWaypoint>(entity);
            AbstractRoute route = entityManager.GetComponentData<AbstractRoute>(entity);
            AbstractTravel.PointAlongRoute(in buffer, in route, now, out position, out forward, out isFinished);
        }

        private static float3[] StraightHundredMetres()
        {
            return new float3[] { new float3(0f, 0f, 0f), new float3(100f, 0f, 0f) };
        }

        private static void AssertPoint(float3 expected, float3 actual)
        {
            Assert.AreEqual(expected.x, actual.x, Tolerance);
            Assert.AreEqual(expected.y, actual.y, Tolerance);
            Assert.AreEqual(expected.z, actual.z, Tolerance);
        }

        [Test]
        public void BuildCumulativeLengthsWritesRunningSumAndReturnsTotal()
        {
            Entity entity = CreateRouteEntity(
                new float3[] { new float3(0f, 0f, 0f), new float3(3f, 4f, 0f), new float3(3f, 4f, 10f) }, 1f, AbstractRouteEnd.Stop);
            DynamicBuffer<AbstractRouteWaypoint> buffer = entityManager.GetBuffer<AbstractRouteWaypoint>(entity);

            float total = AbstractTravel.BuildCumulativeLengths(ref buffer);

            Assert.AreEqual(15f, total, Tolerance);
            Assert.AreEqual(0f, buffer[0].cumulativeLengthMetres, Tolerance);
            Assert.AreEqual(5f, buffer[1].cumulativeLengthMetres, Tolerance);
            Assert.AreEqual(15f, buffer[2].cumulativeLengthMetres, Tolerance);
        }

        [Test]
        public void ThreePointRouteResolvesSegmentsAtStartMiddleWaypointAndBetween()
        {
            Entity entity = CreateRouteEntity(
                new float3[] { new float3(0f, 0f, 0f), new float3(10f, 0f, 0f), new float3(10f, 0f, 20f) }, 1f, AbstractRouteEnd.Stop);

            Evaluate(entity, 0.0, out float3 startPosition, out float3 startForward, out bool startFinished);
            AssertPoint(new float3(0f, 0f, 0f), startPosition);
            AssertPoint(new float3(1f, 0f, 0f), startForward);
            Assert.IsFalse(startFinished);

            Evaluate(entity, 10.0, out float3 middlePosition, out float3 middleForward, out bool middleFinished);
            AssertPoint(new float3(10f, 0f, 0f), middlePosition);
            AssertPoint(new float3(0f, 0f, 1f), middleForward);
            Assert.IsFalse(middleFinished);

            Evaluate(entity, 20.0, out float3 betweenPosition, out float3 betweenForward, out bool betweenFinished);
            AssertPoint(new float3(10f, 0f, 10f), betweenPosition);
            AssertPoint(new float3(0f, 0f, 1f), betweenForward);
            Assert.IsFalse(betweenFinished);
        }

        [Test]
        public void StopClampsAtTheEndAndFinishesAndHoldsTheStartBeforeDeparture()
        {
            Entity entity = CreateRouteEntity(StraightHundredMetres(), 10f, AbstractRouteEnd.Stop);

            Evaluate(entity, 50.0, out float3 endPosition, out float3 endForward, out bool endFinished);
            AssertPoint(new float3(100f, 0f, 0f), endPosition);
            AssertPoint(new float3(1f, 0f, 0f), endForward);
            Assert.IsTrue(endFinished);

            Evaluate(entity, -5.0, out float3 earlyPosition, out float3 earlyForward, out bool earlyFinished);
            AssertPoint(new float3(0f, 0f, 0f), earlyPosition);
            Assert.IsFalse(earlyFinished);
        }

        [Test]
        public void LoopAtTwoAndAHalfLapsLandsAtTheHalfLapPoint()
        {
            Entity entity = CreateRouteEntity(StraightHundredMetres(), 10f, AbstractRouteEnd.Loop);

            Evaluate(entity, 25.0, out float3 position, out float3 forward, out bool isFinished);

            AssertPoint(new float3(50f, 0f, 0f), position);
            AssertPoint(new float3(1f, 0f, 0f), forward);
            Assert.IsFalse(isFinished);
        }

        [Test]
        public void PingPongAtTwoAndAHalfLapsHeadsForwardAndAtOneAndAHalfHeadsBackward()
        {
            Entity entity = CreateRouteEntity(StraightHundredMetres(), 10f, AbstractRouteEnd.PingPong);

            Evaluate(entity, 25.0, out float3 forwardLegPosition, out float3 forwardLegForward, out bool forwardLegFinished);
            AssertPoint(new float3(50f, 0f, 0f), forwardLegPosition);
            AssertPoint(new float3(1f, 0f, 0f), forwardLegForward);
            Assert.IsFalse(forwardLegFinished);

            Evaluate(entity, 15.0, out float3 returnLegPosition, out float3 returnLegForward, out bool returnLegFinished);
            AssertPoint(new float3(50f, 0f, 0f), returnLegPosition);
            AssertPoint(new float3(-1f, 0f, 0f), returnLegForward);
            Assert.IsFalse(returnLegFinished);
        }

        [Test]
        public void ZeroLengthRouteReturnsFirstPointAndFinishesUnderStop()
        {
            Entity entity = CreateRouteEntity(
                new float3[] { new float3(7f, 0f, 3f), new float3(7f, 0f, 3f) }, 10f, AbstractRouteEnd.Stop);

            Evaluate(entity, 100.0, out float3 position, out float3 forward, out bool isFinished);

            AssertPoint(new float3(7f, 0f, 3f), position);
            AssertPoint(new float3(0f, 0f, 1f), forward);
            Assert.IsTrue(isFinished);
        }

        [Test]
        public void SkippedDayOnALoopEqualsTheModuloPoint()
        {
            Entity entity = CreateRouteEntity(
                new float3[] { new float3(0f, 0f, 0f), new float3(1000f, 0f, 0f) }, 1f, AbstractRouteEnd.Loop);

            Evaluate(entity, 86400.0, out float3 position, out float3 forward, out bool isFinished);

            AssertPoint(new float3(400f, 0f, 0f), position);
            Assert.IsFalse(isFinished);
        }

        [Test]
        public void BeginRouteFillsBufferAndRouteAndEnablesAbstractSimulation()
        {
            Entity entity = entityManager.CreateEntity(typeof(AbstractRoute), typeof(AbstractRouteWaypoint), typeof(AbstractSimulation));
            entityManager.SetComponentEnabled<AbstractSimulation>(entity, false);
            NativeArray<float3> positions = new NativeArray<float3>(3, Allocator.Temp);
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(10f, 0f, 0f);
            positions[2] = new float3(10f, 0f, 20f);

            EntityCommandBuffer commandBuffer = new EntityCommandBuffer(Allocator.Temp);
            AbstractTravel.BeginRoute(commandBuffer, entity, in positions, 2f, 123.0, AbstractRouteEnd.PingPong);
            commandBuffer.Playback(entityManager);
            commandBuffer.Dispose();
            positions.Dispose();

            AbstractRoute route = entityManager.GetComponentData<AbstractRoute>(entity);
            Assert.AreEqual(30f, route.totalLengthMetres, Tolerance);
            Assert.AreEqual(2f, route.speedMetresPerSecond, Tolerance);
            Assert.AreEqual(123.0, route.departureWorldSeconds);
            Assert.AreEqual(AbstractRouteEnd.PingPong, route.endBehaviour);

            DynamicBuffer<AbstractRouteWaypoint> buffer = entityManager.GetBuffer<AbstractRouteWaypoint>(entity);
            Assert.AreEqual(3, buffer.Length);
            Assert.AreEqual(0f, buffer[0].cumulativeLengthMetres, Tolerance);
            Assert.AreEqual(10f, buffer[1].cumulativeLengthMetres, Tolerance);
            Assert.AreEqual(30f, buffer[2].cumulativeLengthMetres, Tolerance);
            Assert.IsTrue(entityManager.IsComponentEnabled<AbstractSimulation>(entity));
        }
    }
}
