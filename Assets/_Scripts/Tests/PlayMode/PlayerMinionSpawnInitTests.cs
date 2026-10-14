using NUnit.Framework;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class PlayerMinionSpawnInitTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("PlayerMinionSpawnInitTests");
            testWorld.EntityManager.CreateEntity(typeof(GameSceneTag));
            testWorld.SetTime(new TimeData(1.0, 0.016f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void SpawnAsPlayerMinion_EnablesMinionBitsDisablesUtilityBrainAndRemovesTag()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateNewlySpawnedUnit(true);

            RunInitSystems();

            Assert.IsTrue(entityManager.IsComponentEnabled<Minion>(unit));
            Assert.IsTrue(entityManager.IsComponentEnabled<Undead>(unit));
            Assert.IsTrue(entityManager.IsComponentEnabled<PlayerUnitBrain>(unit));
            Assert.IsFalse(entityManager.IsComponentEnabled<UtilityBrain>(unit));
            Assert.IsFalse(entityManager.HasComponent<SpawnAsPlayerMinion>(unit));
        }

        [Test]
        public void WithoutSpawnAsPlayerMinion_MinionStaysDisabled()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = CreateNewlySpawnedUnit(false);

            RunInitSystems();

            Assert.IsFalse(entityManager.IsComponentEnabled<Minion>(unit));
            Assert.IsFalse(entityManager.IsComponentEnabled<PlayerUnitBrain>(unit));
            Assert.IsTrue(entityManager.IsComponentEnabled<UtilityBrain>(unit));
        }

        private void RunInitSystems()
        {
            SystemHandle stateInitHandle = testWorld.GetOrCreateSystem<SpawnStateInitSystem>();
            stateInitHandle.Update(testWorld.Unmanaged);
            SystemHandle playerMinionInitHandle = testWorld.GetOrCreateSystem<PlayerMinionSpawnInitSystem>();
            playerMinionInitHandle.Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        private Entity CreateNewlySpawnedUnit(bool asPlayerMinion)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity unit = entityManager.CreateEntity(
                typeof(NewlySpawned), typeof(Minion), typeof(Undead), typeof(PlayerUnitBrain), typeof(UtilityBrain));
            if (asPlayerMinion) entityManager.AddComponent<SpawnAsPlayerMinion>(unit);
            entityManager.SetComponentEnabled<NewlySpawned>(unit, true);
            entityManager.SetComponentEnabled<Minion>(unit, false);
            entityManager.SetComponentEnabled<Undead>(unit, false);
            entityManager.SetComponentEnabled<PlayerUnitBrain>(unit, false);
            entityManager.SetComponentEnabled<UtilityBrain>(unit, true);
            return unit;
        }
    }
}
