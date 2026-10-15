using NUnit.Framework;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class BanditCampSystemTests
    {
        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("BanditCampSystemTests");
            testWorld.SetTime(new TimeData(10.0, 0.5f));
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;
        }

        [Test]
        public void CampWithLivingGuards_CountsThemAndStaysLocked()
        {
            Entity camp = CreateCamp();
            CreateGuard(camp);
            CreateGuard(camp);
            Entity loot = CreateLockedLoot(camp);

            RunCampSystem();

            BanditCamp campData = testWorld.EntityManager.GetComponentData<BanditCamp>(camp);
            Assert.AreEqual(2, campData.aliveGuards);
            Assert.IsFalse(campData.cleared);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CampLootLocked>(loot));
        }

        [Test]
        public void AllGuardsDead_ClearsCampAndUnlocksItsLoot()
        {
            Entity camp = CreateCamp();
            Entity guardA = CreateGuard(camp);
            Entity guardB = CreateGuard(camp);
            Entity loot = CreateLockedLoot(camp);
            RunCampSystem();

            testWorld.EntityManager.SetComponentEnabled<Dead>(guardA, true);
            testWorld.EntityManager.SetComponentEnabled<Dead>(guardB, true);
            RunCampSystem();

            BanditCamp campData = testWorld.EntityManager.GetComponentData<BanditCamp>(camp);
            Assert.IsTrue(campData.cleared);
            Assert.AreEqual(0, campData.aliveGuards);
            Assert.IsFalse(testWorld.EntityManager.HasComponent<CampLootLocked>(loot));
        }

        [Test]
        public void CampThatNeverHadAGuard_IsNotCleared()
        {
            Entity camp = CreateCamp();
            Entity loot = CreateLockedLoot(camp);

            RunCampSystem();
            RunCampSystem();

            Assert.IsFalse(testWorld.EntityManager.GetComponentData<BanditCamp>(camp).cleared);
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CampLootLocked>(loot));
        }

        [Test]
        public void ClearingOneCamp_LeavesLootLockedToAnotherCampLocked()
        {
            Entity clearedCamp = CreateCamp();
            Entity otherCamp = CreateCamp();
            Entity guard = CreateGuard(clearedCamp);
            CreateGuard(otherCamp);
            Entity ownLoot = CreateLockedLoot(clearedCamp);
            Entity otherLoot = CreateLockedLoot(otherCamp);
            RunCampSystem();

            testWorld.EntityManager.SetComponentEnabled<Dead>(guard, true);
            RunCampSystem();

            Assert.IsFalse(testWorld.EntityManager.HasComponent<CampLootLocked>(ownLoot));
            Assert.IsTrue(testWorld.EntityManager.HasComponent<CampLootLocked>(otherLoot));
            Assert.IsFalse(testWorld.EntityManager.GetComponentData<BanditCamp>(otherCamp).cleared);
        }

        private void RunCampSystem()
        {
            SystemHandle handle = testWorld.GetOrCreateSystem<BanditCampSystem>();
            handle.Update(testWorld.Unmanaged);
        }

        private Entity CreateCamp()
        {
            return testWorld.EntityManager.CreateEntity(typeof(BanditCamp));
        }

        private Entity CreateGuard(Entity camp)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity guard = entityManager.CreateEntity(typeof(GuardPost), typeof(Dead));
            entityManager.SetComponentData(guard, new GuardPost { camp = camp, anchorPosition = float3.zero, idleRadius = 10f, leashRadius = 25f });
            entityManager.SetComponentEnabled<Dead>(guard, false);
            return guard;
        }

        private Entity CreateLockedLoot(Entity camp)
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity loot = entityManager.CreateEntity(typeof(CampLootLocked));
            entityManager.SetComponentData(loot, new CampLootLocked { camp = camp });
            return loot;
        }
    }
}
