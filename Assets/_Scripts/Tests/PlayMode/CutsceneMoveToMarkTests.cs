using DotsAnimationToolkit;
using DotsMovementToolkit;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace StitchPunk.Tests.PlayMode
{
    // G2-P1: the two halves of CutsceneMoveToMarkSystem — an order becomes a path request for a unit
    // and never for the player, and a resolved order stops the walk it started.
    public sealed class CutsceneMoveToMarkTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneMoveToMarkTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated)
            {
                testWorld.Dispose();
            }
            testWorld = null;
        }

        [Test]
        public void MoveToMark_IssuesAPathRequestForUnitsButNotThePlayer()
        {
            EntityManager entityManager = testWorld.EntityManager;
            float3 markPosition = new float3(12f, 0f, -4f);

            Entity unitEntity = CreateWalker(entityManager, new float3(0f, 0f, 0f));
            OrderToMark(entityManager, unitEntity, markPosition, toleranceMeters: 0.8f);

            Entity playerEntity = CreateWalker(entityManager, new float3(1f, 0f, 1f));
            entityManager.AddComponentData(playerEntity, new Player { interactableEntity = Entity.Null });
            OrderToMark(entityManager, playerEntity, markPosition, toleranceMeters: 0.8f);

            RunSystem<CutsceneMoveToMarkSystem>();

            Assert.IsTrue(entityManager.IsComponentEnabled<PathRequest>(unitEntity),
                "A unit under a cutscene mark must be pathed to it.");
            Assert.AreEqual(markPosition, entityManager.GetComponentData<PathRequest>(unitEntity).targetPosition);
            Assert.AreEqual(0.4f, entityManager.GetComponentData<PathRequest>(unitEntity).stoppingDistance, 1e-5f,
                "The path stops at half the mark's tolerance so arrival is never decided on the margin.");
            Assert.IsTrue(entityManager.IsComponentEnabled<CutsceneMarkIssued>(unitEntity),
                "The order must be marked as issued so the next frame does not re-issue it.");

            Assert.IsFalse(entityManager.IsComponentEnabled<PathRequest>(playerEntity),
                "The player is never pathed — they walk to their own mark on their own input (G2 §4).");
            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneMarkIssued>(playerEntity));
        }

        [Test]
        public void MarkResolved_HaltsPathing()
        {
            EntityManager entityManager = testWorld.EntityManager;
            float3 arrivalPosition = new float3(12f, 0f, -4f);

            Entity unitEntity = CreateWalker(entityManager, arrivalPosition);
            OrderToMark(entityManager, unitEntity, arrivalPosition, toleranceMeters: 0.8f);

            // The state the toolkit leaves behind when it judges the mark reached: the order is
            // disabled in place, and the walk this system started is still running.
            RunSystem<CutsceneMoveToMarkSystem>();
            entityManager.SetComponentEnabled<CutsceneMoveToMark>(unitEntity, false);

            RunSystem<CutsceneMoveToMarkSystem>();

            Assert.AreEqual(PathfindingMode.Stop,
                entityManager.GetComponentData<PathRequest>(unitEntity).requestedMode,
                "A resolved mark must halt the pathing it started.");
            Assert.AreEqual(arrivalPosition, entityManager.GetComponentData<Movement>(unitEntity).targetPosition,
                "The unit stops where it stands rather than drifting on toward a stale target.");
            Assert.IsFalse(entityManager.IsComponentEnabled<CutsceneMarkIssued>(unitEntity),
                "Clearing the flag is what lets the same unit take a second mark later in the cutscene.");
        }

        private static Entity CreateWalker(EntityManager entityManager, float3 position)
        {
            Entity walkerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(walkerEntity, new LocalTransform
            {
                Position = position,
                Rotation = quaternion.identity,
                Scale    = 1f,
            });
            entityManager.AddComponentData(walkerEntity, new Movement { targetPosition = new float3(99f, 0f, 99f) });
            entityManager.AddComponentData(walkerEntity, new PathRequest { requestedMode = PathfindingMode.DStarLite });
            entityManager.SetComponentEnabled<PathRequest>(walkerEntity, false);
            entityManager.AddComponent<CutsceneMarkIssued>(walkerEntity);
            entityManager.SetComponentEnabled<CutsceneMarkIssued>(walkerEntity, false);
            return walkerEntity;
        }

        private static void OrderToMark(
            EntityManager entityManager, Entity walkerEntity, float3 position, float toleranceMeters)
        {
            entityManager.AddComponentData(walkerEntity, new CutsceneMoveToMark
            {
                position        = position,
                toleranceMeters = toleranceMeters,
                timeoutSeconds  = 0f,
            });
            entityManager.SetComponentEnabled<CutsceneMoveToMark>(walkerEntity, true);
        }

        private void RunSystem<TSystem>() where TSystem : unmanaged, ISystem
        {
            SystemHandle systemHandle = testWorld.GetOrCreateSystem<TSystem>();
            systemHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }
    }
}
