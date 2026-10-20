using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // Brawls are drunk flavour and must never kill (LH-D14): same-faction hits from a Brawl-acting
    // source cannot take the target below ceil(0.35 * healthAmountMax).
    public sealed class BrawlNonLethalTests
    {
        private World testWorld;
        private DamageBus damageBus;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("BrawlNonLethalTests");
            EntityManager entityManager = testWorld.EntityManager;

            Entity sceneTagEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(sceneTagEntity);

            // Creating this registers the ECB singleton DamageEventSystem fetches inside Burst.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

            damageBus = new DamageBus
            {
                raw      = new NativeQueue<DamageEvent>(Allocator.Persistent),
                resolved = new NativeQueue<DamageEvent>(Allocator.Persistent),
            };
            Entity busEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(busEntity, damageBus);
        }

        [TearDown]
        public void TearDown()
        {
            if (testWorld != null && testWorld.IsCreated) testWorld.Dispose();
            testWorld = null;

            if (damageBus.raw.IsCreated) damageBus.raw.Dispose();
            if (damageBus.resolved.IsCreated) damageBus.resolved.Dispose();
        }

        private Entity CreateAttacker(EntityManager entityManager, ActionType action)
        {
            Entity attackerEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(attackerEntity, new Faction { factionType = FactionType.Citizens });
            entityManager.AddComponentData(attackerEntity, new StateMachine { action = action });
            return attackerEntity;
        }

        private Entity CreateTarget(EntityManager entityManager, int currentHealth)
        {
            Entity targetEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(targetEntity, new Health
            {
                healthAmount    = currentHealth,
                healthAmountMax = 100,
            });
            entityManager.AddComponentData(targetEntity, new Faction { factionType = FactionType.Citizens });
            entityManager.AddComponent<Dead>(targetEntity);
            entityManager.SetComponentEnabled<Dead>(targetEntity, false);
            return targetEntity;
        }

        private void DeliverHit(Entity attackerEntity, Entity targetEntity, int damageAmount)
        {
            damageBus.resolved.Enqueue(new DamageEvent
            {
                targetEntity = targetEntity,
                sourceEntity = attackerEntity,
                damageAmount = damageAmount,
                damageSource = default,
            });

            testWorld.GetOrCreateSystem<DamageEventSystem>().Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        // Revert-to-fail: remove the ClampBrawlDamageToFloor call and this goes to 0 / Dead.
        [Test]
        public void RepeatedBrawlHitsStopAtThirtyFivePercent()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity attackerEntity = CreateAttacker(entityManager, ActionType.Brawl);
            Entity targetEntity = CreateTarget(entityManager, 100);

            for (int hitIndex = 0; hitIndex < 10; hitIndex++)
                DeliverHit(attackerEntity, targetEntity, 20);

            Assert.AreEqual(35, entityManager.GetComponentData<Health>(targetEntity).healthAmount,
                "Brawl damage stops exactly at ceil(0.35 * max).");
            Assert.IsFalse(entityManager.IsComponentEnabled<Dead>(targetEntity),
                "A brawl must never kill.");
        }

        [Test]
        public void BrawlHitBelowFloorDealsNothing()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity attackerEntity = CreateAttacker(entityManager, ActionType.Brawl);
            Entity targetEntity = CreateTarget(entityManager, 30);

            DeliverHit(attackerEntity, targetEntity, 20);

            Assert.AreEqual(30, entityManager.GetComponentData<Health>(targetEntity).healthAmount,
                "A target already under the floor takes no further brawl damage.");
            Assert.IsFalse(entityManager.IsComponentEnabled<Dead>(targetEntity));
        }

        [Test]
        public void NonBrawlHitStillKills()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity attackerEntity = CreateAttacker(entityManager, ActionType.Idle);
            Entity targetEntity = CreateTarget(entityManager, 30);

            DeliverHit(attackerEntity, targetEntity, 40);

            Assert.LessOrEqual(entityManager.GetComponentData<Health>(targetEntity).healthAmount, 0,
                "Only the Brawl action is clamped; any other same-faction hit is lethal.");
            Assert.IsTrue(entityManager.IsComponentEnabled<Dead>(targetEntity));
        }
    }
}
