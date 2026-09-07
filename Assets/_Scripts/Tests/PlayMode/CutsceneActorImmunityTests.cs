using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace StitchPunk.Tests.PlayMode
{
    // A cutscene puppets an actor by taking its brain away — CutsceneActor gates every awareness
    // system, WinnerSelection and MinionActionSelection — so a bound NPC can neither flee nor fight
    // back. Before 2026-09-07 it was still fully damageable, and a hostile that wandered past during
    // the G3 acceptance run simply killed both bound actors mid-scene; the cutscene then released two
    // corpses, which reads downstream as "the NPC can't walk any more" because DeathSystem disables
    // Movement and UnitMoverJob takes `ref Movement`. DamageEventSystem is the single chokepoint every
    // producer (attacks, thrown items, hazards, AOE) funnels through, so the guard lives there.
    public sealed class CutsceneActorImmunityTests
    {
        private World testWorld;
        private DamageBus damageBus;

        [SetUp]
        public void SetUp()
        {
            testWorld = new World("CutsceneActorImmunityTests");
            EntityManager entityManager = testWorld.EntityManager;

            Entity sceneTagEntity = entityManager.CreateEntity();
            entityManager.AddComponent<GameSceneTag>(sceneTagEntity);

            // DamageEventSystem logs through the end-of-frame ECB whenever no LoggingConfig turns the
            // Combat/Health categories off, and a bare test World has neither. Creating the system is
            // what registers its Singleton (EndSimulationEntityCommandBufferSystem.OnCreate ->
            // RegisterSingleton) — without it the system aborts inside Burst on GetSingleton.
            testWorld.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

            // DamageBusSystem owns these in the game; the fixture builds the singleton by hand so the
            // test drives DamageEventSystem alone rather than the whole combat pipeline.
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

        private Entity CreateVictim(EntityManager entityManager, bool isCutsceneActor)
        {
            Entity victimEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(victimEntity, new Health
            {
                healthAmount    = 100,
                healthAmountMax = 100,
            });
            entityManager.AddComponent<Dead>(victimEntity);
            entityManager.SetComponentEnabled<Dead>(victimEntity, false);
            entityManager.AddComponent<CutsceneActor>(victimEntity);
            entityManager.SetComponentEnabled<CutsceneActor>(victimEntity, isCutsceneActor);
            return victimEntity;
        }

        private void DeliverLethalHit(EntityManager entityManager, Entity victimEntity)
        {
            // Straight into `resolved`: DamageResolutionSystem's only job is expanding AOE into
            // single-target events, and this hit is already single-target.
            damageBus.resolved.Enqueue(new DamageEvent
            {
                targetEntity = victimEntity,
                sourceEntity = Entity.Null,
                damageAmount = 100,
                damageSource = default,
            });

            testWorld.GetOrCreateSystem<DamageEventSystem>().Update(testWorld.Unmanaged);
            testWorld.EntityManager.CompleteAllTrackedJobs();
        }

        [Test]
        public void DamageEventSystem_IgnoresAHit_WhileTheVictimIsAPuppetedCutsceneActor()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity victimEntity = CreateVictim(entityManager, isCutsceneActor: true);

            DeliverLethalHit(entityManager, victimEntity);

            Assert.AreEqual(100, entityManager.GetComponentData<Health>(victimEntity).healthAmount,
                "A puppeted cutscene actor must take no damage — the cutscene disabled its ability to react.");
            Assert.IsFalse(entityManager.IsComponentEnabled<Dead>(victimEntity),
                "A puppeted cutscene actor must not be killed mid-scene.");
        }

        // The other half of the guard: it is scoped to the puppeting, not a blanket immunity that
        // would outlive the cutscene and quietly make released actors invincible.
        [Test]
        public void DamageEventSystem_AppliesTheSameHit_OnceTheActorIsReleased()
        {
            EntityManager entityManager = testWorld.EntityManager;
            Entity victimEntity = CreateVictim(entityManager, isCutsceneActor: false);

            DeliverLethalHit(entityManager, victimEntity);

            Assert.AreEqual(0, entityManager.GetComponentData<Health>(victimEntity).healthAmount,
                "A released actor takes damage exactly like any other unit.");
            Assert.IsTrue(entityManager.IsComponentEnabled<Dead>(victimEntity),
                "A released actor dies to a lethal hit exactly like any other unit.");
        }
    }
}
