using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    public sealed class DespawnSystemTests
    {
        // Mirrors DespawnSystem's private PoolCapPerType const so this fixture can assert the trim boundary.
        private const int PoolCapPerType = 64;

        private World testWorld;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("DespawnSystemTests");
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
        public void Lifetime_ReachingZero_EnablesDespawn_ThenEntityIsDestroyed()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity lifetimeEntity = entityManager.CreateEntity(typeof(Lifetime), typeof(Despawn));
            entityManager.SetComponentData(lifetimeEntity, new Lifetime { secondsRemaining = 0.5f });
            entityManager.SetComponentData(lifetimeEntity, new Despawn { mode = DespawnMode.Auto });
            // Despawn starts disabled to pin that LifetimeSystem's job still visits entities whose Despawn is off.
            entityManager.SetComponentEnabled<Despawn>(lifetimeEntity, false);

            testWorld.SetTime(new TimeData(0d, 1f));

            SystemHandle lifetimeSystemHandle = testWorld.GetOrCreateSystem<LifetimeSystem>();
            lifetimeSystemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.IsTrue(entityManager.IsComponentEnabled<Despawn>(lifetimeEntity), "LifetimeSystem should enable Despawn once secondsRemaining reaches zero.");
            Assert.IsFalse(entityManager.IsComponentEnabled<Lifetime>(lifetimeEntity), "LifetimeSystem should disable Lifetime once it has fired Despawn.");

            SystemHandle despawnSystemHandle = testWorld.GetOrCreateSystem<DespawnSystem>();
            despawnSystemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.IsFalse(entityManager.Exists(lifetimeEntity), "DespawnSystem should destroy a non-pooled entity whose Despawn is enabled.");
        }

        [Test]
        public void PooledEntity_WithDespawn_GainsDisabled_AndDespawnIsReDisabled()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity pooledEntity = entityManager.CreateEntity(typeof(PoolOwner), typeof(Despawn));
            entityManager.SetComponentData(pooledEntity, new PoolOwner { unitType = UnitType.MaleCitizen });
            entityManager.SetComponentData(pooledEntity, new Despawn { mode = DespawnMode.Auto });
            entityManager.SetComponentEnabled<Despawn>(pooledEntity, true);

            SystemHandle despawnSystemHandle = testWorld.GetOrCreateSystem<DespawnSystem>();
            despawnSystemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            Assert.IsTrue(entityManager.Exists(pooledEntity), "DespawnSystem should return a pooled entity to the pool rather than destroying it.");
            Assert.IsTrue(entityManager.HasComponent<Disabled>(pooledEntity), "A pooled entity should gain Disabled when despawned back into its pool.");
            Assert.IsFalse(entityManager.IsComponentEnabled<Despawn>(pooledEntity), "DespawnSystem should re-disable Despawn after pooling the entity.");
        }

        [Test]
        public void PooledEntities_OverCap_AreDestroyed()
        {
            EntityManager entityManager = testWorld.EntityManager;

            for (int entityIndex = 0; entityIndex < PoolCapPerType + 3; entityIndex++)
            {
                Entity dormantEntity = entityManager.CreateEntity(typeof(PoolOwner));
                entityManager.SetComponentData(dormantEntity, new PoolOwner { unitType = UnitType.MaleCitizen });
                entityManager.AddComponent<Disabled>(dormantEntity);
            }

            SystemHandle despawnSystemHandle = testWorld.GetOrCreateSystem<DespawnSystem>();
            despawnSystemHandle.Update(testWorld.Unmanaged);
            entityManager.CompleteAllTrackedJobs();

            EntityQuery dormantPoolQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PoolOwner, Disabled>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                .Build(entityManager);

            Assert.AreEqual(PoolCapPerType, dormantPoolQuery.CalculateEntityCount(), "DespawnSystem should trim dormant pooled entities per UnitType down to the pool cap.");

            dormantPoolQuery.Dispose();
        }
    }
}
