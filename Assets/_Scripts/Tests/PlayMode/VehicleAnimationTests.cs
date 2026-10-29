using DotsAnimationToolkit;
using NUnit.Framework;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class VehicleAnimationTests
    {
        private const uint OpenKey = 11;
        private const uint CloseKey = 12;
        private const uint SlamKey = 13;

        private World testWorld;
        private EntityManager entityManager;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("VehicleAnimationTests");
            entityManager = testWorld.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        // A clip plays once on entering Opening/Closing/BurstOpen; holding a phase or settling into Open sends nothing,
        // or the clip would restart every frame and never reach its end pose.
        [Test]
        public void DoorPhaseChanges_PlayEachClipOnce_OnTheOwnersActor()
        {
            Entity actor = entityManager.CreateEntity(typeof(AnimationCommand), typeof(AnimationCommandPending));
            entityManager.SetComponentEnabled<AnimationCommandPending>(actor, false);
            Entity door = entityManager.CreateEntity(typeof(Door), typeof(DoorAnimationKeys), typeof(DoorOwner));
            entityManager.SetComponentData(door, new DoorOwner { owner = actor });
            entityManager.SetComponentData(door, new DoorAnimationKeys { open = OpenKey, close = CloseKey, slam = SlamKey, lastAnimatedPhase = DoorPhase.Closed });

            SetPhaseAndUpdate(door, DoorPhase.Opening);
            SetPhaseAndUpdate(door, DoorPhase.Opening);
            SetPhaseAndUpdate(door, DoorPhase.Open);
            SetPhaseAndUpdate(door, DoorPhase.Closing);
            SetPhaseAndUpdate(door, DoorPhase.BurstOpen);

            DynamicBuffer<AnimationCommand> commands = entityManager.GetBuffer<AnimationCommand>(actor);
            Assert.AreEqual(3, commands.Length);
            Assert.AreEqual(OpenKey, commands[0].animationKey);
            Assert.AreEqual(CloseKey, commands[1].animationKey);
            Assert.AreEqual(SlamKey, commands[2].animationKey);
            Assert.IsTrue(entityManager.IsComponentEnabled<AnimationCommandPending>(actor));
        }

        private void SetPhaseAndUpdate(Entity door, DoorPhase phase)
        {
            entityManager.SetComponentData(door, new Door { phase = phase });
            SystemHandle handle = testWorld.GetOrCreateSystem<VehicleAnimationSystem>();
            handle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();
        }
    }
}
